using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Linq;
using Kubuno.VisualStudio.Designer.Registry;
using Kubuno.VisualStudio.Designer.Selection;

namespace Kubuno.VisualStudio.Designer.PropertyBrowser
{
    /// <summary>
    /// One <c>.kbview</c> element as Visual Studio's native Properties window (F4) sees it: published by
    /// the designer pane through <c>ITrackSelection</c>/<c>SelectionContainer</c>, and described entirely
    /// by an <see cref="ICustomTypeDescriptor"/> generated from the component registry - no CLR type per
    /// component. The Properties window then behaves like it does for a WinForms control:
    /// <list type="bullet">
    /// <item>one row per registry property (plus <c>(Name)</c> = <c>x:Name</c> and the layout attributes
    /// every element accepts), grouped in WinForms-like categories (<see cref="PropertyCategoryMap"/>), with
    /// the registry doc as the description, enum/bool dropdowns, bold when set to a non-default value and
    /// "Reset" to remove the attribute;</item>
    /// <item>the Events tab (the ⚡ button) listing the component's registry events through
    /// <see cref="GetEvents()"/> and <see cref="KbviewEventBindingService"/> (see that class for how the tab
    /// appears); double-clicking one creates/shows the handler (DSG-10).</item>
    /// </list>
    /// Values are always read back from the live buffer text (<see cref="IKbviewElementHost.GetCurrentText"/>,
    /// cached per buffer version), so the grid follows XML edits too; an edit is written through
    /// <see cref="IKbviewElementHost"/> as a surgical attribute edit (one undo unit), and shown optimistically
    /// until the buffer catches up. Implements <see cref="IComponent"/> because <c>PropertyGrid</c> only
    /// wires events for components (its <c>ViewEvent</c> bails out otherwise).
    /// </summary>
    public sealed class KbviewElementObject : ICustomTypeDescriptor, IComponent
    {
        private static readonly (string Name, PropKind Kind)[] CommonAttributes =
        {
            // kubuno-views-ls/src/common_attrs.rs's COMMON_ATTRIBUTES, same order (user docs: DesignerText.CommonAttributeDoc).
            ("Dock", PropKind.CreateEnum(new[] { "None", "Top", "Bottom", "Left", "Right", "Fill" })),
            ("Anchor", PropKind.String),
            ("X", PropKind.F32),
            ("Y", PropKind.F32),
            ("Width", PropKind.F32),
            ("Height", PropKind.F32),
        };

        /// <summary><c>kubuno_views::registry::DESIGN_TIME_ATTRIBUTES</c>: the view's canvas size, root element only, ignored at runtime.</summary>
        private static readonly string[] DesignTimeAttributes = { "DesignWidth", "DesignHeight" };

        private readonly IKbviewElementHost _host;
        private readonly Dictionary<string, (int Version, string? Value)> _pending = new Dictionary<string, (int, string?)>(StringComparer.Ordinal);
        private PropertyDescriptorCollection? _properties;
        private PropertyDescriptorCollection? _eventProperties;
        private EventDescriptorCollection? _events;
        private int _cachedVersion = int.MinValue;
        private ElementAttributes? _cached;

        public KbviewElementObject(IKbviewElementHost host, string elementId, ComponentMeta component)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            ElementId = elementId ?? throw new ArgumentNullException(nameof(elementId));
            Component = component ?? throw new ArgumentNullException(nameof(component));
            Site = new KbviewElementSite(this);
        }

        public event EventHandler? Disposed;

        /// <summary>The stable element id (docs/DESIGNER.md §8) this object edits.</summary>
        public string ElementId { get; }

        public ComponentMeta Component { get; }

        internal IKbviewElementHost Host => _host;

        public ISite? Site { get; set; }

        /// <summary>The element's <c>x:Name</c>, or null.</summary>
        public string? XName => GetRawValue("x:Name");

        /// <summary>
        /// The raw attribute value as currently written (an optimistic, not-yet-applied edit first), or
        /// null when the attribute is absent.
        /// </summary>
        public string? GetRawValue(string attributeName)
        {
            if (_pending.TryGetValue(attributeName, out var pending))
            {
                if (pending.Version == _host.CurrentVersion)
                {
                    return pending.Value;
                }

                _pending.Remove(attributeName);
            }

            var attributes = ReadAttributes();
            return attributes is not null && attributes.Attributes.TryGetValue(attributeName, out var value) ? value : null;
        }

