using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Kubuno.Desktop.Designer.Properties;

namespace Kubuno.Desktop.Designer.PropertyBrowser
{
    /// <summary>
    /// How one expandable Properties-window row (WinForms' <c>Location</c>, <c>Size</c>, <c>Padding</c>...) maps onto
    /// the element's attributes: its parts (sub-properties) and how the whole value and each part are read, written,
    /// compared to the default and reset.
    /// </summary>
    public abstract class CompositeSpec
    {
        protected CompositeSpec(string name, IReadOnlyList<string> partNames)
        {
            Name = name;
            PartNames = partNames;
        }

        /// <summary>The row's name (<c>"Location"</c>, <c>"Margin"</c>...).</summary>
        public string Name { get; }

        /// <summary>The sub-properties, in order (<c>X, Y</c>; <c>All, Left, Top, Right, Bottom</c>...).</summary>
        public IReadOnlyList<string> PartNames { get; }

        /// <summary>The text of each part as currently written (defaults for absent values).</summary>
        public abstract string[] ReadParts(KbviewElementObject element);

        /// <summary>The whole value's text (<c>"24, 24"</c>).</summary>
        public virtual string Read(KbviewElementObject element) => string.Join(", ", ReadParts(element));

        /// <summary>Writes a whole value typed in the row; throws <see cref="ArgumentException"/> when it is not valid.</summary>
        public abstract void Write(KbviewElementObject element, string text);

        /// <summary>Writes one part; throws <see cref="ArgumentException"/> when it is not valid.</summary>
        public abstract void WritePart(KbviewElementObject element, int part, string text);

        public abstract bool IsDefault(KbviewElementObject element);

        public abstract bool IsPartDefault(KbviewElementObject element, int part);

        /// <summary>Whether anything is written (Reset has something to remove).</summary>
        public abstract bool HasAny(KbviewElementObject element);

        public abstract void Reset(KbviewElementObject element);

        public abstract void ResetPart(KbviewElementObject element, int part);

        /// <summary>The attributes this row reads and writes.</summary>
        public abstract IReadOnlyList<string> Attributes { get; }

        protected static void SetIfChanged(KbviewElementObject element, string attribute, string? value)
        {
            var current = element.GetRawValue(attribute);
            if (value is null)
            {
                if (current is not null)
                {
                    element.RemoveAttribute(attribute);
                }
            }
            else if (!string.Equals(current, value, StringComparison.Ordinal))
            {
                element.SetAttribute(attribute, value);
            }
        }
    }

    /// <summary>
    /// A row over TWO attributes, one per part: <c>Location</c> = <c>X</c>, <c>Y</c>; <c>Size</c> = <c>Width</c>, <c>Height</c>.
    /// An absent coordinate is <c>0</c>; an absent size is <c>auto</c> (the control's natural size), and typing <c>auto</c>
    /// (or nothing) removes the attribute.
    /// </summary>
    public sealed class TwoAttributeSpec : CompositeSpec
    {
        private readonly string[] _attributes;
        private readonly bool _autoWhenAbsent;

        public TwoAttributeSpec(string name, string first, string second, bool autoWhenAbsent)
            : base(name, new[] { first, second })
        {
            _attributes = new[] { first, second };
            _autoWhenAbsent = autoWhenAbsent;
        }

        public const string Auto = "auto";

        public override IReadOnlyList<string> Attributes => _attributes;

        private string Absent => _autoWhenAbsent ? Auto : "0";

        public override string[] ReadParts(KbviewElementObject element) =>
            _attributes.Select(a => element.GetRawValue(a) is { } raw ? raw : Absent).ToArray();

        public override void Write(KbviewElementObject element, string text)
        {
            var parts = (text ?? string.Empty).Split(new[] { ',', ';' });
            if (parts.Length != 2)
            {
                throw new ArgumentException(DesignerText.InvalidComposite(text ?? string.Empty, "0, 0"));
            }

            var values = parts.Select(Normalize).ToArray();
            for (var i = 0; i < 2; i++)
            {
                SetIfChanged(element, _attributes[i], values[i]);
            }
        }

