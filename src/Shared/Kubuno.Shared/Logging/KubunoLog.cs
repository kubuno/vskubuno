using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Kubuno.Shared.Logging
{
    /// <summary>
    /// Writes to the "Kubuno" pane of the Output window: rust-analyzer discovery decisions,
    /// language client start/stop/errors, and (via <see cref="CreateJsonRpcTraceListener"/>) raw
    /// LSP traffic. A static, lazily-created singleton rather than something threaded through
    /// every MEF-constructed component, since MEF parts (the language client, the content type
    /// exports) are built by the MEF container, not by the package. Every layer writes here.
    ///
    /// The pane itself is created once, on the UI thread, by <see cref="Extensibility.KubunoLayerHost"/>
    /// (via <see cref="Initialize"/>) - not lazily from here. The language client can log before the
    /// package has finished loading (MEF activation is not gated on package load), so messages
    /// written before <see cref="Initialize"/> runs are buffered and flushed once the pane exists,
    /// rather than silently dropped or (the previous, broken approach) built lazily via a blocking
    /// <c>JoinableTaskFactory.Run</c> call from arbitrary background threads, whose failures had no
    /// way to surface (the pane was simply never created, with nothing logged anywhere about why).
    ///
    /// Every method is safe to call from any thread and never throws: logging must not be a new
    /// source of failure for the very code that reports failures.
    /// </summary>
    public static class KubunoLog
    {
        private const int MaxBufferedLines = 500;

        private static readonly object SyncRoot = new();
        private static readonly List<string> PendingLines = new();
        private static IVsOutputWindowPane? _pane;

        /// <summary>Called once, on the UI thread, from <see cref="Extensibility.KubunoLayerHost"/> (at idle, after the solution load).</summary>
        public static void Initialize(IVsOutputWindowPane pane)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            List<string> toFlush;
            lock (SyncRoot)
            {
                if (_pane != null)
                {
                    return;
                }

                _pane = pane;
                toFlush = new List<string>(PendingLines);
                PendingLines.Clear();
            }

            foreach (var line in toFlush)
            {
                WriteToPane(pane, line);
            }
        }

        public static void WriteLine(string message)
        {
            var line = FormatLine(message);
            MirrorToFile(line);

            IVsOutputWindowPane? pane;
            lock (SyncRoot)
            {
                pane = _pane;
                if (pane == null)
                {
                    PendingLines.Add(line);
                    if (PendingLines.Count > MaxBufferedLines)
                    {
                        PendingLines.RemoveAt(0);
                    }

                    return;
                }
            }

            WriteToPane(pane, line);
        }

        /// <summary>Brings the "Kubuno" pane to the front of the Output window (UI thread).</summary>
        public static void Activate()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            IVsOutputWindowPane? pane;
            lock (SyncRoot)
            {
                pane = _pane;
            }

            try
            {
                pane?.Activate();
                if (ServiceProvider.GlobalProvider.GetService(typeof(SVsUIShell)) is IVsUIShell shell)
                {
                    var outputWindow = new Guid("34E76E81-EE4A-11D0-AE2E-00A0C90FFFC3"); // GUID_OutWindow
                    if (shell.FindToolWindow((uint)__VSFINDTOOLWIN.FTW_fForceCreate, ref outputWindow, out var frame) == 0 && frame != null)
                    {
                        frame.ShowNoActivate();
                    }
                }
            }
            catch (Exception)
            {
                // Showing the pane is a convenience; the output is written either way.
            }
        }

        public static void WriteException(string context, Exception exception)
        {
            WriteLine($"{context}: {exception}");
        }

        /// <summary>
        /// A <see cref="TraceListener"/> that forwards to the Kubuno pane, meant to be attached to
        /// the StreamJsonRpc <c>JsonRpc.TraceSource</c> of the rust-analyzer connection so LSP
        /// traffic/errors show up here too (see RustLanguageClient.AttachForCustomMessageAsync).
        /// </summary>
        public static TraceListener CreateJsonRpcTraceListener() => new OutputPaneTraceListener();

        /// <summary>
        /// The file named by the <c>KUBUNO_VS_LOG</c> environment variable, if any: every line of the pane is also appended there
        /// (support and automated checks, where the Output window cannot be read).
        /// </summary>
        private static readonly string? MirrorPath = Environment.GetEnvironmentVariable("KUBUNO_VS_LOG");

        private static void MirrorToFile(string line)
        {
            if (string.IsNullOrEmpty(MirrorPath))
            {
                return;
            }

            try
            {
                lock (SyncRoot)
                {
                    System.IO.File.AppendAllText(MirrorPath, line + Environment.NewLine);
                }
            }
            catch (Exception)
            {
                // Best effort, like the pane itself.
            }
        }

        private static string FormatLine(string message) => $"[{DateTime.Now:HH:mm:ss.fff}] {message}";

        private static void WriteToPane(IVsOutputWindowPane pane, string line)
        {
            try
            {
#pragma warning disable VSTHRD010 // OutputStringThreadSafe is documented safe to call off the UI thread - that is the point of WriteLine.
                pane.OutputStringThreadSafe(line + Environment.NewLine);
#pragma warning restore VSTHRD010
            }
            catch (Exception)
            {
                // Logging itself must never throw into caller code; there is nowhere further to
                // report a failure of the logger.
            }
        }

        private sealed class OutputPaneTraceListener : TraceListener
        {
            public override void Write(string? message)
            {
                if (message != null)
                {
                    WriteLine(message);
                }
            }

            public override void WriteLine(string? message)
            {
                if (message != null)
                {
                    KubunoLog.WriteLine(message);
                }
            }
        }
    }
}
