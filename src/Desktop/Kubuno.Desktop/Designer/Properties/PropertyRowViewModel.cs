using System;
using System.ComponentModel;
using Kubuno.VisualStudio.Designer.Registry;

namespace Kubuno.VisualStudio.Designer.Properties
{
    /// <summary>
    /// One row of the Properties tab: a <see cref="PropertyMeta"/> plus the selected element's current
    /// raw attribute value for it (docs/DESIGNER.md §1: "each PropertyMeta becomes one row, typed by
    /// PropKind"). Kept free of any WPF type. Its WPF consumer, the fallback "Kubuno Properties" tool
    /// window, was removed - Visual Studio's own Properties window (<c>KbviewElementObject</c>,
    /// docs/DESIGNER.md §11) took over; this row only tracks state and raises <see cref="ValueCommitted"/>.
    /// </summary>
    public sealed class PropertyRowViewModel : INotifyPropertyChanged
    {
        private string? _rawValue;

        public PropertyRowViewModel(PropertyMeta property, string? currentRawValue)
        {
            Property = property;
            _rawValue = currentRawValue;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Raised whenever <see cref="RawValue"/> is set to a genuinely new value - the hook a later package uses to turn an edit into a <c>SetAttribute</c>/<c>RemoveAttribute</c> request.</summary>
        public event EventHandler<string?>? ValueCommitted;

        public PropertyMeta Property { get; }

        public string Name => Property.Name;

        public PropKind Kind => Property.Kind;

        public string? Default => Property.Default;

        /// <summary>The element's current raw attribute value for this property, or <see langword="null"/> when the attribute is absent (falls back to <see cref="Default"/>).</summary>
        public string? RawValue
        {
            get => _rawValue;
            set
            {
                if (_rawValue == value)
                {
                    return;
                }

                _rawValue = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RawValue)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDefault)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBinding)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Binding)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectiveValue)));
                ValueCommitted?.Invoke(this, value);
            }
        }

        /// <summary><see cref="RawValue"/> when the attribute is present, otherwise <see cref="Default"/> - what the grid should actually show/edit (docs/DESIGNER.md §1: "defaults shown greyed").</summary>
        public string? EffectiveValue => _rawValue ?? Default;

        /// <summary>
        /// <see langword="true"/> when there is no explicit attribute (falling back to <see cref="Default"/>)
        /// or the explicit attribute's text happens to equal <see cref="Default"/> anyway - either way the
        /// grid greys the row (docs/DESIGNER.md §1: "defaults shown greyed").
        /// </summary>
        public bool IsDefault => _rawValue is null || string.Equals(_rawValue, Default, StringComparison.Ordinal);

        public bool IsBinding => BindingExpressionParser.IsBindingExpression(EffectiveValue);

        public BindingExpression? Binding => BindingExpressionParser.TryParse(EffectiveValue, out var expression) ? expression : null;

        /// <summary>Clears the explicit attribute, reverting the grid to showing <see cref="Default"/> (docs/DESIGNER.md §1: "reset-to-default"). A no-op if already at default.</summary>
        public void ResetToDefault() => RawValue = null;
    }
}
