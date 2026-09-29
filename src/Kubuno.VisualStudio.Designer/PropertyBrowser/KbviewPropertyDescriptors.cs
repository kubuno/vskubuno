using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Kubuno.VisualStudio.Designer.Registry;

namespace Kubuno.VisualStudio.Designer.PropertyBrowser
{
    /// <summary>
    /// One Properties-window row for a <c>.kbview</c> attribute. Always string-typed on purpose: whatever
    /// the registry kind, the attribute may hold a <c>{Binding ...}</c> expression, which is shown and
    /// edited as-is (docs/DESIGNER.md §1); the kind only drives the dropdown (<see cref="GetConverter"/>)
    /// and validation (<see cref="AttributeValueRules"/>).
    /// </summary>
    public sealed class KbviewAttributePropertyDescriptor : PropertyDescriptor
    {
        private readonly string? _default;
        private readonly TypeConverter _converter;

        private readonly System.Drawing.Design.UITypeEditor? _editor;
        private readonly bool _readOnly;

        /// <param name="editor">A drop-down editor for the row (the Dock/Anchor pickers), or null.</param>
        /// <param name="readOnly">Greys the row out (e.g. Dock/Anchor under a parent that does not lay out by them).</param>
        /// <param name="customCategory">A project control's own <c>#[category("…")]</c> (docs/EVENTS.md EVT-7b), which wins over <paramref name="category"/>.</param>
        public KbviewAttributePropertyDescriptor(string attributeName, string displayName, PropKind? kind, string? defaultValue, string? doc, PropertyCategoryMap.Category category, System.Drawing.Design.UITypeEditor? editor = null, bool readOnly = false, string? customCategory = null)
            : base(attributeName, BuildAttributes(attributeName, displayName, doc, category, customCategory))
        {
            _editor = editor;
            _readOnly = readOnly;
            AttributeName = attributeName;
            Kind = kind;
            _default = defaultValue;
            _converter = kind?.Tag switch
            {
                PropKindTag.Bool => new AttributeValuesConverter(new[] { "true", "false" }),
                PropKindTag.Enum => new AttributeValuesConverter(kind.EnumVariants),
                _ => new StringConverter(),
            };
        }

        /// <summary>The XML attribute name (<c>x:Name</c>, <c>Text</c>, <c>Dock</c>...).</summary>
        public string AttributeName { get; }

        /// <summary>The registry kind, or null for the free-form <c>x:Name</c>.</summary>
        public PropKind? Kind { get; }

        public override Type ComponentType => typeof(KbviewElementObject);

        public override bool IsReadOnly => _readOnly;

        public override object? GetEditor(Type editorBaseType) =>
            _editor is not null && editorBaseType == typeof(System.Drawing.Design.UITypeEditor) ? _editor : base.GetEditor(editorBaseType);

        public override Type PropertyType => typeof(string);

        public override TypeConverter Converter => _converter;

        public override bool CanResetValue(object component) => Element(component)?.GetRawValue(AttributeName) is not null;

        public override object GetValue(object? component) => Element(component)?.GetRawValue(AttributeName) ?? _default ?? string.Empty;

        public override void ResetValue(object component)
        {
            var element = Element(component);
            if (element?.GetRawValue(AttributeName) is not null)
            {
                element.RemoveAttribute(AttributeName);
            }
        }

        public override void SetValue(object? component, object? value)
        {
            var element = Element(component);
            if (element is null)
            {
                return;
            }

            var text = value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            var current = element.GetRawValue(AttributeName);
            if (text.Length == 0)
            {
                // Clearing a value removes the attribute (back to the component default).
                if (current is not null)
                {
                    element.RemoveAttribute(AttributeName);
                }

                return;
            }

            var normalized = Kind is null ? AttributeValueRules.NormalizeName(text)
                : IsAnchor ? LayoutAttributeText.NormalizeAnchor(text)
                : AttributeValueRules.Normalize(Kind, text);
            if (string.Equals(normalized, current, StringComparison.Ordinal))
            {
                return;
            }

            element.SetAttribute(AttributeName, normalized);
        }

