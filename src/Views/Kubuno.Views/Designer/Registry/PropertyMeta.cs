using System;
using System.Collections.Generic;
using System.Linq;

namespace Kubuno.Views.Designer.Registry
{
    /// <summary>
    /// Mirrors one entry of <c>kubuno_views::registry::ComponentMeta.properties</c> (docs/DESIGNER.md
    /// §5: "properties: [{name, kind, default, doc}]"). Plain settable properties (not <c>init</c>-only
    /// records) so <c>System.Text.Json</c> can populate them with the default parameterless-constructor
    /// deserializer, matching the rest of this bridge (see <see cref="ComponentMeta"/>).
    /// </summary>
    public sealed class PropertyMeta
    {
        public string Name { get; set; } = string.Empty;

        public PropKind Kind { get; set; } = PropKind.String;

        /// <summary>
        /// The raw attribute-text default (e.g. <c>"true"</c>, <c>"12.5"</c>, <c>"Primary"</c>), or
        /// <see langword="null"/> when the component has no default for this property. Deliberately a
        /// plain string, not typed per <see cref="Kind"/>: it is exactly what would appear as an XML
        /// attribute value, which is what the Properties grid needs to compare against a control's
        /// current raw attribute value for "defaults shown greyed" (docs/DESIGNER.md §1).
        /// </summary>
        public string? Default { get; set; }

        public string? Doc { get; set; }

        /// <summary>The French user documentation (<c>doc_fr</c>), or null.</summary>
        public string? DocFr { get; set; }

        /// <summary>The user documentation in Visual Studio's UI language (French when available, else English).</summary>
        public string? LocalizedDoc => DesignerText.IsFrench && !string.IsNullOrEmpty(DocFr) ? DocFr : Doc;

        // ---- Design-time attributes of a project control's property (EVT-7b, WinForms `System.ComponentModel`) ----

        /// <summary><c>#[category("…")]</c>: the Properties window group (the designer's own table groups the built-in properties).</summary>
        public string? Category { get; set; }

        /// <summary>Listed in the Properties window (<c>#[browsable(false)]</c> hides it; still valid in XML).</summary>
        public bool Browsable { get; set; } = true;

        public bool Bindable { get; set; }

        public bool Localizable { get; set; }

        /// <summary><c>"Visible"</c>, <c>"Hidden"</c> or <c>"Content"</c>.</summary>
        public string? Serialization { get; set; }

        /// <summary><c>#[editor("color")]</c>: the editor the Properties window uses.</summary>
        public string? Editor { get; set; }

        public string? TypeConverter { get; set; }

        // ---- The control hierarchy's properties (docs/EVENTS.md, "WinForms-rich property sets") ----

        /// <summary>The level of the class hierarchy declaring the property when the element inherits it (<c>"Control"</c>, <c>"ButtonBase"</c>, <c>"View"</c>...), null for the component's own.</summary>
        public string? InheritedFrom { get; set; }

        /// <summary>A property of the view itself (the form: <c>Title</c>, <c>StartPosition</c>...): only the view's root element has it.</summary>
        public bool RootOnly { get; set; }

        /// <summary>Read by the designer only, ignored when the application runs (<c>Locked</c>, <c>Modifiers</c>...).</summary>
        public bool DesignTime { get; set; }

        /// <summary>Older attribute names still accepted for this property (<c>Max</c> for <c>Maximum</c>).</summary>
        public List<string> Aliases { get; set; } = new List<string>();

        /// <summary>The canonical attribute name followed by the aliases - where an element's value may be written.</summary>
        public IEnumerable<string> AttributeNames => new[] { Name }.Concat(Aliases ?? new List<string>());

        /// <summary>Whether <paramref name="attributeName"/> is this property's attribute or one of its older aliases.</summary>
        public bool Matches(string attributeName) => AttributeNames.Contains(attributeName, StringComparer.Ordinal);
    }
}
