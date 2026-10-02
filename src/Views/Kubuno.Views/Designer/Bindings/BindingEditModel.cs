using System;
using System.Collections.Generic;

namespace Kubuno.Desktop.Designer.Bindings
{
    /// <summary>One attribute edit: set <see cref="Value"/>, or remove the attribute when it is null.</summary>
    public sealed class BindingAttributeEdit
    {
        public BindingAttributeEdit(string attribute, string? value)
        {
            Attribute = attribute;
            Value = value;
        }

        public string Attribute { get; }

        public string? Value { get; }

        public override string ToString() => Value is null ? "-" + Attribute : Attribute + "=" + Value;
    }

    /// <summary>
    /// The model of the « Liaison de données » dialog (docs/DESIGNER.md, "Data bindings"; WinForms' "Formatting and
    /// Advanced Binding", WPF's "Create Data Binding"): every option of one property's binding, read from the attribute as
    /// written and written back without touching what was not changed (unknown keys, the order of the parts, the alias in
    /// use); plus the property's design-time value (<c>d:Property</c>). Pure.
    /// </summary>
    public sealed class BindingEditModel
    {
        private readonly string? _original;
        private readonly string? _originalDesign;

        public BindingEditModel(string property, string? currentRaw, string? designValue)
        {
            Property = property;
            _original = currentRaw;
            _originalDesign = designValue;
            DesignValue = designValue;
            WasBound = BindingMarkup.TryParse(currentRaw, out var m);
            if (m is null)
            {
                // A literal stays the property's value until a path is chosen.
                Literal = currentRaw;
                return;
            }

            Source = m.Source;
            Path = m.Path;
            Mode = m.Mode;
            Trigger = m.Get("UpdateSourceTrigger");
            Converter = m.Get("Converter");
            ConverterParameter = m.Get("ConverterParameter");
            StringFormat = m.Get("StringFormat");
            Culture = m.Get("ConverterCulture");
            TargetNullValue = m.Get("TargetNullValue");
            FallbackValue = m.Get("FallbackValue");
            UnknownKeys = new List<string>(m.UnknownKeys);
        }

        /// <summary>The attribute (the property's XML name).</summary>
        public string Property { get; }

        /// <summary>Whether the attribute held a binding when the dialog opened.</summary>
        public bool WasBound { get; }

        /// <summary>The literal the attribute held (not a binding), or null.</summary>
        public string? Literal { get; }

        /// <summary>The keys written that the runtime does not know (kept as written; the language server warns).</summary>
        public IReadOnlyList<string> UnknownKeys { get; } = Array.Empty<string>();

        public string? Source { get; set; }

        public string? Path { get; set; }

        /// <summary>Null or <c>OneWay</c> (the default) ... <c>OneWayToSource</c>.</summary>
        public string? Mode { get; set; }

        public string? Trigger { get; set; }

        public string? Converter { get; set; }

        public string? ConverterParameter { get; set; }

        public string? StringFormat { get; set; }

        public string? Culture { get; set; }

        public string? TargetNullValue { get; set; }

        public string? FallbackValue { get; set; }

        /// <summary>The design-time value (<c>d:Property</c>), shown by the designer instead of the bound one.</summary>
        public string? DesignValue { get; set; }

        /// <summary>Whether a path (or a source) is chosen: the property is bound.</summary>
        public bool HasTarget => !string.IsNullOrWhiteSpace(Path) || !string.IsNullOrWhiteSpace(Source);

        /// <summary>The attribute holding the design-time value.</summary>
        public string DesignAttribute => "d:" + Property;

        /// <summary>The binding expression (empty when no path or source is chosen).</summary>
        public string Build()
        {
            if (!HasTarget)
            {
                return string.Empty;
            }

            if (!BindingMarkup.TryParse(_original, out var m))
            {
                m = BindingMarkup.Create(null);
            }

            m!.Retarget(Trim(Source), Trim(Path));
            // The default mode and trigger are only written when they already were.
            m.Set("Mode", Default(Mode, "OneWay", m.Get("Mode")));
            m.Set("UpdateSourceTrigger", Default(Trigger, "PropertyChanged", m.Get("UpdateSourceTrigger")));
            m.Set("Converter", Trim(Converter));
            m.Set("ConverterParameter", Converter is { Length: > 0 } ? Keep(ConverterParameter) : null);
            m.Set("StringFormat", Trim(StringFormat));
            m.Set("ConverterCulture", Trim(Culture));
            m.Set("TargetNullValue", Keep(TargetNullValue));
            m.Set("FallbackValue", Keep(FallbackValue));
            return m.ToString();
        }

        /// <summary>
        /// The edits the dialog's OK makes, in order: the binding attribute (removed when no path is chosen), then the
        /// design-time value. Nothing for what did not change. Applied in one dispatcher turn they are one undo unit
        /// (<c>PropertyEditBatcher</c>).
        /// </summary>
        public IReadOnlyList<BindingAttributeEdit> Edits()
        {
            var edits = new List<BindingAttributeEdit>();
            var binding = Build();
            if (binding.Length > 0)
            {
                if (!string.Equals(binding, _original, StringComparison.Ordinal))
                {
                    edits.Add(new BindingAttributeEdit(Property, binding));
                }
            }
            else if (WasBound)
            {
                edits.Add(new BindingAttributeEdit(Property, null));
            }

            var design = string.IsNullOrEmpty(DesignValue) ? null : DesignValue;
            if (!string.Equals(design, string.IsNullOrEmpty(_originalDesign) ? null : _originalDesign, StringComparison.Ordinal))
            {
                edits.Add(new BindingAttributeEdit(DesignAttribute, design));
            }

            return edits;
        }

        /// <summary>
        /// « Supprimer la liaison (rétablir la valeur) »: the binding goes; the property keeps the value the designer showed -
        /// its design-time value becomes its value (and the <c>d:</c> attribute goes), else the attribute is removed (the
        /// property's default).
        /// </summary>
        public static IReadOnlyList<BindingAttributeEdit> RemoveBinding(string property, string? designValue) =>
            string.IsNullOrEmpty(designValue)
                ? new[] { new BindingAttributeEdit(property, null) }
                : new[] { new BindingAttributeEdit(property, designValue), new BindingAttributeEdit("d:" + property, null) };

        private static string? Trim(string? v) => string.IsNullOrWhiteSpace(v) ? null : v!.Trim();

        /// <summary>A text option: kept as typed (spaces are meaningful), null when empty.</summary>
        private static string? Keep(string? v) => string.IsNullOrEmpty(v) ? null : v;

        /// <summary>The value to write for an option with a default: the default itself only when it was written before.</summary>
        private static string? Default(string? value, string defaultValue, string? written)
        {
            var v = Trim(value);
            if (v is null || string.Equals(v, defaultValue, StringComparison.Ordinal))
            {
                return written is not null && string.Equals(Trim(written), defaultValue, StringComparison.Ordinal) ? written : null;
            }

            return v;
        }
    }
}
