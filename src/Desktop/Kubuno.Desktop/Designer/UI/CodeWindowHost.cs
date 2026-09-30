using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using OleInterop = Microsoft.VisualStudio.OLE.Interop;

namespace Kubuno.Desktop.Designer.UI
{
    /// <summary>
    /// Hosts the real Visual Studio text editor (<c>IVsCodeWindow</c>) inside WPF, bound to the same
    /// <c>IVsTextLines</c> buffer the editor factory created or reused
    /// (<see cref="EditorFactory.KbviewEditorFactory"/>) - so IntelliSense, colorization, and the
    /// existing kubuno-views-ls language client (which attaches by content type, not by which editor
    /// opened the file) all keep working exactly as they do in the plain text editor. This is
    /// in-process COM (unlike DSG-7's design surface, which is a separate process reparented via
    /// <c>SetParent</c> - docs/DESIGNER.md §3/§7); <c>IVsCodeWindow</c> is created and torn down on the
    /// UI thread like any other VS editor window.
    ///
    /// Modelled on the well-precedented "embed a second IVsCodeWindow inside a custom WPF pane"
    /// technique used by several real-world split-view VS editors (e.g. Markdown Editor's source pane);
    /// the code window comes from <c>IVsEditorAdaptersFactoryService.CreateVsCodeWindowAdapter</c>, which
    /// only exists inside a running <c>devenv.exe</c> - this class cannot be usefully unit-tested
    /// outside a real VS process (see this project's
    /// tests/Kubuno.Desktop.Tests/Designer for what IS covered without one).
    /// </summary>
    public sealed class CodeWindowHost : HwndHost
    {
        private readonly IVsTextLines _textBuffer;
        private readonly OleInterop.IServiceProvider _oleServiceProvider;
        private IVsCodeWindow? _codeWindow;
        private IntPtr _childHwnd = IntPtr.Zero;

        public CodeWindowHost(IVsTextLines textBuffer, OleInterop.IServiceProvider oleServiceProvider)
        {
            _textBuffer = textBuffer ?? throw new ArgumentNullException(nameof(textBuffer));
            _oleServiceProvider = oleServiceProvider ?? throw new ArgumentNullException(nameof(oleServiceProvider));
        }

        /// <summary>The primary <c>IVsTextView</c> of the embedded code window, once created - null before <see cref="BuildWindowCore"/> runs or after teardown.</summary>
        public IVsTextView? PrimaryView { get; private set; }

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            // HwndHost's Build/DestroyWindowCore always run on the UI thread (they are part of WPF
            // layout/visual-tree attachment); asserting it explicitly is what the VS SDK analyzers
            // expect around STA-affinitized COM interfaces like IVsWindowPane/IVsCodeWindow.
            ThreadHelper.ThrowIfNotOnUIThread();

            // Created (and sited) through the editor adapters factory - `new VsCodeWindowClass()` fails
            // with REGDB_E_CLASSNOTREG inside devenv (see KbviewEditorFactory.GetEditorAdapters).
            _codeWindow = EditorFactory.KbviewEditorFactory.GetEditorAdapters(_oleServiceProvider).CreateVsCodeWindowAdapter(_oleServiceProvider);

            // The buffer may still be unloaded here (a never-opened document is loaded by the shell
            // only after CreateEditorInstance returns); the code window adapter supports that and
            // creates its text view once the buffer is initialized.
            ErrorHandler.ThrowOnFailure(_codeWindow.SetBuffer(_textBuffer));

            var windowPane = (IVsWindowPane)_codeWindow;
            ErrorHandler.ThrowOnFailure(windowPane.CreatePaneWindow(hwndParent.Handle, 0, 0, 0, 0, out var hwnd));
            _childHwnd = hwnd;

            // Best-effort only: a fresh IVsCodeWindow may not have a primary view until the shell
            // finishes laying it out. Callers (DSG-8's selection sync) must tolerate null here and
            // re-query later rather than assume it is populated immediately after construction.
            if (ErrorHandler.Failed(_codeWindow.GetPrimaryView(out var primaryView)))
            {
                primaryView = null;
            }

            PrimaryView = primaryView;

            return new HandleRef(this, hwnd);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            PrimaryView = null;

            if (_codeWindow != null)
            {
                try
                {
                    ((IVsWindowPane)_codeWindow).ClosePane();
                    _codeWindow.Close();
                }
                catch (COMException)
                {
                    // Best-effort teardown: the pane/window may already be gone if the document was force-closed.
                }

                if (Marshal.IsComObject(_codeWindow))
                {
                    Marshal.ReleaseComObject(_codeWindow);
                }

                _codeWindow = null;
            }

            _childHwnd = IntPtr.Zero;
        }

        protected override void OnWindowPositionChanged(Rect rcBoundingBox)
        {
            if (_childHwnd != IntPtr.Zero && rcBoundingBox.Width >= 0 && rcBoundingBox.Height >= 0)
            {
                NativeMethods.SetWindowPos(
                    _childHwnd,
                    IntPtr.Zero,
                    0,
                    0,
                    (int)rcBoundingBox.Width,
                    (int)rcBoundingBox.Height,
                    NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            }

            base.OnWindowPositionChanged(rcBoundingBox);
        }
    }
}
