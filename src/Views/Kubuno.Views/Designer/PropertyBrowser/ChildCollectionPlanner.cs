using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Kubuno.Desktop.Designer.Selection;

namespace Kubuno.Desktop.Designer.PropertyBrowser
{
    /// <summary>A plain text replacement: <see cref="Length"/> characters from <see cref="Start"/> become <see cref="NewText"/>.</summary>
    public sealed class TextReplacement
    {
        public TextReplacement(int start, int length, string newText)
        {
            Start = start;
            Length = length;
            NewText = newText ?? string.Empty;
        }

        public int Start { get; }

        public int Length { get; }

        public string NewText { get; }

        public override string ToString() => $"[{Start},{Start + Length}) -> \"{NewText}\"";
    }

    /// <summary>One member of an edited child collection: an existing child (<see cref="OriginalIndex"/>, its index among the managed children) or a new one.</summary>
    public sealed class CollectionMember
    {
        public CollectionMember(int? originalIndex, IEnumerable<KeyValuePair<string, string?>>? attributes = null, string? tag = null)
        {
            OriginalIndex = originalIndex;
            Tag = tag;
            if (attributes is not null)
            {
                foreach (var pair in attributes)
                {
                    Attributes[pair.Key] = pair.Value;
                }
            }
        }

        /// <summary>The index of the existing child among the managed children, null for a new one.</summary>
        public int? OriginalIndex { get; }

        /// <summary>A new member's element tag in a polymorphic collection (null: the collection's first tag).</summary>
        public string? Tag { get; }

        /// <summary>
        /// The attributes to write, in order: for an existing child only the ones to change (null or empty removes it; the
        /// others are left as written); for a new one every attribute it gets.
        /// </summary>
        public Dictionary<string, string?> Attributes { get; } = new Dictionary<string, string?>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Plans the text edits that turn a container's children of one kind (the <c>Column</c>s of a <c>ListView</c>, the
    /// <c>TabItem</c>s of <c>Tabs</c>, the <c>Option</c>s of a <c>ComboBox</c>...) into an edited collection - what the
    /// collection and string-list editors apply as ONE undo unit. Untouched children keep their text byte for byte
    /// (their content included, a tab page's body); a changed one only gets its attributes edited in place. Pure.
    /// </summary>
    public static class ChildCollectionPlanner
    {
        /// <summary>
        /// The edits for <paramref name="members"/> (the new collection, in order) of the <paramref name="childTag"/> children of
        /// element <paramref name="parentId"/>; empty when nothing changes or the parent is not found. Non-overlapping, in
        /// ascending order.
        /// </summary>
        public static IReadOnlyList<TextReplacement> Plan(string text, string parentId, string childTag, IReadOnlyList<CollectionMember> members) =>
            Plan(text, parentId, new[] { childTag }, members);

