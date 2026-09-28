using System;
using System.Collections.Generic;
using System.Linq;
using Kubuno.VisualStudio.Designer.Registry;
using Kubuno.VisualStudio.Designer.Selection;

namespace Kubuno.VisualStudio.Designer.Editing
{
    /// <summary>
    /// The Windows Forms designer's Layout toolbar / Format menu commands (docs/DESIGNER.md §13). The first
    /// nineteen are computed by the design surface from its painted layout (<c>format {command}</c> →
    /// <c>kubuno_views::design::format_ops</c>); Bring to Front / Send to Back reorder the XML
    /// (<c>reorderChildren</c>). Each is one undo unit.
    /// </summary>
    public enum DesignerLayoutCommand
    {
        AlignLefts,
        AlignCenters,
        AlignRights,
        AlignTops,
        AlignMiddles,
        AlignBottoms,
        MakeSameWidth,
        MakeSameHeight,
        MakeSameSize,
        HorizontalSpacingEqual,
        HorizontalSpacingIncrease,
        HorizontalSpacingDecrease,
        HorizontalSpacingRemove,
        VerticalSpacingEqual,
        VerticalSpacingIncrease,
        VerticalSpacingDecrease,
        VerticalSpacingRemove,
        CenterHorizontally,
        CenterVertically,
        BringToFront,
        SendToBack,
    }

    /// <summary>
    /// Maps each <see cref="DesignerLayoutCommand"/> to Visual Studio's own standard command (<c>guidVSStd97</c>,
    /// the very commands of the Windows Forms Layout toolbar, with VS's own icons and localized names) and to
    /// the surface protocol's <c>format</c> command name.
    /// </summary>
    public static class DesignerLayoutCommands
    {
        // guidVSStd97 command ids (stdidcmd.h), spelled out so this type stays unit-testable without VSConstants.
        private static readonly (DesignerLayoutCommand Command, uint Id)[] Standard =
        {
            (DesignerLayoutCommand.AlignBottoms, 1),
            (DesignerLayoutCommand.AlignCenters, 2),        // cmdidAlignHorizontalCenters
            (DesignerLayoutCommand.AlignLefts, 3),
            (DesignerLayoutCommand.AlignRights, 4),
            (DesignerLayoutCommand.AlignTops, 6),
            (DesignerLayoutCommand.AlignMiddles, 7),        // cmdidAlignVerticalCenters
            (DesignerLayoutCommand.BringToFront, 11),
            (DesignerLayoutCommand.CenterHorizontally, 12),
            (DesignerLayoutCommand.CenterVertically, 13),
            (DesignerLayoutCommand.HorizontalSpacingRemove, 21), // cmdidHorizSpaceConcatenate
            (DesignerLayoutCommand.HorizontalSpacingDecrease, 22),
            (DesignerLayoutCommand.HorizontalSpacingIncrease, 23),
            (DesignerLayoutCommand.HorizontalSpacingEqual, 24),
            (DesignerLayoutCommand.SendToBack, 33),
            (DesignerLayoutCommand.MakeSameSize, 35),       // cmdidSizeToControl
            (DesignerLayoutCommand.MakeSameHeight, 36),
            (DesignerLayoutCommand.MakeSameWidth, 37),
            (DesignerLayoutCommand.VerticalSpacingRemove, 46),
            (DesignerLayoutCommand.VerticalSpacingDecrease, 47),
            (DesignerLayoutCommand.VerticalSpacingIncrease, 48),
            (DesignerLayoutCommand.VerticalSpacingEqual, 49),
        };

        /// <summary>Every command, in toolbar order.</summary>
        public static IReadOnlyList<DesignerLayoutCommand> All { get; } = (DesignerLayoutCommand[])Enum.GetValues(typeof(DesignerLayoutCommand));

        /// <summary>The layout command a <c>guidVSStd97</c> command id stands for, if any.</summary>
        public static bool TryFromStandardCommand(uint commandId, out DesignerLayoutCommand command)
        {
            foreach (var (candidate, id) in Standard)
            {
                if (id == commandId)
                {
                    command = candidate;
                    return true;
                }
            }

            command = default;
            return false;
        }

        /// <summary>The <c>guidVSStd97</c> id of <paramref name="command"/>.</summary>
        public static uint StandardCommandId(DesignerLayoutCommand command) => Standard.First(s => s.Command == command).Id;

        /// <summary>The surface's <c>format</c> command name (<c>kubuno_views::design::FormatCommand</c>, camelCase), null for the z-order commands.</summary>
        public static string? FormatName(DesignerLayoutCommand command)
        {
            if (IsZOrder(command))
            {
                return null;
            }

            var name = command.ToString();
            return char.ToLowerInvariant(name[0]) + name.Substring(1);
        }

        public static bool IsZOrder(DesignerLayoutCommand command) => command is DesignerLayoutCommand.BringToFront or DesignerLayoutCommand.SendToBack;

        /// <summary>How many movable elements a command needs (mirrors <c>format_min_members</c>): 3 for "make spacing equal", 1 for centering and z-order, 2 otherwise.</summary>
        public static int MinimumMembers(DesignerLayoutCommand command) => command switch
        {
            DesignerLayoutCommand.HorizontalSpacingEqual or DesignerLayoutCommand.VerticalSpacingEqual => 3,
            DesignerLayoutCommand.CenterHorizontally or DesignerLayoutCommand.CenterVertically => 1,
            DesignerLayoutCommand.BringToFront or DesignerLayoutCommand.SendToBack => 1,
            _ => 2,
        };

