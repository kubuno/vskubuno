using System;
using System.Globalization;
using System.Linq;
using Kubuno.Views.Designer.Registry;
using Kubuno.Views.Designer.Selection;

namespace Kubuno.Views.Designer.Toolbox
{
    /// <summary>Where a Toolbox double-click inserts a component - see <see cref="ToolboxInsertionPlanner.Plan"/>.</summary>
    public sealed class ToolboxInsertion
    {
        public ToolboxInsertion(string parentId, int index, string xml)
        {
            ParentId = parentId;
            Index = index;
            Xml = xml;
        }

        /// <summary>The container's stable id (<c>""</c> = the root element).</summary>
        public string ParentId { get; }

        /// <summary>Insertion index among the container's children (always "append").</summary>
        public int Index { get; }

        /// <summary>The <c>.kbview</c> fragment to insert (<c>kubuno/applyEdit</c>'s <c>insertChild</c>).</summary>
        public string Xml { get; }

        /// <summary>The new element's own stable id once inserted.</summary>
        public string NewElementId => ParentId.Length == 0 ? Index.ToString(CultureInfo.InvariantCulture) : ParentId + "." + Index.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A double-click on a Toolbox item (<c>IVsToolboxUser.ItemPicked</c>) inserts the component "into the
    /// selected container", like WinForms: the selected element itself when it can take the component as
    /// a child, otherwise its nearest ancestor that can, appended at the end. The acceptance rule is a port
    /// of <c>kubuno_desktop_views::design::can_drop_component</c> (the rule a drag-and-drop on the surface already
    /// uses, DSG-9) and the skeleton mirrors its <c>skeleton_xml</c> (<c>X/Y/Width/Height/Anchor</c> only inside a
    /// <c>DockAnchor</c> container). Pure: reads the current text with <see cref="ElementAttributeReader"/>.
    /// </summary>
    public static class ToolboxInsertionPlanner
    {
        public static ToolboxInsertion? Plan(string documentText, string? selectedElementId, string component, ComponentRegistry registry)
        {
            if (documentText is null || registry is null || registry.Find(component) is null)
            {
                return null;
            }

            var candidate = selectedElementId ?? string.Empty;
            while (true)
            {
                var attributes = ElementAttributeReader.Read(documentText, candidate);
                if (attributes is not null && registry.Find(attributes.TagName) is { } container &&
                    CanDrop(container, attributes.ChildTagNames.Count, component, registry))
                {
                    var index = attributes.ChildTagNames.Count;
                    return new ToolboxInsertion(candidate, index, SkeletonXml(component, container.LayoutKind, index, registry));
                }

                if (candidate.Length == 0)
                {
                    return null;
                }

                var dot = candidate.LastIndexOf('.');
                candidate = dot < 0 ? string.Empty : candidate.Substring(0, dot);
            }
        }

        /// <summary>Port of <c>design.rs</c>'s <c>can_drop_component</c>.</summary>
        public static bool CanDrop(ComponentMeta container, int existingChildren, string component, ComponentRegistry registry)
        {
            switch (container.Children)
            {
                case ChildrenModel.None:
                    return false;
                case ChildrenModel.SingleWidget:
                    return existingChildren == 0;
                default:
                    var gatedAnywhere = registry.Components.Any(c => c.Children == ChildrenModel.List && c.AllowedChildren.Contains(component));
                    return !gatedAnywhere || container.AllowedChildren.Contains(component);
            }
        }

        /// <summary>
        /// <c>&lt;Button/&gt;</c>, or, in a <c>DockAnchor</c> container, placed like a Windows Forms control: a default
        /// <c>X/Y</c> (cascaded so successive insertions do not overlap exactly), the component's
        /// <see cref="DefaultSize"/> and WinForms' default anchoring, <c>Anchor="Top, Left"</c>.
        /// </summary>
        public static string SkeletonXml(string component, LayoutKind? containerLayout, int index, ComponentRegistry? registry = null)
        {
            if (containerLayout == LayoutKind.DockAnchor)
            {
                var offset = 8 + (24 * (index % 10));
                var (width, height) = DefaultSize(component, registry);
                return FormattableString.Invariant($"<{component} X=\"{offset}\" Y=\"{offset}\" Width=\"{width}\" Height=\"{height}\" Anchor=\"{DefaultAnchor}\"/>");
            }

            return $"<{component}/>";
        }

        /// <summary>The anchoring every element placed in a <c>DockAnchor</c> container gets (WinForms' <c>Top | Left</c>).</summary>
        public const string DefaultAnchor = "Top, Left";

        /// <summary>
        /// The size a new element gets in a <c>DockAnchor</c> container, DIP - the port of
        /// <c>kubuno_desktop_views::design::default_drop_size</c> (a drag-and-drop on the surface), like a WinForms
        /// control's <c>DefaultSize</c>.
        /// </summary>
        public static (int Width, int Height) DefaultSize(string component, ComponentRegistry? registry = null)
        {
            switch (component)
            {
                case "Button":
                case "IconButton":
                    return (100, 36);
                case "TextField":
                case "SearchField":
                case "MaskedField":
                case "NumericField":
                case "ColorField":
                case "GradientField":
                case "DatePicker":
                case "ComboBox":
                case "Dropdown":
                    return (200, 36);
                case "Label":
                case "LinkLabel":
                case "Badge":
                    return (100, 24);
                case "CheckBox":
                case "RadioButton":
                case "Switch":
                    return (140, 24);
                case "Slider":
                case "ProgressBar":
                    return (200, 24);
                case "Separator":
                    return (200, 8);
                case "Icon":
                case "Spinner":
                    return (24, 24);
                case "TextArea":
                case "ListBox":
                case "CheckedListBox":
                case "ListView":
                case "TreeView":
                case "DataTable":
                    return (200, 120);
                case "MonthCalendar":
                    return (280, 300);
                default:
                    if (registry?.Find(component) is { DesignSize: { Length: 2 } size } && size[0] > 0 && size[1] > 0)
                    {
                        // A user control: the size it was designed at.
                        return ((int)Math.Round(size[0]), (int)Math.Round(size[1]));
                    }

                    return registry?.Find(component) is { } meta && meta.Children != ChildrenModel.None ? (200, 100) : (120, 36);
            }
        }
    }
}
