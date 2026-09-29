using System;
using System.IO;
using EnvDTE;
using Kubuno.VisualStudio.Logging;
using Kubuno.VisualStudio.Options;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Kubuno.VisualStudio.Infrastructure
{
    /// <summary>
    /// Implements the "Format on save" option: when enabled and a .rs document is about to be
    /// saved, runs Edit.FormatDocument on it first (rust-analyzer wires that command to rustfmt),
    /// so the formatted text is what actually gets written to disk. Off by default
    /// (<see cref="RustOptionsPage.FormatOnSave"/>). A <c>.rsproj</c> can override it per project with its
    /// <c>KubunoFormatOnSave</c> property (Project Properties, Code Analysis page).
    ///
    /// Uses <see cref="IVsRunningDocTableEvents3.OnBeforeSave"/> rather than trying to hook the LSP
    /// layer directly: it is the one place VS guarantees to call synchronously, on the UI thread,
    /// before the buffer is written, which is exactly the timing formatting needs.
    /// </summary>
    internal sealed class FormatOnSaveDocumentEvents : IVsRunningDocTableEvents3, IDisposable
    {
        private readonly RunningDocumentTable _runningDocumentTable;
        private readonly Func<RustOptionsPage> _getOptions;
        private readonly Func<DTE?> _getDte;
        private uint _cookie;

        public FormatOnSaveDocumentEvents(RunningDocumentTable runningDocumentTable, Func<RustOptionsPage> getOptions, Func<DTE?> getDte)
        {
            _runningDocumentTable = runningDocumentTable ?? throw new ArgumentNullException(nameof(runningDocumentTable));
            _getOptions = getOptions ?? throw new ArgumentNullException(nameof(getOptions));
            _getDte = getDte ?? throw new ArgumentNullException(nameof(getDte));
        }

        public void Advise()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _cookie = _runningDocumentTable.Advise(this);
        }

        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_cookie != 0)
            {
                _runningDocumentTable.Unadvise(_cookie);
                _cookie = 0;
            }
        }

        public int OnBeforeSave(uint docCookie)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var info = _runningDocumentTable.GetDocumentInfo(docCookie);
                if (!string.Equals(Path.GetExtension(info.Moniker), Constants.RustFileExtension, StringComparison.OrdinalIgnoreCase))
                {
                    return VSConstants.S_OK;
                }

                // A .rsproj may override the global option (Code Analysis page, "Format on save":
                // KubunoFormatOnSave = true / false; "global" or unset defers to Tools > Options).
                var projectSetting = info.Hierarchy is null ? null : Commands.RsprojSelection.GetBuildProperty(info.Hierarchy, "KubunoFormatOnSave");
                var enabled = projectSetting switch
                {
                    "true" => true,
                    "false" => false,
                    _ => _getOptions().FormatOnSave,
                };
                if (!enabled)
                {
                    return VSConstants.S_OK;
                }

                var dte = _getDte();
                var document = FindOpenDocument(dte, info.Moniker);
                if (document == null)
                {
                    return VSConstants.S_OK;
                }

                document.Activate();
                dte!.ExecuteCommand("Edit.FormatDocument");
            }
            catch (Exception exception)
            {
                // Formatting is a courtesy, not a precondition for saving: a failure here (e.g.
                // rust-analyzer not ready yet) must not block the save that triggered it.
                KubunoLog.WriteException("Format on save failed", exception);
            }

            return VSConstants.S_OK;
        }

        private static EnvDTE.Document? FindOpenDocument(DTE? dte, string moniker)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (dte == null)
            {
                return null;
            }

            foreach (EnvDTE.Document document in dte.Documents)
            {
                if (string.Equals(document.FullName, moniker, StringComparison.OrdinalIgnoreCase))
                {
                    return document;
                }
            }

            return null;
        }

        // --- IVsRunningDocTableEvents (base) - unused, no-op. ---

        public int OnAfterFirstDocumentLock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining) => VSConstants.S_OK;

        public int OnBeforeLastDocumentUnlock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining) => VSConstants.S_OK;

        public int OnAfterSave(uint docCookie) => VSConstants.S_OK;

        public int OnAfterAttributeChange(uint docCookie, uint grfAttribs) => VSConstants.S_OK;

        public int OnAfterDocumentWindowHide(uint docCookie, IVsWindowFrame pFrame) => VSConstants.S_OK;

        public int OnBeforeDocumentWindowShow(uint docCookie, int fFirstShow, IVsWindowFrame pFrame) => VSConstants.S_OK;

        // --- IVsRunningDocTableEvents2 - unused, no-op. ---

        public int OnAfterAttributeChangeEx(uint docCookie, uint grfAttribs, IVsHierarchy pHierOld, uint itemidOld, string pszMkDocumentOld, IVsHierarchy pHierNew, uint itemidNew, string pszMkDocumentNew) => VSConstants.S_OK;
    }
}
