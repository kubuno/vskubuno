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
    /// Both logical views this factory declares (Designer and TextView, see INTEGRATION.md) resolve to
    /// the *same* physical view/window: <see cref="DesignerWindowPane"/> always shows the Design/XML/
    /// Split tab strip, and the requested logical view only affects which tab starts active
    /// (<see cref="MapLogicalView"/> intentionally returns the one physical view name for either).
    /// </summary>
    [Guid(DesignerConstants.EditorFactoryGuidString)]
    public sealed class KbviewEditorFactory : IVsEditorFactory
    {
        /// <summary>The single physical view this factory ever creates - both logical views map to it (see class remarks).</summary>
        private const string PhysicalViewName = "Design";

        private OleInterop.IServiceProvider? _oleServiceProvider;

        public int SetSite(OleInterop.IServiceProvider psp)
        {
            _oleServiceProvider = psp;
            return VSConstants.S_OK;
        }

        public int Close() => VSConstants.S_OK;

        public int MapLogicalView(ref Guid rguidLogicalView, out string? pbstrPhysicalView)
        {
            // Accept the Designer and (plain) TextView/Primary logical views - see INTEGRATION.md for
            // the exact ProvideEditorLogicalView attributes this corresponds to. Both resolve to the
            // one split-view physical window (class remarks); anything else is "not supported here",
            // which is the documented meaning of returning a null physical view name with E_NOTIMPL.
            if (rguidLogicalView == VSConstants.LOGVIEWID_Primary ||
                rguidLogicalView == VSConstants.LOGVIEWID_TextView ||
                rguidLogicalView == VSConstants.LOGVIEWID_Designer)
            {
                pbstrPhysicalView = PhysicalViewName;
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

                pane = new DesignerWindowPane(textLines, oleServiceProvider);

                ppunkDocView = Marshal.GetIUnknownForObject(pane);
                ppunkDocData = Marshal.GetIUnknownForObject(textLines);
                pbstrEditorCaption = string.Empty;
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
