using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EnvDTE;
using Microsoft.VisualStudio.TemplateWizard;

namespace Kubuno.Rust.TemplateWizard
{
    /// <summary>
    /// "Create a new project" wizard shared by every Rust/Kubuno project template
    /// (<c>&lt;WizardExtension&gt;</c> in each .vstemplate). It does not generate or
    /// touch any file itself - it only ADDS three replacement tokens before the template
    /// content is expanded, computed from VS's own <c>$safeprojectname$</c>:
    ///
    /// - <c>$cratename$</c>: a Cargo-valid package name (crates.io/Cargo's own rule:
    ///   ASCII letters, digits, '-' and '_', must start with a letter) - what
    ///   Cargo.toml's <c>[package] name</c> and the .rsproj's <c>CargoPackage</c>
    ///   property use, wherever the plain, unsanitised $safeprojectname$ was used
    ///   before (docs/RSPROJ.md Addendum, lot 7's own "Crate-name casing, revisited").
    /// - <c>$moduleid$</c>: $cratename$ with every '-' turned into '_' - a valid
    ///   Postgres schema identifier / module.toml `id`, for the Kubuno Module template
    ///   (CLAUDE.md section 7: "Schéma &lt;module&gt; uniquement").
    /// - <c>$kubunodesktopsrc$</c>: the Kubuno desktop Cargo workspace (<c>desktop</c> of a
    ///   github.com/kubuno/core checkout; the <c>windows</c> folder of the former kubuno/desktop
    ///   repository) - see <see cref="ResolveDesktopSource"/>.
    /// - <c>$kubunodesktopcrates$</c>: the folder holding the framework's Windows crates
    ///   (<c>kubuno-desktop</c>, <c>kubuno-desktop-ui</c>…) the Kubuno Desktop Application template's path
    ///   dependencies point at: <c>core/desktop/windows</c>, or <c>windows/src/crates</c> of the former
    ///   desktop repository - see <see cref="ResolveDesktopCrates"/>.
    ///
    /// Deliberately minimal: no file generation, no Cargo invocation (cargo
    /// generate-lockfile was tried in the same lot 7 pass and reverted together with
    /// this wizard for unrelated reasons - see the addendum; out of scope here). Every
    /// other IWizard member is a no-op.
    /// </summary>
    public sealed class CrateNameWizard : IWizard
    {
        public void RunStarted(
            object automationObject,
            Dictionary<string, string> replacementsDictionary,
            WizardRunKind runKind,
            object[] customParams)
        {
            if (replacementsDictionary == null)
            {
                return;
            }

            string safeProjectName = replacementsDictionary.TryGetValue("$safeprojectname$", out string? raw)
                ? raw ?? string.Empty
                : string.Empty;

            if (runKind == WizardRunKind.AsNewProject && replacementsDictionary.TryGetValue("$solutiondirectory$", out var solutionDirectory) && !string.IsNullOrWhiteSpace(solutionDirectory))
            {
                _solutionDirectory = solutionDirectory;
            }

            string crateName = SanitizeCrateName(safeProjectName);
            replacementsDictionary["$cratename$"] = crateName;
            replacementsDictionary["$moduleid$"] = crateName.Replace('-', '_');
            var configuredDesktop = Environment.GetEnvironmentVariable(DesktopSourceVariable);
            replacementsDictionary["$kubunodesktopsrc$"] = ResolveDesktopSource(configuredDesktop, File.Exists);
            replacementsDictionary["$kubunodesktopcrates$"] = ResolveDesktopCrates(configuredDesktop, File.Exists);
        }

        /// <summary>The environment variable naming the Kubuno desktop checkout (docs/GETTING-STARTED.md).</summary>
        internal const string DesktopSourceVariable = "KUBUNO_DESKTOP_SRC";

        /// <summary>
        /// Where Kubuno Desktop lives when <see cref="DesktopSourceVariable"/> is not set: the core repository's
        /// <c>desktop</c> workspace (the kubuno/desktop repository was merged into the core on 2026-10-06), else a former
        /// kubuno/desktop checkout.
        /// </summary>
        internal const string DefaultDesktopSource = @"Z:\src\core\desktop";

        /// <summary>The former kubuno/desktop checkout's workspace, tried when <see cref="DefaultDesktopSource"/> has no framework.</summary>
        internal const string FormerDesktopSource = @"Z:\src\desktop\windows";

