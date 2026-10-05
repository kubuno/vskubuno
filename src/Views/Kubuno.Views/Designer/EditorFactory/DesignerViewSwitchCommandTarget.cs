using System;
using Kubuno.Views.Designer.UI;
using Kubuno.Views.Logging;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using OleInterop = Microsoft.VisualStudio.OLE.Interop;

namespace Kubuno.Views.Designer.EditorFactory
{
    /// <summary>
    /// "View Code" (F7, <c>View.ViewCode</c>) and "View Designer" (Shift+F7, <c>View.ViewDesigner</c>) for
    /// <c>.kbview</c> documents, like the WinForms designer: from the designer window, F7 opens the view's code
    /// (<c>main_view.rs</c>, as Windows Forms opens <c>Form1.cs</c>) - or, for a view without one, the XML in its
    /// own code window; from the XML or from the code, Shift+F7 goes back to <c>main_view.kbview [Design]</c>
    /// (<see cref="CodeFileOf"/>, <see cref="ViewFileOf"/>). Registered by the package as a PRIORITY command target
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

        /// <summary>
        /// <c>View.ToggleDesigner</c> (cmdidToggleDesigner): F7 in Visual Studio 2026's default keyboard scheme, where F7 is
        /// no longer View.ViewCode and Shift+F7 is bound in the HTML editor only (found live with a web view, WV-9b): from a
        /// view's designer it is "View Code", from a view's code "View Designer".
        /// </summary>
        private const uint ToggleDesigner = 347;

