using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Kubuno.Rust.Cargo.Toml;

namespace Kubuno.Rust.Logic.ProjectProperties
{
    /// <summary>A property value as the Project Properties editor shows it: what is written, and what it means.</summary>
    public sealed class PropertyValue
    {
        public PropertyValue(string unevaluated, string evaluated)
        {
            Unevaluated = unevaluated;
            Evaluated = evaluated;
        }

        public string Unevaluated { get; }

        public string Evaluated { get; }

        public static PropertyValue Of(string value) => new PropertyValue(value, value);

        public static PropertyValue Empty { get; } = new PropertyValue(string.Empty, string.Empty);
    }

    /// <summary>One surgical change to one file; <see cref="CreatesFile"/> when the file does not exist yet.</summary>
    public sealed class FileTextEdit
    {
        public FileTextEdit(string path, TextEdit edit, bool createsFile)
        {
            Path = path;
            Edit = edit;
            CreatesFile = createsFile;
        }

        public string Path { get; }

        public TextEdit Edit { get; }

        public bool CreatesFile { get; }
    }

    /// <summary>The outcome of setting a property: the edits to apply, or why the value was rejected.</summary>
    public sealed class PropertyWrite
    {
        private PropertyWrite(IReadOnlyList<FileTextEdit> edits, string? error)
        {
            Edits = edits;
            Error = error;
        }

        public IReadOnlyList<FileTextEdit> Edits { get; }

        /// <summary>Non-null when the value was rejected; nothing must be written then.</summary>
        public string? Error { get; }

        public bool IsValid => Error is null;

        public static PropertyWrite None { get; } = new PropertyWrite(Array.Empty<FileTextEdit>(), null);

        public static PropertyWrite Of(params FileTextEdit[] edits) => new PropertyWrite(edits, null);

        public static PropertyWrite Invalid(string error) => new PropertyWrite(Array.Empty<FileTextEdit>(), error);
    }

    /// <summary>Dependencies declared by a manifest, for the Application page's summary.</summary>
    public readonly struct DependencyCounts
    {
        public DependencyCounts(int normal, int dev, int build, int path)
        {
            Normal = normal;
            Dev = dev;
            Build = build;
            Path = path;
        }

        public int Normal { get; }

        public int Dev { get; }

        public int Build { get; }

        /// <summary>Among all of them, those with a <c>path</c> (other projects of the solution or checkout).</summary>
        public int Path { get; }
    }

    /// <summary>
    /// The <c>.rsproj</c> Project Properties stored OUTSIDE the MSBuild project (docs/RSPROJ.md, "Project
    /// properties like .NET"): Cargo.toml fields, Cargo profiles, lint levels, rustfmt.toml settings and the
    /// binary's <c>#![windows_subsystem]</c> attribute. Pure: reads go through <see cref="RustPropertyContext"/>
    /// and writes are returned as minimal <see cref="FileTextEdit"/>s that the Visual Studio layer applies to the
    /// files' text buffers (one undo unit each) - the files are never regenerated or reformatted.
    /// </summary>
    public static class RustManifestProperties
    {
        /// <summary>The value that means "inherit this field from [workspace.package]" (Cargo's <c>field.workspace = true</c>).</summary>
        public const string WorkspaceInherited = "{ workspace = true }";

        /// <summary>Appended to a rejected value's evaluated form so the editor's validation regex (<c>^[^​]*$</c>) shows the page's message.</summary>
        public const char InvalidMarker = '​';

        private static readonly Regex WorkspaceInheritedRegex = new Regex(@"^\{\s*workspace\s*=\s*true\s*\}$", RegexOptions.CultureInvariant);

        private static readonly string[] LibraryCrateTypes = { "lib", "rlib", "dylib", "cdylib", "staticlib", "proc-macro" };

        private static readonly string[] LintLevels = { "allow", "warn", "deny", "forbid" };

        private static readonly Dictionary<string, PropertyDefinition> Definitions = CreateDefinitions();

        /// <summary>Whether this model owns <paramref name="name"/> (otherwise it is an MSBuild property).</summary>
        public static bool Handles(string name) => Definitions.ContainsKey(name);

        /// <summary>The names this model owns.</summary>
        public static IReadOnlyCollection<string> Names => Definitions.Keys;

        /// <summary>The current value of <paramref name="name"/>.</summary>
        public static PropertyValue Get(string name, RustPropertyContext context) =>
            Definitions.TryGetValue(name, out PropertyDefinition? definition) ? definition.Get(context) : PropertyValue.Empty;

        /// <summary>The edits that set <paramref name="name"/> to <paramref name="value"/> (none when unchanged), or the reason it is rejected.</summary>
        public static PropertyWrite Set(string name, string? value, RustPropertyContext context)
        {
            if (!Definitions.TryGetValue(name, out PropertyDefinition? definition) || definition.Set is null)
            {
                return PropertyWrite.None;
            }
            try
            {
                return definition.Set(context, (value ?? string.Empty).Trim());
            }
            catch (TomlParseException ex)
            {
                return PropertyWrite.Invalid(ex.Message);
            }
        }

