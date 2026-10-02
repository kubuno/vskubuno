using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Kubuno.Views.Designer.PropertyBrowser;
using Kubuno.Views.Designer.Registry;
using Kubuno.Views.Designer.Selection;

namespace Kubuno.Views.Designer.Menus
{
    /// <summary>One task of a menu element's smart tag (docs/MENUS.md): what it does, its text, and its argument.</summary>
    public sealed class MenuVerb
    {
        public MenuVerb(MenuVerbKind kind, string text, string? argument = null)
        {
            Kind = kind;
            Text = text;
            Argument = argument;
        }

        public MenuVerbKind Kind { get; }

        /// <summary>The text shown in the menu, in Visual Studio's UI language.</summary>
        public string Text { get; }

        /// <summary>The element tag to add, or the command to bind; null for the other verbs.</summary>
        public string? Argument { get; }

        public override string ToString() => Kind + (Argument is null ? string.Empty : "(" + Argument + ")");
    }

    /// <summary>What a <see cref="MenuVerb"/> does.</summary>
    public enum MenuVerbKind
    {
        /// <summary>WinForms' "Insert Standard Items" on a menu bar: Fichier, Édition, Outils, Aide.</summary>
        InsertStandardItems,

        /// <summary>The polymorphic collection editor on the element's items.</summary>
        EditItems,

        /// <summary>A new element of the <see cref="MenuVerb.Argument"/> tag at the end of the element.</summary>
        Add,

        /// <summary>The icon picker on the item's <c>Icon</c>.</summary>
        ChooseIcon,

        /// <summary>The item's <c>Text</c> in a small input box.</summary>
        EditText,

        /// <summary>The item runs the <c>&lt;Command&gt;</c> named <see cref="MenuVerb.Argument"/>.</summary>
        BindCommand,

        /// <summary>A new <c>&lt;Command&gt;</c> made from the item (its text, icon, shortcut and tooltip), which it then runs.</summary>
        NewCommand,

        /// <summary>The item no longer runs a command.</summary>
        UnbindCommand,

        /// <summary>The title bar's smart tag: a new button in the <c>TitleBar.Region</c> named <see cref="MenuVerb.Argument"/>.</summary>
        AddTitleBarButton,

        /// <summary>The title bar's smart tag: the header item the view property <see cref="MenuVerb.Argument"/> shows, switched.</summary>
        ToggleHeaderItem,
    }

    /// <summary>
    /// What the designer offers on a menu element (docs/MENUS.md section 5): the « Ajouter ▾ » choices of a « Tapez ici »
    /// slot, the smart tag's tasks, the polymorphic collection the Properties window edits, WinForms' « Insérer les éléments
    /// standard », and making or binding a <c>Command</c>. Pure, unit-tested.
    /// </summary>
    public static class MenuDesignerTasks
    {
        /// <summary>The registry family of the menu elements.</summary>
        public const string Family = "menus";

        /// <summary>The elements that hold menu items.</summary>
        private static readonly HashSet<string> Owners = new HashSet<string>(StringComparer.Ordinal) { "MenuBar", "ContextMenu", "MenuItem", "DropDownButton", "SplitButton" };

        /// <summary>The element tags of a menu's items.</summary>
        public static readonly IReadOnlyList<string> ItemTags = new[] { "MenuItem", "MenuSeparator", "MenuHeader" };

        /// <summary>Whether <paramref name="component"/> is a menu element (the "menus" family).</summary>
        public static bool IsMenu(ComponentMeta? component) => string.Equals(component?.Family, Family, StringComparison.Ordinal);

        /// <summary>Whether element <paramref name="tag"/> is an item laid out by its menu (no Layout commands apply to it).</summary>
        public static bool IsMenuItem(string? tag) => tag is not null && ItemTags.Contains(tag);

        /// <summary>
        /// What can be added at the end of element <paramref name="elementId"/>: a menu bar takes menus (<c>MenuItem</c>),
        /// the other menu owners their items, separators and headers; empty for anything else.
        /// </summary>
        public static IReadOnlyList<string> AddChoices(string text, string elementId, ComponentRegistry registry)
        {
            var node = ViewDocument.Find(ViewDocument.Parse(text), elementId);
            var component = node is null ? null : registry.Find(node.Name);
            if (node is null || component is null || !Owners.Contains(node.Name))
            {
                return Array.Empty<string>();
            }

            var allowed = component.AllowedChildren ?? new List<string>();
            return ItemTags.Where(t => allowed.Contains(t) && registry.Find(t) is not null).ToList();
        }

