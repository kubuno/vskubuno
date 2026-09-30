using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Kubuno.VisualStudio.Core.QuickInfo
{
    /// <summary>What a rust-analyzer hover is about (decides the icon and how the signature is shortened).</summary>
    public enum RustItemKind
    {
        Other,
        Function,
        Method,
        Struct,
        Enum,
        Union,
        Trait,
        TypeAlias,
        Const,
        Static,
        Module,
        Macro,
        Field,
        Variant,
        Local,
        Parameter,
        TypeParameter,
        Lifetime,
        Keyword,
        Crate,
    }

    internal enum RustTokenType
    {
        Whitespace,
        Ident,
        Lifetime,
        Number,
        String,
        Comment,
        Punct,
    }

    internal readonly struct RustToken
    {
        public RustToken(RustTokenType type, string text)
        {
            Type = type;
            Text = text;
        }

        public RustTokenType Type { get; }

        public string Text { get; }

        public override string ToString() => Type + ":" + Text;
    }

    /// <summary>
    /// A small Rust lexer + classifier for tooltips: turns a signature (or a line of a doc example) into
    /// <see cref="QuickInfoRun"/>s colored like Roslyn colors C# (keywords, types, traits, functions,
    /// parameters, generics, lifetimes...). It works on the text alone - no semantic model - so it relies
    /// on Rust's conventions (UpperCamelCase types, <c>snake_case</c> values, the item keyword before a name).
    /// </summary>
    public static class RustSyntax
    {
        private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "as", "async", "await", "break", "const", "continue", "crate", "dyn", "else", "enum", "extern", "false", "fn", "for",
            "if", "impl", "in", "let", "loop", "match", "mod", "move", "mut", "pub", "ref", "return", "self", "Self", "static",
            "struct", "super", "trait", "true", "type", "union", "unsafe", "use", "where", "while", "yield", "macro_rules", "macro",
            "bool", "char", "str", "u8", "u16", "u32", "u64", "u128", "usize", "i8", "i16", "i32", "i64", "i128", "isize", "f16",
            "f32", "f64", "f128",
        };

        private static readonly string[] MultiCharPunct = { "::", "->", "=>", "..=", "...", "..", "==", "!=", "<=", ">=", "&&", "||", "+=", "-=", "*=", "/=" };

        internal static List<RustToken> Lex(string text)
        {
            var tokens = new List<RustToken>();
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                int start = i;
                if (char.IsWhiteSpace(c))
                {
                    while (i < text.Length && char.IsWhiteSpace(text[i]))
                    {
                        i++;
                    }

                    tokens.Add(new RustToken(RustTokenType.Whitespace, text.Substring(start, i - start)));
                }
                else if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    i = text.IndexOf('\n', i);
                    i = i < 0 ? text.Length : i;
                    tokens.Add(new RustToken(RustTokenType.Comment, text.Substring(start, i - start)));
                }
                else if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = end < 0 ? text.Length : end + 2;
                    tokens.Add(new RustToken(RustTokenType.Comment, text.Substring(start, i - start)));
                }
                else if (c == '"' || (c == 'b' && i + 1 < text.Length && text[i + 1] == '"'))
                {
                    i = c == 'b' ? i + 2 : i + 1;
                    while (i < text.Length && text[i] != '"')
                    {
                        i += text[i] == '\\' ? 2 : 1;
                    }

                    i = Math.Min(i + 1, text.Length);
                    tokens.Add(new RustToken(RustTokenType.String, text.Substring(start, i - start)));
                }
                else if ((c == 'r' || c == 'b') && IsRawStringStart(text, i, out int hashes, out int quote))
                {
                    var terminator = "\"" + new string('#', hashes);
                    int end = text.IndexOf(terminator, quote + 1, StringComparison.Ordinal);
                    i = end < 0 ? text.Length : end + terminator.Length;
                    tokens.Add(new RustToken(RustTokenType.String, text.Substring(start, i - start)));
                }
                else if (c == '\'')
                {
                    // A char literal ('a', '\n', '\u{1F600}') or a lifetime ('a, 'static).
                    if (i + 2 < text.Length && text[i + 1] == '\\')
                    {
                        int end = text.IndexOf('\'', i + 2);
                        i = end < 0 ? text.Length : end + 1;
                        tokens.Add(new RustToken(RustTokenType.String, text.Substring(start, i - start)));
                    }
                    else if (i + 2 < text.Length && text[i + 2] == '\'')
                    {
                        i += 3;
                        tokens.Add(new RustToken(RustTokenType.String, text.Substring(start, i - start)));
                    }
                    else
                    {
                        i++;
                        while (i < text.Length && IsIdentChar(text[i]))
                        {
                            i++;
                        }

                        tokens.Add(new RustToken(RustTokenType.Lifetime, text.Substring(start, i - start)));
                    }
                }
                else if (char.IsDigit(c))
                {
                    while (i < text.Length && (IsIdentChar(text[i]) || (text[i] == '.' && i + 1 < text.Length && char.IsDigit(text[i + 1]))))
                    {
                        i++;
                    }

                    tokens.Add(new RustToken(RustTokenType.Number, text.Substring(start, i - start)));
                }
                else if (IsIdentStart(c))
                {
                    if (c == 'r' && i + 1 < text.Length && text[i + 1] == '#')
                    {
                        i += 2;
                    }

                    while (i < text.Length && IsIdentChar(text[i]))
                    {
                        i++;
                    }

                    tokens.Add(new RustToken(RustTokenType.Ident, text.Substring(start, i - start)));
                }
                else
                {
                    var multi = MultiCharPunct.FirstOrDefault(p => string.CompareOrdinal(text, i, p, 0, p.Length) == 0);
                    i += multi?.Length ?? 1;
                    tokens.Add(new RustToken(RustTokenType.Punct, text.Substring(start, i - start)));
                }
            }

            return tokens;
        }

        private static bool IsRawStringStart(string text, int i, out int hashes, out int quote)
        {
            hashes = 0;
            quote = -1;
            int j = i;
            if (text[j] == 'b')
            {
                j++;
            }

            if (j >= text.Length || text[j] != 'r')
            {
                return false;
            }

            j++;
            while (j < text.Length && text[j] == '#')
            {
                hashes++;
                j++;
            }

            if (j < text.Length && text[j] == '"')
            {
                quote = j;
                return true;
            }

            return false;
        }

        private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';

        private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        private static bool IsUpper(string ident)
        {
            var name = ident.StartsWith("r#", StringComparison.Ordinal) ? ident.Substring(2) : ident.TrimStart('_');
            return name.Length > 0 && char.IsUpper(name[0]);
        }

        /// <summary>
        /// Classifies a signature. <paramref name="kind"/> is what the hover is about: it colors the declared
        /// name (a field, a variant, a local...) the way Roslyn colors the symbol a C# tooltip describes.
        /// </summary>
        public static List<QuickInfoRun> ClassifySignature(string signature, RustItemKind kind) =>
            Classify(Lex(signature ?? string.Empty), kind, isCode: false);

        /// <summary>Classifies a line of Rust code (a doc example), in code font.</summary>
        public static List<QuickInfoRun> ClassifyCode(string line) =>
            Classify(Lex(line ?? string.Empty), RustItemKind.Other, isCode: true);

        private static List<QuickInfoRun> Classify(List<RustToken> tokens, RustItemKind kind, bool isCode)
        {
            var style = isCode ? QuickInfoRunStyle.Code : QuickInfoRunStyle.None;
            var builder = new RunBuilder();
            var generics = new HashSet<string>(StringComparer.Ordinal);
            int parenDepth = 0;
            int angleDepth = 0;
            bool inWhere = false;
            bool boundContext = false;
            bool expectGenericsAfterName = false;
            int genericsDepth = -1;
            bool firstIdentSeen = false;

            for (int i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                switch (token.Type)
                {
                    case RustTokenType.Whitespace:
                        builder.Add(QuickInfoTextKind.Text, isCode ? token.Text : " ", style);
                        continue;
                    case RustTokenType.Comment:
                        builder.Add(QuickInfoTextKind.Comment, token.Text, style);
                        continue;
                    case RustTokenType.String:
                        builder.Add(QuickInfoTextKind.String, token.Text, style);
                        continue;
                    case RustTokenType.Number:
                        builder.Add(QuickInfoTextKind.Number, token.Text, style);
                        continue;
                    case RustTokenType.Lifetime:
                        builder.Add(QuickInfoTextKind.TypeParameter, token.Text, style);
                        continue;
                    case RustTokenType.Punct:
                        switch (token.Text)
                        {
                            case "(":
                                parenDepth++;
                                boundContext = false;
                                break;
                            case ")":
                                parenDepth = Math.Max(0, parenDepth - 1);
                                break;
                            case "<":
                                angleDepth++;
                                if (expectGenericsAfterName && genericsDepth < 0)
                                {
                                    genericsDepth = angleDepth;
                                }

                                break;
                            case ">":
                                if (angleDepth == genericsDepth)
                                {
                                    genericsDepth = -1;
                                    expectGenericsAfterName = false;
                                }

                                angleDepth = Math.Max(0, angleDepth - 1);
                                boundContext = false;
                                break;
                            case ":":
                                boundContext = (angleDepth > 0 && parenDepth == 0) || inWhere;
                                break;
                            case ",":
                            case "=":
                            case "{":
                            case ";":
                            case "->":
                                boundContext = false;
                                break;
                        }

                        if (token.Text != "<")
                        {
                            expectGenericsAfterName = expectGenericsAfterName && genericsDepth >= 0;
                        }

                        builder.Add(IsOperator(token.Text) ? QuickInfoTextKind.Operator : QuickInfoTextKind.Punctuation, token.Text, style);
                        continue;
                }

                // Identifiers.
                var text = token.Text;
                var previous = PreviousSignificant(tokens, i);
                var next = NextSignificant(tokens, i);
                QuickInfoTextKind textKind;

                if (genericsDepth > 0 && angleDepth == genericsDepth && (previous == "<" || previous == "," || previous == "const") && !Keywords.Contains(text))
                {
                    generics.Add(text);
                    textKind = QuickInfoTextKind.TypeParameter;
                }
                else if (Keywords.Contains(text) && !(text == "union" && next != null && !IsIdentifier(next)))
                {
                    textKind = QuickInfoTextKind.Keyword;
                    if (text == "where")
                    {
                        inWhere = true;
                    }

                    if (text == "impl" || text == "dyn")
                    {
                        boundContext = true;
                        expectGenericsAfterName = text == "impl";
                    }
                }
                else if (next == "!" || (previous == "!" && PreviousSignificant(tokens, IndexOfPreviousSignificant(tokens, i)) == "macro_rules"))
                {
                    textKind = QuickInfoTextKind.Macro;
                }
                else if (previous == "fn")
                {
                    textKind = QuickInfoTextKind.Method;
                    expectGenericsAfterName = true;
                }
                else if (previous == "struct" || previous == "union")
                {
                    textKind = QuickInfoTextKind.Struct;
                    expectGenericsAfterName = true;
                }
                else if (previous == "enum")
                {
                    textKind = QuickInfoTextKind.Enum;
                    expectGenericsAfterName = true;
                }
                else if (previous == "trait")
                {
                    textKind = QuickInfoTextKind.Trait;
                    expectGenericsAfterName = true;
                }
                else if (previous == "type" && angleDepth == 0)
                {
                    textKind = QuickInfoTextKind.Class;
                    expectGenericsAfterName = true;
                }
                else if (previous == "mod" || previous == "crate" && PreviousSignificant(tokens, IndexOfPreviousSignificant(tokens, i)) == "extern")
                {
                    textKind = QuickInfoTextKind.Namespace;
                }
                else if ((previous == "const" || previous == "static") && angleDepth == 0)
                {
                    textKind = QuickInfoTextKind.Constant;
                }
                else if (previous == "let" || (previous == "mut" && PreviousSignificant(tokens, IndexOfPreviousSignificant(tokens, i)) == "let"))
                {
                    textKind = QuickInfoTextKind.Local;
                }
                else if (next == "::")
                {
                    textKind = IsUpper(text) ? QuickInfoTextKind.Class : QuickInfoTextKind.Namespace;
                }
                else if (generics.Contains(text))
                {
                    textKind = QuickInfoTextKind.TypeParameter;
                }
                else if (!firstIdentSeen && !isCode && kind == RustItemKind.Variant)
                {
                    textKind = QuickInfoTextKind.EnumMember;
                }
                else if (!firstIdentSeen && !isCode && kind == RustItemKind.TypeParameter)
                {
                    textKind = QuickInfoTextKind.TypeParameter;
                }
                else if (next == ":" && !isCode)
                {
                    textKind = parenDepth > 0 ? QuickInfoTextKind.Parameter
                        : kind == RustItemKind.Field ? QuickInfoTextKind.Field
                        : kind == RustItemKind.Local ? QuickInfoTextKind.Local
                        : kind == RustItemKind.Parameter ? QuickInfoTextKind.Parameter
                        : IsUpper(text) ? QuickInfoTextKind.TypeParameter
                        : QuickInfoTextKind.Field;
                }
                else if (IsUpper(text))
                {
                    textKind = boundContext ? QuickInfoTextKind.Trait : QuickInfoTextKind.Class;
                }
                else if (next == "(")
                {
                    textKind = QuickInfoTextKind.Method;
                }
                else
                {
                    textKind = QuickInfoTextKind.Identifier;
                }

                if (!Keywords.Contains(text) || text == "Self" || text == "self")
                {
                    firstIdentSeen = true;
                }

                builder.Add(textKind, text, style);
            }

            return builder.Build();
        }

        private static bool IsIdentifier(string text) => text.Length > 0 && IsIdentStart(text[0]);

        private static bool IsOperator(string punct)
        {
            switch (punct)
            {
                case "->":
                case "=>":
                case "=":
                case "&":
                case "*":
                case "+":
                case "-":
                case "/":
                case "%":
                case "!":
                case "?":
                case "|":
                case "^":
                case "==":
                case "!=":
                case "<=":
                case ">=":
                case "&&":
                case "||":
                case "..":
                case "..=":
                case "+=":
                case "-=":
                case "*=":
                case "/=":
                    return true;
                default:
                    return false;
            }
        }

        private static int IndexOfPreviousSignificant(List<RustToken> tokens, int index)
        {
            for (int j = index - 1; j >= 0; j--)
            {
                if (tokens[j].Type != RustTokenType.Whitespace && tokens[j].Type != RustTokenType.Comment)
                {
                    return j;
                }
            }

            return -1;
        }

        private static string? PreviousSignificant(List<RustToken> tokens, int index)
        {
            int j = index < 0 ? -1 : IndexOfPreviousSignificant(tokens, index);
            return j >= 0 ? tokens[j].Text : null;
        }

        private static string? NextSignificant(List<RustToken> tokens, int index)
        {
            for (int j = index + 1; j < tokens.Count; j++)
            {
                if (tokens[j].Type != RustTokenType.Whitespace && tokens[j].Type != RustTokenType.Comment)
                {
                    return tokens[j].Text;
                }
            }

            return null;
        }

        private static readonly Regex Visibility = new Regex(@"^pub\b\s*(\([^)]*\))?\s*", RegexOptions.CultureInvariant);
        // Only qualifiers of a fn/trait/impl (`const fn`, `unsafe trait`, `extern "C" fn`): `const X: T` is a constant.
        private static readonly Regex Qualifiers = new Regex(@"^(?:(?:const|async|unsafe|default|safe|extern(?:\s+""[^""]*"")?)\s+)+(?=(?:fn|trait|impl)\b)", RegexOptions.CultureInvariant);

        /// <summary>
        /// Shortens the signature code block of a rust-analyzer hover to the one line a Visual Studio tooltip
        /// shows (<c>pub struct Point</c>, <c>pub fn insert(&amp;mut self, k: K, v: V) -&gt; Option&lt;V&gt; where K: Eq + Hash</c>):
        /// keeps the item's own line (not the <c>impl</c>/<c>trait</c> line rust-analyzer puts before a
        /// method), joins a signature split over several lines, drops comments, a type's body and a type's
        /// <c>where</c> clause.
        /// </summary>
        /// <param name="code">The signature code block.</param>
        /// <param name="container">The containing path rust-analyzer shows above it (<c>crate::module::Type</c>), or null.</param>
        /// <param name="kind">What the signature declares.</param>
        public static string ToOneLine(string code, string? container, out RustItemKind kind)
        {
            var lines = (code ?? string.Empty).Replace("\r\n", "\n").Split('\n');
            int header = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Length == 0 || char.IsWhiteSpace(line[0]))
                {
                    continue;
                }

                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("}", StringComparison.Ordinal) || trimmed.StartsWith(")", StringComparison.Ordinal)
                    || trimmed.StartsWith("]", StringComparison.Ordinal) || trimmed.StartsWith(">", StringComparison.Ordinal)
                    || trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("//", StringComparison.Ordinal)
                    || trimmed.StartsWith("#[", StringComparison.Ordinal) || Regex.IsMatch(trimmed, @"^where\b"))
                {
                    continue;
                }

                header = i;
            }

            // Tokens of the item, comments dropped, whitespace collapsed.
            var tokens = Lex(string.Join("\n", WithoutInheritedBounds(lines.Skip(header).ToList())))
                .Where(t => t.Type != RustTokenType.Comment)
                .ToList();

            var headerText = lines.Length > header ? lines[header].Trim() : string.Empty;
            kind = DetectKind(headerText, container);

            bool isType = kind == RustItemKind.Struct || kind == RustItemKind.Enum || kind == RustItemKind.Union || kind == RustItemKind.Trait;
            var result = new StringBuilder();
            int depth = 0;
            bool pendingSpace = false;
            foreach (var token in tokens)
            {
                if (token.Type == RustTokenType.Whitespace)
                {
                    pendingSpace = true;
                    continue;
                }

                if (depth == 0 && isType && (token.Text == "{" || token.Text == "where" || token.Text == ";"))
                {
                    break;
                }

                if (token.Text == "(" || token.Text == "[" || token.Text == "{" || token.Text == "<")
                {
                    depth++;
                }
                else if ((token.Text == ")" || token.Text == "]" || token.Text == "}" || token.Text == ">") && depth > 0)
                {
                    depth--;
                }

                // A trailing comma before a closing bracket is layout only.
                if (token.Text == ")" || token.Text == "]" || token.Text == "}" || token.Text == ">")
                {
                    TrimTrailingComma(result);
                }

                bool noSpaceBefore = token.Text == ")" || token.Text == "]" || token.Text == "," || token.Text == ";"
                    || (token.Text == ">" && depth >= 0 && result.Length > 0 && !result.ToString().EndsWith(" -", StringComparison.Ordinal))
                    || token.Text == "::";
                bool afterOpen = result.Length > 0 && (result[result.Length - 1] == '(' || result[result.Length - 1] == '[' || result[result.Length - 1] == '<'
                    || result.ToString().EndsWith("::", StringComparison.Ordinal) || result[result.Length - 1] == '&');
                if (pendingSpace && result.Length > 0 && !noSpaceBefore && !afterOpen)
                {
                    result.Append(' ');
                }

                pendingSpace = false;
                result.Append(token.Text);
            }

            TrimTrailingComma(result);
            var oneLine = result.ToString().Trim();
            oneLine = Regex.Replace(oneLine, @"\s+", " ");
            return oneLine;
        }

        /// <summary>
        /// Removes the bounds rust-analyzer repeats from the enclosing <c>impl</c>/<c>trait</c> (the lines after
        /// a <c>// Bounds from impl:</c> comment): a C# tooltip does not repeat the containing type's
        /// constraints either. A <c>where</c> left without bounds goes too.
        /// </summary>
        private static List<string> WithoutInheritedBounds(List<string> lines)
        {
            var kept = new List<string>();
            bool skipping = false;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("//", StringComparison.Ordinal))
                {
                    skipping = trimmed.IndexOf("Bounds from", StringComparison.OrdinalIgnoreCase) >= 0;
                    continue;
                }

                if (skipping && line.Length > 0 && char.IsWhiteSpace(line[0]))
                {
                    continue;
                }

                skipping = false;
                kept.Add(line);
            }

            int where = kept.FindIndex(l => l.Trim() == "where");
            if (where >= 0 && (where + 1 >= kept.Count || !(kept[where + 1].Length > 0 && char.IsWhiteSpace(kept[where + 1][0]))))
            {
                kept.RemoveAt(where);
            }

            return kept;
        }

        private static void TrimTrailingComma(StringBuilder text)
        {
            while (text.Length > 0 && text[text.Length - 1] == ' ')
            {
                text.Length--;
            }

            if (text.Length > 0 && text[text.Length - 1] == ',')
            {
                text.Length--;
            }
        }

        /// <summary>The kind of item a signature line declares.</summary>
        public static RustItemKind DetectKind(string header, string? container)
        {
            bool hasContainer = !string.IsNullOrEmpty(container);
            bool containerIsType = hasContainer && IsUpper(container!.Split(new[] { "::" }, StringSplitOptions.None).Last());
            var text = (header ?? string.Empty).Trim();
            var afterVisibility = Visibility.Replace(text, string.Empty, 1);
            var afterQualifiers = Qualifiers.Replace(afterVisibility, string.Empty, 1);
            var word = Regex.Match(afterQualifiers, @"^(r#)?[A-Za-z_][A-Za-z0-9_]*").Value;
            var rest = afterQualifiers.Substring(word.Length).TrimStart();
            if (word.Length > 0 && rest.Length == 0 && afterQualifiers == text && (Keywords.Contains(word) || word == "default"))
            {
                // A hover on a keyword itself (rust-analyzer shows the keyword's documentation).
                return RustItemKind.Keyword;
            }

            switch (word)
            {
                case "fn":
                    return Regex.IsMatch(rest, @"\(\s*(&\s*('\w+\s+)?)?(mut\s+)?self\b") || containerIsType ? RustItemKind.Method : RustItemKind.Function;
                case "struct": return RustItemKind.Struct;
                case "enum": return RustItemKind.Enum;
                case "union" when rest.Length > 0 && IsIdentStart(rest[0]): return RustItemKind.Union;
                case "trait": return RustItemKind.Trait;
                case "type" when rest.Length > 0: return RustItemKind.TypeAlias;
                case "const" when rest.Length > 0: return RustItemKind.Const;
                case "static" when rest.Length > 0: return RustItemKind.Static;
                case "mod" when rest.Length > 0: return RustItemKind.Module;
                case "macro_rules":
                case "macro":
                    return RustItemKind.Macro;
                case "let": return RustItemKind.Local;
                case "extern" when rest.StartsWith("crate", StringComparison.Ordinal): return RustItemKind.Crate;
                case "crate" when rest.Length > 0 && IsIdentStart(rest[0]): return RustItemKind.Crate;
            }

            if (text.StartsWith("'", StringComparison.Ordinal))
            {
                return RustItemKind.Lifetime;
            }

            if (word.Length > 0 && (Keywords.Contains(word) || word == "union" || word == "default") && rest.Length == 0)
            {
                return RustItemKind.Keyword;
            }

            if (word.Length > 0 && rest.StartsWith(":", StringComparison.Ordinal) && !rest.StartsWith("::", StringComparison.Ordinal))
            {
                if (IsUpper(word))
                {
                    return RustItemKind.TypeParameter;
                }

                return hasContainer ? RustItemKind.Field : RustItemKind.Parameter;
            }

            if (word.Length > 0 && IsUpper(word) && hasContainer && (rest.Length == 0 || rest[0] == '(' || rest[0] == '{' || rest[0] == '='))
            {
                return RustItemKind.Variant;
            }

            return RustItemKind.Other;
        }
    }
}
