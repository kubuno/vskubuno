using System.Collections.Generic;

namespace Kubuno.Views.Designer.Selection
{
    /// <summary>
    /// Reads one element's tag name and attributes directly off the CURRENT <c>.kbview</c> buffer text,
    /// by its stable id (docs/DESIGNER.md §8) - the "pure C# reader over the buffer text using the same
    /// stable-id scheme" docs/DESIGNER.md's own DSG-8 row offers as an alternative to a new
    /// <c>kubuno/elementAttributes</c> LS method, which does not exist today (checked:
    /// <c>kubuno-views-ls/src/server.rs</c>'s method table has no such arm). Chosen over requesting that
    /// LS addition because the Properties panel needs this on every selection change (docs/DESIGNER.md
    /// §1's own "Properties window ... driven entirely by registry metadata"), and a synchronous,
    /// no-round-trip read keeps that path as snappy as the panel itself already is - see this package's
    /// own report for the LS-addition alternative, left for a follow-up if a future need (e.g. a
    /// property EDIT wants server-validated current values) outgrows this reader's best-effort nature.
    ///
    /// A deliberately independent, SIMPLIFIED re-implementation of <c>kubuno_views::syntax</c>'s grammar
    /// (`kubuno-views/src/syntax/lexer.rs`'s own doc: element/attribute names are ASCII
    /// `[A-Za-z_][A-Za-z0-9_.:-]*` tokens - not real XML namespaces, so <c>x:Name</c> is one flat
    /// identifier, never resolved against a `xmlns:x` declaration; `System.Xml.Linq.XDocument` would
    /// reject exactly this real-world shape, e.g. <c>tests/corpus/settings_view.kbview</c>'s own
    /// <c>x:Class="..."</c> with no <c>xmlns:x</c> anywhere in the file - checked directly against that
    /// corpus file before choosing a hand-rolled scanner over `System.Xml`), read-only, and best-effort:
    /// it never throws, degrading to <see langword="null"/> on anything it cannot make sense of (an
    /// out-of-range/malformed id, a document with no root element, running off the end of the file while
    /// still inside a tag). It is NOT the source of truth for what a designer gesture writes - every
    /// actual mutation still goes through `kubuno-views-ls`'s `kubuno/applyEdit` (docs/DESIGNER.md §2);
    /// this type only ever reads, for immediate, synchronous display in the Properties panel.
    ///
    /// Mirrors the real parser's own error-tolerant shape closely enough for well-formed and mildly
    /// malformed input (a stray/mismatched closing tag closes whatever element is currently open,
    /// regardless of its name - `kubuno-views/src/syntax/parser.rs`'s own `parse_end_tag` never checks
    /// name equality either, see that method's doc), without reproducing its full diagnostic machinery -
    /// this reader only needs "which element is this", not "is this file valid".
    /// </summary>
    public static class ElementAttributeReader
    {
        /// <summary><see langword="null"/> when <paramref name="elementId"/> does not resolve against <paramref name="documentText"/>'s current best-effort parse - a stale id (racing a concurrent edit), a malformed one, or a document with no root element at all.</summary>
        public static ElementAttributes? Read(string documentText, string elementId)
        {
            if (documentText is null || elementId is null)
            {
                return null;
            }

            if (!StableElementId.TryParseSegments(elementId, out var path))
            {
                return null;
            }

            var root = new Scanner(documentText).ParseRootElement();
            if (root is null)
            {
                return null;
            }

            var current = root;
            foreach (var index in path)
            {
                if (index < 0 || index >= current.Children.Count)
                {
                    return null;
                }

                current = current.Children[index];
            }

            return new ElementAttributes(current.Name, current.Attributes, current.Children.ConvertAll(child => child.Name));
        }

        private sealed class ScannedElement
        {
            public string Name = string.Empty;
            public Dictionary<string, string?> Attributes { get; } = new Dictionary<string, string?>(System.StringComparer.Ordinal);
            public List<ScannedElement> Children { get; } = new List<ScannedElement>();
        }

        /// <summary>
        /// A minimal, forward-only scanner over the raw text - no token list, no tree builder beyond the
        /// one <see cref="ScannedElement"/> shape this file needs. Every branch of every loop below
        /// strictly advances <see cref="_pos"/> (by at least one character, or to a found delimiter's end,
        /// or to end-of-input), so nothing here can loop forever even on adversarial/malformed input.
        /// </summary>
        private sealed class Scanner
        {
            private readonly string _text;
            private int _pos;

            public Scanner(string text)
            {
                _text = text;
                _pos = 0;
            }

            /// <summary>Finds and parses the document's one root element, skipping any leading comment/CDATA/processing-instruction/text - <see langword="null"/> if none is found before end of input.</summary>
            public ScannedElement? ParseRootElement()
            {
                while (_pos < _text.Length)
                {
                    if (_text[_pos] != '<')
                    {
                        _pos++;
                        continue;
                    }

                    if (TrySkipNonElementMarkup())
                    {
                        continue;
                    }

                    if (StartsWith("</"))
                    {
                        // A stray closing tag before any root element - malformed, but must not loop:
                        // consume up to (and including) its '>', or to end of input.
                        SkipPastChar('>');
                        continue;
                    }

                    if (IsIdentStart(PeekAt(1)))
                    {
                        return ParseElement();
                    }

                    _pos++;
                }

                return null;
            }

            private ScannedElement ParseElement()
            {
                var element = new ScannedElement();
                _pos++; // consume '<'
                element.Name = ReadIdent();

                var selfClosing = ParseAttributesAndTagClose(element.Attributes);
                if (!selfClosing)
                {
                    ParseChildren(element.Children);
                }

                return element;
            }

