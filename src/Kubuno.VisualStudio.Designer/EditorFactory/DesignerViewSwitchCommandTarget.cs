using System;
using Kubuno.VisualStudio.Designer.UI;
using Kubuno.VisualStudio.Views.Logging;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using OleInterop = Microsoft.VisualStudio.OLE.Interop;

namespace Kubuno.VisualStudio.Designer.EditorFactory
{
    /// <summary>
    /// "View Code" (F7, <c>View.ViewCode</c>) and "View Designer" (Shift+F7, <c>View.ViewDesigner</c>) for
    /// <c>.kbview</c> documents, like the WinForms designer: from the designer window, F7 opens the XML in
    /// its own code window (<c>main_view.kbview</c>); from that code window, Shift+F7 goes back to
    /// <c>main_view.kbview [Design]</c>. Registered by the package as a PRIORITY command target
    /// (<c>IVsRegisterPriorityCommandTarget</c>) so it works the same in a <c>.rsproj</c> (where the
    /// project system would also handle these commands for items with <c>SubType=Designer</c>) and in Open
    /// Folder (where nobody else does). It only claims the two commands when the ACTIVE window is a
    /// <c>.kbview</c> document frame - never for Solution Explorer or any other document, so it cannot
    /// steal F7 from C#/Rust files.
    /// </summary>
    public sealed class DesignerViewSwitchCommandTarget : OleInterop.IOleCommandTarget
    {
        private const uint ViewForm = (uint)VSConstants.VSStd97CmdID.ViewForm;
        private const uint ViewCode = (uint)VSConstants.VSStd97CmdID.ViewCode;

        private readonly IServiceProvider _serviceProvider;

        public DesignerViewSwitchCommandTarget(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OleInterop.OLECMD[] prgCmds, IntPtr pCmdText)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (pguidCmdGroup != VSConstants.GUID_VSStandardCommandSet97 || prgCmds is null || prgCmds.Length == 0 ||
                (prgCmds[0].cmdID != ViewForm && prgCmds[0].cmdID != ViewCode) || ActiveKbviewDocument() is null)
            {
                return (int)OleInterop.Constants.OLECMDERR_E_NOTSUPPORTED;
            }

            prgCmds[0].cmdf = (uint)(OleInterop.OLECMDF.OLECMDF_SUPPORTED | OleInterop.OLECMDF.OLECMDF_ENABLED);
            return VSConstants.S_OK;
        }

        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (pguidCmdGroup != VSConstants.GUID_VSStandardCommandSet97 || (nCmdID != ViewForm && nCmdID != ViewCode) ||
                ActiveKbviewDocument() is not { } active)
            {
                return (int)OleInterop.Constants.OLECMDERR_E_NOTSUPPORTED;
            }

            try
            {
                if (nCmdID == ViewForm && active.DocView is DesignerWindowPane designer)
                {
                    // Already the designer: Shift+F7 brings back the plain Design tab (from XML/Split).
                    designer.Mode = DesignerViewMode.Design;
                    return VSConstants.S_OK;
                }

                var logicalView = nCmdID == ViewForm ? VSConstants.LOGVIEWID_Designer : VSConstants.LOGVIEWID_Code;
                var editor = new Guid(DesignerConstants.EditorFactoryGuidString);
                VsShellUtilities.OpenDocumentWithSpecificEditor(_serviceProvider, active.Path, editor, logicalView, out _, out _, out var frame);
                frame?.Show();
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
            {
                KubunoViewsLogHost.Current.WriteException("[designer] switching between the designer and the XML failed", ex);
            }

            return VSConstants.S_OK;
        }

        /// <summary>The active window when it is a <c>.kbview</c> document frame: its path and doc view.</summary>
        private (string Path, object? DocView)? ActiveKbviewDocument()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_serviceProvider.GetService(typeof(SVsShellMonitorSelection)) is not IVsMonitorSelection selection ||
                ErrorHandler.Failed(selection.GetCurrentElementValue((uint)VSConstants.VSSELELEMID.SEID_WindowFrame, out var windowObject)) ||
                windowObject is not IVsWindowFrame frame ||
                ErrorHandler.Failed(frame.GetProperty((int)__VSFPROPID.VSFPROPID_Type, out var type)) ||
                !(type is int frameType && frameType == (int)__WindowFrameTypeFlags.WINDOWFRAMETYPE_Document) ||
                ErrorHandler.Failed(frame.GetProperty((int)__VSFPROPID.VSFPROPID_pszMkDocument, out var moniker)) ||
                moniker is not string path ||
                !path.EndsWith("." + Kubuno.VisualStudio.Views.KbviewConstants.FileExtension.TrimStart('.'), StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out var docView);
            return (path, docView);
        }
    }
}
