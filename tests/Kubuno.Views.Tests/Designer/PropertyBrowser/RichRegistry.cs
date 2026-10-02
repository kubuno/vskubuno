using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Kubuno.Desktop.Designer.PropertyBrowser;
using Kubuno.Desktop.Designer.Registry;

namespace Kubuno.Desktop.Tests.Designer.PropertyBrowser
{
    /// <summary>
    /// A small registry in the shape of the export with the control hierarchy's properties (docs/EVENTS.md,
    /// "WinForms-rich property sets"): own properties, then the inherited ones (<c>inherited_from</c>), then the view's
    /// (<c>root_only</c>), with categories, editors, converters and aliases - serialized to JSON and read back through
    /// <see cref="ComponentRegistry.FromJson"/>, so the new keys' deserialization is exercised too.
    /// </summary>
    internal static class RichRegistry
    {
        private static readonly Lazy<ComponentRegistry> Instance = new Lazy<ComponentRegistry>(() => ComponentRegistry.FromJson(Json));

        public static ComponentRegistry Registry => Instance.Value;

        private static object P(string name, object kind, string @default, string category, string? inherited = "Control", string? editor = null, string? converter = null, bool rootOnly = false, bool designTime = false, bool bindable = false, string[]? aliases = null, string? doc = null) => new Dictionary<string, object?>
        {
            ["name"] = name,
            ["kind"] = kind,
            ["default"] = @default,
            ["doc"] = doc ?? $"The {name} of the element.",
            ["doc_fr"] = $"Le {name} de l'élément.",
            ["category"] = category,
            ["browsable"] = true,
            ["bindable"] = bindable,
            ["localizable"] = false,
            ["serialization"] = null,
            ["editor"] = editor,
            ["type_converter"] = converter,
            ["inherited_from"] = inherited,
            ["root_only"] = rootOnly,
            ["design_time"] = designTime,
            ["aliases"] = aliases ?? Array.Empty<string>(),
        };

        private static Dictionary<string, string[]> E(params string[] variants) => new Dictionary<string, string[]> { ["Enum"] = variants };

        private static readonly object[] ControlLevel =
        {
            P("AccessibleName", "String", "", "Accessibility"),
            P("AccessibleRole", E("Default", "None", "PushButton", "Text"), "Default", "Accessibility"),
            P("BackColor", "String", "", "Appearance", editor: "color"),
            P("ForeColor", "String", "", "Appearance", editor: "color"),
            P("Font", "String", "", "Appearance", editor: "font"),
            P("Cursor", E(CursorNames.All.ToArray()), "Default", "Appearance", editor: "cursor"),
            P("BackgroundImage", "String", "", "Appearance", editor: "image"),
            P("Enabled", "Bool", "true", "Behavior", bindable: true),
            P("Visible", "Bool", "true", "Behavior", bindable: true),
            P("TabIndex", "F32", "0", "Behavior"),
            P("ContextMenu", "String", "", "Behavior", editor: "reference:ContextMenu"),
            P("ToolTip", "String", "", "Misc"),
            P("Tag", "String", "", "Data"),
            P("Locked", "Bool", "false", "Design", designTime: true),
            P("CausesValidation", "Bool", "true", "Focus"),
            P("X", "F32", "0", "Layout"),
            P("Y", "F32", "0", "Layout"),
            P("Width", "F32", "", "Layout"),
            P("Height", "F32", "", "Layout"),
            P("Dock", E("None", "Top", "Bottom", "Left", "Right", "Fill"), "None", "Layout"),
            P("Anchor", "String", "Top, Left", "Layout"),
            P("Margin", "String", "0, 0, 0, 0", "Layout", converter: "Padding"),
            P("Padding", "String", "0, 0, 0, 0", "Layout", converter: "Padding"),
            P("MinimumSize", "String", "0, 0", "Layout", converter: "Size"),
            P("MaximumSize", "String", "0, 0", "Layout", converter: "Size"),
        };

