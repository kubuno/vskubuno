using System;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Media;
using Kubuno.Shared.Logging;
using Kubuno.Views.Designer.DesignSurface;
using Kubuno.Views.Designer.Toolbox;
using Kubuno.Web.Logic.WebDesigner;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using OleInterop = Microsoft.VisualStudio.OLE.Interop;

namespace Kubuno.Web.WebDesigner
{
    /// <summary>
    /// Keyboard, frame activation and Toolbox drops of the web design surface - the mechanisms measured in the WV-9a spike
    /// (docs/WEB-VIEWS.md section 11): keys typed in the WebView2 never reach Visual Studio's message loop, so the
    /// accelerators WebView2 reports are routed (<see cref="AcceleratorRouting.RouteDesignSurface"/>) and Visual Studio's
    /// bound commands posted; a click in the page does not activate the document frame, so every page gesture does; a
    /// Toolbox drag is detected by Chromium, then taken over by Visual Studio's OLE drop target on a nearly transparent
    /// child window laid over the browser (OLE never reaches a target registered on the browser's own windows).
    /// </summary>
    internal sealed partial class WebDesignSurfaceHost
    {
        private WebToolboxDropTarget? _dropTarget;
        private IntPtr _dropWindow;
        private System.Windows.Threading.DispatcherTimer? _overlayTimer;
        private double _devicePixelRatio;

        // ---- keys ----

        private void OnWebViewPreviewKeyDown(object sender, KeyEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            {
                return;
            }

            var virtualKey = KeyInterop.VirtualKeyFromKey(key);
            var control = VsKeyBindings.IsDown(0x11);
            var shift = VsKeyBindings.IsDown(0x10);
            var alt = VsKeyBindings.IsDown(0x12);
            var route = _designMode
                ? AcceleratorRouting.RouteDesignSurface(virtualKey, control, shift, alt, _pageEditingText)
                : AcceleratorRouting.Route(virtualKey, control, shift, alt, _pageEditingText);
            if (route == KeyRoute.Page)
            {
                return;
            }

            ActivateFrame("key");
            if (VsKeyBindings.TryFind(_webView.Handle, virtualKey, alt, out var group, out var id, out var diagnostic) && VsKeyBindings.Post(group, id))
            {
                KubunoLog.WriteLine($"[web-designer] key {(control ? "Ctrl+" : string.Empty)}{(shift ? "Shift+" : string.Empty)}{(alt ? "Alt+" : string.Empty)}{key} -> {VsKeyBindings.NameOf(group, id)}");
                e.Handled = true;
            }
            else
            {
                KubunoLog.WriteLine($"[web-designer] key {key}: no Visual Studio binding, left to the page ({diagnostic})");
            }
        }

        // ---- activation ----

        /// <summary>
        /// Makes this document Visual Studio's active window on a gesture in the page (WV-9a: a click in the WebView2 lands in
        /// a browser-process window the window manager does not see, so commands posted afterwards - Edit.Undo, Delete, the
        /// Properties window - would go to the previously active window). Never on the browser's own focus events.
        /// </summary>
        private void ActivateFrame(string why)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (_disposed ||
                    !VsShellUtilities.IsDocumentOpen(ServiceProvider.GlobalProvider, DocumentPath, Guid.Empty, out _, out _, out var frame) || frame is null ||
                    Package.GetGlobalService(typeof(SVsShellMonitorSelection)) is not IVsMonitorSelection selection)
                {
                    return;
                }

                if (selection.GetCurrentElementValue((uint)VSConstants.VSSELELEMID.SEID_WindowFrame, out var active) == VSConstants.S_OK && ReferenceEquals(active, frame))
                {
                    return;
                }

                frame.Show();
                KubunoLog.WriteLine($"[web-designer] document frame activated ({why}).");
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
            {
                KubunoLog.WriteException("[web-designer] activating the document frame failed", ex);
            }
        }

        // ---- Toolbox drops (the "Hybrid" channel of docs/WEB-VIEWS.md section 11) ----

        private void CreateDropOverlay()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _dropTarget = new WebToolboxDropTarget(CursorInPage, Post, HideDropOverlay, () => ActivateFrame("drop"));
            _dropWindow = CreateWindowEx(WsExLayered, "Static", null, WsChild | WsClipSiblings | SsNotify, 0, 0, 1, 1, _webView.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (_dropWindow == IntPtr.Zero)
            {
                KubunoLog.WriteLine("[web-designer] could not create the drop overlay window: Toolbox drops are off (double-click a Toolbox item instead).");
                return;
            }

            SetLayeredWindowAttributes(_dropWindow, 0, 1, LwaAlpha);
            var hr = RegisterDragDrop(_dropWindow, _dropTarget);
            if (hr != 0)
            {
                KubunoLog.WriteLine($"[web-designer] RegisterDragDrop on the drop overlay failed (0x{hr:X8}).");
            }

            _overlayTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _overlayTimer.Tick += (_, _) =>
            {
                // Safety net: a drag that ended elsewhere without a DragLeave/Drop on the overlay.
                if ((GetAsyncKeyState(0x01) & 0x8000) == 0 && (GetAsyncKeyState(0x02) & 0x8000) == 0)
                {
                    HideDropOverlay();
                }
            };
        }

