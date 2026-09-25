namespace Kubuno.VisualStudio.Options
{
    /// <summary>
    /// How much raw LSP traffic <see cref="LanguageService.RustLanguageClient"/> forwards to the
    /// "Kubuno" Output pane, in addition to the startup line (rust-analyzer path/source) and
    /// errors, which are always logged regardless of this setting.
    /// </summary>
    public enum LspTraceLevel
    {
        /// <summary>No per-message tracing (default) - only startup and errors are logged.</summary>
        Off,

        /// <summary>One line per LSP request/response/notification.</summary>
        Messages,

        /// <summary>Full StreamJsonRpc trace detail, including raw JSON payloads.</summary>
        Verbose,
    }
}
