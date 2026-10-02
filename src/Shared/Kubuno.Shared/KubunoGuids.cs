using System;

namespace Kubuno.Shared
{
    /// <summary>
    /// The identifiers every layer shares: the package, the command set of <c>KubunoCommands.vsct</c> and the "Kubuno"
    /// Output pane. They are persisted by Visual Studio (window layouts, key bindings, settings), so they never change.
    /// </summary>
    public static class KubunoGuids
    {
        /// <summary>The Kubuno package (<c>KubunoPackage</c>, src/Kubuno.VisualStudio): owner of every registration.</summary>
        public const string PackageString = "5f3a9b2e-9e0c-4f0a-9a7a-6f0f3c9c9d10";

        public static readonly Guid Package = new(PackageString);

        /// <summary>The command set of <c>KubunoCommands.vsct</c>; each layer defines the IDs of the commands it handles.</summary>
        public const string CommandSetString = "5A67B8C7-E9FD-4917-B844-F103C0485DE1";

        public static readonly Guid CommandSet = new(CommandSetString);

        /// <summary>The "Kubuno" pane of the Output window (<see cref="Logging.KubunoLog"/>).</summary>
        public static readonly Guid OutputPane = new("8e4b2a2f-1a6d-4f6a-9a2e-5b7a8c9d0e1f");
    }

    /// <summary>Names shared by every layer.</summary>
    public static class KubunoConstants
    {
        /// <summary>Title of the "Kubuno" pane in the Output window.</summary>
        public const string OutputPaneTitle = "Kubuno";

        /// <summary>The Tools &gt; Options category of every Kubuno options page.</summary>
        public const string OptionsCategoryName = "Kubuno";
    }

    /// <summary>Command IDs the Shared layer handles, inside <see cref="KubunoGuids.CommandSet"/> (matching <c>KubunoCommands.vsct</c>).</summary>
    internal static class SharedCommandIds
    {
        /// <summary>Tools menu entry of the dialog gallery (developer instances only).</summary>
        public const int DialogGalleryCommand = 0x0250;
    }
}
