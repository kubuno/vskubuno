using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Kubuno.Views.Designer;
using Kubuno.Views.Designer.EditorFactory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Views.Tests.Designer
{
    /// <summary>
    /// The Kubuno View Designer is the default editor of <c>.kbview</c> / <c>.kbcontrol</c> files everywhere (docs/DESIGNER.md
    /// section 11, "Default editor"): the highest <c>Editors\{factory}\Extensions</c> priority wins a double-click in a
    /// <c>.rsproj</c>, an <c>.esproj</c>, Open Folder and for a loose file - verified live - as long as no CPS provider claims
    /// views and no per-user "Set as Default" overrides it. These tests pin each part of that rule.
    /// </summary>
    [TestClass]
    public sealed class DefaultEditorRegistrationTests
    {
        private const string DesignerFactoryGuid = "1dcf5c91-a51e-4de8-b066-680d6636c094";

        [TestMethod]
        public void The_designer_outranks_the_text_editor()
        {
            Assert.IsTrue(DesignerConstants.EditorExtensionPriority > DesignerConstants.TextEditorExtensionPriority,
                "The highest Extensions priority is the double-click default: the designer must stay above the text editor.");
            Assert.AreEqual(DesignerFactoryGuid, DesignerConstants.EditorFactoryGuidString.ToLowerInvariant());
        }

        [TestMethod]
        public void The_text_editor_entries_of_the_pkgdef_match_the_constant()
        {
            var pkgdef = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Views", "Kubuno.Views", "kbview-languages.pkgdef"));
            var section = Section(pkgdef, @"$RootKey$\Editors\{8B382828-6202-11d1-8870-0000F87579D2}\Extensions");
            Assert.IsNotNull(section, "kbview-languages.pkgdef must keep the Source Code (Text) Editor registered for views (Open With, kbview content type).");
            foreach (var extension in new[] { "kbview", "kbcontrol" })
            {
                Assert.AreEqual(DesignerConstants.TextEditorExtensionPriority, Dword(section!, extension), "text editor priority of ." + extension);
            }
        }

        [TestMethod]
        public void The_package_registers_the_designer_for_both_view_extensions_at_the_designer_priority()
        {
            var package = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Kubuno.VisualStudio", "KubunoPackage.cs"));
            foreach (var extension in new[] { "FileExtension", "ControlFileExtension" })
            {
                StringAssert.Contains(package,
                    "[ProvideEditorExtension(typeof(Kubuno.Views.Designer.EditorFactory.KbviewEditorFactory), Kubuno.Views.KbviewConstants." + extension + ", Kubuno.Views.Designer.DesignerConstants.EditorExtensionPriority)]");
            }

            // The Primary logical view (a double-click) must reach the factory, whose MapLogicalView sends it to the Design view.
            StringAssert.Contains(package, "[ProvideEditorLogicalView(typeof(Kubuno.Views.Designer.EditorFactory.KbviewEditorFactory), \"{7651a702-06e5-11d1-8ebd-00a0c90f26ea}\")]");
        }

        [TestMethod]
        public void The_built_pkgdef_makes_the_designer_the_default_of_both_view_extensions()
        {
            var pkgdef = new[] { "Release", "Debug" }
                .Select(c => Path.Combine(RepositoryRoot(), "src", "Kubuno.VisualStudio", "bin", c, "Kubuno.VisualStudio.pkgdef"))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (pkgdef is null)
            {
                Assert.Inconclusive("Kubuno.VisualStudio.pkgdef is not built yet (build src/Kubuno.VisualStudio first).");
                return;
            }

            var text = File.ReadAllText(pkgdef);
            var registration = Section(text, @"$RootKey$\Editors\{" + DesignerFactoryGuid + "}");
            Assert.IsNotNull(registration, "the designer's editor factory registration is missing from " + pkgdef);
            StringAssert.Contains(registration!, "\"Package\"=");

            // [ProvideEditorExtension] writes one section per attribute: gather every Extensions section of the factory.
            var extensions = string.Join("\n", Sections(text, @"$RootKey$\Editors\{" + DesignerFactoryGuid + @"}\Extensions"));
            foreach (var extension in new[] { "kbview", "kbcontrol" })
            {
                Assert.AreEqual(DesignerConstants.EditorExtensionPriority, Dword(extensions, extension), "designer priority of ." + extension + " in " + pkgdef);
            }
        }

        [TestMethod]
        public void No_project_system_provider_claims_views()
        {
            // A CPS IProjectSpecificEditorProvider marked as default would beat the user's own "Set as Default" inside its
            // project type, and differ from the other contexts: the designer is the default through its global priority only.
            var offenders = Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "src"), "*.cs", SearchOption.AllDirectories)
                .Where(path => path.IndexOf(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) < 0)
                .Where(path =>
                {
                    var source = File.ReadAllText(path);
                    return source.Contains(": IProjectSpecificEditorProvider") &&
                        (source.Contains("\".kbview\"") || source.Contains("\".kbcontrol\""));
                })
                .ToList();
            Assert.AreEqual(0, offenders.Count, "project-specific editor providers claiming views: " + string.Join(", ", offenders));
        }

        [TestMethod]
        [DataRow("{8B382828-6202-11D1-8870-0000F87579D2}", "{8B382828-6202-11D1-8870-0000F87579D2}", true, DisplayName = "both name the text editor")]
        [DataRow("{8b382828-6202-11d1-8870-0000f87579d2}", null, true, DisplayName = "Default Editors only, lower case")]
        [DataRow(null, "{8B382828-6202-11D1-8870-0000F87579D2}", true, DisplayName = "FileExtensionMapping only")]
        [DataRow(null, null, false, DisplayName = "no per-user default")]
        [DataRow("{FA3CD31E-987B-443A-9B81-186104E8DAC1}", null, false, DisplayName = "the XML editor: the user's own choice")]
        [DataRow("{1DCF5C91-A51E-4DE8-B066-680D6636C094}", null, false, DisplayName = "already the designer")]
        [DataRow("{8B382828-6202-11D1-8870-0000F87579D2}", "{FA3CD31E-987B-443A-9B81-186104E8DAC1}", false, DisplayName = "mixed: kept")]
        [DataRow("not a guid", null, false, DisplayName = "unreadable: kept")]
        [DataRow("{8B382828-6202-11D1-8870-0000F87579D2}:{00000000-0000-0000-0000-000000000000}", null, true, DisplayName = "settings.json form (editor:view)")]
        [DataRow("{FA3CD31E-987B-443A-9B81-186104E8DAC1}:{00000000-0000-0000-0000-000000000000}", null, false, DisplayName = "settings.json form, XML editor: kept")]
        public void Only_a_text_editor_default_is_a_leftover(string? custom, string? mapping, bool leftover)
        {
            Assert.AreEqual(leftover, DefaultEditorMigration.IsLeftover(custom, mapping));
        }

        [TestMethod]
        [DataRow("ST:0:0:{b1e99781-ab81-11d0-b683-00aa00a3ee26}", true, DisplayName = "Visual Studio 2026 name, first instance")]
        [DataRow("{B1E99781-AB81-11D0-B683-00AA00A3EE26}", true, DisplayName = "bare GUID")]
        [DataRow("ST:1:0:{b1e99781-ab81-11d0-b683-00aa00a3ee26}", false, DisplayName = "another instance")]
        [DataRow("ST:0:0:{eefa5220-e298-11d0-8f78-00a0c9110057}", false, DisplayName = "another tool window")]
        [DataRow("Document:main_view.kbview", false, DisplayName = "not a tool window")]
        public void Tool_window_views_are_found_by_their_window_manager_name(string name, bool isToolbox)
        {
            Assert.AreEqual(isToolbox, Kubuno.Views.Designer.ToolWindows.WindowManagerDocking.IsToolWindowView(name, new Guid("B1E99781-AB81-11D0-B683-00AA00A3EE26")));
        }

        [TestMethod]
        public void The_migration_covers_both_view_extensions()
        {
            CollectionAssert.AreEquivalent(new[] { "kbview", "kbcontrol" }, DefaultEditorMigration.Extensions.ToArray());
            Assert.AreEqual(new Guid("8B382828-6202-11d1-8870-0000F87579D2"), DefaultEditorMigration.TextEditor);
        }

        private static string? Section(string pkgdef, string key) => Sections(pkgdef, key).FirstOrDefault();

        private static string[] Sections(string pkgdef, string key)
        {
            // A section runs from its [key] line to the next [ line.
            var matches = Regex.Matches(pkgdef, @"^\[" + Regex.Escape(key) + @"\]\s*$(?<body>[\s\S]*?)(?=^\[|\z)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            return matches.Cast<Match>().Select(m => m.Groups["body"].Value).ToArray();
        }

        private static int Dword(string section, string name)
        {
            var match = Regex.Match(section, "^\"" + Regex.Escape(name) + "\"=dword:([0-9a-fA-F]{8})", RegexOptions.Multiline);
            Assert.IsTrue(match.Success, "no dword \"" + name + "\" in:\n" + section);
            return int.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        private static string RepositoryRoot()
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Kubuno.VisualStudio.sln")))
            {
                dir = dir.Parent;
            }

            Assert.IsNotNull(dir, "Kubuno.VisualStudio.sln not found above the test assembly.");
            return dir!.FullName;
        }
    }
}
