using System.Collections.Generic;

namespace Kubuno.VisualStudio.Designer.Registry
{
    /// <summary>
    /// Mirrors one exported <c>kubuno_views::registry::ComponentMeta</c> entry - the whole shape
    /// docs/DESIGNER.md §5 specifies for the <c>kubuno/registry</c> response ("one JSON object per
    /// ComponentMeta: {name, doc, properties: [...], events: [...], children, family}", plus the two
    /// additive fields, <see cref="Icon"/> and <see cref="LayoutKind"/>, that section calls for). DSG-1
    /// (not built yet) owns the real Rust struct and the language-server endpoint that serializes it;
    /// this class only needs to deserialize whatever that endpoint eventually returns, which is why it
    /// is loaded from a JSON string (<see cref="ComponentRegistry.FromJson"/>) rather than referencing
    /// any Rust crate directly - see docs/ARCHITECTURE.md's "everything that knows Rust/Kubuno is in
    /// Rust; C# only integrates".
    /// </summary>
    public sealed class ComponentMeta
    {
        /// <summary>The element's tag name, e.g. <c>"Button"</c>.</summary>
        public string Name { get; set; } = string.Empty;

        public string? Doc { get; set; }

        /// <summary>Toolbox grouping, e.g. <c>"core"</c>, <c>"choice"</c>, <c>"containers"</c>, <c>"data"</c>, <c>"display"</c>, <c>"text"</c> (§5: "family groups the toolbox exactly as registry::families::ALL_FAMILIES already groups the table internally").</summary>
        public string Family { get; set; } = string.Empty;

        /// <summary>
        /// A short curated glyph name (§5: "an icon glyph name (none exists yet; node.rs's static_icon
        /// table is the right precedent for a short curated name list, reused for the toolbox)"). May be
        /// <see langword="null"/> for a component the registry hasn't assigned one yet.
        /// </summary>
        public string? Icon { get; set; }

        public ChildrenModel Children { get; set; }

        /// <summary>See <see cref="Registry.LayoutKind"/>'s own doc comment for why this is nullable.</summary>
        public LayoutKind? LayoutKind { get; set; }

        public List<PropertyMeta> Properties { get; set; } = new List<PropertyMeta>();

        public List<EventMeta> Events { get; set; } = new List<EventMeta>();
    }
}
