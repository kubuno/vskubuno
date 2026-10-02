namespace Kubuno.Views.Designer.Selection
{
    /// <summary>
    /// The third participant of DSG-8's selection sync (docs/DESIGNER.md §6's package row: "Bidirectional
    /// selection/caret sync + Document Outline") - <see cref="Outline.OutlineViewModel"/> implements
    /// this so <see cref="SelectionSyncService"/> can highlight the tree node matching whatever the
    /// surface or the XML caret just selected, without this package (Selection/) depending on
    /// <c>Outline</c>'s WPF-facing view-model type directly (kept to the one member this seam actually
    /// needs, the same narrow-interface shape as <see cref="IDesignSurfaceSelectionTarget"/>). Optional -
    /// a <see cref="SelectionSyncService"/> constructed without an Outline tool window open simply passes
    /// <see langword="null"/> here (see that class's own constructor).
    /// </summary>
    public interface IOutlineSelectionTarget
    {
        /// <summary>Highlights the node for <paramref name="elementId"/> (<see langword="null"/> clears the highlight) - must NOT itself raise a "the user activated this node" notification back out, or a surface/XML-caret-originated selection would bounce back through the outline as a fresh gesture (the same loop-prevention rule <see cref="SelectionSyncService"/>'s own doc comment states for the other two views).</summary>
        void Select(string? elementId);
    }
}
