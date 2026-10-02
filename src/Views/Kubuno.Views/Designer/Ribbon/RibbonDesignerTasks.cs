using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Kubuno.Views.Designer.PropertyBrowser;
using Kubuno.Views.Designer.Registry;
using Kubuno.Views.Designer.Selection;

namespace Kubuno.Views.Designer.Ribbon
{
    /// <summary>
    /// What the designer offers on a ribbon element (docs/RIBBON.md section 9): the « Ajouter ▾ » choices (the "+" glyphs,
    /// the context menu's Add submenu, the smart tag), the polymorphic collections the Properties window edits (a group's
    /// Items, a tab's Groups, a menu button's DropDownItems...), and "Create Command from this button". Pure, unit-tested.
    /// </summary>
    public static class RibbonDesignerTasks
    {
        /// <summary>The registry family of the ribbon's elements.</summary>
        public const string Family = "ribbon";

        /// <summary>How many "Add" choices a menu lists (the size of its dynamic item range).</summary>
        public const int MaxChoices = 32;

        /// <summary>Children a ribbon holds at most once: offered only while absent, and outside the collections.</summary>
        private static readonly string[] Singletons = { "RibbonQuickAccessToolbar", "RibbonBackstage" };

        /// <summary>The elements a <c>Command</c> can be made from (they take a <c>Command</c> attribute).</summary>
        private static readonly HashSet<string> CommandSources = new HashSet<string>(StringComparer.Ordinal)
        {
            "RibbonButton", "RibbonToggleButton", "RibbonRadioButton", "RibbonSplitButton", "RibbonMenuButton", "RibbonCheckBox",
            "RibbonMenuItem", "RibbonSplitMenuItem", "BackstageButton",
        };

        /// <summary>What a new <c>Command</c> takes from the element it is made from (the element then reads them from it).</summary>
        private static readonly string[] CommandAttributes = { "Label", "SmallIcon", "LargeIcon", "ScreenTipTitle", "ScreenTipText" };

        /// <summary>Whether <paramref name="component"/> is a ribbon element (or <c>Ribbon</c> itself).</summary>
        public static bool IsRibbon(ComponentMeta? component) => string.Equals(component?.Family, Family, StringComparison.Ordinal);

        /// <summary>
        /// The element tags that can be added into element <paramref name="elementId"/> - a ribbon's tabs, a tab's groups, a
        /// group's controls... -, in registry order; empty for anything but a ribbon element that takes children.
        /// </summary>
        public static IReadOnlyList<string> AddChoices(string text, string elementId, ComponentRegistry registry)
        {
            var node = ViewDocument.Find(ViewDocument.Parse(text), elementId);
            var component = node is null ? null : registry.Find(node.Name);
            if (node is null || !IsRibbon(component))
            {
                return Array.Empty<string>();
            }

            return (component!.AllowedChildren ?? new List<string>())
                .Where(t => t.IndexOf('.') < 0 && registry.Find(t) is { Browsable: true })
                .Where(t => !Singletons.Contains(t) || !node.Children.Any(c => c.Name == t))
                .Distinct(StringComparer.Ordinal)
                .Take(MaxChoices)
                .ToList();
        }

        /// <summary>
        /// The ribbon element's children as ONE polymorphic Properties-window collection (a group's <c>Items</c> mixes
        /// buttons, toggles, galleries...; a ribbon's <c>Tabs</c> mixes tabs and contextual tab groups), or null when
        /// it has none (or only the <c>Option</c>s of a combo box, edited as a string list).
        /// </summary>
        public static RibbonCollection? CollectionOf(ComponentMeta component)
        {
            if (!IsRibbon(component))
            {
                return null;
            }

            var tags = (component.AllowedChildren ?? new List<string>())
                .Where(t => t.IndexOf('.') < 0 && !Singletons.Contains(t))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (tags.Count == 0 || (tags.Count == 1 && tags[0] is "Option" or "Item"))
            {
                return null;
            }

            return new RibbonCollection(RowName(component.Name), tags);
        }

        /// <summary>The name of a ribbon element's collection row, Office/WPF-style.</summary>
        public static string RowName(string tag) => tag switch
        {
            "Ribbon" or "RibbonContextualTabGroup" => "Tabs",
            "RibbonTab" => "Groups",
            "RibbonMenuButton" or "RibbonSplitButton" or "RibbonSplitMenuItem" or "RibbonMenuItem" or "RibbonColorPicker" => "DropDownItems",
            _ => "Items",
        };

        /// <summary>
        /// The icon attribute "Choose Icon..." edits on element <paramref name="elementId"/>: <c>LargeIcon</c> for a large
        /// control, <c>SmallIcon</c> for the others, <c>Icon</c> for a group or a Backstage tab; null when it has none.
        /// </summary>
        public static string? IconAttribute(string text, string elementId, ComponentRegistry registry)
        {
            var node = ViewDocument.Find(ViewDocument.Parse(text), elementId);
            var component = node is null ? null : registry.Find(node.Name);
            if (node is null || !IsRibbon(component))
            {
                return null;
            }

            bool Has(string name) => component!.Properties.Any(p => p.Name == name);
            if (Has("SmallIcon"))
            {
                return Has("LargeIcon") && node.Attribute("Size") == "Large" ? "LargeIcon" : "SmallIcon";
            }

            return Has("Icon") ? "Icon" : null;
        }

