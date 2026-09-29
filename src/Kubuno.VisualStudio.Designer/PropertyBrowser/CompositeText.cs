using System;
using System.Globalization;
using System.Linq;

namespace Kubuno.VisualStudio.Designer.PropertyBrowser
{
    /// <summary>
    /// The texts of the composite values the Properties window expands, like WinForms' <c>PointConverter</c>,
    /// <c>SizeConverter</c>, <c>PaddingConverter</c> and <c>OpacityConverter</c>: <c>"24, 24"</c>, <c>"3, 3, 3, 3"</c>,
    /// <c>"80 %"</c>. Pure.
    /// </summary>
    public static class CompositeText
    {
        /// <summary>A number as the attribute writes it (invariant, no trailing zeros).</summary>
        public static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>One number, or null.</summary>
        public static float? ParseNumber(string? text)
        {
            var t = (text ?? string.Empty).Trim();
            return float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && !float.IsNaN(v) && !float.IsInfinity(v) ? v : (float?)null;
        }

        /// <summary><paramref name="count"/> comma-separated numbers, or null.</summary>
        public static float[]? ParseList(string? text, int count)
        {
            var parts = (text ?? string.Empty).Split(new[] { ',', ';' });
            if (parts.Length != count)
            {
                return null;
            }

            var values = parts.Select(ParseNumber).ToArray();
            return values.All(v => v.HasValue) ? values.Select(v => v!.Value).ToArray() : null;
        }

        public static string FormatList(params float[] values) => string.Join(", ", values.Select(Number));

        /// <summary>A padding/margin: <c>"l, t, r, b"</c> or one number for the four sides; null when invalid.</summary>
        public static float[]? ParsePadding(string? text)
        {
            if (ParseNumber(text) is { } all)
            {
                return new[] { all, all, all, all };
            }

            return ParseList(text, 4);
        }

        /// <summary>An opacity in percent (0 to 100) from <c>"80 %"</c>, <c>"80%"</c>, <c>"80"</c> or a fraction <c>"0.8"</c>; null when invalid.</summary>
        public static float? ParseOpacity(string? text)
        {
            var t = (text ?? string.Empty).Trim();
            var percent = t.EndsWith("%", StringComparison.Ordinal);
            if (percent)
            {
                t = t.Substring(0, t.Length - 1).Trim();
            }

            if (ParseNumber(t) is not { } v)
            {
                return null;
            }

            if (!percent && v <= 1f && t.IndexOf('.') >= 0)
            {
                v *= 100f;
            }

            return Math.Max(0f, Math.Min(100f, v));
        }

        /// <summary><c>"80 %"</c>, as WinForms shows an opacity.</summary>
        public static string FormatOpacity(float percent) => Number(percent) + " %";
    }
}
