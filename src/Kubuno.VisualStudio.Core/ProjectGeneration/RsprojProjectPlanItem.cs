namespace Kubuno.VisualStudio.Core.ProjectGeneration
{
    /// <summary>One workspace member's generation outcome, from <see cref="RsprojGenerationPlanner.Plan"/>.</summary>
    public sealed class RsprojProjectPlanItem
    {
        /// <summary>The Cargo package name (also the <c>.rsproj</c>'s base file name).</summary>
        public string PackageName { get; }

        /// <summary>Full path of the <c>.rsproj</c> this member would get.</summary>
        public string ProjectPath { get; }

        /// <summary>Full path of the member's own <c>Cargo.toml</c>.</summary>
        public string ManifestPath { get; }

        public RsprojPlanAction Action { get; }

        /// <summary>
        /// The file content for <see cref="RsprojPlanAction.Create"/>; still populated for
        /// <see cref="RsprojPlanAction.SkipExisting"/> so a caller can show a diff/preview, but
        /// must never be written to disk in that case.
        /// </summary>
        public string Content { get; }

        /// <summary>True when the member has no <c>[[bin]]</c> target at all (a library-only project - only ever planned when <see cref="RsprojGenerationOptions.IncludeLibraryOnlyMembers"/> is set).</summary>
        public bool IsLibraryOnly { get; }

        public RsprojProjectPlanItem(
            string packageName,
            string projectPath,
            string manifestPath,
            RsprojPlanAction action,
            string content,
            bool isLibraryOnly)
        {
            PackageName = packageName;
            ProjectPath = projectPath;
            ManifestPath = manifestPath;
            Action = action;
            Content = content;
            IsLibraryOnly = isLibraryOnly;
        }
    }
}
