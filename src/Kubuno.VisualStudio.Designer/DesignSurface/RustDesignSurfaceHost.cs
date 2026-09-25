using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Kubuno.VisualStudio.Views.Logging;

namespace Kubuno.VisualStudio.Designer.DesignSurface
{
    /// <summary>
    /// The production <see cref="IDesignSurfaceHost"/> (DSG-7): embeds the out-of-process design
    /// surface exe as a cross-process <c>WS_CHILD</c> window, exactly as
    /// <c>vskubuno/docs/DESIGNER.md</c> §7's spike (<c>vskubuno/spikes/HwndHostSpike/DesignSurfaceHost.cs</c>)
    /// proved out - this class is that spike turned into production code: a persistent Job Object,
    /// crash restart with backoff, <c>SetErrorMode</c>, a pre-launch runtime check, and the keyboard
    /// protocol (<c>unhandledKey</c>/<c>tabOut</c>) the spike identified as the two real gaps.
    ///
    /// <para><b>Container window.</b> Like the spike, <see cref="BuildWindowCore"/> creates a plain
    /// Win32 "Static" child of WPF's own hosting HWND (not the surface itself) and launches the surface
    /// with <c>--parent &lt;container&gt;</c>; the surface creates its OWN child of that container
    /// (<c>kubuno_controls::host::HostOptions::parent</c>). Sizing follows the container's own
    /// <c>WM_SIZE</c> (forwarded to the grandchild with <c>MoveWindow</c>) - DPI needs no extra code
    /// here: the surface reacts to <c>WM_DPICHANGED_AFTERPARENT</c> on its own, and WPF already sizes
    /// the container in DPI-correct device pixels per <c>HwndHost</c>'s normal layout, so the physical
    /// pixels in <c>WM_SIZE</c>'s <c>lParam</c> are already right for the display the pane is on.</para>
    ///
    /// <para><b>Document text.</b> The real protocol (<c>vskubuno/docs/DESIGNER.md</c> §3's
    /// <c>kubuno/setBuffer</c>) is stdio JSON-RPC into a <c>kubuno-views-designer</c> process that does
    /// not exist yet (DSG-6 is still examples-only, promoting <c>view_preview.rs</c>/<c>view_embed.rs</c>
    /// into a real crate is that package's own scope). Until then, <see cref="SetDocumentText"/> bridges
    /// DSG-3's "buffer is truth" rule onto what the CURRENT exe (<c>view_embed.exe</c>) actually
    /// supports - a file on disk polled by its own <c>FileWatcher</c> - by writing to a private temp
    /// file passed as that exe's <c>&lt;file.kbview&gt;</c> argument. Swap this for real
    /// <c>kubuno/setBuffer</c> IPC the moment DSG-6 ships a process that speaks it; nothing else in this
    /// class (the embedding, the keyboard protocol, the lifecycle) needs to change to do that.</para>
    ///
    /// <para><b>Keyboard protocol</b> (the two gaps the spike named, see <c>DESIGNER.md</c> §7 "Recommended
    /// approach"):</para>
    /// <list type="bullet">
    /// <item><b>unhandledKey</b>: the surface re-posts to the container the SAME
    /// <c>WM_KEYDOWN</c>/<c>WM_SYSKEYDOWN</c> a real keystroke would have produced, for whatever it did
    /// not consume this frame (<c>kubuno_controls::host::forward_unhandled_keys</c>, additive in
    /// <c>kubuno-controls/src/host/mod.rs</c>). Since the container never itself has the keyboard focus
    /// (the grandchild does), EVERY <c>WM_KEYDOWN</c>/<c>WM_SYSKEYDOWN</c> this window's own
    /// <see cref="WndProc"/> sees is, by construction, one of these forwarded messages - handled by
    /// <see cref="VsFilterKeys"/> if the VSIX wired it (real <c>IVsFilterKeys2.TranslateAcceleratorEx</c>,
    /// left as an integration-time extension point here - see that property's own doc for why), else by
    /// <see cref="ComponentDispatcher.RaiseThreadMessage"/>. An interactive `--selftest` run confirmed a
    /// purely PASSIVE approach (relying only on WPF's own Dispatcher pump raising
    /// `ComponentDispatcher` ambiently for every message it pumps, without an explicit call here) does
    /// NOT reach a `KeyBinding` - the explicit call is required. An EARLIER interactive run had
    /// suspected this same explicit call of stealing native focus back from the surface, but that run
    /// predated this class having a <see cref="TabIntoCore"/> override (see its own doc - a real,
    /// independently confirmed bug); with `TabIntoCore` giving WPF a properly established focus scope
    /// for this sink, the explicit call is the correct, restored design.</item>
    /// <item><b>tabOut</b>: a custom <c>WM_APP</c>-based message (<c>kubuno_controls::host::WM_KUBUNO_TAB_OUT</c>,
    /// posted by <c>kubuno_controls::host::notify_tab_out</c>) - <see cref="WndProc"/> calls
    /// <see cref="UIElement.MoveFocus"/> with a <see cref="TraversalRequest"/>, moving the WPF focus out
    /// of this element exactly as the spike's own <c>TabIntoCore</c> already does for the OPPOSITE
    /// direction (WPF into the surface).</item>
    /// </list>
    /// </summary>
    public sealed class RustDesignSurfaceHost : HwndHost, IDesignSurfaceHost
    {
        // Backoff for both "the surface just crashed" and "the runtime DLLs are still missing" -
        // unified into one retry loop (see ScheduleRetry): a user who runs tools/stage-runtime.ps1
        // while the pane is open should not have to close and reopen it.
        private const int BackoffInitialMs = 250;
        private const int BackoffMaxMs = 30_000;
        private const int StableAfterMs = 10_000;
        /// <summary>How long <see cref="DestroyWindowCore"/> waits for a graceful exit before killing.</summary>
        private const int ShutdownGraceMs = 2_000;

