using Microsoft.VisualStudio;
using Microsoft.VisualStudio.TextManager.Interop;

namespace Kubuno.VisualStudio.Designer.UI
{
    /// <summary>
    /// Reads the whole current contents of an <c>IVsTextLines</c> buffer, the standard
    /// <c>GetLastLineIndex</c> + <c>GetLineText</c> pair VS's own editors use for a one-shot full-text
    /// read (there is no single "get all text" method on the interface). Used once, when the designer
    /// pane opens, to give <see cref="DesignSurface.IDesignSurfaceHost.SetDocumentText"/> its initial
    /// contents (see <see cref="DesignerSplitView"/>); ongoing, debounced pushes on every edit are
    /// DSG-6/DSG-8/DSG-9's concern (docs/DESIGNER.md §2), not this one-shot helper's.
    /// </summary>
    internal static class VsTextLinesText
    {
        internal static string ReadAll(IVsTextLines buffer)
        {
            ErrorHandler.ThrowOnFailure(buffer.GetLastLineIndex(out var lastLine, out var lastIndex));
            ErrorHandler.ThrowOnFailure(buffer.GetLineText(0, 0, lastLine, lastIndex, out var text));
            return text ?? string.Empty;
        }
    }
}
