using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Windows;
using Kubuno.Shared;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;

namespace Kubuno.Views.LanguageService
{
    /// <summary>
    /// Makes a <c>.kbview</c>/<c>.kbcontrol</c> opened outside the Kubuno editor impossible to miss. Found live
    /// (docs/INTELLISENSE.md, "Diagnostics and troubleshooting"): when the extension's pkgdef registrations are missing from
    /// Visual Studio's configuration cache while its MEF parts are loaded, the file opens in the XML editor - no
    /// completion, and every <c>x:</c> underlined as an undeclared prefix - and nothing said why, since the package that
    /// owns the "Kubuno" Output pane never loaded.
    ///
    /// This MEF part needs nothing from the package: on any document text view whose file is a view but whose buffer is
    /// not of the "kbview" content type, it tries to load the package (to tell a missing registration from a deliberate
    /// "Open With..."), writes the cause and the repair command to the "Kubuno" Output pane (creating it if needed) and
    /// shows an info bar with two actions: reopen the file in the core text editor (whose buffer gets the "kbview"
    /// content type from <see cref="ContentDefinition"/>'s MEF export, so the language server and completion work even
    /// without the pkgdef registrations) and copy the repair command (<c>devenv /updateconfiguration</c>).
    /// </summary>
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class KbviewWrongEditorDetector : IWpfTextViewCreationListener
    {
        /// <summary>The core text editor ("Source Code (Text) Editor").</summary>
        private static readonly Guid CoreTextEditorFactory = new("8B382828-6202-11d1-8870-0000F87579D2");

        private static readonly HashSet<string> Reported = new(StringComparer.OrdinalIgnoreCase);

        [Import]
        internal ITextDocumentFactoryService? TextDocuments { get; set; }

        public void TextViewCreated(IWpfTextView textView)
        {
            try
            {
                var buffer = textView.TextDataModel.DocumentBuffer;
                if (buffer.ContentType.IsOfType(KbviewConstants.ContentType))
                {
                    return;
                }

                if (TextDocuments is null || !TextDocuments.TryGetTextDocument(buffer, out var document) || !KbviewConstants.IsViewFile(document.FilePath))
                {
                    return;
                }

                var path = document.FilePath;
                var contentType = buffer.ContentType.TypeName;
#pragma warning disable VSSDK007 // Short UI-thread work with no owner to join (same as KubunoViewsLanguageServerMissingInfoBar).
                ThreadHelper.JoinableTaskFactory.RunAsync(() => ReportAsync(path, contentType)).Task.Forget();
#pragma warning restore VSSDK007
            }
            catch (Exception)
            {
                // A health check must never break the editor it inspects.
            }
        }

        private static async System.Threading.Tasks.Task ReportAsync(string path, string contentType)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            lock (Reported)
            {
                if (!Reported.Add(path))
                {
                    return;
                }
            }

            var shell = ServiceProvider.GlobalProvider.GetService(typeof(SVsShell)) as IVsShell;
            var (cause, packageError) = DiagnosePackage(shell);
            var repair = KbviewEditorHealth.RepairCommand(DevenvPath(), RegistryRoot(shell));
            WriteToOutputPane(KbviewEditorHealth.LogLine(path, contentType, cause, packageError, repair));
            ShowInfoBar(shell, path, cause, repair);
        }

        /// <summary>Whether the Kubuno package is (or can be) loaded, and the failure when it cannot.</summary>
        private static (KbviewEditorHealth.Cause Cause, string? Error) DiagnosePackage(IVsShell? shell)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (shell is null)
            {
                return (KbviewEditorHealth.Cause.PackageNotLoaded, "IVsShell unavailable");
            }

            var guid = KubunoGuids.Package;
            if (ErrorHandler.Succeeded(shell.IsPackageLoaded(ref guid, out var loaded)) && loaded is not null)
            {
                return (KbviewEditorHealth.Cause.OtherEditorChosen, null);
            }

