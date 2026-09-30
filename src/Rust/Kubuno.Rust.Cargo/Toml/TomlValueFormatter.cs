using System;
using System.Globalization;
using System.Text;

namespace Kubuno.Cargo.Toml
{
    /// <summary>Single-line TOML rendering of values.</summary>
    public static class TomlValueFormatter
    {
        /// <summary>
        /// Renders <paramref name="v"/> on one line: basic strings, decimal integers, round-trippable floats,
        /// <c>[a, b]</c> arrays and <c>{ a = 1 }</c> inline tables. Date-times are rendered from their source text.
        /// </summary>
        public static string Format(TomlValue v)
        {
            if (v == null) throw new ArgumentNullException(nameof(v));
            var sb = new StringBuilder();
            Append(sb, v);
            return sb.ToString();
        }

        internal static string FormatString(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            AppendString(sb, s);
            return sb.ToString();
        }

        private static void Append(StringBuilder sb, TomlValue v)
        {
            switch (v.Kind)
            {
                case TomlValueKind.String:
                    AppendString(sb, v.AsString() ?? string.Empty);
                    break;
                case TomlValueKind.Integer:
                    sb.Append((v.AsInteger() ?? 0).ToString(CultureInfo.InvariantCulture));
                    break;
                case TomlValueKind.Float:
                    sb.Append(FormatFloat(v.AsFloat() ?? 0));
                    break;
                case TomlValueKind.Boolean:
                    sb.Append(v.AsBoolean() == true ? "true" : "false");
                    break;
                case TomlValueKind.DateTime:
                    sb.Append(v.AsDateTime());
                    break;
                case TomlValueKind.Array:
                    {
                        sb.Append('[');
                        var items = v.AsArray()!;
                        for (int i = 0; i < items.Count; i++)
                        {
                            if (i > 0) sb.Append(", ");
                            Append(sb, items[i]);
                        }

                        sb.Append(']');
                        break;
                    }

                default:
                    {
                        var entries = v.AsTable()!;
                        if (entries.Count == 0)
                        {
                            sb.Append("{}");
                            break;
                        }

                        sb.Append("{ ");
                        for (int i = 0; i < entries.Count; i++)
                        {
                            if (i > 0) sb.Append(", ");
                            sb.Append(TomlKey.Format(entries[i].Key)).Append(" = ");
                            Append(sb, entries[i].Value);
                        }

                        sb.Append(" }");
                        break;
                    }
            }
        }

        private static string FormatFloat(double d)
        {
            if (double.IsNaN(d)) return "nan";
            if (double.IsPositiveInfinity(d)) return "inf";
            if (double.IsNegativeInfinity(d)) return "-inf";
            string s = d.ToString("R", CultureInfo.InvariantCulture);
            if (s.IndexOf('.') < 0 && s.IndexOf('E') < 0 && s.IndexOf('e') < 0) s += ".0";
            return s;
        }

        private static void AppendString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\r': sb.Append("\\r"); break;
                    default:
                        if (c < 0x20 || c == 0x7f) sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }

            sb.Append('"');
        }
    }
}