        /// <summary>The edits that reset <paramref name="name"/> to its default (the key is removed).</summary>
        public static PropertyWrite Reset(string name, RustPropertyContext context)
        {
            if (!Definitions.TryGetValue(name, out PropertyDefinition? definition) || definition.Reset is null)
            {
                return PropertyWrite.None;
            }
            return definition.Reset(context);
        }

        /// <summary>The binary targets of the package: explicit [[bin]], src/main.rs (named after the package) and src/bin/*.rs.</summary>
        public static IReadOnlyList<string> GetBinaryTargets(RustPropertyContext context)
        {
            var names = new List<string>();
            TomlDocument? manifest = context.Manifest;
            if (manifest is null)
            {
                return names;
            }

            string? package = manifest.GetValue("package", "name")?.AsString();
            foreach (TomlTableView bin in manifest.GetArrayOfTables("bin"))
            {
                string? name = bin.GetValue("name")?.AsString();
                if (!string.IsNullOrEmpty(name) && !names.Contains(name!))
                {
                    names.Add(name!);
                }
            }

            bool autobins = manifest.GetValue("package", "autobins")?.AsBoolean() != false;
            if (autobins && package != null && !names.Contains(package) &&
                context.Files.FileExists(Path.Combine(context.ManifestDirectory, "src", "main.rs")) &&
                !manifest.GetArrayOfTables("bin").Any(b => PathEquals(b.GetValue("path")?.AsString(), "src/main.rs")))
            {
                names.Add(package);
            }
            if (autobins)
            {
                foreach (string file in context.Files.GetFiles(Path.Combine(context.ManifestDirectory, "src", "bin"), "*.rs"))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (!names.Contains(name))
                    {
                        names.Add(name);
                    }
                }
            }
            return names;
        }

        /// <summary>The features a build can enable: the [features] table, plus the implicit feature of each optional dependency not referenced as <c>dep:name</c>.</summary>
        public static IReadOnlyList<string> GetFeatures(RustPropertyContext context)
        {
            var features = new List<string>();
            TomlDocument? manifest = context.Manifest;
            if (manifest is null)
            {
                return features;
            }

            var explicitDeps = new HashSet<string>(StringComparer.Ordinal);
            foreach (string feature in manifest.GetKeys("features"))
            {
                if (feature != "default")
                {
                    features.Add(feature);
                }
                foreach (TomlValue item in manifest.GetValue("features", feature)?.AsArray() ?? Array.Empty<TomlValue>())
                {
                    string? text = item.AsString();
                    if (text != null && text.StartsWith("dep:", StringComparison.Ordinal))
                    {
                        explicitDeps.Add(text.Substring(4));
                    }
                }
            }

            foreach (string dependency in manifest.GetKeys("dependencies"))
            {
                if (manifest.GetValue("dependencies", dependency, "optional")?.AsBoolean() == true &&
                    !explicitDeps.Contains(dependency) && !features.Contains(dependency))
                {
                    features.Add(dependency);
                }
            }
            return features;
        }

        /// <summary>Counts of declared dependencies (target-specific tables included).</summary>
        public static DependencyCounts GetDependencyCounts(RustPropertyContext context)
        {
            TomlDocument? manifest = context.Manifest;
            if (manifest is null)
            {
                return default;
            }

            int normal = 0, dev = 0, build = 0, path = 0;
            void Count(string[] table, ref int counter)
            {
                foreach (string key in manifest.GetKeys(table))
                {
                    counter++;
                    TomlValue? value = manifest.GetValue(table.Concat(new[] { key }).ToArray());
                    if (value?.AsTable()?.Any(e => e.Key == "path") == true)
                    {
                        path++;
                    }
                }
            }

            Count(new[] { "dependencies" }, ref normal);
            Count(new[] { "dev-dependencies" }, ref dev);
            Count(new[] { "build-dependencies" }, ref build);
            foreach (string target in manifest.GetKeys("target"))
            {
                Count(new[] { "target", target, "dependencies" }, ref normal);
                Count(new[] { "target", target, "dev-dependencies" }, ref dev);
                Count(new[] { "target", target, "build-dependencies" }, ref build);
            }
            return new DependencyCounts(normal, dev, build, path);
        }

        /// <summary>The rustfmt configuration file of the package: an existing rustfmt.toml or .rustfmt.toml, else rustfmt.toml (created on first change).</summary>
        public static string GetRustfmtConfigPath(RustPropertyContext context)
        {
            string plain = Path.Combine(context.ManifestDirectory, "rustfmt.toml");
            string dotted = Path.Combine(context.ManifestDirectory, ".rustfmt.toml");
            return !context.Files.FileExists(plain) && context.Files.FileExists(dotted) ? dotted : plain;
        }

