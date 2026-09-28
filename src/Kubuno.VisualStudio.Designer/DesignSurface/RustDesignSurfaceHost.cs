using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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
    /// <c>kubuno-controls/src/host/mod.rs</c>), preceded by a <c>kubuno_controls::host::WM_KUBUNO_KEY_MODS</c>
    /// message carrying the modifiers held at that ORIGINAL, physical moment. Since the container never
    /// itself has the keyboard focus (the grandchild does), EVERY <c>WM_KEYDOWN</c>/<c>WM_SYSKEYDOWN</c>
    /// this window's own <see cref="WndProc"/> sees is, by construction, one of these forwarded messages
    /// - routed by <see cref="HandleUnhandledKey"/> (via <see cref="VsFilterKeysBridge"/>) to the real VS
    /// accelerator path (`IVsFilterKeys2.TranslateAcceleratorEx`) when running inside VS, else to a WPF
    /// fallback (<see cref="RaiseWpfKeyEvent"/>, which walks up from this element and directly executes
    /// the matching `InputBinding`'s command - see that method's own doc for the full story of what did
    /// NOT work first: a purely passive approach; `ComponentDispatcher.RaiseThreadMessage`, which cannot
    /// reach WPF's routed-event pipeline for a plain child `HWND` at all; `AttachThreadInput` alone,
    /// without the captured-modifiers message, which reads the wrong, already-stale key state for a fast
    /// chord; and finally synthesizing routed `PreviewKeyDown`/`KeyDown` events through
    /// `InputManager.ProcessInput`, which needed an explicit `Keyboard.Focus(this)` to route at all and
    /// that, confirmed live, moved native Win32 focus away from the surface with no reliable way to give
    /// it back without also stopping the very `KeyBinding` it was trying to reach).</item>
    /// <item><b>tabOut</b>: a custom <c>WM_APP</c>-based message (<c>kubuno_controls::host::WM_KUBUNO_TAB_OUT</c>,
    /// posted by <c>kubuno_controls::host::notify_tab_out</c>) - <see cref="WndProc"/> calls
    /// <see cref="UIElement.MoveFocus"/> with a <see cref="TraversalRequest"/>, moving the WPF focus out
    /// of this element exactly as the spike's own <c>TabIntoCore</c> already does for the OPPOSITE
    /// direction (WPF into the surface).</item>
    /// </list>
    /// </summary>
    // `partial`: the DSG-6 IPC protocol (`kubuno/setText`/`setDesignMode`/`select` on stdin,
    // `selectionChanged`/`editRequest` on stdout - see `vskubuno/docs/DESIGNER.md`'s "DSG-6 protocol"
    // section) is implemented in the sibling file `RustDesignSurfaceHost.Protocol.cs`, kept separate so
    // it does not collide with concurrent work on this file's own keyboard-forwarding code.
    public sealed partial class RustDesignSurfaceHost : HwndHost, IDesignSurfaceHost
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
        private readonly DispatcherTimer _childPoll = new() { Interval = TimeSpan.FromMilliseconds(20) };
        private readonly DispatcherTimer _retryTimer = new();

        private IntPtr _container;
        private IntPtr _job;
        private Process? _surface;
        private DateTime _lastStart;
        private int _backoffMs = BackoffInitialMs;
        private bool _disposed;
        private static bool s_errorModeSet;
        /// <summary>The modifier bits carried by the last <see cref="WmKubunoKeyMods"/> message, consumed by the very next forwarded key (see that message constant's own doc).</summary>
        private int _pendingKeyMods;

        /// <summary>
        /// An <c>IVsFilterKeys2</c>, boxed as <see cref="object"/> - see
        /// <see cref="VsFilterKeysBridge"/>'s own doc for why this field's DECLARED type must not be the
        /// real VS interop type (it would force <c>Microsoft.VisualStudio.Interop.dll</c> to be loaded
        /// the moment this class is, in every caller - this library's own tests and the updated spike
        /// included - confirmed live as an actual startup crash before this fix:
        /// <c>FileNotFoundException</c> for that assembly, thrown from this constructor).
        /// </summary>
        private readonly object? _vsFilterKeys2;

        /// <param name="exePath">Full path to the design surface exe (today, <c>view_embed.exe</c> - see the class doc).</param>
        /// <param name="extraArgs">Extra command-line arguments appended after the temp view file, if any.</param>
        /// <param name="oleServiceProvider">
        /// The VS package/site's own <c>IOleServiceProvider</c> (the same object
        /// <c>UI\DesignerSplitView.cs</c> already threads through to <c>UI\CodeWindowHost.cs</c>) - an
        /// <c>Microsoft.VisualStudio.OLE.Interop.IServiceProvider</c> instance, typed as
        /// <see cref="object"/> here on purpose (see <see cref="VsFilterKeysBridge"/>'s own doc), used to
        /// query <c>SVsFilterKeys</c>/<c>IVsFilterKeys2</c> for the real VS accelerator path (see
        /// <see cref="HandleUnhandledKey"/>'s own doc). <see langword="null"/> (always the case in this
        /// library's own tests and the updated spike, which have no live VS to query) means every
        /// forwarded key goes straight to the WPF fallback.
        /// </param>
        public RustDesignSurfaceHost(string exePath, string extraArgs = "", object? oleServiceProvider = null)
        {
            _exePath = exePath ?? throw new ArgumentNullException(nameof(exePath));
            _extraArgs = extraArgs ?? string.Empty;
            // The null-check itself needs no VS type, so it is safe to sit directly in this always-run
            // constructor; VsFilterKeysBridge.TryQuery is only ever CALLED (hence only ever JIT'd, hence
            // only ever needs Microsoft.VisualStudio.Interop.dll loaded) when there is something to query.
            // VSTHRD010 flags this call site because TryQuery asserts the UI thread internally - correct,
            // but adding ANOTHER ThreadHelper call directly in THIS method (to satisfy the analyzer
            // itself) would reintroduce a hard runtime dependency on Microsoft.VisualStudio.Shell.15.0.dll
            // in an always-executed constructor, exactly the bug VsFilterKeysBridge's own doc describes -
            // suppressed here, not fixed by adding the call back.
#pragma warning disable VSTHRD010
            _vsFilterKeys2 = oleServiceProvider != null ? VsFilterKeysBridge.TryQuery(oleServiceProvider) : null;
#pragma warning restore VSTHRD010
            Focusable = true;
            EnsureErrorModeSet();
            _retryTimer.Tick += (_, _) => { _retryTimer.Stop(); StartOrShowProblem(); };
        }

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

        /// <summary>
        /// Pushes the buffer's current text as a `kubuno/setText` DSG-6 protocol message
        /// (<c>RustDesignSurfaceHost.Protocol.cs</c>) - see that file's own doc for the wire shape and
        /// why this replaced the earlier temp-file bridge.
        /// </summary>
        public void SetDocumentText(string xmlText) => SendSetText(xmlText ?? string.Empty);

        /// <summary>
        /// Raised when the design surface reports a new selection (a click, or Esc-to-parent) - the
        /// DSG-6 `selectionChanged` protocol message, handled in
        /// <c>RustDesignSurfaceHost.Protocol.cs</c>. Consumed by DSG-8's bidirectional selection sync to
        /// move the XML pane's caret.
        /// </summary>
        public event EventHandler<DesignSurfaceSelectionChangedEventArgs>? SelectionChanged;

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
            // No `<file.kbview>` positional argument any more (DSG-6: `view_embed`'s file argument is now
            // OPTIONAL, and the document text arrives over stdin instead - see `SendSetText`).
            var args = $"--parent {_container.ToInt64()}" + (_extraArgs.Length > 0 ? " " + _extraArgs : string.Empty);
            var psi = new ProcessStartInfo(_exePath, args)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                // DSG-6 (`RustDesignSurfaceHost.Protocol.cs`): the surface's own stdin/stdout now carry
                // the line-delimited JSON protocol (`setText`/`setDesignMode`/`select` out,
                // `selectionChanged`/`editRequest` in) - stderr stays the plain trace channel above.
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                // Explicit UTF-8 WITHOUT a BOM preamble - left unset, .NET's default `StreamWriter`/
                // `StreamReader` preamble handling silently corrupted the FIRST line ever written to a
                // freshly-launched surface's stdin (confirmed live, DSG-9's own visual check: the Rust
                // side's `parse_host_message` correctly, silently dropped a line arriving with a
                // leading U+FEFF byte-order mark, so the surface never even loaded a document). Also
                // set for stdout for the same reason, symmetrically, even though no BOM was observed there.
                StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
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
                if (e.Data == null)
                {
                    return;
                }

                // This runs on a .NET thread-pool thread (async pipe read), not the UI thread. A
                // misbehaving IKubunoLog implementation or SurfaceOutputLine subscriber must not be able
                // to crash the process from there - caught and logged, never rethrown.
                try
                {
                    KubunoViewsLogHost.Current.WriteLine("[design surface] " + e.Data);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("KubunoViewsLogHost.Current.WriteLine threw for a design-surface stderr line: " + ex);
                }

                try
                {
                    SurfaceOutputLine?.Invoke(e.Data);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("A SurfaceOutputLine subscriber threw for a design-surface stderr line: " + ex);
                }
            };
            _surface.BeginErrorReadLine();
            _surface.Exited += OnSurfaceExited;
            // DSG-6 protocol wiring (`RustDesignSurfaceHost.Protocol.cs`): starts the async stdout
            // reader and re-sends whatever the pane already knows (design mode, selection) to the FRESH
            // process - a restart-with-backoff (`OnSurfaceExited`) must not silently drop that state.
            BeginProtocolIo();
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
            else if (msg == WmKubunoKeyMods)
            {
                // See kubuno_controls::host::WM_KUBUNO_KEY_MODS's own doc: posted immediately BEFORE the
                // key message it describes (same source thread, same destination - PostMessage is FIFO),
                // so simply remembering it here and consuming it in HandleUnhandledKey is reliable.
                _pendingKeyMods = (int)wParam.ToInt64();
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
                var mods = _pendingKeyMods;
                _pendingKeyMods = 0;
                // See the constructor's own comment on why this is suppressed, not "fixed" with another
                // ThreadHelper call directly in WndProc.
#pragma warning disable VSTHRD010
                HandleUnhandledKey(hwnd, msg, wParam, lParam, mods);
#pragma warning restore VSTHRD010
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

        /// <summary>
        /// Routes one forwarded key to the real VS accelerator path (`IVsFilterKeys2.
        /// TranslateAcceleratorEx`, via <see cref="VsFilterKeysBridge"/> and <see cref="_vsFilterKeys2"/>)
        /// when running inside VS, else to a WPF fallback (<see cref="RaiseWpfKeyEvent"/>) - "the VS path is
        /// primary, WPF is only the fallback for when the host is not VS" (confirmed live to be
        /// necessary: <c>ComponentDispatcher.RaiseThreadMessage</c>, tried first, never reached a
        /// `KeyBinding` at all, because WPF only turns a message into a routed keyboard event through an
        /// `HwndSource`'s own `HwndKeyboardInputProvider` for THAT `HwndSource`'s window - the container
        /// is a plain child `HWND`, not an `HwndSource`, so `ComponentDispatcher` was never going to work
        /// here regardless of focus or modifiers).
        ///
        /// Either path reads modifiers from the calling (WPF UI) thread's per-thread key-state table
        /// ("input state" - what `GetKeyState`/`Keyboard.Modifiers`/`IVsFilterKeys2` all read modifiers
        /// from), briefly forced to match <paramref name="mods"/> - the modifiers
        /// `kubuno_controls::host::forward_unhandled_keys` captured at the ORIGINAL, physical moment this
        /// key went down (see `kubuno_controls::host::WM_KUBUNO_KEY_MODS`'s own doc) - confirmed live to
        /// be necessary too: `AttachThreadInput` alone shares only the CURRENT/ambient state, which by
        /// the time this method runs (after a frame, a `PostMessage` round trip and this thread's own
        /// queue) may already show a fast chord's modifiers released again. `AttachThreadInput` is kept
        /// alongside `SetKeyboardState` (belt and braces - some of what downstream code reads may come
        /// from the shared/attached state rather than this thread's own table); both are undone in a
        /// `finally`, and only for the duration of this one call, never for the surface's whole focused
        /// lifetime - a long-lived attachment shares MORE than key state (also the active/focus/capture
        /// window) and would let an unresponsive surface process hang this thread's own input
        /// processing.
        /// </summary>
        private void HandleUnhandledKey(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, int mods)
        {
            var child = Child;
            var childThreadId = child == IntPtr.Zero ? 0 : NativeMethods.GetWindowThreadProcessId(child, IntPtr.Zero);
            var ourThreadId = NativeMethods.GetCurrentThreadId();
            var attached = childThreadId != 0 && childThreadId != ourThreadId
                && NativeMethods.AttachThreadInput(ourThreadId, childThreadId, true);

            var ctrl = (mods & 0x1) != 0;
            var shift = (mods & 0x2) != 0;
            var alt = (mods & 0x4) != 0;
            byte[]? savedState = null;
            var stateApplied = false;
            try
            {
                KubunoViewsLogHost.Current.WriteLine(
                    $"[designer] unhandledKey msg={msg:X} vk={wParam.ToInt64():X} capturedMods=(ctrl={ctrl},shift={shift},alt={alt}) viaVs={_vsFilterKeys2 != null}");

                var state = new byte[256];
                if (NativeMethods.GetKeyboardState(state))
                {
                    savedState = (byte[])state.Clone();
                    state[NativeMethods.VK_CONTROL] = ctrl ? NativeMethods.KEY_DOWN_BIT : (byte)0;
                    state[NativeMethods.VK_SHIFT] = shift ? NativeMethods.KEY_DOWN_BIT : (byte)0;
                    state[NativeMethods.VK_MENU] = alt ? NativeMethods.KEY_DOWN_BIT : (byte)0;
                    stateApplied = NativeMethods.SetKeyboardState(state);
                }

                if (_vsFilterKeys2 != null)
                {
                    // See the constructor's own comment on why this is suppressed, not "fixed" with
                    // another ThreadHelper call directly in this method.
#pragma warning disable VSTHRD010
                    var diagnostic = VsFilterKeysBridge.TryTranslateAccelerator(_vsFilterKeys2, hwnd, msg, wParam, lParam, out var translated);
#pragma warning restore VSTHRD010
                    KubunoViewsLogHost.Current.WriteLine("[designer] " + diagnostic);
                    if (!translated && (msg == WmKeyDown || msg == WmSysKeyDown))
                    {
                        // Found live: Visual Studio does not translate every chord that reaches it this way
                        // (Ctrl+Z came back untranslated), so the pane gets a last chance to act on it.
                        UnhandledSurfaceKey?.Invoke(this, new DesignSurfaceKeyEventArgs((int)wParam.ToInt64(), ctrl, shift, alt));
                    }
                }
                else
                {
                    RaiseWpfKeyEvent(wParam, mods);
                }
            }
            catch (Exception ex)
            {
                // A keyboard shim must never crash the designer pane over a single forwarded key.
                KubunoViewsLogHost.Current.WriteException("Forwarding an unhandled design-surface key failed", ex);
            }
            finally
            {
                if (stateApplied && savedState != null)
                {
                    NativeMethods.SetKeyboardState(savedState);
                }

                if (attached)
                {
                    NativeMethods.AttachThreadInput(ourThreadId, childThreadId, false);
                }
            }
        }

        /// <summary>
        /// The WPF fallback (no VS to route through - this library's own tests, the updated spike):
        /// converts the captured virtual key and modifiers (see
        /// <c>kubuno_controls::host::WM_KUBUNO_KEY_MODS</c>'s own doc) into WPF's own
        /// <see cref="Key"/>/<see cref="ModifierKeys"/> types and asks
        /// <see cref="TryExecuteMatchingInputBinding"/> to find and execute a matching `KeyBinding`
        /// directly - see that method's own doc for why this, rather than synthesizing routed keyboard
        /// events, is what actually works here, confirmed live.
        /// </summary>
        private void RaiseWpfKeyEvent(IntPtr wParam, int mods)
        {
            var key = KeyInterop.KeyFromVirtualKey((int)wParam.ToInt64());
            var modifiers = ModifierKeys.None;
            if ((mods & 0x1) != 0)
            {
                modifiers |= ModifierKeys.Control;
            }

            if ((mods & 0x2) != 0)
            {
                modifiers |= ModifierKeys.Shift;
            }

            if ((mods & 0x4) != 0)
            {
                modifiers |= ModifierKeys.Alt;
            }

            var executed = TryExecuteMatchingInputBinding(key, modifiers);
            KubunoViewsLogHost.Current.WriteLine($"[designer] RaiseWpfKeyEvent key={key} modifiers={modifiers} executed={executed}");
        }

        /// <summary>
        /// Walks up from this element (the visual tree, falling back to the logical tree for a
        /// <see cref="ContentElement"/>) looking for an <see cref="InputBinding"/> whose
        /// <see cref="KeyGesture"/> matches, and executes its <see cref="ICommand"/> directly.
        ///
        /// This is DELIBERATELY not done by synthesizing <c>PreviewKeyDown</c>/<c>KeyDown</c> through
        /// <see cref="InputManager.ProcessInput"/> and letting WPF's own <see cref="CommandManager"/>
        /// translate it into the bound command - that WAS the first WPF-fallback implementation, and was
        /// confirmed live, repeatedly and reproducibly, to silently NOT invoke the command, even with
        /// <see cref="Keyboard.FocusedElement"/> and <see cref="Keyboard.Modifiers"/> BOTH verified
        /// correct at the exact moment of the call (logged). Worse, making that approach route correctly
        /// at all required an explicit <see cref="Keyboard.Focus(IInputElement)"/> call first, which -
        /// confirmed live - moves NATIVE Win32 focus from the surface's own child window onto this
        /// class's container, so the very next physical keystroke would go to WPF instead of the
        /// surface; restoring native focus after (tried both synchronously and deferred via
        /// <see cref="DispatcherObject.Dispatcher"/>.<c>BeginInvoke</c>) made the KeyBinding stop firing
        /// again. This method sidesteps the whole problem: it never touches focus, native or logical, at
        /// all - deterministic, and exactly what is needed here (fire a VS-accelerator-shaped
        /// `KeyBinding`), without depending on the precise timing of WPF's own routed-input pipeline.
        /// </summary>
        private bool TryExecuteMatchingInputBinding(Key key, ModifierKeys modifiers)
        {
            for (DependencyObject? node = this; node != null; node = GetVisualOrLogicalParent(node))
            {
                var bindings = node switch
                {
                    UIElement ue => ue.InputBindings,
                    ContentElement ce => ce.InputBindings,
                    _ => null,
                };
                if (bindings == null)
                {
                    continue;
                }

                foreach (InputBinding binding in bindings)
                {
                    if (binding.Gesture is not KeyGesture gesture || gesture.Key != key || gesture.Modifiers != modifiers)
                    {
                        continue;
                    }

                    var parameter = binding.CommandParameter;
                    if (binding.Command is RoutedCommand routedCommand)
                    {
                        var target = binding.CommandTarget ?? (node as IInputElement);
                        if (routedCommand.CanExecute(parameter, target))
                        {
                            routedCommand.Execute(parameter, target);
                            return true;
                        }
                    }
                    else if (binding.Command != null && binding.Command.CanExecute(parameter))
                    {
                        binding.Command.Execute(parameter);
                        return true;
                    }
                }
            }

            return false;
        }

        private static DependencyObject? GetVisualOrLogicalParent(DependencyObject node)
        {
            if (node is Visual visual)
            {
                var visualParent = VisualTreeHelper.GetParent(visual);
                if (visualParent != null)
                {
                    return visualParent;
                }
            }

            return LogicalTreeHelper.GetParent(node);
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
        /// <summary>Must match Rust's <c>kubuno_controls::host::WM_KUBUNO_KEY_MODS</c> exactly (<c>WM_APP + 0x4B50</c>).</summary>
        private const int WmKubunoKeyMods = 0x8000 + 0x4B50;
    }
}
