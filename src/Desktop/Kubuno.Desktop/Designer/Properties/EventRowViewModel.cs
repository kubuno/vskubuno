using System;
using System.Collections.Generic;
using System.ComponentModel;
using Kubuno.Desktop.Designer.Registry;

namespace Kubuno.Desktop.Designer.Properties
{
    /// <summary>
    /// One row of the Events tab (docs/DESIGNER.md §1: "The Events tab lists ComponentMeta.events; each
    /// row shows the current On*='handler_name' attribute if present, with a dropdown of handler names
    /// already found via the language server ... plus double-click on an empty row to generate a new
    /// handler"). <see cref="RequestCreateHandler"/> only *raises* that request - DSG-10 is the package
    /// that will subscribe to it and actually call <c>kubuno/insertHandler</c> (docs/DESIGNER.md §6).
    /// </summary>
    public sealed class EventRowViewModel : INotifyPropertyChanged
    {
        private string? _handlerName;

        public EventRowViewModel(
            EventMeta @event,
            string? handlerName,
            IReadOnlyList<string> availableHandlerNames,
            string documentUri,
            string elementId)
        {
            Event = @event ?? throw new ArgumentNullException(nameof(@event));
            _handlerName = handlerName;
            AvailableHandlerNames = availableHandlerNames ?? throw new ArgumentNullException(nameof(availableHandlerNames));
            DocumentUri = documentUri ?? throw new ArgumentNullException(nameof(documentUri));
            ElementId = elementId ?? throw new ArgumentNullException(nameof(elementId));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Raised by a double-click on an empty row, or the row's "Create handler" button - the "create handler" request hook DSG-10 wires up.</summary>
        public event EventHandler? CreateHandlerRequested;

        public EventMeta Event { get; }

        public string Name => Event.Name;

        /// <summary>
        /// The XML attribute this event maps to, e.g. <c>OnClick</c>. <see cref="Registry.EventMeta.Name"/>
        /// is ALREADY the full attribute name (see that type's own doc comment - verified against the
        /// real DSG-1 registry export/fixture), so this is a direct passthrough, not a
        /// <c>"On" + Event.Name</c> computation (which previously produced <c>"OnOnClick"</c> against
        /// real data - a bug caught by DSG-8's own selection-sync work; see
        /// <c>Selection.SelectionSyncService.ApplyPropertiesPanel</c>'s doc comment for where it was
        /// first flagged).
        /// </summary>
        public string AttributeName => Event.Name;

        /// <summary>The current <c>fn</c> name from the <c>On*="..."</c> attribute, or <see langword="null"/> if unset.</summary>
        public string? HandlerName
        {
            get => _handlerName;
            set
            {
                if (_handlerName == value)
                {
                    return;
                }

                _handlerName = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HandlerName)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasHandler)));
            }
        }

        /// <summary>Handler names already known to the language server for this document (definition.rs's existing lookup) - the dropdown's source list.</summary>
        public IReadOnlyList<string> AvailableHandlerNames { get; }

        /// <summary>The open <c>.kbview</c> document's own URI - together with <see cref="ElementId"/>, everything <c>Handlers.HandlerCreationService</c> needs to call <c>kubuno/createHandler</c> for this row (DSG-10).</summary>
        public string DocumentUri { get; }

        /// <summary>The owning element's stable id (docs/DESIGNER.md §6/§8's "DSG-2 protocol").</summary>
        public string ElementId { get; }

        public bool HasHandler => !string.IsNullOrEmpty(_handlerName);

        public void RequestCreateHandler() => CreateHandlerRequested?.Invoke(this, EventArgs.Empty);
    }
}