        /// <summary>The main source file of the binary the project builds (for #![windows_subsystem]), or null when there is none.</summary>
        public static string? GetMainSourcePath(RustPropertyContext context)
        {
            TomlDocument? manifest = context.Manifest;
            if (manifest is null)
            {
                return null;
            }

            string? package = manifest.GetValue("package", "name")?.AsString();
            string? bin = context.BinName ?? manifest.GetValue("package", "default-run")?.AsString() ?? package;
            foreach (TomlTableView table in manifest.GetArrayOfTables("bin"))
            {
                if (bin != null && table.GetValue("name")?.AsString() == bin)
                {
                    string? explicitPath = table.GetValue("path")?.AsString();
                    if (!string.IsNullOrEmpty(explicitPath))
                    {
                        return Path.GetFullPath(Path.Combine(context.ManifestDirectory, explicitPath!));
                    }
                    string inBin = Path.Combine(context.ManifestDirectory, "src", "bin", bin + ".rs");
                    if (context.Files.FileExists(inBin))
                    {
                        return inBin;
                    }
                    return bin == package ? Path.Combine(context.ManifestDirectory, "src", "main.rs") : Path.Combine(context.ManifestDirectory, "src", "bin", bin, "main.rs");
                }
            }

            if (bin != null && bin != package)
            {
                string inBin = Path.Combine(context.ManifestDirectory, "src", "bin", bin + ".rs");
                if (context.Files.FileExists(inBin))
                {
                    return inBin;
                }
            }

            string main = Path.Combine(context.ManifestDirectory, "src", "main.rs");
            return context.Files.FileExists(main) ? main : null;
        }

        private static bool PathEquals(string? a, string b) =>
            a != null && string.Equals(a.Replace('\\', '/').TrimStart('.', '/'), b, StringComparison.OrdinalIgnoreCase);

        // ---------------------------------------------------------------------------------------------
        // Definitions
        // ---------------------------------------------------------------------------------------------

        private sealed class PropertyDefinition
        {
            public PropertyDefinition(Func<RustPropertyContext, PropertyValue> get, Func<RustPropertyContext, string, PropertyWrite>? set, Func<RustPropertyContext, PropertyWrite>? reset)
            {
                Get = get;
                Set = set;
                Reset = reset;
            }

            public Func<RustPropertyContext, PropertyValue> Get { get; }

            public Func<RustPropertyContext, string, PropertyWrite>? Set { get; }

            public Func<RustPropertyContext, PropertyWrite>? Reset { get; }
        }

