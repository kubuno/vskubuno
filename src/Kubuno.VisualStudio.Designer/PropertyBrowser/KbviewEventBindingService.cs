using System;
using System.Collections;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Linq;

namespace Kubuno.VisualStudio.Designer.PropertyBrowser
{
    /// <summary>
    /// The <see cref="IEventBindingService"/> behind the Properties window's Events tab (⚡) for
    /// <c>.kbview</c> elements - the same contract the WinForms designer implements for its controls.
    ///
    /// <para><b>How the ⚡ tab appears</b> (checked by decompiling the installed Visual Studio 18
    /// <c>Microsoft.VisualStudio.dll</c>, <c>Microsoft.VisualStudio.PropertyBrowser.PropertyBrowser</c>):
    /// the Properties window always hosts its own <c>VsEventsTab</c>, but only shows its toolbar button
    /// when the ACTIVE designer (<c>IDesignerEventService.ActiveDesigner</c>) offers an
    /// <see cref="IEventBindingService"/> and the selected object is an <see cref="IComponent"/>. Visual
    /// Studio's <c>IDesignerEventService</c> is its <c>DesignSurfaceManager</c>, which makes the design
    /// surface of the active document frame the active designer by asking the frame's doc view for
    /// <c>IVSMDDesigner</c> (<c>Microsoft.VisualStudio.Design.VSDesignSurfaceManager.GetSurfaceFromFrame</c>).
    /// So the designer pane owns a (never loaded, component-less) <see cref="DesignSurface"/> created by
    /// that manager, answers <c>IVSMDDesigner</c> with it, and registers this service in its service
    /// container - exactly the hook the WinForms designer uses, with our registry-described elements as
    /// the components. The package also proffers it as a global service: found live, the grid's own
    /// service chain for a double-click ends there and does not always reach the active designer.</para>
    ///
    /// <para><b>A double-click on an event row</b> then runs <c>PropertyGrid</c>'s own WinForms logic:
    /// <see cref="GetEvent"/> identifies the row as an event, <see cref="CreateUniqueMethodName"/> proposes
    /// the name (the same default <c>kubuno/createHandler</c> uses), the row's value is set
    /// (<see cref="KbviewEventPropertyDescriptor.SetValue"/> = create the handler, DSG-10) and
    /// <see cref="ShowCode(IComponent, EventDescriptor)"/> navigates to it. Stateless: everything it needs
    /// hangs off the <see cref="KbviewElementObject"/> it is handed.</para>
    /// </summary>
    public sealed class KbviewEventBindingService : IEventBindingService
    {
        public static KbviewEventBindingService Instance { get; } = new KbviewEventBindingService();

        public string CreateUniqueMethodName(IComponent component, EventDescriptor e)
        {
            var element = component as KbviewElementObject;
            return AttributeValueRules.DefaultHandlerName(element?.XName, element?.Component.Name ?? string.Empty, e?.Name ?? string.Empty);
        }

        /// <summary>Handler names are not enumerated from the code-behind yet (no language-server method for it) - an empty list, which only means the grid always navigates after binding.</summary>
        public ICollection GetCompatibleMethods(EventDescriptor e) => Array.Empty<string>();

        public EventDescriptor? GetEvent(PropertyDescriptor property) => (property as KbviewEventPropertyDescriptor)?.EventDescriptor;

        public PropertyDescriptorCollection GetEventProperties(EventDescriptorCollection events) =>
            new PropertyDescriptorCollection(events.Cast<EventDescriptor>().OfType<KbviewEventDescriptor>().Select(e => (PropertyDescriptor)new KbviewEventPropertyDescriptor(e)).ToArray());

        public PropertyDescriptor GetEventProperty(EventDescriptor e) =>
            e is KbviewEventDescriptor kbview ? new KbviewEventPropertyDescriptor(kbview) : throw new ArgumentException("Not a .kbview event.", nameof(e));

        public bool ShowCode() => false;

        public bool ShowCode(int lineNumber) => false;

        public bool ShowCode(IComponent component, EventDescriptor e)
        {
            if (component is not KbviewElementObject element || e is null)
            {
                return false;
            }

            // The row's SetValue (just before this call) already created the handler / navigated to it;
            // a second request while that one is in flight would create a SECOND handler (the attribute is
            // not written yet, so the server would see an unbound event again).
            if (!element.Host.IsHandlerRequestRecent(element.ElementId, e.Name) && !string.IsNullOrEmpty(element.GetRawValue(e.Name)))
            {
                element.Host.CreateOrShowHandler(element.ElementId, e.Name, null);
            }

            return true;
        }
    }

    /// <summary>The <see cref="ISite"/> of a <see cref="KbviewElementObject"/>: names it (<c>x:Name</c>) and serves the <see cref="IEventBindingService"/> (the grid's fallback when it types a handler name).</summary>
    public sealed class KbviewElementSite : ISite
    {
        private readonly KbviewElementObject _element;

        public KbviewElementSite(KbviewElementObject element)
        {
            _element = element ?? throw new ArgumentNullException(nameof(element));
        }

        public IComponent Component => _element;

        public IContainer? Container => null;

        public bool DesignMode => true;

        public string? Name
        {
            get => _element.XName;
            set
            {
            }
        }

        public object? GetService(Type serviceType) => serviceType == typeof(IEventBindingService) ? KbviewEventBindingService.Instance : null;
    }
}
