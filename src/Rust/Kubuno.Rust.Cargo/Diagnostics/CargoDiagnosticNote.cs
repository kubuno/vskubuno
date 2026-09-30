namespace Kubuno.Rust.Cargo.Diagnostics
{
    /// <summary>A child of a diagnostic: rustc's "note:" and "help:" sub-messages.</summary>
    public sealed class CargoDiagnosticNote
    {
        public CargoDiagnosticNote(CargoDiagnosticSeverity severity, string message)
        {
            Severity = severity;
            Message = message;
        }

        public CargoDiagnosticSeverity Severity { get; }

        public string Message { get; }

        public override string ToString() => $"{Severity}: {Message}";
    }
}