        /// <summary>Bold in the grid (WinForms' "non-default value") when the attribute is written and differs from the registry default.</summary>
        public override bool ShouldSerializeValue(object component)
        {
            var raw = Element(component)?.GetRawValue(AttributeName);
            if (raw is null)
            {
                return false;
            }

            // "Left, Top" is the default anchoring too: compare the edges, not the text.
            return IsAnchor ? !LayoutAttributeText.SameAnchor(raw, _default) : !string.Equals(raw, _default, StringComparison.Ordinal);
        }

        private bool IsAnchor => string.Equals(AttributeName, "Anchor", StringComparison.Ordinal) && _editor is KbviewAnchorEditor;

        private static KbviewElementObject? Element(object? component) => component as KbviewElementObject;

        private static Attribute[] BuildAttributes(string attributeName, string displayName, string? doc, PropertyCategoryMap.Category category, string? customCategory = null)
        {
            var attributes = new List<Attribute>
            {
                new CategoryAttribute(customCategory is { Length: > 0 } custom ? PropertyCategoryMap.DisplayName(custom) : PropertyCategoryMap.DisplayName(category)),
                new DescriptionAttribute(doc ?? string.Empty),
                new DisplayNameAttribute(displayName),
                new RefreshPropertiesAttribute(RefreshProperties.Repaint),
            };

            if (string.Equals(attributeName, "x:Name", StringComparison.Ordinal))
            {
                // Shown as "(Name)" at the top of the list, like WinForms: the grid adds the parentheses.
                attributes.Add(new ParenthesizePropertyNameAttribute(true));
            }

            return attributes.ToArray();
        }
    }

    /// <summary>
    /// The dropdown of a bool/enum attribute: its variants as standard values, deliberately NOT exclusive
    /// so a <c>{Binding ...}</c> can still be typed in the same cell.
    /// </summary>
    public sealed class AttributeValuesConverter : StringConverter
    {
        private readonly IReadOnlyList<string> _values;

        public AttributeValuesConverter(IReadOnlyList<string> values)
        {
            _values = values ?? throw new ArgumentNullException(nameof(values));
        }

        public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;

