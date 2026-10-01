using System;
using System.Collections.Generic;
using System.Linq;

namespace Kubuno.Web.Logic.WebDesigner
{
    /// <summary>The value kind of a spike element property (drives the Properties window's editor).</summary>
    public enum SpikePropertyKind
    {
        Text,
        Number,
        Bool,
        Choice,
    }

    /// <summary>One property of a spike element: an attribute of the view markup.</summary>
    public sealed class SpikeProperty
    {
        public SpikeProperty(string name, SpikePropertyKind kind, string category, string description, params string[] choices)
        {
            Name = name;
            Kind = kind;
            Category = category;
            Description = description;
            Choices = choices;
        }

        public string Name { get; }

        public SpikePropertyKind Kind { get; }

        public string Category { get; }

        public string Description { get; }

        /// <summary>The allowed values of a <see cref="SpikePropertyKind.Choice"/> property (else empty).</summary>
        public IReadOnlyList<string> Choices { get; }

        /// <summary>The value an absent attribute means (shown by the Properties window), or null.</summary>
        public string? Default { get; set; }
    }

    /// <summary>One element the spike surface renders: its properties, whether it contains children, its Toolbox markup.</summary>
    public sealed class SpikeElement
    {
        public SpikeElement(string name, bool isContainer, string toolboxXml, params SpikeProperty[] properties)
        {
            Name = name;
            IsContainer = isContainer;
            ToolboxXml = toolboxXml;
            Properties = properties;
        }

        public string Name { get; }

        public bool IsContainer { get; }

        /// <summary>The markup a Toolbox drop or double-click inserts.</summary>
        public string ToolboxXml { get; }

        public IReadOnlyList<SpikeProperty> Properties { get; }
    }

    /// <summary>
    /// SPIKE (docs/WEB-VIEWS.md, WV-9a): the handful of <c>@kubuno/ui</c>-like elements the spike page draws, with the
    /// desktop names and properties (docs/WEB-VIEWS.md §3, decision Q1). WV-4's <c>kbview-registry.json</c> replaces it.
    /// </summary>
    public static class SpikeElementCatalog
    {
        private static readonly string[] Variants = { "Primary", "Secondary", "Ghost", "Text", "Danger" };

        public static IReadOnlyList<SpikeElement> Elements { get; } = new[]
        {
            new SpikeElement(
                "Stack",
                isContainer: true,
                "<Stack Direction=\"LeftToRight\" Gap=\"8\" Padding=\"8\"/>",
                new SpikeProperty("Direction", SpikePropertyKind.Choice, "Layout", "Direction of the children.", "TopDown", "LeftToRight"),
                new SpikeProperty("Gap", SpikePropertyKind.Number, "Layout", "Space between the children, in DIP."),
                new SpikeProperty("Padding", SpikePropertyKind.Number, "Layout", "Inner margin, in DIP.")),
            new SpikeElement(
                "Label",
                isContainer: false,
                "<Label Text=\"Label\"/>",
                new SpikeProperty("Text", SpikePropertyKind.Text, "Appearance", "The text shown."),
                new SpikeProperty("TextStyle", SpikePropertyKind.Choice, "Appearance", "Typographic style token.", "Body", "Caption", "Title", "Heading")),
            new SpikeElement(
                "Button",
                isContainer: false,
                "<Button Text=\"Button\"/>",
                new SpikeProperty("Text", SpikePropertyKind.Text, "Appearance", "The caption."),
                new SpikeProperty("Variant", SpikePropertyKind.Choice, "Appearance", "Visual variant.", Variants),
                new SpikeProperty("Enabled", SpikePropertyKind.Bool, "Behavior", "Whether the button can be clicked.") { Default = "true" }),
            new SpikeElement(
                "TextField",
                isContainer: false,
                "<TextField Placeholder=\"Text\"/>",
                new SpikeProperty("Text", SpikePropertyKind.Text, "Appearance", "The initial text."),
                new SpikeProperty("Placeholder", SpikePropertyKind.Text, "Appearance", "Hint shown while empty.")),
            new SpikeElement(
                "CheckBox",
                isContainer: false,
                "<CheckBox Text=\"CheckBox\"/>",
                new SpikeProperty("Text", SpikePropertyKind.Text, "Appearance", "The caption."),
                new SpikeProperty("Checked", SpikePropertyKind.Bool, "Behavior", "Whether the box is checked.") { Default = "false" }),
        };

        /// <summary>The element named <paramref name="name"/>, or null.</summary>
        public static SpikeElement? Find(string? name) => Elements.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.Ordinal));

        /// <summary>
        /// The text the Toolbox data object also carries as <c>CF_UNICODETEXT</c> (Chromium's <c>text/plain</c>), for the
        /// HTML5 drag channel: <c>kubuno-toolbox:Button</c>.
        /// </summary>
        public static string ToolboxText(string componentName) => TextPrefix + componentName;

        /// <summary>The component of a <see cref="ToolboxText"/> payload, or null for any other text.</summary>
        public static string? ParseToolboxText(string? text)
        {
            if (text is null || !text.StartsWith(TextPrefix, StringComparison.Ordinal))
            {
                return null;
            }

            var name = text.Substring(TextPrefix.Length).Trim();
            return IsElementName(name) ? name : null;
        }

        /// <summary>
        /// The markup inserted for a Toolbox component: the catalog's, else an empty element of that name (a component
        /// of the desktop Toolbox the spike does not draw: the page shows a labelled placeholder). Null for a name that
        /// is not an XML element name.
        /// </summary>
        public static string? ToolboxXml(string componentName) =>
            Find(componentName)?.ToolboxXml ?? (IsElementName(componentName) ? "<" + componentName + "/>" : null);

        /// <summary>A plain element name: a letter, then letters, digits or <c>_</c> (no namespace prefix, no markup).</summary>
        public static bool IsElementName(string? name) =>
            !string.IsNullOrEmpty(name) && name!.Length <= 128 && char.IsLetter(name[0]) && name.All(c => char.IsLetterOrDigit(c) || c == '_');

        private const string TextPrefix = "kubuno-toolbox:";
    }
}
