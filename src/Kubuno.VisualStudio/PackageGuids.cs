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
    }
}
