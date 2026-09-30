namespace Kubuno.VisualStudio.Core.SolutionExplorer
{
    /// <summary>
    /// <c>KnownMonikers</c> names of the Dependencies tree, the ones Visual Studio's .NET project system
    /// uses for the matching groups (read from its assemblies): <c>ReferenceGroup</c> for the root,
    /// <c>CodeInformation</c> for analyzers (here procedural macros), <c>Framework</c> for frameworks
    /// (the toolchain), <c>PackageReference</c>/<c>NuGetNoColor</c> for packages (crates),
    /// <c>Application</c> for project references, each with its <c>…Warning</c> variant for an
    /// unresolved dependency (the yellow triangle). Names, not monikers, so that a unit test can check
    /// them against the installed image catalog without a Visual Studio host.
    /// </summary>
    public static class DependencyMonikerNames
    {
        public const string Root = "ReferenceGroup";
        public const string RootWarning = "ReferenceGroupWarning";
        public const string RootError = "ReferenceGroupError";
        public const string Diagnostic = "StatusError";
        public const string UpdateOverlay = "StatusUpdateAvailable";

        public static string Group(DependencyCategory category) => category switch
        {
            DependencyCategory.ProcMacros => "CodeInformation",
            DependencyCategory.Toolchain => "Framework",
            DependencyCategory.Projects => "Application",
            DependencyCategory.Git => "GitRepository",
            _ => "PackageReference",
        };

        public static string Item(DependencyItem item)
        {
            var warning = item.State == DependencyState.Unresolved || item.IsYanked;
            switch (item.ItemKind)
            {
                case DependencyItemKind.Toolchain:
                    return "Framework";
                case DependencyItemKind.SysrootCrate:
                    return "Library";
            }

            switch (item.Category)
            {
                case DependencyCategory.ProcMacros:
                    return warning ? "CodeInformationWarning" : "CodeInformation";
                case DependencyCategory.Projects:
                    return warning ? "ApplicationWarning" : "Application";
                case DependencyCategory.Git:
                    return warning ? "StatusWarning" : "GitNoColor";
                default:
                    return warning ? "NuGetNoColorWarning" : "NuGetNoColor";
            }
        }
    }
}
