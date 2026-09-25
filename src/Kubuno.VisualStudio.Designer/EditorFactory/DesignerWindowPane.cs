using Kubuno.VisualStudio.Designer.UI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TextManager.Interop;
using OleInterop = Microsoft.VisualStudio.OLE.Interop;

namespace Kubuno.VisualStudio.Designer.EditorFactory
{
    /// <summary>
    /// The <c>IVsWindowPane</c> (via the base <see cref="WindowPane"/>) shown for a <c>.kbview</c>
    /// document opened through <see cref="KbviewEditorFactory"/>. All of its actual content - the
    /// Design/XML/Split tab strip and both panes - lives in <see cref="DesignerSplitView"/>; this class
    /// is only the thin VS-hosting shell around it (site, caption/undo/frame plumbing is handled by the
    /// <see cref="WindowPane"/> base once the shell calls <c>SetSite</c>/shows the frame).
    /// </summary>
    public sealed class DesignerWindowPane : WindowPane
    {
        private readonly DesignerSplitView _view;

        public DesignerWindowPane(IVsTextLines textBuffer, OleInterop.IServiceProvider oleServiceProvider)
        {
            _view = new DesignerSplitView(textBuffer, oleServiceProvider);
            Content = _view;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _view.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