        /// <summary>The page reported a Toolbox drag: lay the overlay over it so that Visual Studio's drop target takes the drag.</summary>
        private void ShowDropOverlay()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_dropWindow == IntPtr.Zero || !GetClientRect(_webView.Handle, out var rect))
            {
                return;
            }

            _devicePixelRatio = VisualTreeHelper.GetDpi(_webView).DpiScaleX;
            SetWindowPos(_dropWindow, IntPtr.Zero, 0, 0, rect.Right, rect.Bottom, SwpShowWindow | SwpNoActivate);
            _overlayTimer?.Start();
        }

        private void HideDropOverlay()
        {
            _overlayTimer?.Stop();
            if (_dropWindow != IntPtr.Zero && IsWindowVisible(_dropWindow))
            {
                ShowWindow(_dropWindow, 0);
            }
        }

        private void DestroyDropOverlay()
        {
            _overlayTimer?.Stop();
            if (_dropWindow != IntPtr.Zero)
            {
                RevokeDragDrop(_dropWindow);
                DestroyWindow(_dropWindow);
                _dropWindow = IntPtr.Zero;
            }
        }

        private void SetDropTargetValid(bool valid)
        {
            if (_dropTarget is not null)
            {
                _dropTarget.TargetValid = valid;
            }
        }

        /// <summary>The cursor in page CSS pixels (physical client pixels of the WebView2 window / its DPI scale), null when unknown.</summary>
        private (double X, double Y)? CursorInPage()
        {
            if (!GetCursorPos(out var point) || _dropWindow == IntPtr.Zero || !ScreenToClient(_dropWindow, ref point))
            {
                return null;
            }

            var scale = _devicePixelRatio > 0 ? _devicePixelRatio : 1.0;
            return (point.X / scale, point.Y / scale);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const int WsExLayered = 0x00080000;
        private const int WsChild = 0x40000000;
        private const int WsClipSiblings = 0x04000000;
        private const int SsNotify = 0x00000100;
        private const uint LwaAlpha = 0x2;
        private const uint SwpShowWindow = 0x0040;
        private const uint SwpNoActivate = 0x0010;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out NativePoint point);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ScreenToClient(IntPtr hwnd, ref NativePoint point);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(int exStyle, string className, string? windowName, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hwnd, int command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int key);

        [DllImport("ole32.dll")]
        private static extern int RegisterDragDrop(IntPtr hwnd, OleInterop.IDropTarget target);

        [DllImport("ole32.dll")]
        private static extern int RevokeDragDrop(IntPtr hwnd);
    }

    /// <summary>
    /// Visual Studio's OLE drop target over the web page during a Toolbox drag: reads the Kubuno component of the Toolbox
    /// item (<see cref="ToolboxDataObjectReader"/>) and drives the page with the desktop protocol's
    /// <c>dragEnter/dragOver/drop/dragLeave</c> (page CSS pixels); the page answers <c>dropTargetChanged</c> (whose
    /// validity is the drop effect) and posts the <c>insertChild</c> edit request itself.
    /// </summary>
    internal sealed class WebToolboxDropTarget : OleInterop.IDropTarget
    {
        private const uint DropEffectNone = 0;
        private const uint DropEffectCopy = 1;

        private readonly Func<(double X, double Y)?> _toPage;
        private readonly Action<string> _post;
        private readonly Action _ended;
        private readonly Action _dropped;
        private string? _component;
        private (double X, double Y)? _last;

        public WebToolboxDropTarget(Func<(double X, double Y)?> toPage, Action<string> post, Action ended, Action dropped)
        {
            _toPage = toPage;
            _post = post;
            _ended = ended;
            _dropped = dropped;
        }

        /// <summary>The page's last answer: whether a drop here is allowed.</summary>
        public bool TargetValid { get; set; } = true;

        public void DragEnter(OleInterop.IDataObject pDataObj, uint grfKeyState, OleInterop.POINTL pt, ref uint pdwEffect)
        {
            _component = ToolboxDataObjectReader.TryGetComponent(pDataObj, out var name) ? name : null;
            _last = null;
            if (_component is null)
            {
                pdwEffect = DropEffectNone;
                return;
            }

            TargetValid = false;
            _post(DesignSurfaceDragDropProtocol.EncodeDragEnter(_component));
            Over(ref pdwEffect);
        }

        public void DragOver(uint grfKeyState, OleInterop.POINTL pt, ref uint pdwEffect)
        {
            if (_component is null)
            {
                pdwEffect = DropEffectNone;
                return;
            }

            Over(ref pdwEffect);
        }

        public void DragLeave()
        {
            if (_component is not null)
            {
                _post(DesignSurfaceDragDropProtocol.EncodeDragLeave());
            }

            _component = null;
            _ended();
        }

        public void Drop(OleInterop.IDataObject pDataObj, uint grfKeyState, OleInterop.POINTL pt, ref uint pdwEffect)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var component = _component;
            _component = null;
            _ended();
            if (component is null || _toPage() is not { } point || !TargetValid)
            {
                pdwEffect = DropEffectNone;
                _post(DesignSurfaceDragDropProtocol.EncodeDragLeave());
                return;
            }

            _dropped();
            _post(DesignSurfaceDragDropProtocol.EncodeDrop(point.X, point.Y));
            pdwEffect = DropEffectCopy;
            (Package.GetGlobalService(typeof(SVsToolbox)) as IVsToolbox)?.DataUsed();
        }

        private void Over(ref uint effect)
        {
            if (_toPage() is not { } point)
            {
                effect = DropEffectNone;
                return;
            }

            if (_last is not { } last || Math.Abs(last.X - point.X) >= 0.5 || Math.Abs(last.Y - point.Y) >= 0.5)
            {
                _last = point;
                _post(DesignSurfaceDragDropProtocol.EncodeDragOver(point.X, point.Y));
            }

            effect = TargetValid ? DropEffectCopy : DropEffectNone;
        }
    }
}
