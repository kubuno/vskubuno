using System;

namespace Kubuno.Shared.DevAssistant
{
    /// <summary>Identifiers of the Dev Assistant: tool window and its own command set (KubunoCommands.vsct, guidKubunoDevAssistantCmdSet).</summary>
    public static class DevAssistantGuids
    {
        public const string ToolWindowString = "6f1c2d7e-3b9a-4e51-9c8d-2a7b5e4f1d03";

        public const string CommandSetString = "b4e8f3a1-7c2d-4f6e-8a9b-1d3c5e7f9a20";

        public static readonly Guid CommandSet = new Guid(CommandSetString);

        /// <summary>View &gt; Other Windows &gt; Kubuno Dev Assistant.</summary>
        public const int ShowToolWindow = 0x0100;

        /// <summary>Tools &gt; Kubuno Dev Assistant settings.</summary>
        public const int ShowSettings = 0x0101;
    }
}
