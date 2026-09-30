using System;
using System.Collections.Generic;
using Kubuno.Rust.Cargo.Metadata;

namespace Kubuno.Rust.Cargo.Diagnostics.Internal
{
    // Raw, 1:1 shapes of Cargo's `--message-format=json` lines. Kept separate from the public,
    // curated models (CargoDiagnostic, CargoArtifact...) so the public API stays small and
    // Error-List-shaped while parsing still sees every field it needs. Deliberately omits
    // fields CargoMessageParser never reads (byte offsets, span "text" snippets, macro
    // expansion...) — System.Text.Json ignores unknown JSON members by default, so Cargo can
    // keep adding fields without breaking this. Property names all follow Cargo's snake_case
    // convention, so they round-trip through CargoJsonOptions' naming policy without needing
    // per-property [JsonPropertyName] attributes.

    /// <summary>First-pass shape: just enough to dispatch on "reason".</summary>
    internal sealed class RawEventEnvelope
    {
        public string? Reason { get; set; }
    }

    internal sealed class RawCompilerMessage
    {
        public string Reason { get; set; } = string.Empty;

        public string? PackageId { get; set; }

        public CargoTarget? Target { get; set; }

        public RawDiagnostic Message { get; set; } = new RawDiagnostic();
    }

    internal sealed class RawDiagnostic
    {
        public string Message { get; set; } = string.Empty;

        public RawDiagnosticCode? Code { get; set; }

        public string Level { get; set; } = string.Empty;

        public IReadOnlyList<RawSpan> Spans { get; set; } = Array.Empty<RawSpan>();

        public IReadOnlyList<RawDiagnostic> Children { get; set; } = Array.Empty<RawDiagnostic>();

        /// <summary>Full rustc-rendered text, with ANSI color codes when using the "-ansi" message format.</summary>
        public string? Rendered { get; set; }
    }

    internal sealed class RawDiagnosticCode
    {
        public string? Code { get; set; }
    }

    internal sealed class RawSpan
    {
        /// <summary>Path to the source file, relative to the directory Cargo was invoked from (normally the workspace root).</summary>
        public string FileName { get; set; } = string.Empty;

        public int LineStart { get; set; }

        public int ColumnStart { get; set; }

        public bool IsPrimary { get; set; }
    }

    internal sealed class RawCompilerArtifact
    {
        public string Reason { get; set; } = string.Empty;

        public string? PackageId { get; set; }

        public CargoTarget? Target { get; set; }

        public IReadOnlyList<string> Filenames { get; set; } = Array.Empty<string>();

        public string? Executable { get; set; }

        public bool Fresh { get; set; }
    }

    internal sealed class RawBuildFinished
    {
        public string Reason { get; set; } = string.Empty;

        public bool Success { get; set; }
    }
}
