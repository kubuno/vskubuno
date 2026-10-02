using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Kubuno.Shared.DevAssistant.Logic.Prompts
{
    /// <summary>The <c>#</c> reference kinds (docs/AI-ASSISTANT.md section 5.2); DA-1 resolves the first three.</summary>
    public static class ReferenceKinds
    {
        public const string File = "fichier";
        public const string Selection = "sélection";
        public const string Element = "élément";

        /// <summary>Accepted spellings (with or without accents, English aliases) → canonical kind.</summary>
        private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["fichier"] = File,
            ["file"] = File,
            ["selection"] = Selection,
            ["element"] = Element,
        };

        /// <summary>The canonical kind of <paramref name="word"/>, or null when it names no known reference.</summary>
        public static string? Canonical(string word) =>
            Aliases.TryGetValue(PromptParser.Fold(word), out var kind) ? kind : null;
    }

    /// <summary>A <c>#kind[:argument]</c> reference written in a prompt.</summary>
    public sealed class PromptReference
    {
        public PromptReference(string kind, string? argument)
        {
            Kind = kind;
            Argument = argument;
        }

        public string Kind { get; }

        /// <summary><c>#fichier:src/app.rs</c> → <c>src/app.rs</c>; null for the active document.</summary>
        public string? Argument { get; }

        public override string ToString() => "#" + Kind + (Argument is null ? string.Empty : ":" + Argument);
    }

    /// <summary>A prompt split into its command, its references and the free text.</summary>
    public sealed class ParsedPrompt
    {
        public ParsedPrompt(string? command, IReadOnlyList<PromptReference> references, string text)
        {
            Command = command;
            References = references;
            Text = text;
        }

        /// <summary>The canonical command name (<c>vue</c>, <c>expliquer</c>, <c>aide</c>...), or null for plain chat.</summary>
        public string? Command { get; }

        public IReadOnlyList<PromptReference> References { get; }

        /// <summary>The prompt without its leading command; reference tokens are kept in the text (they read naturally).</summary>
        public string Text { get; }
    }

    /// <summary>
    /// Parses what the developer typed: an optional leading <c>/command</c> (French names, English aliases, section 5.3)
    /// and any number of <c>#reference</c> tokens (section 5.2). Commands are known through the
    /// <paramref name="commandAliases"/> map the extension builds from its command providers.
    /// </summary>
    public static class PromptParser
    {
        private static readonly Regex ReferencePattern = new Regex(@"(?<![\w#])#(?<kind>[\p{L}]+)(?::(?<arg>""[^""]+""|[^\s,;]+))?", RegexOptions.CultureInvariant);

        public static ParsedPrompt Parse(string input, IReadOnlyDictionary<string, string> commandAliases)
        {
            var text = (input ?? string.Empty).Trim();
            string? command = null;
            if (text.StartsWith("/", StringComparison.Ordinal))
            {
                int end = 1;
                while (end < text.Length && !char.IsWhiteSpace(text[end]))
                {
                    end++;
                }

                var word = Fold(text.Substring(1, end - 1));
                if (commandAliases.TryGetValue(word, out var canonical))
                {
                    command = canonical;
                    text = text.Substring(end).TrimStart();
                }
            }

            var references = new List<PromptReference>();
            foreach (Match match in ReferencePattern.Matches(text))
            {
                var kind = ReferenceKinds.Canonical(match.Groups["kind"].Value);
                if (kind is null)
                {
                    continue;
                }

                var argument = match.Groups["arg"].Success ? match.Groups["arg"].Value.Trim('"') : null;
                if (!references.Any(r => r.Kind == kind && r.Argument == argument))
                {
                    references.Add(new PromptReference(kind, argument));
                }
            }

            return new ParsedPrompt(command, references, text);
        }

        /// <summary>Lower-case without diacritics: <c>Sélection</c> → <c>selection</c>.</summary>
        public static string Fold(string word)
        {
            var decomposed = (word ?? string.Empty).ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);
            foreach (var c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(c);
                }
            }

            return builder.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