        /// <summary>The text attribute "Edit Label" edits: <c>Header</c> for a tab, a group or a Backstage tab, <c>Label</c> otherwise; null when the element has none.</summary>
        public static string? LabelAttribute(string text, string elementId, ComponentRegistry registry)
        {
            var node = ViewDocument.Find(ViewDocument.Parse(text), elementId);
            var component = node is null ? null : registry.Find(node.Name);
            if (node is null || !IsRibbon(component))
            {
                return null;
            }

            return new[] { "Header", "Label" }.FirstOrDefault(n => component!.Properties.Any(p => p.Name == n));
        }

        /// <summary>The element's <c>Size</c> (Large or Small) when it has one, else null.</summary>
        public static string? SizeOf(string text, string elementId, ComponentRegistry registry)
        {
            var node = ViewDocument.Find(ViewDocument.Parse(text), elementId);
            var component = node is null ? null : registry.Find(node.Name);
            if (node is null || !IsRibbon(component) || !component!.Properties.Any(p => p.Name == "Size" && p.Kind.Tag == PropKindTag.Enum))
            {
                return null;
            }

            return string.IsNullOrEmpty(node.Attribute("Size")) ? "Small" : node.Attribute("Size");
        }

        /// <summary>Whether a <c>Command</c> can be made from element <paramref name="elementId"/> (a button-like element without one).</summary>
        public static bool CanCreateCommand(string text, string elementId)
        {
            var node = ViewDocument.Find(ViewDocument.Parse(text), elementId);
            return node is not null && CommandSources.Contains(node.Name) && string.IsNullOrEmpty(node.Attribute("Command"));
        }

        /// <summary>
        /// "Create Command from this button": a new <c>&lt;Command&gt;</c> at the top of the view, holding the element's label,
        /// icons and screen tip (and its check state for a toggle), which the element then uses through its
        /// <c>Command</c> attribute. Returns the edits (one undo unit) and the new command's name; empty when it cannot.
        /// </summary>
        public static IReadOnlyList<TextReplacement> PlanCreateCommand(string text, string elementId, out string commandName)
        {
            commandName = string.Empty;
            var root = ViewDocument.Parse(text);
            var node = ViewDocument.Find(root, elementId);
            if (root is null || node is null || !CanCreateCommand(text, elementId) || root.Children.Count == 0)
            {
                return Array.Empty<TextReplacement>();
            }

            var names = new HashSet<string>(root.DescendantsAndSelf().Select(n => n.Attribute("x:Name")).Where(n => !string.IsNullOrEmpty(n))!, StringComparer.Ordinal);
            var stem = Identifier(node.Attribute("x:Name")) ?? Identifier(node.Attribute("Label")) ?? "command";
            commandName = "cmd_" + stem;
            for (var i = 2; names.Contains(commandName); i++)
            {
                commandName = "cmd_" + stem + i;
            }

            var command = new StringBuilder("<Command x:Name=\"").Append(commandName).Append('"');
            var moved = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var name in CommandAttributes)
            {
                if (node.Attribute(name) is { Length: > 0 } value)
                {
                    command.Append(' ').Append(name).Append("=\"").Append(ChildCollectionPlanner.Escape(value)).Append('"');
                    moved[name] = null;
                }
            }

            if (node.Name == "RibbonToggleButton")
            {
                command.Append(" IsCheckable=\"true\"");
                if (node.Attribute("Checked") is { Length: > 0 } isChecked)
                {
                    command.Append(" Checked=\"").Append(ChildCollectionPlanner.Escape(isChecked)).Append('"');
                    moved["Checked"] = null;
                }
            }

            command.Append("/>");
            moved["Command"] = commandName;

            var first = root.Children[0];
            var newline = text.Contains("\r\n") ? "\r\n" : "\n";
            var edits = new List<TextReplacement> { new TextReplacement(first.Start, 0, command + newline + LineIndent(text, first.Start)) };
            edits.AddRange(ChildCollectionPlanner.AttributeEdits(text, node, moved));
            return edits.OrderBy(e => e.Start).ThenBy(e => e.Length).ToList();
        }

        /// <summary><paramref name="value"/> as a snake_case identifier (letters, digits, underscores), or null when nothing is left.</summary>
        private static string? Identifier(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var sb = new StringBuilder();
            foreach (var ch in value!.Normalize(NormalizationForm.FormD))
            {
                if (char.IsLetterOrDigit(ch) && ch < 128)
                {
                    sb.Append(char.ToLowerInvariant(ch));
                }
                else if ((char.IsWhiteSpace(ch) || ch == '_' || ch == '-') && sb.Length > 0 && sb[sb.Length - 1] != '_')
                {
                    sb.Append('_');
                }
            }

            var id = sb.ToString().Trim('_');
            return id.Length == 0 ? null : char.IsDigit(id[0]) ? "_" + id : id;
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
    }

    /// <summary>A ribbon element's polymorphic children collection: its row name and the element tags it holds.</summary>
    public sealed class RibbonCollection
    {
        public RibbonCollection(string row, IReadOnlyList<string> tags)
        {
            Row = row;
            Tags = tags;
        }

        /// <summary>The Properties window row (<c>Items</c>, <c>Groups</c>, <c>Tabs</c>, <c>DropDownItems</c>).</summary>
        public string Row { get; }

        /// <summary>The element tags of its members, in registry order (the first is what "Add" adds by default).</summary>
        public IReadOnlyList<string> Tags { get; }
    }
}