            /// <summary>Reads <c>Name="value"</c> pairs up to the tag's own closer; returns whether it self-closed (<c>/&gt;</c>) rather than opening (<c>&gt;</c>).</summary>
            private bool ParseAttributesAndTagClose(Dictionary<string, string?> attributes)
            {
                while (_pos < _text.Length)
                {
                    SkipTagWhitespace();
                    if (_pos >= _text.Length)
                    {
                        break; // Unterminated tag, ran off the end of the file - recover as self-closing.
                    }

                    if (StartsWith("/>"))
                    {
                        _pos += 2;
                        return true;
                    }

                    if (_text[_pos] == '>')
                    {
                        _pos++;
                        return false;
                    }

                    if (IsIdentStart(_text[_pos]))
                    {
                        var name = ReadIdent();
                        SkipTagWhitespace();
                        string? value = null;
                        if (_pos < _text.Length && _text[_pos] == '=')
                        {
                            _pos++;
                            SkipTagWhitespace();
                            value = ReadQuotedStringOrNull();
                        }

                        // First occurrence wins on a duplicate name - mirrors `Element::attribute`'s own
                        // `.find(...)` (first match), see this file's own class doc.
                        if (!attributes.ContainsKey(name))
                        {
                            attributes[name] = value;
                        }

                        continue;
                    }

                    // An unexpected character inside the tag (stray punctuation) - skip it and keep
                    // scanning for the real closer, the same error-tolerance spirit as the real parser's
                    // own `recover_unexpected`.
                    _pos++;
                }

                return true; // Ran off the end of input mid-tag - recover as self-closing (nothing sane to nest children into).
            }

            /// <summary>Element/text/comment/CDATA/PI children, until the parent's own closing tag (any name, per this file's own class doc) or end of input.</summary>
            private void ParseChildren(List<ScannedElement> children)
            {
                while (_pos < _text.Length)
                {
                    if (_text[_pos] != '<')
                    {
                        var next = _text.IndexOf('<', _pos);
                        _pos = next < 0 ? _text.Length : next;
                        continue;
                    }

                    if (TrySkipNonElementMarkup())
                    {
                        continue;
                    }

                    if (StartsWith("</"))
                    {
                        SkipPastChar('>');
                        return; // The parent's own end tag, consumed - stop reading children.
                    }

                    if (IsIdentStart(PeekAt(1)))
                    {
                        children.Add(ParseElement());
                        continue;
                    }

                    _pos++; // A stray '<' matching nothing recognised - treat as one garbage character.
                }
            }

            /// <summary>At a <c>'&lt;'</c>: skips a comment/CDATA section/processing instruction if the text starts with one, advancing past it. Returns whether it did.</summary>
            private bool TrySkipNonElementMarkup()
            {
                if (StartsWith("<!--"))
                {
                    SkipPastOrEof(4, "-->");
                    return true;
                }

                if (StartsWith("<![CDATA["))
                {
                    SkipPastOrEof(9, "]]>");
                    return true;
                }

                if (StartsWith("<?"))
                {
                    SkipPastOrEof(2, "?>");
                    return true;
                }

                return false;
            }

            private void SkipPastOrEof(int prefixLength, string needle)
            {
                var searchFrom = _pos + prefixLength;
                var idx = searchFrom <= _text.Length ? _text.IndexOf(needle, searchFrom, System.StringComparison.Ordinal) : -1;
                _pos = idx < 0 ? _text.Length : idx + needle.Length;
            }

            private void SkipPastChar(char c)
            {
                var idx = _text.IndexOf(c, _pos);
                _pos = idx < 0 ? _text.Length : idx + 1;
            }

            private void SkipTagWhitespace()
            {
                while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos]))
                {
                    _pos++;
                }
            }

            private string ReadIdent()
            {
                var start = _pos;
                while (_pos < _text.Length && IsIdentContinue(_text[_pos]))
                {
                    _pos++;
                }

                return _text.Substring(start, _pos - start);
            }

            /// <summary>
            /// Reads a <c>"value"</c>/<c>'value'</c> token at the current position (quotes stripped, character
            /// references decoded like the runtime reads them); <see langword="null"/> (with <see cref="_pos"/>
            /// advanced to end of input) for an unterminated string, mirroring
            /// <c>kubuno_views::ast::Attribute::value</c>'s own "None when malformed" contract.
            /// </summary>
            private string? ReadQuotedStringOrNull()
            {
                if (_pos >= _text.Length || (_text[_pos] != '"' && _text[_pos] != '\''))
                {
                    return null;
                }

                var quote = _text[_pos];
                var valueStart = _pos + 1;
                var closing = _text.IndexOf(quote, valueStart);
                if (closing < 0)
                {
                    _pos = _text.Length;
                    return null;
                }

                var value = XmlCharacterReferences.Decode(_text.Substring(valueStart, closing - valueStart));
                _pos = closing + 1;
                return value;
            }

            private bool StartsWith(string needle)
            {
                return _pos + needle.Length <= _text.Length && string.CompareOrdinal(_text, _pos, needle, 0, needle.Length) == 0;
            }

            private char PeekAt(int offset)
            {
                var i = _pos + offset;
                return i < _text.Length ? _text[i] : '\0';
            }

            /// <summary>Ascii alphabetic or <c>_</c> - mirrors <c>kubuno-views/src/syntax/lexer.rs</c>'s own <c>is_ident_start</c>.</summary>
            private static bool IsIdentStart(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';

            /// <summary>Ascii alphanumeric, <c>_</c>, <c>-</c>, <c>.</c> or <c>:</c> - mirrors <c>lexer.rs</c>'s own <c>is_ident_continue</c> ("enough for Panel, x:Name, data-foo, Header.Icon-style names").</summary>
            private static bool IsIdentContinue(char c) =>
                (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.' || c == ':';
        }
    }
}
