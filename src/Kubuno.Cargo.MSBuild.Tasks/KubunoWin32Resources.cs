using System;
using System.IO;
using System.Xml;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Kubuno.Cargo.Toml;
using Kubuno.Cargo.Win32;
using Microsoft.Build.Framework;
using MSBuildTask = Microsoft.Build.Utilities.Task;

namespace Kubuno.Cargo.MSBuild.Tasks
{
    /// <summary>
    /// Generates a binary Win32 <c>.res</c> file (icon, application manifest, version info) that
    /// the SDK hands to the MSVC linker, so a Rust executable gets its resources without any
    /// <c>build.rs</c>. The output is rewritten only when its bytes change, which keeps
    /// incremental builds and cargo fingerprints stable.
    /// <para>
    /// Error codes: KUBUNO0101 icon file not found; KUBUNO0102 icon file is not a valid .ico;
    /// KUBUNO0103 invalid <see cref="ManifestMode"/>; KUBUNO0104 custom manifest missing
    /// (not set or file not found); KUBUNO0105 invalid <see cref="DpiAwareness"/>;
    /// KUBUNO0106 invalid <see cref="ExecutionLevel"/>; KUBUNO0107 invalid
    /// <see cref="MinimumWindowsVersion"/>; KUBUNO0108 the output file cannot be written;
    /// KUBUNO0109 the custom manifest cannot be read or is not well-formed XML.
    /// </para>
    /// </summary>
    public sealed class KubunoWin32Resources : MSBuildTask
    {
        /// <summary>
        /// The Cargo.toml whose [package] version / name / description / authors fill the version fields left
        /// empty (FileVersion+ProductVersion, ProductName, FileDescription, CompanyName). Optional.
        /// </summary>
        public string? CargoManifestPath { get; set; }

        /// <summary>
        /// The .res file to produce. A <c>{hash}</c> placeholder in the file name is replaced by a hash of the
        /// content, and older files of the same pattern are deleted: the SDK links the file by path, and cargo
        /// only relinks when its arguments change, so a new content must mean a new path.
        /// </summary>
        [Required]
        public string OutputFile { get; set; } = string.Empty;

        /// <summary>Optional .ico file to embed as the application icon (relative paths resolve against the project directory).</summary>
        public string? IconFile { get; set; }

        /// <summary>"Default" (generated manifest, the default), "Custom" (embed <see cref="CustomManifestFile"/> verbatim) or "None".</summary>
        public string? ManifestMode { get; set; }

        /// <summary>Manifest embedded verbatim when <see cref="ManifestMode"/> is "Custom".</summary>
        public string? CustomManifestFile { get; set; }

        /// <summary>"Unaware", "System", "PerMonitor" or "PerMonitorV2" (default).</summary>
        public string? DpiAwareness { get; set; }

        /// <summary>"AsInvoker" (default), "HighestAvailable" or "RequireAdministrator".</summary>
        public string? ExecutionLevel { get; set; }

        /// <summary>"10" (default), "8.1", "8" or "7".</summary>
        public string? MinimumWindowsVersion { get; set; }

        /// <summary>Declare long path awareness.</summary>
        public bool LongPathAware { get; set; }

        /// <summary>Declare the UTF-8 active code page.</summary>
        public bool UseUtf8CodePage { get; set; }

        /// <summary>Depend on Common Controls 6 (visual styles).</summary>
        public bool CommonControlsV6 { get; set; }

        /// <summary>File version (falls back to <see cref="ProductVersion"/> when empty).</summary>
        public string? FileVersion { get; set; }

        /// <summary>Product version (falls back to <see cref="FileVersion"/> when empty).</summary>
        public string? ProductVersion { get; set; }

        /// <summary>ProductName string.</summary>
        public string? ProductName { get; set; }

        /// <summary>CompanyName string.</summary>
        public string? CompanyName { get; set; }

        /// <summary>FileDescription string.</summary>
        public string? FileDescription { get; set; }

        /// <summary>LegalCopyright string.</summary>
        public string? Copyright { get; set; }

        /// <summary>LegalTrademarks string.</summary>
        public string? Trademarks { get; set; }

        /// <summary>Comments string.</summary>
        public string? Comments { get; set; }

        /// <summary>OriginalFilename string.</summary>
        public string? OriginalFilename { get; set; }

        /// <summary>InternalName string.</summary>
        public string? InternalName { get; set; }

        /// <summary>True when the target is a DLL.</summary>
        public bool IsDll { get; set; }

        /// <summary>Full path of the generated .res file.</summary>
        [Output]
        public string ResourceFile { get; set; } = string.Empty;

