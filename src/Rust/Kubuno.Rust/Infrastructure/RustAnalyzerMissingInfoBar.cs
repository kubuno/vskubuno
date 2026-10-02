using System;
using System.Collections.Generic;
using System.Windows;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace Kubuno.Rust.Infrastructure
{
    /// <summary>
    /// Shows a main-window info bar with the exact fix when rust-analyzer cannot be found, so the
    /// failure is never silent. Deduplicated per VS session: showing it again while it is already
    /// up (e.g. two .rs files opened back to back before the user reacts) is a no-op.
    /// </summary>
    internal static class RustAnalyzerMissingInfoBar
    {
        private static bool _isShowing;

        public static void ShowIfNeeded()
        {
            // ThreadHelper.JoinableTaskFactory is not safe for fire-and-forget (VSSDK007): use the
            // package's own JoinableTaskFactory, which ties the task to the package's lifetime,
            // when the package has already loaded; before that (unlikely - this only runs once a
            // .rs document activates the language client), fall back to the shared one.
            var joinableTaskFactory = Kubuno.Shared.KubunoHost.JoinableTaskFactory;
            joinableTaskFactory.RunAsync(ShowIfNeededAsync).Task.Forget();
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
                new InfoBarTextSpan("Kubuno: rust-analyzer was not found. Run ", false, false, false),
                new InfoBarTextSpan(Constants.InstallRustAnalyzerCommand, true, false, false),
                new InfoBarTextSpan(" (or set a path override in Tools > Options > Kubuno > Rust), then reopen the .rs file.", false, false, false),
            };
            var actions = new List<IVsInfoBarActionItem>
            {
                new InfoBarButton("Copy command", null),
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
                    Clipboard.SetText(Constants.InstallRustAnalyzerCommand);
                }
                catch (Exception)
                {
                    // Clipboard access can fail (e.g. another process holding it); the command is
                    // still visible in the info bar text itself, so this is not fatal.
                }

                infoBarUIElement.Close();
            }
        }
    }
}