        /// <summary>The element's items as one polymorphic Properties-window collection (<c>Items</c>, or <c>DropDownItems</c> of a sub-menu or a button), or null.</summary>
        public static Ribbon.RibbonCollection? CollectionOf(ComponentMeta component)
        {
            if (component is null || !Owners.Contains(component.Name))
            {
                return null;
            }

            var allowed = component.AllowedChildren ?? new List<string>();
            var tags = ItemTags.Where(allowed.Contains).ToList();
            if (tags.Count == 0)
            {
                return null;
            }

            return new Ribbon.RibbonCollection(component.Name is "MenuItem" or "DropDownButton" or "SplitButton" ? "DropDownItems" : "Items", tags);
        }

        /// <summary>« Insérer les éléments standard » applies: a menu bar with no menu yet (WinForms offers it on an empty MenuStrip).</summary>
        public static bool CanInsertStandardItems(string text, string elementId)
        {
            var node = ViewDocument.Find(ViewDocument.Parse(text), elementId);
            return node is not null && node.Name == "MenuBar" && !node.Children.Any(c => c.Name == "MenuItem");
        }

        /// <summary>The smart tag's tasks of menu element <paramref name="elementId"/>, in order (empty for anything else).</summary>
        public static IReadOnlyList<MenuVerb> Verbs(string text, string elementId, ComponentRegistry registry)
        {
            var root = ViewDocument.Parse(text);
            var node = ViewDocument.Find(root, elementId);
            var component = node is null ? null : registry.Find(node.Name);
            if (node is null || !IsMenu(component))
            {
                return Array.Empty<MenuVerb>();
            }

            var verbs = new List<MenuVerb>();
            if (CanInsertStandardItems(text, elementId))
            {
                verbs.Add(new MenuVerb(MenuVerbKind.InsertStandardItems, DesignerText.MenuInsertStandardItems));
            }

            if (CollectionOf(component!) is { } collection)
            {
                verbs.Add(new MenuVerb(MenuVerbKind.EditItems, DesignerText.MenuRibbonEditCollection(collection.Row)));
            }

            foreach (var tag in AddChoices(text, elementId, registry))
            {
                verbs.Add(new MenuVerb(MenuVerbKind.Add, DesignerText.MenuAddMenuElement(tag), tag));
            }

            if (node.Name == "MenuItem")
            {
                verbs.Add(new MenuVerb(MenuVerbKind.EditText, DesignerText.MenuEditMenuText));
                verbs.Add(new MenuVerb(MenuVerbKind.ChooseIcon, DesignerText.MenuRibbonChooseIcon));
                var bound = node.Attribute("Command");
                foreach (var command in CommandNames(root))
                {
                    if (command != bound)
                    {
                        verbs.Add(new MenuVerb(MenuVerbKind.BindCommand, DesignerText.MenuBindCommand(command), command));
                    }
                }

                if (string.IsNullOrEmpty(bound))
                {
                    verbs.Add(new MenuVerb(MenuVerbKind.NewCommand, DesignerText.MenuNewCommandFromItem));
                }
                else
                {
                    verbs.Add(new MenuVerb(MenuVerbKind.UnbindCommand, DesignerText.MenuUnbindCommand(bound!)));
                }
            }

            return verbs.Take(DesignSurface.DesignerCommandIds.MaxDynamicItems).ToList();
        }

        /// <summary>The <c>x:Name</c> of every <c>&lt;Command&gt;</c> of the view, in document order.</summary>
        public static IReadOnlyList<string> CommandNames(ViewNode? root) =>
            root?.DescendantsAndSelf().Where(n => n.Name == "Command").Select(n => n.Attribute("x:Name")).Where(n => !string.IsNullOrEmpty(n)).Select(n => n!).ToList()
            ?? (IReadOnlyList<string>)Array.Empty<string>();

        // ---- Insert Standard Items ----

        /// <summary>One item of the standard set: its name stem, text, icon and shortcut; null text = a separator.</summary>
        private sealed class StandardItem
        {
            public StandardItem(string? name, string? text, string? icon = null, string? shortcut = null, StandardItem[]? children = null)
            {
                Name = name;
                Text = text;
                Icon = icon;
                Shortcut = shortcut;
                Children = children ?? Array.Empty<StandardItem>();
            }

            public string? Name { get; }

            public string? Text { get; }