        /// <inheritdoc />
        public override bool Execute()
        {
            string mode = NonEmpty(ManifestMode) ?? "Default";
            bool modeDefault = string.Equals(mode, "Default", StringComparison.OrdinalIgnoreCase);
            bool modeCustom = string.Equals(mode, "Custom", StringComparison.OrdinalIgnoreCase);
            bool modeNone = string.Equals(mode, "None", StringComparison.OrdinalIgnoreCase);
            if (!modeDefault && !modeCustom && !modeNone)
            {
                Log.LogError(null, "KUBUNO0103", null, null, 0, 0, 0, 0, $"Invalid ManifestMode '{mode}'. Expected Default, Custom or None.");
                return false;
            }

            var manifestOptions = new Win32ManifestOptions
            {
                LongPathAware = LongPathAware,
                UseUtf8CodePage = UseUtf8CodePage,
                CommonControlsV6 = CommonControlsV6,
            };

            if (modeDefault)
            {
                string? dpi = NonEmpty(DpiAwareness);
                if (dpi != null)
                {
                    if (!TryParseEnum(dpi, out Win32DpiAwareness dpiValue))
                    {
                        Log.LogError(null, "KUBUNO0105", null, null, 0, 0, 0, 0, $"Invalid DpiAwareness '{dpi}'. Expected Unaware, System, PerMonitor or PerMonitorV2.");
                        return false;
                    }

                    manifestOptions.DpiAwareness = dpiValue;
                }

                string? level = NonEmpty(ExecutionLevel);
                if (level != null)
                {
                    if (!TryParseEnum(level, out Win32ExecutionLevel levelValue))
                    {
                        Log.LogError(null, "KUBUNO0106", null, null, 0, 0, 0, 0, $"Invalid ExecutionLevel '{level}'. Expected AsInvoker, HighestAvailable or RequireAdministrator.");
                        return false;
                    }

                    manifestOptions.ExecutionLevel = levelValue;
                }

                string? min = NonEmpty(MinimumWindowsVersion);
                if (min != null)
                {
                    switch (min)
                    {
                        case "10": manifestOptions.MinimumWindows = Win32MinimumWindows.Windows10; break;
                        case "8.1": manifestOptions.MinimumWindows = Win32MinimumWindows.Windows81; break;
                        case "8": manifestOptions.MinimumWindows = Win32MinimumWindows.Windows8; break;
                        case "7": manifestOptions.MinimumWindows = Win32MinimumWindows.Windows7; break;
                        default:
                            Log.LogError(null, "KUBUNO0107", null, null, 0, 0, 0, 0, $"Invalid MinimumWindowsVersion '{min}'. Expected 10, 8.1, 8 or 7.");
                            return false;
                    }
                }
            }

            var resources = new Win32ResourceFile();
            string embedded = string.Empty;

            string? iconFile = NonEmpty(IconFile);
            if (iconFile != null)
            {
                string iconPath = Resolve(iconFile);
                if (!File.Exists(iconPath))
                {
                    Log.LogError(null, "KUBUNO0101", null, iconPath, 0, 0, 0, 0, $"Icon file not found: {iconPath}");
                    return false;
                }

                try
                {
                    resources.AddIcon(1, File.ReadAllBytes(iconPath));
                }
                catch (InvalidDataException ex)
                {
                    Log.LogError(null, "KUBUNO0102", null, iconPath, 0, 0, 0, 0, $"Invalid icon file '{iconPath}': {ex.Message}");
                    return false;
                }
                catch (IOException ex)
                {
                    Log.LogError(null, "KUBUNO0101", null, iconPath, 0, 0, 0, 0, $"Cannot read icon file '{iconPath}': {ex.Message}");
                    return false;
                }

                embedded += "icon, ";
            }

            if (modeCustom)
            {
                string? custom = NonEmpty(CustomManifestFile);
                if (custom == null)
                {
                    Log.LogError(null, "KUBUNO0104", null, null, 0, 0, 0, 0, "ManifestMode is Custom but CustomManifestFile is not set.");
                    return false;
                }

                string customPath = Resolve(custom);
                if (!File.Exists(customPath))
                {
                    Log.LogError(null, "KUBUNO0104", null, customPath, 0, 0, 0, 0, $"Custom manifest file not found: {customPath}");
                    return false;
                }

                string xml;
                try
                {
                    xml = File.ReadAllText(customPath);
                    var doc = new XmlDocument();
                    doc.LoadXml(xml);
                }
                catch (Exception ex) when (ex is IOException || ex is XmlException || ex is UnauthorizedAccessException)
                {
                    Log.LogError(null, "KUBUNO0109", null, customPath, 0, 0, 0, 0, $"Cannot use custom manifest '{customPath}': {ex.Message}");
                    return false;
                }

                // ReadAllText strips a BOM; the payload must be BOM-free UTF-8, which AddManifest guarantees.
                resources.AddManifest(xml);
                embedded += "custom manifest, ";
            }
            else if (modeDefault)
            {
                resources.AddManifest(Win32ManifestBuilder.Build(manifestOptions));
                embedded += "generated manifest, ";
            }

            ApplyCargoDefaults();
            string? fileVersion = NonEmpty(FileVersion) ?? NonEmpty(ProductVersion);
            string? productVersion = NonEmpty(ProductVersion) ?? NonEmpty(FileVersion);
            resources.AddVersionInfo(new Win32VersionInfo
            {
                FileVersion = fileVersion,
                ProductVersion = productVersion,
                ProductName = NonEmpty(ProductName),
                CompanyName = NonEmpty(CompanyName),
                FileDescription = NonEmpty(FileDescription),
                LegalCopyright = NonEmpty(Copyright),
                LegalTrademarks = NonEmpty(Trademarks),
                Comments = NonEmpty(Comments),
                OriginalFilename = NonEmpty(OriginalFilename),
                InternalName = NonEmpty(InternalName),
                IsDll = IsDll,
            });
            embedded += "version info";

            byte[] bytes = resources.ToArray();
            string output;
            try
            {
                output = Path.GetFullPath(Resolve(OutputFile));
                output = ApplyHashPlaceholder(output, bytes);
                string? dir = Path.GetDirectoryName(output);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                bool same = File.Exists(output) && BytesEqual(File.ReadAllBytes(output), bytes);
                if (!same)
                {
                    File.WriteAllBytes(output, bytes);
                }

                Log.LogMessage(MessageImportance.Low, "Win32 resources ({0}) {1}: {2}", embedded, same ? "unchanged" : "written", output);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                Log.LogError(null, "KUBUNO0108", null, OutputFile, 0, 0, 0, 0, $"Cannot write the resource file '{OutputFile}': {ex.Message}");
                return false;
            }

            ResourceFile = output;
            return true;
        }

