using System;
using System.Collections.Generic;
using System.Text;

namespace Kubuno.VisualStudio.Core
{
    /// <summary>
    /// Finds the fully-qualified name of the <c>#[test]</c> function enclosing (or nearest
    /// after) a given line in a Rust source file, for the "Debug Rust test at cursor" command:
    /// the qualified name is exactly the filter <c>cargo test</c>'s harness expects on its
    /// command line (e.g. <c>tests::greet_includes_the_name</c> for a test nested in
    /// <c>mod tests { ... }</c>, or just <c>greet_includes_the_name</c> for one at the file's
    /// top level, as in a `tests/*.rs` integration test). Pure text scanning - no VS/editor
    /// dependency - so it is unit-testable with plain strings.
    /// </summary>
    public static class RustTestLocator
    {
        /// <param name="documentText">The full text of the `.rs` file.</param>
        /// <param name="caretLine">0-based line the caret is on.</param>
        /// <returns>
        /// The qualified test name (module path joined with "::", then the function name), or
        /// null when no <c>#[test]</c> function contains or immediately follows
        /// <paramref name="caretLine"/>.
        /// </returns>
        public static string? FindEnclosingTestName(string documentText, int caretLine)
        {
            if (documentText is null)
            {
                throw new ArgumentNullException(nameof(documentText));
            }

            if (caretLine < 0)
            {
                throw new ArgumentException("Caret line must not be negative.", nameof(caretLine));
            }

            var tokens = Tokenize(documentText);
            var functions = FindFunctions(tokens);

            // Prefer whichever function's body contains the caret line (test or not - if it's
            // not a test, the caret is simply not "in" a test, and we must not guess at a
            // different, unrelated test elsewhere in the file). Only when the caret sits outside
            // every function (e.g. on the "#[test]"/"fn" line itself, or a blank line just above)
            // do we fall back to the nearest test function starting on or after the caret.
            RustFunction? containing = null;
            RustFunction? nextTestAfter = null;
            foreach (var function in functions)
            {
                if (caretLine >= function.StartLine && caretLine <= function.EndLine)
                {
                    containing = function;
                    break;
                }

                if (function.IsTest && function.StartLine >= caretLine &&
                    (nextTestAfter is null || function.StartLine < nextTestAfter.Value.StartLine))
                {
                    nextTestAfter = function;
                }
            }

            if (containing is not null)
            {
                return containing.Value.IsTest ? containing.Value.QualifiedName : null;
            }

            return nextTestAfter?.QualifiedName;
        }

        private readonly struct RustFunction
        {
            public RustFunction(string qualifiedName, bool isTest, int startLine, int endLine)
            {
                QualifiedName = qualifiedName;
                IsTest = isTest;
                StartLine = startLine;
                EndLine = endLine;
            }

            public string QualifiedName { get; }

            public bool IsTest { get; }

            public int StartLine { get; }

            public int EndLine { get; }
        }

