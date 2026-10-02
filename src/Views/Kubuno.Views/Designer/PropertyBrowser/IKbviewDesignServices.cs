using System.Collections.Generic;

namespace Kubuno.Views.Designer.PropertyBrowser
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
        /// What the bindings of element <paramref name="elementId"/> can name and their problems (<c>kubuno/bindingSources</c>,
        /// docs/DESIGNER.md "Data bindings"): cached per buffer version; <see cref="Bindings.BindingSourceSchema.Empty"/> when the
        /// server cannot tell.
        /// </summary>
        Bindings.BindingSourceSchema GetBindingSources(string elementId);

        /// <summary>What a property of shape <paramref name="want"/> shows for <paramref name="sample"/> through <paramref name="expression"/> (<c>kubuno/bindingPreview</c>: the runtime's own converters and formats).</summary>
        Bindings.BindingPreview PreviewBinding(string expression, string? sample, Bindings.BindingShape sampleShape, Bindings.BindingShape? want);

        /// <summary>Opens the Rust member (else the data component, the converter) the binding of <paramref name="attribute"/> names; false when there is none.</summary>
        bool GoToBindingDefinition(string elementId, string attribute);

        /// <summary>
        /// Applies <paramref name="edits"/> (offsets into <see cref="IKbviewElementHost.GetCurrentText"/> at
        /// <paramref name="version"/>) as one undo unit named <paramref name="description"/>; nothing is applied when the buffer
        /// changed since that version.
        /// </summary>
        void ApplyTextEdits(int version, IReadOnlyList<TextReplacement> edits, string description);
    }
}