        /// <summary>
        /// <see cref="Plan(string, string, string, IReadOnlyList{CollectionMember})"/> for a POLYMORPHIC collection (docs/RIBBON.md section 9): the
        /// children whose tag is one of <paramref name="childTags"/> (a ribbon group's buttons, toggles, galleries...) are the
        /// managed ones, in document order; a new member is written with its own <see cref="CollectionMember.Tag"/> (the first
        /// tag when it has none).
        /// </summary>
        public static IReadOnlyList<TextReplacement> Plan(string text, string parentId, IReadOnlyList<string> childTags, IReadOnlyList<CollectionMember> members)
        {
            var parent = ViewDocument.Find(ViewDocument.Parse(text), parentId);
            if (parent is null)
            {
                return Array.Empty<TextReplacement>();
            }

            var managed = parent.Children.Where(c => childTags.Contains(c.Name)).ToList();
            var edits = new List<TextReplacement>();
            var kept = members.Where(m => m.OriginalIndex is { } i && i >= 0 && i < managed.Count).ToList();
            var keptIndexes = kept.Select(m => m.OriginalIndex!.Value).ToList();
            var inOrder = keptIndexes.SequenceEqual(keptIndexes.OrderBy(i => i)) && keptIndexes.Distinct().Count() == keptIndexes.Count;
            var firstNew = members.ToList().FindIndex(m => m.OriginalIndex is null);
            var lastKept = members.ToList().FindLastIndex(m => m.OriginalIndex is not null);
            var newAtEnd = firstNew < 0 || firstNew > lastKept;
            var newline = text.Contains("\r\n") ? "\r\n" : "\n";

            if (managed.Count == 0)
            {
                if (members.Count == 0)
                {
                    return edits;
                }

                edits.Add(InsertInto(text, parent, members.Select(m => NewElement(m.Tag ?? childTags[0], m)).ToList(), newline));
                return edits;
            }

            if (inOrder && newAtEnd && kept.Count > 0)
            {
                // In place: remove what went, edit what changed, append what is new after the last kept child.
                foreach (var (child, index) in managed.Select((c, i) => (c, i)))
                {
                    var member = kept.FirstOrDefault(m => m.OriginalIndex == index);
                    if (member is null)
                    {
                        edits.Add(Removal(text, child));
                    }
                    else
                    {
                        edits.AddRange(AttributeEdits(text, child, member.Attributes));
                    }
                }

                var added = members.Where(m => m.OriginalIndex is null).Select(m => NewElement(m.Tag ?? childTags[0], m)).ToList();
                if (added.Count > 0)
                {
                    var anchor = managed[kept.Last().OriginalIndex!.Value];
                    var indent = LineIndent(text, anchor.Start);
                    edits.Add(new TextReplacement(anchor.End, 0, string.Concat(added.Select(a => newline + indent + a))));
                }

                return Normalize(edits);
            }

            // Reordered (or everything replaced): the managed children are rewritten as one block where the first one was.
            var first = managed[0];
            var last = managed[managed.Count - 1];
            var siblingsBefore = parent.Children.TakeWhile(c => c != first).ToList();
            var spanStart = siblingsBefore.Count > 0 ? siblingsBefore[siblingsBefore.Count - 1].End : parent.StartTagEnd;
            var blockIndent = LineIndent(text, first.Start);
            var items = new List<string>();
            foreach (var member in members)
            {
                if (member.OriginalIndex is { } i && i >= 0 && i < managed.Count)
                {
                    var child = managed[i];
                    var own = AttributeEdits(text, child, member.Attributes).Select(e => new TextReplacement(e.Start - child.Start, e.Length, e.NewText)).ToList();
                    items.Add(Apply(text.Substring(child.Start, child.End - child.Start), own));
                }
                else
                {
                    items.Add(NewElement(member.Tag ?? childTags[0], member));
                }
            }

            // Other children that sat between the managed ones keep their place after them.
            items.AddRange(parent.Children.Where(c => !childTags.Contains(c.Name) && c.Start > first.Start && c.End <= last.End).Select(c => text.Substring(c.Start, c.End - c.Start)));
            var block = string.Concat(items.Select(item => newline + blockIndent + item));
            var original = text.Substring(spanStart, last.End - spanStart);
            if (string.Equals(original, block, StringComparison.Ordinal))
            {
                return Array.Empty<TextReplacement>();
            }

            edits.Add(new TextReplacement(spanStart, last.End - spanStart, block));
            return edits;
        }

        /// <summary>Applies non-overlapping <paramref name="edits"/> to <paramref name="text"/>.</summary>
        public static string Apply(string text, IReadOnlyList<TextReplacement> edits)
        {
            var sb = new StringBuilder(text);
            foreach (var edit in edits.OrderByDescending(e => e.Start).ThenByDescending(e => e.Length))
            {
                sb.Remove(edit.Start, edit.Length);
                sb.Insert(edit.Start, edit.NewText);
            }

            return sb.ToString();
        }

        /// <summary>The attribute edits that give <paramref name="node"/> the values of <paramref name="changes"/> (null/empty removes), inside its start tag.</summary>
        public static List<TextReplacement> AttributeEdits(string text, ViewNode node, IReadOnlyDictionary<string, string?> changes)
        {
            var edits = new List<TextReplacement>();
            var added = new StringBuilder();
            foreach (var pair in changes)
            {
                var attribute = node.Attributes.FirstOrDefault(a => a.Name == pair.Key);
                var remove = string.IsNullOrEmpty(pair.Value);
                if (attribute is null)
                {
                    if (!remove)
                    {
                        added.Append(' ').Append(pair.Key).Append("=\"").Append(Escape(pair.Value!)).Append('"');
                    }
                }
                else if (remove)
                {
                    var start = attribute.Start;
                    while (start > 0 && char.IsWhiteSpace(text[start - 1]))
                    {
                        start--;
                    }

                    edits.Add(new TextReplacement(start, attribute.End - start, string.Empty));
                }
                else if (!string.Equals(attribute.Value, pair.Value, StringComparison.Ordinal))
                {
                    edits.Add(attribute.ValueStart >= 0
                        ? new TextReplacement(attribute.ValueStart, attribute.ValueEnd - attribute.ValueStart, Escape(pair.Value!))
                        : new TextReplacement(attribute.Start, attribute.End - attribute.Start, pair.Key + "=\"" + Escape(pair.Value!) + "\""));
                }
            }

            if (added.Length > 0)
            {
                var at = node.StartTagClose;
                while (at > node.Start && char.IsWhiteSpace(text[at - 1]))
                {
                    at--;
                }

                edits.Add(new TextReplacement(at, 0, added.ToString()));
            }

            return edits;
        }