            public string? Icon { get; }

            public string? Shortcut { get; }

            public StandardItem[] Children { get; }
        }

        private static StandardItem Sep() => new StandardItem(null, null);

        /// <summary>WinForms' standard menus, in French with Kubuno icons.</summary>
        private static readonly StandardItem[] Standard =
        {
            new StandardItem("file", "&Fichier", children: new[]
            {
                new StandardItem("file_new", "&Nouveau", "FilePlus", "Ctrl+N"),
                new StandardItem("file_open", "&Ouvrir…", "FolderOpen", "Ctrl+O"),
                Sep(),
                new StandardItem("file_save", "&Enregistrer", "Save", "Ctrl+S"),
                new StandardItem("file_save_as", "Enregistrer &sous…"),
                Sep(),
                new StandardItem("file_print", "&Imprimer…", "Printer", "Ctrl+P"),
                new StandardItem("file_print_preview", "Aperçu a&vant impression", "Eye"),
                Sep(),
                new StandardItem("file_exit", "&Quitter", "LogOut"),
            }),
            new StandardItem("edit", "É&dition", children: new[]
            {
                new StandardItem("edit_undo", "&Annuler", "Undo2", "Ctrl+Z"),
                new StandardItem("edit_redo", "&Rétablir", "Redo2", "Ctrl+Y"),
                Sep(),
                new StandardItem("edit_cut", "Co&uper", "Scissors", "Ctrl+X"),
                new StandardItem("edit_copy", "&Copier", "Copy", "Ctrl+C"),
                new StandardItem("edit_paste", "C&oller", "ClipboardPaste", "Ctrl+V"),
                Sep(),
                new StandardItem("edit_select_all", "Sélectionner &tout", shortcut: "Ctrl+A"),
            }),
            new StandardItem("tools", "&Outils", children: new[]
            {
                new StandardItem("tools_customize", "&Personnaliser"),
                new StandardItem("tools_options", "&Options…", "Settings"),
            }),
            new StandardItem("help", "&Aide", children: new[]
            {
                new StandardItem("help_contents", "&Sommaire", "BookOpen"),
                new StandardItem("help_index", "&Index"),
                new StandardItem("help_search", "&Rechercher", "Search"),
                Sep(),
                new StandardItem("help_about", "À &propos…", "Info"),
            }),
        };

        /// <summary>
        /// The edits that fill menu bar <paramref name="elementId"/> with the standard menus (one undo unit), every item
        /// named <c>menu_file_new</c>… (names the view already uses get a number). With <paramref name="resourceKey"/>,
        /// each text is the reference <paramref name="resourceKey"/> answers for the item's name and French text (it
        /// adds the string to the project's <c>.kbres</c>) and <paramref name="resources"/> receives the keys and French texts to add to the project's
        /// <c>.kbres</c>; without it the texts are written as they are.
        /// </summary>
        public static IReadOnlyList<TextReplacement> PlanStandardItems(string text, string elementId, Func<string, string, string>? resourceKey, out IReadOnlyList<(string Key, string Value)> resources)
        {
            resources = Array.Empty<(string, string)>();
            var root = ViewDocument.Parse(text);
            var bar = ViewDocument.Find(root, elementId);
            if (root is null || bar is null || !CanInsertStandardItems(text, elementId))
            {
                return Array.Empty<TextReplacement>();
            }

            var names = new HashSet<string>(root.DescendantsAndSelf().Select(n => n.Attribute("x:Name")).Where(n => !string.IsNullOrEmpty(n))!, StringComparer.Ordinal);
            var newline = text.Contains("\r\n") ? "\r\n" : "\n";
            var indent = bar.Children.Count > 0 ? ChildCollectionPlanner.LineIndent(text, bar.Children[0].Start) : ChildCollectionPlanner.LineIndent(text, bar.Start) + "  ";
            var keys = new List<(string, string)>();
            string Unique(string stem)
            {
                var name = "menu_" + stem;
                for (var i = 2; names.Contains(name); i++)
                {
                    name = "menu_" + stem + i;
                }

                names.Add(name);
                return name;
            }

            string Element(StandardItem item, string inner)
            {
                if (item.Text is null)
                {
                    return "<MenuSeparator/>";
                }

                var name = Unique(item.Name!);
                var sb = new StringBuilder("<MenuItem x:Name=\"").Append(name).Append("\" Text=\"");
                if (resourceKey is not null)
                {
                    keys.Add((name, item.Text));
                    sb.Append(ChildCollectionPlanner.Escape(resourceKey(name, item.Text)));
                }
                else
                {
                    sb.Append(ChildCollectionPlanner.Escape(item.Text));
                }

                sb.Append('"');
                if (item.Icon is not null)
                {
                    sb.Append(" Icon=\"").Append(item.Icon).Append('"');
                }

                if (item.Shortcut is not null)
                {
                    sb.Append(" ShortcutKeys=\"").Append(item.Shortcut).Append('"');
                }

                if (item.Children.Length == 0)
                {
                    return sb.Append("/>").ToString();
                }

                sb.Append('>');
                foreach (var child in item.Children)
                {
                    sb.Append(newline).Append(inner).Append("  ").Append(Element(child, inner + "  "));
                }

                return sb.Append(newline).Append(inner).Append("</MenuItem>").ToString();
            }

            var elements = Standard.Select(menu => Element(menu, indent)).ToList();
            resources = keys;
            return new[] { ChildCollectionPlanner.InsertInto(text, bar, elements, newline) };
        }

