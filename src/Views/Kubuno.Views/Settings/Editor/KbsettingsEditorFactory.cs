using System;
using System.Runtime.InteropServices;
using Kubuno.Views.Logging;
using Kubuno.Views.Resources.Editor;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using OleInterop = Microsoft.VisualStudio.OLE.Interop;

namespace Kubuno.Views.Settings.Editor
{
    /// <summary>
    /// Produces the settings editor (<see cref="KbsettingsEditorPane"/>) for <c>.kbsettings</c> files, the equivalent of
    /// Windows Forms' Settings.settings designer (docs/STORAGE-COMPONENTS.md §5.3). Two physical views on the SAME text
    /// buffer, like the resource editor's factory: the Designer and Primary logical views show the grid, the Code and
    /// TextView ones a plain code window ("View Code"). Registered by the attributes on KubunoPackage; the
    /// <c>.rsproj</c> project system makes it the default editor (Kubuno.Desktop.ProjectSystem's
    /// <c>KbsettingsEditorProvider</c>).
    /// </summary>
    [Guid(GuidString)]
    public sealed class KbsettingsEditorFactory : IVsEditorFactory
    {
        /// <summary>The factory's GUID (also in Kubuno.Desktop.ProjectSystem's KbsettingsEditorProvider - keep in sync).</summary>
        public const string GuidString = "5C2E9A47-1B3D-4E6F-8A90-2D7C4B1E6F38";

        public const string EditorPhysicalView = "Editor";

        public const string CodePhysicalView = "Code";

        private OleInterop.IServiceProvider? _oleServiceProvider;

        public int SetSite(OleInterop.IServiceProvider psp)
        {
            _oleServiceProvider = psp;
            return VSConstants.S_OK;
        }

        public int Close() => VSConstants.S_OK;

        public int MapLogicalView(ref Guid rguidLogicalView, out string? pbstrPhysicalView)
        {
            if (rguidLogicalView == VSConstants.LOGVIEWID_Primary || rguidLogicalView == VSConstants.LOGVIEWID_Designer)
            {
                pbstrPhysicalView = EditorPhysicalView;
                return VSConstants.S_OK;
            }

            if (rguidLogicalView == VSConstants.LOGVIEWID_Code || rguidLogicalView == VSConstants.LOGVIEWID_TextView)
            {
                pbstrPhysicalView = CodePhysicalView;
                return VSConstants.S_OK;
            }

            pbstrPhysicalView = null;
            return VSConstants.E_NOTIMPL;
        }

        public int CreateEditorInstance(
            uint grfCreateDoc,
            string pszMkDocument,
            string? pszPhysicalView,
            IVsHierarchy pvHier,
            uint itemid,
            IntPtr punkDocDataExisting,
            out IntPtr ppunkDocView,
            out IntPtr ppunkDocData,
            out string pbstrEditorCaption,
            out Guid pguidCmdUI,
            out int pgrfCDW)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ppunkDocView = IntPtr.Zero;
            ppunkDocData = IntPtr.Zero;
            pbstrEditorCaption = string.Empty;
            pguidCmdUI = new Guid(GuidString);
            pgrfCDW = 0;

            if ((grfCreateDoc & (uint)(__VSCREATEEDITORFLAGS.CEF_OPENFILE | __VSCREATEEDITORFLAGS.CEF_SILENT)) == 0)
            {
                return VSConstants.E_INVALIDARG;
            }

            if (_oleServiceProvider is not { } ole)
            {
                return VSConstants.E_UNEXPECTED;
            }

            KbsettingsEditorPane? pane = null;
            try
            {
                var docData = punkDocDataExisting != IntPtr.Zero ? Marshal.GetObjectForIUnknown(punkDocDataExisting) : CreateTextBuffer(ole);
                if (docData is not IVsTextLines textLines)
                {
                    return VSConstants.VS_E_INCOMPATIBLEDOCDATA;
                }

                if (string.Equals(pszPhysicalView, CodePhysicalView, StringComparison.Ordinal))
                {
                    var codeWindow = KbresEditorFactory.GetEditorAdapters(ole).CreateVsCodeWindowAdapter(ole);
                    ErrorHandler.ThrowOnFailure(codeWindow.SetBuffer(textLines));
                    ppunkDocView = Marshal.GetIUnknownForObject(codeWindow);
                    ppunkDocData = Marshal.GetIUnknownForObject(textLines);
                    pguidCmdUI = VSConstants.GUID_TextEditorFactory;
                    return VSConstants.S_OK;
                }

                pane = new KbsettingsEditorPane(textLines, ole, pszMkDocument);
                ppunkDocView = Marshal.GetIUnknownForObject(pane);
                ppunkDocData = Marshal.GetIUnknownForObject(textLines);
                return VSConstants.S_OK;
            }
            catch (Exception ex)
            {
                KubunoViewsLogHost.Current.WriteException("[settings] CreateEditorInstance failed for '" + pszMkDocument + "'", ex);
                pane?.Dispose();
                ppunkDocView = IntPtr.Zero;
                ppunkDocData = IntPtr.Zero;
                return ex.HResult != 0 ? ex.HResult : VSConstants.E_FAIL;
            }
        }

        private static IVsTextLines CreateTextBuffer(OleInterop.IServiceProvider ole)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var textLines = (IVsTextLines)KbresEditorFactory.GetEditorAdapters(ole).CreateVsTextBufferAdapter(ole);

            // VsBufferDetectLangSid: pick the language service (and content type) from the file extension (XML).
            var detectLangSidGuid = new Guid("17F375AC-C814-11d1-88AD-0000F87579D2");
            if (textLines is IVsUserData userData)
            {
                userData.SetData(ref detectLangSidGuid, true);
            }

            return textLines;
        }
    }
}
