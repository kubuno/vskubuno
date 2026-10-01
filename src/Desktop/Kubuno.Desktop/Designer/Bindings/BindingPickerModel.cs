using System;
using System.Collections.Generic;
using System.Linq;
using Kubuno.Desktop.Designer.Registry;

namespace Kubuno.Desktop.Designer.Bindings
{
    /// <summary>Which shapes fit which properties (mirrors <c>binding_lsp::fits</c> of the language server).</summary>
    public static class BindingShapes
    {
        /// <summary>The shape a property wants: from its registry kind and editor; null when unknown (anything).</summary>
        public static BindingShape? Of(PropertyMeta? property, string? attributeName = null, PropKind? kind = null)
        {
            if (attributeName == "ItemsSource")
            {
                return BindingShape.List;
            }

            switch (property?.Editor)
            {
                case "list":
                    return BindingShape.List;
                case "object":
                    return BindingShape.Object;
            }

            return (property?.Kind ?? kind)?.Tag switch
            {
                PropKindTag.Bool => BindingShape.Bool,
                PropKindTag.F32 => BindingShape.Number,
                PropKindTag.String or PropKindTag.Enum => BindingShape.Text,
                _ => null,
            };
        }

        /// <summary>Whether <paramref name="have"/> fits <paramref name="want"/>: true, false (never), or null (only when the text converts).</summary>
        public static bool? Fits(BindingShape have, BindingShape? want)
        {
            if (want is not { } w || have == BindingShape.Any || w == BindingShape.Any || have == w)
            {
                return true;
            }

            if (have is BindingShape.List or BindingShape.Object || w is BindingShape.List or BindingShape.Object)
            {
                return false;
            }

            return have == BindingShape.Text && w is BindingShape.Bool or BindingShape.Number ? null : true;
        }
    }

    /// <summary>One entry of the binding picker's tree.</summary>
    public sealed class BindingPickerNode
    {
        public BindingPickerNode(BindingMember member, string label, bool? fits, bool isCurrent, IReadOnlyList<BindingPickerNode> children)
        {
            Member = member;
            Label = label;
            Fits = fits;
            IsCurrent = isCurrent;
            Children = children;
        }

        public BindingMember Member { get; }

        public string Label { get; }

        /// <summary>true: fits the property; null: only when the text converts; false: does not fit (greyed).</summary>
        public bool? Fits { get; }

        /// <summary>Whether the property is bound to it now.</summary>
        public bool IsCurrent { get; }

        public IReadOnlyList<BindingPickerNode> Children { get; }

        /// <summary>Whether it is offered as fitting (not greyed).</summary>
        public bool Compatible => Fits != false;
    }

    /// <summary>A group of the picker: <c>item</c> (the template's row), <c>context</c>, <c>sources</c>, <c>resources</c>.</summary>
    public sealed class BindingPickerGroup
    {
        public BindingPickerGroup(string id, string label, IReadOnlyList<BindingPickerNode> nodes)
        {
            Id = id;
            Label = label;
            Nodes = nodes;
        }

        public string Id { get; }

        /// <summary>What answers (the type of the data context, the template's list), shown after the group's title.</summary>
        public string Label { get; }

        public IReadOnlyList<BindingPickerNode> Nodes { get; }
    }

    /// <summary>
    /// The binding picker of the Properties window (docs/DESIGNER.md, "Data bindings"): the tree of what a property can be
    /// bound to - the row of the template, the view's data context, the data components and their members, the resources -
    /// members that fit the property first, the others greyed; and the attribute value a pick writes (the other options of
    /// the current binding - mode, converter, format… - are kept). Pure.
    /// </summary>
    public static class BindingPickerModel
    {
        /// <summary>The groups for a property of shape <paramref name="want"/> bound to <paramref name="currentRaw"/>, filtered by <paramref name="filter"/> (case-insensitive, on the name).</summary>
        public static IReadOnlyList<BindingPickerGroup> Build(BindingSourceSchema schema, BindingShape? want, string? currentRaw, string? filter = null, bool includeResources = true)
        {
            var current = CurrentPath(currentRaw);
            var groups = new List<BindingPickerGroup>();
            void Add(string id, string label, IEnumerable<BindingPickerNode> nodes)
            {
                var list = Order(nodes.Where(n => Matches(n, filter))).ToList();
                if (list.Count > 0)
                {
                    groups.Add(new BindingPickerGroup(id, label, list));
                }
            }

            if (schema.Item is { } item)
            {
                Add("item", item.Label, item.Members.Select(m => Node(m, m.Path, want, current)));
            }

            Add("context", schema.Context.Label, schema.Context.Members.Select(m => Node(m, m.Path, want, current)));
            Add("sources", string.Empty, schema.Components.Select(c =>
            {
                var children = Order(c.Children.Select(m => Node(m, m.Name, want, current))).ToList();
                var own = BindingShapes.Fits(c.Shape, want);
                var fits = own == false && children.Any(n => n.Compatible) ? null : own;
                return new BindingPickerNode(c, c.Name, fits, current == c.Path, children);
            }));
            if (includeResources && want is null or BindingShape.Text)
            {
                var key = BindingMarkup.ResourceKey(currentRaw);
                Add("resources", string.Empty, schema.Resources.Select(r => new BindingPickerNode(r, r.Name, true, key == r.Name, Array.Empty<BindingPickerNode>())));
            }

            return groups;
        }

        private static BindingPickerNode Node(BindingMember m, string label, BindingShape? want, string? current) =>
            new BindingPickerNode(m, label, BindingShapes.Fits(m.Shape, want), current == m.Path, Array.Empty<BindingPickerNode>());

        /// <summary>Fitting first (in their declared order), then the text ones that convert, then the others.</summary>
        private static IEnumerable<BindingPickerNode> Order(IEnumerable<BindingPickerNode> nodes) =>
            nodes.Select((n, i) => (n, i)).OrderBy(x => x.n.Fits == true ? 0 : x.n.Fits is null ? 1 : 2).ThenBy(x => x.i).Select(x => x.n);

        private static bool Matches(BindingPickerNode n, string? filter) =>
            string.IsNullOrWhiteSpace(filter)
            || n.Label.IndexOf(filter!.Trim(), StringComparison.OrdinalIgnoreCase) >= 0
            || n.Children.Any(c => Matches(c, filter));

        /// <summary>The full path <paramref name="raw"/> binds, or null.</summary>
        public static string? CurrentPath(string? raw) => BindingMarkup.TryParse(raw, out var m) ? m!.FullPath : null;

        /// <summary>
        /// The attribute value that binds the property to <paramref name="member"/>: the current binding pointed at it (its
        /// other options kept), or the member's own expression when the property is not bound (or for a resource).
        /// </summary>
        public static string Apply(string? currentRaw, BindingMember member)
        {
            if (member.Kind == "resource" || !BindingMarkup.TryParse(currentRaw, out var markup))
            {
                return member.Expression;
            }

            switch (member.Kind)
            {
                case "component":
                    return markup!.Retarget(member.Path, null).ToString();
                case "column":
                case "state":
                    var dot = member.Path.IndexOf('.');
                    return dot > 0 ? markup!.Retarget(member.Path.Substring(0, dot), member.Path.Substring(dot + 1)).ToString() : member.Expression;
                default:
                    return markup!.Retarget(null, member.Path).ToString();
            }
        }
    }
}
