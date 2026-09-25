using System.Collections.Generic;

namespace Kubuno.VisualStudio.Designer.Registry
{
    /// <summary>
    /// Mirrors one exported <c>kubuno_views::registry::ComponentMeta</c> entry - the whole shape
    /// docs/DESIGNER.md §5 specifies for the <c>kubuno/registry</c> response ("one JSON object per
    /// ComponentMeta: {name, doc, properties: [...], events: [...], children, family}", plus the
    /// additive fields <see cref="Icon"/>, <see cref="LayoutKind"/> and <see cref="AllowedChildren"/>
    /// that section (and DSG-1's own implementation, <c>kubuno-views/src/registry/export.rs</c>) call
    /// for. Loaded from a JSON string (<see cref="ComponentRegistry.FromJson"/>) rather than
    /// referencing the Rust crate directly - see docs/ARCHITECTURE.md's "everything that knows
    /// Rust/Kubuno is in Rust; C# only integrates".
    /// </summary>
    public sealed class ComponentMeta
    {
        /// <summary>The element's tag name, e.g. <c>"Button"</c>.</summary>
        public string Name { get; set; } = string.Empty;

        public string? Doc { get; set; }

        /// <summary>Toolbox grouping, e.g. <c>"core"</c>, <c>"choice"</c>, <c>"containers"</c>, <c>"data"</c>, <c>"display"</c>, <c>"text"</c> (§5: "family groups the toolbox exactly as registry::families::ALL_FAMILIES already groups the table internally").</summary>
        public string Family { get; set; } = string.Empty;

        /// <summary>
        /// A short glyph name for the toolbox (§5: "an icon glyph name"). The real export
        /// (<c>export.rs</c>) derives this mechanically - the component name, kebab-cased - rather
        /// than from a hand-curated table, so it is effectively never <see langword="null"/> for a
        /// component the real registry exports; kept nullable here only because nothing in this type
        /// should assume the server always sets it.
        /// </summary>
        public string? Icon { get; set; }

        public ChildrenModel Children { get; set; }

        /// <summary>
        /// <see cref="ChildrenModel.List"/>'s gated child element names (docs/DESIGNER.md §5:
        /// "children model incl. allowed child names"), e.g. <c>["TabItem"]</c> for <c>Tabs</c>. Empty
        /// for every other component and for an ungated <see cref="ChildrenModel.List"/> container
        /// (e.g. <c>Stack</c>, which accepts any child). A new field the real export
        /// (<c>ComponentJson::allowed_children</c>) added alongside <see cref="Children"/> rather than
        /// folding into it, since <see cref="Children"/> must stay the bare string
        /// <c>JsonStringEnumConverter</c> expects.
        /// </summary>
        public List<string> AllowedChildren { get; set; } = new List<string>();

        /// <summary>See <see cref="Registry.LayoutKind"/>'s own doc comment for why this is nullable.</summary>
        public LayoutKind? LayoutKind { get; set; }

        public List<PropertyMeta> Properties { get; set; } = new List<PropertyMeta>();

        public List<EventMeta> Events { get; set; } = new List<EventMeta>();
    }
}
