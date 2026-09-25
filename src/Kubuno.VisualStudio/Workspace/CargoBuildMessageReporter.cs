using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Kubuno.Cargo.Diagnostics;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Workspace.Build;

namespace Kubuno.VisualStudio.Workspace
{
    /// <summary>
    /// Turns <c>cargo build/clean --message-format=json-diagnostic-rendered-ansi</c> output
    /// lines (via <see cref="CargoMessageParser"/>) into <see cref="BuildMessage"/>s and reports
    /// them through <see cref="IBuildMessageService"/>: a <c>TaskType</c> other than <c>None</c>
    /// becomes a clickable **Error List** entry (file/line/column, severity, code, project - see
    /// Microsoft's "Workspace build in Visual Studio" doc). Verified live: for an Error/Warning
    /// diagnostic, only the concise <c>TaskText</c> reliably reaches the Build Output pane, not
    /// the full rustc-rendered text (source snippet, carets, notes) also set on the same
    /// message's <c>LogMessage</c>. A second, separate <c>None</c>-typed <see cref="BuildMessage"/>
    /// carrying just that rendered text was tried next, to get it into the Build pane
    /// independently of the Error List entry - and reverted: it reproducibly crashed devenv
    /// itself (a native access violation in msenv.dll a few seconds after a build with any
    /// reported diagnostic). <see cref="ReportDiagnostic"/>'s current approach - still exactly
    /// one <see cref="BuildMessage"/> per diagnostic for the Error List, plus a direct
    /// <c>IVsOutputWindowPane.OutputStringThreadSafe</c> write to the Build pane for the full
    /// rendered text, bypassing <see cref="IBuildMessageService"/> for that part entirely - was
    /// verified live not to crash (two consecutive builds with a diagnostic, devenv monitored
    /// alive for 30s after each).
    /// Messages are buffered and sent in batches via <see cref="FlushAsync"/> rather than one
    /// <c>ReportBuildMessages</c> call per line, since the underlying call is a
    /// <see cref="IBuildMessageService.ReportBuildMessages"/> round-trip.
    /// </summary>
    internal sealed class CargoBuildMessageReporter
    {
        private readonly IBuildMessageService? _service;
        private readonly string _workspaceRoot;
        private readonly string _manifestPath;
        private readonly List<BuildMessage> _pending = new();

        public CargoBuildMessageReporter(IBuildMessageService? service, string workspaceRoot, string manifestPath)
        {
            _service = service;
            _workspaceRoot = workspaceRoot;
            _manifestPath = manifestPath;
        }

        /// <summary>Plain informational text (command lines, artifact/build-finished summaries): Build pane only.</summary>
        public void LogInfo(string text) =>
            _pending.Add(new BuildMessage { Type = BuildMessage.TaskType.None, LogMessage = text + Environment.NewLine });

        /// <summary>A failure that isn't itself a rustc diagnostic (e.g. cargo could not be started): both panes.</summary>
        public void LogError(string text) =>
            _pending.Add(new BuildMessage
            {
                Type = BuildMessage.TaskType.Error,
                TaskText = text,
                LogMessage = text + Environment.NewLine,
            });

        /// <summary>Parses one line of <c>--message-format=json...</c> output and queues the resulting message(s), if any.</summary>
        public void ReportBuildLine(string line)
        {
            CargoBuildEvent buildEvent;
            try
            {
                buildEvent = CargoMessageParser.Parse(line, _workspaceRoot);
            }
            catch (Exception exception)
            {
                // Never let a single malformed line abort the whole build/report loop.
                LogInfo(line);
                LogInfo($"(Kubuno: could not parse the line above: {exception.Message})");
                return;
            }

            switch (buildEvent)
            {
                case CargoDiagnosticEvent diagnosticEvent:
                    ReportDiagnostic(diagnosticEvent.Diagnostic);
                    break;

                case CargoArtifactEvent artifactEvent:
                    var artifact = artifactEvent.Artifact;
                    LogInfo(artifact.Fresh
                        ? $"{artifact.Target} (up to date)"
                        : $"Compiling {artifact.Target}");
                    break;

                case CargoBuildFinishedEvent finishedEvent:
                    LogInfo(finishedEvent.Success ? "Build succeeded." : "Build failed.");
                    break;

                case CargoUnrecognizedEvent unrecognizedEvent:
                    if (!string.IsNullOrEmpty(unrecognizedEvent.RawLine))
                    {
                        // Cargo occasionally writes a plain-text line to stdout even under
                        // --message-format=json (e.g. a nightly-only feature notice): pass it
                        // through as-is rather than dropping it.
                        LogInfo(unrecognizedEvent.RawLine);
                    }
                    break;
            }
        }

