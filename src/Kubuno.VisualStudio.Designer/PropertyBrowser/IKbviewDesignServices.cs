using System.Collections.Generic;

namespace Kubuno.VisualStudio.Designer.PropertyBrowser
{
    /// <summary>
    /// The extra services the Properties window's rich editors need from the designer pane, beyond
    /// <see cref="IKbviewElementHost"/>: the view file (the image editor writes paths relative to it), the view model's
    /// bindable paths (the binding editor), and a text-level edit (the collection and list editors rewrite children as
    /// ONE undo unit). Optional: an <see cref="IKbviewElementHost"/> that does not implement it simply gets editors without
    /// these features. Implemented by <c>DesignSurface.DesignSurfaceEditingCoordinator</c>.
    /// </summary>
    public interface IKbviewDesignServices
    {
        /// <summary>The full path of the <c>.kbview</c> file being designed, or null when unknown.</summary>
        string? ViewFilePath { get; }

        /// <summary>
        /// The binding paths the view model offers (<c>kubuno/bindingPaths</c>: the names its <c>get</c> answers), for the
        /// binding editor's list. Synchronous with a short timeout; empty when the server cannot tell.
        /// </summary>
        IReadOnlyList<string> GetBindingPaths();

        /// <summary>
        /// Applies <paramref name="edits"/> (offsets into <see cref="IKbviewElementHost.GetCurrentText"/> at
        /// <paramref name="version"/>) as one undo unit named <paramref name="description"/>; nothing is applied when the buffer
        /// changed since that version.
        /// </summary>
        void ApplyTextEdits(int version, IReadOnlyList<TextReplacement> edits, string description);
    }
}