        private static List<RustFunction> FindFunctions(IReadOnlyList<Token> tokens)
        {
            var results = new List<RustFunction>();

            // A stack of "mod <name> { ... }" scopes still open at the current brace depth,
            // each remembering the depth its own opening brace was pushed at so it can be
            // popped when that brace closes.
            var modStack = new List<(string Name, int OpenDepth)>();
            var depth = 0;
            var pendingTestAttribute = false;

            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                switch (token.Kind)
                {
                    case TokenKind.Attribute when IsTestAttribute(token.Text):
                        pendingTestAttribute = true;
                        continue;

                    case TokenKind.Attribute:
                        // A different attribute (e.g. #[ignore], #[should_panic]) doesn't clear
                        // a pending #[test] seen just above it - only a non-attribute token does.
                        continue;

                    case TokenKind.Keyword when token.Text == "mod" && i + 1 < tokens.Count && tokens[i + 1].Kind == TokenKind.Identifier:
                        {
                            var name = tokens[i + 1].Text;
                            // Only a real "mod name { ... }" (not "mod name;", a separate file)
                            // pushes a scope; look ahead past the identifier for the opening brace.
                            var j = i + 2;
                            if (j < tokens.Count && tokens[j].Kind == TokenKind.OpenBrace)
                            {
                                modStack.Add((name, depth));
                            }
                            pendingTestAttribute = false;
                            break;
                        }

                    case TokenKind.Keyword when token.Text == "fn" && i + 1 < tokens.Count && tokens[i + 1].Kind == TokenKind.Identifier:
                        {
                            var name = tokens[i + 1].Text;
                            var isTest = pendingTestAttribute;
                            pendingTestAttribute = false;

                            // Find the function body's brace span (skip the signature).
                            var j = i + 2;
                            while (j < tokens.Count && tokens[j].Kind != TokenKind.OpenBrace && tokens[j].Kind != TokenKind.Semicolon)
                            {
                                j++;
                            }

                            if (j >= tokens.Count || tokens[j].Kind != TokenKind.OpenBrace)
                            {
                                // No body (trait method signature, or malformed input): nothing to record.
                                break;
                            }

                            var startLine = token.Line;
                            var endLine = FindMatchingCloseLine(tokens, j);

                            var qualifiedName = modStack.Count == 0
                                ? name
                                : string.Join("::", GetNames(modStack)) + "::" + name;
                            results.Add(new RustFunction(qualifiedName, isTest, startLine, endLine));

                            break;
                        }

                    case TokenKind.OpenBrace:
                        depth++;
                        pendingTestAttribute = false;
                        continue;

                    case TokenKind.CloseBrace:
                        depth--;
                        while (modStack.Count > 0 && modStack[modStack.Count - 1].OpenDepth >= depth)
                        {
                            modStack.RemoveAt(modStack.Count - 1);
                        }
                        continue;

                    case TokenKind.Semicolon:
                        pendingTestAttribute = false;
                        continue;

                    default:
                        // Identifiers/keywords that aren't "mod"/"fn" themselves - e.g. "pub",
                        // "async", "unsafe", "extern", visibility paths - are signature/item
                        // modifiers that may legitimately sit between an attribute and the
                        // "fn"/"mod" it applies to, so a pending #[test] must survive them.
                        continue;
                }
            }

            return results;
        }

        private static IEnumerable<string> GetNames(List<(string Name, int OpenDepth)> stack)
        {
            foreach (var entry in stack)
            {
                yield return entry.Name;
            }
        }

        private static bool IsTestAttribute(string attributeText)
        {
            // Matches "test" and "tokio::test"/"async_std::test"-style macro attributes, but not
            // "test_case" or other attributes that merely contain "test" as a substring.
            var trimmed = attributeText.Trim();
            if (trimmed == "test")
            {
                return true;
            }

            var lastSegmentStart = trimmed.LastIndexOf("::", StringComparison.Ordinal);
            var lastSegment = lastSegmentStart >= 0 ? trimmed.Substring(lastSegmentStart + 2) : trimmed;
            var parenIndex = lastSegment.IndexOf('(');
            if (parenIndex >= 0)
            {
                lastSegment = lastSegment.Substring(0, parenIndex);
            }

            return lastSegment.Trim() == "test";
        }

        private static int FindMatchingCloseLine(IReadOnlyList<Token> tokens, int openBraceIndex)
        {
            var depth = 0;
            for (var i = openBraceIndex; i < tokens.Count; i++)
            {
                if (tokens[i].Kind == TokenKind.OpenBrace)
                {
                    depth++;
                }
                else if (tokens[i].Kind == TokenKind.CloseBrace)
                {
                    depth--;
                    if (depth == 0)
                    {
                        return tokens[i].Line;
                    }
                }
            }

            // Unbalanced input (unlikely for real Rust source): treat as extending to the end.
            return tokens.Count > 0 ? tokens[tokens.Count - 1].Line : openBraceIndex;
        }

        private enum TokenKind
        {
            Keyword,
            Identifier,
            Attribute,
            OpenBrace,
            CloseBrace,
            Semicolon,
            Other,
        }

        private readonly struct Token
        {
            public Token(TokenKind kind, string text, int line)
            {
                Kind = kind;
                Text = text;
                Line = line;
            }

            public TokenKind Kind { get; }

            public string Text { get; }

            public int Line { get; }
        }

