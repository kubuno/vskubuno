using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Kubuno.Desktop.Designer.Selection
{
    /// <summary>One attribute of a <see cref="ViewNode"/> with where it sits in the text.</summary>
    public sealed class ViewAttribute
    {
        internal ViewAttribute(string name, string? value, int start, int valueStart, int valueEnd, int end)
        {
            Name = name;
            Value = value;
            Start = start;
            ValueStart = valueStart;
            ValueEnd = valueEnd;
            End = end;
        }

        public string Name { get; }

        /// <summary>The raw value (quotes stripped, no entity decoding), null when malformed.</summary>
        public string? Value { get; }

        /// <summary>Offset of the attribute's name.</summary>
        public int Start { get; }

        /// <summary>Offsets of the value inside its quotes (<c>-1</c> when there is none).</summary>
        public int ValueStart { get; }

        public int ValueEnd { get; }

        /// <summary>Offset just after the closing quote.</summary>
        public int End { get; }
    }

    /// <summary>
    /// One element of a <c>.kbview</c> text with its stable id (docs/DESIGNER.md §8) and its position in the text - what
    /// the Properties window's collection and list editors need to rewrite children surgically. Read with the same
    /// tolerant grammar as <see cref="ElementAttributeReader"/>.
    /// </summary>
    public sealed class ViewNode
    {
        internal ViewNode(string id, ViewNode? parent)
        {
            Id = id;
            Parent = parent;
        }

        public string Id { get; }

        public ViewNode? Parent { get; }

        public string Name { get; internal set; } = string.Empty;

        public List<ViewAttribute> Attributes { get; } = new List<ViewAttribute>();

        public List<ViewNode> Children { get; } = new List<ViewNode>();

        /// <summary>Offset of the <c>&lt;</c> opening the element.</summary>
        public int Start { get; internal set; }

        /// <summary>Offset of the start tag's closer: the <c>/</c> of <c>/&gt;</c>, or its <c>&gt;</c>.</summary>
        public int StartTagClose { get; internal set; }

        /// <summary>Offset just after the start tag.</summary>
        public int StartTagEnd { get; internal set; }

        public bool SelfClosing { get; internal set; }

        /// <summary>Offset of the end tag's <c>&lt;/</c> (the end of the start tag for a self-closing element).</summary>
        public int CloseTagStart { get; internal set; }

        /// <summary>Offset just after the whole element.</summary>
        public int End { get; internal set; }

        /// <summary>The raw value of attribute <paramref name="name"/>, or null.</summary>
        public string? Attribute(string name) => Attributes.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.Ordinal))?.Value;

        /// <summary>This element and every descendant, in document order.</summary>
        public IEnumerable<ViewNode> DescendantsAndSelf()
        {
            yield return this;
            foreach (var child in Children)
            {
                foreach (var node in child.DescendantsAndSelf())
                {
                    yield return node;
                }
            }
        }
    }

    /// <summary>Parses a <c>.kbview</c> text into <see cref="ViewNode"/>s. Never throws; null without a root element.</summary>
    public static class ViewDocument
    {
        public static ViewNode? Parse(string? text) => text is null ? null : new Scanner(text).ParseRoot();

        /// <summary>The element with stable id <paramref name="id"/>, or null.</summary>
        public static ViewNode? Find(ViewNode? root, string id) => root?.DescendantsAndSelf().FirstOrDefault(n => string.Equals(n.Id, id, StringComparison.Ordinal));

        private sealed class Scanner
        {
            private readonly string _text;
            private int _pos;

            public Scanner(string text) => _text = text;

            public ViewNode? ParseRoot()
            {
                while (_pos < _text.Length)
                {
                    if (_text[_pos] != '<')
                    {
                        _pos++;
                        continue;
                    }

                    if (SkipMarkup())
                    {
                        continue;
                    }

                    if (StartsWith("</"))
                    {
                        SkipPast('>');
                        continue;
                    }

                    if (IsIdentStart(Peek(1)))
                    {
                        return ParseElement(string.Empty, null);
                    }

                    _pos++;
                }

                return null;
            }

            private ViewNode ParseElement(string id, ViewNode? parent)
            {
                var node = new ViewNode(id, parent) { Start = _pos };
                _pos++;
                node.Name = ReadIdent();
                while (_pos < _text.Length)
                {
                    SkipWhitespace();
                    if (_pos >= _text.Length)
                    {
                        break;
                    }

                    if (StartsWith("/>"))
                    {
                        node.StartTagClose = _pos;
                        _pos += 2;
                        node.StartTagEnd = node.CloseTagStart = node.End = _pos;
                        node.SelfClosing = true;
                        return node;
                    }

                    if (_text[_pos] == '>')
                    {
                        node.StartTagClose = _pos;
                        _pos++;
                        node.StartTagEnd = _pos;
                        ParseChildren(node);
                        return node;
                    }

                    if (IsIdentStart(_text[_pos]))
                    {
                        var start = _pos;
                        var name = ReadIdent();
                        SkipWhitespace();
                        string? value = null;
                        int valueStart = -1, valueEnd = -1;
                        if (_pos < _text.Length && _text[_pos] == '=')
                        {
                            _pos++;
                            SkipWhitespace();
                            if (_pos < _text.Length && (_text[_pos] == '"' || _text[_pos] == '\''))
                            {
                                var quote = _text[_pos];
                                var close = _text.IndexOf(quote, _pos + 1);
                                if (close < 0)
                                {
                                    _pos = _text.Length;
                                }
                                else
                                {
                                    valueStart = _pos + 1;
                                    valueEnd = close;
                                    value = XmlCharacterReferences.Decode(_text.Substring(valueStart, valueEnd - valueStart));
                                    _pos = close + 1;
                                }
                            }
                        }

                        if (!node.Attributes.Any(a => a.Name == name))
                        {
                            node.Attributes.Add(new ViewAttribute(name, value, start, valueStart, valueEnd, _pos));
                        }

                        continue;
                    }

                    _pos++;
                }

                node.StartTagClose = node.StartTagEnd = node.CloseTagStart = node.End = _pos;
                node.SelfClosing = true;
                return node;
            }

            private void ParseChildren(ViewNode node)
            {
                while (_pos < _text.Length)
                {
                    if (_text[_pos] != '<')
                    {
                        var next = _text.IndexOf('<', _pos);
                        _pos = next < 0 ? _text.Length : next;
                        continue;
                    }

                    if (SkipMarkup())
                    {
                        continue;
                    }

                    if (StartsWith("</"))
                    {
                        node.CloseTagStart = _pos;
                        SkipPast('>');
                        node.End = _pos;
                        return;
                    }

                    if (IsIdentStart(Peek(1)))
                    {
                        var index = node.Children.Count.ToString(CultureInfo.InvariantCulture);
                        node.Children.Add(ParseElement(node.Id.Length == 0 ? index : node.Id + "." + index, node));
                        continue;
                    }

                    _pos++;
                }

                node.CloseTagStart = node.End = _pos;
            }

            private bool SkipMarkup()
            {
                foreach (var (open, close) in new[] { ("<!--", "-->"), ("<![CDATA[", "]]>"), ("<?", "?>") })
                {
                    if (StartsWith(open))
                    {
                        var idx = _text.IndexOf(close, _pos + open.Length, StringComparison.Ordinal);
                        _pos = idx < 0 ? _text.Length : idx + close.Length;
                        return true;
                    }
                }

                return false;
            }

            private void SkipPast(char c)
            {
                var idx = _text.IndexOf(c, _pos);
                _pos = idx < 0 ? _text.Length : idx + 1;
            }

            private void SkipWhitespace()
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

            private bool StartsWith(string needle) => _pos + needle.Length <= _text.Length && string.CompareOrdinal(_text, _pos, needle, 0, needle.Length) == 0;

            private char Peek(int offset) => _pos + offset < _text.Length ? _text[_pos + offset] : '\0';

            private static bool IsIdentStart(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';

            private static bool IsIdentContinue(char c) => IsIdentStart(c) || (c >= '0' && c <= '9') || c == '-' || c == '.' || c == ':';
        }
    }
}
