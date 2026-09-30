using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Kubuno.Desktop.Designer.PropertyBrowser
{
    /// <summary>A parsed <c>Font</c> attribute (see <see cref="FontText"/>).</summary>
    public sealed class FontSpec
    {
        public FontSpec(string family, float size, bool pixels, bool bold, bool italic, bool underline, bool strikeout)
        {
            Family = family;
            Size = size;
            Pixels = pixels;
            Bold = bold;
            Italic = italic;
            Underline = underline;
            Strikeout = strikeout;
        }

        public string Family { get; }

        /// <summary>The size, in points (or in pixels when <see cref="Pixels"/>).</summary>
        public float Size { get; }

        public bool Pixels { get; }

        public bool Bold { get; }

        public bool Italic { get; }

        public bool Underline { get; }

        public bool Strikeout { get; }

        /// <summary>The size in points (pixels are converted at 96 dpi: 4 px = 3 pt).</summary>
        public float Points => Pixels ? Size * 0.75f : Size;

        public override string ToString() => FontText.Format(this);
    }

    /// <summary>
    /// The <c>Font</c> attribute grammar - the text of WinForms' <c>FontConverter</c>: <c>"Segoe UI, 12pt"</c>,
    /// <c>"Segoe UI, 12pt, style=Bold, Italic"</c> (styles: Bold, Italic, Underline, Strikeout); <c>px</c> is accepted too.
    /// Empty means the ambient font (the container's). Pure; the runtime parses the same text.
    /// </summary>
    public static class FontText
    {
        /// <summary>What an absent <c>Font</c> stands for (shown greyed, like WinForms' ambient font).</summary>
        public const string AmbientDefault = "Segoe UI Variable Text, 10.5pt";

        /// <summary>Parses <paramref name="text"/>; false when it is not a font (an empty text is not a font either: it means ambient).</summary>
        public static bool TryParse(string? text, out FontSpec font)
        {
            font = null!;
            var parts = (text ?? string.Empty).Split(',').Select(p => p.Trim()).ToList();
            if (parts.Count < 2 || parts[0].Length == 0)
            {
                return false;
            }

            var sizeText = parts[1];
            var pixels = sizeText.EndsWith("px", StringComparison.OrdinalIgnoreCase);
            if (pixels || sizeText.EndsWith("pt", StringComparison.OrdinalIgnoreCase))
            {
                sizeText = sizeText.Substring(0, sizeText.Length - 2).Trim();
            }

            if (!float.TryParse(sizeText, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) || size <= 0 || float.IsInfinity(size))
            {
                return false;
            }

            bool bold = false, italic = false, underline = false, strikeout = false;
            for (var i = 2; i < parts.Count; i++)
            {
                var style = parts[i];
                if (style.StartsWith("style", StringComparison.OrdinalIgnoreCase))
                {
                    var eq = style.IndexOf('=');
                    if (eq < 0)
                    {
                        return false;
                    }

                    style = style.Substring(eq + 1).Trim();
                }

                switch (style.ToLowerInvariant())
                {
                    case "bold": bold = true; break;
                    case "italic": italic = true; break;
                    case "underline": underline = true; break;
                    case "strikeout": strikeout = true; break;
                    case "regular": break;
                    default: return false;
                }
            }

            font = new FontSpec(parts[0], size, pixels, bold, italic, underline, strikeout);
            return true;
        }

        /// <summary><c>"Family, 12pt[, style=Bold, Italic]"</c>.</summary>
        public static string Format(FontSpec font)
        {
            var styles = new List<string>();
            if (font.Bold) styles.Add("Bold");
            if (font.Italic) styles.Add("Italic");
            if (font.Underline) styles.Add("Underline");
            if (font.Strikeout) styles.Add("Strikeout");
            var size = font.Size.ToString("0.##", CultureInfo.InvariantCulture) + (font.Pixels ? "px" : "pt");
            return styles.Count == 0 ? $"{font.Family}, {size}" : $"{font.Family}, {size}, style={string.Join(", ", styles)}";
        }

        /// <summary>The canonical text of <paramref name="text"/> (empty stays empty: ambient); throws <see cref="ArgumentException"/> when it is not a font.</summary>
        public static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || Properties.BindingExpressionParser.IsBindingExpression(text))
            {
                return text?.Trim() ?? string.Empty;
            }

            return TryParse(text, out var font) ? Format(font) : throw new ArgumentException(DesignerText.InvalidFont(text));
        }

        /// <summary>The attribute text of a WinForms <see cref="System.Drawing.Font"/> (what the font dialog returns), in points.</summary>
        public static string FromDrawingFont(System.Drawing.Font font)
        {
            if (font is null)
            {
                throw new ArgumentNullException(nameof(font));
            }

            var size = (float)Math.Round(font.SizeInPoints * 4, MidpointRounding.AwayFromZero) / 4;
            return Format(new FontSpec(font.FontFamily.Name, size, false, font.Bold, font.Italic, font.Underline, font.Strikeout));
        }

        /// <summary>A WinForms font for <paramref name="text"/> (the font dialog's initial selection), or null.</summary>
        public static System.Drawing.Font? ToDrawingFont(string? text)
        {
            if (!TryParse(string.IsNullOrWhiteSpace(text) ? AmbientDefault : text, out var spec))
            {
                return null;
            }

            var style = System.Drawing.FontStyle.Regular;
            if (spec.Bold) style |= System.Drawing.FontStyle.Bold;
            if (spec.Italic) style |= System.Drawing.FontStyle.Italic;
            if (spec.Underline) style |= System.Drawing.FontStyle.Underline;
            if (spec.Strikeout) style |= System.Drawing.FontStyle.Strikeout;
            try
            {
                return new System.Drawing.Font(spec.Family, spec.Points, style, System.Drawing.GraphicsUnit.Point);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
