using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Kubuno.Web.Logic.WebDesigner;

namespace Kubuno.Web.WebDesigner.Spike
{
    /// <summary>
    /// SPIKE (docs/WEB-VIEWS.md, WV-9a, question 3): what the Properties window shows for the element selected on the web
    /// surface - a type descriptor built from the element's markup and the spike catalog (no CLR type per element, like
    /// the desktop's <c>KbviewElementObject</c> built from the registry). Setting a value calls back into the pane, which
    /// edits the buffer (one undo unit); the page re-renders from the new text.
    /// </summary>
    internal sealed class SpikeElementObject : ICustomTypeDescriptor
    {
        private readonly SpikeNode _node;
        private readonly Action<string, string> _set;

        public SpikeElementObject(SpikeNode node, Action<string, string> set)
        {
            _node = node;
            _set = set;
        }

        /// <summary>The element's id (<c>""</c> = root).</summary>
        public string Id => _node.Id;

        public PropertyDescriptorCollection GetProperties() => GetProperties(Array.Empty<Attribute>());

        public PropertyDescriptorCollection GetProperties(Attribute[]? attributes)
        {
            var list = new List<PropertyDescriptor>
            {
                new ReadOnlyProperty("(Element)", _node.Name, "Design", "The element name."),
                new ReadOnlyProperty("(Id)", _node.Id.Length == 0 ? "(root)" : _node.Id, "Design", "The designer element id: dot path of element-child ordinals."),
            };
            var known = SpikeElementCatalog.Find(_node.Name)?.Properties ?? Array.Empty<SpikeProperty>();
            foreach (var property in known)
            {
                list.Add(new AttributeProperty(property, _node.Attribute(property.Name), _set));
            }

            foreach (var attribute in _node.Attributes.Where(a => known.All(k => k.Name != a.Name)))
            {
                list.Add(new AttributeProperty(new SpikeProperty(attribute.Name, SpikePropertyKind.Text, "Misc", "An attribute the spike catalog does not know."), attribute.Value, _set));
            }

            return new PropertyDescriptorCollection(list.ToArray());
        }

        public AttributeCollection GetAttributes() => AttributeCollection.Empty;

        public string GetClassName() => _node.Name;

        public string GetComponentName() => _node.Id.Length == 0 ? _node.Name + " (root)" : _node.Name + " " + _node.Id;

        public TypeConverter GetConverter() => new TypeConverter();

        public EventDescriptor? GetDefaultEvent() => null;

        public PropertyDescriptor? GetDefaultProperty() => null;

        public object? GetEditor(Type editorBaseType) => null;

        public EventDescriptorCollection GetEvents() => EventDescriptorCollection.Empty;

        public EventDescriptorCollection GetEvents(Attribute[]? attributes) => EventDescriptorCollection.Empty;

        public object GetPropertyOwner(PropertyDescriptor? pd) => this;

        private sealed class ReadOnlyProperty : PropertyDescriptor
        {
            private readonly string _value;

            public ReadOnlyProperty(string name, string value, string category, string description)
                : base(name, new Attribute[] { new CategoryAttribute(category), new DescriptionAttribute(description), new ReadOnlyAttribute(true) }) => _value = value;

            public override Type ComponentType => typeof(SpikeElementObject);

            public override bool IsReadOnly => true;

            public override Type PropertyType => typeof(string);

            public override bool CanResetValue(object component) => false;

            public override object GetValue(object? component) => _value;

            public override void ResetValue(object component)
            {
            }

            public override void SetValue(object? component, object? value)
            {
            }

            public override bool ShouldSerializeValue(object component) => false;
        }

        /// <summary>One attribute as a property: <c>bool</c> for Bool, a string with standard values for Choice, else a string.</summary>
        private sealed class AttributeProperty : PropertyDescriptor
        {
            private readonly SpikeProperty _property;
            private readonly string? _value;
            private readonly Action<string, string> _set;

            public AttributeProperty(SpikeProperty property, string? value, Action<string, string> set)
                : base(property.Name, AttributesOf(property))
            {
                _property = property;
                _value = value;
                _set = set;
            }

            private static Attribute[] AttributesOf(SpikeProperty property)
            {
                var list = new List<Attribute> { new CategoryAttribute(property.Category), new DescriptionAttribute(property.Description) };
                if (property.Kind == SpikePropertyKind.Choice)
                {
                    list.Add(new TypeConverterAttribute(typeof(ChoiceConverter)));
                }

                return list.ToArray();
            }

            public IReadOnlyList<string> Choices => _property.Choices;

            public override Type ComponentType => typeof(SpikeElementObject);

            public override bool IsReadOnly => false;

            public override Type PropertyType => _property.Kind == SpikePropertyKind.Bool ? typeof(bool) : typeof(string);

            public override bool CanResetValue(object component) => _value is not null;

            public override object? GetValue(object? component) =>
                _property.Kind == SpikePropertyKind.Bool ? string.Equals(_value ?? _property.Default, "true", StringComparison.OrdinalIgnoreCase) : _value ?? _property.Default ?? string.Empty;

            public override void ResetValue(object component) => _set(Name, string.Empty);

            public override void SetValue(object? component, object? value)
            {
                var text = value switch
                {
                    bool flag => flag ? "true" : "false",
                    null => string.Empty,
                    _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
                };
                _set(Name, text);
            }

            public override bool ShouldSerializeValue(object component) => _value is not null;
        }

        /// <summary>The choices of a Choice property as a drop-down in the Properties window.</summary>
        private sealed class ChoiceConverter : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;

            public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => true;

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context) =>
                new StandardValuesCollection((context?.PropertyDescriptor as AttributeProperty)?.Choices.ToArray() ?? Array.Empty<string>());
        }
    }
}
