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

        public KbviewAttributePropertyDescriptor(string attributeName, string displayName, PropKind? kind, string? defaultValue, string? doc, PropertyCategoryMap.Category category)
            : base(attributeName, BuildAttributes(attributeName, displayName, doc, category))
        {
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

        public override bool IsReadOnly => false;

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

            var normalized = Kind is null ? AttributeValueRules.NormalizeName(text) : AttributeValueRules.Normalize(Kind, text);
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
            return raw is not null && !string.Equals(raw, _default, StringComparison.Ordinal);
        }

        private static KbviewElementObject? Element(object? component) => component as KbviewElementObject;

        private static Attribute[] BuildAttributes(string attributeName, string displayName, string? doc, PropertyCategoryMap.Category category)
        {
            var attributes = new List<Attribute>
            {
                new CategoryAttribute(PropertyCategoryMap.DisplayName(category)),
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
        public KbviewEventDescriptor(EventMeta @event)
            : base(@event?.Name ?? throw new ArgumentNullException(nameof(@event)), new Attribute[]
            {
                new CategoryAttribute(DesignerText.CategoryAction),
                new DescriptionAttribute(@event.Doc ?? string.Empty),
            })
        {
            Event = @event;
        }

        public EventMeta Event { get; }

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
                new CategoryAttribute(DesignerText.CategoryAction),
                new DescriptionAttribute(@event.Event.Doc ?? string.Empty),
                new RefreshPropertiesAttribute(RefreshProperties.Repaint),
            })
        {
            EventDescriptor = @event;
        }

        public KbviewEventDescriptor EventDescriptor { get; }

        public override Type ComponentType => typeof(KbviewElementObject);

        public override bool IsReadOnly => false;

        public override Type PropertyType => typeof(string);

        public override TypeConverter Converter => new StringConverter();

        public override bool CanResetValue(object component) => (component as KbviewElementObject)?.GetRawValue(Name) is not null;

        public override object GetValue(object? component) => (component as KbviewElementObject)?.GetRawValue(Name) ?? string.Empty;

        public override void ResetValue(object component)
        {
            if (component is KbviewElementObject element && element.GetRawValue(Name) is not null)
            {
                element.RemoveAttribute(Name);
            }
        }

        public override void SetValue(object? component, object? value)
        {
            if (component is not KbviewElementObject element)
            {
                return;
            }

            var text = (value as string ?? string.Empty).Trim();
            var current = element.GetRawValue(Name);
            if (text.Length == 0)
            {
                if (current is not null)
                {
                    element.RemoveAttribute(Name);
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
            else
            {
                element.SetAttribute(Name, name);
            }
        }

        public override bool ShouldSerializeValue(object component) => !string.IsNullOrEmpty((component as KbviewElementObject)?.GetRawValue(Name));
    }
}