        private static Dictionary<string, PropertyDefinition> CreateDefinitions()
        {
            var d = new Dictionary<string, PropertyDefinition>(StringComparer.OrdinalIgnoreCase);

            // ----- Application -----
            d["PackageName"] = PackageString("name", PropertyValidation.IsValidCrateName, "Invalid crate name.", allowInherit: false, allowEmpty: false);
            d["Edition"] = PackageEnum("edition", "2015", new[] { "2015", "2018", "2021", "2024" });
            d["RustVersion"] = PackageString("rust-version", PropertyValidation.IsValidRustVersion, "Invalid Rust version.");
            d["DefaultRun"] = PackageString("default-run", _ => true, string.Empty, allowInherit: false);
            d["HasBinaryTarget"] = ReadOnly(c => GetBinaryTargets(c).Count > 0 ? "true" : "false");
            d["HasLibraryTarget"] = ReadOnly(c => HasLibrary(c) ? "true" : "false");
            d["TargetKinds"] = ReadOnly(DescribeTargets);
            d["LibraryCrateTypes"] = StringArray(c => c.ManifestPath, new[] { "lib", "crate-type" },
                v => LibraryCrateTypes.Contains(v, StringComparer.Ordinal) ? null : $"Unknown crate type '{v}'.", maxCount: null, inheritedField: null);
            d["WindowsSubsystem"] = new PropertyDefinition(GetSubsystem, SetSubsystem, c => SetSubsystem(c, WindowsSubsystemAttribute.Console));

            // ----- Build (configuration-specific: [profile.<name>] of the workspace root) -----
            d["CargoProfileName"] = ReadOnly(c => c.ProfileName);
            d["ProfileOptLevel"] = Profile("opt-level", ReadOptLevel, WriteOptLevel, p => p == "release" ? "3" : "0");
            d["ProfileDebug"] = Profile("debug", ReadDebug, WriteDebug, p => p == "release" ? "none" : "full");
            d["ProfileIncremental"] = Profile("incremental", ReadBool, WriteBool, p => p == "release" ? "false" : "true");
            d["ProfileLto"] = Profile("lto", ReadLto, WriteLto, _ => "false");
            d["ProfileCodegenUnits"] = Profile("codegen-units", v => v.AsInteger()?.ToString(CultureInfo.InvariantCulture), WriteCodegenUnits, p => p == "release" ? "16" : "256");
            d["ProfilePanic"] = Profile("panic", v => v.AsString(), v => v == "unwind" || v == "abort" ? TomlValue.String(v) : null, _ => "unwind");
            d["ProfileOverflowChecks"] = Profile("overflow-checks", ReadBool, WriteBool, p => p == "release" ? "false" : "true");
            d["ProfileDebugAssertions"] = Profile("debug-assertions", ReadBool, WriteBool, p => p == "release" ? "false" : "true");
            d["ProfileStrip"] = Profile("strip", ReadStrip, WriteStrip, _ => "none");

            // ----- Package -----
            d["PackageVersion"] = PackageString("version", PropertyValidation.IsValidSemVer, "Invalid version.");
            d["PackageDescription"] = PackageString("description", _ => true, string.Empty);
            d["PackageReadme"] = PackageString("readme", _ => true, string.Empty);
            d["PackageHomepage"] = PackageString("homepage", PropertyValidation.IsValidUrl, "Invalid URL.");
            d["PackageRepository"] = PackageString("repository", PropertyValidation.IsValidUrl, "Invalid URL.");
            d["PackageDocumentation"] = PackageString("documentation", PropertyValidation.IsValidUrl, "Invalid URL.");
            d["PackageLicense"] = PackageString("license", PropertyValidation.IsValidSpdxExpression, "Invalid SPDX license expression.");
            d["PackageLicenseFile"] = PackageString("license-file", _ => true, string.Empty);
            d["PackageLicensePreset"] = new PropertyDefinition(GetLicensePreset, SetLicensePreset, null);
            d["PackageAuthors"] = StringArray(c => c.ManifestPath, new[] { "package", "authors" }, _ => null, maxCount: null, inheritedField: "authors");
            d["PackageKeywords"] = StringArray(c => c.ManifestPath, new[] { "package", "keywords" },
                v => PropertyValidation.IsValidKeyword(v) ? null : $"Invalid keyword '{v}'.", maxCount: 5, inheritedField: "keywords");
            d["PackageCategories"] = StringArray(c => c.ManifestPath, new[] { "package", "categories" },
                v => CratesIoCategories.All.Contains(v, StringComparer.Ordinal) ? null : $"Unknown crates.io category '{v}'.", maxCount: 5, inheritedField: "categories");
            d["PackageInclude"] = StringArray(c => c.ManifestPath, new[] { "package", "include" }, _ => null, maxCount: null, inheritedField: "include");
            d["PackageExclude"] = StringArray(c => c.ManifestPath, new[] { "package", "exclude" }, _ => null, maxCount: null, inheritedField: "exclude");
            d["PackagePublish"] = new PropertyDefinition(GetPublish, SetPublish, c => RemoveKey(c, c.ManifestPath, "package", "publish"));
            d["PackagePublishRegistries"] = new PropertyDefinition(GetPublishRegistries, SetPublishRegistries, null);

            // ----- Code Analysis: [lints] -----
            foreach (string group in new[] { "correctness", "suspicious", "style", "complexity", "perf", "pedantic", "nursery", "cargo" })
            {
                d["Clippy" + char.ToUpperInvariant(group[0]) + group.Substring(1)] = Lint("clippy", group, isGroup: true);
            }
            d["RustLintUnsafeCode"] = Lint("rust", "unsafe_code", isGroup: false);
            d["RustLintMissingDocs"] = Lint("rust", "missing_docs", isGroup: false);

            // ----- Code Analysis: rustfmt.toml -----
            d["RustfmtEdition"] = Rustfmt("edition", "default", v => v.AsString(), v => v == "2015" || v == "2018" || v == "2021" || v == "2024" ? TomlValue.String(v) : null);
            d["RustfmtMaxWidth"] = Rustfmt("max_width", "100", v => v.AsInteger()?.ToString(CultureInfo.InvariantCulture), v => IntValue(v, 10, 1000));
            d["RustfmtHardTabs"] = Rustfmt("hard_tabs", "false", ReadBool, WriteBool);
            d["RustfmtTabSpaces"] = Rustfmt("tab_spaces", "4", v => v.AsInteger()?.ToString(CultureInfo.InvariantCulture), v => IntValue(v, 1, 16));
            d["RustfmtNewlineStyle"] = Rustfmt("newline_style", "Auto", v => v.AsString(), v => v == "Auto" || v == "Native" || v == "Unix" || v == "Windows" ? TomlValue.String(v) : null);
            d["RustfmtReorderImports"] = Rustfmt("reorder_imports", "true", ReadBool, WriteBool);
            d["RustfmtUseFieldInitShorthand"] = Rustfmt("use_field_init_shorthand", "false", ReadBool, WriteBool);
            d["RustfmtConfigPath"] = ReadOnly(GetRustfmtConfigPath);
            return d;
        }

        private static PropertyDefinition ReadOnly(Func<RustPropertyContext, string> get) =>
            new PropertyDefinition(c => PropertyValue.Of(get(c)), null, null);

        // ----- [package] scalar fields -----

        private static PropertyDefinition PackageString(string field, Func<string, bool> isValid, string error, bool allowInherit = true, bool allowEmpty = true) =>
            new PropertyDefinition(
                c => GetPackageScalar(c, field),
                (c, value) =>
                {
                    if (value.Length == 0)
                    {
                        return allowEmpty ? RemoveKey(c, c.ManifestPath, "package", field) : PropertyWrite.Invalid(error);
                    }
                    if (WorkspaceInheritedRegex.IsMatch(value))
                    {
                        return allowInherit ? SetKey(c, c.ManifestPath, WorkspaceTable(), "package", field) : PropertyWrite.Invalid(error);
                    }
                    if (!isValid(value))
                    {
                        return PropertyWrite.Invalid(error);
                    }
                    return SetKey(c, c.ManifestPath, TomlValue.String(value), "package", field);
                },
                c => allowEmpty ? RemoveKey(c, c.ManifestPath, "package", field) : PropertyWrite.None);

