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

        [Category("Editor")]
        [DisplayName("Inlay hints")]
        [Description("rust-analyzer's inline hints (inferred types, parameter names, method chains) in .rs files. Always " +
            "(default); While pressing Alt+F1, like C#'s \"Display inline hints only when pressing Alt+F1\"; Visual Studio " +
            "setting (Text Editor > All Languages > Inlay Hints); or Never.")]
        [DefaultValue(RustInlayHintsMode.Always)]
        public RustInlayHintsMode InlayHints { get; set; } = RustInlayHintsMode.Always;

        [Category("Editor")]
        [DisplayName("CodeLens")]
        [Description("Show reference and implementation counts (\"3 references\") above Rust items, like C#'s CodeLens. " +
            "Click a count to open Find All References / the implementations.")]
        [DefaultValue(true)]
        public bool CodeLens { get; set; } = true;

        /// <summary>Raised after the page's settings were applied (open editors refresh their inlay hints and code lenses).</summary>
        internal static event System.EventHandler? Applied;

        protected override void OnApply(PageApplyEventArgs e)
        {
            base.OnApply(e);
            if (e.ApplyBehavior == ApplyKind.Apply)
            {
                Applied?.Invoke(this, System.EventArgs.Empty);
            }
        }
    }

    /// <summary>When rust-analyzer's inlay hints are shown (<see cref="RustOptionsPage.InlayHints"/>).</summary>
    public enum RustInlayHintsMode
    {
        Always,
        WhilePressingAltF1,
        VisualStudioSetting,
        Never,
    }
}
