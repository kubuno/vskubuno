namespace Kubuno.Rust.Cargo.Diagnostics
{
    /// <summary>Base type for one line of <c>--message-format=json</c> output, parsed by <see cref="CargoMessageParser"/>.</summary>
    public abstract class CargoBuildEvent
    {
    }

    /// <summary>A "compiler-message" line: a diagnostic (error/warning/note/help/failure-note).</summary>
    public sealed class CargoDiagnosticEvent : CargoBuildEvent
    {
        public CargoDiagnosticEvent(CargoDiagnostic diagnostic)
        {
            Diagnostic = diagnostic;
        }

        public CargoDiagnostic Diagnostic { get; }
    }

    /// <summary>A "compiler-artifact" line: one target finished (or was already fresh).</summary>
    public sealed class CargoArtifactEvent : CargoBuildEvent
    {
        public CargoArtifactEvent(CargoArtifact artifact)
        {
            Artifact = artifact;
        }

        public CargoArtifact Artifact { get; }
    }

    /// <summary>A "build-finished" line: the last event of a build/check/test run.</summary>
    public sealed class CargoBuildFinishedEvent : CargoBuildEvent
    {
        public CargoBuildFinishedEvent(bool success)
        {
            Success = success;
        }

        public bool Success { get; }
    }

    /// <summary>
    /// A "build-script-executed" line: a package's build script ran; <see cref="LinkedPaths"/> are the library
    /// search paths it asked for (<c>cargo:rustc-link-search</c>, as <c>native=C:\…</c> / <c>all=…</c> or a plain
    /// path) — what anything linking that package later must pass to <c>rustc -L</c> too.
    /// </summary>
    public sealed class CargoBuildScriptEvent : CargoBuildEvent
    {
        public CargoBuildScriptEvent(string packageId, System.Collections.Generic.IReadOnlyList<string> linkedPaths)
        {
            PackageId = packageId;
            LinkedPaths = linkedPaths;
        }

        public string PackageId { get; }

        public System.Collections.Generic.IReadOnlyList<string> LinkedPaths { get; }
    }

    /// <summary>
    /// Any other line: a recognized-but-unmodeled reason, or a
    /// line that isn't valid JSON at all (Cargo occasionally writes plain text to stdout even
    /// under <c>--message-format=json</c>). Carries the raw line so callers can still show or
    /// log it instead of losing it.
    /// </summary>
    public sealed class CargoUnrecognizedEvent : CargoBuildEvent
    {
        public CargoUnrecognizedEvent(string? reason, string rawLine)
        {
            Reason = reason;
            RawLine = rawLine;
        }

        /// <summary>The JSON "reason" field, or null when the line wasn't valid JSON at all.</summary>
        public string? Reason { get; }

        public string RawLine { get; }
    }
}
