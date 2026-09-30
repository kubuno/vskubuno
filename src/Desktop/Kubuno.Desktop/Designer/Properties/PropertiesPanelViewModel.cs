using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Kubuno.Desktop.Designer.Registry;

namespace Kubuno.Desktop.Designer.Properties
{
    /// <summary>Raised when a row's "create handler" hook fires - see <see cref="EventRowViewModel.CreateHandlerRequested"/> for the exact scope.</summary>
    public sealed class CreateHandlerRequestedEventArgs : EventArgs
    {
        public CreateHandlerRequestedEventArgs(EventRowViewModel row)
        {
            Row = row;
        }

        public EventRowViewModel Row { get; }
    }

    /// <summary>
    /// The Properties/Events tool window's whole state for the currently-selected design-surface element
    /// (docs/DESIGNER.md §1: "Properties window with Properties and Events tabs ... driven entirely by
    /// registry metadata"). Selection sync itself (DSG-8) and turning a row edit into a
    /// <c>kubuno/applyEdit</c> request (Editing/) are both out of this package's scope; this view-model
    /// only holds the rows a caller builds from a <see cref="ComponentMeta"/> plus the element's current
    /// attribute/handler state.
    /// </summary>
    public sealed class PropertiesPanelViewModel : INotifyPropertyChanged
    {
        private static readonly IReadOnlyList<PropertyRowViewModel> NoProperties = Array.Empty<PropertyRowViewModel>();
        private static readonly IReadOnlyList<EventRowViewModel> NoEvents = Array.Empty<EventRowViewModel>();

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Bubbled from every current row's <see cref="EventRowViewModel.CreateHandlerRequested"/>.</summary>
        public event EventHandler<CreateHandlerRequestedEventArgs>? CreateHandlerRequested;

        public ComponentMeta? Component { get; private set; }

        public IReadOnlyList<PropertyRowViewModel> Properties { get; private set; } = NoProperties;

        public IReadOnlyList<EventRowViewModel> Events { get; private set; } = NoEvents;

        public bool HasSelection => Component is not null;

        /// <summary>
        /// Populates <see cref="Properties"/>/<see cref="Events"/> for a newly-selected element.
        /// <paramref name="attributeValues"/>/<paramref name="eventHandlers"/> hold only the attributes
        /// actually present on the element (missing keys fall back to each <see cref="PropertyMeta.Default"/>
        /// via <see cref="PropertyRowViewModel.EffectiveValue"/>). <paramref name="documentUri"/>/
        /// <paramref name="elementId"/> identify the selected element itself (the open <c>.kbview</c>
        /// document's own URI, and its stable id, docs/DESIGNER.md §6/§8's "DSG-2 protocol") - carried
        /// through to each <see cref="EventRowViewModel"/> so a "create handler" request
        /// (<see cref="CreateHandlerRequested"/>) has everything <c>Handlers.HandlerCreationService</c>
        /// needs without this view-model depending on that package itself (DSG-10).
        /// </summary>
        public void SetSelection(
            ComponentMeta component,
            IReadOnlyDictionary<string, string?> attributeValues,
            IReadOnlyDictionary<string, string?> eventHandlers,
            IReadOnlyList<string> availableHandlerNames,
            string documentUri,
            string elementId)
        {
            if (component is null)
            {
                throw new ArgumentNullException(nameof(component));
            }

            if (attributeValues is null)
            {
                throw new ArgumentNullException(nameof(attributeValues));
            }

            if (eventHandlers is null)
            {
                throw new ArgumentNullException(nameof(eventHandlers));
            }

            if (availableHandlerNames is null)
            {
                throw new ArgumentNullException(nameof(availableHandlerNames));
            }

            if (documentUri is null)
            {
                throw new ArgumentNullException(nameof(documentUri));
            }

            if (elementId is null)
            {
                throw new ArgumentNullException(nameof(elementId));
            }

            Component = component;

            Properties = component.Properties
                .Select(p => new PropertyRowViewModel(p, attributeValues.TryGetValue(p.Name, out var v) ? v : null))
                .ToList();

            Events = component.Events
                .Select(e =>
                {
                    var row = new EventRowViewModel(
                        e,
                        eventHandlers.TryGetValue(e.Name, out var h) ? h : null,
                        availableHandlerNames,
                        documentUri,
                        elementId);
                    row.CreateHandlerRequested += (_, _) => CreateHandlerRequested?.Invoke(this, new CreateHandlerRequestedEventArgs(row));
                    return row;
                })
                .ToList();

            RaiseAll();
        }

        public void ClearSelection()
        {
            if (Component is null)
            {
                return;
            }

            Component = null;
            Properties = NoProperties;
            Events = NoEvents;
            RaiseAll();
        }

        private void RaiseAll()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Component)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Properties)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Events)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasSelection)));
        }
    }
}