        public override void WritePart(KbviewElementObject element, int part, string text) => SetIfChanged(element, _attributes[part], Normalize(text));

        /// <summary>The attribute text of one part (<c>null</c> = remove it); throws when it is not a number.</summary>
        private string? Normalize(string text)
        {
            var t = (text ?? string.Empty).Trim();
            if (BindingExpressionParser.IsBindingExpression(t))
            {
                return t;
            }

            if (t.Length == 0 || (_autoWhenAbsent && string.Equals(t, Auto, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            return CompositeText.ParseNumber(t) is { } v ? CompositeText.Number(v) : throw new ArgumentException(DesignerText.InvalidNumber(t));
        }

        public override bool IsDefault(KbviewElementObject element) => Enumerable.Range(0, 2).All(i => IsPartDefault(element, i));

        public override bool IsPartDefault(KbviewElementObject element, int part)
        {
            var raw = element.GetRawValue(_attributes[part]);
            return raw is null || (!_autoWhenAbsent && CompositeText.ParseNumber(raw) == 0f);
        }

        public override bool HasAny(KbviewElementObject element) => _attributes.Any(a => element.GetRawValue(a) is not null);

        public override void Reset(KbviewElementObject element)
        {
            foreach (var a in _attributes)
            {
                SetIfChanged(element, a, null);
            }
        }

        public override void ResetPart(KbviewElementObject element, int part) => SetIfChanged(element, _attributes[part], null);
    }

    /// <summary>
    /// A row over ONE attribute holding several numbers: <c>Margin</c>/<c>Padding</c> (<c>"l, t, r, b"</c>, parts All, Left,
    /// Top, Right, Bottom - All shows a value only when the four sides are equal, and sets the four) or
    /// <c>MinimumSize</c>/<c>MaximumSize</c> (<c>"w, h"</c>, parts Width, Height). A value equal to the default removes
    /// the attribute; a <c>{Binding ...}</c> is kept as written.
    /// </summary>
    public sealed class NumberListSpec : CompositeSpec
    {
        private readonly string _attribute;
        private readonly bool _padding;
        private readonly float[] _default;

        private NumberListSpec(string attribute, bool padding, string? defaultText)
            : base(attribute, padding ? new[] { "All", "Left", "Top", "Right", "Bottom" } : new[] { "Width", "Height" })
        {
            _attribute = attribute;
            _padding = padding;
            _default = Parse(defaultText) ?? new float[padding ? 4 : 2];
        }

        /// <summary>A <c>Margin</c>/<c>Padding</c> row over <paramref name="attribute"/>.</summary>
        public static NumberListSpec Padding(string attribute, string? defaultText) => new NumberListSpec(attribute, true, defaultText);

        /// <summary>A <c>MinimumSize</c>/<c>MaximumSize</c> row over <paramref name="attribute"/>.</summary>
        public static NumberListSpec Size(string attribute, string? defaultText) => new NumberListSpec(attribute, false, defaultText);

        public override IReadOnlyList<string> Attributes => new[] { _attribute };

        private int Count => _padding ? 4 : 2;

        private float[]? Parse(string? text) => _padding ? CompositeText.ParsePadding(text) : CompositeText.ParseList(text, 2);

        /// <summary>The current values (the default when absent or unreadable).</summary>
        private float[] Values(KbviewElementObject element) => Parse(element.GetRawValue(_attribute)) ?? (float[])_default.Clone();

        private bool IsBinding(KbviewElementObject element) => BindingExpressionParser.IsBindingExpression(element.GetRawValue(_attribute));

        public override string Read(KbviewElementObject element) =>
            IsBinding(element) ? element.GetRawValue(_attribute)! : CompositeText.FormatList(Values(element));

        public override string[] ReadParts(KbviewElementObject element)
        {
            if (IsBinding(element))
            {
                return PartNames.Select(_ => string.Empty).ToArray();
            }

            var v = Values(element);
            if (!_padding)
            {
                return v.Select(CompositeText.Number).ToArray();
            }

            var all = v.All(x => x == v[0]) ? CompositeText.Number(v[0]) : string.Empty;
            return new[] { all }.Concat(v.Select(CompositeText.Number)).ToArray();
        }

        public override void Write(KbviewElementObject element, string text)
        {
            var t = (text ?? string.Empty).Trim();
            if (BindingExpressionParser.IsBindingExpression(t))
            {
                SetIfChanged(element, _attribute, t);
                return;
            }

            if (t.Length == 0)
            {
                SetIfChanged(element, _attribute, null);
                return;
            }

            var values = Parse(t) ?? throw new ArgumentException(DesignerText.InvalidComposite(t, _padding ? "0, 0, 0, 0" : "0, 0"));
            Store(element, values);
        }

        public override void WritePart(KbviewElementObject element, int part, string text)
        {
            var number = CompositeText.ParseNumber(text) ?? throw new ArgumentException(DesignerText.InvalidNumber(text ?? string.Empty));
            var values = Values(element);
            if (_padding && part == 0)
            {
                values = new[] { number, number, number, number };
            }
            else
            {
                values[_padding ? part - 1 : part] = number;
            }

            Store(element, values);
        }

        private void Store(KbviewElementObject element, float[] values) =>
            SetIfChanged(element, _attribute, values.SequenceEqual(_default) ? null : CompositeText.FormatList(values));

        public override bool IsDefault(KbviewElementObject element) => !IsBinding(element) && Values(element).SequenceEqual(_default);

        public override bool IsPartDefault(KbviewElementObject element, int part)
        {
            if (IsBinding(element))
            {
                return false;
            }

            var v = Values(element);
            return _padding && part == 0 ? v.SequenceEqual(_default) : v[_padding ? part - 1 : part] == _default[_padding ? part - 1 : part];
        }

        public override bool HasAny(KbviewElementObject element) => element.GetRawValue(_attribute) is not null;

        public override void Reset(KbviewElementObject element) => SetIfChanged(element, _attribute, null);

        public override void ResetPart(KbviewElementObject element, int part)
        {
            if (IsBinding(element))
            {
                return;
            }

            var values = Values(element);
            if (_padding && part == 0)
            {
                values = (float[])_default.Clone();
            }
            else
            {
                var i = _padding ? part - 1 : part;
                values[i] = _default[i];
            }

            Store(element, values);
        }
    }

    /// <summary>
    /// The value of an expandable row: the element it belongs to and its text. Mutable through its sub-properties (they
    /// write the element's attributes), so the grid never has to rebuild it; equal to another when the texts are
    /// (what a multi-selection compares to blank a differing value). A value converted from typed text has no
    /// <see cref="Owner"/>: setting it on the row writes that text.
    /// </summary>
    public sealed class KbviewCompositeValue
    {
        public KbviewCompositeValue(KbviewElementObject? owner, CompositeSpec spec, string text)
        {
            Owner = owner;
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
            Text = text ?? string.Empty;
        }

        public KbviewElementObject? Owner { get; }

        public CompositeSpec Spec { get; }

        public string Text { get; }

        public override bool Equals(object? obj) => obj is KbviewCompositeValue other && string.Equals(Text, other.Text, StringComparison.Ordinal);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Text);

        public override string ToString() => Text;
    }

    /// <summary>The expandable row itself (see <see cref="CompositeSpec"/>).</summary>
    public sealed class KbviewCompositePropertyDescriptor : PropertyDescriptor
    {
        public KbviewCompositePropertyDescriptor(CompositeSpec spec, string displayName, string? doc, string category)
            : base(spec?.Name ?? throw new ArgumentNullException(nameof(spec)), new Attribute[]
            {
                new CategoryAttribute(category),
                new DescriptionAttribute(doc ?? string.Empty),
                new DisplayNameAttribute(displayName),
                new RefreshPropertiesAttribute(RefreshProperties.Repaint),
            })
        {
            Spec = spec;
        }

        public CompositeSpec Spec { get; }

        public override Type ComponentType => typeof(KbviewElementObject);

        public override bool IsReadOnly => false;

        public override Type PropertyType => typeof(KbviewCompositeValue);

        public override TypeConverter Converter => new KbviewCompositeConverter(Spec);

        public override object GetValue(object? component) =>
            component is KbviewElementObject element ? new KbviewCompositeValue(element, Spec, Spec.Read(element)) : new KbviewCompositeValue(null, Spec, string.Empty);

        public override void SetValue(object? component, object? value)
        {
            if (component is not KbviewElementObject element)
            {
                return;
            }

            switch (value)
            {
                case KbviewCompositeValue composite when ReferenceEquals(composite.Owner, element):
                    // Its sub-properties already wrote the attributes (the grid sets the row back after a part).
                    return;
                case KbviewCompositeValue composite:
                    if (!string.Equals(composite.Text, Spec.Read(element), StringComparison.Ordinal))
                    {
                        Spec.Write(element, composite.Text);
                    }

                    return;
                case string text:
                    Spec.Write(element, text);
                    return;
            }
        }

        public override bool CanResetValue(object component) => component is KbviewElementObject element && Spec.HasAny(element);

        public override void ResetValue(object component)
        {
            if (component is KbviewElementObject element)
            {
                Spec.Reset(element);
            }
        }

        public override bool ShouldSerializeValue(object component) => component is KbviewElementObject element && !Spec.IsDefault(element);
    }

    /// <summary>One sub-property of an expandable row (<c>X</c> under <c>Location</c>, <c>Left</c> under <c>Padding</c>).</summary>
    public sealed class KbviewCompositePartDescriptor : PropertyDescriptor
    {
        private readonly CompositeSpec _spec;
        private readonly int _part;

        public KbviewCompositePartDescriptor(CompositeSpec spec, int part)
            : base(spec.PartNames[part], new Attribute[]
            {
                new DescriptionAttribute(DesignerText.CompositePartDoc(spec.Name, spec.PartNames[part])),
                new RefreshPropertiesAttribute(RefreshProperties.Repaint),
            })
        {
            _spec = spec;
            _part = part;
        }

        public override Type ComponentType => typeof(KbviewCompositeValue);

        public override bool IsReadOnly => false;

        public override Type PropertyType => typeof(string);

        public override object GetValue(object? component) => component is KbviewCompositeValue { Owner: { } owner } ? _spec.ReadParts(owner)[_part] : string.Empty;

        public override void SetValue(object? component, object? value)
        {
            if (component is KbviewCompositeValue { Owner: { } owner })
            {
                var text = value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                if (!string.Equals(text.Trim(), _spec.ReadParts(owner)[_part], StringComparison.Ordinal))
                {
                    _spec.WritePart(owner, _part, text);
                }
            }
        }

        public override bool CanResetValue(object component) => component is KbviewCompositeValue { Owner: { } owner } && !_spec.IsPartDefault(owner, _part);

        public override void ResetValue(object component)
        {
            if (component is KbviewCompositeValue { Owner: { } owner })
            {
                _spec.ResetPart(owner, _part);
            }
        }

        public override bool ShouldSerializeValue(object component) => component is KbviewCompositeValue { Owner: { } owner } && !_spec.IsPartDefault(owner, _part);
    }

    /// <summary>The expandable rows' converter: the text, the sub-properties, and typed text back into a value.</summary>
    public sealed class KbviewCompositeConverter : TypeConverter
    {
        private readonly CompositeSpec _spec;

        public KbviewCompositeConverter(CompositeSpec spec)
        {
            _spec = spec ?? throw new ArgumentNullException(nameof(spec));
        }

        public override bool GetPropertiesSupported(ITypeDescriptorContext? context) => true;

        public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext? context, object value, Attribute[]? attributes) =>
            new PropertyDescriptorCollection(Enumerable.Range(0, _spec.PartNames.Count).Select(i => (PropertyDescriptor)new KbviewCompositePartDescriptor(_spec, i)).ToArray(), readOnly: true);

        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) =>
            value is string text ? new KbviewCompositeValue(null, _spec, text.Trim()) : base.ConvertFrom(context, culture, value);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) => destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) =>
            destinationType == typeof(string) ? (value as KbviewCompositeValue)?.Text ?? value?.ToString() ?? string.Empty : base.ConvertTo(context, culture, value, destinationType);
    }
}