        // ---- Commands ----

        /// <summary>What a new <c>Command</c> takes from the item it is made from: item attribute → command attribute.</summary>
        private static readonly (string Item, string Command)[] CommandAttributes =
        {
            ("Text", "Label"), ("Icon", "SmallIcon"), ("ShortcutKeys", "Shortcut"), ("ToolTip", "ScreenTipText"),
        };

        /// <summary>
        /// « Nouvelle commande… » on menu item <paramref name="elementId"/>: a new <c>&lt;Command&gt;</c> at the top of the
        /// view holding the item's text, icon, shortcut and tooltip (a <c>CheckOnClick</c> item makes a checkable one),
        /// which the item then runs through its <c>Command</c> attribute. The edits are one undo unit; empty when it cannot.
        /// </summary>
        public static IReadOnlyList<TextReplacement> PlanNewCommand(string text, string elementId, out string commandName)
        {
            commandName = string.Empty;
            var root = ViewDocument.Parse(text);
            var node = ViewDocument.Find(root, elementId);
            if (root is null || node is null || node.Name != "MenuItem" || !string.IsNullOrEmpty(node.Attribute("Command")) || root.Children.Count == 0)
            {
                return Array.Empty<TextReplacement>();
            }

            var names = new HashSet<string>(root.DescendantsAndSelf().Select(n => n.Attribute("x:Name")).Where(n => !string.IsNullOrEmpty(n))!, StringComparer.Ordinal);
            var stem = Identifier(node.Attribute("x:Name")?.Replace("menu_", string.Empty)) ?? Identifier(Plain(node.Attribute("Text"))) ?? "command";
            commandName = "cmd_" + stem;
            for (var i = 2; names.Contains(commandName); i++)
            {
                commandName = "cmd_" + stem + i;
            }

            var command = new StringBuilder("<Command x:Name=\"").Append(commandName).Append('"');
            var moved = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var (item, target) in CommandAttributes)
            {
                if (node.Attribute(item) is { Length: > 0 } value)
                {
                    command.Append(' ').Append(target).Append("=\"").Append(ChildCollectionPlanner.Escape(value)).Append('"');
                    moved[item] = null;
                }
            }

            if (node.Attribute("CheckOnClick") == "true")
            {
                command.Append(" IsCheckable=\"true\"");
                moved["CheckOnClick"] = null;
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
            var edits = new List<TextReplacement> { new TextReplacement(first.Start, 0, command + newline + ChildCollectionPlanner.LineIndent(text, first.Start)) };
            edits.AddRange(ChildCollectionPlanner.AttributeEdits(text, node, moved));
            return edits.OrderBy(e => e.Start).ThenBy(e => e.Length).ToList();
        }

        /// <summary>A menu text without its <c>&amp;</c> mnemonic markers (<c>&amp;&amp;</c> is a literal ampersand).</summary>
        public static string Plain(string? text) => (text ?? string.Empty).Replace("&&", "\u0001").Replace("&", string.Empty).Replace("\u0001", "&");

        /// <summary><paramref name="value"/> as a snake_case identifier (ASCII letters, digits, underscores), or null when nothing is left.</summary>
        public static string? Identifier(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || value!.StartsWith("{", StringComparison.Ordinal))
            {
                return null;
            }

            var sb = new StringBuilder();
            foreach (var ch in value.Normalize(NormalizationForm.FormD))
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
    }
}
