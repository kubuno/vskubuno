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
using Kubuno.VisualStudio.Designer.DesignSurface;
using Kubuno.VisualStudio.Views.Logging;

namespace HwndHostSpike
{
    /// <summary>
    /// DSG-7 verification harness: <c>HwndHostSpike.exe --exe view_embed.exe --view file.kbview [--selftest] [--close] [--log path] [--design]</c>.
    /// Drives the PRODUCTION <see cref="RustDesignSurfaceHost"/> (not a spike-local class anymore -
    /// see the csproj's own comment) so <c>--selftest</c> exercises the real job object, restart
    /// backoff, error mode, DLL check and keyboard protocol (<c>unhandledKey</c>/<c>tabOut</c>) without
    /// needing a live <c>devenv.exe</c>. With <c>--selftest</c> it drives focus/keyboard/popup/capture/
    /// resize/crash/keyboard-protocol probes itself (real input through the system queue) and logs
    /// PASS/FAIL/INFO lines.
    ///
    /// <c>--design</c> (DSG-6 visual check): turns design mode on right after the surface's first
    /// `setText` (<see cref="RustDesignSurfaceHost.SetDesignMode"/>) and logs every
    /// <see cref="RustDesignSurfaceHost.SelectionChanged"/>/<see cref="RustDesignSurfaceHost.EditRequested"/>
    /// event to the same log file `--selftest` already greps - a click on the embedded surface should
    /// select the element under it (adorners drawn, a `SelectionChanged` line logged); Delete/an arrow
    /// on an Anchor element should log an `EditRequested` line.
    ///
    /// <c>--simulate-toolbox &lt;Component&gt;</c> (DSG-9 visual check, requires <c>--design</c>): once
    /// the surface's child window is ready, drives the SAME <see cref="RustDesignSurfaceHost.NotifyDragEnter"/>/
    /// <see cref="RustDesignSurfaceHost.NotifyDragOver"/>/<see cref="RustDesignSurfaceHost.NotifyDrop"/>/
    /// <see cref="RustDesignSurfaceHost.NotifyDragLeave"/> calls a real VS Toolbox OLE-drag translation
    /// would (there is no live Toolbox to drag from here) - a few `dragOver` steps walking toward the
    /// drop point, so the insertion marker/ghost has time to visibly update between them, then `drop`.
    /// Logs every <see cref="RustDesignSurfaceHost.DropTargetChanged"/>/
    /// <see cref="RustDesignSurfaceHost.EditRequestsReceived"/>/<see cref="RustDesignSurfaceHost.DragDropEditRequested"/>
    /// event to the same log file, and the marker/ghost itself should be visible on screen while the
    /// simulated drag is in progress.
    /// </summary>
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            // RustDesignSurfaceHost's own static constructor path already calls SetErrorMode before any
            // child process is started (see its EnsureErrorModeSet) - no need to duplicate it here.
            string Arg(string name, string dflt) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : dflt; }
            var exe = Arg("--exe", @"C:\kubuno-build\agent-dsg7b\debug\examples\view_embed.exe");
            var view = Arg("--view", @"Z:\src\desktop\windows\src\crates\kubuno-desktop-views\examples\views\settings.kbview");
            var logPath = Arg("--log", Path.Combine(Path.GetTempPath(), "hwndhostspike.log"));
            var simulateToolbox = Arg("--simulate-toolbox", string.Empty);
            File.WriteAllText(logPath, "");
            KubunoViewsLogHost.Current = new SpikeLogAdapter(logPath);
            var app = new Application();
            var win = new SpikeWindow(exe, view, logPath, args.Contains("--selftest"), args.Contains("--close"), args.Contains("--design"), simulateToolbox);
            return app.Run(win);
        }
    }

    /// <summary>
    /// The ONE place that ever appends to the log file, from either the UI thread
    /// (<see cref="SpikeWindow.Log"/>) or a .NET thread-pool thread
    /// (<see cref="SpikeLogAdapter"/>, reached from <see cref="RustDesignSurfaceHost"/>'s own
    /// <c>ErrorDataReceived</c> handler - a real, live-observed bug: those two classes used to each lock
    /// their OWN separate <c>object</c> around their own <c>File.AppendAllText</c> call, which serialised
    /// each writer against ITSELF but not against the OTHER one, so two truly concurrent appends to the
    /// same file could still race and throw <see cref="IOException"/> ("file in use") - which, unhandled
    /// on that thread-pool thread, took the whole process down mid-selftest). A single shared lock here
    /// is the fix; every writer funnels through this instead of touching the file itself.
    /// </summary>
    internal static class SpikeLog
    {
        private static readonly object Gate = new object();

        public static void Append(string logPath, string line)
        {
            lock (Gate)
            {
                File.AppendAllText(logPath, DateTime.Now.ToString("HH:mm:ss.fff ") + line + Environment.NewLine);
            }
        }
    }

    /// <summary>
    /// Routes <see cref="RustDesignSurfaceHost"/>'s own logging (job object, restart backoff, DLL
    /// check...) into the SAME log file <see cref="SpikeWindow"/> already writes to and
    /// <c>--selftest</c> already greps - the "VSIX supplies a small adapter" pattern
    /// <c>Kubuno.VisualStudio.Views/INTEGRATION.md</c> documents for the real package. Runs on a
    /// thread-pool thread (this class is reached from <c>ErrorDataReceived</c>), so every write goes
    /// through <see cref="SpikeLog"/>'s single shared lock, never its own.
    /// </summary>
    internal sealed class SpikeLogAdapter : IKubunoLog
    {
        private readonly string _logPath;

        public SpikeLogAdapter(string logPath) => _logPath = logPath;

        public void WriteLine(string message) => SpikeLog.Append(_logPath, message);

        public void WriteException(string context, Exception exception) => WriteLine($"{context}: {exception}");
    }

    internal sealed class SpikeWindow : Window
    {
        private readonly string _logPath;
        private readonly object _gate = new object();
        private readonly List<string> _rustLines = new List<string>();
        private readonly RustDesignSurfaceHost _host;
        private readonly TextBox _before, _after;
        private bool _ctrlS;

        public SpikeWindow(string exe, string view, string logPath, bool selftest, bool close, bool design = false, string simulateToolboxComponent = "")
        {
            _logPath = logPath;
            Title = "HwndHost spike (DSG-7)";
            Width = 900; Height = 640;
            _before = new TextBox { Text = "WPF before", Margin = new Thickness(4) };
            _after = new TextBox { Text = "WPF after", Margin = new Thickness(4) };
            // --debug-probe: the probe line + Save/Menu demo buttons this harness's checks rely on
            // (the production designer surface does not show them).
            _host = new RustDesignSurfaceHost(exe, "--debug-probe");
            // DSG-6: `SetDocumentText` now sends a `setText` protocol message over the surface's own
            // stdin (see that method's own doc) instead of writing a temp file - nothing here needed to
            // change to pick that up.
            _host.SetDocumentText(File.ReadAllText(view));
            if (design)
            {
                // DSG-6 visual check: turn design mode on and log every selection/edit-request the
                // surface reports - see this class's own `--design` doc on `Program`.
                _host.SetDesignMode(true);
                _host.SelectionChanged += (_, e) => Log($"SelectionChanged ids=[{string.Join(",", e.ElementIds)}]");
                _host.EditRequested += (_, e) => Log($"EditRequested kind={e.Op.Kind} elementId={e.Op.ElementId} name={e.Op.Name} value={e.Op.Value}");
                // DSG-9 visual check: the batched move/resize form, the single moveElement/insertChild
                // form, and the live toolbox drop-target feedback - see `Program`'s own `--simulate-toolbox` doc.
                _host.EditRequestsReceived += (_, e) => Log($"EditRequestsReceived gesture={e.Gesture} ops=[{FormatOps(e.Ops)}]");
                _host.DragDropEditRequested += (_, e) => Log(
                    $"DragDropEditRequested kind={e.Op.Kind} elementId={e.Op.ElementId} newParentId={e.Op.NewParentId} parentId={e.Op.ParentId} index={e.Op.Index} xml={e.Op.Xml}");
                _host.DropTargetChanged += (_, e) => Log(e.Target == null
                    ? "DropTargetChanged target=null"
                    : $"DropTargetChanged valid={e.Target.Valid} parentId={e.Target.ParentId} index={e.Target.Index} xy=({e.Target.X},{e.Target.Y})");
                Log("design mode ON (--design)");

                if (!string.IsNullOrEmpty(simulateToolboxComponent))
                {
                    // `ChildReady` is a plain `Action` (void handler) - same `async void` shape the
                    // `--selftest` wiring below already uses, and for the same reason.
#pragma warning disable VSTHRD100
                    async void OnceToolbox() { _host.ChildReady -= OnceToolbox; await SimulateToolboxDropAsync(simulateToolboxComponent); }
#pragma warning restore VSTHRD100
                    _host.ChildReady += OnceToolbox;
                }
            }
            // The host owns its OWN stderr capture/logging now (unlike the original spike, where
            // DesignSurfaceHost took a Log callback directly) - re-publish every line into `_rustLines`
            // with the SAME "  rust| " prefix the original spike used, so RustSaid/ClearRust below keep
            // working. Their absence here was a real bug: every "child never saw the key" FAIL in an
            // earlier version of this file was this wiring missing, not a forwarding regression - the
            // surface's own [embed] trace lines were landing in the log file (via KubunoViewsLogHost)
            // but never in `_rustLines`, so RustSaid always returned false.
            _host.SurfaceOutputLine += line => Log("  rust| " + line);
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
                // `ChildReady` is a plain `Action`, so the handler must be void - `async void` is the
                // only shape that fits; SelfTestAsync's own try/catch is what keeps an exception from
                // crashing the process instead of a caller awaiting this one.
#pragma warning disable VSTHRD100
                async void Once() { _host.ChildReady -= Once; await SelfTestAsync(close); }
#pragma warning restore VSTHRD100
                _host.ChildReady += Once;
            }
            Closed += (_, __) => Log("window closed");
        }

        private void Log(string line)
        {
            // `_gate` only ever protects `_rustLines` now - the file write itself goes through
            // SpikeLog's own single shared lock (see that class's doc for the bug this fixes).
            lock (_gate)
            {
                if (line.StartsWith("  rust|")) _rustLines.Add(line);
            }

            SpikeLog.Append(_logPath, line);
        }

        private bool RustSaid(string needle) { lock (_gate) return _rustLines.Any(l => l.Contains(needle)); }
        private void ClearRust() { lock (_gate) _rustLines.Clear(); }
        private IntPtr Main => new WindowInteropHelper(this).Handle;
        private static Task WaitAsync(int ms) => Task.Delay(ms);

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

        /// <summary>
        /// Makes this window the real foreground/active window, robustly enough to survive a
        /// non-interactive launch (a background/automation session, which Windows' foreground-lock
        /// heuristic normally refuses a plain <c>SetForegroundWindow</c> for - a coordinator running this
        /// manually hit exactly that and worked around it by hand before this method existed). Tries, in
        /// order: <c>AllowSetForegroundWindow</c> plus an <c>AttachThreadInput</c>-brokered
        /// <c>SetForegroundWindow</c> (attach to whatever thread currently owns the foreground, act,
        /// detach - the standard robust technique, not just a superficial "GetForegroundWindow now
        /// returns us" check), then a synthetic Alt tap (a well-known way to satisfy the foreground-lock
        /// timeout heuristic) as a last resort. Returns whether it actually worked, checked the only way
        /// that matters for this harness: <c>GetForegroundWindow() == Main</c> afterwards.
        /// </summary>
        private async Task<bool> TryAcquireForegroundAsync()
        {
            Topmost = true;
            Activate();
            Native.AllowSetForegroundWindow(Native.ASFW_ANY);

            var fg = Native.GetForegroundWindow();
            var fgThread = fg != IntPtr.Zero ? Native.GetWindowThreadProcessId(fg, out _) : 0;
            var ourThread = Native.GetCurrentThreadId();
            var attached = fgThread != 0 && fgThread != ourThread && Native.AttachThreadInput(ourThread, fgThread, true);
            try
            {
                Native.BringWindowToTop(Main);
                Native.SetForegroundWindow(Main);
                Native.SetFocus(Main);
            }
            finally
            {
                if (attached)
                {
                    Native.AttachThreadInput(ourThread, fgThread, false);
                }
            }

            await WaitAsync(300);
            var ok = Native.GetForegroundWindow() == Main;
            Log($"INFO foreground==main {ok} (AttachThreadInput used={attached})");
            if (ok)
            {
                return true;
            }

            // Last resort: a synthetic Alt press+release is a well-known way to satisfy the
            // foreground-lock timeout heuristic when a plain SetForegroundWindow is refused outright.
            Native.keybd_event(0x12 /* VK_MENU */, 0, 0, UIntPtr.Zero);
            Native.keybd_event(0x12, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
            Native.SetForegroundWindow(Main);
            await WaitAsync(300);
            ok = Native.GetForegroundWindow() == Main;
            Log($"INFO foreground==main {ok} (after Alt-tap fallback)");
            return ok;
        }

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

        private async Task SelfTestAsync(bool close)
        {
            try
            {
                await WaitAsync(800);
                var child = _host.Child;
                Native.GetWindowThreadProcessId(child, out var childPid);
                Native.GetWindowThreadProcessId(Main, out var mainPid);
                int Aw(IntPtr h) => Native.GetAwarenessFromDpiAwarenessContext(Native.GetWindowDpiAwarenessContext(h));
                Log($"INFO child {Native.Hex(child)} pid {childPid} (main pid {mainPid}); parent==container {Native.GetParent(child) == _host.Container}; root==main {Native.GetAncestor(child, 2) == Main}");
                Log($"INFO dpi main {Native.GetDpiForWindow(Main)} child {Native.GetDpiForWindow(child)}; awareness main {Aw(Main)} child {Aw(child)} (2 = per-monitor)");
                Check("cross-process child created", child != IntPtr.Zero && childPid != mainPid);

                if (!await TryAcquireForegroundAsync())
                {
                    Log("FAIL environment: no foreground - this session denies SetForegroundWindow to this process even with AttachThreadInput/AllowSetForegroundWindow/an Alt-tap fallback, so click/keyboard input cannot reliably reach any window here. Aborting the rest of --selftest instead of cascading unrelated failures.");
                    Log("SELFTEST DONE");
                    return;
                }

                // 1. Click focuses the child.
                Native.GetWindowRect(child, out var cr);
                Log($"INFO child rect {cr}");
                await ClickAsync((cr.Left + cr.Right) / 2, cr.Bottom - 20);
                await WaitAsync(400);
                var ti = Native.ThreadInfo(child);
                Check("click moves keyboard focus into child", ti.hwndFocus == child, $"focus={Native.Hex(ti.hwndFocus)}; main still active={Native.ThreadInfo(Main).hwndActive == Main}");

                // 2. Keys reach the child.
                ClearRust();
                Press(0x41);
                await WaitAsync(300);
                Check("key 'A' delivered to child", RustSaid("key vk 0x41"));

                // 3. A WPF accelerator while the child has focus - the "unhandledKey" protocol:
                // the surface does not consume a bare Ctrl+S, forwards WM_KEYDOWN to the container
                // (kubuno_desktop_controls::host::forward_unhandled_keys), and RustDesignSurfaceHost's WndProc
                // routes it into WPF via ComponentDispatcher.RaiseThreadMessage, firing this window's
                // own KeyBinding - the VS-accelerator stand-in.
                _ctrlS = false; ClearRust();
                Press(0x53, ctrl: true);
                await WaitAsync(300);
                Check("Ctrl+S reaches the WPF handler while the surface has focus (unhandledKey)", _ctrlS, $"child saw S={RustSaid("key vk 0x53")}");

                // 3b. Ctrl+S must not steal native focus away from the child: RaiseWpfKeyEvent's
                // Keyboard.Focus(this) (added to make Ctrl+S itself reach the KeyBinding) sets WPF's
                // LOGICAL focus - if that also moves native Win32 focus onto the container instead of
                // just keeping WPF's bookkeeping in sync, the next real keystroke would go to WPF instead
                // of the surface, breaking ordinary typing right after any VS accelerator.
                await WaitAsync(100);
                var tiAfterCtrlS = Native.ThreadInfo(child);
                Check("focus stays on the child after Ctrl+S", tiAfterCtrlS.hwndFocus == child, $"focus={Native.Hex(tiAfterCtrlS.hwndFocus)}");
                ClearRust();
                Press(0x42); // 'B'
                await WaitAsync(300);
                Check("key 'B' still delivered to child right after Ctrl+S", RustSaid("key vk 0x42"));

                // 4. Tab from WPF into the host.
                _before.Focus();
                await WaitAsync(200);
                Press(0x09); // Tab: before -> after
                await WaitAsync(200);
                Press(0x09); // after -> host?
                await WaitAsync(400);
                ti = Native.ThreadInfo(child);
                Check("Tab from WPF enters the child (TabIntoCore)", ti.hwndFocus == child, $"focus={Native.Hex(ti.hwndFocus)} wpf={Keyboard.FocusedElement}");

                // 5. Tab inside the child: the "tabOut" protocol. view_embed.rs's own two-control demo
                // ring (save_btn, menu_btn - see that file's module doc) calls
                // kubuno_desktop_controls::host::notify_tab_out once Tab runs past its LAST control;
                // RustDesignSurfaceHost's WndProc turns that into MoveFocus, moving the keyboard focus
                // out of the child and into whatever WPF element is next. Up to 6 Tabs for margin (the
                // ring wraps within 3 from an unfocused state); the check is simply that focus is no
                // longer in the child by the end. Checked and stopped as soon as focus leaves (rather
                // than always firing exactly 6): the WPF-level tab cycle here is only 3 stops (before,
                // after, host), so blindly continuing to press Tab after it already left can cycle
                // straight back into the host via TabIntoCore and produce a false FAIL.
                for (var i = 0; i < 6; i++)
                {
                    Press(0x09);
                    await WaitAsync(150);
                    if (Native.ThreadInfo(child).hwndFocus != child)
                    {
                        break;
                    }
                }
                ti = Native.ThreadInfo(child);
                Check("Tab exits the surface (tabOut protocol)", ti.hwndFocus != child, $"focus={Native.Hex(ti.hwndFocus)} wpf={Keyboard.FocusedElement}");

                // 6. Popup (menu) overflowing the child, owned top-level.
                var scale = Native.GetDpiForWindow(child) / 96.0;
                Native.GetWindowRect(child, out cr);
                async Task OpenMenuAsync() { await ClickAsync(cr.Right - (int)(48 * scale), cr.Top + (int)(14 * scale)); await WaitAsync(600); }
                await OpenMenuAsync();
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
                await WaitAsync(500);
                Check("click on the overflowing part of the popup reaches the child's page", RustSaid("popup item 2 clicked"));
                Log($"INFO focus after popup click still in child={Native.ThreadInfo(child).hwndFocus == child}");
                // Reopen, then click on a WPF element: focus leaves the child -> blur -> menu closes.
                await OpenMenuAsync();
                Log($"INFO reopened overlays={Overlays().Count}");
                var p = _after.PointToScreen(new Point(10, 5));
                await ClickAsync((int)p.X, (int)p.Y);
                await WaitAsync(600);
                Check("popup dismissed when focus leaves the child", Overlays().Count == 0, $"visible overlays={Overlays().Count}");

                // 7. Mouse capture while dragging out of the child.
                Native.GetWindowRect(child, out cr);
                Native.SetCursorPos((cr.Left + cr.Right) / 2, (cr.Top + cr.Bottom) / 2);
                Native.mouse_event(Native.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                await WaitAsync(150);
                Native.SetCursorPos(cr.Right + 200, cr.Bottom + 100);
                await WaitAsync(200);
                ti = Native.ThreadInfo(child);
                Check("drag keeps capture in child outside its rect", ti.hwndCapture == child, $"capture={Native.Hex(ti.hwndCapture)}");
                Native.mouse_event(Native.MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                await WaitAsync(150);

                // 8. Resize follows.
                foreach (var (w, h) in new[] { (700.0, 500.0), (1100.0, 800.0), (900.0, 640.0) })
                {
                    Width = w; Height = h;
                    await WaitAsync(400);
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
                    await WaitAsync(600);
                    Native.GetClientRect(_host.Container, out var c1);
                    Native.GetWindowRect(_host.Child, out var c2);
                    Log($"INFO on {s.DeviceName}: dpi main {Native.GetDpiForWindow(Main)} child {dpiBefore}->{Native.GetDpiForWindow(_host.Child)}; sizes match {c1.Right == c2.Right - c2.Left && c1.Bottom == c2.Bottom - c2.Top}");
                }
                Log($"INFO WM_DPICHANGED_AFTERPARENT seen by child: {RustSaid("WM_DPICHANGED_AFTERPARENT")}");
                Left = origin.Left; Top = origin.Top;

                // 10. Child crash: host survives, surface restarts AUTOMATICALLY (production's own
                // restart-with-backoff, no manual StartSurface call anymore - see
                // RustDesignSurfaceHost.OnSurfaceExited/ScheduleRetry). Subscribe to ChildReady
                // BEFORE killing: the automatic backoff (250 ms initial + launch, well under 1 s) is
                // fast enough to complete DURING a fixed post-kill wait, so subscribing only after
                // that wait (as a manual-restart flow could afford to) races the very event it is
                // waiting for and times out having missed it - this bit the first version of this
                // check.
                var oldPid = _host.Surface.Id;
                var restarted = new TaskCompletionSource<bool>();
                void OnReady() { _host.ChildReady -= OnReady; restarted.TrySetResult(true); }
                _host.ChildReady += OnReady;
                _host.Surface.Kill();
                await WaitAsync(300);
                Check("host survives child crash", Native.IsWindow(_host.Container) && IsLoaded, $"container alive={Native.IsWindow(_host.Container)} child gone={_host.Child == IntPtr.Zero}");
                var done = await Task.WhenAny(restarted.Task, Task.Delay(15000));
                Check("surface restarts into the same container (automatic backoff)", done == restarted.Task && _host.Surface.Id != oldPid);

                Topmost = false;
                Log("SELFTEST DONE");
                if (close)
                {
                    await WaitAsync(500);
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

        /// <summary>
        /// DSG-9 visual check driver (`Program`'s own `--simulate-toolbox &lt;Component&gt;` doc): plays
        /// out a toolbox drag entirely through <see cref="RustDesignSurfaceHost"/>'s own DSG-9 API, since
        /// there is no live VS Toolbox here to actually drag from. A few `dragOver` steps (not one) so the
        /// insertion marker/ghost has time to visibly move between them in a screenshot/recording, ending
        /// well inside the child's own client area (a fixed, small offset - this spike does not know the
        /// loaded view's own layout, so it cannot aim at a specific container; the log's own
        /// `DropTargetChanged`/`DragDropEditRequested` lines say exactly where it actually landed).
        /// </summary>
        private async Task SimulateToolboxDropAsync(string component)
        {
            Log($"--simulate-toolbox: dragEnter {component}");
            _host.NotifyDragEnter(component);
            await WaitAsync(400);

            double x = 40.0;
            double y = 40.0;
            for (var i = 0; i < 4; i++)
            {
                Log($"--simulate-toolbox: dragOver ({x:F0}, {y:F0})");
                _host.NotifyDragOver(x, y);
                await WaitAsync(350);
                x += 15.0;
                y += 20.0;
            }

            Log($"--simulate-toolbox: drop ({x:F0}, {y:F0})");
            _host.NotifyDrop(x, y);
            await WaitAsync(400);
            _host.NotifyDragLeave();
            Log("--simulate-toolbox: done");
        }

        /// <summary>One line per op, for <see cref="RustDesignSurfaceHost.EditRequestsReceived"/>'s own log line above - no LINQ, matching this file's existing style.</summary>
        private static string FormatOps(System.Collections.Generic.IReadOnlyList<DesignSurfaceEditOp> ops)
        {
            var parts = new System.Collections.Generic.List<string>(ops.Count);
            foreach (var op in ops)
            {
                parts.Add($"{op.Kind}:{op.ElementId}.{op.Name}={op.Value}");
            }

            return string.Join(";", parts);
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
