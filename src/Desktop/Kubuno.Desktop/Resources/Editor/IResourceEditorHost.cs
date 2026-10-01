namespace Kubuno.Desktop.Resources.Editor
{
    /// <summary>
    /// What the resource editor view needs from whoever hosts it: the document pane in Visual Studio, or a stub in the
    /// dialog gallery. Keeps <see cref="ResourceEditorView"/> free of Visual Studio services.
    /// </summary>
    internal interface IResourceEditorHost
    {
        /// <summary>Opens a file in Visual Studio (its default editor: the image editor for a PNG...).</summary>
        void OpenFile(string path);

        /// <summary>Shows an error message (a message box).</summary>
        void ShowError(string message);

        /// <summary>Asks a yes/no question (a message box); true for yes.</summary>
        bool Confirm(string message);

        /// <summary>Switches to the XML code view of the document.</summary>
        void ViewCode();
    }
}