            var hr = shell.LoadPackage(ref guid, out var package);
            return ErrorHandler.Succeeded(hr) && package is not null
                ? (KbviewEditorHealth.Cause.OtherEditorChosen, null)
                : (KbviewEditorHealth.Cause.PackageNotLoaded, $"LoadPackage failed with 0x{hr:X8}");
        }

        private static string DevenvPath()
        {
            try
            {
                return Process.GetCurrentProcess().MainModule?.FileName ?? "devenv.exe";
            }
            catch (Exception)
            {
                return "devenv.exe";
            }
        }

        private static string? RegistryRoot(IVsShell? shell)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return shell is not null && ErrorHandler.Succeeded(shell.GetProperty((int)__VSSPROPID.VSSPROPID_VirtualRegistryRoot, out var root)) ? root as string : null;
        }

        /// <summary>The "Kubuno" Output pane, created here when the package never did.</summary>
        private static void WriteToOutputPane(string line)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (ServiceProvider.GlobalProvider.GetService(typeof(SVsOutputWindow)) is not IVsOutputWindow output)
            {
                return;
            }

            var paneGuid = KubunoGuids.OutputPane;
            if (ErrorHandler.Failed(output.GetPane(ref paneGuid, out var pane)) || pane is null)
            {
                output.CreatePane(ref paneGuid, KubunoConstants.OutputPaneTitle, fInitVisible: 1, fClearWithSolution: 0);
                output.GetPane(ref paneGuid, out pane);
            }

            pane?.OutputStringThreadSafe($"[{DateTime.Now:HH:mm:ss}] [views] {line}{Environment.NewLine}");
            pane?.Activate();
        }

        private static void ShowInfoBar(IVsShell? shell, string path, KbviewEditorHealth.Cause cause, string repair)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (shell is null ||
                ErrorHandler.Failed(shell.GetProperty((int)__VSSPROPID7.VSSPROPID_MainWindowInfoBarHost, out var hostObject)) ||
                hostObject is not IVsInfoBarHost host ||
                ServiceProvider.GlobalProvider.GetService(typeof(SVsInfoBarUIFactory)) is not IVsInfoBarUIFactory factory)
            {
                return;
            }

            var french = Kubuno.Shared.Logic.Localization.UiLanguage.IsFrench;
            var reopen = new InfoBarHyperlink(french ? "Rouvrir avec l'éditeur Kubuno" : "Reopen with the Kubuno editor", "reopen");
            var copy = new InfoBarHyperlink(french ? "Copier la commande de réparation" : "Copy the repair command", "copy");
            var actions = cause == KbviewEditorHealth.Cause.PackageNotLoaded ? new IVsInfoBarActionItem[] { reopen, copy } : new IVsInfoBarActionItem[] { reopen };
            var model = new InfoBarModel(
                new[] { new InfoBarTextSpan(KbviewEditorHealth.InfoBarText(path, cause, french)) },
                actions,
                KnownMonikers.StatusWarning,
                isCloseButtonVisible: true);
            var element = factory.CreateInfoBar(model);
            element.Advise(new Events(path, repair), out _);
            host.AddInfoBar(element);
        }

        /// <summary>Reopens <paramref name="path"/> in the core text editor: closes its current window first (saving it if
        /// the user agrees, like any close), so Visual Studio never asks about a document open in another editor.</summary>
        private static void Reopen(string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var provider = ServiceProvider.GlobalProvider;
            if (VsShellUtilities.IsDocumentOpen(provider, path, Guid.Empty, out _, out _, out var frame) && frame is not null &&
                ErrorHandler.Failed(frame.CloseFrame((uint)__FRAMECLOSE.FRAMECLOSE_PromptSave)))
            {
                return;
            }

            if (VsShellUtilities.IsDocumentOpen(provider, path, Guid.Empty, out _, out _, out _))
            {
                return; // The user cancelled the close.
            }

            lock (Reported)
            {
                Reported.Remove(path);
            }

            VsShellUtilities.OpenDocumentWithSpecificEditor(provider, path, CoreTextEditorFactory, VSConstants.LOGVIEWID_Primary);
        }

        private sealed class Events : IVsInfoBarUIEvents
        {
            private readonly string _path;
            private readonly string _repair;

            public Events(string path, string repair)
            {
                _path = path;
                _repair = repair;
            }

            public void OnClosed(IVsInfoBarUIElement infoBarUIElement)
            {
            }

            public void OnActionItemClicked(IVsInfoBarUIElement infoBarUIElement, IVsInfoBarActionItem actionItem)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                try
                {
                    if (Equals(actionItem.ActionContext, "copy"))
                    {
                        Clipboard.SetText(_repair);
                        return;
                    }

                    infoBarUIElement.Close();
                    Reopen(_path);
                }
                catch (Exception)
                {
                    // Clipboard or shell failures are not fatal: the command is also in the Output pane.
                }
            }
        }
    }
}