        private static PropertyDefinition PackageEnum(string field, string defaultValue, string[] values) =>
            new PropertyDefinition(
                c =>
                {
                    TomlValue? value = c.Manifest?.GetValue("package", field);
                    if (value is null)
                    {
                        return new PropertyValue(string.Empty, defaultValue);
                    }
                    if (IsWorkspaceInherited(value))
                    {
                        return PropertyValue.Of(WorkspaceInherited);
                    }
                    return PropertyValue.Of(value.AsString() ?? value.Raw ?? string.Empty);
                },
                (c, value) =>
                {
                    if (WorkspaceInheritedRegex.IsMatch(value))
                    {
                        return SetKey(c, c.ManifestPath, WorkspaceTable(), "package", field);
                    }
                    if (!values.Contains(value, StringComparer.Ordinal))
                    {
                        return PropertyWrite.Invalid($"Unknown {field} '{value}'.");
                    }
                    return SetKey(c, c.ManifestPath, TomlValue.String(value), "package", field);
                },
                c => RemoveKey(c, c.ManifestPath, "package", field));

        private static PropertyValue GetPackageScalar(RustPropertyContext context, string field)
        {
            TomlValue? value = context.Manifest?.GetValue("package", field);
            if (value is null)
            {
                return PropertyValue.Empty;
            }
            if (IsWorkspaceInherited(value))
            {
                TomlValue? inherited = GetInherited(context, "package", field);
                return new PropertyValue(WorkspaceInherited, inherited is null ? string.Empty : Display(inherited));
            }
            return PropertyValue.Of(Display(value));
        }

        private static string Display(TomlValue value) =>
            value.AsString() ?? (value.Kind == TomlValueKind.Boolean ? (value.AsBoolean() == true ? "true" : "false") : value.Raw ?? TomlValueFormatter.Format(value));

        private static TomlValue? GetInherited(RustPropertyContext context, params string[] pathInWorkspace)
        {
            string? root = context.WorkspaceRootManifestPath;
            if (root is null)
            {
                return null;
            }
            return context.GetDocument(root)?.GetValue(new[] { "workspace" }.Concat(pathInWorkspace).ToArray());
        }

        private static bool IsWorkspaceInherited(TomlValue value) =>
            value.AsTable()?.Any(e => e.Key == "workspace" && e.Value.AsBoolean() == true) == true;

        private static TomlValue WorkspaceTable() =>
            TomlValue.Table(new[] { new KeyValuePair<string, TomlValue>("workspace", TomlValue.Boolean(true)) });

        // ----- String arrays (MultiStringSelector) -----

        private static PropertyDefinition StringArray(Func<RustPropertyContext, string> file, string[] path, Func<string, string?> validate, int? maxCount, string? inheritedField) =>
            new PropertyDefinition(
                c =>
                {
                    TomlValue? value = c.GetDocument(file(c))?.GetValue(path);
                    if (value != null && inheritedField != null && IsWorkspaceInherited(value))
                    {
                        // Inherited entries are shown checked and read-only (the selector's "True" flag).
                        IEnumerable<string> inherited = GetInherited(c, "package", inheritedField)?.AsArray()?.Select(v => v.AsString()).Where(v => v != null).Select(v => v!) ?? Enumerable.Empty<string>();
                        return PropertyValue.Of(PropertyListEncoding.EncodePairs(inherited.Select(v => new KeyValuePair<string, string>(v, "True"))));
                    }
                    IEnumerable<string> items = value?.AsArray()?.Select(v => v.AsString()).Where(v => v != null).Select(v => v!) ?? Enumerable.Empty<string>();
                    return PropertyValue.Of(PropertyListEncoding.EncodeStrings(items));
                },
                (c, value) =>
                {
                    string path0 = file(c);
                    TomlValue? current = c.GetDocument(path0)?.GetValue(path);
                    IReadOnlyList<string> items = PropertyListEncoding.DecodeStrings(value);
                    if (current != null && inheritedField != null && IsWorkspaceInherited(current))
                    {
                        IReadOnlyList<string> inherited = GetInherited(c, "package", inheritedField)?.AsArray()?.Select(v => v.AsString() ?? string.Empty).ToArray() ?? Array.Empty<string>();
                        if (items.SequenceEqual(inherited, StringComparer.Ordinal))
                        {
                            return PropertyWrite.None;
                        }
                    }
                    foreach (string item in items)
                    {
                        string? error = validate(item);
                        if (error != null)
                        {
                            return PropertyWrite.Invalid(error);
                        }
                    }
                    if (maxCount.HasValue && items.Count > maxCount.Value)
                    {
                        return PropertyWrite.Invalid($"At most {maxCount.Value} entries are allowed.");
                    }
                    if (items.Count == 0)
                    {
                        return RemoveKey(c, path0, path);
                    }
                    return SetKey(c, path0, TomlValue.Array(items.Select(TomlValue.String)), path);
                },
                c => RemoveKey(c, file(c), path));

        // ----- Library / targets -----

        private static bool HasLibrary(RustPropertyContext context) =>
            context.Manifest?.ContainsTable("lib") == true || context.Files.FileExists(Path.Combine(context.ManifestDirectory, "src", "lib.rs"));

        private static string DescribeTargets(RustPropertyContext context)
        {
            var parts = new List<string>();
            foreach (string bin in GetBinaryTargets(context))
            {
                parts.Add($"bin: {bin}.exe");
            }
            if (HasLibrary(context))
            {
                string? package = context.Manifest?.GetValue("package", "name")?.AsString();
                string name = context.Manifest?.GetValue("lib", "name")?.AsString() ?? package?.Replace('-', '_') ?? "lib";
                IReadOnlyList<string> types = context.Manifest?.GetValue("lib", "crate-type")?.AsArray()?.Select(v => v.AsString() ?? string.Empty).ToArray() ?? Array.Empty<string>();
                parts.Add(types.Count == 0 ? $"lib: {name}" : $"lib: {name} ({string.Join(", ", types)})");
            }
            return string.Join(" · ", parts);
        }