        /// <summary>Whether this element still resolves against the current text (a stale id after a structural edit does not).</summary>
        public bool IsValid => ReadAttributes() is { } attributes && string.Equals(attributes.TagName, Component.Name, StringComparison.Ordinal);

        internal void SetAttribute(string name, string value)
        {
            _pending[name] = (_host.CurrentVersion, value);
            _host.SetAttribute(ElementId, name, value);
        }

        internal void RemoveAttribute(string name)
        {
            _pending[name] = (_host.CurrentVersion, null);
            _host.RemoveAttribute(ElementId, name);
        }

        private ElementAttributes? ReadAttributes()
        {
            var version = _host.CurrentVersion;
            if (version != _cachedVersion)
            {
                _cached = ElementAttributeReader.Read(_host.GetCurrentText(), ElementId);
                _cachedVersion = version;
            }

            return _cached;
        }

        /// <summary>The Properties tab's rows (built once per object; values are read live).</summary>
        public PropertyDescriptorCollection GetAttributeProperties()
        {
            if (_properties is null)
            {
                var list = new List<PropertyDescriptor>
                {
                    new KbviewAttributePropertyDescriptor("x:Name", "Name", kind: null, defaultValue: null, DesignerText.NameDescription, PropertyCategoryMap.Category.Design),
                };

                var seen = new HashSet<string>(StringComparer.Ordinal) { "x:Name" };
                foreach (var property in Component.Properties)
                {
                    // A project control's `#[browsable(false)]` property stays settable in XML but is not listed (EVT-7b).
                    if (property.Browsable && seen.Add(property.Name))
                    {
                        list.Add(new KbviewAttributePropertyDescriptor(property.Name, property.Name, property.Kind, property.Default, property.LocalizedDoc, PropertyCategoryMap.For(property.Name, property.Kind), customCategory: property.Category));
                    }
                }

                // Dock/Anchor get the Windows Forms pickers; like WinForms (which keeps them visible for a control
                // in a FlowLayoutPanel), they stay listed under a parent that does not lay out by them, but
                // greyed out with a description saying so - the validator warns when they are set there.
                var dockAnchorParent = ParentLaysOutByDockAnchor();
                foreach (var (name, kind) in CommonAttributes)
                {
                    if (seen.Add(name))
                    {
                        var isDockOrAnchor = name == "Dock" || name == "Anchor";
                        System.Drawing.Design.UITypeEditor? editor = name == "Dock" ? new KbviewDockEditor() : name == "Anchor" ? new KbviewAnchorEditor() : null;
                        var doc = isDockOrAnchor && !dockAnchorParent ? DesignerText.DockAnchorOnlyInPanel(name) : DesignerText.CommonAttributeDoc(name);
                        // Like WinForms, an absent Dock/Anchor shows its default value ("None" / "Top, Left"), non-bold.
                        var defaultValue = name == "Dock" ? LayoutAttributeText.DefaultDock : name == "Anchor" ? LayoutAttributeText.DefaultAnchor : null;
                        list.Add(new KbviewAttributePropertyDescriptor(name, name, kind, defaultValue, doc, PropertyCategoryMap.Category.Layout, editor, readOnly: isDockOrAnchor && !dockAnchorParent));
                    }
                }

                // The view itself (the root element): its design-time canvas size (docs/DESIGNER.md §12).
                if (ElementId.Length == 0)
                {
                    foreach (var name in DesignTimeAttributes)
                    {
                        if (seen.Add(name))
                        {
                            list.Add(new KbviewAttributePropertyDescriptor(name, name, PropKind.F32, defaultValue: null, DesignerText.DesignTimeAttributeDoc(name), PropertyCategoryMap.Category.Layout));
                        }
                    }
                }

                _properties = new PropertyDescriptorCollection(list.ToArray(), readOnly: true);
            }

            return _properties;
        }

