namespace Kubuno.Views.Designer.Selection
{
    /// <summary>
    /// The host -&gt; surface half of docs/DESIGNER.md §9's DSG-6 protocol (<c>kubuno/select</c>) -
    /// deliberately a separate, tiny interface from <see cref="DesignSurface.IDesignSurfaceHost"/> rather
    /// than a member added to it: that interface (DSG-7's own seam, out of this package's scope to edit
    /// per this task's own brief) only ever reports a selection OUTWARD today
    /// (<see cref="DesignSurface.IDesignSurfaceHost.SelectionChanged"/>); <c>RustDesignSurfaceHost</c>
    /// (DSG-6/DSG-7, also out of scope here) already exposes the real <c>Select(string?)</c> method this
    /// interface abstracts - see <see cref="Infrastructure.DesignSurfaceSelectionTarget"/>, the thin
    /// adapter that forwards to it - so <see cref="SelectionSyncService"/> never depends on that concrete
    /// type directly and stays unit-testable with a fake
    /// (tests/Kubuno.Desktop.Tests/Designer/Selection/Fakes/FakeDesignSurfaceSelectionTarget.cs).
    /// </summary>
    public interface IDesignSurfaceSelectionTarget
    {
        /// <summary>Mirrors <c>kubuno_views::protocol::HostMessage::Select</c>'s own <c>id: string | null</c> field (docs/DESIGNER.md §9): <see langword="null"/> clears the surface's selection.</summary>
        void Select(string? elementId);
    }
}
