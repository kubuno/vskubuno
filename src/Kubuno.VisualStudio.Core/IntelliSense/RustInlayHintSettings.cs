using System;
using System.Text.Json.Nodes;

namespace Kubuno.VisualStudio.Core.IntelliSense
{
    /// <summary>When rust-analyzer shows closure return types (<c>inlayHints.closureReturnTypeHints.enable</c>).</summary>
    public enum RustClosureReturnTypeHints
    {
        Never,
        WithBlock,
        Always,
    }

    /// <summary>When rust-analyzer shows elided lifetimes (<c>inlayHints.lifetimeElisionHints.enable</c>).</summary>
    public enum RustLifetimeElisionHints
    {
        Never,
        SkipTrivial,
        Always,
    }

    /// <summary>
    /// The per-kind inlay hint choices of Tools &gt; Options &gt; Kubuno &gt; Rust (C#'s "Inline Parameter Name Hints" and
    /// "Inline Type Hints" groups), and their translation to rust-analyzer's <c>inlayHints.*</c> settings. The options
    /// page publishes a new snapshot in <see cref="Current"/> whenever it loads or applies.
    /// </summary>
    public sealed class RustInlayHintSettings
    {
        private static volatile RustInlayHintSettings _current = new RustInlayHintSettings();

        /// <summary>The settings in effect (C#-like defaults until the options page has been read).</summary>
        public static RustInlayHintSettings Current
        {
            get => _current;
            set => _current = value ?? new RustInlayHintSettings();
        }

        /// <summary><c>parameterHints.enable</c>: "name:" before each argument.</summary>
        public bool ParameterNames { get; set; } = true;

        /// <summary>Keep the parameter name hint when the argument is a literal (<c>5</c>, <c>"text"</c>, <c>true</c>). Client-side: rust-analyzer has no such switch.</summary>
        public bool ParameterNamesForLiterals { get; set; } = true;

        /// <summary>Keep the parameter name hint when the argument is anything but a literal. Client-side.</summary>
        public bool ParameterNamesForOtherArguments { get; set; } = true;

        /// <summary><c>typeHints.enable</c>: the inferred type after a <c>let</c> binding or a pattern.</summary>
        public bool Types { get; set; } = true;

        /// <summary><c>typeHints.hideNamedConstructor</c>: no type hint when the initializer names it (<c>let w = Widget::new()</c>), like C#'s "Suppress hints when type is apparent".</summary>
        public bool HideObviousTypes { get; set; } = true;

        /// <summary><c>typeHints.hideClosureInitialization</c>: no type hint on <c>let f = |x| ...</c>.</summary>
        public bool HideClosureTypes { get; set; } = true;

        /// <summary>Inverse of <c>typeHints.hideClosureParameter</c>: types of closure parameters, like C#'s lambda parameter types.</summary>
        public bool ClosureParameterTypes { get; set; } = true;

        /// <summary><c>chainingHints.enable</c>: the type at the end of each line of a method chain.</summary>
        public bool Chaining { get; set; } = true;

        /// <summary><c>closureReturnTypeHints.enable</c>.</summary>
        public RustClosureReturnTypeHints ClosureReturnTypes { get; set; } = RustClosureReturnTypeHints.Never;

        /// <summary><c>lifetimeElisionHints.enable</c>.</summary>
        public RustLifetimeElisionHints LifetimeElision { get; set; } = RustLifetimeElisionHints.Never;

        /// <summary><c>bindingModeHints.enable</c>: <c>&amp;</c> / <c>ref</c> hints in patterns.</summary>
        public bool BindingModes { get; set; }

        /// <summary><c>closingBraceHints.enable</c>: the item's name after a long block's closing brace.</summary>
        public bool ClosingBraces { get; set; }

        /// <summary>True when parameter hints must be looked at one by one (some argument kind is filtered out).</summary>
        public bool FiltersParameterNames => ParameterNames && !(ParameterNamesForLiterals && ParameterNamesForOtherArguments);

        /// <summary>rust-analyzer's <c>inlayHints</c> object for these settings.</summary>
        public JsonObject ToRustAnalyzerSettings() => new JsonObject
        {
            ["parameterHints"] = new JsonObject { ["enable"] = ParameterNames },
            ["typeHints"] = new JsonObject
            {
                ["enable"] = Types,
                ["hideNamedConstructor"] = HideObviousTypes,
                ["hideClosureInitialization"] = HideClosureTypes,
                ["hideClosureParameter"] = !ClosureParameterTypes,
            },
            ["chainingHints"] = new JsonObject { ["enable"] = Chaining },
            ["closureReturnTypeHints"] = new JsonObject
            {
                ["enable"] = ClosureReturnTypes == RustClosureReturnTypeHints.Always ? "always"
                    : ClosureReturnTypes == RustClosureReturnTypeHints.WithBlock ? "with_block" : "never",
            },
            ["lifetimeElisionHints"] = new JsonObject
            {
                ["enable"] = LifetimeElision == RustLifetimeElisionHints.Always ? "always"
                    : LifetimeElision == RustLifetimeElisionHints.SkipTrivial ? "skip_trivial" : "never",
            },
            ["bindingModeHints"] = new JsonObject { ["enable"] = BindingModes },
            ["closingBraceHints"] = new JsonObject { ["enable"] = ClosingBraces },
        };

