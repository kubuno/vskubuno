using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace HwndHostSpike
{
    /// <summary>
    /// Hosts the Rust view surface (another process) in WPF.
    ///
    /// The HwndHost owns a plain in-process container window (a "Static"
    /// child); the Rust process is started with <c>--parent &lt;container&gt;</c>
    /// and creates its own <c>WS_CHILD</c> inside it. The container is what WPF
    /// lays out; its <c>WM_SIZE</c> resizes the foreign child (cross-process
    /// <c>MoveWindow</c> on a child window is legal).
    /// </summary>
    internal sealed class DesignSurfaceHost : HwndHost
    {
        private readonly string _exe;
        private readonly string _args;
        private readonly Action<string> _log;
        private IntPtr _container;
        private DispatcherTimer _poll;

        public DesignSurfaceHost(string exe, string args, Action<string> log)
        {
            _exe = exe;
            _args = args;
            _log = log;
            Focusable = true;
        }

        public Process Surface { get; private set; }
        public IntPtr Container => _container;
        /// <summary>The foreign child HWND once the Rust process created it.</summary>
        public IntPtr Child => _container == IntPtr.Zero ? IntPtr.Zero : Native.GetWindow(_container, Native.GW_CHILD);
        public event Action ChildReady;

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            _container = Native.CreateWindowEx(0, "Static", "", Native.WS_CHILD | Native.WS_VISIBLE | Native.WS_CLIPCHILDREN,
                0, 0, 1, 1, hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (_container == IntPtr.Zero)
                throw new InvalidOperationException("CreateWindowEx(container) failed: " + Marshal.GetLastWin32Error());
            StartSurface();
            return new HandleRef(this, _container);
        }

        /// <summary>(Re)starts the Rust process into the existing container.</summary>
        public void StartSurface()
        {
            // The workspace links with `-C prefer-dynamic`: the exe needs Rust's
            // std-*.dll and kubuno_ui.dll beside it (tools/stage-runtime.ps1).
            // Missing ones must be a log line, never the loader's modal dialog.
            var dir = Path.GetDirectoryName(_exe);
            if (!File.Exists(_exe) || !File.Exists(Path.Combine(dir, "kubuno_ui.dll")) || Directory.GetFiles(dir, "std-*.dll").Length == 0)
            {
                _log($"surface NOT started: {_exe} or its runtime (kubuno_ui.dll, std-*.dll) is missing; run tools/stage-runtime.ps1");
                return;
            }
            var psi = new ProcessStartInfo(_exe, $"--parent {_container.ToInt64()} {_args}")
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(_exe),
            };
            // kubuno_ui is a Rust dylib: make the build's own copy resolvable.
            psi.EnvironmentVariables["PATH"] = Path.GetDirectoryName(Path.GetDirectoryName(_exe)) + ";" + psi.EnvironmentVariables["PATH"];
            var sw = Stopwatch.StartNew();
            Surface = Process.Start(psi);
            Surface.EnableRaisingEvents = true;
            Surface.ErrorDataReceived += (_, e) => { if (e.Data != null) _log("  rust| " + e.Data); };
            Surface.BeginErrorReadLine();
            Surface.Exited += (_, __) => _log($"surface process exited, code {SafeExitCode()}");
            _log($"started surface pid {Surface.Id}");

            // Wait for the child without blocking the UI thread (the Rust
            // CreateWindowEx sends WM_PARENTNOTIFY etc. to OUR thread; a
            // blocking wait here would deadlock it).
            _poll?.Stop();
            _poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            _poll.Tick += (_, __) =>
            {
                if (Child != IntPtr.Zero)
                {
                    _poll.Stop();
                    _log($"child hwnd {Native.Hex(Child)} appeared after {sw.ElapsedMilliseconds} ms");
                    ChildReady?.Invoke();
                }
                else if (sw.ElapsedMilliseconds > 15000)
                {
                    _poll.Stop();
                    _log("child never appeared (15 s)");
                }
            };
            _poll.Start();
        }

        private string SafeExitCode()
        {
            try { return Surface.ExitCode.ToString(); } catch { return "?"; }
        }

        protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_SIZE)
            {
                var child = Child;
                if (child != IntPtr.Zero)
                {
                    int w = (int)(lParam.ToInt64() & 0xFFFF), h = (int)((lParam.ToInt64() >> 16) & 0xFFFF);
                    Native.MoveWindow(child, 0, 0, w, h, true);
                }
            }
            else if (msg == Native.WM_SETFOCUS)
            {
                // The container itself never wants the keyboard: pass it on.
                var child = Child;
                if (child != IntPtr.Zero) Native.SetFocus(child);
            }
            return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
        }

        // Tab from WPF INTO the surface: WPF asks the sink to take the focus.
        protected override bool TabIntoCore(TraversalRequest request)
        {
            var child = Child;
            if (child == IntPtr.Zero) return false;
            Native.SetFocus(child);
            _log($"TabIntoCore({request.FocusNavigationDirection}) -> SetFocus(child)");
            return true;
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            _poll?.Stop();
            // Destroying the container destroys the foreign child with it
            // (its thread gets WM_DESTROY and its message loop ends).
            Native.DestroyWindow(hwnd.Handle);
            if (Surface != null && !Surface.HasExited && !Surface.WaitForExit(2000))
            {
                _log("surface did not exit within 2 s of DestroyWindow; killing");
                try { Surface.Kill(); } catch { }
            }
        }
    }
}
