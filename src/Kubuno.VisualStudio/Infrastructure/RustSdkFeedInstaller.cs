using System;
using System.IO;
using Kubuno.VisualStudio.Core.ProjectGeneration;
using Kubuno.VisualStudio.Logging;

namespace Kubuno.VisualStudio.Infrastructure
{
    /// <summary>
    /// Registers this VSIX's bundled <c>Kubuno.Rust.Sdk</c> feed (docs/RSPROJ.md work package 5's
    /// SDK-distribution decision) as a NuGet package source in the developer's own NuGet.Config, so
    /// a generated <c>.rsproj</c>'s <c>Sdk="Kubuno.Rust.Sdk/…"</c> resolves on a machine that never
    /// ran this repository's own <c>pack-sdk.ps1</c>/<c>samples/NuGet.config</c> dance - the exact
    /// gap docs/RSPROJ.md §6 flagged as this design's main distribution risk. Mirrors the JavaScript
    /// project system's own model (its SDK is placed in the shared NuGet packages folder by VS
    /// Setup) adapted for a third-party VSIX, which cannot participate in VS Setup's own
    /// component/workload manifest system: the package ships as VSIX content instead
    /// (<c>tools\SdkFeed\Kubuno.Rust.Sdk.1.0.0.nupkg</c> - see Kubuno.VisualStudio.csproj's own
    /// comment), and this class points NuGet at that folder.
    ///
    /// The merge itself (<see cref="NuGetLocalFeedRegistration"/>) is surgical and idempotent -
    /// unit-tested (tests/Kubuno.VisualStudio.Tests/ProjectGeneration/NuGetLocalFeedRegistrationTests.cs)
    /// - and the underlying "local feed folder -&gt; NuGet restore -&gt; MSBuild SDK resolution"
    /// mechanism was verified live (docs/RSPROJ.md work package 5): a fresh, otherwise-empty NuGet
    /// package cache with a config containing *only* a source pointing at this feed folder restores
    /// and builds a <c>.rsproj</c> successfully. This class's own call into the developer's real
    /// NuGet.Config is intentionally not exercised by an automated test (it mutates shared,
    /// machine-wide state) - every failure is caught and logged rather than thrown, so it can never
    /// break package load.
    /// </summary>
    internal static class RustSdkFeedInstaller
    {
        public const string SourceName = "Kubuno.Rust.Sdk (bundled)";

        /// <param name="extensionInstallDirectory">
        /// <see cref="KubunoPackage"/>'s own <c>GetExtensionInstallDirectory()</c> - <see
        /// langword="null"/> (e.g. reflection failed) is a silent no-op, same as every other
        /// consumer of that value.
        /// </param>
        public static void EnsureRegistered(string? extensionInstallDirectory)
        {
            try
            {
                if (string.IsNullOrEmpty(extensionInstallDirectory))
                {
                    return;
                }

                var feedDirectory = Path.Combine(extensionInstallDirectory!, "tools", "SdkFeed");
                if (!Directory.Exists(feedDirectory))
                {
                    // Not shipped in this build (e.g. a component built without
                    // /p:KubunoRustSdkNupkgPath pointing at a real .nupkg) - nothing to register.
                    return;
                }

                var configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "NuGet",
                    "NuGet.Config");

                // Checked on every package load: skip even reading NuGet.Config when it has not changed
                // since this very feed folder was last found (or made) registered in it.
                var stampPath = StampPath();
                if (stampPath is not null && File.Exists(stampPath) && File.ReadAllText(stampPath) == Stamp(feedDirectory, configPath))
                {
                    return;
                }

                var existingContent = File.Exists(configPath) ? File.ReadAllText(configPath) : null;
                var (content, changed) = NuGetLocalFeedRegistration.Plan(existingContent, SourceName, feedDirectory);
                if (changed)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
                    File.WriteAllText(configPath, content);
                    KubunoLog.WriteLine($"Kubuno: registered the bundled Kubuno.Rust.Sdk NuGet feed ('{feedDirectory}') in '{configPath}'.");
                }

                if (stampPath is not null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(stampPath)!);
                    File.WriteAllText(stampPath, Stamp(feedDirectory, configPath));
                }
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException("Kubuno: could not register the bundled Kubuno.Rust.Sdk NuGet feed", exception);
            }
        }

        /// <summary>Where the last verified state is remembered (per user, outside NuGet.Config).</summary>
        private static string? StampPath()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return string.IsNullOrEmpty(localAppData) ? null : Path.Combine(localAppData, "Kubuno", "VisualStudio", "sdk-feed.stamp");
        }

        /// <summary>The feed folder plus NuGet.Config's size and time stamp: any edit of the file, or another install folder, invalidates it.</summary>
        private static string Stamp(string feedDirectory, string configPath)
        {
            var config = new FileInfo(configPath);
            return config.Exists
                ? $"{feedDirectory}|{config.Length}|{config.LastWriteTimeUtc.Ticks}"
                : $"{feedDirectory}|missing";
        }
    }
}
