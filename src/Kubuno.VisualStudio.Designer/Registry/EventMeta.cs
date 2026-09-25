namespace Kubuno.VisualStudio.Designer.Registry
{
    /// <summary>
    /// Mirrors one entry of <c>kubuno_views::registry::ComponentMeta.events</c> (docs/DESIGNER.md §5:
    /// "events: [{name, doc}]"), e.g. <c>{ Name = "Click" }</c> for a <c>Button</c>'s
    /// <c>OnClick="..."</c> attribute.
    /// </summary>
    public sealed class EventMeta
    {
        public string Name { get; set; } = string.Empty;

        public string? Doc { get; set; }
    }
}
