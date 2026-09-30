using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms.Design;
using Kubuno.Rust.Logic.IntelliSense;
using Kubuno.Core.Settings;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Rust.Options
{
    /// <summary>
    /// Tools &gt; Options &gt; Kubuno &gt; Rust. <see cref="LanguageService.RustLanguageClient"/> reads
    /// <see cref="RustAnalyzerPathOverride"/> when starting rust-analyzer; the format-on-save
    /// document-save hook reads <see cref="FormatOnSave"/>.
    /// </summary>
    // The identity Visual Studio stored this page under before the layers (docs/ARCHITECTURE.md, "Layers (as built)"):
    // the page GUID of the pkgdef and profile (formerly derived from the type's full name) and the classic settings
    // key (DialogPage's default is derived from the full name too), kept so existing settings and exports still apply.
    [Guid("038ac86f-881c-3765-8011-46a3dd0967e2")]
    public sealed class RustOptionsPage : KubunoDialogPage
    {
        public RustOptionsPage()
            : base(legacyTypeFullName: "Kubuno.VisualStudio.Options.RustOptionsPage")
        {
        }

        [Category("rust-analyzer")]
        [DisplayName("Path override")]
        [Description("Full path to rust-analyzer.exe. When empty, Kubuno tries 'rustup which rust-analyzer', " +
            "then %USERPROFILE%\\.cargo\\bin\\rust-analyzer.exe, then the PATH environment variable.")]
        [Editor(typeof(FileNameEditor), typeof(System.Drawing.Design.UITypeEditor))]
        [UnifiedSetting("kubuno.rust.rustAnalyzer.pathOverride")]
        public string RustAnalyzerPathOverride { get; set; } = string.Empty;

        [Category("Editor")]
        [DisplayName("Format on save")]
        [Description("Run Format Document (rustfmt, via rust-analyzer) on every .rs file before it is saved. Off by default.")]
        [UnifiedSetting("kubuno.rust.editor.formatOnSave")]
        public bool FormatOnSave { get; set; }

        [Category("Diagnostics")]
        [DisplayName("LSP trace")]
        [Description("How much raw LSP traffic (requests/responses/notifications to and from rust-analyzer) to log " +
            "in the Kubuno Output pane. Off (default) logs only the startup line and errors; Messages adds one line " +
            "per request/response; Verbose adds full StreamJsonRpc detail including raw JSON payloads.")]
        [DefaultValue(LspTraceLevel.Off)]
        [UnifiedSetting("kubuno.rust.diagnostics.lspTrace")]
        public LspTraceLevel LspTrace { get; set; } = LspTraceLevel.Off;

        private const string HintsCategory = "Inlay hints";
        private const string ParameterHintsCategory = "Inlay hints: parameter names";
        private const string TypeHintsCategory = "Inlay hints: types";
        private const string OtherHintsCategory = "Inlay hints: other";

        /// <summary>Version of the hint settings this page stored (see <see cref="LoadSettingsFromStorage"/>).</summary>
        [Browsable(false)]
        [DefaultValue(0)]
        public int InlayHintsSettingsVersion { get; set; }

        [Category(HintsCategory)]
        [DisplayName("Show inline hints")]
        [Description("When rust-analyzer's inline hints appear in .rs files. While pressing Alt+F1 (default) is C#'s " +
            "\"Display inline hints only when pressing Alt+F1\"; Always keeps them on; Visual Studio setting follows " +
            "Text Editor > All Languages > Inlay Hints; Never hides them. Which hints exist is chosen below.")]
        [DefaultValue(RustInlayHintsMode.WhilePressingAltF1)]
        [UnifiedSetting("kubuno.rust.inlayHints.show")]
        public RustInlayHintsMode InlayHints { get; set; } = RustInlayHintsMode.WhilePressingAltF1;

        [Category(ParameterHintsCategory)]
        [DisplayName("Parameter name hints")]
        [Description("Show the parameter's name before each argument (\"title: name\"), like C#'s \"Display inline parameter name hints\". " +
            "rust-analyzer already leaves out the hint when the argument has the parameter's name or a single obvious parameter.")]
        [DefaultValue(true)]
        [UnifiedSetting("kubuno.rust.inlayHints.parameterNames.enabled")]
        public bool ParameterNameHints { get; set; } = true;

        [Category(ParameterHintsCategory)]
        [DisplayName("For literal arguments")]
        [Description("Keep the parameter name hint when the argument is a literal (5, \"text\", true, 'c'), like C#'s \"Show hints for literals\".")]
        [DefaultValue(true)]
        [UnifiedSetting("kubuno.rust.inlayHints.parameterNames.forLiterals")]
        public bool ParameterNameHintsForLiterals { get; set; } = true;

        [Category(ParameterHintsCategory)]
        [DisplayName("For other arguments")]
        [Description("Keep the parameter name hint when the argument is a variable, a call or any expression that is not a literal, " +
            "like C#'s \"Show hints for everything else\".")]
        [DefaultValue(true)]
        [UnifiedSetting("kubuno.rust.inlayHints.parameterNames.forOtherArguments")]
        public bool ParameterNameHintsForOtherArguments { get; set; } = true;

        [Category(TypeHintsCategory)]
        [DisplayName("Type hints")]
        [Description("Show the inferred type after a variable (\": Vec<u8>\"), like C#'s \"Display inline type hints\".")]
        [DefaultValue(true)]
        [UnifiedSetting("kubuno.rust.inlayHints.types.enabled")]
        public bool TypeHints { get; set; } = true;

        [Category(TypeHintsCategory)]
        [DisplayName("Hide when the type is apparent")]
        [Description("No type hint when the initializer names the type (let w = Widget::new()), like C#'s \"Suppress hints when type is apparent\".")]
        [DefaultValue(true)]
        [UnifiedSetting("kubuno.rust.inlayHints.types.hideApparent")]
        public bool HideApparentTypes { get; set; } = true;

        [Category(TypeHintsCategory)]
        [DisplayName("Hide for closures")]
        [Description("No type hint on a variable initialized with a closure (let f = |x| x + 1): the closure's own type is unreadable noise.")]
        [DefaultValue(true)]
        [UnifiedSetting("kubuno.rust.inlayHints.types.hideClosureVariables")]
        public bool HideClosureVariableTypes { get; set; } = true;

        [Category(TypeHintsCategory)]
        [DisplayName("Closure parameter types")]
        [Description("Show the type of a closure's parameters, like C#'s \"Show hints for lambda parameter types\".")]
        [DefaultValue(true)]
        [UnifiedSetting("kubuno.rust.inlayHints.types.closureParameters")]
        public bool ClosureParameterTypeHints { get; set; } = true;

        [Category(OtherHintsCategory)]
        [DisplayName("Method chain types")]
        [Description("Show the type at the end of each line of a method chain.")]
        [DefaultValue(true)]
        [UnifiedSetting("kubuno.rust.inlayHints.other.methodChains")]
        public bool ChainingHints { get; set; } = true;

        [Category(OtherHintsCategory)]
        [DisplayName("Closure return types")]
        [Description("Show a closure's return type: Never (default), only when its body is a block, or Always.")]
        [DefaultValue(RustClosureReturnTypeHints.Never)]
        [UnifiedSetting("kubuno.rust.inlayHints.other.closureReturnTypes")]
        public RustClosureReturnTypeHints ClosureReturnTypeHints { get; set; } = RustClosureReturnTypeHints.Never;

        [Category(OtherHintsCategory)]
        [DisplayName("Lifetime elision")]
        [Description("Show the lifetimes Rust infers in signatures (fn f<'a>(x: &'a T) -> &'a U): Never (default), skipping the trivial ones, or Always.")]
        [DefaultValue(RustLifetimeElisionHints.Never)]
        [UnifiedSetting("kubuno.rust.inlayHints.other.lifetimeElision")]
        public RustLifetimeElisionHints LifetimeElisionHints { get; set; } = RustLifetimeElisionHints.Never;

        [Category(OtherHintsCategory)]
        [DisplayName("Binding modes")]
        [Description("Show the implicit & / ref of patterns matched through a reference.")]
        [DefaultValue(false)]
        [UnifiedSetting("kubuno.rust.inlayHints.other.bindingModes")]
        public bool BindingModeHints { get; set; }

        [Category(OtherHintsCategory)]
        [DisplayName("Closing brace names")]
        [Description("Show the name of the item after the closing brace of a long block (\"// fn main\").")]
        [DefaultValue(false)]
        [UnifiedSetting("kubuno.rust.inlayHints.other.closingBraces")]
        public bool ClosingBraceHints { get; set; }

        [Category("Editor")]
        [DisplayName("CodeLens")]
        [Description("Show reference and implementation counts (\"3 references\") above Rust items, like C#'s CodeLens. " +
            "Click a count to open Find All References / the implementations.")]
        [DefaultValue(true)]
        [UnifiedSetting("kubuno.rust.editor.codeLens")]
        public bool CodeLens { get; set; } = true;

        /// <summary>Raised after the page's settings were applied (open editors refresh their inlay hints and code lenses).</summary>
        internal static event System.EventHandler? Applied;

        /// <summary>The per-kind hint choices as the settings rust-analyzer and the middle layer use.</summary>
        internal RustInlayHintSettings ToInlayHintSettings() => new RustInlayHintSettings
        {
            ParameterNames = ParameterNameHints,
            ParameterNamesForLiterals = ParameterNameHintsForLiterals,
            ParameterNamesForOtherArguments = ParameterNameHintsForOtherArguments,
            Types = TypeHints,
            HideObviousTypes = HideApparentTypes,
            HideClosureTypes = HideClosureVariableTypes,
            ClosureParameterTypes = ClosureParameterTypeHints,
            Chaining = ChainingHints,
            ClosureReturnTypes = ClosureReturnTypeHints,
            LifetimeElision = LifetimeElisionHints,
            BindingModes = BindingModeHints,
            ClosingBraces = ClosingBraceHints,
        };

        private bool _initialized;

        /// <summary>
        /// Loads the classic storage and moves an installation that predates the "Alt+F1 by default" hints onto the new
        /// default (see <see cref="RustInlayHintsMigration"/>).
        /// </summary>
        protected override void LoadLegacySettings()
        {
            base.LoadLegacySettings();
            var migrated = RustInlayHintsMigration.Migrate(InlayHintsSettingsVersion, InlayHints);
            bool changed = InlayHintsSettingsVersion != RustInlayHintsMigration.CurrentVersion;
            InlayHints = migrated;
            InlayHintsSettingsVersion = RustInlayHintsMigration.CurrentVersion;
            if (changed)
            {
                SaveLegacySettings();
            }
        }

        /// <summary>The settings were loaded or changed (classic dialog, unified settings page or JSON): publish the hint settings and tell the open editors and rust-analyzer.</summary>
        protected override void OnSettingsChanged()
        {
            RustInlayHintSettings.Current = ToInlayHintSettings();
            if (_initialized)
            {
                Applied?.Invoke(this, System.EventArgs.Empty);
            }

            _initialized = true;
        }

        protected override void OnApply(PageApplyEventArgs e)
        {
            base.OnApply(e);
            if (e.ApplyBehavior == ApplyKind.Apply)
            {
                RustInlayHintSettings.Current = ToInlayHintSettings();
                Applied?.Invoke(this, System.EventArgs.Empty);
            }
        }
    }
}