        // ----- windows_subsystem -----

        private static PropertyValue GetSubsystem(RustPropertyContext context)
        {
            string? path = GetMainSourcePath(context);
            string? text = path is null ? null : context.Files.ReadText(path);
            return PropertyValue.Of(text is null ? WindowsSubsystemAttribute.Console : WindowsSubsystemAttribute.Read(text));
        }

        private static PropertyWrite SetSubsystem(RustPropertyContext context, string value)
        {
            if (value != WindowsSubsystemAttribute.Console && value != WindowsSubsystemAttribute.Windows && value != WindowsSubsystemAttribute.WindowsInRelease)
            {
                return PropertyWrite.Invalid($"Unknown Windows subsystem '{value}'.");
            }
            string? path = GetMainSourcePath(context);
            string? text = path is null ? null : context.Files.ReadText(path);
            if (path is null || text is null)
            {
                return PropertyWrite.Invalid("The binary's main source file (src/main.rs) was not found.");
            }
            TextEdit? edit = WindowsSubsystemAttribute.Write(text, value);
            return edit is null ? PropertyWrite.None : PropertyWrite.Of(new FileTextEdit(path, edit.Value, createsFile: false));
        }

        // ----- License preset -----

        private static readonly string[] LicensePresets =
        {
            "MIT OR Apache-2.0", "MIT", "Apache-2.0", "AGPL-3.0-only", "AGPL-3.0-or-later", "GPL-3.0-only", "GPL-3.0-or-later",
            "GPL-2.0-only", "LGPL-3.0-only", "LGPL-2.1-only", "MPL-2.0", "BSD-3-Clause", "BSD-2-Clause", "ISC", "Zlib", "BSL-1.0", "0BSD", "Unlicense",
        };

        private static PropertyValue GetLicensePreset(RustPropertyContext context)
        {
            TomlValue? value = context.Manifest?.GetValue("package", "license");
            if (value is null)
            {
                return PropertyValue.Of("(none)");
            }
            if (IsWorkspaceInherited(value))
            {
                return PropertyValue.Of(WorkspaceInherited);
            }
            string? text = value.AsString();
            string? preset = LicensePresets.FirstOrDefault(p => string.Equals(p, text, StringComparison.Ordinal))
                ?? LicensePresets.FirstOrDefault(p => text != null && string.Equals(p, text.Replace("/", " OR "), StringComparison.Ordinal));
            return PropertyValue.Of(preset ?? "(other)");
        }

        private static PropertyWrite SetLicensePreset(RustPropertyContext context, string value)
        {
            switch (value)
            {
                case "(other)":
                    return PropertyWrite.None;
                case "(none)":
                case "":
                    return RemoveKey(context, context.ManifestPath, "package", "license");
                default:
                    if (WorkspaceInheritedRegex.IsMatch(value))
                    {
                        return SetKey(context, context.ManifestPath, WorkspaceTable(), "package", "license");
                    }
                    return LicensePresets.Contains(value, StringComparer.Ordinal)
                        ? SetKey(context, context.ManifestPath, TomlValue.String(value), "package", "license")
                        : PropertyWrite.Invalid($"Unknown license '{value}'.");
            }
        }

        // ----- publish -----

        private static PropertyValue GetPublish(RustPropertyContext context)
        {
            TomlValue? value = context.Manifest?.GetValue("package", "publish");
            if (value is null)
            {
                return new PropertyValue(string.Empty, "true");
            }
            if (IsWorkspaceInherited(value))
            {
                return PropertyValue.Of(WorkspaceInherited);
            }
            if (value.Kind == TomlValueKind.Array)
            {
                return PropertyValue.Of("registries");
            }
            return PropertyValue.Of(value.AsBoolean() == false ? "false" : "true");
        }

        private static PropertyWrite SetPublish(RustPropertyContext context, string value)
        {
            TomlValue? current = context.Manifest?.GetValue("package", "publish");
            switch (value)
            {
                case "true":
                    return RemoveKey(context, context.ManifestPath, "package", "publish");
                case "false":
                    return SetKey(context, context.ManifestPath, TomlValue.Boolean(false), "package", "publish");
                case "registries":
                    return current?.Kind == TomlValueKind.Array
                        ? PropertyWrite.None
                        : SetKey(context, context.ManifestPath, TomlValue.Array(new[] { TomlValue.String("crates-io") }), "package", "publish");
                default:
                    return WorkspaceInheritedRegex.IsMatch(value)
                        ? SetKey(context, context.ManifestPath, WorkspaceTable(), "package", "publish")
                        : PropertyWrite.Invalid($"Unknown publish setting '{value}'.");
            }
        }

