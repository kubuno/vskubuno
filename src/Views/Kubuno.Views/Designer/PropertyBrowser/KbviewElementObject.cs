using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Linq;
using Kubuno.Views.Designer.Registry;
using Kubuno.Views.Designer.Selection;

namespace Kubuno.Views.Designer.PropertyBrowser
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

        /// <summary><c>kubuno_desktop_views::registry::DESIGN_TIME_ATTRIBUTES</c>: the view's canvas size, root element only, ignored at runtime.</summary>
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

        // Public for the target layers (the desktop Dev Assistant parts read the selected element's view).
        public IKbviewElementHost Host => _host;

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
                var dockAnchorParent = ParentLaysOutByDockAnchor();
                var toolTipProvider = ToolTipProviderName();
                foreach (var property in Component.Properties)
                {
                    // A hidden property (a ribbon element's X, Y, Dock...) also keeps the fallback below from listing it.
                    if (!property.Browsable)
                    {
                        seen.Add(property.Name);
                    }

                    // A project control's `#[browsable(false)]` property stays settable in XML but is not listed (EVT-7b);
                    // the view's own (form) properties belong to its root element only.
                    if (!property.Browsable || (property.RootOnly && !IsRoot) || !seen.Add(property.Name))
                    {
                        continue;
                    }

                    list.Add(DescriptorFor(property.Name, property.Kind, property, dockAnchorParent, toolTipProvider));
                }

                // An older export without the Control level's properties: the layout attributes every element accepts.
                foreach (var (name, kind) in CommonAttributes)
                {
                    if (seen.Add(name))
                    {
                        list.Add(DescriptorFor(name, kind, null, dockAnchorParent, toolTipProvider));
                    }
                }

                // Location and Size, expandable like WinForms', over X/Y and Width/Height (whose own rows are not listed).
                var ownSize = Component.Properties.Exists(p => p.Name == "Size" && p.InheritedFrom is null);
                var ownLocation = Component.Properties.Exists(p => p.Name == "Location" && p.InheritedFrom is null);
                var layout = PropertyCategoryMap.DisplayName(PropertyCategoryMap.Category.Layout);
                // Not for an element whose position or size the component hides (a ribbon lays its elements out).
                bool Hidden(string name) => Component.Properties.Exists(p => p.Name == name && !p.Browsable);
                if (!Hidden("X") && !Hidden("Y"))
                {
                    list.Add(new KbviewCompositePropertyDescriptor(new TwoAttributeSpec(LocationRow, "X", "Y", autoWhenAbsent: false), ownLocation ? DesignerText.PositionName : "Location", DesignerText.LocationDoc, layout));
                }

                if (!Hidden("Width") && !Hidden("Height"))
                {
                    list.Add(new KbviewCompositePropertyDescriptor(new TwoAttributeSpec(SizeRow, "Width", "Height", autoWhenAbsent: true), ownSize ? DesignerText.DimensionsName : "Size", DesignerText.SizeDoc, layout));
                }

                // (DataBindings), and the children collections (Columns, TabPages, Items...).
                var visible = Component.Properties.Where(p => p.Browsable && (!p.RootOnly || IsRoot)).ToList();
                if (visible.Count > 0)
                {
                    list.Add(new KbviewBindingsPropertyDescriptor(KbviewBindingsPropertyDescriptor.BindableOf(visible), visible));
                }

                list.AddRange(KbviewChildrenPropertyDescriptor.RowsFor(Component));
                if (Component.Name == "RibbonTab")
                {
                    // docs/RIBBON.md section 5: the tab's collapse order.
                    list.Add(new Ribbon.RibbonScalingPolicyDescriptor(PropertyCategoryMap.DisplayName(PropertyCategoryMap.Category.Layout)));
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

        /// <summary>The internal name of the expandable Location row (never clashes with a component's own property).</summary>
        public const string LocationRow = "Bounds.Location";

        /// <summary>The internal name of the expandable Size row (a Button has its own <c>Size</c>).</summary>
        public const string SizeRow = "Bounds.Size";

        private static readonly HashSet<string> BoundsAttributes = new HashSet<string>(StringComparer.Ordinal) { "X", "Y", "Width", "Height" };

        /// <summary>The row of attribute <paramref name="name"/>: the Dock/Anchor pickers, the expandable rows, or a plain one.</summary>
        private PropertyDescriptor DescriptorFor(string name, PropKind kind, PropertyMeta? property, bool dockAnchorParent, string? toolTipProvider)
        {
            var category = PropertyCategoryMap.For(name, kind);
            var doc = property?.LocalizedDoc is { Length: > 0 } registryDoc ? registryDoc : DesignerText.CommonAttributeDoc(name);
            if (name == "Dock" || name == "Anchor")
            {
                // Dock/Anchor get the Windows Forms pickers; like WinForms (which keeps them visible for a control in a
                // FlowLayoutPanel), they stay listed under a parent that does not lay out by them, but greyed out with a
                // description saying so - the validator warns when they are set there. An absent value shows its default
                // ("None" / "Top, Left"), non-bold.
                System.Drawing.Design.UITypeEditor editor = name == "Dock" ? new KbviewDockEditor() : new KbviewAnchorEditor();
                var defaultValue = name == "Dock" ? LayoutAttributeText.DefaultDock : LayoutAttributeText.DefaultAnchor;
                return new KbviewAttributePropertyDescriptor(name, name, kind, defaultValue, dockAnchorParent ? doc : DesignerText.DockAnchorOnlyInPanel(name), PropertyCategoryMap.Category.Layout, editor, readOnly: !dockAnchorParent, customCategory: property?.Category, meta: property);
            }

            if (BoundsAttributes.Contains(name))
            {
                // Shown through Location / Size; still reachable by name (a multi-selection edit, the tests).
                return new KbviewAttributePropertyDescriptor(name, name, kind, property?.Default, doc, PropertyCategoryMap.Category.Layout, customCategory: property?.Category, meta: property, browsable: false);
            }

            var displayCategory = property?.Category is { Length: > 0 } c ? PropertyCategoryMap.DisplayName(c) : PropertyCategoryMap.DisplayName(category);
            switch (property?.TypeConverter)
            {
                case "Padding":
                    return new KbviewCompositePropertyDescriptor(NumberListSpec.Padding(name, property.Default), name, doc, displayCategory);
                case "Size":
                    return new KbviewCompositePropertyDescriptor(NumberListSpec.Size(name, property.Default), name, doc, displayCategory);
            }

            var display = name == "ToolTip" ? DesignerText.ToolTipOn(toolTipProvider) : name;
            var shownDefault = property?.Editor == "font" && string.IsNullOrEmpty(property.Default) ? FontText.AmbientDefault : property?.Default;
            // Every icon property is listed with the icon's options (docs/ICONS.md), unless its declaration puts it elsewhere (a
            // window's Icon in Window Style, like WinForms).
            var custom = property?.Editor == "icon" && (property.Category is null || property.Category == "Appearance") ? "Icon" : property?.Category;
            return new KbviewAttributePropertyDescriptor(name, display, kind, shownDefault, doc, category, customCategory: custom, meta: property);
        }

        /// <summary>The <c>x:Name</c> of the view's first <c>&lt;ToolTip&gt;</c> component (the WinForms "ToolTip on toolTip1" extender), or null.</summary>
        private string? ToolTipProviderName() =>
            Selection.ViewDocument.Parse(_host.GetCurrentText())?.DescendantsAndSelf().FirstOrDefault(n => n.Name == "ToolTip" && !string.IsNullOrEmpty(n.Attribute("x:Name")))?.Attribute("x:Name");

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