        /// <summary>Whether this element's parent places its children by Dock/Anchor (a <c>Panel</c>); false for the root.</summary>
        private bool ParentLaysOutByDockAnchor()
        {
            if (!Editing.DesignerStructurePlanner.TrySplit(ElementId, out var parentId, out _))
            {
                return false;
            }

            var parent = ElementAttributeReader.Read(_host.GetCurrentText(), parentId);
            return parent is not null && _host.Registry.Find(parent.TagName)?.LayoutKind == LayoutKind.DockAnchor;
        }

        /// <summary>The Events tab's rows - one string-valued row per registry event (the <c>On*</c> attribute holding the handler name).</summary>
        public PropertyDescriptorCollection GetEventProperties()
        {
            if (_eventProperties is null)
            {
                _eventProperties = new PropertyDescriptorCollection(
                    GetEvents().Cast<KbviewEventDescriptor>().Select(e => (PropertyDescriptor)new KbviewEventPropertyDescriptor(e)).ToArray(),
                    readOnly: true);
            }

            return _eventProperties;
        }

        public EventDescriptorCollection GetEvents()
        {
            if (_events is null)
            {
                // Browsable events only; the view's own events (Load, Shown...) on its root element only.
                _events = new EventDescriptorCollection(
                    Component.Events.Where(e => e.Browsable && (IsRoot || !e.RootOnly)).Select(e => (EventDescriptor)new KbviewEventDescriptor(e, this)).ToArray(),
                    readOnly: true);
            }

            return _events;
        }

        /// <summary>Whether this is the view's root element (it alone has the view events: Load, Shown...).</summary>
        public bool IsRoot => ElementId.Length == 0;

        /// <summary>
        /// The element's default event (docs/EVENTS.md §3, WinForms' <c>DefaultEvent</c>): the registry's
        /// <c>default_event</c> (<c>OnClick</c> for a <c>Button</c>, <c>OnCheckedChanged</c> for a <c>Switch</c>...),
        /// <c>OnLoad</c> for the view's root element. What a double-click on the element in the designer creates.
        /// </summary>
        public EventMeta? DefaultEventMeta => Component.DefaultEventFor(IsRoot);

        /// <summary>The Events tab's default row (see <see cref="DefaultEventMeta"/>).</summary>
        public PropertyDescriptor? DefaultEventProperty => DefaultEventMeta is { } e ? GetEventProperties().Find(e.Name, ignoreCase: false) : null;

        public override string ToString() => XName is { Length: > 0 } name ? $"{name} ({Component.Name})" : Component.Name;

        // ---- ICustomTypeDescriptor ----

        public AttributeCollection GetAttributes() => new AttributeCollection(
            new DefaultPropertyAttribute(DefaultPropertyName),
            new DefaultEventAttribute(DefaultEventMeta?.Name));

        /// <summary>The property the Properties window selects first: a project control's <c>#[default_property]</c> (EVT-7b), else the first one.</summary>
        private string DefaultPropertyName => Component.DefaultProperty is { Length: > 0 } declared && Component.Properties.Exists(p => p.Name == declared)
            ? declared
            : Component.Properties.Count > 0 ? Component.Properties[0].Name : "x:Name";

        public string GetClassName() => Component.Name;

        public string? GetComponentName() => XName;

        public TypeConverter GetConverter() => new TypeConverter();

        public EventDescriptor? GetDefaultEvent() => DefaultEventMeta is { } e ? GetEvents().Find(e.Name, ignoreCase: false) : null;

        public PropertyDescriptor? GetDefaultProperty()
        {
            var properties = GetAttributeProperties();
            return properties.Find(DefaultPropertyName, ignoreCase: false) ?? properties[0];
        }

        public object? GetEditor(Type editorBaseType) => null;

        public EventDescriptorCollection GetEvents(Attribute[]? attributes) => GetEvents();

        public PropertyDescriptorCollection GetProperties() => GetAttributeProperties();

        public PropertyDescriptorCollection GetProperties(Attribute[]? attributes) => GetAttributeProperties();

        public object GetPropertyOwner(PropertyDescriptor? pd) => this;

        // ---- IComponent ----

        public void Dispose() => Disposed?.Invoke(this, EventArgs.Empty);
    }
}