        private static PropertyValue GetPublishRegistries(RustPropertyContext context)
        {
            TomlValue? value = context.Manifest?.GetValue("package", "publish");
            IEnumerable<string> items = value?.AsArray()?.Select(v => v.AsString()).Where(v => v != null).Select(v => v!) ?? Enumerable.Empty<string>();
            return PropertyValue.Of(PropertyListEncoding.EncodeStrings(items));
        }

        private static PropertyWrite SetPublishRegistries(RustPropertyContext context, string value)
        {
            if (context.Manifest?.GetValue("package", "publish")?.Kind != TomlValueKind.Array)
            {
                return PropertyWrite.None;
            }
            IReadOnlyList<string> items = PropertyListEncoding.DecodeStrings(value);
            return SetKey(context, context.ManifestPath, TomlValue.Array(items.Select(TomlValue.String)), "package", "publish");
        }

        // ----- [profile.<name>] -----

        private static PropertyDefinition Profile(string key, Func<TomlValue, string?> read, Func<string, TomlValue?> write, Func<string, string> defaultFor) =>
            new PropertyDefinition(
                c =>
                {
                    TomlValue? value = c.GetDocument(c.ProfileManifestPath)?.GetValue("profile", c.ProfileName, key);
                    string defaultValue = defaultFor(ProfileBase(c));
                    if (value is null)
                    {
                        return new PropertyValue(string.Empty, defaultValue);
                    }
                    string text = read(value) ?? value.Raw ?? defaultValue;
                    return PropertyValue.Of(text);
                },
                (c, value) =>
                {
                    TomlValue? toml = write(value);
                    if (toml is null)
                    {
                        return PropertyWrite.Invalid($"Invalid {key} value '{value}'.");
                    }
                    TomlValue? current = c.GetDocument(c.ProfileManifestPath)?.GetValue("profile", c.ProfileName, key);
                    if (current is null && value == defaultFor(ProfileBase(c)))
                    {
                        return PropertyWrite.None; // Already Cargo's default: keep the manifest untouched.
                    }
                    if (current != null && read(current) == value)
                    {
                        return PropertyWrite.None; // Equivalent spelling already present (e.g. debug = 2 vs "full").
                    }
                    return SetKey(c, c.ProfileManifestPath, toml, "profile", c.ProfileName, key);
                },
                c => RemoveKey(c, c.ProfileManifestPath, "profile", c.ProfileName, key));

        /// <summary>"dev" or "release": the built-in profile a profile (possibly custom, through <c>inherits</c>) derives from.</summary>
        private static string ProfileBase(RustPropertyContext context)
        {
            string profile = context.ProfileName;
            for (int depth = 0; depth < 8; depth++)
            {
                if (profile == "dev" || profile == "release" || profile == "test" || profile == "bench")
                {
                    return profile == "bench" ? "release" : profile == "test" ? "dev" : profile;
                }
                string? inherits = context.GetDocument(context.ProfileManifestPath)?.GetValue("profile", profile, "inherits")?.AsString();
                if (inherits is null)
                {
                    return "dev";
                }
                profile = inherits;
            }
            return "dev";
        }

        private static string? ReadBool(TomlValue value) => value.AsBoolean() is bool b ? (b ? "true" : "false") : null;

        private static TomlValue? WriteBool(string value) =>
            value == "true" ? TomlValue.Boolean(true) : value == "false" ? TomlValue.Boolean(false) : null;

        private static string? ReadOptLevel(TomlValue value) =>
            value.AsInteger()?.ToString(CultureInfo.InvariantCulture) ?? value.AsString();

        private static TomlValue? WriteOptLevel(string value) =>
            value is "0" or "1" or "2" or "3" ? TomlValue.Integer(long.Parse(value, CultureInfo.InvariantCulture)) :
            value is "s" or "z" ? TomlValue.String(value) : null;

        private static string? ReadDebug(TomlValue value)
        {
            if (value.AsBoolean() is bool b)
            {
                return b ? "full" : "none";
            }
            switch (value.AsInteger())
            {
                case 0:
                    return "none";
                case 1:
                    return "limited";
                case 2:
                    return "full";
            }
            return value.AsString();
        }

        private static TomlValue? WriteDebug(string value)
        {
            switch (value)
            {
                case "none":
                    return TomlValue.Boolean(false);
                case "full":
                    return TomlValue.Boolean(true);
                case "limited":
                    return TomlValue.Integer(1);
                case "line-tables-only":
                case "line-directives-only":
                    return TomlValue.String(value);
                default:
                    return null;
            }
        }

        private static string? ReadLto(TomlValue value)
        {
            if (value.AsBoolean() is bool b)
            {
                return b ? "fat" : "false";
            }
            return value.AsString();
        }

        private static TomlValue? WriteLto(string value) =>
            value == "false" ? TomlValue.Boolean(false) : value is "thin" or "fat" or "off" ? TomlValue.String(value) : null;

        private static TomlValue? WriteCodegenUnits(string value) => IntValue(value, 1, 65536);

        private static string? ReadStrip(TomlValue value)
        {
            if (value.AsBoolean() is bool b)
            {
                return b ? "symbols" : "none";
            }
            return value.AsString();
        }

        private static TomlValue? WriteStrip(string value) => value is "none" or "debuginfo" or "symbols" ? TomlValue.String(value) : null;

        private static TomlValue? IntValue(string value, long min, long max) =>
            PropertyValidation.IsIntegerInRange(value, min, max) ? TomlValue.Integer(long.Parse(value, CultureInfo.InvariantCulture)) : null;

