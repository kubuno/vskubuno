using System;

namespace Kubuno.VisualStudio
{
    /// <summary>GUIDs referenced from attributes (which require compile-time constants) and from code.</summary>
    internal static class PackageGuidStrings
    {
        public const string Package = "5f3a9b2e-9e0c-4f0a-9a7a-6f0f3c9c9d10";

        /// <summary>The crate manager tool window (<see cref="CrateManager.CrateManagerToolWindow"/>).</summary>
        public const string CrateManagerToolWindow = "b7f0a3d2-6c1e-4f8b-9a4d-2e5c7b1f9a63";

        /// <summary>The Data Explorer tool window (<see cref="DataExplorer.DataExplorerToolWindow"/>, docs/DATA.md DATA-5).</summary>
        public const string DataExplorerToolWindow = "b71dac9b-900e-4d1b-98e3-af5a45f9f76f";

        /// <summary>The query windows of the Data Explorer (<see cref="DataExplorer.QueryToolWindow"/>); also their key binding scope.</summary>
        public const string QueryToolWindow = "48c1434c-7de1-43cb-b8c6-2e7ce28d72ed";

        /// <summary>The Data Sources tool window (<see cref="DataSources.DataSourcesToolWindow"/>, docs/DATA.md DATA-6).</summary>
        public const string DataSourcesToolWindow = "0f6b1c3e-8d2a-4a57-b6e1-3c9d52a7e4f8";
    }

    internal static class PackageGuids
    {
        public static readonly Guid KubunoOutputPane = new("8e4b2a2f-1a6d-4f6a-9a2e-5b7a8c9d0e1f");

        /// <summary>Command set for <c>KubunoCommands.vsct</c> (see <see cref="PackageIds"/> for the individual command IDs).</summary>
        public static readonly Guid KubunoCommandSet = new("5A67B8C7-E9FD-4917-B844-F103C0485DE1");

        /// <summary>
        /// UIContext for "the active project is a .rsproj" (<c>guidRustProjectUIContext</c> in
        /// <c>KubunoCommands.vsct</c>'s <c>VisibilityConstraints</c>), matching the
        /// <c>ProvideUIContextRuleAttribute</c> on <see cref="KubunoPackage"/>
        /// (<c>ActiveProjectCapability:RustProjectSystem</c>) - the extended "Ajouter" submenu's
        /// scoping mechanism.
        /// </summary>
        public const string RustProjectUIContextString = "2e69f0d4-848f-4fc0-a7a1-0fdaefec0f1d";

        public static readonly Guid RustProjectUIContext = new(RustProjectUIContextString);

        /// <summary>
        /// The only context <see cref="KubunoPackage"/> auto-loads on (a <c>ProvideUIContextRule</c>): a solution
        /// with a <c>.rsproj</c>, a Rust or <c>.kbview</c> editor, or an Open Folder Cargo workspace
        /// (<see cref="CargoFolderUIContextString"/>). Never active for a C#-only solution or the start window.
        /// </summary>
        public const string KubunoActivationUIContextString = "d0f63dbb-a2e8-47da-9211-127cf0f4c953";

        /// <summary>
        /// Set by <see cref="Workspace.CargoFolderActivation"/> while the Open Folder workspace is a Cargo one -
        /// UI context rules have no "the folder contains Cargo.toml" term.
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

    /// <summary>Command IDs inside <see cref="PackageGuids.KubunoCommandSet"/>, matching <c>KubunoCommands.vsct</c>.</summary>
    internal static class PackageIds
    {
        public const int DebugRustTestAtCursorCommand = 0x0100;

        /// <summary>Shows <see cref="Kubuno.VisualStudio.Designer.ToolWindows.OutlineToolWindow"/> (Kubuno.VisualStudio.Designer's own INTEGRATION.md §9).</summary>
        public const int ShowKubunoOutlineCommand = 0x0103;

        /// <summary>Tools menu entry for <see cref="Commands.GenerateRustProjectsCommand"/> (docs/RSPROJ.md work package 5).</summary>
        public const int GenerateRustProjectsCommand = 0x0104;

        /// <summary>Solution Explorer/Open Folder item context menu entry for <see cref="Commands.GenerateRustProjectsCommand"/>, shown only on a workspace-root Cargo.toml.</summary>
        public const int GenerateRustProjectsContextCommand = 0x0105;

