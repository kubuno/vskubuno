namespace Kubuno.VisualStudio.Designer.Registry
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
    }
}