        // ----- [lints] -----

        private static PropertyDefinition Lint(string tool, string lint, bool isGroup) =>
            new PropertyDefinition(
                c =>
                {
                    TomlDocument? manifest = c.Manifest;
                    if (manifest?.GetValue("lints", "workspace")?.AsBoolean() == true)
                    {
                        return PropertyValue.Of(ReadLintLevel(GetInherited(c, "lints", tool, lint)));
                    }
                    return PropertyValue.Of(ReadLintLevel(manifest?.GetValue("lints", tool, lint)));
                },
                (c, value) =>
                {
                    TomlDocument? manifest = c.Manifest;
                    if (manifest?.GetValue("lints", "workspace")?.AsBoolean() == true)
                    {
                        return PropertyWrite.Invalid("The lints of this package are inherited from the workspace ([lints] workspace = true): change them in the workspace's Cargo.toml.");
                    }
                    if (value == "default" || value.Length == 0)
                    {
                        return RemoveKey(c, c.ManifestPath, "lints", tool, lint);
                    }
                    if (!LintLevels.Contains(value, StringComparer.Ordinal))
                    {
                        return PropertyWrite.Invalid($"Unknown lint level '{value}'.");
                    }
                    TomlValue? current = manifest?.GetValue("lints", tool, lint);
                    if (ReadLintLevel(current) == value)
                    {
                        return PropertyWrite.None;
                    }
                    if (current?.Kind == TomlValueKind.Table)
                    {
                        return SetKey(c, c.ManifestPath, TomlValue.String(value), "lints", tool, lint, "level");
                    }
                    // A group needs a lower priority than the individual lints (Cargo warns otherwise).
                    TomlValue level = isGroup
                        ? TomlValue.Table(new[]
                        {
                            new KeyValuePair<string, TomlValue>("level", TomlValue.String(value)),
                            new KeyValuePair<string, TomlValue>("priority", TomlValue.Integer(-1)),
                        })
                        : TomlValue.String(value);
                    return SetKey(c, c.ManifestPath, level, "lints", tool, lint);
                },
                c => RemoveKey(c, c.ManifestPath, "lints", tool, lint));

        private static string ReadLintLevel(TomlValue? value)
        {
            if (value is null)
            {
                return "default";
            }
            string? level = value.AsString() ?? value.AsTable()?.FirstOrDefault(e => e.Key == "level").Value?.AsString();
            return level != null && LintLevels.Contains(level, StringComparer.Ordinal) ? level : "default";
        }

        // ----- rustfmt.toml -----

        private static PropertyDefinition Rustfmt(string key, string defaultValue, Func<TomlValue, string?> read, Func<string, TomlValue?> write) =>
            new PropertyDefinition(
                c =>
                {
                    TomlValue? value = c.GetDocument(GetRustfmtConfigPath(c))?.GetValue(key);
                    return value is null ? new PropertyValue(string.Empty, defaultValue) : PropertyValue.Of(read(value) ?? value.Raw ?? defaultValue);
                },
                (c, value) =>
                {
                    string path = GetRustfmtConfigPath(c);
                    TomlValue? current = c.GetDocument(path)?.GetValue(key);
                    if (value == "default" || value.Length == 0)
                    {
                        return RemoveKey(c, path, key);
                    }
                    TomlValue? toml = write(value);
                    if (toml is null)
                    {
                        return PropertyWrite.Invalid($"Invalid {key} value '{value}'.");
                    }
                    if ((current is null && value == defaultValue) || (current != null && read(current) == value))
                    {
                        return PropertyWrite.None;
                    }
                    return SetKey(c, path, toml, key);
                },
                c => RemoveKey(c, GetRustfmtConfigPath(c), key));

        // ----- Edits -----

        private static PropertyWrite SetKey(RustPropertyContext context, string path, TomlValue value, params string[] keyPath)
        {
            string? text = context.Files.ReadText(path);
            bool creates = text is null;
            TomlDocument? document = creates ? TomlDocument.Parse(string.Empty) : context.GetDocument(path);
            if (document is null)
            {
                return PropertyWrite.Invalid(context.GetParseError(path) ?? $"{Path.GetFileName(path)} could not be read.");
            }
            TomlEdit? edit = document.SetValue(value, keyPath);
            return edit is null ? PropertyWrite.None : PropertyWrite.Of(new FileTextEdit(path, new TextEdit(edit.Value.Start, edit.Value.OldLength, edit.Value.NewText), creates));
        }

        private static PropertyWrite RemoveKey(RustPropertyContext context, string path, params string[] keyPath)
        {
            if (context.Files.ReadText(path) is null)
            {
                return PropertyWrite.None;
            }
            TomlDocument? document = context.GetDocument(path);
            if (document is null)
            {
                return PropertyWrite.Invalid(context.GetParseError(path) ?? $"{Path.GetFileName(path)} could not be read.");
            }
            TomlEdit? edit = document.Remove(keyPath);
            return edit is null ? PropertyWrite.None : PropertyWrite.Of(new FileTextEdit(path, new TextEdit(edit.Value.Start, edit.Value.OldLength, edit.Value.NewText), createsFile: false));
        }
    }
}