        /// <summary>
        /// The folder holding the framework's Windows crates (<c>kubuno-desktop</c>, <c>kubuno-desktop-ui</c>…) under
        /// <paramref name="configured"/> (<c>KUBUNO_DESKTOP_SRC</c>: a core checkout, its <c>desktop</c> or
        /// <c>desktop\windows</c> folder, or a former kubuno/desktop checkout or its <c>windows</c> folder), else under the
        /// defaults: <c>core/desktop/windows</c>, or the former <c>windows/src/crates</c>. When nothing is found, the core
        /// layout's folder of the configured (or default) path, so the generated project's build names it in its error.
        /// Forward slashes, no trailing separator.
        /// </summary>
        internal static string ResolveDesktopCrates(string? configured, Func<string, bool> fileExists)
        {
            var roots = string.IsNullOrWhiteSpace(configured)
                ? new[] { @"Z:\src\core", @"Z:\src\desktop" }
                : new[] { configured!.Trim().Trim('"').TrimEnd('\\', '/') };
            foreach (var root in roots)
            {
                foreach (var crates in new[]
                {
                    root,
                    Path.Combine(root, "windows"),
                    Path.Combine(root, "desktop", "windows"),
                    Path.Combine(root, "src", "crates"),
                    Path.Combine(root, "windows", "src", "crates"),
                })
                {
                    if (fileExists(Path.Combine(crates, "kubuno-desktop-ui", "Cargo.toml")) || fileExists(Path.Combine(crates, "kubuno-ui", "Cargo.toml")))
                    {
                        return crates.Replace('\\', '/');
                    }
                }
            }

            var fallback = roots[0];
            var name = Path.GetFileName(fallback);
            if (string.Equals(name, "windows", StringComparison.OrdinalIgnoreCase))
            {
                return fallback.Replace('\\', '/');
            }

            return (string.Equals(name, "desktop", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(fallback, "windows")
                : Path.Combine(fallback, "desktop", "windows")).Replace('\\', '/');
        }

        /// <summary>
        /// The desktop Cargo workspace a new Kubuno Desktop Application depends on, resolved once, at
        /// project creation: <c>KUBUNO_DESKTOP_SRC</c> when set (either the workspace itself or the
        /// repository root holding it in <c>windows\</c>), else <see cref="DefaultDesktopSource"/>.
        /// A value that does not contain <c>src\crates\kubuno-ui\Cargo.toml</c> is still returned as
        /// given: the generated project's build then stops on a clear error naming this path (the
        /// .rsproj's KubunoCheckDesktopSources target) rather than the wizard failing silently.
        /// Returned with forward slashes and no trailing separator (valid in Cargo.toml and MSBuild).
        /// </summary>
        internal static string ResolveDesktopSource(string? configured, Func<string, bool> fileExists)
        {
            string root = string.IsNullOrWhiteSpace(configured)
                ? (IsDesktopWorkspace(DefaultDesktopSource, fileExists) || !IsDesktopWorkspace(FormerDesktopSource, fileExists) ? DefaultDesktopSource : FormerDesktopSource)
                : configured!.Trim().Trim('"').TrimEnd('\\', '/');

            // A core checkout: its desktop workspace.
            if (!IsDesktopWorkspace(root, fileExists) && IsDesktopWorkspace(Path.Combine(root, "desktop"), fileExists))
            {
                root = Path.Combine(root, "desktop");
            }

            if (!IsDesktopWorkspace(root, fileExists) && IsDesktopWorkspace(Path.Combine(root, "windows"), fileExists))
            {
                root = Path.Combine(root, "windows");
            }

            return root.Replace('\\', '/');
        }

        private static bool IsDesktopWorkspace(string root, Func<string, bool> fileExists)
            // The core's desktop workspace (desktop/windows/<crate>).
            => fileExists(Path.Combine(root, "windows", "kubuno-desktop-ui", "Cargo.toml"))
                // The former desktop repository's windows workspace (src/crates/<crate>).
                || fileExists(Path.Combine(root, "src", "crates", "kubuno-desktop-ui", "Cargo.toml"))
                // A checkout older than the 2026-10 rename.
                || fileExists(Path.Combine(root, "src", "crates", "kubuno-ui", "Cargo.toml"));

        /// <summary>
        /// Lower-cases, replaces every character outside [a-z0-9_-] with '-', collapses
        /// runs of '-', trims leading/trailing '-'/'_', and - since Cargo requires a
        /// package name to start with an ASCII letter - prepends "app-" if the result
        /// would otherwise start with a digit (or be empty). "My App 2" -&gt; "my-app-2".
        /// </summary>
        internal static string SanitizeCrateName(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return "app";
            }

            var sb = new StringBuilder(input.Length);
            char previous = '\0';
            foreach (char c in input.ToLowerInvariant())
            {
                char mapped = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-'
                    ? c
                    : '-';
                if (mapped == '-' && previous == '-')
                {
                    continue; // collapse runs of '-'
                }

                sb.Append(mapped);
                previous = mapped;
            }

            string result = sb.ToString().Trim('-', '_');
            if (result.Length == 0)
            {
                return "app";
            }

            if (result[0] < 'a' || result[0] > 'z')
            {
                result = "app-" + result;
            }

            return result;
        }

        public void ProjectFinishedGenerating(Project project)
        {
        }

        public void ProjectItemFinishedGenerating(ProjectItem projectItem)
        {
        }

        public bool ShouldAddProjectItem(string filePath) => true;

        public void BeforeOpeningFile(ProjectItem projectItem)
        {
        }

        /// <summary>
        /// The new solution gets its own <c>Kubuno.Rust.Sdk</c> NuGet feed and the user's NuGet.Config gets the bundled one.
        /// This wizard runs whether or not the Kubuno package is loaded (a New Project dialog loads only this assembly), and
        /// the package would load too late for the new <c>.rsproj</c>'s <c>Sdk="Kubuno.Rust.Sdk/…"</c> to resolve on a fresh
        /// machine - see <c>SdkFeedDistribution</c>. Best-effort: never throws.
        /// </summary>
        public void RunFinished()
        {
            try
            {
                if (_solutionDirectory is null)
                {
                    return;
                }

                var extensionDirectory = Path.GetDirectoryName(typeof(CrateNameWizard).Assembly.Location);
                Kubuno.Rust.Logic.ProjectGeneration.SdkFeedDistribution.EnsureUserRegistered(extensionDirectory, _ => { });
                Kubuno.Rust.Logic.ProjectGeneration.SdkFeedDistribution.EnsureSolutionLocal(_solutionDirectory, extensionDirectory, _ => { });
            }
            catch (Exception)
            {
                // A template must never fail to create a project over an optional convenience.
            }
        }

        private string? _solutionDirectory;
    }
}
