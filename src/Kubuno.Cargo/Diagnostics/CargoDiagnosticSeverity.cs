namespace Kubuno.Cargo.Diagnostics
{
    /// <summary>Maps rustc/cargo's diagnostic "level" string to an Error-List-ready severity.</summary>
    public enum CargoDiagnosticSeverity
    {
        Error,
        Warning,
        Note,
        Help,

        /// <summary>rustc level "failure-note": a trailing summary like "aborting due to N previous errors", never has spans.</summary>
        FailureNote,

        /// <summary>rustc level "error: internal compiler error".</summary>
        InternalCompilerError,
    }
}
