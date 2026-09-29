using Kubuno.VisualStudio.Core.ProjectGeneration;
using Kubuno.VisualStudio.Logging;

namespace Kubuno.VisualStudio.Infrastructure
{
    /// <summary>
    /// Registers this VSIX's bundled <c>Kubuno.Rust.Sdk</c> feed (docs/RSPROJ.md work package 5's SDK-distribution
    /// decision) as a NuGet package source in the developer's own NuGet.Config, so a <c>.rsproj</c>'s
    /// <c>Sdk="Kubuno.Rust.Sdk/…"</c> resolves without running this repository's own <c>pack-sdk.ps1</c>. This is the
    /// package-load half of the story; the package-independent half (a feed that travels with the solution, written by the
    /// template wizard and the project generator) is <see cref="SdkFeedDistribution"/>, which holds the logic: the package
    /// loads too late for a project that is the first thing opened on a fresh machine (docs/RSPROJ.md, "SDK feed").
    /// Every failure is logged, never thrown, so it can never break package load.
    /// </summary>
    internal static class RustSdkFeedInstaller
    {
        public const string SourceName = SdkFeedDistribution.UserSourceName;

        /// <param name="extensionInstallDirectory">The package's own install directory; null is a silent no-op.</param>
        public static void EnsureRegistered(string? extensionInstallDirectory) =>
            SdkFeedDistribution.EnsureUserRegistered(extensionInstallDirectory, KubunoLog.WriteLine);
    }
}
