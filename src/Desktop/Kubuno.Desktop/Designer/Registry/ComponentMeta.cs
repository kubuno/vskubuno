using System.Collections.Generic;

namespace Kubuno.Desktop.Designer.Registry
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

        /// <summary>The French user documentation (<c>doc_fr</c>), or null.</summary>
        public string? DocFr { get; set; }

        /// <summary>The user documentation in Visual Studio's UI language (French when available, else English).</summary>
        public string? LocalizedDoc => DesignerText.IsFrench && !string.IsNullOrEmpty(DocFr) ? DocFr : Doc;

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

        /// <summary>
        /// The event a double-click on the element creates a handler for (WinForms' <c>DefaultEvent</c>;
        /// docs/EVENTS.md §3/§5.2): <c>OnClick</c> for a <c>Button</c>, <c>OnCheckedChanged</c> for a
        /// <c>Switch</c>... Null for an element without events. The view's root element uses
        /// <c>OnLoad</c> instead (<see cref="DefaultEventFor"/>).
        /// </summary>
        public string? DefaultEvent { get; set; }

        /// <summary>The element's default event: <c>OnLoad</c> for the view's root element when it has it, else <see cref="DefaultEvent"/>, else its first listed event.</summary>
        public EventMeta? DefaultEventFor(bool isRoot)
        {
            if (isRoot)
            {
                var load = Events.Find(e => e.RootOnly && e.Name == "OnLoad");
                if (load is not null)
                {
                    return load;
                }
            }

            return (DefaultEvent is null ? null : Events.Find(e => e.Matches(DefaultEvent)))
                ?? Events.Find(e => e.Browsable && (isRoot || !e.RootOnly));
        }

        /// <summary>The event whose attribute (or older alias) is <paramref name="attributeName"/>.</summary>
        public EventMeta? FindEvent(string attributeName) => Events.Find(e => e.Matches(attributeName));

        // ---- EVT-7b (docs/EVENTS.md): the class hierarchy and the project's own controls ----

        /// <summary>The element's class followed by its ancestors, <c>"Component"</c> last (<c>["Button", "ButtonBase", "Control", "Component"]</c>).</summary>
        public List<string> BaseChain { get; set; } = new List<string>();

        /// <summary><c>"builtin"</c>, or <c>"project"</c> for a control of the application (<c>#[derive(Component)]</c>, <c>#[derive(UserControl)]</c>).</summary>
        public string Origin { get; set; } = "builtin";

        /// <summary><c>"control"</c>, <c>"user_control"</c> or <c>"component"</c> (non-visual: the component tray).</summary>
        public string Kind { get; set; } = "control";

        /// <summary>A non-visual component (a <c>Timer</c>): listed in the component tray under the design surface, not drawn on it.</summary>
        public bool NonVisual { get; set; }

        /// <summary>A project control compiled into the program that exported the registry (the design surface after a build), not only known from its source.</summary>
        public bool Linked { get; set; }

        /// <summary>A project control's base (a built-in class, a level such as <c>Control</c>, or another project class).</summary>
        public string? Extends { get; set; }

        /// <summary>The crate declaring a project control.</summary>
        public string? CrateName { get; set; }

        /// <summary><c>#[category("…")]</c> of a project control: its Toolbox group.</summary>
        public string? ToolboxCategory { get; set; }

        /// <summary><c>#[toolbox(icon = "…")]</c> of a project control.</summary>
        public string? ToolboxIcon { get; set; }

        /// <summary>Offered in the Toolbox (<c>#[browsable(false)]</c> hides a project control).</summary>
        public bool Browsable { get; set; } = true;

        /// <summary><c>#[default_property("…")]</c>: the property the Properties window selects first.</summary>
        public string? DefaultProperty { get; set; }

        /// <summary>A user control's view, relative to its source file.</summary>
        public string? ViewPath { get; set; }

        /// <summary>The file declaring a project control, and its line.</summary>
        public string? SourceFile { get; set; }

        public int? SourceLine { get; set; }

        /// <summary>Whether this is a control of the application.</summary>
        public bool IsProject => string.Equals(Origin, "project", System.StringComparison.Ordinal);

        /// <summary>Whether the class is <paramref name="name"/> or derives from it.</summary>
        public bool IsA(string name) => BaseChain.Contains(name);
    }
}
