using System;
using System.Collections.Generic;
using System.Text;
using EnvDTE;
using Microsoft.VisualStudio.TemplateWizard;

namespace Kubuno.VisualStudio.TemplateWizard
{
    /// <summary>
    /// "Create a new project" wizard shared by every Rust/Kubuno project template
    /// (<c>&lt;WizardExtension&gt;</c> in each .vstemplate). It does not generate or
    /// touch any file itself - it only ADDS two replacement tokens before the template
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

            string crateName = SanitizeCrateName(safeProjectName);
            replacementsDictionary["$cratename$"] = crateName;
            replacementsDictionary["$moduleid$"] = crateName.Replace('-', '_');
        }

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

        public void RunFinished()
        {
        }
    }
}
