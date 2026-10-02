using System;
using Kubuno.Shared;

namespace Kubuno.Rust
{
    /// <summary>GUIDs of the Rust layer referenced from attributes (which require compile-time constants) and from code.</summary>
    public static class PackageGuidStrings
    {
        /// <summary>The Kubuno package (<see cref="KubunoGuids.PackageString"/>).</summary>
        public const string Package = KubunoGuids.PackageString;

        /// <summary>The crate manager tool window (<see cref="CrateManager.CrateManagerToolWindow"/>).</summary>
        public const string CrateManagerToolWindow = "b7f0a3d2-6c1e-4f8b-9a4d-2e5c7b1f9a63";
    }

    /// <summary>GUIDs of the Rust layer.</summary>
    public static class PackageGuids
    {
        /// <summary>Command set of <c>KubunoCommands.vsct</c> (<see cref="KubunoGuids.CommandSet"/>); <see cref="PackageIds"/> lists the Rust commands.</summary>
        public static readonly Guid KubunoCommandSet = KubunoGuids.CommandSet;

        /// <summary>
        /// UIContext for "the active project is a .rsproj" (<c>guidRustProjectUIContext</c> in
        /// <c>KubunoCommands.vsct</c>'s <c>VisibilityConstraints</c>), matching the package's
        /// <c>ProvideUIContextRuleAttribute</c> (<c>ActiveProjectCapability:RustProjectSystem</c>) - the extended
        /// "Ajouter" submenu's scoping mechanism.
        /// </summary>
        public const string RustProjectUIContextString = "2e69f0d4-848f-4fc0-a7a1-0fdaefec0f1d";

        public static readonly Guid RustProjectUIContext = new(RustProjectUIContextString);

        /// <summary>
        /// Set by <see cref="Workspace.CargoFolderActivation"/> while the Open Folder workspace is a Cargo one - UI context
        /// rules have no "the folder contains Cargo.toml" term. One of the terms of the package's activation rule.
        /// </summary>
        public const string CargoFolderUIContextString = "dbfeebb4-e0c3-46c7-a180-cc41fb041f9e";

        public static readonly Guid CargoFolderUIContext = new(CargoFolderUIContextString);

        /// <summary>
        /// <c>providerType</c> for <see cref="Workspace.CargoBuildFileContextProviderFactory"/>
        /// (an <c>ExportFileContextProviderAttribute</c> string, and the <c>FileContext.ProviderType</c>
        /// every <see cref="Workspace.CargoBuildFileContextProvider"/> it creates carries).
        /// </summary>
        public const string CargoBuildFileContextProviderType = "7D540E37-2734-4B7A-8EC7-DB9AB0E6A5FC";

        public static readonly Guid CargoBuildFileContextProviderTypeGuid = new(CargoBuildFileContextProviderType);

        /// <summary><c>providerType</c> for <see cref="Workspace.CargoBuildFileContextActionProviderFactory"/>.</summary>
        public const string CargoBuildFileContextActionProviderType = "728F1FB9-620C-4758-9F7F-03C1C225D748";
    }

    /// <summary>IDs of the commands the Rust layer handles, inside <see cref="PackageGuids.KubunoCommandSet"/>, matching <c>KubunoCommands.vsct</c>.</summary>
    public static class PackageIds
    {
        public const int DebugRustTestAtCursorCommand = 0x0100;

        /// <summary>Tools menu entry for <see cref="Commands.GenerateRustProjectsCommand"/> (docs/RSPROJ.md work package 5).</summary>
        public const int GenerateRustProjectsCommand = 0x0104;

        /// <summary>Solution Explorer/Open Folder item context menu entry for <see cref="Commands.GenerateRustProjectsCommand"/>, shown only on a workspace-root Cargo.toml.</summary>
        public const int GenerateRustProjectsContextCommand = 0x0105;

        // Extended "Ajouter" submenu on a .rsproj project node (Commands\AddProjectItemCommands.cs,
        // Commands\AddProjectReferenceCommand.cs, Commands\AddCargoDependencyCommand.cs), scoped to
        // .rsproj only via guidRustProjectUIContext (KubunoCommands.vsct VisibilityConstraints). The Kubuno view
        // and control entries of the same submenu belong to the desktop layer (Kubuno.Desktop.PackageIds).
        public const int AddRustModuleCommand = 0x0107;
        public const int AddRustIntegrationTestCommand = 0x0108;
        public const int AddRustExampleCommand = 0x0109;
        public const int AddRustBinaryCommand = 0x010A;
        public const int AddProjectReferenceCommand = 0x010B;
        public const int AddCargoDependencyCommand = 0x010C;

        /// <summary>Floating context menu of the Dependencies node and its category nodes (KubunoCommands.vsct).</summary>
        public const int KubunoDependenciesNodeContextMenu = 0x1025;

        /// <summary>Floating context menu of one dependency node (KubunoCommands.vsct).</summary>
        public const int KubunoDependencyItemContextMenu = 0x1027;

        // Commands of those two menus (SolutionExplorer\DependenciesNodeContextMenu.cs routes them).
        public const int RemoveCargoDependencyCommand = 0x010E;
        public const int DependenciesAddProjectReferenceCommand = 0x0110;
        public const int DependenciesManageCratesCommand = 0x0111;
        public const int DependenciesUpdateAllCommand = 0x0112;
        public const int DependenciesRemoveUnusedCommand = 0x0113;
        public const int DependencyOpenDocumentationCommand = 0x0114;
        public const int DependencyOpenSourceCommand = 0x0115;
        public const int DependencyUpdateCommand = 0x0116;
        public const int DependencyCopyPathCommand = 0x0117;
        public const int DependencyOpenFolderCommand = 0x0118;
        public const int DependencyManageCommand = 0x0119;

        /// <summary>Tools menu entry for <see cref="Commands.RestartRustAnalyzerCommand"/>.</summary>
        public const int RestartRustAnalyzerCommand = 0x010F;
    }
}
