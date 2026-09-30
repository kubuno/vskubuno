using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Kubuno.Cargo.Diagnostics.Internal;
using Kubuno.Cargo.Internal;

namespace Kubuno.Cargo.Diagnostics
{
    /// <summary>
    /// Parses <c>cargo build/check/test --message-format=json[-diagnostic-rendered-ansi]</c>
    /// output, one line at a time, into <see cref="CargoBuildEvent"/>s ready for an Error List
    /// (diagnostics) or a build/artifact view.
    /// </summary>
    public static class CargoMessageParser
    {
        /// <param name="line">One line of Cargo's line-delimited JSON output.</param>
        /// <param name="workspaceRoot">
        /// Absolute workspace root (<see cref="Metadata.CargoMetadata.WorkspaceRoot"/>). Span
        /// file names in compiler messages are reported relative to the directory Cargo was
        /// invoked from — normally the workspace root — so this is required to turn them into
        /// absolute, Error-List-ready paths.
        /// </param>
        public static CargoBuildEvent Parse(string line, string workspaceRoot)
        {
            if (line is null)
            {
                throw new ArgumentNullException(nameof(line));
            }
            if (workspaceRoot is null)
            {
                throw new ArgumentNullException(nameof(workspaceRoot));
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                return new CargoUnrecognizedEvent(reason: null, rawLine: line);
            }

            RawEventEnvelope envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<RawEventEnvelope>(line, CargoJsonOptions.Default)
                           ?? new RawEventEnvelope();
            }
            catch (JsonException)
            {
                // Cargo occasionally writes a plain-text line to stdout (e.g. a nightly-only
                // feature notice) even under --message-format=json: never throw on it.
                return new CargoUnrecognizedEvent(reason: null, rawLine: line);
            }

            switch (envelope.Reason)
            {
                case "compiler-message":
                    return ParseCompilerMessage(line, workspaceRoot);
                case "compiler-artifact":
                    return ParseCompilerArtifact(line);
                case "build-finished":
                    return ParseBuildFinished(line);
                default:
                    return new CargoUnrecognizedEvent(envelope.Reason, line);
            }
        }

        /// <summary>Convenience overload for streaming a whole build's output.</summary>
        public static IEnumerable<CargoBuildEvent> ParseLines(IEnumerable<string> lines, string workspaceRoot)
        {
            if (lines is null)
            {
                throw new ArgumentNullException(nameof(lines));
            }
            foreach (string line in lines)
            {
                yield return Parse(line, workspaceRoot);
            }
        }

        private static CargoDiagnosticEvent ParseCompilerMessage(string line, string workspaceRoot)
        {
            var raw = JsonSerializer.Deserialize<RawCompilerMessage>(line, CargoJsonOptions.Default)
                      ?? new RawCompilerMessage();

            var diagnostic = new CargoDiagnostic
            {
                Severity = MapSeverity(raw.Message.Level),
                Code = raw.Message.Code?.Code,
                Message = raw.Message.Message,
                RenderedText = raw.Message.Rendered is null ? null : AnsiText.Strip(raw.Message.Rendered),
                PackageId = raw.PackageId,
                TargetName = raw.Target?.Name,
                Children = MapChildren(raw.Message.Children),
            };

            RawSpan? primary = FindPrimarySpan(raw.Message.Spans);
            if (primary is not null)
            {
                diagnostic.FilePath = ResolvePath(workspaceRoot, primary.FileName);
                diagnostic.Line = primary.LineStart;
                diagnostic.Column = primary.ColumnStart;
            }

            return new CargoDiagnosticEvent(diagnostic);
        }

        /// <summary>
        /// Only ever returns a span explicitly flagged primary by rustc. A diagnostic's other
        /// spans point at *related* code (e.g. "value moved here") that must never be mistaken
        /// for where the diagnostic itself should be shown.
        /// </summary>
        private static RawSpan? FindPrimarySpan(IReadOnlyList<RawSpan> spans)
        {
            foreach (RawSpan span in spans)
            {
                if (span.IsPrimary)
                {
                    return span;
                }
            }
            return null;
        }

        private static IReadOnlyList<CargoDiagnosticNote> MapChildren(IReadOnlyList<RawDiagnostic> children)
        {
            if (children.Count == 0)
            {
                return Array.Empty<CargoDiagnosticNote>();
            }

            var notes = new List<CargoDiagnosticNote>(children.Count);
            foreach (RawDiagnostic child in children)
            {
                notes.Add(new CargoDiagnosticNote(MapSeverity(child.Level), child.Message));
            }
            return notes;
        }

        private static CargoArtifactEvent ParseCompilerArtifact(string line)
        {
            var raw = JsonSerializer.Deserialize<RawCompilerArtifact>(line, CargoJsonOptions.Default)
                      ?? new RawCompilerArtifact();

            var artifact = new CargoArtifact
            {
                PackageId = raw.PackageId ?? string.Empty,
                Target = raw.Target ?? new Metadata.CargoTarget(),
                Filenames = raw.Filenames,
                Executable = raw.Executable,
                Fresh = raw.Fresh,
            };
            return new CargoArtifactEvent(artifact);
        }

        private static CargoBuildFinishedEvent ParseBuildFinished(string line)
        {
            var raw = JsonSerializer.Deserialize<RawBuildFinished>(line, CargoJsonOptions.Default)
                      ?? new RawBuildFinished();
            return new CargoBuildFinishedEvent(raw.Success);
        }

        private static string ResolvePath(string workspaceRoot, string relativeOrAbsolutePath)
        {
            // Diagnostics pointing outside the workspace (e.g. into a crates.io dependency's
            // registry cache) are already reported as absolute by Cargo.
            if (Path.IsPathRooted(relativeOrAbsolutePath))
            {
                return relativeOrAbsolutePath;
            }
            return Path.GetFullPath(Path.Combine(workspaceRoot, relativeOrAbsolutePath));
        }

        private static CargoDiagnosticSeverity MapSeverity(string level)
        {
            if (string.IsNullOrEmpty(level))
            {
                return CargoDiagnosticSeverity.Note;
            }
            if (level.StartsWith("error: internal compiler error", StringComparison.Ordinal))
            {
                return CargoDiagnosticSeverity.InternalCompilerError;
            }

            switch (level)
            {
                case "error":
                    return CargoDiagnosticSeverity.Error;
                case "warning":
                    return CargoDiagnosticSeverity.Warning;
                case "note":
                    return CargoDiagnosticSeverity.Note;
                case "help":
                    return CargoDiagnosticSeverity.Help;
                case "failure-note":
                    return CargoDiagnosticSeverity.FailureNote;
                default:
                    return CargoDiagnosticSeverity.Note;
            }
        }
    }
}
