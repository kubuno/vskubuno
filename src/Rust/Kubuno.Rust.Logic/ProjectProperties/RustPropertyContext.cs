using System;
using System.Collections.Generic;
using System.IO;
using Kubuno.Cargo.Toml;

namespace Kubuno.VisualStudio.Core.ProjectProperties
{
    /// <summary>File access for the property model: the VS layer reads open, possibly unsaved documents first.</summary>
    public interface IPropertyFileReader
    {
        /// <summary>The file's current text, or null when it does not exist.</summary>
        string? ReadText(string path);

        /// <summary>Whether the file exists.</summary>
        bool FileExists(string path);

        /// <summary>Full paths of the files directly in <paramref name="directory"/> matching <paramref name="searchPattern"/> (empty when the folder is missing).</summary>
        IReadOnlyList<string> GetFiles(string directory, string searchPattern);
    }

    /// <summary>The plain file system, for tests and for reads outside Visual Studio.</summary>
    public sealed class FileSystemPropertyFileReader : IPropertyFileReader
    {
        public static FileSystemPropertyFileReader Instance { get; } = new FileSystemPropertyFileReader();

        public string? ReadText(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

        public bool FileExists(string path) => File.Exists(path);

        public IReadOnlyList<string> GetFiles(string directory, string searchPattern) =>
            Directory.Exists(directory) ? Directory.GetFiles(directory, searchPattern) : Array.Empty<string>();
    }

    /// <summary>
    /// Everything the property model needs to know about one configured <c>.rsproj</c>: its Cargo.toml, the
    /// configuration being edited (Debug/Release map to Cargo's dev/release profiles, like Kubuno.Rust.Sdk's
    /// <c>CargoProfile</c>), the binary it builds, and a file reader. Parsed documents are cached for the
    /// lifetime of the context (one property read or write).
    /// </summary>
    public sealed class RustPropertyContext
    {
        private readonly Dictionary<string, TomlDocument?> _documents = new Dictionary<string, TomlDocument?>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _parseErrors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private string? _workspaceRoot;
        private bool _workspaceRootResolved;

        public RustPropertyContext(string manifestPath, string configuration, string? binName, IPropertyFileReader files)
        {
            ManifestPath = Path.GetFullPath(manifestPath ?? throw new ArgumentNullException(nameof(manifestPath)));
            Configuration = configuration ?? string.Empty;
            BinName = string.IsNullOrWhiteSpace(binName) ? null : binName;
            Files = files ?? throw new ArgumentNullException(nameof(files));
        }

        public string ManifestPath { get; }

        public string ManifestDirectory => Path.GetDirectoryName(ManifestPath)!;

        /// <summary>The MSBuild configuration (Debug, Release, ...).</summary>
        public string Configuration { get; }

        /// <summary>The <c>CargoBin</c> of the project, when set.</summary>
        public string? BinName { get; }

        public IPropertyFileReader Files { get; }

        /// <summary>The Cargo profile of <see cref="Configuration"/>: Debug is "dev", Release is "release", any other name is itself, lower-cased.</summary>
        public string ProfileName => ProfileNameFor(Configuration);

        /// <summary>The package's own Cargo.toml, or null when missing or unparsable (see <see cref="GetParseError"/>).</summary>
        public TomlDocument? Manifest => GetDocument(ManifestPath);

        /// <summary>
        /// The Cargo.toml that holds [profile.*] for this package: the workspace root when the package is a
        /// member (Cargo ignores profiles declared in a member), else the package manifest itself.
        /// </summary>
        public string ProfileManifestPath => WorkspaceRootManifestPath ?? ManifestPath;

        /// <summary>The manifest of the enclosing workspace ([workspace] table), which may be the package manifest itself; null for a standalone package.</summary>
        public string? WorkspaceRootManifestPath
        {
            get
            {
                if (!_workspaceRootResolved)
                {
                    _workspaceRoot = FindWorkspaceRoot();
                    _workspaceRootResolved = true;
                }
                return _workspaceRoot;
            }
        }

        /// <summary>Cargo's profile name for an MSBuild configuration.</summary>
        public static string ProfileNameFor(string configuration)
        {
            if (string.Equals(configuration, "Debug", StringComparison.OrdinalIgnoreCase) || configuration.Length == 0)
            {
                return "dev";
            }
            return string.Equals(configuration, "Release", StringComparison.OrdinalIgnoreCase) ? "release" : configuration.ToLowerInvariant();
        }

        /// <summary>A parsed TOML file (cached), or null when it is missing or invalid.</summary>
        public TomlDocument? GetDocument(string path)
        {
            if (_documents.TryGetValue(path, out TomlDocument? cached))
            {
                return cached;
            }

            TomlDocument? document = null;
            string? text = Files.ReadText(path);
            if (text != null)
            {
                try
                {
                    document = TomlDocument.Parse(text);
                }
                catch (TomlParseException ex)
                {
                    _parseErrors[path] = $"{Path.GetFileName(path)}({ex.Line},{ex.Column}): {ex.Message}";
                }
            }
            _documents[path] = document;
            return document;
        }

        /// <summary>Why <see cref="GetDocument"/> returned null for an existing file, if it did.</summary>
        public string? GetParseError(string path) => _parseErrors.TryGetValue(path, out string? error) ? error : null;

        private string? FindWorkspaceRoot()
        {
            TomlDocument? manifest = Manifest;
            if (manifest != null && manifest.ContainsTable("workspace"))
            {
                return ManifestPath;
            }

            // An explicit [package] workspace = "path" wins over the directory walk, as in Cargo.
            string? explicitRoot = manifest?.GetValue("package", "workspace")?.AsString();
            if (!string.IsNullOrEmpty(explicitRoot))
            {
                string candidate = Path.GetFullPath(Path.Combine(ManifestDirectory, explicitRoot!, "Cargo.toml"));
                return Files.FileExists(candidate) ? candidate : null;
            }

            DirectoryInfo? directory = new DirectoryInfo(ManifestDirectory).Parent;
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "Cargo.toml");
                if (Files.FileExists(candidate) && GetDocument(candidate)?.ContainsTable("workspace") == true)
                {
                    return candidate;
                }
                directory = directory.Parent;
            }
            return null;
        }
    }
}
