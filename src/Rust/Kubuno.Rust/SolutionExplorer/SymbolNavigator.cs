using System;
using System.IO;
using System.Threading.Tasks;
using Kubuno.Core.Logging;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;

namespace Kubuno.Rust.SolutionExplorer
{
    /// <summary>
    /// Double-click on a Solution Explorer symbol node (docs/RSPROJ.md lot 8): a Rust item opens its
    /// file with the caret on the item's name; an element of another language's file opens in the logical view its
    /// <see cref="Extensibility.ISolutionSymbolProvider"/> names, with the caret of the text view it names (the desktop
    /// layer: the Kubuno View Designer, its XML pane's caret on the element).
    /// </summary>
    internal static class SymbolNavigator
    {
        public static void Navigate(string path, int line, int column, Extensibility.ISolutionSymbolProvider? provider) =>
            NavigateSafeAsync(path, line, column, provider).FileAndForget("Kubuno/SolutionExplorer/Navigate");

        private static async Task NavigateSafeAsync(string path, int line, int column, Extensibility.ISolutionSymbolProvider? provider)
        {
            try
            {
                await NavigateAsync(path, line, column, provider);
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException($"Solution Explorer: navigating to {path}({line + 1},{column + 1})", exception);
            }
        }

        private static async Task NavigateAsync(string path, int line, int column, Extensibility.ISolutionSymbolProvider? provider)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var serviceProvider = ServiceProvider.GlobalProvider;

            if (!VsShellUtilities.IsDocumentOpen(serviceProvider, path, Guid.Empty, out _, out _, out IVsWindowFrame? frame) || frame == null)
            {
                // Another language's element opens in its provider's view (e.g. the designer, the "select in
                // designer" gesture); Rust code in the default editor.
                var logicalView = provider?.NavigationLogicalView ?? VSConstants.LOGVIEWID.Primary_guid;
                VsShellUtilities.OpenDocument(serviceProvider, path, logicalView, out _, out _, out frame);
            }

            if (frame == null)
            {
                return;
            }

            frame.Show();

            // A provider's text view (the designer's XML pane, a hosted code window) is created once the frame is laid out.
            for (int attempt = 0; attempt < 40; attempt++)
            {
                var view = provider is not null ? provider.GetTextView(frame) : VsShellUtilities.GetTextView(frame);
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
    }
}
