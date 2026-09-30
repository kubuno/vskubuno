using System;
using System.IO;
using System.Threading.Tasks;
using Kubuno.VisualStudio.Designer.EditorFactory;
using Kubuno.VisualStudio.Logging;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;

namespace Kubuno.VisualStudio.SolutionExplorer
{
    /// <summary>
    /// Double-click on a Solution Explorer symbol node (docs/RSPROJ.md lot 8): a Rust item opens its
    /// file with the caret on the item's name; a <c>.kbview</c> element opens (or brings up) the
    /// Kubuno View Designer and puts its XML caret on the element, which the designer's own selection
    /// sync turns into a selection on the design surface - the same path a click in the XML pane takes.
    /// </summary>
    internal static class SymbolNavigator
    {
        public static void Navigate(string path, int line, int column) =>
            NavigateSafeAsync(path, line, column).FileAndForget("Kubuno/SolutionExplorer/Navigate");

        private static async Task NavigateSafeAsync(string path, int line, int column)
        {
            try
            {
                await NavigateAsync(path, line, column);
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException($"Solution Explorer: navigating to {path}({line + 1},{column + 1})", exception);
            }
        }

        private static async Task NavigateAsync(string path, int line, int column)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var serviceProvider = ServiceProvider.GlobalProvider;
            bool isView = string.Equals(Path.GetExtension(path), ".kbview", StringComparison.OrdinalIgnoreCase);

            if (!VsShellUtilities.IsDocumentOpen(serviceProvider, path, Guid.Empty, out _, out _, out IVsWindowFrame? frame) || frame == null)
            {
                // A view element opens in the designer (the "select in designer" gesture); Rust code
                // in the default editor.
                var logicalView = isView ? VSConstants.LOGVIEWID.Designer_guid : VSConstants.LOGVIEWID.Primary_guid;
                VsShellUtilities.OpenDocument(serviceProvider, path, logicalView, out _, out _, out frame);
            }

            if (frame == null)
            {
                return;
            }

            frame.Show();

            // The designer's XML pane (a hosted code window) is created once the frame is laid out.
            for (int attempt = 0; attempt < 40; attempt++)
            {
                var view = GetTextView(frame);
                if (view != null)
                {
                    view.SetCaretPos(line, column);
                    view.CenterLines(line, 1);
                    return;
                }

                await Task.Delay(50);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            }
        }

        private static IVsTextView? GetTextView(IVsWindowFrame frame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out var docView) == VSConstants.S_OK
                && docView is DesignerWindowPane designer)
            {
                return designer.XmlTextView;
            }

            return VsShellUtilities.GetTextView(frame);
        }
    }
}
