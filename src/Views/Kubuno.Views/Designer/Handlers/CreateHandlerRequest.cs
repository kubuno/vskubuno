using System;
using System.Collections.Generic;

namespace Kubuno.Desktop.Designer.Handlers
{
    /// <summary>
    /// The <c>kubuno/createHandler</c> request (docs/DESIGNER.md §6/§8, DSG-10 - "double-click on a
    /// control/event -&gt; create the handler"). <see cref="EventName"/> is the registry's own event name
    /// exactly as <c>kubuno_views::registry::EventMeta.name</c> carries it (e.g. <c>"OnClick"</c> -
    /// already the <c>.kbview</c> attribute name, not a bare <c>"Click"</c>).
    /// </summary>
    public sealed class CreateHandlerRequest
    {
        public CreateHandlerRequest(string kbviewUri, string elementId, string eventName, string? suggestedName = null, IDictionary<string, string>? openFiles = null)
        {
            OpenFiles = openFiles ?? new Dictionary<string, string>();
            KbviewUri = kbviewUri ?? throw new ArgumentNullException(nameof(kbviewUri));
            ElementId = elementId ?? throw new ArgumentNullException(nameof(elementId));
            EventName = eventName ?? throw new ArgumentNullException(nameof(eventName));
            SuggestedName = suggestedName;
        }

        /// <summary>The open <c>.kbview</c> document's own URI - the element the double-click happened on lives here.</summary>
        public string KbviewUri { get; }

        /// <summary>The stable element id (docs/DESIGNER.md §6/§8's "DSG-2 protocol" - a dot-separated path of child-ordinal indices) the double-click resolved to.</summary>
        public string ElementId { get; }

        public string EventName { get; }

        /// <summary><see langword="null"/> to let the server pick <c>on_&lt;xname or element&gt;_&lt;event&gt;</c> itself (docs/DESIGNER.md's own DSG-10 naming rule).</summary>
        public string? SuggestedName { get; }

        /// <summary>The texts of the open code-behind documents (<c>uri -&gt; text</c>): the server computes its offsets against them, not the files (EVT-5).</summary>
        public IDictionary<string, string> OpenFiles { get; }
    }
}