        /// <summary>Whether a parameter name hint whose argument starts with <paramref name="argumentText"/> is kept.</summary>
        public bool KeepsParameterHint(string argumentText)
        {
            if (!ParameterNames)
            {
                return false;
            }

            return RustArgumentText.IsLiteral(argumentText) ? ParameterNamesForLiterals : ParameterNamesForOtherArguments;
        }
    }

    /// <summary>Classifies the text of a call argument (what follows a parameter name hint).</summary>
    public static class RustArgumentText
    {
        /// <summary>
        /// True when the argument (its text starts at <paramref name="text"/>) is a single literal token followed by
        /// <c>,</c>, <c>)</c> or the end of the text: a number, a string, a character, <c>true</c> or <c>false</c>.
        /// </summary>
        public static bool IsLiteral(string text)
        {
            if (text is null)
            {
                return false;
            }

            int i = 0;
            while (i < text.Length && char.IsWhiteSpace(text[i]))
            {
                i++;
            }

            if (i < text.Length && text[i] == '-')
            {
                i++;
                if (i >= text.Length || !char.IsDigit(text[i]))
                {
                    return false;
                }
            }

            int end = LiteralEnd(text, i);
            if (end < 0)
            {
                return false;
            }

            while (end < text.Length && char.IsWhiteSpace(text[end]))
            {
                end++;
            }

            return end >= text.Length || text[end] == ',' || text[end] == ')';
        }

        /// <summary>The index just after the literal token starting at <paramref name="i"/>, or -1 when none starts there.</summary>
        private static int LiteralEnd(string s, int i)
        {
            if (i >= s.Length)
            {
                return -1;
            }

            char c = s[i];
            if (char.IsDigit(c))
            {
                // Digits, separators, radix letters, exponent and type suffix; a '.' only continues a number when a digit follows
                // ("1.5"), so "3.max(2)" stops at "3" and is then rejected by the caller (a '.' follows).
                int j = i;
                while (j < s.Length && (char.IsLetterOrDigit(s[j]) || s[j] == '_' || (s[j] == '.' && j + 1 < s.Length && char.IsDigit(s[j + 1]))))
                {
                    j++;
                }

                return j;
            }

            if (c == '"' || c == '\'')
            {
                return QuotedEnd(s, i, c);
            }

            if (c == 'b' && i + 1 < s.Length && (s[i + 1] == '"' || s[i + 1] == '\''))
            {
                return QuotedEnd(s, i + 1, s[i + 1]);
            }

            if (c == 'r' || (c == 'b' && i + 1 < s.Length && s[i + 1] == 'r'))
            {
                int j = c == 'b' ? i + 2 : i + 1;
                int hashes = 0;
                while (j < s.Length && s[j] == '#')
                {
                    hashes++;
                    j++;
                }

                if (j < s.Length && s[j] == '"')
                {
                    string close = "\"" + new string('#', hashes);
                    int at = s.IndexOf(close, j + 1, StringComparison.Ordinal);
                    return at < 0 ? -1 : at + close.Length;
                }

                return -1;
            }

            foreach (var word in new[] { "true", "false" })
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) == 0
                    && (i + word.Length >= s.Length || !(char.IsLetterOrDigit(s[i + word.Length]) || s[i + word.Length] == '_')))
                {
                    return i + word.Length;
                }
            }

            return -1;
        }

        private static int QuotedEnd(string s, int start, char quote)
        {
            for (int j = start + 1; j < s.Length; j++)
            {
                if (s[j] == '\\')
                {
                    j++;
                }
                else if (s[j] == quote)
                {
                    return j + 1;
                }
            }

            return -1;
        }
    }
}

namespace Kubuno.VisualStudio.Core.IntelliSense
{
    /// <summary>When rust-analyzer's inlay hints are shown in .rs editors.</summary>
    public enum RustInlayHintsMode
    {
        /// <summary>Only while Alt+F1 is held, like C#'s "Display inline hints only when pressing Alt+F1" (the default).</summary>
        WhilePressingAltF1,
        Always,
        VisualStudioSetting,
        Never,
    }

    /// <summary>Moves the stored "Show inline hints" choice onto the current default once.</summary>
    public static class RustInlayHintsMigration
    {
        /// <summary>Version 1 = hints shown always by default (the first release of the option), 2 = while pressing Alt+F1.</summary>
        public const int CurrentVersion = 2;

        /// <summary>
        /// The mode to use given the stored one. Before version 2 the default (and the only value the options page
        /// stored for someone who never touched the choice) was <see cref="RustInlayHintsMode.Always"/>, so that one
        /// becomes the new default; any other stored mode was picked on purpose and stays.
        /// </summary>
        public static RustInlayHintsMode Migrate(int storedVersion, RustInlayHintsMode stored) =>
            storedVersion < CurrentVersion && stored == RustInlayHintsMode.Always ? RustInlayHintsMode.WhilePressingAltF1 : stored;
    }
}
