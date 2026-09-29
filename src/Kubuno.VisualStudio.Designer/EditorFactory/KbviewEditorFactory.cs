using System;
using System.Runtime.InteropServices;
using Kubuno.VisualStudio.Views.Logging;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using OleInterop = Microsoft.VisualStudio.OLE.Interop;

namespace Kubuno.VisualStudio.Designer.EditorFactory
{
    /// <summary>
    /// Produces the split Design | XML <see cref="DesignerWindowPane"/> for <c>.kbview</c> files.
    /// Registered alongside - never instead of - the existing plain-text LSP editor (see
    /// <c>Kubuno.VisualStudio.Views.LanguageService.KubunoViewsLanguageClient</c>, which targets the
    /// "kbview" content type independently of which editor factory opened the buffer): VS allows more
    /// than one registered editor per extension, and "Open With..." picks between them (docs/DESIGNER.md
    /// §1). See INTEGRATION.md for the exact <c>[ProvideEditorFactory]</c>/<c>[ProvideEditorExtension]</c>/
    /// <c>[ProvideEditorLogicalView]</c> attributes a VSIX package class must add to actually register
    /// this factory - nothing here does that itself (this library ships no package class).
    ///
    /// Two physical views, like the WinForms designer's <c>Form1.cs [Design]</c> / <c>Form1.cs</c> pair
    /// (docs/DESIGNER.md §11): the Designer and Primary logical views map to <see cref="DesignPhysicalView"/>
    /// - a <see cref="DesignerWindowPane"/> opened on its Design tab, captioned <c>xxx.kbview [Design]</c>
    /// (<c>[Conception]</c> in a French Visual Studio) - and the Code and TextView logical views map to
    /// <see cref="CodePhysicalView"/>, a plain Visual Studio code window on the SAME text buffer (the XML;
    /// "View Code"/F7). Both windows share one document (doc data), so an edit in either is an edit of the
    /// file. The Design window keeps its own XML and Split tabs for side-by-side editing.
    /// </summary>
    [Guid(DesignerConstants.EditorFactoryGuidString)]
    public sealed class KbviewEditorFactory : IVsEditorFactory
    {
        /// <summary>Physical view of the designer window (Designer/Primary logical views).</summary>
        public const string DesignPhysicalView = "Design";

        /// <summary>Physical view of the plain XML code window (Code/TextView logical views).</summary>
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
            // See the class remarks; anything else is "not supported here", which is the documented
            // meaning of returning a null physical view name with E_NOTIMPL.
            if (rguidLogicalView == VSConstants.LOGVIEWID_Primary ||
                rguidLogicalView == VSConstants.LOGVIEWID_Designer)
            {
                pbstrPhysicalView = DesignPhysicalView;
                return VSConstants.S_OK;
            }

            if (rguidLogicalView == VSConstants.LOGVIEWID_Code ||
                rguidLogicalView == VSConstants.LOGVIEWID_TextView)
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
            // The shell always calls CreateEditorInstance on the main thread; asserted explicitly
            // because this method goes on to touch STA-affinitized COM interfaces (IVsTextLines,
            // IObjectWithSite via CreateTextBuffer, and constructs an IVsWindowPane).
            ThreadHelper.ThrowIfNotOnUIThread();

            ppunkDocView = IntPtr.Zero;
            ppunkDocData = IntPtr.Zero;
            pbstrEditorCaption = string.Empty;
            pguidCmdUI = new Guid(DesignerConstants.CommandUiContextGuidString);
            pgrfCDW = 0;

            if ((grfCreateDoc & (uint)(__VSCREATEEDITORFLAGS.CEF_OPENFILE | __VSCREATEEDITORFLAGS.CEF_SILENT)) == 0)
            {
                return VSConstants.E_INVALIDARG;
            }

            var oleServiceProvider = _oleServiceProvider;
            if (oleServiceProvider is null)
            {
                // SetSite must run before the shell ever asks us to create an instance - if it has
                // not, something is badly wrong with how VS activated this factory rather than
                // anything a caller did, so this fails loudly instead of creating a half-sited buffer.
                return VSConstants.E_UNEXPECTED;
            }