        /// <summary>Fills empty version fields from Cargo.toml's [package] (inherited workspace fields are skipped).</summary>
        private void ApplyCargoDefaults()
        {
            string? manifestPath = NonEmpty(CargoManifestPath);
            if (manifestPath is null)
            {
                return;
            }

            string path = Resolve(manifestPath);
            if (!File.Exists(path))
            {
                return;
            }

            TomlDocument document;
            try
            {
                document = TomlDocument.Parse(File.ReadAllText(path));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is TomlParseException)
            {
                Log.LogMessage(MessageImportance.Low, "Win32 resources: {0} not read ({1}); no version defaults.", path, ex.Message);
                return;
            }

            string? version = document.GetValue("package", "version")?.AsString();
            if (NonEmpty(FileVersion) is null && NonEmpty(ProductVersion) is null && version != null)
            {
                FileVersion = version;
                ProductVersion = version;
            }
            if (NonEmpty(ProductName) is null)
            {
                ProductName = document.GetValue("package", "name")?.AsString();
            }
            if (NonEmpty(FileDescription) is null)
            {
                FileDescription = document.GetValue("package", "description")?.AsString() ?? ProductName;
            }
            if (NonEmpty(CompanyName) is null)
            {
                IEnumerable<string> authors = document.GetValue("package", "authors")?.AsArray()?.Select(a => a.AsString()).Where(a => a != null).Select(a => StripEmail(a!)) ?? Enumerable.Empty<string>();
                string joined = string.Join(", ", authors);
                CompanyName = joined.Length == 0 ? null : joined;
            }
        }

        private static string StripEmail(string author)
        {
            int bracket = author.IndexOf('<');
            return (bracket > 0 ? author.Substring(0, bracket) : author).Trim();
        }

        private static string ApplyHashPlaceholder(string path, byte[] bytes)
        {
            string fileName = Path.GetFileName(path);
            if (fileName.IndexOf("{hash}", StringComparison.Ordinal) < 0)
            {
                return path;
            }

            string directory = Path.GetDirectoryName(path) ?? string.Empty;
            string hash;
            using (SHA256 sha = SHA256.Create())
            {
                hash = string.Concat(sha.ComputeHash(bytes).Take(6).Select(b => b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
            }
            string result = Path.Combine(directory, fileName.Replace("{hash}", hash));

            // Drop the files of previous contents (same pattern, other hash).
            if (Directory.Exists(directory))
            {
                string pattern = fileName.Replace("{hash}", "*");
                foreach (string stale in Directory.GetFiles(directory, pattern))
                {
                    if (!string.Equals(stale, result, StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            File.Delete(stale);
                        }
                        catch (IOException)
                        {
                        }
                        catch (UnauthorizedAccessException)
                        {
                        }
                    }
                }
            }
            return result;
        }

        private static string? NonEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s!.Trim();

        private static bool TryParseEnum<T>(string text, out T value) where T : struct
        {
            // Enum.TryParse also accepts numbers; only names are valid here.
            value = default;
            foreach (string name in Enum.GetNames(typeof(T)))
            {
                if (string.Equals(name, text, StringComparison.OrdinalIgnoreCase))
                {
                    return Enum.TryParse(name, out value);
                }
            }

            return false;
        }

        private string Resolve(string path)
        {
            if (Path.IsPathRooted(path))
            {
                return path;
            }

            string? projectDir = null;
            try
            {
                string? project = BuildEngine?.ProjectFileOfTaskNode;
                if (!string.IsNullOrEmpty(project))
                {
                    projectDir = Path.GetDirectoryName(Path.GetFullPath(project));
                }
            }
            catch (ArgumentException)
            {
            }

            return projectDir == null ? path : Path.Combine(projectDir, path);
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
