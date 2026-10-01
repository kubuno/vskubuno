using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kubuno.Web.Logic.Node
{
    /// <summary>
    /// A frontend whose <c>node_modules</c> was installed by ANOTHER operating system (docs/WEB.md, "node_modules
    /// installed by Linux"): the Kubuno checkouts live on the Linux server and are opened from Windows through a share,
    /// so their <c>node_modules</c> holds the Linux builds of the native packages (rolldown, lightningcss, the Tailwind
    /// oxide scanner) and <c>.bin</c> entries that are Linux symbolic links. Running <c>npm install</c> from Windows
    /// would replace the Linux packages and break the server's own builds, so the tree is left untouched and Windows
    /// gets an OVERLAY instead:
    /// <list type="bullet">
    /// <item>the Windows counterparts of the native packages (<c>@rolldown/binding-win32-x64-msvc</c> for
    /// <c>@rolldown/binding-linux-x64-gnu</c>, same version) are installed into a local folder and found through
    /// <c>NODE_PATH</c>, which Node's <c>require</c> consults after the <c>node_modules</c> chain - the native loaders of
    /// these packages use <c>require</c>;</item>
    /// <item>a <c>.cmd</c> shim per <c>.bin</c> command (<c>vite.cmd</c>, <c>tsc.cmd</c>), generated from the packages'
    /// <c>bin</c> fields, put on PATH so <c>npm run build</c>'s scripts find them.</item>
    /// </list>
    /// </summary>
    public static class NodeModulesPlatform
    {
        /// <summary>The platform suffix of a native package for another system (napi-rs and esbuild naming).</summary>
        private static readonly Regex ForeignSuffix = new Regex(
            @"(?<os>linux|darwin|android|freebsd|openbsd|sunos)-(?<cpu>x64|arm64|arm|ia32|riscv64|ppc64|s390x|loong64)(?<abi>-(gnu|musl|gnueabihf|musleabihf|androideabi|msvc))?$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>Inspects <c>&lt;frontend&gt;\node_modules</c> (top level and scoped packages only).</summary>
        public static NodeModulesState Inspect(string frontendDirectory)
        {
            var nodeModules = Path.Combine(frontendDirectory, "node_modules");
            if (!Directory.Exists(nodeModules))
            {
                return new NodeModulesState(nodeModules, exists: false, Array.Empty<NativePackage>());
            }

            var names = PackageNames(nodeModules).ToList();
            var present = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            var missing = new Dictionary<string, NativePackage>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in names)
            {
                var counterpart = WindowsCounterpart(name);
                if (counterpart is null || present.Contains(counterpart) || missing.ContainsKey(counterpart))
                {
                    continue;
                }

                var version = ReadVersion(Path.Combine(nodeModules, name.Replace('/', Path.DirectorySeparatorChar), "package.json"));
                if (version is not null)
                {
                    missing[counterpart] = new NativePackage(counterpart, version, name);
                }
            }

            return new NodeModulesState(nodeModules, exists: true, missing.Values.OrderBy(package => package.Name, StringComparer.Ordinal).ToList());
        }

        /// <summary>
        /// The Windows x64 package standing for <paramref name="packageName"/>, or null when the name is not a native
        /// package of another platform: <c>@rolldown/binding-linux-x64-gnu</c> and <c>lightningcss-linux-x64-musl</c>
        /// give <c>…-win32-x64-msvc</c>; esbuild's ABI-less <c>@esbuild/linux-x64</c> gives <c>@esbuild/win32-x64</c>.
        /// </summary>
        public static string? WindowsCounterpart(string packageName)
        {
            var match = ForeignSuffix.Match(packageName);
            if (!match.Success)
            {
                return null;
            }

            var prefix = packageName.Substring(0, match.Index);
            return prefix + (match.Groups["abi"].Success ? "win32-x64-msvc" : "win32-x64");
        }

        /// <summary>
        /// The overlay folder for a set of packages: <c>&lt;base&gt;\&lt;hash&gt;</c>, the hash covering the sorted
        /// name@version list so two frontends needing the same packages share one overlay, and a version bump gets a new one.
        /// </summary>
        public static string OverlayDirectory(string baseDirectory, IEnumerable<NativePackage> packages)
        {
            var key = string.Join("\n", packages.Select(package => package.Name + "@" + package.Version).OrderBy(text => text, StringComparer.Ordinal));
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
            var hex = string.Concat(hash.Take(8).Select(b => b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
            return Path.Combine(baseDirectory, hex);
        }

        /// <summary>Default overlay base: <c>%LOCALAPPDATA%\Kubuno\node-overlay</c>.</summary>
        public static string DefaultOverlayBase()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "Kubuno", "node-overlay");
        }

        /// <summary>Whether <paramref name="package"/> is already installed in the overlay at that version.</summary>
        public static bool IsInstalled(string overlayDirectory, NativePackage package)
        {
            var packageJson = Path.Combine(overlayDirectory, "node_modules", package.Name.Replace('/', Path.DirectorySeparatorChar), "package.json");
            return string.Equals(ReadVersion(packageJson), package.Version, StringComparison.Ordinal);
        }

        /// <summary>
        /// The overlay's own <c>package.json</c>: every package as an OPTIONAL dependency, installed by one
        /// <c>npm install</c> (separate installs would prune each other) that tolerates a package without a Windows build.
        /// </summary>
        public static string OverlayPackageJson(IEnumerable<NativePackage> packages)
        {
            var builder = new StringBuilder("{\n  \"name\": \"kubuno-node-overlay\",\n  \"private\": true,\n  \"optionalDependencies\": {\n");
            var list = packages.OrderBy(package => package.Name, StringComparer.Ordinal).ToList();
            for (var index = 0; index < list.Count; index++)
            {
                builder.Append("    \"").Append(list[index].Name).Append("\": \"").Append(list[index].Version).Append('"').Append(index + 1 < list.Count ? ",\n" : "\n");
            }

            return builder.Append("  }\n}\n").ToString();
        }

        /// <summary>The npm arguments installing the overlay (run in the overlay folder; touches nothing else).</summary>
        public const string NpmInstallArguments = "install --no-package-lock --ignore-scripts --no-audit --no-fund --loglevel=error";

        /// <summary>
        /// The <c>.bin</c> commands of <paramref name="nodeModules"/> mapped to the script each one runs, read from the
        /// <c>bin</c> field of the top-level packages (a Linux <c>.bin</c> entry is a symbolic link, which a Windows
        /// share shows as a plain copy of its target, so the link itself says nothing). Restricted to the names
        /// present in <c>.bin</c> - what npm actually linked.
        /// </summary>
        public static IReadOnlyDictionary<string, string> BinCommands(string nodeModules)
        {
            var result = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var binDirectory = Path.Combine(nodeModules, ".bin");
            if (!Directory.Exists(binDirectory))
            {
                return result;
            }

            var linked = new HashSet<string>(
                Directory.GetFiles(binDirectory).Select(Path.GetFileName).Where(name => !string.IsNullOrEmpty(name) && Path.GetExtension(name) == string.Empty)!,
                StringComparer.OrdinalIgnoreCase);
            if (linked.Count == 0)
            {
                return result;
            }

            foreach (var name in PackageNames(nodeModules))
            {
                var packageDirectory = Path.Combine(nodeModules, name.Replace('/', Path.DirectorySeparatorChar));
                foreach (var pair in ReadBins(Path.Combine(packageDirectory, "package.json"), name))
                {
                    if (linked.Contains(pair.Key) && !result.ContainsKey(pair.Key))
                    {
                        result[pair.Key] = Path.GetFullPath(Path.Combine(packageDirectory, pair.Value.Replace('/', Path.DirectorySeparatorChar)));
                    }
                }
            }

            return result;
        }

        /// <summary>The text of a <c>.cmd</c> shim running <paramref name="script"/> with node.</summary>
        public static string ShimText(string script) => "@node \"" + script + "\" %*\r\n";

        /// <summary>
        /// The <c>cmd</c> prefix that gives a command the overlay: <c>set "PATH=..." &amp;&amp; set "NODE_PATH=..." &amp;&amp; </c>.
        /// </summary>
        public static string CommandPrefix(string? shimDirectory, string? nodeDirectory, string? nodePath)
        {
            var builder = new StringBuilder();
            var pathParts = new[] { shimDirectory, nodeDirectory }.Where(part => !string.IsNullOrEmpty(part)).ToList();
            if (pathParts.Count > 0)
            {
                builder.Append("set \"PATH=").Append(string.Join(";", pathParts)).Append(";%PATH%\" && ");
            }

            if (!string.IsNullOrEmpty(nodePath))
            {
                builder.Append("set \"NODE_PATH=").Append(nodePath).Append("\" && ");
            }

            return builder.ToString();
        }

        /// <summary>The folder of <c>node.exe</c> on <paramref name="path"/>, or null.</summary>
        public static string? FindOnPath(string executable, string? path)
        {
            foreach (var directory in (path ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    if (File.Exists(Path.Combine(directory.Trim().Trim('"'), executable)))
                    {
                        return directory.Trim().Trim('"');
                    }
                }
                catch (ArgumentException)
                {
                    // An invalid PATH entry.
                }
            }

            return null;
        }

        private static IEnumerable<string> PackageNames(string nodeModules)
        {
            foreach (var directory in Directory.GetDirectories(nodeModules))
            {
                var name = Path.GetFileName(directory);
                if (name.StartsWith(".", StringComparison.Ordinal))
                {
                    continue;
                }

                if (name.StartsWith("@", StringComparison.Ordinal))
                {
                    foreach (var scoped in Directory.GetDirectories(directory))
                    {
                        yield return name + "/" + Path.GetFileName(scoped);
                    }
                }
                else
                {
                    yield return name;
                }
            }
        }

        private static string? ReadVersion(string packageJson)
        {
            try
            {
                if (!File.Exists(packageJson))
                {
                    return null;
                }

                using var document = JsonDocument.Parse(File.ReadAllText(packageJson));
                return document.RootElement.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String ? version.GetString() : null;
            }
            catch (Exception exception) when (exception is JsonException || exception is IOException || exception is UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static IEnumerable<KeyValuePair<string, string>> ReadBins(string packageJson, string packageName)
        {
            JsonDocument document;
            try
            {
                if (!File.Exists(packageJson))
                {
                    yield break;
                }

                document = JsonDocument.Parse(File.ReadAllText(packageJson));
            }
            catch (Exception exception) when (exception is JsonException || exception is IOException || exception is UnauthorizedAccessException)
            {
                yield break;
            }

            using (document)
            {
                if (!document.RootElement.TryGetProperty("bin", out var bin))
                {
                    yield break;
                }

                if (bin.ValueKind == JsonValueKind.String)
                {
                    // "bin": "cli.js" names the command after the package (without its scope).
                    var command = packageName.Contains("/") ? packageName.Substring(packageName.IndexOf('/') + 1) : packageName;
                    yield return new KeyValuePair<string, string>(command, bin.GetString() ?? string.Empty);
                }
                else if (bin.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in bin.EnumerateObject())
                    {
                        if (property.Value.ValueKind == JsonValueKind.String)
                        {
                            yield return new KeyValuePair<string, string>(property.Name, property.Value.GetString() ?? string.Empty);
                        }
                    }
                }
            }
        }
    }

    /// <summary>What <see cref="NodeModulesPlatform.Inspect"/> found.</summary>
    public sealed class NodeModulesState
    {
        public NodeModulesState(string nodeModules, bool exists, IReadOnlyList<NativePackage> missingWindowsPackages)
        {
            NodeModules = nodeModules;
            Exists = exists;
            MissingWindowsPackages = missingWindowsPackages;
        }

        public string NodeModules { get; }

        public bool Exists { get; }

        /// <summary>The Windows native packages the tree lacks (empty for a tree installed on Windows).</summary>
        public IReadOnlyList<NativePackage> MissingWindowsPackages { get; }

        /// <summary>Installed by another operating system: leave it alone and use the overlay.</summary>
        public bool IsForeign => Exists && MissingWindowsPackages.Count > 0;
    }

    /// <summary>A Windows native package to install into the overlay.</summary>
    public sealed class NativePackage
    {
        public NativePackage(string name, string version, string foreignName)
        {
            Name = name;
            Version = version;
            ForeignName = foreignName;
        }

        public string Name { get; }

        public string Version { get; }

        /// <summary>The other platform's package it stands for.</summary>
        public string ForeignName { get; }
    }
}
