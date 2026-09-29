using System;

namespace Kubuno.VisualStudio
{
    /// <summary>GUIDs referenced from attributes (which require compile-time constants) and from code.</summary>
    internal static class PackageGuidStrings
    {
        public const string Package = "5f3a9b2e-9e0c-4f0a-9a7a-6f0f3c9c9d10";
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

        /// <summary>Dependencies node's own floating context menu (KubunoDependenciesNodeContextMenu in KubunoCommands.vsct).</summary>
        public const int KubunoDependenciesNodeContextMenu = 0x1025;

        public const int AddCargoDependencyFromNodeCommand = 0x010D;
        public const int RemoveCargoDependencyCommand = 0x010E;

        /// <summary>Tools menu entry for <see cref="Commands.RestartRustAnalyzerCommand"/>.</summary>
        public const int RestartRustAnalyzerCommand = 0x010F;
    }
}
