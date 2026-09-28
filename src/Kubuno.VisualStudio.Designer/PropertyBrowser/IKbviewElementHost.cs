using Kubuno.VisualStudio.Designer.Registry;

namespace Kubuno.VisualStudio.Designer.PropertyBrowser
{
    /// <summary>
    /// What a <see cref="KbviewElementObject"/> shown in Visual Studio's Properties window needs from the
    /// designer pane that published it: the live buffer text (values are always read from the text, the
    /// single source of truth - docs/DESIGNER.md §2) and the three surgical edits a property/event edit
    /// turns into. Implemented by <c>DesignSurface.DesignSurfaceEditingCoordinator</c> (every edit goes
    /// through <c>kubuno/applyEdit</c>/<c>kubuno/createHandler</c> and lands as one undo unit); faked in
    /// the unit tests.
    /// </summary>
    public interface IKbviewElementHost
    {
        ComponentRegistry Registry { get; }

        /// <summary>Monotonic buffer version - lets element objects cache their parse of <see cref="GetCurrentText"/>.</summary>
        int CurrentVersion { get; }

        string GetCurrentText();

        /// <summary>Sets (adds or replaces) attribute <paramref name="name"/> on element <paramref name="elementId"/>. Asynchronous: the buffer changes shortly after.</summary>
        void SetAttribute(string elementId, string name, string value);

        /// <summary>Removes attribute <paramref name="name"/> from element <paramref name="elementId"/> (Properties window "Reset").</summary>
        void RemoveAttribute(string elementId, string name);

        /// <summary>
        /// DSG-10: creates the handler for <paramref name="eventName"/> (sets the <c>On*</c> attribute and
        /// inserts the <c>fn</c> stub into the code-behind <c>.rs</c>), or navigates to the existing one when
        /// the attribute is already set. <paramref name="suggestedName"/> null lets the server pick its
        /// default name.
        /// </summary>
        void CreateOrShowHandler(string elementId, string eventName, string? suggestedName);

        /// <summary>Whether a <see cref="CreateOrShowHandler"/> request for this element/event was issued very recently (still in flight) - guards against the Properties window's own double navigation.</summary>
        bool IsHandlerRequestRecent(string elementId, string eventName);
    }
}
