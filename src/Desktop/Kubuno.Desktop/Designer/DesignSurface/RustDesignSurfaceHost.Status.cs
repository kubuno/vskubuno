using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Kubuno.Views.Logging;
using Kubuno.Views.Designer;
using Kubuno.Views.Designer.DesignSurface;

namespace Kubuno.Desktop.Designer.DesignSurface
{
    /// <summary>
    /// The status half of <see cref="RustDesignSurfaceHost"/> (docs/DESIGNER.md section 17): the
    /// <c>renderStatus</c> lines, the last preview kept on screen while the surface restarts (a snapshot of
    /// its window, painted in the container until the new window appears), and the detection of a surface
    /// that keeps crashing.
    /// </summary>
    public sealed partial class RustDesignSurfaceHost : IDesignSurfaceStatusAware
    {
        /// <summary>Unexpected exits in a row, each within <see cref="StableAfterMs"/> of its start, before the pane says so.</summary>
        private const int FailuresBeforeBar = 3;

        /// <summary>How long after the last change the preview is captured (the surface has painted it by then).</summary>
        private static readonly TimeSpan SnapshotDelay = TimeSpan.FromMilliseconds(700);

        private readonly DispatcherTimer _snapshotTimer = new() { Interval = SnapshotDelay };
        private readonly DispatcherTimer _stableTimer = new() { Interval = TimeSpan.FromMilliseconds(StableAfterMs) };
        private IntPtr _snapshot;
        private int _snapshotWidth;
        private int _snapshotHeight;
        private int _quickFailures;
        private bool _statusTimersHooked;

        public DesignSurfaceRenderStatus? RenderStatus { get; private set; }

        public event EventHandler? RenderStatusChanged;

        public bool IsFailingRepeatedly { get; private set; }

        public event EventHandler? HealthChanged;

        public event EventHandler<DesignSurfaceDiagnostic>? SourceNavigationRequested;

        /// <summary>Whether a snapshot of the last preview is kept (tests, diagnostics).</summary>
        public bool HasPreviewSnapshot => _snapshot != IntPtr.Zero;

        private void HookStatusTimers()
        {
            if (_statusTimersHooked)
            {
                return;
            }

            _statusTimersHooked = true;
            _snapshotTimer.Tick += (_, _) =>
            {
                _snapshotTimer.Stop();
                CaptureSnapshot();
            };
            _stableTimer.Tick += (_, _) =>
            {
                _stableTimer.Stop();
                _quickFailures = 0;
                SetFailing(false);
            };
        }

        /// <summary>Handles a <c>renderStatus</c> line (on the stdout reader's thread); false for any other line.</summary>
        private bool TryHandleRenderStatusLine(string line)
        {
            if (DesignSurfaceProtocol.TryParseGoToSource(line, out var diagnostic) && diagnostic is not null)
            {
#pragma warning disable VSTHRD001, VSTHRD110 // same reasoning as OnSurfaceExited.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!_disposed)
                    {
                        SourceNavigationRequested?.Invoke(this, diagnostic);
                    }
                }));
#pragma warning restore VSTHRD001, VSTHRD110
                return true;
            }

            if (!DesignSurfaceProtocol.TryParseRenderStatus(line, out var status) || status is null)
            {
                return false;
            }

#pragma warning disable VSTHRD001, VSTHRD110 // same reasoning as OnSurfaceExited: no JoinableTaskFactory outside VS.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_disposed)
                {
                    return;
                }

                RenderStatus = status;
                RenderStatusChanged?.Invoke(this, EventArgs.Empty);
                ScheduleSnapshot();
            }));