        private readonly string _exePath;
        private readonly string _extraArgs;
        private readonly string _tempViewFile;
        private readonly DispatcherTimer _childPoll = new() { Interval = TimeSpan.FromMilliseconds(20) };
        private readonly DispatcherTimer _retryTimer = new();

        private IntPtr _container;
        private IntPtr _job;
        private Process? _surface;
        private DateTime _lastStart;
        private int _backoffMs = BackoffInitialMs;
        private bool _disposed;
        private static bool s_errorModeSet;

        /// <param name="exePath">Full path to the design surface exe (today, <c>view_embed.exe</c> - see the class doc).</param>
        /// <param name="extraArgs">Extra command-line arguments appended after the temp view file, if any.</param>
        public RustDesignSurfaceHost(string exePath, string extraArgs = "")
        {
            _exePath = exePath ?? throw new ArgumentNullException(nameof(exePath));
            _extraArgs = extraArgs ?? string.Empty;
            _tempViewFile = Path.Combine(Path.GetTempPath(), "kubuno-designer-" + Guid.NewGuid().ToString("N") + ".kbview");
            Focusable = true;
            EnsureErrorModeSet();
            _retryTimer.Tick += (_, _) => { _retryTimer.Stop(); StartOrShowProblem(); };
        }

        /// <summary>
        /// Optional hook into the real VS <c>IVsFilterKeys2.TranslateAcceleratorEx</c> path (see the
        /// class doc's "unhandledKey" remarks). Takes the raw <c>(message, wParam, lParam)</c> of a
        /// forwarded key and returns whether it was translated/executed as a VS command; returning
        /// <see langword="true"/> skips the <see cref="ComponentDispatcher.RaiseThreadMessage"/> fallback
        /// for that key. Left <see langword="null"/> (its default - always the case in this library's own
        /// tests and the updated spike, which have no live VS to call into) means every forwarded key
        /// goes straight to that fallback.
        ///
        /// Left as a settable property rather than wired directly to the real COM interface here: this
        /// task could not exercise <c>IVsFilterKeys2</c> against a live <c>devenv.exe</c> (see
        /// <c>docs/DESIGNER.md</c> §7's own "still to verify" list), and guessing its call shape without
        /// that verification risks a silently wrong integration. The VSIX's own integration step (see
        /// <c>INTEGRATION.md</c>) should set this from a working reference against the real SDK once it
        /// can be checked live.
        /// </summary>
        public Func<int, IntPtr, IntPtr, bool>? VsFilterKeys { get; set; }

