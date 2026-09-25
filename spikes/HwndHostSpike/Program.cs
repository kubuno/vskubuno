using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace HwndHostSpike
{
    /// <summary>
    /// DSG-7 spike: <c>HwndHostSpike.exe --exe view_embed.exe --view file.kbview [--selftest] [--close] [--log path]</c>.
    /// With <c>--selftest</c> it drives focus/keyboard/popup/capture/resize/crash
    /// probes itself (real input through the system queue) and logs PASS/INFO lines.
    /// </summary>
    public static class Program
    {
        private const uint SEM_FAILCRITICALERRORS = 0x0001, SEM_NOOPENFILEERRORBOX = 0x8000;
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern uint SetErrorMode(uint mode);

        [STAThread]
        public static int Main(string[] args)
        {
            // Inherited by the child process: a missing DLL makes it fail with
            // an exit code instead of popping the loader's modal dialog.
            SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOOPENFILEERRORBOX);
            string Arg(string name, string dflt) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : dflt; }
            var exe = Arg("--exe", @"C:\kubuno-build\agent-dsg7\debug\examples\view_embed.exe");
            var view = Arg("--view", @"Z:\projects\kubuno\desktop\windows\src\crates\kubuno-views\examples\views\settings.kbview");
            var logPath = Arg("--log", Path.Combine(Path.GetTempPath(), "hwndhostspike.log"));
            File.WriteAllText(logPath, "");
            var app = new Application();
            var win = new SpikeWindow(exe, view, logPath, args.Contains("--selftest"), args.Contains("--close"));
            return app.Run(win);
        }
    }

    internal sealed class SpikeWindow : Window
    {
        private readonly string _logPath;
        private readonly object _gate = new object();
        private readonly List<string> _rustLines = new List<string>();
        private readonly DesignSurfaceHost _host;
        private readonly TextBox _before, _after;
        private bool _ctrlS;

        public SpikeWindow(string exe, string view, string logPath, bool selftest, bool close)
        {
            _logPath = logPath;
            Title = "HwndHost spike (DSG-7)";
            Width = 900; Height = 640;
            _before = new TextBox { Text = "WPF before", Margin = new Thickness(4) };
            _after = new TextBox { Text = "WPF after", Margin = new Thickness(4) };
            _host = new DesignSurfaceHost(exe, "\"" + view + "\"", Log);
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            _before.Width = 200; _after.Width = 200;
            top.Children.Add(_before); top.Children.Add(_after);
            var dock = new DockPanel();
            DockPanel.SetDock(top, Dock.Top);
            dock.Children.Add(top);
            dock.Children.Add(new Border { BorderThickness = new Thickness(2), BorderBrush = System.Windows.Media.Brushes.SteelBlue, Child = _host });
            Content = dock;
            // A stand-in for a VS accelerator (Ctrl+S = File.Save).
            InputBindings.Add(new KeyBinding(new Relay(() => { _ctrlS = true; Log("WPF KeyBinding Ctrl+S fired"); }), Key.S, ModifierKeys.Control));
            Log($"spike pid {Process.GetCurrentProcess().Id}, exe {exe}");
            if (selftest)
            {
                async void Once() { _host.ChildReady -= Once; await SelfTest(close); }
                _host.ChildReady += Once;
            }
            Closed += (_, __) => Log("window closed");
        }

        private void Log(string line)
        {
            lock (_gate)
            {
                if (line.StartsWith("  rust|")) _rustLines.Add(line);
                File.AppendAllText(_logPath, DateTime.Now.ToString("HH:mm:ss.fff ") + line + Environment.NewLine);
            }
        }

        private bool RustSaid(string needle) { lock (_gate) return _rustLines.Any(l => l.Contains(needle)); }
        private void ClearRust() { lock (_gate) _rustLines.Clear(); }
        private IntPtr Main => new WindowInteropHelper(this).Handle;
        private static Task Wait(int ms) => Task.Delay(ms);

        private static async Task ClickAsync(int x, int y)
        {
            Native.SetCursorPos(x, y);
            Native.mouse_event(Native.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            // Held long enough for the immediate-mode page to paint a frame
            // with the button down (a press is an edge between two frames).
            await Task.Delay(120);
            Native.mouse_event(Native.MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        }

        private static void Press(byte vk, bool ctrl = false)
        {
            if (ctrl) Native.keybd_event(0x11, 0, 0, UIntPtr.Zero);
            Native.keybd_event(vk, 0, 0, UIntPtr.Zero);
            Native.keybd_event(vk, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
            if (ctrl) Native.keybd_event(0x11, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private void Check(string name, bool ok, string detail = "") => Log($"{(ok ? "PASS" : "FAIL")} {name} {detail}");

        private List<IntPtr> Overlays()
        {
            var list = new List<IntPtr>();
            var pid = (uint)_host.Surface.Id;
            Native.EnumWindows((h, _) =>
            {
                Native.GetWindowThreadProcessId(h, out var p);
                if (p == pid && Native.IsWindowVisible(h) && Native.ClassOf(h) == "KubunoControlsOverlay") list.Add(h);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        private async Task SelfTest(bool close)
        {
            try
            {
                await Wait(800);
                var child = _host.Child;
                Native.GetWindowThreadProcessId(child, out var childPid);
                Native.GetWindowThreadProcessId(Main, out var mainPid);
                int Aw(IntPtr h) => Native.GetAwarenessFromDpiAwarenessContext(Native.GetWindowDpiAwarenessContext(h));
                Log($"INFO child {Native.Hex(child)} pid {childPid} (main pid {mainPid}); parent==container {Native.GetParent(child) == _host.Container}; root==main {Native.GetAncestor(child, 2) == Main}");
                Log($"INFO dpi main {Native.GetDpiForWindow(Main)} child {Native.GetDpiForWindow(child)}; awareness main {Aw(Main)} child {Aw(child)} (2 = per-monitor)");
                Check("cross-process child created", child != IntPtr.Zero && childPid != mainPid);

                Topmost = true;
                Activate();
                Native.SetForegroundWindow(Main);
                await Wait(300);
                Log($"INFO foreground==main {Native.GetForegroundWindow() == Main}");

                // 1. Click focuses the child.
                Native.GetWindowRect(child, out var cr);
                Log($"INFO child rect {cr}");
                await ClickAsync((cr.Left + cr.Right) / 2, cr.Bottom - 20);
                await Wait(400);
                var ti = Native.ThreadInfo(child);
                Check("click moves keyboard focus into child", ti.hwndFocus == child, $"focus={Native.Hex(ti.hwndFocus)}; main still active={Native.ThreadInfo(Main).hwndActive == Main}");

                // 2. Keys reach the child.
                ClearRust();
                Press(0x41);
                await Wait(300);
                Check("key 'A' delivered to child", RustSaid("key vk 0x41"));

                // 3. A WPF accelerator while the child has focus.
                _ctrlS = false; ClearRust();
                Press(0x53, ctrl: true);
                await Wait(300);
                Log($"INFO Ctrl+S with child focused: WPF KeyBinding fired={_ctrlS}, child saw S={RustSaid("key vk 0x53")}");

                // 4. Tab from WPF into the host.
                _before.Focus();
                await Wait(200);
                Press(0x09); // Tab: before -> after
                await Wait(200);
                Press(0x09); // after -> host?
                await Wait(400);
                ti = Native.ThreadInfo(child);
                Check("Tab from WPF enters the child (TabIntoCore)", ti.hwndFocus == child, $"focus={Native.Hex(ti.hwndFocus)} wpf={Keyboard.FocusedElement}");

                // 5. Tab inside the child: does focus ever come back to WPF?
                for (var i = 0; i < 6; i++) { Press(0x09); await Wait(150); }
                ti = Native.ThreadInfo(child);
                Log($"INFO after 6 Tabs inside child: focus still in child={ti.hwndFocus == child} (no Tab-out protocol yet)");

                // 6. Popup (menu) overflowing the child, owned top-level.
                var scale = Native.GetDpiForWindow(child) / 96.0;
                Native.GetWindowRect(child, out cr);
                async Task OpenMenu() { await ClickAsync(cr.Right - (int)(48 * scale), cr.Top + (int)(14 * scale)); await Wait(600); }
                await OpenMenu();
                var ov = Overlays();
                if (ov.Count == 0) Check("popup shown", false);
                foreach (var o in ov)
                {
                    Native.GetWindowRect(o, out var orc);
                    var owner = Native.GetWindow(o, Native.GW_OWNER);
                    Check("popup shown, overflows the child", orc.Right > cr.Right || orc.Bottom > cr.Bottom, $"popup {orc} vs child {cr}");
                    Log($"INFO popup owner {Native.Hex(owner)} (child {Native.Hex(child)}, main {Native.Hex(Main)}): owner==main {owner == Main}, owner==child {owner == child}");
                }
                // Click item 2 in the part of the popup that lies OUTSIDE the child.
                ClearRust();
                await ClickAsync(cr.Right + (int)(60 * scale), cr.Top + (int)((26 + 2 + 4 + 28 + 14) * scale));
                await Wait(500);
                Check("click on the overflowing part of the popup reaches the child's page", RustSaid("popup item 2 clicked"));
                Log($"INFO focus after popup click still in child={Native.ThreadInfo(child).hwndFocus == child}");
                // Reopen, then click on a WPF element: focus leaves the child -> blur -> menu closes.
                await OpenMenu();
                Log($"INFO reopened overlays={Overlays().Count}");
                var p = _after.PointToScreen(new Point(10, 5));
                await ClickAsync((int)p.X, (int)p.Y);
                await Wait(600);
                Check("popup dismissed when focus leaves the child", Overlays().Count == 0, $"visible overlays={Overlays().Count}");

                // 7. Mouse capture while dragging out of the child.
                Native.GetWindowRect(child, out cr);
                Native.SetCursorPos((cr.Left + cr.Right) / 2, (cr.Top + cr.Bottom) / 2);
                Native.mouse_event(Native.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                await Wait(150);
                Native.SetCursorPos(cr.Right + 200, cr.Bottom + 100);
                await Wait(200);
                ti = Native.ThreadInfo(child);
                Check("drag keeps capture in child outside its rect", ti.hwndCapture == child, $"capture={Native.Hex(ti.hwndCapture)}");
                Native.mouse_event(Native.MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                await Wait(150);

                // 8. Resize follows.
                foreach (var (w, h) in new[] { (700.0, 500.0), (1100.0, 800.0), (900.0, 640.0) })
                {
                    Width = w; Height = h;
                    await Wait(400);
                    Native.GetClientRect(_host.Container, out var c1);
                    Native.GetWindowRect(_host.Child, out var c2);
                    Check($"child follows container at {w}x{h}", c1.Right == c2.Right - c2.Left && c1.Bottom == c2.Bottom - c2.Top, $"container {c1} child {c2}");
                }
                ClearRust();
                // 9. Move to each monitor (DPI change if the monitors differ).
                var origin = (Left, Top);
                foreach (var s in System.Windows.Forms.Screen.AllScreens)
                {
                    var dpiBefore = Native.GetDpiForWindow(_host.Child);
                    Native.SetWindowPos(Main, IntPtr.Zero, s.WorkingArea.Left + 50, s.WorkingArea.Top + 50, 0, 0, 0x0001 | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                    await Wait(600);
                    Native.GetClientRect(_host.Container, out var c1);
                    Native.GetWindowRect(_host.Child, out var c2);
                    Log($"INFO on {s.DeviceName}: dpi main {Native.GetDpiForWindow(Main)} child {dpiBefore}->{Native.GetDpiForWindow(_host.Child)}; sizes match {c1.Right == c2.Right - c2.Left && c1.Bottom == c2.Bottom - c2.Top}");
                }
                Log($"INFO WM_DPICHANGED_AFTERPARENT seen by child: {RustSaid("WM_DPICHANGED_AFTERPARENT")}");
                Left = origin.Left; Top = origin.Top;

                // 10. Child crash: host survives, surface restarts.
                var oldPid = _host.Surface.Id;
                _host.Surface.Kill();
                await Wait(700);
                Check("host survives child crash", Native.IsWindow(_host.Container) && IsLoaded, $"container alive={Native.IsWindow(_host.Container)} child gone={_host.Child == IntPtr.Zero}");
                var restarted = new TaskCompletionSource<bool>();
                void OnReady() { _host.ChildReady -= OnReady; restarted.TrySetResult(true); }
                _host.ChildReady += OnReady;
                _host.StartSurface();
                var done = await Task.WhenAny(restarted.Task, Task.Delay(15000));
                Check("surface restarts into the same container", done == restarted.Task && _host.Surface.Id != oldPid);

                Topmost = false;
                Log("SELFTEST DONE");
                if (close)
                {
                    await Wait(500);
                    var pid = _host.Surface.Id;
                    var sw = Stopwatch.StartNew();
                    Close();
                    Log($"INFO after Close(): surface pid {pid} exited={_host.Surface.HasExited} in {sw.ElapsedMilliseconds} ms");
                }
            }
            catch (Exception e)
            {
                Log("SELFTEST ERROR " + e);
            }
        }

        private sealed class Relay : ICommand
        {
            private readonly Action _a;
            public Relay(Action a) { _a = a; }
            public event EventHandler CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object p) => true;
            public void Execute(object p) => _a();
        }
    }
}
