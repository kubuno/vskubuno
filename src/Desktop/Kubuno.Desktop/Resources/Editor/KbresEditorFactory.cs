using System;
using System.IO;
using System.Runtime.InteropServices;
using Kubuno.Desktop.Logic.Resources;
using Kubuno.Desktop.Views.Logging;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using OleInterop = Microsoft.VisualStudio.OLE.Interop;

namespace Kubuno.Desktop.Resources.Editor
{
    /// <summary>
    /// Produces the resource editor (<see cref="KbresEditorPane"/>) for <c>.kbres</c> files, the equivalent of Visual
    /// Studio's .resx editor. Two physical views on the SAME text buffer (the XML), like the .kbview designer's
    /// factory: the Designer and Primary logical views show the editor, the Code and TextView ones a plain code
    /// window ("View Code"). A culture file (<c>name.fr.kbres</c>) opened on its own is shown as XML: its content is
    /// edited from the neutral file's editor, which owns the whole set.
    /// Registered by the attributes on KubunoPackage (ProvideEditorFactory, logical views, extension). The invisible
    /// editors of the culture files go through it too and get the plain code window.
    /// </summary>
    [Guid(GuidString)]
    public sealed class KbresEditorFactory : IVsEditorFactory
    {
        /// <summary>The factory's GUID (also in Kubuno.Desktop.ProjectSystem's KbresEditorProvider - keep in sync).</summary>
        public const string GuidString = "7E4B2A19-3C5D-4F86-9A21-B5D8E0C47F63";

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

            KbresEditorPane? pane = null;
            try
            {
                var docData = punkDocDataExisting != IntPtr.Zero ? Marshal.GetObjectForIUnknown(punkDocDataExisting) : CreateTextBuffer(ole);
                if (docData is not IVsTextLines textLines)
                {
                    return VSConstants.VS_E_INCOMPATIBLEDOCDATA;
                }

                var isCultureFile = ResourceNames.SplitFileName(Path.GetFileName(pszMkDocument)) is { Culture: not null };
                if (isCultureFile || string.Equals(pszPhysicalView, CodePhysicalView, StringComparison.Ordinal))
                {
                    var codeWindow = GetEditorAdapters(ole).CreateVsCodeWindowAdapter(ole);
                    ErrorHandler.ThrowOnFailure(codeWindow.SetBuffer(textLines));
                    ppunkDocView = Marshal.GetIUnknownForObject(codeWindow);
                    ppunkDocData = Marshal.GetIUnknownForObject(textLines);
                    pguidCmdUI = VSConstants.GUID_TextEditorFactory;
                    return VSConstants.S_OK;
                }

                pane = new KbresEditorPane(textLines, ole, pszMkDocument);
                ppunkDocView = Marshal.GetIUnknownForObject(pane);
                ppunkDocData = Marshal.GetIUnknownForObject(textLines);
                return VSConstants.S_OK;
            }
            catch (Exception ex)
            {
                KubunoViewsLogHost.Current.WriteException("[resources] CreateEditorInstance failed for '" + pszMkDocument + "'", ex);
                pane?.Dispose();
                ppunkDocView = IntPtr.Zero;
                ppunkDocData = IntPtr.Zero;
                return ex.HResult != 0 ? ex.HResult : VSConstants.E_FAIL;
            }
        }

        /// <summary>The editor adapters bridge: the only supported way to create IVsTextLines / IVsCodeWindow from an extension (never <c>new VsTextBufferClass()</c>).</summary>
        internal static IVsEditorAdaptersFactoryService GetEditorAdapters(OleInterop.IServiceProvider oleServiceProvider)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var serviceProvider = new ServiceProvider(oleServiceProvider);
            if (serviceProvider.GetService(typeof(SComponentModel)) is not IComponentModel componentModel)
            {
                throw new InvalidOperationException("SComponentModel is unavailable - cannot create editor adapters.");
            }

            return componentModel.GetService<IVsEditorAdaptersFactoryService>();
        }

        private static IVsTextLines CreateTextBuffer(OleInterop.IServiceProvider ole)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var textLines = (IVsTextLines)GetEditorAdapters(ole).CreateVsTextBufferAdapter(ole);

            // VsBufferDetectLangSid: pick the language service (and content type) from the file extension.
            var detectLangSidGuid = new Guid("17F375AC-C814-11d1-88AD-0000F87579D2");
            if (textLines is IVsUserData userData)
            {
                userData.SetData(ref detectLangSidGuid, true);
            }

            return textLines;
        }
    }
}