#pragma warning restore VSTHRD001, VSTHRD110
            return true;
        }

        /// <summary>Captures the preview a little later (after the surface painted the latest change).</summary>
        private void ScheduleSnapshot()
        {
            if (_disposed)
            {
                return;
            }

            HookStatusTimers();
            _snapshotTimer.Stop();
            _snapshotTimer.Start();
        }

        /// <summary>A surface was started: once it has run <see cref="StableAfterMs"/>, the crash count starts over.</summary>
        private void OnSurfaceLaunched()
        {
            HookStatusTimers();
            _stableTimer.Stop();
            _stableTimer.Start();
        }

        /// <summary>The surface exited unexpectedly; <paramref name="stable"/>: it had run long enough to count as healthy.</summary>
        private void OnSurfaceCrashed(bool stable)
        {
            _stableTimer.Stop();
            _quickFailures = stable ? 1 : _quickFailures + 1;
            KubunoViewsLogHost.Current.WriteLine($"[designer] design surface crash #{_quickFailures} in a row; the last preview stays on screen until it restarts.");
            if (_container != IntPtr.Zero)
            {
                // The container shows the last preview now (WM_PAINT below).
                StatusNative.InvalidateRect(_container, IntPtr.Zero, true);
            }

            if (_quickFailures >= FailuresBeforeBar)
            {
                SetFailing(true);
            }
        }

        private void SetFailing(bool failing)
        {
            if (IsFailingRepeatedly == failing)
            {
                return;
            }

            IsFailingRepeatedly = failing;
            HealthChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Starts the surface again now, whatever the backoff (the « Relancer » action of the pane's info bar).</summary>
        public void Restart()
        {
            if (_disposed)
            {
                return;
            }

            KubunoViewsLogHost.Current.WriteLine("[designer] design surface restarted by the user.");
            _quickFailures = 0;
            SetFailing(false);
            _backoffMs = BackoffInitialMs;
            _retryTimer.Stop();
            var previous = _surface;
            _surface = null;
            StopProcess(previous);
            if (_container != IntPtr.Zero)
            {
                StartOrShowProblem();
            }
        }

        /// <summary>
        /// Copies what the surface's window shows into <see cref="_snapshot"/>: through
        /// <c>PrintWindow(PW_RENDERFULLCONTENT)</c> (the surface draws with a flip-model swap chain, which a
        /// plain window DC does not hold), else from the screen when the window is visible there.
        /// </summary>
        private void CaptureSnapshot()
        {
            var child = Child;
            if (_disposed || child == IntPtr.Zero || !StatusNative.IsWindowVisible(child) || !StatusNative.GetClientRect(child, out var rc))
            {
                return;
            }

            int w = rc.Right - rc.Left, h = rc.Bottom - rc.Top;
            if (w <= 0 || h <= 0)
            {
                return;
            }

            var screen = StatusNative.GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero)
            {
                return;
            }

            var memory = StatusNative.CreateCompatibleDC(screen);
            var bitmap = StatusNative.CreateCompatibleBitmap(screen, w, h);
            var captured = false;
            try
            {
                var old = StatusNative.SelectObject(memory, bitmap);
                captured = StatusNative.PrintWindow(child, memory, StatusNative.PW_CLIENTONLY | StatusNative.PW_RENDERFULLCONTENT) && !IsBlank(memory, w, h);
                if (!captured)
                {
                    var origin = new StatusNative.POINT { X = 0, Y = 0 };
                    StatusNative.ClientToScreen(child, ref origin);
                    captured = StatusNative.BitBlt(memory, 0, 0, w, h, screen, origin.X, origin.Y, StatusNative.SRCCOPY) && !IsBlank(memory, w, h);
                }

                StatusNative.SelectObject(memory, old);
            }
            finally
            {
                StatusNative.DeleteDC(memory);
                StatusNative.ReleaseDC(IntPtr.Zero, screen);
            }

            if (!captured)
            {
                StatusNative.DeleteObject(bitmap);
                return;
            }

            ReleaseSnapshot();
            _snapshot = bitmap;
            _snapshotWidth = w;
            _snapshotHeight = h;
        }

        /// <summary>Whether a few sampled pixels are all black (a capture that did not get the content).</summary>
        private static bool IsBlank(IntPtr dc, int w, int h)
        {
            for (var i = 1; i <= 4; i++)
            {
                for (var j = 1; j <= 4; j++)
                {
                    if ((StatusNative.GetPixel(dc, w * i / 5, h * j / 5) & 0xFFFFFF) != 0)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private void ReleaseSnapshot()
        {
            if (_snapshot != IntPtr.Zero)
            {
                StatusNative.DeleteObject(_snapshot);
                _snapshot = IntPtr.Zero;
            }
        }

        /// <summary>
        /// The container's <c>WM_PAINT</c> while the surface's window is gone (a crash, a restart, the hot swap
        /// onto the project's runtime): the last preview, with a thin band saying it restarts - never a blank
        /// pane. Left to the "Static" control when there is no snapshot or when it shows a message (a missing
        /// runtime, a refused one).
        /// </summary>
        private bool TryPaintSnapshot(IntPtr hwnd)
        {
            if (_snapshot == IntPtr.Zero || Child != IntPtr.Zero || StatusNative.GetWindowTextLength(hwnd) > 0)
            {
                return false;
            }

            var ps = default(StatusNative.PAINTSTRUCT);
            var dc = StatusNative.BeginPaint(hwnd, ref ps);
            if (dc == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                StatusNative.GetClientRect(hwnd, out var rc);
                var background = StatusNative.GetSysColorBrush(StatusNative.COLOR_WINDOW);
                StatusNative.FillRect(dc, ref rc, background);
                var memory = StatusNative.CreateCompatibleDC(dc);
                var old = StatusNative.SelectObject(memory, _snapshot);
                StatusNative.BitBlt(dc, 0, 0, Math.Min(_snapshotWidth, rc.Right), Math.Min(_snapshotHeight, rc.Bottom), memory, 0, 0, StatusNative.SRCCOPY);
                StatusNative.SelectObject(memory, old);
                StatusNative.DeleteDC(memory);

                var band = new StatusNative.RECT { Left = 0, Top = 0, Right = rc.Right, Bottom = 24 };
                StatusNative.FillRect(dc, ref band, StatusNative.GetSysColorBrush(StatusNative.COLOR_INFOBK));
                StatusNative.SetBkMode(dc, StatusNative.TRANSPARENT);
                StatusNative.SetTextColor(dc, StatusNative.GetSysColor(StatusNative.COLOR_INFOTEXT));
                var text = new StatusNative.RECT { Left = 8, Top = 0, Right = rc.Right - 8, Bottom = 24 };
                var message = IsFailingRepeatedly ? DesignerText.SurfaceFailing : DesignerText.SurfaceRestarting;
                StatusNative.DrawText(dc, message, -1, ref text, StatusNative.DT_SINGLELINE | StatusNative.DT_VCENTER | StatusNative.DT_END_ELLIPSIS | StatusNative.DT_NOPREFIX);
            }
            finally
            {
                StatusNative.EndPaint(hwnd, ref ps);
            }

            return true;
        }

        /// <summary>The GDI calls of the snapshot (kept here rather than in the shared <c>NativeMethods</c>).</summary>
        private static class StatusNative
        {
            public const uint PW_CLIENTONLY = 0x1;
            public const uint PW_RENDERFULLCONTENT = 0x2;
            public const int SRCCOPY = 0x00CC0020;
            public const int COLOR_WINDOW = 5;
            public const int COLOR_INFOTEXT = 23;
            public const int COLOR_INFOBK = 24;
            public const int TRANSPARENT = 1;
            public const uint DT_VCENTER = 0x4;
            public const uint DT_SINGLELINE = 0x20;
            public const uint DT_NOPREFIX = 0x800;
            public const uint DT_END_ELLIPSIS = 0x8000;

            [StructLayout(LayoutKind.Sequential)]
            public struct RECT
            {
                public int Left;
                public int Top;
                public int Right;
                public int Bottom;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct POINT
            {
                public int X;
                public int Y;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct PAINTSTRUCT
            {
                public IntPtr Hdc;
                public int Erase;
                public RECT Paint;
                public int Restore;
                public int IncUpdate;
                [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
                public byte[] Reserved;
            }

            [DllImport("user32.dll")]
            public static extern IntPtr GetDC(IntPtr hwnd);

            [DllImport("user32.dll")]
            public static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool IsWindowVisible(IntPtr hwnd);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, [MarshalAs(UnmanagedType.Bool)] bool erase);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            public static extern int GetWindowTextLength(IntPtr hwnd);

            [DllImport("user32.dll")]
            public static extern IntPtr BeginPaint(IntPtr hwnd, ref PAINTSTRUCT paint);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT paint);

            [DllImport("user32.dll")]
            public static extern int FillRect(IntPtr dc, ref RECT rect, IntPtr brush);

            [DllImport("user32.dll")]
            public static extern IntPtr GetSysColorBrush(int index);

            [DllImport("user32.dll")]
            public static extern int GetSysColor(int index);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            public static extern int DrawText(IntPtr dc, string text, int length, ref RECT rect, uint format);

            [DllImport("gdi32.dll")]
            public static extern IntPtr CreateCompatibleDC(IntPtr dc);

            [DllImport("gdi32.dll")]
            public static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);

            [DllImport("gdi32.dll")]
            public static extern IntPtr SelectObject(IntPtr dc, IntPtr gdiObject);

            [DllImport("gdi32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, int rop);

            [DllImport("gdi32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool DeleteDC(IntPtr dc);

            [DllImport("gdi32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool DeleteObject(IntPtr gdiObject);

            [DllImport("gdi32.dll")]
            public static extern uint GetPixel(IntPtr dc, int x, int y);

            [DllImport("gdi32.dll")]
            public static extern int SetBkMode(IntPtr dc, int mode);

            [DllImport("gdi32.dll")]
            public static extern int SetTextColor(IntPtr dc, int color);
        }
    }
}
