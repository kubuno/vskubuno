using System;
using System.Runtime.InteropServices;
using Kubuno.Core.Logging;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using OleInterop = Microsoft.VisualStudio.OLE.Interop;

namespace Kubuno.Web.WebDesigner.Spike
{
    /// <summary>
    /// SPIKE (docs/WEB-VIEWS.md, WV-9a): the editor of <c>.kbwebspike</c> files. Same shape as the desktop
    /// <c>.kbview</c> designer: the document's data is a plain text buffer (the markup, the single source of truth); the
    /// Design/Primary logical views open <see cref="WebDesignSpikePane"/> (the WebView2 surface), the Code/TextView ones a
    /// Visual Studio code window on the same buffer (F7).
    /// </summary>
    [Guid(WebDesignSpikeConstants.EditorFactoryGuidString)]
    public sealed class WebDesignSpikeEditorFactory : IVsEditorFactory
    {
        private const string DesignView = "Design";
        private const string CodeView = "Code";

        private OleInterop.IServiceProvider? _site;

        public int SetSite(OleInterop.IServiceProvider psp)
        {
            _site = psp;
            return VSConstants.S_OK;
        }

        public int Close() => VSConstants.S_OK;

        public int MapLogicalView(ref Guid rguidLogicalView, out string? pbstrPhysicalView)
        {
            if (rguidLogicalView == VSConstants.LOGVIEWID_Primary || rguidLogicalView == VSConstants.LOGVIEWID_Designer)
            {
                pbstrPhysicalView = DesignView;
                return VSConstants.S_OK;
            }

            if (rguidLogicalView == VSConstants.LOGVIEWID_Code || rguidLogicalView == VSConstants.LOGVIEWID_TextView)
            {
                pbstrPhysicalView = CodeView;
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
            pguidCmdUI = new Guid(WebDesignSpikeConstants.CommandUiContextGuidString);
            pgrfCDW = 0;
            if ((grfCreateDoc & (uint)(__VSCREATEEDITORFLAGS.CEF_OPENFILE | __VSCREATEEDITORFLAGS.CEF_SILENT)) == 0)
            {
                return VSConstants.E_INVALIDARG;
            }

            if (_site is not { } site)
            {
                return VSConstants.E_UNEXPECTED;
            }

            try
            {
                var adapters = Adapters(site);
                object docData = punkDocDataExisting != IntPtr.Zero
                    ? Marshal.GetObjectForIUnknown(punkDocDataExisting)
                    : adapters.CreateVsTextBufferAdapter(site);
                if (docData is not IVsTextLines buffer)
                {
                    return VSConstants.VS_E_INCOMPATIBLEDOCDATA;
                }

                if (string.Equals(pszPhysicalView, CodeView, StringComparison.Ordinal))
                {
                    var codeWindow = adapters.CreateVsCodeWindowAdapter(site);
                    ErrorHandler.ThrowOnFailure(codeWindow.SetBuffer(buffer));
                    ppunkDocView = Marshal.GetIUnknownForObject(codeWindow);
                    ppunkDocData = Marshal.GetIUnknownForObject(buffer);
                    pguidCmdUI = VSConstants.GUID_TextEditorFactory;
                    return VSConstants.S_OK;
                }

                var pane = new WebDesignSpikePane(buffer, site, pszMkDocument);
                ppunkDocView = Marshal.GetIUnknownForObject(pane);
                ppunkDocData = Marshal.GetIUnknownForObject(buffer);
                pbstrEditorCaption = WebDesignSpikeConstants.DesignCaption;
                return VSConstants.S_OK;
            }
            catch (Exception ex)
            {
                KubunoLog.WriteException("[web-spike] CreateEditorInstance failed for '" + pszMkDocument + "'", ex);
                ppunkDocView = IntPtr.Zero;
                ppunkDocData = IntPtr.Zero;
                return ex.HResult != 0 ? ex.HResult : VSConstants.E_FAIL;
            }
        }

        /// <summary>The editor adapters factory (the supported way to create <c>IVsTextLines</c>/<c>IVsCodeWindow</c>).</summary>
        internal static IVsEditorAdaptersFactoryService Adapters(OleInterop.IServiceProvider site)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (new ServiceProvider(site).GetService(typeof(SComponentModel)) is not IComponentModel model)
            {
                throw new InvalidOperationException("SComponentModel is unavailable.");
            }

            return model.GetService<IVsEditorAdaptersFactoryService>();
        }
    }
}