        /// <summary>The design surface's own process, once started.</summary>
        public Process? Surface => _surface;

        /// <summary>The container this host created (a plain Win32 "Static" window).</summary>
        public IntPtr Container => _container;

        /// <summary>The design surface's own child window inside <see cref="Container"/>, once it exists.</summary>
        public IntPtr Child => _container == IntPtr.Zero ? IntPtr.Zero : NativeMethods.GetWindow(_container, NativeMethods.GW_CHILD);

        /// <summary>Raised once <see cref="Child"/> exists after a (re)start - what a caller (this library's tests, the updated spike) awaits instead of polling.</summary>
        public event Action? ChildReady;

        /// <summary>
        /// Raised for every line the design surface process writes to its own stderr (diagnostics -
        /// docs/DESIGNER.md's <c>[embed]</c> trace lines, panics), alongside this class's own logging of
        /// it through <see cref="KubunoViewsLogHost"/>. A caller that needs to assert on what the
        /// SURFACE itself observed (a test harness grepping for a specific trace line, not just what
        /// ended up in the shared log file) should subscribe to this rather than parse the log.
        /// </summary>
        public event Action<string>? SurfaceOutputLine;

        FrameworkElement IDesignSurfaceHost.Content => this;

        public void SetDocumentText(string xmlText)
        {
            try
            {
                File.WriteAllText(_tempViewFile, xmlText ?? string.Empty);
            }
            catch (IOException)
            {
                // Best-effort (see the class doc's "Document text" remarks): a transient share
                // violation must not prevent the pane from opening or take down the editor.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

#pragma warning disable CS0067 // DSG-8 (selection sync) raises this once the surface reports hit-tests; not yet wired (see IDesignSurfaceHost's own remarks).
        public event EventHandler<DesignSurfaceSelectionChangedEventArgs>? SelectionChanged;
#pragma warning restore CS0067

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            _container = NativeMethods.CreateWindowEx(
                0, "Static", string.Empty,
                NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE | NativeMethods.WS_CLIPCHILDREN | NativeMethods.WS_CLIPSIBLINGS,
                0, 0, 1, 1, hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (_container == IntPtr.Zero)
            {
                throw new InvalidOperationException("CreateWindowEx(container) failed: " + Marshal.GetLastWin32Error());
            }

            StartOrShowProblem();
            return new HandleRef(this, _container);
        }

        /// <summary>
        /// Checks the runtime is staged, then either launches the surface or shows a clear message in
        /// the container itself (the placeholder panel item 1's pre-launch check asks for) and arms a
        /// retry - see the class doc's "unified backoff" remark.
        /// </summary>
        private void StartOrShowProblem()
        {
            if (_disposed)
            {
                return;
            }

            var problem = FindRuntimeProblem();
            if (problem != null)
            {
                KubunoViewsLogHost.Current.WriteLine($"[designer] surface not started: {problem}");
                NativeMethods.SetWindowText(_container, problem + "\r\n\r\nRun tools/stage-runtime.ps1, then reopen this file.");
                ScheduleRetry();
                return;
            }

            LaunchSurface();
        }

        /// <summary>
        /// <see langword="null"/> when the exe and its runtime DLLs (<c>kubuno_ui.dll</c>, Rust's own
        /// <c>std-*.dll</c> - the workspace links <c>-C prefer-dynamic</c>) are all present next to it;
        /// otherwise a one-line, user-facing description of what is missing.
        /// </summary>
        private string? FindRuntimeProblem()
        {
            if (!File.Exists(_exePath))
            {
                return $"Design surface exe not found: {_exePath}";
            }

            var dir = Path.GetDirectoryName(_exePath);
            if (string.IsNullOrEmpty(dir))
            {
                return $"Design surface exe has no directory: {_exePath}";
            }

            if (!File.Exists(Path.Combine(dir, "kubuno_ui.dll")))
            {
                return $"kubuno_ui.dll missing next to {_exePath}";
            }

            if (Directory.GetFiles(dir, "std-*.dll").Length == 0)
            {
                return $"Rust runtime (std-*.dll) missing next to {_exePath}";
            }

            return null;
        }

        private void LaunchSurface()
        {
            NativeMethods.SetWindowText(_container, string.Empty);
            var dir = Path.GetDirectoryName(_exePath)!;
            var args = $"--parent {_container.ToInt64()} \"{_tempViewFile}\"" + (_extraArgs.Length > 0 ? " " + _extraArgs : string.Empty);
            var psi = new ProcessStartInfo(_exePath, args)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = dir,
            };
            // kubuno_ui is a Rust dylib one folder up in a dev build; make it resolvable without
            // relying on the caller's own PATH.
            var parentDir = Path.GetDirectoryName(dir);
            if (!string.IsNullOrEmpty(parentDir))
            {
                psi.EnvironmentVariables["PATH"] = parentDir + ";" + psi.EnvironmentVariables["PATH"];
            }

            Process proc;
            try
            {
                proc = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null.");
            }
            catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
            {
                KubunoViewsLogHost.Current.WriteException("Failed to start the design surface", ex);
                NativeMethods.SetWindowText(_container, "Design surface failed to start: " + ex.Message);
                ScheduleRetry();
                return;
            }

            _surface = proc;
            _surface.EnableRaisingEvents = true;
            _surface.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    KubunoViewsLogHost.Current.WriteLine("[design surface] " + e.Data);
                    SurfaceOutputLine?.Invoke(e.Data);
                }
            };
            _surface.BeginErrorReadLine();
            _surface.Exited += OnSurfaceExited;
            AssignToJobObject(proc);
            _lastStart = DateTime.UtcNow;
            KubunoViewsLogHost.Current.WriteLine($"[designer] design surface started (PID {proc.Id}).");
            WaitForChild();
        }

