using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Kubuno.VisualStudio.Designer.Registry;
using Kubuno.VisualStudio.Designer.Selection;
using Kubuno.VisualStudio.Designer.Toolbox;

namespace Kubuno.VisualStudio.Designer.Editing
{
    /// <summary>Where a pasted or duplicated fragment goes: a container and an index among its children.</summary>
    public sealed class StructurePlacement
    {
        public StructurePlacement(string parentId, int index)
        {
            ParentId = parentId;
            Index = index;
        }

        /// <summary>The container's stable id (<c>""</c> = the root element).</summary>
        public string ParentId { get; }

        public int Index { get; }

        /// <summary>The new element's own stable id once inserted.</summary>
        public string NewElementId => StableElementId.Child(ParentId, Index);
    }

    /// <summary>
    /// The registry rules behind the designer's structure gestures (docs/DESIGNER.md §12's context menus):
    /// where a paste or a duplicate lands, whether a wrap or an unwrap is allowed, and the reorder
    /// indices of "Bring to Front"/"Send to Back". Every acceptance check is the rule a Toolbox drop already
    /// uses (<see cref="ToolboxInsertionPlanner.CanDrop"/>, the port of <c>design::can_drop_component</c>):
    /// a gesture the registry's allowed children forbid is refused here, before any edit is requested.
    /// Pure: reads the current text with <see cref="ElementAttributeReader"/>.
    /// </summary>
    public static class DesignerStructurePlanner
    {
        /// <summary>The containers "Wrap in" offers, in menu order (only those the registry knows are shown).</summary>
        public static readonly IReadOnlyList<string> WrapContainers = new[] { "Stack", "Panel", "Card", "GroupBox", "ScrollArea" };

        /// <summary>
        /// A paste onto <paramref name="targetId"/> (null or <c>""</c> = the view's root): into the target itself
        /// when it accepts <paramref name="fragmentTag"/> (appended), else next to it in its parent, like WinForms.
        /// Null when neither accepts it.
        /// </summary>
        public static StructurePlacement? PlanPaste(string text, string? targetId, string fragmentTag, ComponentRegistry registry)
        {
            var id = targetId ?? StableElementId.Root;
            if (registry.Find(fragmentTag) is null || ElementAttributeReader.Read(text, id) is not { } target)
            {
                return null;
            }

            if (registry.Find(target.TagName) is { } container && ToolboxInsertionPlanner.CanDrop(container, target.ChildTagNames.Count, fragmentTag, registry))
            {
                return new StructurePlacement(id, target.ChildTagNames.Count);
            }

            return PlanNextTo(text, id, fragmentTag, registry);
        }

        /// <summary>
        /// A paste of several elements (a multi-selection copied together, docs/DESIGNER.md §13) onto
        /// <paramref name="targetId"/>: into the target when it accepts every one of <paramref name="tags"/> (appended,
        /// in order), else next to it in its parent. Null when neither accepts them all.
        /// </summary>
        public static StructurePlacement? PlanPasteMany(string text, string? targetId, IReadOnlyList<string> tags, ComponentRegistry registry)
        {
            if (tags is null || tags.Count == 0)
            {
                return null;
            }

            if (tags.Count == 1)
            {
                return PlanPaste(text, targetId, tags[0], registry);
            }

            var id = targetId ?? StableElementId.Root;
            if (tags.Any(t => registry.Find(t) is null) || ElementAttributeReader.Read(text, id) is not { } target)
            {
                return null;
            }

            if (AcceptsAll(target, tags, registry))
            {
                return new StructurePlacement(id, target.ChildTagNames.Count);
            }

            return TrySplit(id, out var parentId, out var index) && ElementAttributeReader.Read(text, parentId) is { } parent && AcceptsAll(parent, tags, registry)
                ? new StructurePlacement(parentId, index + 1)
                : null;
        }

        /// <summary>
        /// Where "Duplicate" puts the copies of a multi-selection (docs/DESIGNER.md §13): all of them, in document
        /// order, right after the last selected element of the primary's container - like a WinForms paste next to
        /// the selection. Null when that container does not accept them all, or for the root.
        /// </summary>
        public static StructurePlacement? PlanDuplicateMany(string text, IReadOnlyList<string> elementIds, string primaryId, ComponentRegistry registry)
        {
            var top = MultiSelection.TopLevel(elementIds ?? Array.Empty<string>());
            if (top.Count == 0 || !TrySplit(primaryId, out var parentId, out _) || ElementAttributeReader.Read(text, parentId) is not { } parent)
            {
                return null;
            }

            var tags = top.Select(id => ElementAttributeReader.Read(text, id)?.TagName ?? string.Empty).ToList();
            if (tags.Any(t => registry.Find(t) is null) || !AcceptsAll(parent, tags, registry))
            {
                return null;
            }

            var last = top.Select(id => TrySplit(id, out var p, out var i) && p == parentId ? i : -1).Max();
            return new StructurePlacement(parentId, (last < 0 ? parent.ChildTagNames.Count - 1 : last) + 1);
        }

        /// <summary>Whether <paramref name="container"/> accepts all of <paramref name="tags"/> added to its current children.</summary>
        private static bool AcceptsAll(ElementAttributes container, IReadOnlyList<string> tags, ComponentRegistry registry)
        {
            if (registry.Find(container.TagName) is not { } meta)
            {
                return false;
            }

            var count = container.ChildTagNames.Count;
            foreach (var tag in tags)
            {
                if (!ToolboxInsertionPlanner.CanDrop(meta, count, tag, registry))
                {
                    return false;
                }

                count++;
            }

            return true;
        }

