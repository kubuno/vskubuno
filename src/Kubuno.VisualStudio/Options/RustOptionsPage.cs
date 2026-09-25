using System.ComponentModel;
using System.Windows.Forms.Design;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.VisualStudio.Options
{
    /// <summary>
    /// Tools &gt; Options &gt; Kubuno &gt; Rust. <see cref="LanguageService.RustLanguageClient"/> reads
    /// <see cref="RustAnalyzerPathOverride"/> when starting rust-analyzer; the format-on-save
    /// document-save hook reads <see cref="FormatOnSave"/>.
    /// </summary>
    public sealed class RustOptionsPage : DialogPage
    {
        [Category("rust-analyzer")]
        [DisplayName("Path override")]
        [Description("Full path to rust-analyzer.exe. When empty, Kubuno tries 'rustup which rust-analyzer', " +
            "then %USERPROFILE%\\.cargo\\bin\\rust-analyzer.exe, then the PATH environment variable.")]
        [Editor(typeof(FileNameEditor), typeof(System.Drawing.Design.UITypeEditor))]
        public string RustAnalyzerPathOverride { get; set; } = string.Empty;

        [Category("Editor")]
        [DisplayName("Format on save")]
        [Description("Run Format Document (rustfmt, via rust-analyzer) on every .rs file before it is saved. Off by default.")]
        public bool FormatOnSave { get; set; }

        [Category("Diagnostics")]
        [DisplayName("LSP trace")]
        [Description("How much raw LSP traffic (requests/responses/notifications to and from rust-analyzer) to log " +
            "in the Kubuno Output pane. Off (default) logs only the startup line and errors; Messages adds one line " +
            "per request/response; Verbose adds full StreamJsonRpc detail including raw JSON payloads.")]
        [DefaultValue(LspTraceLevel.Off)]
        public LspTraceLevel LspTrace { get; set; } = LspTraceLevel.Off;
    }
}
