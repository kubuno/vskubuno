using System;
using Kubuno.Cargo.Diagnostics;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Kubuno.Cargo.MSBuild.Tasks.Internal
{
    /// <summary>
    /// Pure mapping from one parsed <see cref="CargoBuildEvent"/> (already produced by
    /// <see cref="CargoMessageParser"/>) to MSBuild logging calls — kept separate from any actual
    /// <see cref="Microsoft.Build.Utilities.Task"/> so it can be unit tested against a plain
    /// <see cref="TaskLoggingHelper"/>, with no live cargo process involved.
    ///
    /// Every diagnostic becomes exactly one Error List entry (<see cref="TaskLoggingHelper.LogError"/>
    /// or <see cref="TaskLoggingHelper.LogWarning"/>, with file/line/column/code so it is clickable
    /// there), plus — when it has one — a second, plain message carrying the full rustc-rendered
    /// text (source snippet, carets, notes) for the Build Output pane. This mirrors the two-sink
    /// split the Open Folder integration already uses (`IBuildMessageService` for the Error List
    /// entry, `IVsOutputWindowPane` for the rendered text) instead of trying to cram both into one
    /// message, which that integration found could crash devenv — see this repo's CHANGELOG.
    /// </summary>
    public static class CargoDiagnosticLogging
    {
        public static void Log(TaskLoggingHelper log, CargoBuildEvent buildEvent)
        {
            if (log is null)
            {
                throw new ArgumentNullException(nameof(log));
            }
            if (buildEvent is null)
            {
                throw new ArgumentNullException(nameof(buildEvent));
            }

            switch (buildEvent)
            {
                case CargoDiagnosticEvent diagnosticEvent:
                    LogDiagnostic(log, diagnosticEvent.Diagnostic);
                    return;
                case CargoArtifactEvent artifactEvent:
                    log.LogMessage(MessageImportance.Low, "cargo: {0}", artifactEvent.Artifact);
                    return;
                case CargoBuildFinishedEvent finishedEvent:
                    log.LogMessage(
                        finishedEvent.Success ? MessageImportance.Low : MessageImportance.Normal,
                        "cargo: build finished (success={0})",
                        finishedEvent.Success);
                    return;
                case CargoUnrecognizedEvent unrecognizedEvent:
                    if (!string.IsNullOrWhiteSpace(unrecognizedEvent.RawLine))
                    {
                        log.LogMessage(MessageImportance.Low, unrecognizedEvent.RawLine);
                    }
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(buildEvent), buildEvent, "Unknown Cargo build event.");
            }
        }

        private static void LogDiagnostic(TaskLoggingHelper log, CargoDiagnostic diagnostic)
        {
            // rustc's line/column are already 1-based, which is exactly what LogError/LogWarning's
            // own lineNumber/columnNumber parameters expect; 0 (unknown location) is also accepted
            // by those overloads as "no location".
            int line = diagnostic.Line ?? 0;
            int column = diagnostic.Column ?? 0;

            switch (diagnostic.Severity)
            {
                case CargoDiagnosticSeverity.Error:
                case CargoDiagnosticSeverity.InternalCompilerError:
                    log.LogError(
                        subcategory: null,
                        errorCode: diagnostic.Code,
                        helpKeyword: null,
                        file: diagnostic.FilePath,
                        lineNumber: line,
                        columnNumber: column,
                        endLineNumber: 0,
                        endColumnNumber: 0,
                        message: diagnostic.Message);
                    break;
                case CargoDiagnosticSeverity.Warning:
                    log.LogWarning(
                        subcategory: null,
                        warningCode: diagnostic.Code,
                        helpKeyword: null,
                        file: diagnostic.FilePath,
                        lineNumber: line,
                        columnNumber: column,
                        endLineNumber: 0,
                        endColumnNumber: 0,
                        message: diagnostic.Message);
                    break;
                default:
                    // Note / Help / FailureNote: never their own Error List entry (rustc always
                    // attaches these to a preceding error/warning as children in the same
                    // diagnostic — logging them as bare messages here is only a fallback for the
                    // rare top-level "failure-note" summary line, which has no primary span).
                    log.LogMessage(MessageImportance.Normal, "{0}: {1}", diagnostic.Severity, diagnostic.Message);
                    break;
            }

            if (!string.IsNullOrEmpty(diagnostic.RenderedText))
            {
                log.LogMessage(MessageImportance.Normal, diagnostic.RenderedText);
            }
        }
    }
}