        /// <summary>
        /// A minimal Rust lexer: only distinguishes what <see cref="FindTestFunctions"/> needs
        /// (keywords "mod"/"fn", identifiers, "#[...]" attributes, braces, semicolons) while
        /// correctly skipping string/char literals and comments, so a brace inside a string
        /// (e.g. `format!("Hello, {name}!")`) never corrupts the brace-depth scan.
        /// </summary>
        private static List<Token> Tokenize(string text)
        {
            var tokens = new List<Token>();
            var line = 0;
            var i = 0;
            var length = text.Length;

            while (i < length)
            {
                var c = text[i];

                if (c == '\n')
                {
                    line++;
                    i++;
                    continue;
                }

                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                // Line comment.
                if (c == '/' && i + 1 < length && text[i + 1] == '/')
                {
                    while (i < length && text[i] != '\n')
                    {
                        i++;
                    }
                    continue;
                }

                // Block comment (nested, as Rust allows).
                if (c == '/' && i + 1 < length && text[i + 1] == '*')
                {
                    var depth = 1;
                    i += 2;
                    while (i < length && depth > 0)
                    {
                        if (text[i] == '\n')
                        {
                            line++;
                        }
                        else if (i + 1 < length && text[i] == '/' && text[i + 1] == '*')
                        {
                            depth++;
                            i++;
                        }
                        else if (i + 1 < length && text[i] == '*' && text[i + 1] == '/')
                        {
                            depth--;
                            i++;
                        }
                        i++;
                    }
                    continue;
                }

                // String literal (best-effort: honors backslash escapes; raw strings r"..."/r#"..."#
                // never contain unescaped braces that would need matching, so treating their
                // interior as opaque up to the first quote is sufficient here).
                if (c == '"')
                {
                    i++;
                    while (i < length && text[i] != '"')
                    {
                        if (text[i] == '\\' && i + 1 < length)
                        {
                            i++;
                        }
                        if (i < length && text[i] == '\n')
                        {
                            line++;
                        }
                        i++;
                    }
                    i++; // closing quote
                    continue;
                }

                // Char literal, e.g. '{' or '\''.
                if (c == '\'' && i + 1 < length && !IsIdentifierStart(text[i + 1]))
                {
                    i++;
                    while (i < length && text[i] != '\'')
                    {
                        if (text[i] == '\\' && i + 1 < length)
                        {
                            i++;
                        }
                        i++;
                    }
                    i++; // closing quote
                    continue;
                }

                if (c == '#' && i + 1 < length && text[i + 1] == '[')
                {
                    var startLine = line;
                    var start = i + 2;
                    i = start;
                    var depth = 1;
                    while (i < length && depth > 0)
                    {
                        if (text[i] == '[')
                        {
                            depth++;
                        }
                        else if (text[i] == ']')
                        {
                            depth--;
                        }
                        else if (text[i] == '\n')
                        {
                            line++;
                        }
                        if (depth > 0)
                        {
                            i++;
                        }
                    }

                    var attributeText = text.Substring(start, i - start);
                    tokens.Add(new Token(TokenKind.Attribute, attributeText, startLine));
                    i++; // closing ]
                    continue;
                }

                if (c == '{')
                {
                    tokens.Add(new Token(TokenKind.OpenBrace, "{", line));
                    i++;
                    continue;
                }

                if (c == '}')
                {
                    tokens.Add(new Token(TokenKind.CloseBrace, "}", line));
                    i++;
                    continue;
                }

                if (c == ';')
                {
                    tokens.Add(new Token(TokenKind.Semicolon, ";", line));
                    i++;
                    continue;
                }

                if (IsIdentifierStart(c))
                {
                    var start = i;
                    while (i < length && IsIdentifierPart(text[i]))
                    {
                        i++;
                    }

                    var word = text.Substring(start, i - start);
                    var kind = word == "mod" || word == "fn" ? TokenKind.Keyword : TokenKind.Identifier;
                    tokens.Add(new Token(kind, word, line));
                    continue;
                }

                i++;
            }

            return tokens;
        }

        private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';

        private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_';
    }
}
