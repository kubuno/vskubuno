using System;
using System.Collections.Generic;

namespace Kubuno.Rust.Cargo.Diagnostics
{
    /// <summary>
    /// One rustc diagnostic (a "compiler-message" build event), flattened into what a VS
    /// Error List entry needs: severity, code, message, an absolute file path with a 1-based
    /// line/column (from the message's *primary* span only — secondary spans, e.g. "value
    /// moved here" pointing elsewhere in the file, are never used as the diagnostic's own
    /// location), and its child notes/help.
    /// </summary>
    public sealed class CargoDiagnostic
    {
        public CargoDiagnosticSeverity Severity { get; set; }

        /// <summary>e.g. "E0382", or null for lint-less/summary messages.</summary>
        public string? Code { get; set; }

        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// The full rustc-rendered text (multi-line, with the source snippet and carets), with
        /// any ANSI escape codes already stripped.
        /// </summary>
        public string? RenderedText { get; set; }

        /// <summary>Absolute path of the primary span's file, or null when the message has no primary span.</summary>
        public string? FilePath { get; set; }

        /// <summary>1-based line of the primary span, or null when the message has no primary span.</summary>
        public int? Line { get; set; }

        /// <summary>1-based column of the primary span, or null when the message has no primary span.</summary>
        public int? Column { get; set; }

        public string? PackageId { get; set; }

        /// <summary>Name of the target being compiled when this diagnostic was emitted (e.g. the bin/lib name).</summary>
        public string? TargetName { get; set; }

        public IReadOnlyList<CargoDiagnosticNote> Children { get; set; } = Array.Empty<CargoDiagnosticNote>();

        public override string ToString() =>
            FilePath is null
                ? $"{Severity}: {Message}"
                : $"{Severity}: {Message} ({FilePath}:{Line}:{Column})";
    }
}