        /// <summary>The command <paramref name="id"/> stands for here (<see cref="ToggleDesigner"/> becomes View Code or View Designer).</summary>
        private uint Effective(uint id)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return id == ToggleDesigner ? (ActiveKbviewDocument() is not null ? ViewCode : ViewForm) : id;
        }

        private readonly IServiceProvider _serviceProvider;

        public DesignerViewSwitchCommandTarget(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OleInterop.OLECMD[] prgCmds, IntPtr pCmdText)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var queried = prgCmds is { Length: > 0 } && pguidCmdGroup == VSConstants.GUID_VSStandardCommandSet97 ? Effective(prgCmds[0].cmdID) : 0;
            if (pguidCmdGroup != VSConstants.GUID_VSStandardCommandSet97 || prgCmds is null || prgCmds.Length == 0 ||
                (queried != ViewForm && queried != ViewCode) ||
                (ActiveKbviewDocument() is null && (queried != ViewForm || ActiveCodeBehindView() is null)))
            {
                return (int)OleInterop.Constants.OLECMDERR_E_NOTSUPPORTED;
            }

            prgCmds[0].cmdf = (uint)(OleInterop.OLECMDF.OLECMDF_SUPPORTED | OleInterop.OLECMDF.OLECMDF_ENABLED);
            return VSConstants.S_OK;
        }

        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (pguidCmdGroup == VSConstants.GUID_VSStandardCommandSet97)
            {
                nCmdID = Effective(nCmdID);
            }

            if (pguidCmdGroup != VSConstants.GUID_VSStandardCommandSet97 || (nCmdID != ViewForm && nCmdID != ViewCode))
            {
                return (int)OleInterop.Constants.OLECMDERR_E_NOTSUPPORTED;
            }

            var editor = new Guid(DesignerConstants.EditorFactoryGuidString);
            try
            {
                if (ActiveKbviewDocument() is not { } active)
                {
                    // Shift+F7 in a view's code (`main_view.rs`): the view's designer, like Form1.cs → Form1 [Design].
                    if (nCmdID != ViewForm || ActiveCodeBehindView() is not { } view)
                    {
                        return (int)OleInterop.Constants.OLECMDERR_E_NOTSUPPORTED;
                    }

                    VsShellUtilities.OpenDocumentWithSpecificEditor(_serviceProvider, view, editor, VSConstants.LOGVIEWID_Designer, out _, out _, out var designerFrame);
                    designerFrame?.Show();
                    return VSConstants.S_OK;
                }

                if (nCmdID == ViewForm && active.DocView is DesignerWindowPane designer)
                {
                    // Already the designer: Shift+F7 brings back the plain Design tab (from XML/Split).
                    designer.Mode = DesignerViewMode.Design;
                    return VSConstants.S_OK;
                }

                if (nCmdID == ViewCode && CodeFileOf(active.Path, System.IO.File.Exists) is { } code)
                {
                    // F7: the view's code (`main_view.rs`, a `#[kubuno_desktop::view]` form class or a code-behind), like
                    // Windows Forms opening Form1.cs; the XML stays one click away (the designer's XML tab).
                    // Its own editor (the Rust editor), in its primary view: a .rs file has no "code" view of its own.
                    VsShellUtilities.OpenDocument(_serviceProvider, code, VSConstants.LOGVIEWID_Primary, out _, out _, out var codeFrame);
                    codeFrame?.Show();
                    return VSConstants.S_OK;
                }

                var logicalView = nCmdID == ViewForm ? VSConstants.LOGVIEWID_Designer : VSConstants.LOGVIEWID_Code;
                VsShellUtilities.OpenDocumentWithSpecificEditor(_serviceProvider, active.Path, editor, logicalView, out _, out _, out var frame);
                frame?.Show();
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
            {
                KubunoViewsLogHost.Current.WriteException("[designer] switching between the designer and the code failed", ex);
            }

            return VSConstants.S_OK;
        }

        /// <summary>
        /// The code of a view: the same-stem <c>.rs</c> file next to it (<c>main_view.kbview</c> → <c>main_view.rs</c>), or a
        /// web view's TypeScript code-behind (<c>WaffleMenu.kbcontrol</c> → <c>WaffleMenu.ts</c>, or <c>WaffleMenu.kbcontrol.ts</c>),
        /// when it exists; null otherwise (F7 then shows the XML).
        /// </summary>
        public static string? CodeFileOf(string kbviewPath, Func<string, bool> fileExists) =>
            Kubuno.Views.Logic.ViewFiles.CodeBehindOf(kbviewPath, fileExists);

        /// <summary>The view of a code file: the same-stem <c>.kbview</c> or <c>.kbcontrol</c> next to a <c>.rs</c> / <c>.ts</c> / <c>.tsx</c> file, when it exists.</summary>
        public static string? ViewFileOf(string rsPath, Func<string, bool> fileExists) =>
            Kubuno.Views.Logic.ViewFiles.ViewOf(rsPath, fileExists);

        /// <summary>The view of the active document when it is a view's code (<c>main_view.rs</c>), else null.</summary>
        private string? ActiveCodeBehindView()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return ActiveDocumentPath() is { } path ? ViewFileOf(path, System.IO.File.Exists) : null;
        }

        /// <summary>The path of the active window when it is a document frame.</summary>
        private string? ActiveDocumentPath()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_serviceProvider.GetService(typeof(SVsShellMonitorSelection)) is not IVsMonitorSelection selection ||
                ErrorHandler.Failed(selection.GetCurrentElementValue((uint)VSConstants.VSSELELEMID.SEID_WindowFrame, out var windowObject)) ||
                windowObject is not IVsWindowFrame frame ||
                ErrorHandler.Failed(frame.GetProperty((int)__VSFPROPID.VSFPROPID_Type, out var type)) ||
                !(type is int frameType && frameType == (int)__WindowFrameTypeFlags.WINDOWFRAMETYPE_Document) ||
                ErrorHandler.Failed(frame.GetProperty((int)__VSFPROPID.VSFPROPID_pszMkDocument, out var moniker)))
            {
                return null;
            }

            return moniker as string;
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
                !Kubuno.Views.KbviewConstants.IsViewFile(path))
            {
                return null;
            }

            frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out var docView);
            return (path, docView);
        }
    }
}