        private static readonly object[] ViewLevel =
        {
            P("Title", "String", "", "Appearance", inherited: "View", rootOnly: true),
            P("StartPosition", E("Manual", "CenterScreen", "WindowsDefaultLocation"), "WindowsDefaultLocation", "Layout", inherited: "View", rootOnly: true),
            P("Opacity", "F32", "100", "Window Style", inherited: "View", converter: "Opacity", rootOnly: true),
            P("AcceptButton", "String", "", "Misc", inherited: "View", editor: "reference:ButtonBase", rootOnly: true),
            P("Icon", "String", "", "Window Style", inherited: "View", editor: "image", rootOnly: true),
        };

        private static object C(string name, string[] chain, object[] own, string children = "None", string[]? allowed = null, string? layout = null, bool control = true) => new Dictionary<string, object?>
        {
            ["name"] = name,
            ["doc"] = name + ".",
            ["family"] = "core",
            ["children"] = children,
            ["allowed_children"] = allowed ?? Array.Empty<string>(),
            ["layout_kind"] = layout,
            ["properties"] = control ? own.Concat(ControlLevel.Where(c => !own.Any(o => Name(o) == Name(c)))).Concat(ViewLevel).ToArray() : own,
            ["events"] = Array.Empty<object>(),
            ["base_chain"] = chain,
        };

        private static string Name(object property) => (string)((Dictionary<string, object?>)property)["name"]!;

        private static string Json => JsonSerializer.Serialize(new[]
        {
            C("Button", new[] { "Button", "ButtonBase", "Control", "Component" }, new[]
            {
                P("Text", "String", "", "Appearance", inherited: null, bindable: true),
                P("Size", E("Sm", "Md", "Lg"), "Md", "Appearance", inherited: null),
                P("UseVisualStyleBackColor", "Bool", "true", "Appearance", inherited: "ButtonBase"),
            }),
            C("Panel", new[] { "Panel", "ContainerBase", "ScrollableControl", "Control", "Component" }, Array.Empty<object>(), children: "List", layout: "DockAnchor"),
            C("Stack", new[] { "Stack", "ContainerBase", "ScrollableControl", "Control", "Component" }, new[] { P("Padding", "String", "0, 0, 0, 0", "Layout", inherited: null, converter: "Padding") }, children: "List", layout: "Flow"),
            C("Slider", new[] { "Slider", "RangeBase", "Control", "Component" }, new[]
            {
                P("Maximum", "F32", "100", "Behavior", inherited: "RangeBase", aliases: new[] { "Max" }),
                P("Minimum", "F32", "0", "Behavior", inherited: "RangeBase", aliases: new[] { "Min" }),
            }),
            C("ListView", new[] { "ListView", "Control", "Component" }, Array.Empty<object>(), children: "List", allowed: new[] { "Item", "Column" }),
            C("ComboBox", new[] { "ComboBox", "ListControl", "Control", "Component" }, Array.Empty<object>(), children: "List", allowed: new[] { "Option" }),
            C("Tabs", new[] { "Tabs", "ContainerBase", "ScrollableControl", "Control", "Component" }, Array.Empty<object>(), children: "List", allowed: new[] { "TabItem" }),
            C("Column", new[] { "Column", "Component" }, new[] { P("Header", "String", "", "Appearance", inherited: null), P("Binding", "String", "", "Data", inherited: null), P("Width", "F32", "", "Layout", inherited: null) }, control: false),
            C("Item", new[] { "Item", "Component" }, new[] { P("Text", "String", "", "Appearance", inherited: null) }, children: "List", allowed: new[] { "Item" }, control: false),
            C("Option", new[] { "Option", "Component" }, new[] { P("Value", "String", "", "Data", inherited: null), P("Label", "String", "", "Appearance", inherited: null) }, control: false),
            C("TabItem", new[] { "TabItem", "Component" }, new[] { P("Header", "String", "", "Appearance", inherited: null) }, children: "SingleWidget", control: false),
            C("ToolTip", new[] { "ToolTip", "Component" }, new[] { P("InitialDelay", "F32", "500", "Behavior", inherited: null) }, control: false),
            C("ContextMenu", new[] { "ContextMenu", "Component" }, Array.Empty<object>(), children: "List", allowed: new[] { "MenuItem" }, control: false),
            C("MenuItem", new[] { "MenuItem", "Component" }, new[] { P("Text", "String", "", "Appearance", inherited: null) }, control: false),
        });
    }
}