        private void WaitForChild()
        {
            _childPoll.Stop();
            var started = DateTime.UtcNow;
            _childPoll.Tick += Tick;
            _childPoll.Start();

            void Tick(object? sender, EventArgs e)
            {
                if (Child != IntPtr.Zero)
                {
                    _childPoll.Stop();
                    _childPoll.Tick -= Tick;
                    SyncChildSize();
                    ChildReady?.Invoke();
                }
                else if ((DateTime.UtcNow - started).TotalSeconds > 15)
                {
                    _childPoll.Stop();
                    _childPoll.Tick -= Tick;
                    KubunoViewsLogHost.Current.WriteLine("[designer] design surface's child window never appeared (15 s).");
                }
            }
        }

        private void SyncChildSize()
        {
            var child = Child;
            if (child == IntPtr.Zero || !NativeMethods.GetClientRect(_container, out var rc))
            {
                return;
            }

            NativeMethods.MoveWindow(child, 0, 0, rc.Right - rc.Left, rc.Bottom - rc.Top, true);
        }

        /// <summary>
        /// Restart-with-backoff: an unexpected exit doubles the delay (capped at
        /// <see cref="BackoffMaxMs"/>); a surface that stayed up at least <see cref="StableAfterMs"/>
        /// resets it first - a surface that keeps crashing immediately backs off, one that crashed once
        /// after running fine does not inherit a long wait from an earlier bad patch.
        /// </summary>
        private void OnSurfaceExited(object? sender, EventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            // Process.Exited fires on a thread-pool thread; every field/Win32 call here belongs to the UI
            // thread. Plain Dispatcher.BeginInvoke, not JoinableTaskFactory.SwitchToMainThreadAsync: this
            // library also runs standalone (its own tests, the spike) where VS's JoinableTaskContext is
            // never initialized, so depending on ThreadHelper.JoinableTaskFactory here would break outside
            // a live devenv.exe.
#pragma warning disable VSTHRD001, VSTHRD110
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_disposed)
                {
                    return;
                }

                var exitCode = SafeExitCode();
                KubunoViewsLogHost.Current.WriteLine($"[designer] design surface exited unexpectedly (code {exitCode}); restarting.");
                if ((DateTime.UtcNow - _lastStart).TotalMilliseconds >= StableAfterMs)
                {
                    _backoffMs = BackoffInitialMs;
                }
                else
                {
                    _backoffMs = Math.Min(_backoffMs * 2, BackoffMaxMs);
                }

                ScheduleRetry();
            }));
