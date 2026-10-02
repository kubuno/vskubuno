using System;
using Kubuno.Shared;

namespace Kubuno.Desktop
{
    /// <summary>GUIDs of the desktop layer referenced from attributes (which require compile-time constants) and from code.</summary>
    public static class PackageGuidStrings
    {
        /// <summary>The Data Explorer tool window (<see cref="DataExplorer.DataExplorerToolWindow"/>, docs/DATA.md DATA-5).</summary>
        public const string DataExplorerToolWindow = "b71dac9b-900e-4d1b-98e3-af5a45f9f76f";

        /// <summary>The query windows of the Data Explorer (<see cref="DataExplorer.QueryToolWindow"/>); also their key binding scope.</summary>
        public const string QueryToolWindow = "48c1434c-7de1-43cb-b8c6-2e7ce28d72ed";

        /// <summary>The Data Sources tool window (<see cref="DataSources.DataSourcesToolWindow"/>, docs/DATA.md DATA-6).</summary>
        public const string DataSourcesToolWindow = "0f6b1c3e-8d2a-4a57-b6e1-3c9d52a7e4f8";
    }

    /// <summary>GUIDs of the desktop layer.</summary>
    public static class PackageGuids
    {
        /// <summary>Command set of <c>KubunoCommands.vsct</c> (<see cref="KubunoGuids.CommandSet"/>); <see cref="PackageIds"/> lists the desktop commands.</summary>
        public static readonly Guid KubunoCommandSet = KubunoGuids.CommandSet;
    }

    /// <summary>IDs of the commands the desktop layer handles, inside <see cref="PackageGuids.KubunoCommandSet"/>, matching <c>KubunoCommands.vsct</c>.</summary>
    public static class PackageIds
    {
        // The Kubuno view and control entries of the extended "Ajouter" submenu on a .rsproj project node
        // (docs/EVENTS.md EVT-7b), added to the Rust layer's item commands by DesktopLayer.
        public const int AddKubunoViewCommand = 0x0106;
        public const int AddKubunoCustomControlCommand = 0x0130;
        public const int AddKubunoUserControlCommand = 0x0131;
        public const int AddKubunoInheritedControlCommand = 0x0132;
        public const int AddKubunoComponentCommand = 0x0133;

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

        /// <summary>SELECT; INSERT, UPDATE, DELETE and CREATE follow (+1..+4, in <see cref="Logic.Data.ScriptKind"/> order).</summary>
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

        /// <summary>TextField; NumericField, CheckBox, DatePicker, Label and None follow (+1..+5, in <see cref="Logic.DataSources.DataControlKind"/> order).</summary>
        public const int DataSourcesControlTextFieldCommand = 0x0348;
        public const int KubunoDataSourcesToolbar = 0x10A0;
        public const int KubunoDataSourcesContextMenu = 0x10A2;
    }
}