        public async Task FlushAsync()
        {
            if (_pending.Count == 0)
            {
                return;
            }

            var batch = _pending.ToArray();
            _pending.Clear();

            if (_service is null)
            {
                return;
            }

            await _service.ReportBuildMessages(batch).ConfigureAwait(false);
        }

        private void ReportDiagnostic(CargoDiagnostic diagnostic)
        {
            // A second, separate (Type = None) BuildMessage per diagnostic - tried first, to
            // route the full rustc-rendered text to the Build pane independently of the Error
            // List entry - was reverted: it reproduced a devenv crash (native access violation
            // in msenv.dll, consistently a few seconds into a build with a reported diagnostic).
            // A single BuildMessage per diagnostic (below) does not crash.
            var renderedText = diagnostic.RenderedText ?? diagnostic.Message;
            var message = new BuildMessage
            {
                Type = MapSeverity(diagnostic.Severity),
                Code = diagnostic.Code ?? string.Empty,
                TaskText = diagnostic.Message,
                LogMessage = renderedText + Environment.NewLine,
                ProjectFile = _manifestPath,
            };

            if (diagnostic.FilePath is not null)
            {
                message.File = diagnostic.FilePath;
                message.LineNumber = diagnostic.Line ?? 0;
                message.ColumnNumber = diagnostic.Column ?? 0;
            }

            _pending.Add(message);

            // Second attempt at getting the full rendered text (source snippet, carets, notes)
            // into the Build pane: write it directly via IVsOutputWindow - the same low-level,
            // proven-stable mechanism KubunoLog already uses for the "Kubuno" pane - rather than
            // through IBuildMessageService at all. This sidesteps whatever in that service's own
            // pipeline caused the crash above, since it never touches it.
            WriteToBuildPane(renderedText + Environment.NewLine);
        }

        private static void WriteToBuildPane(string text)
        {
            try
            {
                // This whole sequence (GlobalProvider.GetService, IVsOutputWindow.GetPane,
                // OutputStringThreadSafe) is the documented free-threaded path to the Output
                // window - ServiceProvider.GlobalProvider marshals internally, GetPane is a cheap
                // metadata lookup with no UI affinity, and OutputStringThreadSafe is explicitly
                // named/documented as safe off the UI thread. This method is called from
                // CargoBuildMessageReporter while streaming cargo's output, which happens off the
                // UI thread (see CargoBuildFileContextAction.RunCargoAsync) - switching to the UI
                // thread just for this would add latency to every streamed build line for no
                // benefit.
#pragma warning disable VSTHRD010
                if (ServiceProvider.GlobalProvider.GetService(typeof(SVsOutputWindow)) is not IVsOutputWindow outputWindow)
                {
                    return;
                }

                var paneGuid = VSConstants.OutputWindowPaneGuid.BuildOutputPane_guid;
                if (ErrorHandler.Failed(outputWindow.GetPane(ref paneGuid, out var pane)) || pane is null)
                {
                    return;
                }

                pane.OutputStringThreadSafe(text);
#pragma warning restore VSTHRD010
            }
            catch (Exception)
            {
                // The Build pane is a nicety on top of the Error List entry already added above;
                // never let a failure here fail the build/report itself.
            }
        }

        private static BuildMessage.TaskType MapSeverity(CargoDiagnosticSeverity severity) => severity switch
        {
            CargoDiagnosticSeverity.Error => BuildMessage.TaskType.Error,
            CargoDiagnosticSeverity.InternalCompilerError => BuildMessage.TaskType.Error,
            CargoDiagnosticSeverity.Warning => BuildMessage.TaskType.Warning,
            _ => BuildMessage.TaskType.None,
        };
    }
}