        /// <summary>A copy of <paramref name="elementId"/> right after it, in the same container; null for the root or a forbidden duplicate.</summary>
        public static StructurePlacement? PlanDuplicate(string text, string elementId, ComponentRegistry registry) =>
            ElementAttributeReader.Read(text, elementId) is { } element ? PlanNextTo(text, elementId, element.TagName, registry) : null;

        private static StructurePlacement? PlanNextTo(string text, string elementId, string tag, ComponentRegistry registry)
        {
            if (!TrySplit(elementId, out var parentId, out var index) ||
                ElementAttributeReader.Read(text, parentId) is not { } parent ||
                registry.Find(parent.TagName) is not { } container ||
                !ToolboxInsertionPlanner.CanDrop(container, parent.ChildTagNames.Count, tag, registry))
            {
                return null;
            }

            return new StructurePlacement(parentId, index + 1);
        }

        /// <summary>Whether <paramref name="elementId"/> can be wrapped in a new <paramref name="wrapper"/>: the wrapper takes it as its child, and its container takes the wrapper in its place.</summary>
        public static bool CanWrap(string text, string elementId, string wrapper, ComponentRegistry registry)
        {
            if (registry.Find(wrapper) is not { } wrapperMeta || ElementAttributeReader.Read(text, elementId) is not { } element ||
                !ToolboxInsertionPlanner.CanDrop(wrapperMeta, 0, element.TagName, registry))
            {
                return false;
            }

            return !TrySplit(elementId, out var parentId, out _) || AcceptsInPlace(text, parentId, wrapper, registry);
        }

        /// <summary>Whether the container <paramref name="elementId"/> can be replaced by its children (the root only with exactly one child).</summary>
        public static bool CanUnwrap(string text, string elementId, ComponentRegistry registry)
        {
            if (ElementAttributeReader.Read(text, elementId) is not { } element || element.ChildTagNames.Count == 0)
            {
                return false;
            }

            if (!TrySplit(elementId, out var parentId, out _))
            {
                return element.ChildTagNames.Count == 1;
            }

            if (ElementAttributeReader.Read(text, parentId) is not { } parent || registry.Find(parent.TagName) is not { } container)
            {
                return false;
            }

            if (container.Children == ChildrenModel.SingleWidget)
            {
                return element.ChildTagNames.Count == 1;
            }

            return element.ChildTagNames.All(child => AcceptsInPlace(text, parentId, child, registry));
        }

        /// <summary>The index "Bring to Front" moves the element to (the last one: later siblings paint on top), or null when it is already there or is the root.</summary>
        public static int? BringToFrontIndex(string text, string elementId) =>
            TrySplit(elementId, out var parentId, out var index) && ElementAttributeReader.Read(text, parentId) is { } parent &&
            index < parent.ChildTagNames.Count - 1 ? parent.ChildTagNames.Count - 1 : (int?)null;

        /// <summary>The index "Send to Back" moves the element to (the first one), or null when it is already there or is the root.</summary>
        public static int? SendToBackIndex(string text, string elementId) =>
            TrySplit(elementId, out _, out var index) && index > 0 ? 0 : (int?)null;

        /// <summary>The element's containers, nearest first, each with its menu label (<c>name (Tag)</c> or <c>Tag</c>).</summary>
        public static IReadOnlyList<(string Id, string Label)> Ancestors(string text, string elementId)
        {
            var result = new List<(string, string)>();
            var id = elementId;
            while (TrySplit(id, out var parentId, out _))
            {
                if (ElementAttributeReader.Read(text, parentId) is { } parent)
                {
                    result.Add((parentId, Label(parent)));
                }

                id = parentId;
            }

            return result;
        }

        /// <summary><c>name (Tag)</c> for an element with an <c>x:Name</c>, else <c>Tag</c>.</summary>
        public static string Label(ElementAttributes element) =>
            element.Attributes.TryGetValue("x:Name", out var name) && !string.IsNullOrEmpty(name) ? $"{name} ({element.TagName})" : element.TagName;

        /// <summary>Whether <paramref name="parentId"/>'s element accepts <paramref name="child"/> in the place of one of its current children (its child count unchanged).</summary>
        private static bool AcceptsInPlace(string text, string parentId, string child, ComponentRegistry registry)
        {
            if (ElementAttributeReader.Read(text, parentId) is not { } parent || registry.Find(parent.TagName) is not { } container)
            {
                return false;
            }

            return container.Children == ChildrenModel.SingleWidget || ToolboxInsertionPlanner.CanDrop(container, 0, child, registry);
        }

        /// <summary>Splits <c>"2.0.3"</c> into <c>("2.0", 3)</c>; false for the root (<c>""</c>) or a malformed id.</summary>
        public static bool TrySplit(string elementId, out string parentId, out int index)
        {
            parentId = string.Empty;
            index = 0;
            if (string.IsNullOrEmpty(elementId))
            {
                return false;
            }

            var dot = elementId.LastIndexOf('.');
            parentId = dot < 0 ? string.Empty : elementId.Substring(0, dot);
            return int.TryParse(dot < 0 ? elementId : elementId.Substring(dot + 1), NumberStyles.None, CultureInfo.InvariantCulture, out index);
        }
    }
}
