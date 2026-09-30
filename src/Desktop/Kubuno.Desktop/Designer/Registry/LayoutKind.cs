namespace Kubuno.Desktop.Designer.Registry
{
    /// <summary>
    /// Disambiguates a <see cref="ChildrenModel.List"/> container's drop/resize behavior
    /// (docs/DESIGNER.md §4: "<c>ChildrenModel::List</c> alone cannot tell the designer whether a
    /// container is Dock, Flow, or (once registered) Split ... the designer needs a
    /// <c>LayoutKind</c> (or equivalent) alongside <c>ChildrenModel</c>"). One of the two additive
    /// registry fields §5 calls for beyond what <c>ComponentMeta</c> carries today, so
    /// <see cref="ComponentMeta.LayoutKind"/> is nullable: <see langword="null"/> for a leaf
    /// (<see cref="ChildrenModel.None"/>) or single-slot (<see cref="ChildrenModel.SingleWidget"/>)
    /// component, and for any <see cref="ChildrenModel.List"/> container the registry hasn't
    /// classified yet.
    /// <para>
    /// Members mirror <c>kubuno_views::registry::LayoutKind</c> exactly (DSG-1,
    /// <c>kubuno-views/src/registry/mod.rs</c>) rather than §4's own narrative, which this type was
    /// first written against, before that Rust enum existed: §4 describes an "Anchor (absolute
    /// canvas)" engine and a "Dock" engine as if a container picks one or the other, but the real
    /// engine that landed (<c>Panel</c>'s) handles both per *child* (a child's own <c>Dock</c> or
    /// <c>Anchor</c> attribute decides, not the container) - one variant, <see cref="DockAnchor"/>,
    /// not two. The real enum also adds <see cref="Tabs"/> (<c>&lt;Tabs&gt;</c>'s paged layout, not a
    /// spatial arrangement at all). DSG-1 follows the real Rust type and widens this enum to match,
    /// per the rule "if the design note and the real schema disagree, follow the schema."
    /// </para>
    /// </summary>
    public enum LayoutKind
    {
        /// <summary>Sequential blocks along one axis with no <c>X</c>/<c>Y</c> (§4's "Flow (<c>&lt;Stack&gt;</c>)"), e.g. <c>Stack</c>.</summary>
        Flow,

        /// <summary><c>Panel</c>'s Dock/Anchor engine (§4's "Dock"/"Anchor (absolute canvas)"): a <c>Dock</c>-ed child stacks in document order (document order IS z-order), an <c>Anchor</c>-ed child gets free <c>X</c>/<c>Y</c> plus edge-stretch - which of the two a given child uses is its own attribute, not a per-container choice, hence one merged variant rather than two.</summary>
        DockAnchor,

        /// <summary>A resizable-ratio split (§4's "Split (<c>&lt;Splitter&gt;</c>/<c>&lt;Pane&gt;</c>)"): exactly two children, one divider.</summary>
        Split,

        /// <summary><c>Tabs</c>'s paged layout: one <c>TabItem</c> visible at a time - not a spatial arrangement of its children at all, which is why it is its own kind rather than reusing <see cref="Flow"/> or <see cref="DockAnchor"/>.</summary>
        Tabs,
    }
}
