using System;
using System.IO;
using System.Linq;

namespace Kubuno.Rust.Logic.ProjectGeneration
{
    /// <summary>
    /// How a <c>.rsproj</c>'s <c>Sdk="Kubuno.Rust.Sdk/…"</c> is found without any Kubuno package having been loaded.
    /// Visual Studio evaluates a project (and resolves its SDK through NuGet) <i>before</i> our package could load - the
    /// package only loads once a project with the <c>RustProjectSystem</c> capability exists - so registering the bundled
    /// feed from the package alone leaves a hole on a fresh machine. Two package-independent routes close it:
    /// <list type="number">
    /// <item><b>The project travels with its SDK.</b> <see cref="EnsureSolutionLocal"/> copies the bundled
    /// <c>Kubuno.Rust.Sdk.*.nupkg</c> into <c>&lt;solution&gt;\.kubuno\sdk-feed</c> and adds a relative source for it to
    /// the <c>NuGet.Config</c> next to the solution. NuGet's SDK resolver reads that file from the solution/project
    /// folder, so a checkout opens on any machine that has Visual Studio, with nothing else installed or loaded. Written
    /// when a project is created (the template wizard runs without the package) and by "Generate Rust projects".</item>
    /// <item><b>The user's NuGet.Config.</b> <see cref="EnsureUserRegistered"/> adds the extension's own feed folder to
    /// <c>%APPDATA%\NuGet\NuGet.Config</c>, for projects that do not carry their own feed (hand-written ones). Run by the
    /// package when it loads and by the template wizard.</item>
    /// </list>
    /// Everything is best-effort and never throws: a failure is reported through <c>log</c> only.
    /// </summary>
#if KUBUNO_SHARED_AS_SOURCE
    internal static class SdkFeedDistribution
#else
    public static class SdkFeedDistribution
#endif
    {
        public const string UserSourceName = "Kubuno.Rust.Sdk (bundled)";

        public const string SolutionSourceName = "Kubuno.Rust.Sdk (solution)";

        /// <summary>The solution-relative folder holding the copied package (forward slashes: valid in a NuGet.Config).</summary>
        public const string SolutionFeedFolder = ".kubuno/sdk-feed";

        /// <summary>The <c>tools\SdkFeed</c> folder of the extension, or null when it is not shipped or unknown.</summary>
        public static string? BundledFeedDirectory(string? extensionInstallDirectory)
        {
            if (string.IsNullOrEmpty(extensionInstallDirectory))
            {
                return null;
            }

            var directory = Path.Combine(extensionInstallDirectory!, "tools", "SdkFeed");
            return Directory.Exists(directory) ? directory : null;
        }

        /// <summary>Registers the extension's feed folder in the user's NuGet.Config (cached by a stamp file). Returns whether a source is registered.</summary>
        public static bool EnsureUserRegistered(string? extensionInstallDirectory, Action<string> log, string? userNuGetConfigPath = null, string? stampPath = null)
        {
            try
            {
                var feedDirectory = BundledFeedDirectory(extensionInstallDirectory);
                if (feedDirectory is null)
                {
                    // Not shipped in this build (no /p:KubunoRustSdkNupkgPath) - nothing to register.
                    return false;
                }

                var configPath = userNuGetConfigPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NuGet", "NuGet.Config");
                stampPath ??= DefaultStampPath();
                if (stampPath is not null && File.Exists(stampPath) && File.ReadAllText(stampPath) == Stamp(feedDirectory, configPath))
                {
                    return true;
                }

                var existing = File.Exists(configPath) ? File.ReadAllText(configPath) : null;

                // The user's NuGet.Config is shared by every Visual Studio instance of the machine. An experimental
                // instance (devenv /rootsuffix X, an extension developer's or a test hive) must not repoint the source
                // that the regular installation registered to its own, short-lived copy: that would break the regular
                // Visual Studio's SDK resolution as soon as the experimental hive is reset. It only fills a gap (no
                // entry, or one pointing at a feed that no longer exists); a solution's own feed (EnsureSolutionLocal)
                // is what carries a newer SDK to it.
                var registered = NuGetLocalFeedRegistration.GetSourceValue(existing, UserSourceName);
                if (registered is not null
                    && !string.Equals(registered, feedDirectory, StringComparison.OrdinalIgnoreCase)
                    && IsExperimentalHiveDirectory(extensionInstallDirectory)
                    && IsUsableFeed(registered))
                {
                    log($"Kubuno: not repointing the '{UserSourceName}' NuGet source from '{registered}' to this experimental instance's feed ('{feedDirectory}').");
                    if (stampPath is not null)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(stampPath)!);
                        File.WriteAllText(stampPath, Stamp(feedDirectory, configPath));
                    }

                    return true;
                }

                var (content, changed) = NuGetLocalFeedRegistration.Plan(existing, UserSourceName, feedDirectory);
                if (changed)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
                    File.WriteAllText(configPath, content);
                    log($"Kubuno: registered the bundled Kubuno.Rust.Sdk NuGet feed ('{feedDirectory}') in '{configPath}'.");
                }

