using System;
using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Media;
using Kubuno.Rust.Logic.IntelliSense;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace Kubuno.Rust.LanguageService.IntelliSense
{
    /// <summary>
    /// The Rust-only classifications rust-analyzer's semantic tokens are mapped onto as modifiers
    /// (<see cref="RustSemanticTokenMap"/>) - everything else uses C#'s own classifications, so Rust is colored
    /// like C# (types, traits = interfaces, enums, methods, locals, parameters, control keywords...). They are
    /// listed in Tools &gt; Options &gt; Environment &gt; Fonts and Colors as "Rust - ...".
    /// </summary>
    internal static class RustClassificationTypes
    {
        [Export]
        [Name(RustSemanticTokenMap.Macro)]
        internal static ClassificationTypeDefinition Macro = null!;

        [Export]
        [Name(RustSemanticTokenMap.Lifetime)]
        internal static ClassificationTypeDefinition Lifetime = null!;

        [Export]
        [Name(RustSemanticTokenMap.Mutable)]
        internal static ClassificationTypeDefinition Mutable = null!;

        [Export]
        [Name(RustSemanticTokenMap.Unsafe)]
        internal static ClassificationTypeDefinition Unsafe = null!;

        /// <summary>The macro color: Visual Studio's C++ macro purple, for the current theme.</summary>
        internal static Color MacroColor(bool dark) => dark ? Color.FromRgb(0xBE, 0xB7, 0xFF) : Color.FromRgb(0x8A, 0x1B, 0xFF);

        internal static bool IsDarkTheme() => Kubuno.Shared.UI.VsTheme.IsDark();
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = RustSemanticTokenMap.Macro)]
    [Name(RustSemanticTokenMap.Macro)]
    [UserVisible(true)]
    [Order(After = Priority.High)]
    internal sealed class RustMacroFormat : ClassificationFormatDefinition
    {
        public RustMacroFormat()
        {
            DisplayName = "Rust - Macro";
            ForegroundColor = RustClassificationTypes.MacroColor(RustClassificationTypes.IsDarkTheme());
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = RustSemanticTokenMap.Lifetime)]
    [Name(RustSemanticTokenMap.Lifetime)]
    [UserVisible(true)]
    [Order(After = Priority.High)]
    internal sealed class RustLifetimeFormat : ClassificationFormatDefinition
    {
        public RustLifetimeFormat()
        {
            DisplayName = "Rust - Lifetime";
            IsItalic = true;
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = RustSemanticTokenMap.Mutable)]
    [Name(RustSemanticTokenMap.Mutable)]
    [UserVisible(true)]
    [Order(After = Priority.High)]
    internal sealed class RustMutableFormat : ClassificationFormatDefinition
    {
        public RustMutableFormat()
        {
            DisplayName = "Rust - Mutable variable";
            TextDecorations = System.Windows.TextDecorations.Underline;
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = RustSemanticTokenMap.Unsafe)]
    [Name(RustSemanticTokenMap.Unsafe)]
    [UserVisible(true)]
    [Order(After = Priority.High)]
    internal sealed class RustUnsafeFormat : ClassificationFormatDefinition
    {
        public RustUnsafeFormat()
        {
            DisplayName = "Rust - Unsafe operation";
            IsBold = true;
        }
    }

    /// <summary>
    /// Keeps the macro color right for the theme: a format definition's color is read once, so a theme switch
    /// (dark ⇄ light) would leave the other theme's purple. The user's own Fonts and Colors choice is left alone.
    /// </summary>
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType(Constants.RustContentType)]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class RustClassificationThemeListener : IWpfTextViewCreationListener
    {
        private static bool _subscribed;

        [Import]
        internal IClassificationFormatMapService FormatMapService { get; set; } = null!;

        [Import]
        internal IClassificationTypeRegistryService Registry { get; set; } = null!;

        public void TextViewCreated(IWpfTextView textView)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_subscribed)
            {
                return;
            }

            _subscribed = true;
            VSColorTheme.ThemeChanged += _ => Apply();
        }

        private void Apply()
        {
            var type = Registry.GetClassificationType(RustSemanticTokenMap.Macro);
            if (type is null)
            {
                return;
            }

            var map = FormatMapService.GetClassificationFormatMap("text");
            var dark = RustClassificationTypes.IsDarkTheme();
            var current = map.GetExplicitTextProperties(type);
            var brush = current.ForegroundBrush as SolidColorBrush;
            if (brush is null || brush.Color == RustClassificationTypes.MacroColor(!dark))
            {
                map.SetExplicitTextProperties(type, current.SetForeground(RustClassificationTypes.MacroColor(dark)));
            }
        }
    }
}
