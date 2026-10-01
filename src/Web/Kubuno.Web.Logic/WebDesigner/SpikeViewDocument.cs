using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Kubuno.Web.Logic.WebDesigner
{
    /// <summary>One contiguous replacement of a document's text: what Visual Studio applies to the buffer as one undo unit.</summary>
    public readonly struct SpikeTextChange
    {
        public SpikeTextChange(int start, int oldLength, string newText)
        {
            Start = start;
            OldLength = oldLength;
            NewText = newText;
        }

        public int Start { get; }

        public int OldLength { get; }

        public string NewText { get; }

        public bool IsEmpty => OldLength == 0 && NewText.Length == 0;

        /// <summary>The smallest single replacement turning <paramref name="before"/> into <paramref name="after"/> (common prefix and suffix kept).</summary>
        public static SpikeTextChange Between(string before, string after)
        {
            var prefix = 0;
            var max = Math.Min(before.Length, after.Length);
            while (prefix < max && before[prefix] == after[prefix])
            {
                prefix++;
            }

            var suffix = 0;
            while (suffix < max - prefix && before[before.Length - 1 - suffix] == after[after.Length - 1 - suffix])
            {
                suffix++;
            }

            return new SpikeTextChange(prefix, before.Length - prefix - suffix, after.Substring(prefix, after.Length - prefix - suffix));
        }

        public string ApplyTo(string text) => text.Substring(0, Start) + NewText + text.Substring(Start + OldLength);
    }

    /// <summary>
    /// SPIKE (docs/WEB-VIEWS.md, WV-9a): the spike's view markup, read and edited <b>surgically</b> - every byte outside
    /// the edited attribute or element is kept (comments, attribute order and quotes, indentation, self-closing style),
    /// the rule of docs/DESIGNER.md §2. A deliberately small XML scanner (elements, attributes, comments, processing
    /// instructions, CDATA; no DTD): in WV-9b the language server's <c>kubuno/applyEdit</c> on the real rowan parser does
    /// this job. Element ids are the desktop's: the dot path of element-child ordinals, <c>""</c> for the root.
    /// </summary>
    public sealed class SpikeViewDocument
    {
        private SpikeViewDocument(string text, SpikeNode root)
        {
            Text = text;
            Root = root;
        }

        public string Text { get; }

        public SpikeNode Root { get; }

        /// <summary>Parses <paramref name="text"/>; null with <paramref name="error"/> set when it is not well-formed.</summary>
        public static SpikeViewDocument? Parse(string text, out string? error)
        {
            try
            {
                var root = new Scanner(text).ParseDocument();
                error = null;
                return new SpikeViewDocument(text, root);
            }
            catch (FormatException ex)
            {
                error = ex.Message;
                return null;
            }
        }

        /// <summary>The element of id <paramref name="id"/> (<c>""</c> = root, <c>"1.0"</c> = first child of the second child), or null.</summary>
        public SpikeNode? Find(string? id)
        {
            if (id is null)
            {
                return null;
            }

            var node = Root;
            if (id.Length == 0)
            {
                return node;
            }

            foreach (var part in id.Split('.'))
            {
                if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index >= node.Children.Count)
                {
                    return null;
                }

                node = node.Children[index];
            }

            return node;
        }

        /// <summary>
        /// The text after applying <paramref name="op"/>, as one replacement of <see cref="Text"/>; null with
        /// <paramref name="error"/> when the op does not apply (unknown element, not a container, invalid markup).
        /// </summary>
        public SpikeTextChange? Apply(SurfaceEditOp op, out string? error)
        {
            error = null;
            string? after = op.Kind switch
            {
                SurfaceEditKind.SetAttribute => SetAttribute(op.ElementId, op.Name ?? string.Empty, op.Value ?? string.Empty, out error),
                SurfaceEditKind.RemoveElement => RemoveElement(op.ElementId, out error),
                SurfaceEditKind.InsertChild => InsertChild(op.ParentId ?? string.Empty, op.Index, op.Xml ?? string.Empty, out error),
                SurfaceEditKind.MoveElement => MoveElement(op.ElementId, op.ParentId ?? string.Empty, op.Index, out error),
                _ => null,
            };
            if (after is null)
            {
                error ??= "unsupported edit";
                return null;
            }

            if (Parse(after, out var reparse) is null)
            {
                error = "the edit would make the markup invalid: " + reparse;
                return null;
            }

            return SpikeTextChange.Between(Text, after);
        }

        private string? SetAttribute(string id, string name, string value, out string? error)
        {
            error = null;
            var element = Find(id);
            if (element is null)
            {
                error = $"no element '{id}'";
                return null;
            }

            if (!SpikeElementCatalog.IsElementName(name))
            {
                error = $"invalid attribute name '{name}'";
                return null;
            }

            var existing = element.Attributes.FirstOrDefault(a => a.Name == name);
            if (existing is not null)
            {
                return Text.Substring(0, existing.ValueStart) + Escape(value, existing.Quote) + Text.Substring(existing.ValueEnd);
            }

            var insertAt = element.Attributes.Count > 0 ? element.Attributes[element.Attributes.Count - 1].End : element.NameEnd;
            return Text.Substring(0, insertAt) + " " + name + "=\"" + Escape(value, '"') + "\"" + Text.Substring(insertAt);
        }

        private string? RemoveElement(string id, out string? error)
        {
            error = null;
            var element = Find(id);
            if (element is null || element.Parent is null)
            {
                error = element is null ? $"no element '{id}'" : "the root element cannot be removed";
                return null;
            }

            var (start, end) = LineRange(element);
            return Text.Substring(0, start) + Text.Substring(end);
        }

        private string? InsertChild(string parentId, int index, string xml, out string? error)
        {
            error = null;
            var parent = Find(parentId);
            if (parent is null)
            {
                error = $"no element '{parentId}'";
                return null;
            }

            if (SpikeElementCatalog.Find(parent.Name) is { IsContainer: false })
            {
                error = $"{parent.Name} cannot contain children";
                return null;
            }

            var inserted = Parse(xml.Trim(), out var xmlError);
            if (inserted is null)
            {
                error = "invalid element markup: " + xmlError;
                return null;
            }

            var markup = inserted.Text;
            var newline = Text.Contains("\r\n") ? "\r\n" : "\n";
            var parentIndent = IndentOf(parent.Start);
            var childIndent = parent.Children.Count > 0 ? IndentOf(parent.Children[0].Start) : parentIndent + IndentUnit(parentIndent);

            if (parent.SelfClosing)
            {
                // <Stack .../> becomes <Stack ...> child </Stack>, keeping the attributes as written.
                var close = parent.StartTagEnd - 2;
                while (close > parent.NameEnd && char.IsWhiteSpace(Text[close - 1]))
                {
                    close--;
                }

                return Text.Substring(0, close) + ">" + newline + childIndent + markup + newline + parentIndent + "</" + parent.Name + ">" + Text.Substring(parent.End);
            }

            index = Math.Max(0, Math.Min(index, parent.Children.Count));
            if (index < parent.Children.Count)
            {
                var before = parent.Children[index];
                var lineStart = LineStart(before.Start);
                if (lineStart >= 0)
                {
                    return Text.Substring(0, lineStart) + childIndent + markup + newline + Text.Substring(lineStart);
                }

                return Text.Substring(0, before.Start) + markup + Text.Substring(before.Start);
            }

            var endTagLineStart = LineStart(parent.EndTagStart);
            if (endTagLineStart >= 0)
            {
                return Text.Substring(0, endTagLineStart) + childIndent + markup + newline + Text.Substring(endTagLineStart);
            }

            return Text.Substring(0, parent.EndTagStart) + markup + Text.Substring(parent.EndTagStart);
        }

        private string? MoveElement(string id, string newParentId, int index, out string? error)
        {
            var element = Find(id);
            if (element is null || element.Parent is null)
            {
                error = element is null ? $"no element '{id}'" : "the root element cannot be moved";
                return null;
            }

            if (newParentId == id || newParentId.StartsWith(id + ".", StringComparison.Ordinal))
            {
                error = "an element cannot move into itself";
                return null;
            }

            var markup = Text.Substring(element.Start, element.End - element.Start);
            var removed = RemoveElement(id, out error);
            var afterRemoval = removed is null ? null : Parse(removed, out error);
            if (afterRemoval is null)
            {
                return null;
            }

            // The new parent's id as it reads once the element is gone (a later sibling of an ancestor shifts by one).
            var parentAfter = ShiftAfterRemoval(newParentId, id);
            return afterRemoval.InsertChild(parentAfter, index, markup, out error);
        }

        /// <summary>The id <paramref name="id"/> designates once the element <paramref name="removed"/> is gone.</summary>
        public static string ShiftAfterRemoval(string id, string removed)
        {
            if (removed.Length == 0 || id.Length == 0)
            {
                return id;
            }

            var removedParts = removed.Split('.');
            var parts = id.Split('.');
            var depth = removedParts.Length - 1;
            if (parts.Length <= depth)
            {
                return id;
            }

            for (var i = 0; i < depth; i++)
            {
                if (parts[i] != removedParts[i])
                {
                    return id;
                }
            }

            var at = int.Parse(parts[depth], CultureInfo.InvariantCulture);
            if (at > int.Parse(removedParts[depth], CultureInfo.InvariantCulture))
            {
                parts[depth] = (at - 1).ToString(CultureInfo.InvariantCulture);
            }

            return string.Join(".", parts);
        }

        /// <summary>The element's range, widened to its whole lines when nothing else shares them.</summary>
        private (int Start, int End) LineRange(SpikeNode element)
        {
            var start = element.Start;
            var end = element.End;
            var lineStart = LineStart(start);
            var lineEnd = end;
            while (lineEnd < Text.Length && (Text[lineEnd] == ' ' || Text[lineEnd] == '\t'))
            {
                lineEnd++;
            }

            if (lineStart >= 0 && (lineEnd == Text.Length || Text[lineEnd] == '\r' || Text[lineEnd] == '\n'))
            {
                if (lineEnd < Text.Length && Text[lineEnd] == '\r')
                {
                    lineEnd++;
                }

                if (lineEnd < Text.Length && Text[lineEnd] == '\n')
                {
                    lineEnd++;
                }

                return (lineStart, lineEnd);
            }

            return (start, end);
        }

        /// <summary>The start of the line holding <paramref name="offset"/> when only spaces or tabs precede it on that line, else -1.</summary>
        private int LineStart(int offset)
        {
            var i = offset;
            while (i > 0 && (Text[i - 1] == ' ' || Text[i - 1] == '\t'))
            {
                i--;
            }

            return i == 0 || Text[i - 1] == '\n' ? i : -1;
        }

        private string IndentOf(int offset)
        {
            var start = offset;
            while (start > 0 && (Text[start - 1] == ' ' || Text[start - 1] == '\t'))
            {
                start--;
            }

            return start == 0 || Text[start - 1] == '\n' ? Text.Substring(start, offset - start) : string.Empty;
        }

        private static string IndentUnit(string parentIndent) => parentIndent.StartsWith("\t", StringComparison.Ordinal) ? "\t" : "  ";

        /// <summary>An attribute value as written between <paramref name="quote"/>s.</summary>
        public static string Escape(string value, char quote)
        {
            var builder = new StringBuilder(value.Length + 8);
            foreach (var c in value)
            {
                switch (c)
                {
                    case '&': builder.Append("&amp;"); break;
                    case '<': builder.Append("&lt;"); break;
                    case '"' when quote == '"': builder.Append("&quot;"); break;
                    case '\'' when quote == '\'': builder.Append("&apos;"); break;
                    case '\n': builder.Append("&#10;"); break;
                    case '\r': builder.Append("&#13;"); break;
                    case '\t': builder.Append("&#9;"); break;
                    default: builder.Append(c); break;
                }
            }

            return builder.ToString();
        }

        /// <summary>An attribute's raw text with its character references resolved.</summary>
        public static string Unescape(string raw)
        {
            if (raw.IndexOf('&') < 0)
            {
                return raw;
            }

            var builder = new StringBuilder(raw.Length);
            for (var i = 0; i < raw.Length; i++)
            {
                var semicolon = raw[i] == '&' ? raw.IndexOf(';', i) : -1;
                if (semicolon < 0)
                {
                    builder.Append(raw[i]);
                    continue;
                }

                var entity = raw.Substring(i + 1, semicolon - i - 1);
                string? resolved = entity switch
                {
                    "amp" => "&",
                    "lt" => "<",
                    "gt" => ">",
                    "quot" => "\"",
                    "apos" => "'",
                    _ when entity.StartsWith("#x", StringComparison.OrdinalIgnoreCase) && int.TryParse(entity.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex) => char.ConvertFromUtf32(hex),
                    _ when entity.StartsWith("#", StringComparison.Ordinal) && int.TryParse(entity.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out var dec) => char.ConvertFromUtf32(dec),
                    _ => null,
                };
                if (resolved is null)
                {
                    builder.Append(raw[i]);
                    continue;
                }

                builder.Append(resolved);
                i = semicolon;
            }

            return builder.ToString();
        }

        /// <summary>The minimal XML scanner (see the class remarks).</summary>
        private sealed class Scanner
        {
            private readonly string _text;
            private int _pos;

            public Scanner(string text) => _text = text;

            public SpikeNode ParseDocument()
            {
                SkipMisc();
                if (_pos >= _text.Length || _text[_pos] != '<')
                {
                    throw Error("a root element is expected");
                }

                var root = ParseElement(null, string.Empty);
                SkipMisc();
                if (_pos < _text.Length)
                {
                    throw Error("only one root element is allowed");
                }

                return root;
            }

            private SpikeNode ParseElement(SpikeNode? parent, string id)
            {
                var start = _pos;
                _pos++; // '<'
                var nameStart = _pos;
                var name = ReadName();
                var node = new SpikeNode(parent, id, name, start, nameStart + name.Length);
                while (true)
                {
                    SkipWhitespace();
                    if (_pos >= _text.Length)
                    {
                        throw Error($"unterminated start tag <{name}>");
                    }

                    if (_text[_pos] == '/')
                    {
                        Expect("/>");
                        node.SelfClosing = true;
                        node.StartTagEnd = _pos;
                        node.EndTagStart = _pos;
                        node.End = _pos;
                        return node;
                    }

                    if (_text[_pos] == '>')
                    {
                        _pos++;
                        node.StartTagEnd = _pos;
                        break;
                    }

                    var attributeStart = _pos;
                    var attributeName = ReadName();
                    SkipWhitespace();
                    Expect("=");
                    SkipWhitespace();
                    if (_pos >= _text.Length || (_text[_pos] != '"' && _text[_pos] != '\''))
                    {
                        throw Error($"attribute {attributeName} needs a quoted value");
                    }

                    var quote = _text[_pos++];
                    var valueStart = _pos;
                    var valueEnd = _text.IndexOf(quote, _pos);
                    if (valueEnd < 0)
                    {
                        throw Error($"unterminated value of {attributeName}");
                    }

                    if (_text.IndexOf('<', valueStart, valueEnd - valueStart) >= 0)
                    {
                        throw Error($"'<' in the value of {attributeName}");
                    }

                    _pos = valueEnd + 1;
                    if (node.Attributes.Any(a => a.Name == attributeName))
                    {
                        throw Error($"duplicate attribute {attributeName}");
                    }

                    node.Attributes.Add(new SpikeAttribute(attributeName, quote, attributeStart, valueStart, valueEnd, _pos, Unescape(_text.Substring(valueStart, valueEnd - valueStart))));
                }

                // Content: text, comments, children, then the end tag.
                while (true)
                {
                    var next = _text.IndexOf('<', _pos);
                    if (next < 0)
                    {
                        throw Error($"<{name}> is never closed");
                    }

                    _pos = next;
                    if (StartsWith("</"))
                    {
                        node.EndTagStart = _pos;
                        _pos += 2;
                        var endName = ReadName();
                        if (endName != name)
                        {
                            throw Error($"</{endName}> closes <{name}>");
                        }

                        SkipWhitespace();
                        Expect(">");
                        node.End = _pos;
                        return node;
                    }

                    if (SkipComment() || SkipCData() || SkipProcessingInstruction())
                    {
                        continue;
                    }

                    var childId = (id.Length == 0 ? string.Empty : id + ".") + node.Children.Count.ToString(CultureInfo.InvariantCulture);
                    node.Children.Add(ParseElement(node, childId));
                }
            }

            private void SkipMisc()
            {
                while (true)
                {
                    SkipWhitespace();
                    if (!(SkipComment() || SkipProcessingInstruction()))
                    {
                        return;
                    }
                }
            }

            private bool SkipComment() => SkipDelimited("<!--", "-->");

            private bool SkipCData() => SkipDelimited("<![CDATA[", "]]>");

            private bool SkipProcessingInstruction() => SkipDelimited("<?", "?>");

            private bool SkipDelimited(string open, string close)
            {
                if (!StartsWith(open))
                {
                    return false;
                }

                var end = _text.IndexOf(close, _pos + open.Length, StringComparison.Ordinal);
                if (end < 0)
                {
                    throw Error($"unterminated {open}");
                }

                _pos = end + close.Length;
                return true;
            }

            private string ReadName()
            {
                var start = _pos;
                while (_pos < _text.Length && (char.IsLetterOrDigit(_text[_pos]) || _text[_pos] == '_' || _text[_pos] == ':' || _text[_pos] == '.' || _text[_pos] == '-'))
                {
                    _pos++;
                }

                if (_pos == start || !char.IsLetter(_text[start]) && _text[start] != '_')
                {
                    throw Error("a name is expected");
                }

                return _text.Substring(start, _pos - start);
            }

            private void SkipWhitespace()
            {
                while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos]))
                {
                    _pos++;
                }
            }

            private bool StartsWith(string value) => string.CompareOrdinal(_text, _pos, value, 0, value.Length) == 0;

            private void Expect(string value)
            {
                if (!StartsWith(value))
                {
                    throw Error($"'{value}' is expected");
                }

                _pos += value.Length;
            }

            private FormatException Error(string message)
            {
                var line = 1;
                var column = 1;
                for (var i = 0; i < Math.Min(_pos, _text.Length); i++)
                {
                    if (_text[i] == '\n')
                    {
                        line++;
                        column = 1;
                    }
                    else
                    {
                        column++;
                    }
                }

                return new FormatException($"({line},{column}): {message}");
            }
        }
    }

    /// <summary>An element of a <see cref="SpikeViewDocument"/>, with the offsets of its parts in the text.</summary>
    public sealed class SpikeNode
    {
        internal SpikeNode(SpikeNode? parent, string id, string name, int start, int nameEnd)
        {
            Parent = parent;
            Id = id;
            Name = name;
            Start = start;
            NameEnd = nameEnd;
        }

        public SpikeNode? Parent { get; }

        /// <summary>The desktop element id: dot path of element-child ordinals, <c>""</c> for the root.</summary>
        public string Id { get; }

        public string Name { get; }

        /// <summary>Offset of the start tag's <c>&lt;</c>.</summary>
        public int Start { get; }

        /// <summary>Offset just after the element name in the start tag.</summary>
        public int NameEnd { get; }

        /// <summary>Offset just after the start tag's <c>&gt;</c> (or <c>/&gt;</c>).</summary>
        public int StartTagEnd { get; internal set; }

        /// <summary>Offset of the end tag's <c>&lt;</c> (= <see cref="End"/> for a self-closing element).</summary>
        public int EndTagStart { get; internal set; }

        /// <summary>Offset just after the element.</summary>
        public int End { get; internal set; }

        public bool SelfClosing { get; internal set; }

        public List<SpikeAttribute> Attributes { get; } = new List<SpikeAttribute>();

        public List<SpikeNode> Children { get; } = new List<SpikeNode>();

        /// <summary>The value of attribute <paramref name="name"/>, or null when absent.</summary>
        public string? Attribute(string name) => Attributes.FirstOrDefault(a => a.Name == name)?.Value;
    }

    /// <summary>An attribute of a <see cref="SpikeNode"/>.</summary>
    public sealed class SpikeAttribute
    {
        internal SpikeAttribute(string name, char quote, int start, int valueStart, int valueEnd, int end, string value)
        {
            Name = name;
            Quote = quote;
            Start = start;
            ValueStart = valueStart;
            ValueEnd = valueEnd;
            End = end;
            Value = value;
        }

        public string Name { get; }

        public char Quote { get; }

        public int Start { get; }

        /// <summary>Offset of the value's first character (just after the opening quote).</summary>
        public int ValueStart { get; }

        /// <summary>Offset of the closing quote.</summary>
        public int ValueEnd { get; }

        /// <summary>Offset just after the closing quote.</summary>
        public int End { get; }

        /// <summary>The value with its character references resolved.</summary>
        public string Value { get; }
    }
}