        public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => false;

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context) => new StandardValuesCollection(_values.ToArray());
    }

    /// <summary>A registry event (<c>OnClick</c>...) as a component event - what the Events tab and <see cref="KbviewEventBindingService"/> reason about.</summary>
    public sealed class KbviewEventDescriptor : EventDescriptor
    {
        public KbviewEventDescriptor(EventMeta @event, KbviewElementObject? owner = null)
            : base(@event?.Name ?? throw new ArgumentNullException(nameof(@event)), new Attribute[]
            {
                new CategoryAttribute(@event.LocalizedCategory),
                new DisplayNameAttribute(@event.EffectiveDisplayName),
                new DescriptionAttribute(@event.LocalizedDoc ?? string.Empty),
            })
        {
            Event = @event;
            Owner = owner;
        }

        public EventMeta Event { get; }

        /// <summary>The element whose Events tab lists this event (what <see cref="KbviewEventBindingService.GetCompatibleMethods"/> asks about), or null.</summary>
        public KbviewElementObject? Owner { get; }

        public override Type ComponentType => typeof(KbviewElementObject);

        /// <summary>Any non-null delegate type: <c>PropertyGrid</c>'s <c>ViewEvent</c> refuses an event without one. Handlers are Rust <c>fn</c>s, never CLR delegates.</summary>
        public override Type EventType => typeof(EventHandler);

        public override bool IsMulticast => false;

        public override void AddEventHandler(object component, Delegate value)
        {
        }

        public override void RemoveEventHandler(object component, Delegate value)
        {
        }
    }

    /// <summary>
    /// One Events-tab row: the handler name held by the element's <c>On*</c> attribute. Setting it (the
    /// grid does so on double-click, through <see cref="KbviewEventBindingService.CreateUniqueMethodName"/>,
    /// or when a name is typed) creates the handler when the event had none (DSG-10: attribute + Rust
    /// <c>fn</c> stub, then the code-behind opens at it), navigates to it when unchanged, rebinds the
    /// attribute when a different name is typed, and removes the attribute when cleared.
    /// </summary>
    public sealed class KbviewEventPropertyDescriptor : PropertyDescriptor
    {
        public KbviewEventPropertyDescriptor(KbviewEventDescriptor @event)
            : base(@event?.Name ?? throw new ArgumentNullException(nameof(@event)), new Attribute[]
            {
                new CategoryAttribute(@event.Event.LocalizedCategory),
                new DisplayNameAttribute(@event.Event.EffectiveDisplayName),
                new DescriptionAttribute(@event.Event.LocalizedDoc ?? string.Empty),
                new RefreshPropertiesAttribute(RefreshProperties.Repaint),
            })
        {
            EventDescriptor = @event;
        }

        public KbviewEventDescriptor EventDescriptor { get; }

        public override Type ComponentType => typeof(KbviewElementObject);

        public override bool IsReadOnly => false;

        public override Type PropertyType => typeof(string);

        public override TypeConverter Converter => new HandlerNamesConverter(Name);

        /// <summary>
        /// The attribute holding this event's handler on <paramref name="element"/>: the canonical one, or an older
        /// alias an existing view still uses (<c>OnToggled="x"</c> shows in the CheckedChanged row, and is edited
        /// in place - the file keeps its spelling). The canonical name when neither is written.
        /// </summary>
        private string AttributeOf(KbviewElementObject element) =>
            EventDescriptor.Event.AttributeNames.FirstOrDefault(a => element.GetRawValue(a) is not null) ?? Name;

        public override bool CanResetValue(object component) => component is KbviewElementObject element && element.GetRawValue(AttributeOf(element)) is not null;

        public override object GetValue(object? component) => component is KbviewElementObject element ? element.GetRawValue(AttributeOf(element)) ?? string.Empty : string.Empty;

        public override void ResetValue(object component)
        {
            if (component is KbviewElementObject element && element.GetRawValue(AttributeOf(element)) is not null)
            {
                element.Host.RemoveHandler(element.ElementId, Name);
            }
        }

        public override void SetValue(object? component, object? value)
        {
            if (component is not KbviewElementObject element)
            {
                return;
            }

            var text = (value as string ?? string.Empty).Trim();
            var attribute = AttributeOf(element);
            var current = element.GetRawValue(attribute);
            if (text.Length == 0)
            {
                // Clearing the row (EVT-5): the attribute goes, and the handler too when it is an untouched stub.
                if (current is not null)
                {
                    element.Host.RemoveHandler(element.ElementId, Name);
                }

                return;
            }

            var name = AttributeValueRules.NormalizeHandlerName(text);
            if (string.IsNullOrEmpty(current))
            {
                // New handler: let the server pick (and uniquify) its own default name when the grid
                // proposes exactly that default, otherwise suggest what the user typed.
                var defaultName = AttributeValueRules.DefaultHandlerName(element.XName, element.Component.Name, Name);
                element.Host.CreateOrShowHandler(element.ElementId, Name, string.Equals(name, defaultName, StringComparison.Ordinal) ? null : name);
            }
            else if (string.Equals(name, current, StringComparison.Ordinal))
            {
                element.Host.CreateOrShowHandler(element.ElementId, Name, null);
            }
            else if (element.Host.GetCompatibleHandlers(element.ElementId, Name).Contains(name, StringComparer.Ordinal))
            {
                // Another existing handler picked from the dropdown: rebind, like WinForms.
                element.SetAttribute(attribute, name);
            }
            else
            {
                // A new name for the bound handler: rename it, in every view and in the Rust code (EVT-5), like
                // WinForms renames the method when its name is edited in the Events tab.
                element.Host.RenameHandler(element.ElementId, Name, current!, name);
            }
        }

        public override bool ShouldSerializeValue(object component) => component is KbviewElementObject element && !string.IsNullOrEmpty(element.GetRawValue(AttributeOf(element)));
    }

    /// <summary>
    /// The Events tab row's dropdown (EVT-5, docs/EVENTS.md §5.3): the code-behind's handlers the event can be bound
    /// to - WinForms' compatible methods -, asked from the language server when the dropdown opens. Not exclusive: a
    /// new name can still be typed (it creates or renames the handler).
    /// </summary>
    public sealed class HandlerNamesConverter : StringConverter
    {
        private readonly string _eventName;

        public HandlerNamesConverter(string eventName)
        {
            _eventName = eventName ?? throw new ArgumentNullException(nameof(eventName));
        }

        public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => context?.Instance is KbviewElementObject;

        public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => false;

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context) =>
            new StandardValuesCollection(context?.Instance is KbviewElementObject element
                ? element.Host.GetCompatibleHandlers(element.ElementId, _eventName).ToArray()
                : Array.Empty<string>());
    }
}
