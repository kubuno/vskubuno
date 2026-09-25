namespace Kubuno.VisualStudio.Designer.Registry
{
    /// <summary>
    /// Mirrors one entry of <c>kubuno_views::registry::ComponentMeta.events</c> (docs/DESIGNER.md §5:
    /// "events: [{name, doc}]"). <see cref="Name"/> is already the FULL <c>.kbview</c> attribute name -
    /// e.g. <c>"OnClick"</c>, not a bare <c>"Click"</c> - verified directly against the real DSG-1
    /// registry export (`kubuno-views/src/registry/export.rs`'s own <c>EventJson</c>) and the checked-in
    /// fixture (<c>tests/Kubuno.VisualStudio.Designer.Tests/Fixtures/registry.sample.json</c>: every
    /// <c>events[]</c> entry's <c>"name"</c> is already <c>"OnClick"</c>/<c>"OnToggled"</c>/
    /// <c>"OnChanged"</c>). <see cref="Properties.EventRowViewModel.AttributeName"/> reads this value
    /// as-is for exactly that reason - do not re-prepend <c>"On"</c> anywhere this value is consumed.
    /// </summary>
    public sealed class EventMeta
    {
        public string Name { get; set; } = string.Empty;

        public string? Doc { get; set; }
    }
}
