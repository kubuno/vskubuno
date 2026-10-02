using System;
using System.Collections.Generic;

namespace Kubuno.Views.Logic
{
    /// <summary>
    /// The Kubuno framework's own crates, by name. The designer looks for an application's controls in its folder
    /// dependencies (a library of controls such as <c>kubuno-shell-controls = { path = "…" }</c>): the project's
    /// Toolbox tab (<c>ProjectComponentsFile.ApplicationDependencyCrates</c>) and the "out of date" watch of the design
    /// build (<c>DesignSourceWatch.WatchedDirectories</c>). The framework's crates are not such a library, so they are
    /// skipped - by this explicit list and never by a <c>kubuno</c> prefix: third-party and application crates are
    /// named <c>kubuno-…</c> too.
    /// </summary>
    /// <remarks>
    /// The same list is <c>FRAMEWORK_CRATES</c> of the Rust crate <c>kubuno-views-meta</c>
    /// (<c>desktop/windows/src/crates/kubuno-views-meta/src/framework.rs</c>), used by the <c>#[kubuno::view]</c>
    /// macro and the language server; <c>FrameworkCratesTests</c> reads that file and fails when the two differ.
    /// desktop/common's service crates (<c>kubuno-sync*</c>, <c>kubuno-account</c>, <c>kubuno-api-client</c>,
    /// <c>kubuno-secrets</c>, <c>kubuno-app-storage</c>, <c>kubuno-docs-core</c>) are not listed: they declare no
    /// control and are not a layer of the views framework.
    /// </remarks>
    public static class FrameworkCrates
    {
        /// <summary>The framework's crates (package names, <c>-</c> form). Compare with <see cref="Contains"/>.</summary>
        public static readonly IReadOnlyList<string> Names = new[]
        {
            "kubuno",
            "kubuno-ui",
            "kubuno-controls",
            "kubuno-views",
            "kubuno-views-macros",
            "kubuno-views-meta",
            "kubuno-views-model",
            "kubuno-views-syntax",
            "kubuno-views-ls",
            "kubuno-views-web",
            "kubuno-data",
            "kubuno-data-model",
            "kubuno-data-macros",
            "kubuno-data-tool",
            "kubuno-print",
            "kubuno-resources",
            "kubuno-resources-model",
            "kubuno-resources-macros",
            "kubuno-resources-tool",
            "kubuno-app-storage-components",
        };

        private static readonly HashSet<string> Set = new HashSet<string>(Names, StringComparer.Ordinal);

        /// <summary>
        /// Whether <paramref name="crate"/> (a package, dependency key or extern crate name; <c>-</c> and <c>_</c> are
        /// one character) is one of the framework's crates. <c>kubuno-shell-controls</c> is not.
        /// </summary>
        public static bool Contains(string? crate)
        {
            var name = (crate ?? string.Empty).Trim().Trim('"').Replace('_', '-');
            return Set.Contains(name);
        }
    }
}
