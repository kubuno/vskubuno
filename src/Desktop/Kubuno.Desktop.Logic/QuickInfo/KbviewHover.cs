using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kubuno.VisualStudio.Core.QuickInfo
{
    /// <summary>What a <c>.kbview</c> hover is about.</summary>
    public enum KbviewHoverKind
    {
        Element,
        Property,
        Event,
        CommonAttribute,
        XName,
    }

    /// <summary>
    /// A kubuno-views-ls <c>textDocument/hover</c> markdown (crates/kubuno-views-ls/src/hover.rs), taken
    /// apart so the tooltip can be rebuilt like a C# one - with the registry's localized documentation
    /// when the caller has it (<see cref="KbviewSymbolDetails"/>).
    /// </summary>
    public sealed class KbviewHover
    {
        private static readonly Regex Title = new Regex(@"^\*\*`(?<name>[^`]+)`\*\*(?<rest>.*)$", RegexOptions.CultureInvariant);
        private static readonly Regex OnElement = new Regex(@"^\s*(?<event>event\s+)?on\s+`<(?<element>[^>`]+)>`", RegexOptions.CultureInvariant);
        private static readonly Regex DefaultLine = new Regex(@"^\*Default: `(?<value>.*)`\*$", RegexOptions.CultureInvariant);
        private static readonly Regex EventLine = new Regex(@"^\*(?<category>[^*]+?) — `(?<args>[^`]*)`\*$", RegexOptions.CultureInvariant);
        private static readonly Regex ValidValuesLine = new Regex(@"^Valid values:\s*(?<values>.*)$", RegexOptions.CultureInvariant);

        public KbviewHoverKind Kind { get; private set; }

        /// <summary>The element tag, attribute name or <c>x:Name</c>.</summary>
        public string Name { get; private set; } = string.Empty;

        /// <summary>The element an attribute is on (null for an element hover or a common attribute).</summary>
        public string? Element { get; private set; }

        /// <summary>The server's (English) documentation.</summary>
        public string Documentation { get; private set; } = string.Empty;

        public string? Default { get; private set; }

        public string? EventCategory { get; private set; }

        public string? EventArgs { get; private set; }

        public IReadOnlyList<string> ValidValues { get; private set; } = Array.Empty<string>();

        /// <summary>Extra lines the server added that are not parsed (e.g. "Older name for ...").</summary>
        public IReadOnlyList<string> Remarks { get; private set; } = Array.Empty<string>();

        public static KbviewHover? Parse(string? markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return null;
            }

            var paragraphs = Regex.Split(markdown!.Replace("\r\n", "\n").Trim(), @"\n\s*\n");
            var title = Title.Match(paragraphs[0].Trim());
            if (!title.Success)
            {
                return null;
            }

            var hover = new KbviewHover();
            var name = title.Groups["name"].Value;
            var rest = title.Groups["rest"].Value;
            var on = OnElement.Match(rest);
            if (name.StartsWith("<", StringComparison.Ordinal) && name.EndsWith(">", StringComparison.Ordinal))
            {
                hover.Kind = KbviewHoverKind.Element;
                hover.Name = name.Substring(1, name.Length - 2);
            }
            else if (name == "x:Name")
            {
                hover.Kind = KbviewHoverKind.XName;
                hover.Name = name;
            }
            else if (rest.Contains("(common attribute)"))
            {
                hover.Kind = KbviewHoverKind.CommonAttribute;
                hover.Name = name;
            }
            else if (on.Success)
            {
                hover.Kind = on.Groups["event"].Success ? KbviewHoverKind.Event : KbviewHoverKind.Property;
                hover.Name = name;
                hover.Element = on.Groups["element"].Value;
            }
            else
            {
                return null;
            }

            var docs = new List<string>();
            var remarks = new List<string>();
            foreach (var paragraph in paragraphs.Skip(1).Select(p => p.Trim()))
            {
                Match match;
                if ((match = DefaultLine.Match(paragraph)).Success)
                {
                    hover.Default = match.Groups["value"].Value;
                }
                else if (hover.Kind == KbviewHoverKind.Event && (match = EventLine.Match(paragraph)).Success)
                {
                    hover.EventCategory = match.Groups["category"].Value.Trim();
                    hover.EventArgs = match.Groups["args"].Value.Trim();
                }
                else if ((match = ValidValuesLine.Match(paragraph)).Success)
                {
                    hover.ValidValues = match.Groups["values"].Value.Split(',').Select(v => v.Trim()).Where(v => v.Length > 0).ToList();
                }
                else if (docs.Count > 0 && paragraph.StartsWith("*", StringComparison.Ordinal) && paragraph.EndsWith("*", StringComparison.Ordinal))
                {
                    remarks.Add(paragraph);
                }
                else
                {
                    docs.Add(paragraph);
                }
            }

            hover.Documentation = string.Join("\n\n", docs);
            hover.Remarks = remarks;
            return hover;
        }

        /// <summary>
        /// The tooltip: icon (the Kubuno control icon of an element; Visual Studio's property/event glyph for
        /// an attribute), a classified signature (<c>Button</c>, <c>Text: String = ""</c>,
        /// <c>event OnClick(MouseEventArgs)</c>), the element in grey, then the documentation.
        /// </summary>
        public QuickInfoElement ToQuickInfo(KbviewSymbolDetails? details = null)
        {
            details ??= new KbviewSymbolDetails();
            var signature = new RunBuilder();
            QuickInfoImage icon;
            string? container = Element;
            switch (Kind)
            {
                case KbviewHoverKind.Element:
                    icon = QuickInfoImage.Control(Name);
                    signature.Add(QuickInfoTextKind.Class, Name);
                    container = details.Container;
                    break;
                case KbviewHoverKind.Event:
                    icon = QuickInfoImage.Moniker("EventPublic");
                    signature.Add(QuickInfoTextKind.Keyword, "event");
                    signature.Add(QuickInfoTextKind.Text, " ");
                    signature.Add(QuickInfoTextKind.Event, Name);
                    var args = details.EventArgs ?? EventArgs;
                    signature.Add(QuickInfoTextKind.Punctuation, "(");
                    if (!string.IsNullOrEmpty(args))
                    {
                        signature.Add(QuickInfoTextKind.Class, args!);
                    }

                    signature.Add(QuickInfoTextKind.Punctuation, ")");
                    var category = details.EventCategory ?? EventCategory;
                    if (!string.IsNullOrEmpty(category) && !string.IsNullOrEmpty(container))
                    {
                        container += " · " + category;
                    }

                    break;
                case KbviewHoverKind.XName:
                    icon = QuickInfoImage.Moniker("FieldPublic");
                    AddTyped(signature, QuickInfoTextKind.Field, Name, details.TypeName ?? "String", null);
                    break;
                default:
                    icon = QuickInfoImage.Moniker("PropertyPublic");
                    AddTyped(signature, QuickInfoTextKind.Property, Name, details.TypeName, details.Default ?? Default);
                    break;
            }

            var rows = new List<QuickInfoElement> { QuickInfoLayout.Header(icon, signature.Build(), container) };
            var documentation = DocumentationRenderer.Render(string.IsNullOrWhiteSpace(details.Documentation) ? Documentation : details.Documentation);
            if (documentation != null)
            {
                rows.Add(documentation);
            }

            var values = details.ValidValues ?? ValidValues;
            if (values.Count > 0)
            {
                var runs = new RunBuilder();
                runs.Add(QuickInfoTextKind.Text, (details.ValidValuesLabel ?? "Valid values:") + " ");
                for (int i = 0; i < values.Count; i++)
                {
                    if (i > 0)
                    {
                        runs.Add(QuickInfoTextKind.Punctuation, ", ");
                    }

                    runs.Add(QuickInfoTextKind.EnumMember, values[i]);
                }

                rows.Add(new QuickInfoText(runs.Build()));
            }

            foreach (var remark in Remarks)
            {
                rows.Add(new QuickInfoText(Markdown.ParseInline(remark)));
            }

            return new QuickInfoContainer(QuickInfoContainerStyle.Stacked | QuickInfoContainerStyle.VerticalPadding, rows);
        }

        private static void AddTyped(RunBuilder signature, QuickInfoTextKind nameKind, string name, string? type, string? defaultValue)
        {
            signature.Add(nameKind, name);
            if (!string.IsNullOrEmpty(type))
            {
                signature.Add(QuickInfoTextKind.Punctuation, ": ");
                signature.Add(IsPrimitive(type!) ? QuickInfoTextKind.Keyword : QuickInfoTextKind.Class, type!);
            }

            if (defaultValue != null)
            {
                signature.Add(QuickInfoTextKind.Text, " ");
                signature.Add(QuickInfoTextKind.Operator, "=");
                signature.Add(QuickInfoTextKind.Text, " ");
                if (type == "String" || type == "enum" || (type == null && !IsLiteral(defaultValue)))
                {
                    signature.Add(QuickInfoTextKind.String, "\"" + defaultValue + "\"");
                }
                else
                {
                    signature.Add(defaultValue == "true" || defaultValue == "false" ? QuickInfoTextKind.Keyword : QuickInfoTextKind.Number, defaultValue);
                }
            }
        }

        private static bool IsPrimitive(string type) => type == "bool" || type == "f32" || type == "f64" || type == "i32" || type == "u32" || type == "enum" || type == "str";

        private static bool IsLiteral(string value) => value == "true" || value == "false" || double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);
    }

    /// <summary>What the Kubuno component registry (localized) says about a <c>.kbview</c> symbol; any null value falls back to the server's hover.</summary>
    public sealed class KbviewSymbolDetails
    {
        /// <summary>Documentation (markdown) in Visual Studio's language.</summary>
        public string? Documentation { get; set; }

        /// <summary>The value type: <c>bool</c>, <c>f32</c>, <c>String</c>, <c>enum</c>.</summary>
        public string? TypeName { get; set; }

        public string? Default { get; set; }

        public IReadOnlyList<string>? ValidValues { get; set; }

        /// <summary>The label before the list of valid values ("Valid values:" / "Valeurs possibles :").</summary>
        public string? ValidValuesLabel { get; set; }

        public string? EventArgs { get; set; }

        public string? EventCategory { get; set; }

        /// <summary>Grey line under an element's signature (its Toolbox family, for example).</summary>
        public string? Container { get; set; }
    }
}