        // Extended "Ajouter" submenu on a .rsproj project node (Commands\AddProjectItemCommands.cs,
        // Commands\AddProjectReferenceCommand.cs, Commands\AddCargoDependencyCommand.cs), scoped to
        // .rsproj only via guidRustProjectUIContext (KubunoCommands.vsct VisibilityConstraints).
        public const int AddKubunoViewCommand = 0x0106;
        public const int AddRustModuleCommand = 0x0107;
        public const int AddRustIntegrationTestCommand = 0x0108;
        public const int AddRustExampleCommand = 0x0109;
        public const int AddRustBinaryCommand = 0x010A;
        public const int AddProjectReferenceCommand = 0x010B;
        public const int AddCargoDependencyCommand = 0x010C;

        // docs/EVENTS.md EVT-7b: the control item templates in the same "Ajouter" submenu.
        public const int AddKubunoCustomControlCommand = 0x0130;
        public const int AddKubunoUserControlCommand = 0x0131;
        public const int AddKubunoInheritedControlCommand = 0x0132;
        public const int AddKubunoComponentCommand = 0x0133;

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

        /// <summary>Tools menu entry for <see cref="Commands.DialogGalleryCommand"/> (developer instances only).</summary>
        public const int DialogGalleryCommand = 0x0250;

        /// <summary>Debug &gt; Kubuno &gt; Paint debug entry for <see cref="Commands.PaintDebugCommand"/>.</summary>
        public const int PaintDebugCommand = 0x0260;

        // docs/DATA.md DATA-5: the Data Explorer, its toolbar and context menus, and the query windows.
        public const int ShowDataExplorerCommand = 0x0300;
        public const int DataAddConnectionCommand = 0x0301;
        public const int DataRefreshCommand = 0x0302;
        public const int DataNewQueryCommand = 0x0303;
        public const int DataDeleteCommand = 0x0304;
        public const int DataShowDataCommand = 0x0305;
        public const int DataCopyNameCommand = 0x0306;

        /// <summary>SELECT; INSERT, UPDATE, DELETE and CREATE follow (+1..+4, in <see cref="Core.Data.ScriptKind"/> order).</summary>
        public const int DataScriptSelectCommand = 0x0310;
        public const int QueryExecuteCommand = 0x0320;
        public const int QueryCancelCommand = 0x0321;
        public const int KubunoDataExplorerToolbar = 0x1080;
        public const int KubunoDataConnectionContextMenu = 0x1082;
        public const int KubunoDataTableContextMenu = 0x1085;
        public const int KubunoDataNodeContextMenu = 0x108E;
        public const int KubunoQueryToolbar = 0x108B;

        // docs/DATA.md DATA-7: the project node's "Database" submenu and the Migrations node's context menu
        // (Migrations\MigrationCommands.cs, Migrations\MigrationsTree.cs).
        public const int MigrationAddCommand = 0x0540;
        public const int MigrationRunCommand = 0x0541;
        public const int MigrationRevertCommand = 0x0542;
        public const int SqlxPrepareCommand = 0x0543;
        public const int MigrationRefreshCommand = 0x0544;
        public const int MigrationOpenCommand = 0x0545;
        public const int KubunoDatabaseProjectMenu = 0x10D0;
        public const int KubunoMigrationsContextMenu = 0x10D5;
        // docs/DATA.md DATA-6: the Data Sources window, its toolbar and its node menu (drop choices).
        public const int ShowDataSourcesCommand = 0x0340;
        public const int DataSourcesAddCommand = 0x0341;
        public const int DataSourcesConfigureCommand = 0x0342;
        public const int DataSourcesRefreshCommand = 0x0343;
        public const int DataSourcesEditFileCommand = 0x0344;
        public const int DataSourcesInsertCommand = 0x0345;
        public const int DataSourcesModeGridCommand = 0x0346;
        public const int DataSourcesModeDetailsCommand = 0x0347;

        /// <summary>TextField; NumericField, CheckBox, DatePicker, Label and None follow (+1..+5, in <see cref="Core.DataSources.DataControlKind"/> order).</summary>
        public const int DataSourcesControlTextFieldCommand = 0x0348;
        public const int KubunoDataSourcesToolbar = 0x10A0;
        public const int KubunoDataSourcesContextMenu = 0x10A2;
    }
}
