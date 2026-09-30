using System;
using System.ComponentModel.Composition;
using System.Reflection;
using Kubuno.VisualStudio.Core.IntelliSense;
using Kubuno.VisualStudio.Options;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace Kubuno.VisualStudio.LanguageService.IntelliSense
{
    /// <summary>
    /// Shows rust-analyzer's inlay hints in .rs editors according to Tools &gt; Options &gt; Kubuno &gt; Rust &gt; Inlay hints.
    /// Visual Studio's LSP client draws them (types, parameter names, chains) only when the editor's global
    /// "inlay hints" option allows it - off by default - so the option is set on each Rust view (Kubuno's default: "While pressing
    /// Alt+F1", Visual Studio's own hold-to-show mode, the one C# offers).
    /// </summary>
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType(Constants.RustContentType)]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class RustInlayHintsOption : IWpfTextViewCreationListener
    {
        /// <summary>The editor option Visual Studio's LSP client reads (<c>DefaultTextViewOptions.EnableInlayHintsOptionId</c>, not public in the SDK Kubuno builds against).</summary>
        private static readonly string OptionName = ReadOptionName() ?? "EnableInlayHints";

        /// <summary>The option's value type (<c>Microsoft.VisualStudio.Text.Editor.InlayHintsEnableKind</c>, internal in that SDK), or null.</summary>
        private static readonly Type? InlayHintsEnableKind = typeof(DefaultTextViewOptions).Assembly.GetType("Microsoft.VisualStudio.Text.Editor.InlayHintsEnableKind", throwOnError: false);

        [Import]
        internal Microsoft.VisualStudio.Text.ITextDocumentFactoryService TextDocumentFactory { get; set; } = null!;

        public void TextViewCreated(IWpfTextView textView)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (TextDocumentFactory.TryGetTextDocument(textView.TextBuffer, out var document))
            {
                RustArgumentSnapshots.Register(document.FilePath, textView.TextBuffer);
            }

            Apply(textView);
            EventHandler onApplied = (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                Apply(textView);
            };
            RustOptionsPage.Applied += onApplied;
            textView.Closed += (_, _) => RustOptionsPage.Applied -= onApplied;
        }

        private static string? ReadOptionName()
        {
            var member = (object?)typeof(DefaultTextViewOptions).GetField("EnableInlayHintsOptionId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
                ?? typeof(DefaultTextViewOptions).GetProperty("EnableInlayHintsOptionId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
            return member?.GetType().GetProperty("Name")?.GetValue(member) as string;
        }

        internal static RustOptionsPage? Options()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return KubunoPackage.Instance?.GetDialogPage(typeof(RustOptionsPage)) as RustOptionsPage;
        }

        private static void Apply(ITextView view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var mode = Options()?.InlayHints ?? RustInlayHintsMode.WhilePressingAltF1;
            if (!view.Options.IsOptionDefined(OptionName, false) || InlayHintsEnableKind is null)
            {
                return;
            }

            if (mode == RustInlayHintsMode.VisualStudioSetting)
            {
                view.Options.ClearOptionValue(OptionName);
                return;
            }

            // InlayHintsEnableKind: 0 = always, 1 = never, 2 = while pressing Alt+F1.
            int value = mode == RustInlayHintsMode.Never ? 1 : mode == RustInlayHintsMode.WhilePressingAltF1 ? 2 : 0;
            view.Options.SetOptionValue(OptionName, Enum.ToObject(InlayHintsEnableKind, value));
        }
    }
}