                if (stampPath is not null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(stampPath)!);
                    File.WriteAllText(stampPath, Stamp(feedDirectory, configPath));
                }

                return true;
            }
            catch (Exception exception)
            {
                log("Kubuno: could not register the bundled Kubuno.Rust.Sdk NuGet feed: " + exception.Message);
                return false;
            }
        }

        /// <summary>
        /// Gives <paramref name="solutionDirectory"/> its own copy of the SDK package and a <c>NuGet.Config</c> source for it
        /// (see the class remarks). Idempotent; an existing NuGet.Config is merged into, never replaced. Returns whether the
        /// solution now carries its feed.
        /// </summary>
        public static bool EnsureSolutionLocal(string solutionDirectory, string? extensionInstallDirectory, Action<string> log)
        {
            try
            {
                var bundled = BundledFeedDirectory(extensionInstallDirectory);
                if (bundled is null || string.IsNullOrEmpty(solutionDirectory))
                {
                    return false;
                }

                // Every MSBuild SDK the extension ships (Kubuno.Rust.Sdk, and the target layers' additional SDKs such as
                // Kubuno.Web.Sdk): a project names its SDKs, the feed folder carries them all.
                var packages = Directory.GetFiles(bundled, "Kubuno.*Sdk.*.nupkg");
                if (packages.Length == 0)
                {
                    return false;
                }

                var feed = Path.Combine(solutionDirectory, SolutionFeedFolder.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(feed);
                foreach (var package in packages)
                {
                    var target = Path.Combine(feed, Path.GetFileName(package));
                    if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(package).Length)
                    {
                        File.Copy(package, target, overwrite: true);
                    }
                }

                // NuGet accepts NuGet.Config, nuget.config and NuGet.config: merge into whichever spelling exists.
                var configPath = Directory.GetFiles(solutionDirectory, "nuget.config").FirstOrDefault() ?? Path.Combine(solutionDirectory, "NuGet.Config");
                var existing = File.Exists(configPath) ? File.ReadAllText(configPath) : null;
                var (content, changed) = NuGetLocalFeedRegistration.Plan(existing, SolutionSourceName, SolutionFeedFolder);
                if (changed)
                {
                    File.WriteAllText(configPath, content);
                    log($"Kubuno: the solution at '{solutionDirectory}' now carries its Kubuno.Rust.Sdk feed ('{SolutionFeedFolder}', NuGet.Config).");
                }

                return true;
            }
            catch (Exception exception)
            {
                log("Kubuno: could not give the solution its Kubuno.Rust.Sdk feed: " + exception.Message);
                return false;
            }
        }

        /// <summary>
        /// Whether an extension folder belongs to a Visual Studio hive with a root suffix (<c>/rootsuffix Exp</c>): its
        /// hive folder is <c>&lt;version&gt;_&lt;instance id&gt;&lt;suffix&gt;</c> (<c>18.0_dc9e2338Exp</c>), where the regular
        /// hive has no suffix (<c>18.0_dc9e2338</c>).
        /// </summary>
        public static bool IsExperimentalHiveDirectory(string? extensionInstallDirectory)
        {
            if (string.IsNullOrEmpty(extensionInstallDirectory))
            {
                return false;
            }

            foreach (var segment in extensionInstallDirectory!.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            {
                var match = System.Text.RegularExpressions.Regex.Match(segment, @"^\d+\.\d+_[0-9A-Fa-f]{8}(?<suffix>.*)$");
                if (match.Success)
                {
                    return match.Groups["suffix"].Value.Length > 0;
                }
            }

            return false;
        }

        private static bool IsUsableFeed(string directory)
        {
            try
            {
                return Directory.Exists(directory) && Directory.GetFiles(directory, "Kubuno.Rust.Sdk.*.nupkg").Length > 0;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return false;
            }
        }

        private static string? DefaultStampPath()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return string.IsNullOrEmpty(localAppData) ? null : Path.Combine(localAppData, "Kubuno", "VisualStudio", "sdk-feed.stamp");
        }

        /// <summary>The feed folder plus NuGet.Config's size and time stamp: any edit of the file, or another install folder, invalidates it.</summary>
        private static string Stamp(string feedDirectory, string configPath)
        {
            var config = new FileInfo(configPath);
            return config.Exists ? $"{feedDirectory}|{config.Length}|{config.LastWriteTimeUtc.Ticks}" : $"{feedDirectory}|missing";
        }
    }
}
