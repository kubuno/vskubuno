namespace Kubuno.VisualStudio.Designer.Registry
{
    /// <summary>
    /// Mirrors <c>kubuno_views::registry::ChildrenModel</c> (docs/DESIGNER.md §5: "children:
    /// 'None'|'SingleWidget'|'List'"). Member names are spelled exactly like the Rust enum's own
    /// variants - see <see cref="RegistryJsonOptions"/> for why that lets a plain
    /// <c>JsonStringEnumConverter</c> round-trip this type with no extra naming policy.
    /// </summary>
    public enum ChildrenModel
    {
        /// <summary>A leaf component - no children slot at all (e.g. <c>Button</c>).</summary>
        None,

        /// <summary>Exactly one child slot (e.g. <c>Card</c>'s body).</summary>
        SingleWidget,

        /// <summary>An ordered list of children, arranged per <see cref="LayoutKind"/> (e.g. <c>Stack</c>).</summary>
        List,
    }
}