        /// <summary>Whether the command aligns or sizes relative to the primary selection (which must then be one of the moved elements).</summary>
        public static bool NeedsPrimary(DesignerLayoutCommand command) =>
            command is >= DesignerLayoutCommand.AlignLefts and <= DesignerLayoutCommand.MakeSameSize;
    }

    /// <summary>
    /// What the Layout commands can do with the current selection (docs/DESIGNER.md §13) - pure, computed from
    /// the buffer text and the registry, so the toolbar, the pane and the context menu enable exactly the same
    /// commands. <b>Members</b> are the top-level selected elements (<see cref="MultiSelection.TopLevel"/>) placed
    /// by a Dock/Anchor container (<c>Panel</c>) and not docked: only those have an <c>X</c>/<c>Y</c> the
    /// surface can change. The z-order commands reorder the selected children of the primary's container.
    /// </summary>
    public sealed class LayoutSelectionInfo
    {
        private LayoutSelectionInfo(int memberCount, bool primaryIsMember, string? orderParentId, IReadOnlyList<int>? frontOrder, IReadOnlyList<int>? backOrder)
        {
            MemberCount = memberCount;
            PrimaryIsMember = primaryIsMember;
            OrderParentId = orderParentId;
            FrontOrder = frontOrder;
            BackOrder = backOrder;
        }

        public static LayoutSelectionInfo Empty { get; } = new LayoutSelectionInfo(0, false, null, null, null);

        /// <summary>How many selected elements the align/size/spacing/center commands move.</summary>
        public int MemberCount { get; }

        /// <summary>Whether the primary selection is one of them (the reference of align/size).</summary>
        public bool PrimaryIsMember { get; }

        /// <summary>The container whose children Bring to Front / Send to Back reorder (the primary's parent), or null.</summary>
        public string? OrderParentId { get; }

        /// <summary>The <c>reorderChildren</c> order of "Bring to Front" (the selected children last, painted on top), or null when it changes nothing.</summary>
        public IReadOnlyList<int>? FrontOrder { get; }

        /// <summary>The <c>reorderChildren</c> order of "Send to Back" (the selected children first), or null when it changes nothing.</summary>
        public IReadOnlyList<int>? BackOrder { get; }

        /// <summary>Whether <paramref name="command"/> would change something - the enabled state of its toolbar button and menu item.</summary>
        public bool IsEnabled(DesignerLayoutCommand command) => command switch
        {
            DesignerLayoutCommand.BringToFront => FrontOrder is not null,
            DesignerLayoutCommand.SendToBack => BackOrder is not null,
            _ => MemberCount >= DesignerLayoutCommands.MinimumMembers(command) && (PrimaryIsMember || !DesignerLayoutCommands.NeedsPrimary(command)),
        };

        /// <summary>Builds the info for <paramref name="selectedIds"/> (primary <paramref name="primaryId"/>) against the current <paramref name="text"/>.</summary>
        public static LayoutSelectionInfo Build(string text, IReadOnlyList<string> selectedIds, string? primaryId, ComponentRegistry registry)
        {
            if (text is null || selectedIds is null || registry is null)
            {
                return Empty;
            }

            var top = MultiSelection.TopLevel(selectedIds);
            var members = top.Where(id => IsMovable(text, id, registry)).ToList();
            var primaryIsMember = primaryId is not null && members.Contains(primaryId, StringComparer.Ordinal);

            string? orderParent = null;
            IReadOnlyList<int>? front = null;
            IReadOnlyList<int>? back = null;
            if (primaryId is { Length: > 0 } && DesignerStructurePlanner.TrySplit(primaryId, out var parentId, out _) &&
                ElementAttributeReader.Read(text, parentId) is { } parent)
            {
                var count = parent.ChildTagNames.Count;
                var selected = top
                    .Select(id => DesignerStructurePlanner.TrySplit(id, out var p, out var index) && p == parentId ? index : -1)
                    .Where(index => index >= 0 && index < count)
                    .Distinct()
                    .OrderBy(index => index)
                    .ToList();
                if (selected.Count > 0)
                {
                    orderParent = parentId;
                    var others = Enumerable.Range(0, count).Where(i => !selected.Contains(i)).ToList();
                    front = NonIdentity(others.Concat(selected).ToList());
                    back = NonIdentity(selected.Concat(others).ToList());
                }
            }

            return new LayoutSelectionInfo(members.Count, primaryIsMember, orderParent, front, back);
        }

        private static IReadOnlyList<int>? NonIdentity(List<int> order) => order.Select((value, index) => value == index).All(same => same) ? null : order;

        /// <summary>Whether <paramref name="id"/> is placed by a Dock/Anchor container and not docked.</summary>
        private static bool IsMovable(string text, string id, ComponentRegistry registry)
        {
            if (!DesignerStructurePlanner.TrySplit(id, out var parentId, out _) ||
                ElementAttributeReader.Read(text, parentId) is not { } parent ||
                registry.Find(parent.TagName)?.LayoutKind != LayoutKind.DockAnchor ||
                ElementAttributeReader.Read(text, id) is not { } element)
            {
                return false;
            }

            return !element.Attributes.TryGetValue("Dock", out var dock) || string.IsNullOrWhiteSpace(dock) || (dock ?? string.Empty).Trim() == "None";
        }
    }
}
