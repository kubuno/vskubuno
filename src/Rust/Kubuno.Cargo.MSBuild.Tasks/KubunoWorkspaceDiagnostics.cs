using System;
using System.Globalization;
using System.Linq;
using Kubuno.Cargo.MSBuild.Tasks.Internal;
using Microsoft.Build.Framework;
using MSBuildTask = Microsoft.Build.Utilities.Task;

namespace Kubuno.Cargo.MSBuild.Tasks
{
    /// <summary>
    /// The per-project half of a workspace-wide build (<c>Kubuno.Rust.Sdk</c>'s <c>CargoBuildScope=Workspace</c>, docs/RSPROJ.md
    /// "Cargo workspaces"): the build itself ran once, in <c>KubunoCargoWorkspace.proj</c>, and handed every project the same
    /// executables and diagnostics. This task logs the diagnostics this project owns (<see cref="WorkspaceDiagnosticOwnership"/>),
    /// so that each reaches the Error List once, under its own project, picks this project's executable, and fails the
    /// project when one of its own files has an error or its executable could not be built.
    /// </summary>
    public sealed class KubunoWorkspaceDiagnostics : MSBuildTask
    {
        /// <summary>The <c>Diagnostics</c> output of <see cref="CargoBuild"/> (with <see cref="CargoBuild.DiagnosticsAsItems"/>).</summary>
        public ITaskItem[] Diagnostics { get; set; } = Array.Empty<ITaskItem>();

        /// <summary>The <c>Executables</c> output of <see cref="CargoBuild"/>.</summary>
        public ITaskItem[] Executables { get; set; } = Array.Empty<ITaskItem>();

        /// <summary>Whether the workspace build succeeded.</summary>
        public bool BuildSucceeded { get; set; }

        /// <summary>This project's file (<c>$(MSBuildProjectFullPath)</c>).</summary>
        [Required]
        public string ProjectFullPath { get; set; } = string.Empty;

        /// <summary>The workspace's root folder.</summary>
        [Required]
        public string WorkspaceRoot { get; set; } = string.Empty;

        /// <summary><c>$(CurrentSolutionConfigurationContents)</c>: the solution's projects (empty when a project is built alone).</summary>
        public string? SolutionConfigurationContents { get; set; }

        /// <summary>This project's <c>[[bin]]</c>, empty for a library.</summary>
        public string? BinName { get; set; }

        /// <summary>This project's executable, when the build produced it (or found it up to date).</summary>
        [Output]
        public string? ExecutablePath { get; set; }

        public override bool Execute()
        {
            var projects = WorkspaceDiagnosticOwnership.SolutionProjects(SolutionConfigurationContents, System.IO.Path.GetExtension(ProjectFullPath));

            foreach (var diagnostic in Diagnostics)
            {
                string file = diagnostic.GetMetadata("File");
                if (!WorkspaceDiagnosticOwnership.IsOwnedBy(file, ProjectFullPath, projects, WorkspaceRoot))
                {
                    continue;
                }

                int line = ParseInt(diagnostic.GetMetadata("Line"));
                int column = ParseInt(diagnostic.GetMetadata("Column"));
                string code = diagnostic.GetMetadata("Code");
                string message = diagnostic.GetMetadata("Message");
                if (diagnostic.GetMetadata("Severity") == "Error")
                {
                    Log.LogError(null, NullIfEmpty(code), null, NullIfEmpty(file), line, column, 0, 0, message);
                }
                else
                {
                    Log.LogWarning(null, NullIfEmpty(code), null, NullIfEmpty(file), line, column, 0, 0, message);
                }
            }

            if (!string.IsNullOrEmpty(BinName))
            {
                ExecutablePath = Executables
                    .Where(item => string.Equals(item.GetMetadata("BinName"), BinName, StringComparison.Ordinal))
                    .Select(item => item.ItemSpec)
                    .FirstOrDefault();

                if (ExecutablePath is null && !Log.HasLoggedErrors)
                {
                    Log.LogError(BuildSucceeded
                        ? $"cargo did not build the '{BinName}' executable (check its required-features in Cargo.toml)."
                        : $"'{BinName}' was not built: the Cargo workspace build failed in a crate it depends on. The errors are listed under the projects they belong to.");
                }
            }

            return !Log.HasLoggedErrors;
        }

        private static int ParseInt(string text) =>
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

        private static string? NullIfEmpty(string text) => string.IsNullOrEmpty(text) ? null : text;
    }
}
