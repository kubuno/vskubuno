using System;
using System.Globalization;
using System.Linq;
using System.Text;
using Kubuno.Views.Designer.Properties;
using Kubuno.Views.Designer.Registry;

namespace Kubuno.Views.Designer.PropertyBrowser
{
    /// <summary>
    /// Pure validation/normalization of a value typed into the Properties window before it is written to
    /// the <c>.kbview</c> text, plus the default event-handler name - kept free of any VS/WinForms type so
    /// it is plain unit-testable.
    /// </summary>
    public static class AttributeValueRules
    {
        /// <summary>
        /// Checks <paramref name="value"/> against <paramref name="kind"/> and returns the text to write
        /// (an enum variant or a bool is normalized to its canonical spelling). A <c>{Binding ...}</c>
        /// expression is always accepted as-is, for every kind - a bound value is shown and edited as text
        /// (docs/DESIGNER.md §1). Throws <see cref="ArgumentException"/> with a localized message the
        /// Properties window shows in its own "invalid property value" dialog, like WinForms.
        /// </summary>
        public static string Normalize(PropKind kind, string value)
        {
            if (kind is null)
            {
                throw new ArgumentNullException(nameof(kind));
            }

            value ??= string.Empty;
            if (Bindings.BindingMarkup.IsMarkupExtension(value))
            {
                return value;
            }

            switch (kind.Tag)
            {
                case PropKindTag.Bool:
                    if (string.Equals(value.Trim(), "true", StringComparison.OrdinalIgnoreCase))
                    {
                        return "true";
                    }

                    if (string.Equals(value.Trim(), "false", StringComparison.OrdinalIgnoreCase))
                    {
                        return "false";
                    }

                    throw new ArgumentException(DesignerText.InvalidBool(value));
                case PropKindTag.F32:
                    if (double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    {
                        return value.Trim();
                    }

                    throw new ArgumentException(DesignerText.InvalidNumber(value));
                case PropKindTag.Enum:
                    var match = kind.EnumVariants.FirstOrDefault(v => string.Equals(v, value.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (match is not null)
                    {
                        return match;
                    }

                    throw new ArgumentException(DesignerText.InvalidEnum(value, string.Join(", ", kind.EnumVariants)));
                default:
                    return value;
            }
        }

        /// <summary>An <c>x:Name</c> must be an identifier (it feeds <c>FocusId</c> and handler names): letters, digits, <c>_</c>, <c>-</c>, not starting with a digit.</summary>
        public static string NormalizeName(string value)
        {
            var trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0 || char.IsDigit(trimmed[0]) || trimmed.Any(c => !(char.IsLetterOrDigit(c) || c == '_' || c == '-')))
            {
                throw new ArgumentException(DesignerText.InvalidName(value ?? string.Empty));
            }

            return trimmed;
        }

        /// <summary>A handler name must be a plain Rust identifier.</summary>
        public static string NormalizeHandlerName(string value)
        {
            var trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Length == 0 || char.IsDigit(trimmed[0]) || trimmed.Any(c => !(c < 128 && (char.IsLetterOrDigit(c) || c == '_'))))
            {
                throw new ArgumentException(DesignerText.InvalidHandlerName(value ?? string.Empty));
            }

            return trimmed;
        }

        /// <summary>
        /// The handler name <c>kubuno/createHandler</c> generates when no name is suggested - a verbatim
        /// port of <c>kubuno-views-ls/src/handler_insert.rs</c>'s <c>default_handler_name</c>
        /// (<c>on_&lt;snake(x:Name or tag)&gt;_&lt;snake(event without its leading "On")&gt;</c>, e.g.
        /// <c>on_save_btn_click</c>), so the Properties window's Events tab proposes exactly the name the
        /// server will use. The server still makes the final name unique in the code-behind file.
        /// </summary>
        public static string DefaultHandlerName(string? xName, string elementName, string eventName)
        {
            var subject = string.IsNullOrEmpty(xName) ? string.Empty : ToSnakeCase(xName!);
            if (subject.Length == 0)
            {
                subject = ToSnakeCase(elementName ?? string.Empty);
            }

            var eventBase = eventName ?? string.Empty;
            if (eventBase.Length > 2 && eventBase.StartsWith("On", StringComparison.Ordinal) && char.IsUpper(eventBase[2]))
            {
                eventBase = eventBase.Substring(2);
            }

            return $"on_{subject}_{ToSnakeCase(eventBase)}";
        }

        /// <summary>Port of <c>handler_insert.rs</c>'s <c>to_snake_case</c>: ASCII upper-case letters start a new word, any other non-alphanumeric character is a separator.</summary>
        public static string ToSnakeCase(string s)
        {
            var output = new StringBuilder(s.Length + 4);
            var previousWasLowerOrDigit = false;
            foreach (var c in s)
            {
                if (c >= 'A' && c <= 'Z')
                {
                    if (previousWasLowerOrDigit)
                    {
                        output.Append('_');
                    }

                    output.Append(char.ToLowerInvariant(c));
                    previousWasLowerOrDigit = false;
                }
                else if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    output.Append(c);
                    previousWasLowerOrDigit = true;
                }
                else if (output.Length > 0 && output[output.Length - 1] != '_')
                {
                    output.Append('_');
                    previousWasLowerOrDigit = false;
                }
            }

            return output.ToString().Trim('_');
        }
    }
}
