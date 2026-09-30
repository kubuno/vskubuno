using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kubuno.VisualStudio.Core.ProjectProperties;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.VisualStudio.Tests.ProjectProperties
{
    /// <summary>An in-memory file system for the property model (paths are compared case-insensitively, like Windows).</summary>
    internal sealed class FakeFiles : IPropertyFileReader
    {
        private readonly Dictionary<string, string> _files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public FakeFiles Add(string path, string text)
        {
            _files[Path.GetFullPath(path)] = text;
            return this;
        }

        public string Text(string path) => _files[Path.GetFullPath(path)];

        public bool Contains(string path) => _files.ContainsKey(Path.GetFullPath(path));

        /// <summary>Applies a write the way the Visual Studio layer does (each edit against the current text).</summary>
        public void Apply(PropertyWrite write)
        {
            Assert.IsTrue(write.IsValid, write.Error);
            foreach (FileTextEdit edit in write.Edits)
            {
                string current = _files.TryGetValue(Path.GetFullPath(edit.Path), out string? text) ? text : string.Empty;
                Assert.AreEqual(!_files.ContainsKey(Path.GetFullPath(edit.Path)), edit.CreatesFile, "CreatesFile");
                _files[Path.GetFullPath(edit.Path)] = edit.Edit.ApplyTo(current);
            }
        }

        public string? ReadText(string path) => _files.TryGetValue(Path.GetFullPath(path), out string? text) ? text : null;

        public bool FileExists(string path) => _files.ContainsKey(Path.GetFullPath(path));

        public IReadOnlyList<string> GetFiles(string directory, string searchPattern)
        {
            string prefix = Path.GetFullPath(directory).TrimEnd('\\') + "\\";
            string extension = searchPattern.StartsWith("*", StringComparison.Ordinal) ? searchPattern.Substring(1) : searchPattern;
            return _files.Keys
                .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && k.IndexOf('\\', prefix.Length) < 0 && k.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    [TestClass]
    public class RustManifestPropertiesTests
    {
        private const string Root = @"C:\fake\app";
        private static readonly string Manifest = Path.Combine(Root, "Cargo.toml");

        private const string Basic =
            "[package]\n" +
            "name = \"hello\"\n" +
            "version = \"0.1.0\"   # bumped by release.sh\n" +
            "edition = \"2021\"\n" +
            "\n" +
            "# features of the crate\n" +
            "[features]\n" +
            "default = [\"fancy\"]\n" +
            "fancy = []\n" +
            "gui = [\"dep:winit\"]\n" +
            "\n" +
            "[dependencies]\n" +
            "serde = { version = \"1\", features = [\"derive\"] }\n" +
            "itoa = { version = \"1\", optional = true }\n" +
            "winit = { version = \"0.30\", optional = true }\n" +
            "helper = { path = \"../helper\" }\n" +
            "\n" +
            "[dev-dependencies]\n" +
            "pretty_assertions = \"1\"\n" +
            "\n" +
            "[target.'cfg(windows)'.dependencies]\n" +
            "windows = \"0.62\"\n";

        private static RustPropertyContext Context(FakeFiles files, string configuration = "Debug", string? bin = null) =>
            new RustPropertyContext(Manifest, configuration, bin, files);

        private static FakeFiles Files(string manifest = Basic) =>
            new FakeFiles().Add(Manifest, manifest).Add(Path.Combine(Root, "src", "main.rs"), "fn main() {}\n");

        private static void Set(FakeFiles files, string name, string value, string configuration = "Debug") =>
            files.Apply(RustManifestProperties.Set(name, value, Context(files, configuration)));

        private static string Get(FakeFiles files, string name, string configuration = "Debug") =>
            RustManifestProperties.Get(name, Context(files, configuration)).Evaluated;

        /// <summary>The lines that differ between two texts (the "diff surface" of an edit).</summary>
        private static string[] ChangedLines(string before, string after)
        {
            string[] a = before.Split('\n');
            string[] b = after.Split('\n');
            return b.Except(a).ToArray();
        }

        [TestMethod]
        public void Edition_IsReadAndReplacedInPlace()
        {
            FakeFiles files = Files();
            Assert.AreEqual("2021", Get(files, "Edition"));

            Set(files, "Edition", "2024");

            Assert.AreEqual(Basic.Replace("edition = \"2021\"", "edition = \"2024\""), files.Text(Manifest));
        }

        [TestMethod]
        public void Edition_DefaultsTo2015WhenAbsent()
        {
            FakeFiles files = Files("[package]\nname = \"x\"\nversion = \"0.1.0\"\n");
            PropertyValue value = RustManifestProperties.Get("Edition", Context(files));
            Assert.AreEqual(string.Empty, value.Unevaluated);
            Assert.AreEqual("2015", value.Evaluated);
        }

        [TestMethod]
        public void Edition_RejectsUnknownValue()
        {
            PropertyWrite write = RustManifestProperties.Set("Edition", "2019", Context(Files()));
            Assert.IsFalse(write.IsValid);
        }

        [TestMethod]
        public void PackageVersion_KeepsTrailingComment()
        {
            FakeFiles files = Files();
            Set(files, "PackageVersion", "1.2.3-beta.1");
            StringAssert.Contains(files.Text(Manifest), "version = \"1.2.3-beta.1\"   # bumped by release.sh\n");
            CollectionAssert.AreEqual(new[] { "version = \"1.2.3-beta.1\"   # bumped by release.sh" }, ChangedLines(Basic, files.Text(Manifest)));
        }

        [TestMethod]
        public void InvalidValues_AreRejected_AndNothingIsWritten()
        {
            FakeFiles files = Files();
            foreach (var (name, value) in new[]
            {
                ("PackageVersion", "1.2"), ("PackageName", "1abc"), ("PackageName", "con"), ("PackageName", ""), ("RustVersion", "latest"),
                ("PackageHomepage", "ftp://example.com"), ("PackageLicense", "MIT OR"), ("PackageLicense", "(MIT"), ("ProfileCodegenUnits", "0"),
                ("ProfileOptLevel", "4"), ("RustfmtMaxWidth", "5"), ("RustfmtTabSpaces", "abc"), ("WindowsSubsystem", "gui"),
            })
            {
                PropertyWrite write = RustManifestProperties.Set(name, value, Context(files));
                Assert.IsFalse(write.IsValid, $"{name} = {value} should be rejected");
                Assert.AreEqual(0, write.Edits.Count);
            }
            Assert.AreEqual(Basic, files.Text(Manifest));
        }

        [TestMethod]
        public void RustVersion_IsInsertedAfterTheLastPackageKey()
        {
            FakeFiles files = Files();
            Set(files, "RustVersion", "1.85");
            StringAssert.Contains(files.Text(Manifest), "edition = \"2021\"\nrust-version = \"1.85\"\n\n# features of the crate");
        }

        [TestMethod]
        public void EmptyString_RemovesTheKey()
        {
            FakeFiles files = Files(Basic.Replace("edition = \"2021\"\n", "edition = \"2021\"\nhomepage = \"https://kubuno.org\"\n"));
            Set(files, "PackageHomepage", "");
            Assert.AreEqual(Basic, files.Text(Manifest));
        }

        [TestMethod]
        public void WorkspaceInheritance_IsShownWithTheInheritedValue_AndCanBeRestored()
        {
            const string member = "[package]\nname = \"member\"\nversion.workspace = true\nedition = { workspace = true }\nauthors.workspace = true\n";
            FakeFiles files = new FakeFiles()
                .Add(Manifest, member)
                .Add(@"C:\fake\Cargo.toml", "[workspace]\nmembers = [\"app\"]\n\n[workspace.package]\nversion = \"3.1.4\"\nedition = \"2024\"\nauthors = [\"Kubuno\"]\n");

            PropertyValue version = RustManifestProperties.Get("PackageVersion", Context(files));
            Assert.AreEqual(RustManifestProperties.WorkspaceInherited, version.Unevaluated);
            Assert.AreEqual("3.1.4", version.Evaluated);
            Assert.AreEqual(RustManifestProperties.WorkspaceInherited, Get(files, "Edition"));
            Assert.AreEqual("Kubuno=True", Get(files, "PackageAuthors"), "inherited list entries are read-only");

            Set(files, "PackageVersion", "4.0.0");
            StringAssert.Contains(files.Text(Manifest), "version = \"4.0.0\"\n");
            Assert.IsFalse(files.Text(Manifest).Contains("version.workspace"));

            Set(files, "PackageVersion", "{workspace=true}");
            Assert.AreEqual(RustManifestProperties.WorkspaceInherited, RustManifestProperties.Get("PackageVersion", Context(files)).Unevaluated);
            Assert.AreEqual("3.1.4", Get(files, "PackageVersion"));

            // Setting the inherited list back unchanged keeps the inheritance.
            PropertyWrite unchanged = RustManifestProperties.Set("PackageAuthors", "Kubuno=True", Context(files));
            Assert.AreEqual(0, unchanged.Edits.Count);
        }

        [TestMethod]
        public void Profiles_AreWrittenToTheWorkspaceRoot_ForAMember()
        {
            const string root = "[workspace]\nmembers = [\"app\"]\n";
            FakeFiles files = new FakeFiles().Add(Manifest, "[package]\nname = \"member\"\nversion = \"0.1.0\"\n").Add(@"C:\fake\Cargo.toml", root);

            Set(files, "ProfileOptLevel", "s", "Release");

            Assert.AreEqual("[package]\nname = \"member\"\nversion = \"0.1.0\"\n", files.Text(Manifest));
            Assert.AreEqual(root + "\n[profile.release]\nopt-level = \"s\"\n", files.Text(@"C:\fake\Cargo.toml"));
            Assert.AreEqual("s", Get(files, "ProfileOptLevel", "Release"));
            Assert.AreEqual("0", Get(files, "ProfileOptLevel", "Debug"));
        }

        [TestMethod]
        public void Profiles_ReadCargoDefaults_AndSkipWritingTheDefault()
        {
            FakeFiles files = Files();
            Assert.AreEqual("full", Get(files, "ProfileDebug", "Debug"));
            Assert.AreEqual("none", Get(files, "ProfileDebug", "Release"));
            Assert.AreEqual("256", Get(files, "ProfileCodegenUnits", "Debug"));
            Assert.AreEqual("16", Get(files, "ProfileCodegenUnits", "Release"));
            Assert.AreEqual("true", Get(files, "ProfileOverflowChecks", "Debug"));
            Assert.AreEqual("false", Get(files, "ProfileIncremental", "Release"));
            Assert.AreEqual("dev", Get(files, "CargoProfileName", "Debug"));
            Assert.AreEqual("release", Get(files, "CargoProfileName", "Release"));

            PropertyWrite write = RustManifestProperties.Set("ProfileOptLevel", "3", Context(files, "Release"));
            Assert.AreEqual(0, write.Edits.Count, "Cargo's default needs no [profile.release] table");
        }

        [TestMethod]
        public void Profiles_MapEquivalentSpellings()
        {
            FakeFiles files = Files(Basic + "\n[profile.release]\ndebug = 2\nlto = true\nstrip = true\n");
            Assert.AreEqual("full", Get(files, "ProfileDebug", "Release"));
            Assert.AreEqual("fat", Get(files, "ProfileLto", "Release"));
            Assert.AreEqual("symbols", Get(files, "ProfileStrip", "Release"));

            Assert.AreEqual(0, RustManifestProperties.Set("ProfileDebug", "full", Context(files, "Release")).Edits.Count);

            Set(files, "ProfileDebug", "line-tables-only", "Release");
            Set(files, "ProfileLto", "false", "Release");
            StringAssert.Contains(files.Text(Manifest), "[profile.release]\ndebug = \"line-tables-only\"\nlto = false\nstrip = true\n");
        }

        [TestMethod]
        public void CustomProfile_InheritsItsDefaults()
        {
            FakeFiles files = Files(Basic + "\n[profile.dist]\ninherits = \"release\"\n");
            Assert.AreEqual("dist", Get(files, "CargoProfileName", "Dist"));
            Assert.AreEqual("3", Get(files, "ProfileOptLevel", "Dist"));
        }

        [TestMethod]
        public void Reset_RemovesTheKey_AndPrunesTheEmptyTable()
        {
            FakeFiles files = Files();
            Set(files, "ProfilePanic", "abort", "Release");
            StringAssert.Contains(files.Text(Manifest), "[profile.release]\npanic = \"abort\"\n");

            files.Apply(RustManifestProperties.Reset("ProfilePanic", Context(files, "Release")));

            Assert.AreEqual(Basic, files.Text(Manifest));
        }

        [TestMethod]
        public void ClippyGroup_IsWrittenWithALowerPriority_AndItsLevelUpdatedInPlace()
        {
            FakeFiles files = Files();
            Assert.AreEqual("default", Get(files, "ClippyPedantic"));

            Set(files, "ClippyPedantic", "warn");
            StringAssert.Contains(files.Text(Manifest), "[lints.clippy]\npedantic = { level = \"warn\", priority = -1 }\n");

            Set(files, "ClippyPedantic", "deny");
            StringAssert.Contains(files.Text(Manifest), "pedantic = { level = \"deny\", priority = -1 }");
            Assert.AreEqual("deny", Get(files, "ClippyPedantic"));

            Set(files, "RustLintUnsafeCode", "forbid");
            StringAssert.Contains(files.Text(Manifest), "[lints.rust]\nunsafe_code = \"forbid\"\n");

            Set(files, "ClippyPedantic", "default");
            Assert.IsFalse(files.Text(Manifest).Contains("pedantic"));
        }

        [TestMethod]
        public void Lints_InheritedFromTheWorkspace_AreReadOnly()
        {
            FakeFiles files = new FakeFiles()
                .Add(Manifest, "[package]\nname = \"m\"\nversion = \"0.1.0\"\n\n[lints]\nworkspace = true\n")
                .Add(@"C:\fake\Cargo.toml", "[workspace]\nmembers = [\"app\"]\n\n[workspace.lints.clippy]\nperf = \"deny\"\n");

            Assert.AreEqual("deny", Get(files, "ClippyPerf"));
            Assert.IsFalse(RustManifestProperties.Set("ClippyPerf", "warn", Context(files)).IsValid);
        }

        [TestMethod]
        public void Rustfmt_CreatesTheFileOnFirstChange_AndSkipsDefaults()
        {
            FakeFiles files = Files();
            string rustfmt = Path.Combine(Root, "rustfmt.toml");
            Assert.AreEqual("100", Get(files, "RustfmtMaxWidth"));
            Assert.AreEqual(0, RustManifestProperties.Set("RustfmtMaxWidth", "100", Context(files)).Edits.Count);

            Set(files, "RustfmtMaxWidth", "120");
            Set(files, "RustfmtHardTabs", "true");

            Assert.AreEqual("max_width = 120\nhard_tabs = true\n", files.Text(rustfmt));
            Assert.AreEqual("120", Get(files, "RustfmtMaxWidth"));
            Assert.AreEqual(rustfmt, Get(files, "RustfmtConfigPath"));
        }

        [TestMethod]
        public void Rustfmt_UsesAnExistingDotFile()
        {
            FakeFiles files = Files().Add(Path.Combine(Root, ".rustfmt.toml"), "# team settings\ntab_spaces = 2\n");
            Assert.AreEqual("2", Get(files, "RustfmtTabSpaces"));
            Set(files, "RustfmtTabSpaces", "4");
            Assert.AreEqual("# team settings\ntab_spaces = 4\n", files.Text(Path.Combine(Root, ".rustfmt.toml")));
        }

        [TestMethod]
        public void LicensePreset_ReflectsAndWritesTheExpression()
        {
            FakeFiles files = Files();
            Assert.AreEqual("(none)", Get(files, "PackageLicensePreset"));

            Set(files, "PackageLicensePreset", "MIT OR Apache-2.0");
            Assert.AreEqual("MIT OR Apache-2.0", Get(files, "PackageLicense"));
            Assert.AreEqual("MIT OR Apache-2.0", Get(files, "PackageLicensePreset"));

            Set(files, "PackageLicense", "(MIT OR Apache-2.0) AND GPL-2.0-or-later WITH Classpath-exception-2.0");
            Assert.AreEqual("(other)", Get(files, "PackageLicensePreset"));
            Assert.AreEqual(0, RustManifestProperties.Set("PackageLicensePreset", "(other)", Context(files)).Edits.Count);

            Set(files, "PackageLicensePreset", "(none)");
            Assert.IsFalse(files.Text(Manifest).Contains("license"));
        }

        [TestMethod]
        public void Lists_UseTheSelectorEncoding_AndAreValidated()
        {
            FakeFiles files = Files();
            Set(files, "PackageKeywords", PropertyListEncoding.EncodeStrings(new[] { "gui", "desktop" }));
            StringAssert.Contains(files.Text(Manifest), "keywords = [\"gui\", \"desktop\"]");
            Assert.AreEqual("gui=False,desktop=False", Get(files, "PackageKeywords"));

            Assert.IsFalse(RustManifestProperties.Set("PackageKeywords", PropertyListEncoding.EncodeStrings(new[] { "a", "b", "c", "d", "e", "f" }), Context(files)).IsValid, "at most 5");
            Assert.IsFalse(RustManifestProperties.Set("PackageKeywords", PropertyListEncoding.EncodeStrings(new[] { "not valid" }), Context(files)).IsValid);
            Assert.IsFalse(RustManifestProperties.Set("PackageCategories", PropertyListEncoding.EncodeStrings(new[] { "not-a-category" }), Context(files)).IsValid);

            Set(files, "PackageCategories", PropertyListEncoding.EncodeStrings(new[] { "gui", "os::windows-apis" }));
            StringAssert.Contains(files.Text(Manifest), "categories = [\"gui\", \"os::windows-apis\"]");

            Set(files, "PackageKeywords", string.Empty);
            Assert.IsFalse(files.Text(Manifest).Contains("keywords"));
        }

        [TestMethod]
        public void LibraryCrateTypes_CreateTheLibTable()
        {
            FakeFiles files = Files().Add(Path.Combine(Root, "src", "lib.rs"), "pub fn f() {}\n");
            Assert.AreEqual("true", Get(files, "HasLibraryTarget"));
            Set(files, "LibraryCrateTypes", PropertyListEncoding.EncodeStrings(new[] { "cdylib", "rlib" }));
            StringAssert.Contains(files.Text(Manifest), "[lib]\ncrate-type = [\"cdylib\", \"rlib\"]\n");
            Assert.IsFalse(RustManifestProperties.Set("LibraryCrateTypes", PropertyListEncoding.EncodeStrings(new[] { "exe" }), Context(files)).IsValid);
            Assert.AreEqual("bin: hello.exe · lib: hello (cdylib, rlib)", Get(files, "TargetKinds"));
        }

        [TestMethod]
        public void Publish_MapsFalseArraysAndDefault()
        {
            FakeFiles files = Files();
            Assert.AreEqual("true", Get(files, "PackagePublish"));
            Set(files, "PackagePublish", "false");
            StringAssert.Contains(files.Text(Manifest), "publish = false");
            Set(files, "PackagePublish", "registries");
            StringAssert.Contains(files.Text(Manifest), "publish = [\"crates-io\"]");
            Set(files, "PackagePublishRegistries", PropertyListEncoding.EncodeStrings(new[] { "kubuno" }));
            StringAssert.Contains(files.Text(Manifest), "publish = [\"kubuno\"]");
            Assert.AreEqual("registries", Get(files, "PackagePublish"));
            Set(files, "PackagePublish", "true");
            Assert.AreEqual(Basic, files.Text(Manifest));
        }

        [TestMethod]
        public void Targets_FeaturesAndDependencies_AreEnumerated()
        {
            FakeFiles files = Files()
                .Add(Path.Combine(Root, "src", "bin", "tool.rs"), "fn main() {}\n");

            CollectionAssert.AreEqual(new[] { "hello", "tool" }, RustManifestProperties.GetBinaryTargets(Context(files)).ToArray());
            CollectionAssert.AreEqual(new[] { "fancy", "gui", "itoa" }, RustManifestProperties.GetFeatures(Context(files)).ToArray(), "winit is only reachable as dep:winit");

            DependencyCounts counts = RustManifestProperties.GetDependencyCounts(Context(files));
            Assert.AreEqual(5, counts.Normal);
            Assert.AreEqual(1, counts.Dev);
            Assert.AreEqual(0, counts.Build);
            Assert.AreEqual(1, counts.Path);
        }

        [TestMethod]
        public void WindowsSubsystem_EditsTheBinarysMainFile()
        {
            FakeFiles files = Files();
            string main = Path.Combine(Root, "src", "main.rs");
            Assert.AreEqual("console", Get(files, "WindowsSubsystem"));

            Set(files, "WindowsSubsystem", "windows");
            Assert.AreEqual("#![windows_subsystem = \"windows\"]\nfn main() {}\n", files.Text(main));

            Set(files, "WindowsSubsystem", "windows-release");
            Assert.AreEqual("#![cfg_attr(not(debug_assertions), windows_subsystem = \"windows\")]\nfn main() {}\n", files.Text(main));
            Assert.AreEqual("windows-release", Get(files, "WindowsSubsystem"));
        }

        [TestMethod]
        public void MainSourcePath_FollowsBinTables()
        {
            FakeFiles files = Files(Basic + "\n[[bin]]\nname = \"other\"\npath = \"tools/other.rs\"\n")
                .Add(Path.Combine(Root, "tools", "other.rs"), "fn main() {}\n");
            Assert.AreEqual(Path.Combine(Root, "tools", "other.rs"), RustManifestProperties.GetMainSourcePath(Context(files, bin: "other")));
            Assert.AreEqual(Path.Combine(Root, "src", "main.rs"), RustManifestProperties.GetMainSourcePath(Context(files)));
        }

        [TestMethod]
        public void UnparsableManifest_IsReportedInsteadOfWritten()
        {
            FakeFiles files = Files("[package\nname = \"x\"\n");
            Assert.AreEqual(string.Empty, Get(files, "PackageName"));
            PropertyWrite write = RustManifestProperties.Set("PackageDescription", "x", Context(files));
            Assert.IsFalse(write.IsValid);
            StringAssert.Contains(write.Error, "Cargo.toml(");
        }

        [TestMethod]
        public void ProfileName_MapsConfigurations()
        {
            Assert.AreEqual("dev", RustPropertyContext.ProfileNameFor("Debug"));
            Assert.AreEqual("release", RustPropertyContext.ProfileNameFor("release"));
            Assert.AreEqual("bench-fast", RustPropertyContext.ProfileNameFor("Bench-Fast"));
        }
    }
}
