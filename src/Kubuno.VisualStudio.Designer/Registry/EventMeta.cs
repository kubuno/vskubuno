using System;
using System.Collections.Generic;
using System.Linq;

namespace Kubuno.VisualStudio.Designer.Registry
{
    /// <summary>
    /// Mirrors one entry of <c>kubuno_views::registry::ComponentMeta.events</c> as exported
    /// (<c>kubuno-views/src/registry/export.rs</c>'s <c>EventJson</c>; docs/EVENTS.md §5.1, EVT-3).
    /// <see cref="Name"/> is the FULL <c>.kbview</c> attribute name - <c>"OnClick"</c>, not a bare
    /// <c>"Click"</c> (that is <see cref="DisplayName"/>) - and every consumer reads it as-is: do not
    /// re-prepend <c>"On"</c> anywhere. An element may still carry an older attribute name for the same
    /// event (<see cref="Aliases"/>, e.g. <c>OnToggled</c> for a <c>Switch</c>'s <c>OnCheckedChanged</c>).
    /// </summary>
    public sealed class EventMeta
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>The name shown in the Events tab (<c>"Click"</c>); <see cref="Name"/> without its <c>On</c> when the export has none.</summary>
        public string? DisplayName { get; set; }

        public string? Doc { get; set; }

        /// <summary>The French user documentation (<c>doc_fr</c>), or null.</summary>
        public string? DocFr { get; set; }

        /// <summary>The Events tab group, in English as exported (<c>"Action"</c>, <c>"Mouse"</c>, <c>"Property Changed"</c>...).</summary>
        public string? Category { get; set; }

        /// <summary>The args type (<c>"MouseEventArgs"</c>); <c>"EventArgs"</c> when the export has none.</summary>
        public string? ArgsType { get; set; }

        /// <summary>The args type followed by its ancestors, root last - what makes a handler compatible (§5.3).</summary>
        public List<string> ArgsChain { get; set; } = new List<string>();

        /// <summary>The Rust args type a typed handler declares (<c>"MouseEventArgs"</c>, <c>"TextChangedEventArgs"</c>,
        /// <c>"EmptyEventArgs"</c> for the root) - EVT-4; null in an older export.</summary>
        public string? ArgsRustType { get; set; }

        /// <summary>A typed handler takes the args as <c>&amp;mut</c> (it can set <c>handled</c>/<c>cancel</c>) - EVT-4.</summary>
        public bool ArgsMut { get; set; }

        /// <summary>A handler can cancel it (<c>Validating</c>).</summary>
        public bool Cancelable { get; set; }

        /// <summary><c>"Direct"</c> or <c>"Bubble"</c>.</summary>
        public string? Routing { get; set; }

        /// <summary>Older attribute names still accepted for this event.</summary>
        public List<string> Aliases { get; set; } = new List<string>();

        /// <summary>Listed in the Events tab. A missing field (an older export) means listed.</summary>
        public bool Browsable { get; set; } = true;

        /// <summary>One of the view's own events (<c>OnLoad</c>, <c>OnShown</c>...): only the view's root element has it.</summary>
        public bool RootOnly { get; set; }

        /// <summary>One of the events every control raises (mouse, keys, focus...), rather than the component's own.</summary>
        public bool Common { get; set; }

        /// <summary>The user documentation in Visual Studio's UI language (French when available, else English).</summary>
        public string? LocalizedDoc => DesignerText.IsFrench && !string.IsNullOrEmpty(DocFr) ? DocFr : Doc;

        /// <summary><see cref="DisplayName"/>, else <see cref="Name"/> without its <c>On</c> prefix.</summary>
        public string EffectiveDisplayName =>
            !string.IsNullOrEmpty(DisplayName) ? DisplayName!
            : Name.Length > 2 && Name.StartsWith("On", StringComparison.Ordinal) && char.IsUpper(Name[2]) ? Name.Substring(2)
            : Name;

        /// <summary>The Events tab category in Visual Studio's UI language.</summary>
        public string LocalizedCategory => DesignerText.EventCategory(Category);

        /// <summary>Whether <paramref name="attributeName"/> is this event's attribute or one of its older aliases.</summary>
        public bool Matches(string attributeName) =>
            string.Equals(Name, attributeName, StringComparison.Ordinal) || Aliases.Any(a => string.Equals(a, attributeName, StringComparison.Ordinal));

        /// <summary>The canonical attribute name followed by the aliases - where an element's handler may be written.</summary>
        public IEnumerable<string> AttributeNames => new[] { Name }.Concat(Aliases);
    }
}
