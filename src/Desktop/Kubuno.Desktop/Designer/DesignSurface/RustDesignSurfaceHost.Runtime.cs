using System;
using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Kubuno.Views.Logging;
using Kubuno.Views.Designer;
using Kubuno.Views.Designer.DesignSurface;

namespace Kubuno.Desktop.Designer.DesignSurface
{
    /// <summary>
    /// The runtime half of <see cref="RustDesignSurfaceHost"/> (docs/DESIGNER.md section 15): which exe
    /// the pane runs, the hot swap onto a rebuilt project runtime, and the <c>surfaceInfo</c> ABI
    /// handshake that refuses a surface whose loaded <c>kubuno_ui.dll</c> is not the one it was linked
    /// against.
    /// </summary>
    public sealed partial class RustDesignSurfaceHost : IDesignSurfaceRuntimeAware
    {
        private enum HandshakeState
        {
            Waiting,
            Verified,
            Refused,
        }

        private readonly DesignSurfaceRuntimeLease _lease;
        private DesignSurfaceRuntime _runtime;
        private HandshakeState _handshake;
        /// <summary>The current runtime failed the handshake: no restart until the source offers another one.</summary>
        private bool _rejected;

        private string CurrentExePath => _runtime.ExePath;

        public IDesignSurfaceRuntimeSource RuntimeSource => _lease.Source;

        /// <summary>The runtime the pane currently runs (tests, diagnostics).</summary>
        public DesignSurfaceRuntime Runtime => _runtime;

        public event EventHandler<string>? RuntimeRejected;

        private void OnRuntimeSourceChanged(object? sender, EventArgs e)
        {
            // Raised on any thread (a design build completes on the thread pool).
#pragma warning disable VSTHRD001, VSTHRD110 // same reasoning as OnSurfaceExited: no JoinableTaskFactory outside VS.
            Dispatcher.BeginInvoke(new Action(ApplyRuntimeFromSource));
#pragma warning restore VSTHRD001, VSTHRD110
        }

        /// <summary>
        /// Hot swap: when the source offers a different exe (the project was built, or rebuilt with a
        /// changed <c>kubuno_ui</c>), the running surface is stopped and the new one started in its place;
        /// <see cref="BeginProtocolIo"/> hands it the document text, design mode and whole selection.
        /// </summary>
        private void ApplyRuntimeFromSource()
        {
            if (_disposed)
            {
                return;
            }

            var next = _lease.Source.Current;
            if (next is null || next.IsSameAs(_runtime))
            {
                return;
            }

            KubunoViewsLogHost.Current.WriteLine($"[designer] design surface runtime: {(next.IsProjectRuntime ? "project" : "bundled")} {next.ExePath}");
            _runtime = next;
            _rejected = false;
            _backoffMs = BackoffInitialMs;
            _retryTimer.Stop();
            if (_container == IntPtr.Zero)
            {
                return; // Not built yet: BuildWindowCore starts the new runtime.
            }

            var previous = _surface;
            _surface = null;
            StopProcess(previous);
            StartOrShowProblem();
        }

        private static void StopProcess(System.Diagnostics.Process? process)
        {
            if (process is null)
            {
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                // Already gone.
            }
        }

        /// <summary>Handles the <c>surfaceInfo</c> line (on the stdout reader's thread); false for any other line.</summary>
        private bool TryHandleSurfaceInfo(string line)
        {
            if (!DesignSurfaceProtocol.TryParseSurfaceInfo(line, out var version, out var uiDll, out var reportedSha))
            {
                if (_handshake == HandshakeState.Waiting)
                {
                    // Every surface starts with surfaceInfo: one that does not is older than this host.
                    Refuse("the design surface did not identify its kubuno_ui.dll");
                }

                return false;
            }

            var runtime = _runtime;
            var problem = DesignSurfaceProtocol.CheckSurfaceInfo(version, uiDll, reportedSha, runtime.ExePath, runtime.ExpectedUiDllSha256, Sha256OrNull);
            if (problem is null)
            {
                _handshake = HandshakeState.Verified;
                KubunoViewsLogHost.Current.WriteLine($"[designer] design surface uses {uiDll} ({(runtime.IsProjectRuntime ? "the project's runtime" : "the bundled runtime")}; ABI check passed).");
            }
            else
            {
                Refuse(problem);
            }

            return true;
        }

        private void Refuse(string problem)
        {
            _handshake = HandshakeState.Refused;
            var message = DesignerText.RuntimeRejected(problem);
            KubunoViewsLogHost.Current.WriteLine("[designer] " + message);
#pragma warning disable VSTHRD001, VSTHRD110 // same reasoning as OnSurfaceExited.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_disposed)
                {
                    return;
                }

                _rejected = true;
                var surface = _surface;
                StopProcess(surface);
                NativeMethods.SetWindowText(_container, message);
                RuntimeRejected?.Invoke(this, message);
                _lease.Source.ReportRejected(problem);
            }));
#pragma warning restore VSTHRD001, VSTHRD110
        }

        private static string? Sha256OrNull(string path)
        {
            try
            {
                using var sha = SHA256.Create();
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var hash = sha.ComputeHash(stream);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                {
                    builder.Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _lease.Source.Changed -= OnRuntimeSourceChanged;
                _lease.Dispose();
                DisposeResources();
            }

            base.Dispose(disposing);
        }
    }
}
