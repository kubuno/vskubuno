using System;
using System.Collections.Generic;
using System.Windows;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace Kubuno.Desktop.Views.Infrastructure
{
    /// <summary>
    /// Shows a main-window info bar with the exact fix when <c>kubuno-views-ls.exe</c> cannot be
    /// found, so the failure is never silent. Deduplicated per VS session: showing it again while it
    /// is already up (e.g. two <c>.kbview</c> files opened back to back before the user reacts) is a
    /// no-op. Mirrors the sibling VSIX project's <c>RustAnalyzerMissingInfoBar</c>, with one
    /// difference: it uses the shared <see cref="ThreadHelper.JoinableTaskFactory"/> rather than a
    /// package-owned one, since this library has no package instance of its own to tie the task's
    /// lifetime to (see INTEGRATION.md for how the VSIX hosts this library's MEF parts under its own
    /// package).
    /// </summary>
    public static class KubunoViewsLanguageServerMissingInfoBar
    {
        private static bool _isShowing;

        public static void ShowIfNeeded()
        {
            // Fire-and-forget via the shared JoinableTaskFactory. VSSDK007 warns that a task from
            // ThreadHelper.JoinableTaskFactory.RunAsync should be awaited/joined rather than only
            // .Forget()'d, because the shared factory has no owning object to join it against on
            // package/solution unload; the sibling VSIX project avoids the warning by using a
            // package-owned factory (Kubuno.Core.KubunoHost.Package.JoinableTaskFactory) instead, which this
            // library - having no package instance of its own - cannot do (see class remarks). The
            // task itself is short (a few UI-thread service calls), so the suppression is safe.
#pragma warning disable VSSDK007
            ThreadHelper.JoinableTaskFactory.RunAsync(ShowIfNeededAsync).Task.Forget();
#pragma warning restore VSSDK007
        }

        private static async System.Threading.Tasks.Task ShowIfNeededAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (_isShowing)
            {
                return;
            }

            if (ServiceProvider.GlobalProvider.GetService(typeof(SVsShell)) is not IVsShell shell)
            {
                return;
            }

            var hr = shell.GetProperty((int)__VSSPROPID7.VSSPROPID_MainWindowInfoBarHost, out var infoBarHostObj);
            if (ErrorHandler.Failed(hr) || infoBarHostObj is not IVsInfoBarHost infoBarHost)
            {
                return;
            }

            if (ServiceProvider.GlobalProvider.GetService(typeof(SVsInfoBarUIFactory)) is not IVsInfoBarUIFactory factory)
            {
                return;
            }

            // InfoBarTextSpan(text, bold, italic, underline) - no overload takes just text.
            var textSpans = new List<IVsInfoBarTextSpan>
            {
                new InfoBarTextSpan("Kubuno: kubuno-views-ls was not found. Build it (see the desktop workspace's ", false, false, false),
                new InfoBarTextSpan("kubuno-views-ls", true, false, false),
                new InfoBarTextSpan(" crate) so it lands in one of the dev build folders, or set a path override in Tools > Options > Kubuno > Views, then reopen the .kbview file.", false, false, false),
            };
            var actions = new List<IVsInfoBarActionItem>
            {
                new InfoBarButton("Copy options page path", null),
            };
            var model = new InfoBarModel(textSpans, actions, KnownMonikers.StatusWarning, true);

            var uiElement = factory.CreateInfoBar(model);
            var events = new InfoBarEvents(() => _isShowing = false);
            uiElement.Advise(events, out _);
            infoBarHost.AddInfoBar(uiElement);
            _isShowing = true;
        }

        private sealed class InfoBarEvents : IVsInfoBarUIEvents
        {
            private readonly Action _onClosed;

            public InfoBarEvents(Action onClosed) => _onClosed = onClosed;

            public void OnClosed(IVsInfoBarUIElement infoBarUIElement) => _onClosed();

            public void OnActionItemClicked(IVsInfoBarUIElement infoBarUIElement, IVsInfoBarActionItem actionItem)
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                try
                {
                    // There is no single CLI command to copy here (unlike "rustup component add
                    // rust-analyzer" for the Rust info bar): kubuno-views-ls is a local, not-yet-published
                    // dev build. Copying the options page path is the next best fixed piece of text -
                    // Visual Studio does not expose a documented way to deep-link Tools > Options to a
                    // specific page from an info bar action without a package-owned service this library
                    // does not have access to (see class remarks).
                    Clipboard.SetText("Tools > Options > Kubuno > Views");
                }
                catch (Exception)
                {
                    // Clipboard access can fail (e.g. another process holding it); the path is still
                    // visible in the info bar text itself, so this is not fatal.
                }

                infoBarUIElement.Close();
            }
        }
    }
}
