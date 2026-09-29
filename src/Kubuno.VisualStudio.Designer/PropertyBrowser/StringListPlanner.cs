using System;
using System.Collections.Generic;
using System.Linq;
using Kubuno.VisualStudio.Designer.Registry;
using Kubuno.VisualStudio.Designer.Selection;

namespace Kubuno.VisualStudio.Designer.PropertyBrowser
{
    /// <summary>
    /// The "Items" string list of a list control (<c>ListBox</c>, <c>CheckedListBox</c>, <c>ComboBox</c>, <c>Dropdown</c>): its
    /// <c>Item</c>/<c>Option</c> children as one line each, like WinForms' string collection editor. A line equal to an
    /// existing child's text keeps that child as written (its other attributes too); any other line becomes a new
    /// child. Pure.
    /// </summary>
    public static class StringListPlanner
    {
        /// <summary>The attribute holding the shown text of a <paramref name="child"/> element: <c>Text</c>, else <c>Label</c>, else its first text property.</summary>
        public static string LabelAttribute(ComponentMeta? child) =>
            child?.Properties.FirstOrDefault(p => p.Name == "Text")?.Name
            ?? child?.Properties.FirstOrDefault(p => p.Name == "Label")?.Name
            ?? child?.Properties.FirstOrDefault(p => p.Kind.Tag == PropKindTag.String && p.InheritedFrom is null)?.Name
            ?? "Text";

        /// <summary>The attribute a new child also gets the line as its value (<c>Value</c> for an <c>Option</c>), or null.</summary>
        public static string? ValueAttribute(ComponentMeta? child) => child?.Properties.Any(p => p.Name == "Value" && p.InheritedFrom is null) == true ? "Value" : null;

        /// <summary>The text shown for an existing child (its label, else its value).</summary>
        public static string TextOf(ViewNode child, string labelAttribute, string? valueAttribute) =>
            child.Attribute(labelAttribute) ?? (valueAttribute is null ? null : child.Attribute(valueAttribute)) ?? string.Empty;

        /// <summary>The lines of the children of <paramref name="parentId"/> named <paramref name="childTag"/>.</summary>
        public static IReadOnlyList<string> Lines(string text, string parentId, string childTag, string labelAttribute, string? valueAttribute) =>
            ViewDocument.Find(ViewDocument.Parse(text), parentId)?.Children.Where(c => c.Name == childTag).Select(c => TextOf(c, labelAttribute, valueAttribute)).ToList()
            ?? (IReadOnlyList<string>)Array.Empty<string>();

        /// <summary>The collection <paramref name="lines"/> describe (empty lines dropped).</summary>
        public static IReadOnlyList<CollectionMember> Members(IReadOnlyList<string> originals, IEnumerable<string> lines, string labelAttribute, string? valueAttribute)
        {
            var used = new bool[originals.Count];
            var members = new List<CollectionMember>();
            foreach (var raw in lines)
            {
                var line = (raw ?? string.Empty).TrimEnd('\r');
                if (line.Trim().Length == 0)
                {
                    continue;
                }

                var index = Enumerable.Range(0, originals.Count).FirstOrDefault(i => !used[i] && string.Equals(originals[i], line, StringComparison.Ordinal), -1);
                if (index >= 0)
                {
                    used[index] = true;
                    members.Add(new CollectionMember(index));
                    continue;
                }

                var attributes = new List<KeyValuePair<string, string?>>();
                if (valueAttribute is not null)
                {
                    attributes.Add(new KeyValuePair<string, string?>(valueAttribute, line));
                }

                attributes.Add(new KeyValuePair<string, string?>(labelAttribute, line));
                members.Add(new CollectionMember(null, attributes));
            }

            return members;
        }

        /// <summary>The edits that make the children of <paramref name="parentId"/> the given <paramref name="lines"/>.</summary>
        public static IReadOnlyList<TextReplacement> Plan(string text, string parentId, string childTag, ComponentMeta? child, IEnumerable<string> lines)
        {
            var label = LabelAttribute(child);
            var value = ValueAttribute(child);
            var originals = Lines(text, parentId, childTag, label, value);
            return ChildCollectionPlanner.Plan(text, parentId, childTag, Members(originals, lines, label, value));
        }

        private static int FirstOrDefault(this IEnumerable<int> source, Func<int, bool> predicate, int fallback)
        {
            foreach (var i in source)
            {
                if (predicate(i))
                {
                    return i;
                }
            }

            return fallback;
        }
    }
}
