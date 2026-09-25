namespace Kubuno.VisualStudio.Designer.Registry
{
    /// <summary>
    /// Disambiguates a <see cref="ChildrenModel.List"/> container's drop/resize behavior
    /// (docs/DESIGNER.md §4: "<c>ChildrenModel::List</c> alone cannot tell the designer whether a
    /// container is Dock, Flow, or (once registered) Split ... the designer needs a
    /// <c>LayoutKind</c> (or equivalent) alongside <c>ChildrenModel</c>"). This is one of the two
    /// additive registry fields §5 calls for beyond what <c>ComponentMeta</c> carries today - it does
    /// not exist on the Rust side yet (DSG-1), so <see cref="ComponentMeta.LayoutKind"/> is nullable:
    /// <see langword="null"/> for a leaf (<see cref="ChildrenModel.None"/>) or single-slot
    /// (<see cref="ChildrenModel.SingleWidget"/>) component, and for any <see cref="ChildrenModel.List"/>
    /// container the registry hasn't classified yet.
    /// </summary>
    public enum LayoutKind
    {
        /// <summary>Free XY placement (§4's "Anchor (absolute canvas)"); <c>Anchor</c>/<c>X</c>/<c>Y</c>/<c>Width</c>/<c>Height</c> attached properties.</summary>
        Anchor,

        /// <summary>Bands stacking in reverse z-order along an edge (§4's "Dock"); document order is z-order.</summary>
        Dock,

        /// <summary>Sequential blocks along one axis with no <c>X</c>/<c>Y</c> (§4's "Flow (<c>&lt;Stack&gt;</c>)"), e.g. <c>Stack</c>.</summary>
        Flow,

        /// <summary>A resizable-ratio split (§4's "Split (<c>&lt;Splitter&gt;</c>/<c>&lt;Pane&gt;</c>)") - not yet a registered component anywhere.</summary>
        Split,
    }
}