        /// <summary>A new child's text: <c>&lt;Tag A="v"/&gt;</c> (empty values skipped).</summary>
        public static string NewElement(string tag, CollectionMember member)
        {
            var sb = new StringBuilder("<").Append(tag);
            foreach (var pair in member.Attributes.Where(p => !string.IsNullOrEmpty(p.Value)))
            {
                sb.Append(' ').Append(pair.Key).Append("=\"").Append(Escape(pair.Value!)).Append('"');
            }

            return sb.Append("/>").ToString();
        }

        /// <summary>An attribute value as written between double quotes (<c>&amp;</c>, <c>"</c>, <c>&lt;</c> and line breaks/tabs escaped, like the language server writes it).</summary>
        public static string Escape(string value) =>
            value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace("\r", "&#13;").Replace("\n", "&#10;").Replace("\t", "&#9;");

        private static TextReplacement Removal(string text, ViewNode child)
        {
            var start = child.Start;
            while (start > 0 && (text[start - 1] == ' ' || text[start - 1] == '\t'))
            {
                start--;
            }

            if (start > 0 && text[start - 1] == '\n')
            {
                start--;
                if (start > 0 && text[start - 1] == '\r')
                {
                    start--;
                }
            }

            return new TextReplacement(start, child.End - start, string.Empty);
        }

        private static TextReplacement InsertInto(string text, ViewNode parent, IReadOnlyList<string> elements, string newline)
        {
            var parentIndent = LineIndent(text, parent.Start);
            var indent = parent.Children.Count > 0 ? LineIndent(text, parent.Children[0].Start) : parentIndent + "  ";
            var items = string.Concat(elements.Select(e => newline + indent + e));
            if (parent.SelfClosing)
            {
                var start = parent.StartTagClose;
                while (start > parent.Start && char.IsWhiteSpace(text[start - 1]))
                {
                    start--;
                }

                return new TextReplacement(start, parent.End - start, ">" + items + newline + parentIndent + "</" + parent.Name + ">");
            }

            if (parent.Children.Count > 0)
            {
                return new TextReplacement(parent.Children[parent.Children.Count - 1].End, 0, items);
            }

            var inner = text.Substring(parent.StartTagEnd, Math.Max(0, parent.CloseTagStart - parent.StartTagEnd));
            return inner.IndexOf('\n') >= 0
                ? new TextReplacement(parent.StartTagEnd, 0, items)
                : new TextReplacement(parent.StartTagEnd, inner.Length, items + newline + parentIndent);
        }

        /// <summary>The whitespace that starts the line holding offset <paramref name="offset"/>.</summary>
        private static string LineIndent(string text, int offset)
        {
            var lineStart = offset;
            while (lineStart > 0 && text[lineStart - 1] != '\n' && text[lineStart - 1] != '\r')
            {
                lineStart--;
            }

            var end = lineStart;
            while (end < text.Length && (text[end] == ' ' || text[end] == '\t'))
            {
                end++;
            }

            return text.Substring(lineStart, end - lineStart);
        }

        /// <summary>Sorts <paramref name="edits"/> and folds an insertion into a replacement starting at the same offset (so no two edits share a position).</summary>
        private static IReadOnlyList<TextReplacement> Normalize(List<TextReplacement> edits)
        {
            var sorted = edits.OrderBy(e => e.Start).ThenBy(e => e.Length).ToList();
            var result = new List<TextReplacement>();
            foreach (var edit in sorted)
            {
                if (result.Count > 0 && result[result.Count - 1] is { } previous && previous.Length == 0 && previous.Start == edit.Start)
                {
                    result[result.Count - 1] = new TextReplacement(edit.Start, edit.Length, previous.NewText + edit.NewText);
                }
                else if (result.Count > 0 && result[result.Count - 1] is { } before && before.Start + before.Length == edit.Start && edit.Length == 0)
                {
                    result[result.Count - 1] = new TextReplacement(before.Start, before.Length, before.NewText + edit.NewText);
                }
                else
                {
                    result.Add(edit);
                }
            }

            return result;
        }
    }
}