            // Never let a managed exception escape into the shell: it arrives there as a bare failure
            // HRESULT with no trace, and msenv.dll's own open-document error path is not robust to
            // every failure (INTEGRATION.md §10). Log it and return its HRESULT instead.
            DesignerWindowPane? pane = null;
            try
            {
                object docDataObject;
                if (punkDocDataExisting != IntPtr.Zero)
                {
                    docDataObject = Marshal.GetObjectForIUnknown(punkDocDataExisting);
                }
                else
                {
                    docDataObject = CreateTextBuffer(oleServiceProvider);
                }

                if (docDataObject is not IVsTextLines textLines)
                {
                    // Some other editor's doc data (e.g. a binary/diff view) was passed in - we only know
                    // how to co-edit a text buffer, so decline rather than silently failing later.
                    return VSConstants.VS_E_INCOMPATIBLEDOCDATA;
                }

                if (string.Equals(pszPhysicalView, CodePhysicalView, StringComparison.Ordinal))
                {
                    // "View Code" (F7): the standard VSSDK pattern for a text view of an editor factory's
                    // own document - a plain code window on the same buffer, hosted by the shell exactly
                    // like the core text editor's (same command UI context, so every text command works).
                    var codeWindow = GetEditorAdapters(oleServiceProvider).CreateVsCodeWindowAdapter(oleServiceProvider);
                    ErrorHandler.ThrowOnFailure(codeWindow.SetBuffer(textLines));
                    ppunkDocView = Marshal.GetIUnknownForObject(codeWindow);
                    ppunkDocData = Marshal.GetIUnknownForObject(textLines);
                    pguidCmdUI = VSConstants.GUID_TextEditorFactory;
                    pbstrEditorCaption = string.Empty;
                    return VSConstants.S_OK;
                }

                // The document's project decides which design surface runtime renders it (docs/DESIGNER.md section 15).
                pane = new DesignerWindowPane(textLines, oleServiceProvider, document: new DesignSurface.DesignSurfaceDocument(pszMkDocument, pvHier, itemid));

                ppunkDocView = Marshal.GetIUnknownForObject(pane);
                ppunkDocData = Marshal.GetIUnknownForObject(textLines);
                // "main_view.kbview [Design]" / "[Conception]", like WinForms' "Form1.cs [Design]".
                pbstrEditorCaption = DesignerText.DesignCaptionSuffix;
                return VSConstants.S_OK;
            }
            catch (Exception ex)
            {
                KubunoViewsLogHost.Current.WriteException("[designer] CreateEditorInstance failed for '" + pszMkDocument + "'", ex);
                pane?.Dispose();
                ppunkDocView = IntPtr.Zero;
                ppunkDocData = IntPtr.Zero;
                return ex.HResult != 0 ? ex.HResult : VSConstants.E_FAIL;
            }
        }

        /// <summary>
        /// The editor adapters bridge (<c>IVsEditorAdaptersFactoryService</c>, MEF) - the only supported
        /// way to create the legacy <c>IVsTextLines</c>/<c>IVsCodeWindow</c> COM objects from an
        /// extension. Do NOT <c>new VsTextBufferClass()</c>/<c>new VsCodeWindowClass()</c> instead: those
        /// coclasses live in VS's private registry hive, so a plain <c>CoCreateInstance</c> fails with
        /// <c>REGDB_E_CLASSNOTREG</c> (0x80040154) - which is exactly what made this factory's first
        /// version fail every never-opened <c>.kbview</c> (see INTEGRATION.md §10).
        /// </summary>
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

        /// <summary>
        /// A fresh, sited (by the adapters factory itself), not-yet-loaded <c>IVsTextLines</c> with
        /// automatic language/content-type detection turned on - the same mechanism the standard text
        /// editor relies on, which is what lets kubuno-views-ls's MEF <c>ILanguageClient</c> (registered
        /// against the "kbview" content type by file extension, see
        /// <c>Kubuno.VisualStudio.Views.LanguageService.ContentDefinition</c>) attach to this buffer
        /// exactly as it would in the plain text editor. The shell loads the file into it
        /// (<c>IVsPersistDocData.LoadDocData</c>) only AFTER <see cref="CreateEditorInstance"/> returns,
        /// so nothing may read it before then - see <c>DesignerSplitView</c>'s deferred initialization.
        /// </summary>
        private static IVsTextLines CreateTextBuffer(OleInterop.IServiceProvider oleServiceProvider)
        {
            // CreateEditorInstance is always called by the shell on the main thread; asserted
            // explicitly for the same reason as CodeWindowHost's Build/DestroyWindowCore.
            ThreadHelper.ThrowIfNotOnUIThread();

            var textLines = (IVsTextLines)GetEditorAdapters(oleServiceProvider).CreateVsTextBufferAdapter(oleServiceProvider);

            // Well-known "detect language service id from the document moniker's extension" flag - the
            // same GUID as VSConstants.VsTextBufferUserDataGuid.VsBufferDetectLangSid_guid. Setting it
            // is what makes a freshly created IVsTextLines pick up the "kbview" content type/language
            // service purely from the .kbview extension, with no bespoke wiring in this factory.
            var detectLangSidGuid = new Guid("17F375AC-C814-11d1-88AD-0000F87579D2");
            if (textLines is IVsUserData userData)
            {
                userData.SetData(ref detectLangSidGuid, true);
            }

            return textLines;
        }
    }
}