#pragma warning restore VSTHRD001, VSTHRD110
        }

        private string SafeExitCode()
        {
            try
            {
                return _surface?.ExitCode.ToString() ?? "?";
            }
            catch (InvalidOperationException)
            {
                return "?";
            }
        }

        private void ScheduleRetry()
        {
            if (_disposed)
            {
                return;
            }

            _retryTimer.Stop();
            _retryTimer.Interval = TimeSpan.FromMilliseconds(_backoffMs);
            _retryTimer.Start();
        }

        private void AssignToJobObject(Process process)
        {
            if (_job == IntPtr.Zero)
            {
                _job = NativeMethods.CreateJobObject(IntPtr.Zero, null);
                if (_job == IntPtr.Zero)
                {
                    KubunoViewsLogHost.Current.WriteLine("[designer] CreateJobObject failed (Win32 " + Marshal.GetLastWin32Error() + "); the design surface will not be killed automatically if VS is force-terminated.");
                    return;
                }

                var info = default(NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION);
                info.BasicLimitInformation.LimitFlags = NativeMethods.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                var length = Marshal.SizeOf<NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
                var ptr = Marshal.AllocHGlobal(length);
                try
                {
                    Marshal.StructureToPtr(info, ptr, false);
                    if (!NativeMethods.SetInformationJobObject(_job, NativeMethods.JobObjectExtendedLimitInformation, ptr, (uint)length))
                    {
                        KubunoViewsLogHost.Current.WriteLine("[designer] SetInformationJobObject failed (Win32 " + Marshal.GetLastWin32Error() + ").");
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }

            try
            {
                if (!NativeMethods.AssignProcessToJobObject(_job, process.Handle))
                {
                    KubunoViewsLogHost.Current.WriteLine("[designer] AssignProcessToJobObject failed (Win32 " + Marshal.GetLastWin32Error() + ").");
                }
            }
            catch (InvalidOperationException)
            {
                // The process already exited between Process.Start and here; OnSurfaceExited handles the restart.
            }
        }

        private static void EnsureErrorModeSet()
        {
            if (s_errorModeSet)
            {
                return;
            }

            // Inherited by every child process started after this point (Process.Start ->
            // CreateProcess): a missing DLL fails the launch with an exit code instead of popping the
            // loader's modal dialog on the user's desktop (docs/DESIGNER.md §7's "Runtime DLLs" finding).
            NativeMethods.SetErrorMode(NativeMethods.SEM_FAILCRITICALERRORS | NativeMethods.SEM_NOOPENFILEERRORBOX);
            s_errorModeSet = true;
        }

        protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmSize)
            {
                var child = Child;
                if (child != IntPtr.Zero)
                {
                    var w = (int)(lParam.ToInt64() & 0xFFFF);
                    var h = (int)((lParam.ToInt64() >> 16) & 0xFFFF);
                    NativeMethods.MoveWindow(child, 0, 0, w, h, true);
                }
            }
            else if (msg == WmSetFocus)
            {
                // The container never itself wants the keyboard: pass it on to the surface's own
                // window, exactly as the spike does.
                var child = Child;
                if (child != IntPtr.Zero)
                {
                    NativeMethods.SetFocus(child);
                }
            }
            else if (msg == WmKeyDown || msg == WmSysKeyDown)
            {
                // The container is never itself focused (the surface's grandchild window is), so every
                // WM_KEYDOWN/WM_SYSKEYDOWN reaching this WndProc is one `forward_unhandled_keys` posted
                // (see the class doc's "unhandledKey" remarks) - never a real keystroke on this window.
                //
                // History: an earlier version of this class did NOT call
                // ComponentDispatcher.RaiseThreadMessage explicitly here, betting that WPF's own
                // Dispatcher pump already raises it ambiently for every message on the thread (the
                // "cheap variant" docs/DESIGNER.md §7 describes). An interactive run showed a genuine
                // Ctrl+S never reaching the WPF KeyBinding under that passive-only approach - the
                // ambient pass alone is not enough. The explicit call below WAS also, separately,
                // suspected (in an EARLIER interactive run) of stealing native keyboard focus back from
                // the surface; that run predated this class having a `TabIntoCore` override (see that
                // method's own doc - its absence was a real, independently confirmed bug), so the
                // explicit call is restored here as the more likely correct fix once WPF has a properly
                // established focus scope for this sink to hand control back to.
                HandleUnhandledKey(hwnd, msg, wParam, lParam);
            }
            else if (msg == WmKubunoTabOut)
            {
                var backward = wParam.ToInt64() != 0;
                KubunoViewsLogHost.Current.WriteLine($"[designer] tabOut backward={backward}: MoveFocus({(backward ? "Previous" : "Next")}) requested.");
                // Deferred rather than called inline: MoveFocus re-enters WPF's own focus machinery,
                // which should not run re-entrantly from inside a native WndProc callback. Plain
                // Dispatcher.BeginInvoke for the same reason as OnSurfaceExited's own (see its comment).
#pragma warning disable VSTHRD001, VSTHRD110
                Dispatcher.BeginInvoke(new Action(() =>
                    MoveFocus(new TraversalRequest(backward ? FocusNavigationDirection.Previous : FocusNavigationDirection.Next))));
#pragma warning restore VSTHRD001, VSTHRD110
            }

            return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
        }

        private void HandleUnhandledKey(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (VsFilterKeys?.Invoke(msg, wParam, lParam) == true)
                {
                    return;
                }

                var nativeMsg = new MSG
                {
                    hwnd = hwnd,
                    message = msg,
                    wParam = wParam,
                    lParam = lParam,
                };
                ComponentDispatcher.RaiseThreadMessage(ref nativeMsg);
            }
            catch (Exception ex)
            {
                // A keyboard shim must never crash the designer pane over a single forwarded key.
                KubunoViewsLogHost.Current.WriteException("Forwarding an unhandled design-surface key failed", ex);
            }
        }

        /// <summary>
        /// Tab from WPF INTO the surface: WPF asks the sink to take the focus - exactly the spike's own
        /// override (<c>git show 614594d:spikes/HwndHostSpike/DesignSurfaceHost.cs</c>), missing from an
        /// earlier version of this class (a real regression: without it, WPF's Tab navigation has no way
        /// to know this sink can take the focus and simply skips over it, per docs/DESIGNER.md §7's own
        /// "Tab from WPF into the surface" row - restored here as the fix, found via an interactive
        /// <c>--selftest</c> run whose "Tab from WPF enters the child (TabIntoCore)" check failed).
        /// </summary>
        protected override bool TabIntoCore(TraversalRequest request)
        {
            var child = Child;
            if (child == IntPtr.Zero)
            {
                return false;
            }

            NativeMethods.SetFocus(child);
            return true;
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            _disposed = true;
            _childPoll.Stop();
            _retryTimer.Stop();
            NativeMethods.DestroyWindow(hwnd.Handle);

            if (_surface != null && !SafeHasExited() && !_surface.WaitForExit(ShutdownGraceMs))
            {
                KubunoViewsLogHost.Current.WriteLine("[designer] design surface did not exit within " + ShutdownGraceMs + " ms of DestroyWindow; killing.");
                try
                {
                    _surface.Kill();
                }
                catch (InvalidOperationException)
                {
                }
                catch (Win32Exception)
                {
                }
            }

            // Closing the job's last handle kills anything still in it (JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE)
            // - belt and braces for a hard VS kill that skips this method entirely (docs/DESIGNER.md §7).
            if (_job != IntPtr.Zero)
            {
                NativeMethods.CloseHandle(_job);
                _job = IntPtr.Zero;
            }

            try
            {
                if (File.Exists(_tempViewFile))
                {
                    File.Delete(_tempViewFile);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private bool SafeHasExited()
        {
            try
            {
                return _surface!.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }

        // Raw Win32 message ids (this project does not reference the `windows` crate's C# equivalent,
        // matching the spike's own Native.cs - see that file's remarks).
        private const int WmSize = 0x0005;
        private const int WmSetFocus = 0x0007;
        private const int WmKeyDown = 0x0100;
        private const int WmSysKeyDown = 0x0104;
        /// <summary>Must match Rust's <c>kubuno_controls::host::WM_KUBUNO_TAB_OUT</c> exactly (<c>WM_APP + 0x4B4F</c>).</summary>
        private const int WmKubunoTabOut = 0x8000 + 0x4B4F;
    }
}
