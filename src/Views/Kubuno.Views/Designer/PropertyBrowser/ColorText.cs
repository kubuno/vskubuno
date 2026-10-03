using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using Kubuno.Views.Designer.Registry;

namespace Kubuno.Views.Designer.PropertyBrowser
{
    /// <summary>What a colour attribute holds.</summary>
    public enum ColorValueKind
    {
        /// <summary>No colour: the control takes its container's (ambient), like an unset WinForms colour.</summary>
        Empty,

        /// <summary>A Kubuno theme colour (<see cref="ThemeTokens"/>), which follows the light, dark and high-contrast themes.</summary>
        Token,

        /// <summary><c>#RGB</c>, <c>#RRGGBB</c> or <c>#RRGGBBAA</c>.</summary>
        Hex,

        /// <summary>A named web colour (<c>CornflowerBlue</c>, <c>Transparent</c>).</summary>
        Web,

        /// <summary>A Windows system colour (<c>Control</c>, <c>WindowText</c>).</summary>
        System,
    }

    /// <summary>A parsed colour attribute value (see <see cref="ColorText"/>).</summary>
    public sealed class ColorValue
    {
        internal ColorValue(ColorValueKind kind, string text, Color light, Color dark, ThemeToken? token)
        {
            Kind = kind;
            Text = text;
            Light = light;
            Dark = dark;
            Token = token;
        }

        public ColorValueKind Kind { get; }

        /// <summary>The canonical attribute text (<c>"Primary"</c>, <c>"#FF8800"</c>, <c>"CornflowerBlue"</c>), empty for <see cref="ColorValueKind.Empty"/>.</summary>
        public string Text { get; }

        /// <summary>The colour in the light theme.</summary>
        public Color Light { get; }

        /// <summary>The colour in the dark theme (a free colour is the same in both).</summary>
        public Color Dark { get; }

        /// <summary>The theme token, for <see cref="ColorValueKind.Token"/>.</summary>
        public ThemeToken? Token { get; }

        /// <summary>A free colour: one that does not follow the theme (hex, web or system).</summary>
        public bool IsFree => Kind is ColorValueKind.Hex or ColorValueKind.Web;

        public override string ToString() => Text;
    }

    /// <summary>
    /// The colour attribute grammar, shared with the runtime (<c>kubuno-desktop-views</c>): empty (ambient), a theme token, <c>#RGB</c>
    /// / <c>#RRGGBB</c> / <c>#RRGGBBAA</c> (alpha last), a web colour name or a Windows system colour name - plus the WCAG
    /// contrast math the colour editor and the designer's warning use. Pure.
    /// </summary>
    public static class ColorText
    {
        /// <summary>The Windows system colours a view may name, in the WinForms colour editor's order.</summary>
        public static IReadOnlyList<string> SystemColorNames { get; } = new[]
        {
            "ActiveBorder", "ActiveCaption", "ActiveCaptionText", "AppWorkspace", "ButtonFace", "ButtonHighlight", "ButtonShadow",
            "Control", "ControlDark", "ControlDarkDark", "ControlLight", "ControlLightLight", "ControlText", "Desktop",
            "GradientActiveCaption", "GradientInactiveCaption", "GrayText", "Highlight", "HighlightText", "HotTrack",
            "InactiveBorder", "InactiveCaption", "InactiveCaptionText", "Info", "InfoText", "Menu", "MenuBar", "MenuHighlight",
            "MenuText", "ScrollBar", "Window", "WindowFrame", "WindowText",
        };

        /// <summary>The named web colours (every non-system <see cref="KnownColor"/>, Transparent first), like the WinForms editor's Web tab.</summary>
        public static IReadOnlyList<string> WebColorNames { get; } = Enum.GetValues(typeof(KnownColor))
            .Cast<KnownColor>()
            .Select(Color.FromKnownColor)
            .Where(c => !c.IsSystemColor)
            .Select(c => c.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        /// <summary>The WCAG AA minimum contrast for normal text.</summary>
        public const double MinimumContrast = 4.5;

        /// <summary>Parses <paramref name="text"/>; false when it is none of the accepted forms (an empty text is <see cref="ColorValueKind.Empty"/>).</summary>
        public static bool TryParse(string? text, out ColorValue value)
        {
            var trimmed = (text ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                value = new ColorValue(ColorValueKind.Empty, string.Empty, Color.Empty, Color.Empty, null);
                return true;
            }

            if (ThemeTokens.Find(trimmed) is { } token)
            {
                value = new ColorValue(ColorValueKind.Token, token.Name, ParseHex(token.Light) ?? Color.Empty, ParseHex(token.Dark) ?? Color.Empty, token);
                return true;
            }

            if (trimmed[0] == '#')
            {
                if (ParseHex(trimmed) is { } hex)
                {
                    value = new ColorValue(ColorValueKind.Hex, FormatHex(hex), hex, hex, null);
                    return true;
                }

                value = null!;
                return false;
            }

            var system = SystemColorNames.FirstOrDefault(n => string.Equals(n, trimmed, StringComparison.OrdinalIgnoreCase));
            if (system is not null)
            {
                var color = SystemColor(system);
                value = new ColorValue(ColorValueKind.System, system, color, color, null);
                return true;
            }

            var web = WebColorNames.FirstOrDefault(n => string.Equals(n, trimmed, StringComparison.OrdinalIgnoreCase));
            if (web is not null)
            {
                var color = Color.FromName(web);
                value = new ColorValue(ColorValueKind.Web, web, color, color, null);
                return true;
            }

            value = null!;
            return false;
        }

        /// <summary>The canonical text of <paramref name="text"/>; throws <see cref="ArgumentException"/> (the grid's "invalid property value") when it is not a colour.</summary>
        public static string Normalize(string text)
        {
            if (Properties.BindingExpressionParser.IsBindingExpression(text))
            {
                return text;
            }

            return TryParse(text, out var value) ? value.Text : throw new ArgumentException(DesignerText.InvalidColor(text));
        }

        /// <summary>
        /// <c>#RGB</c>, <c>#RRGGBB</c> or <c>#RRGGBBAA</c> (CSS order, alpha last), or null. <c>#RGB</c> doubles each digit.
        /// </summary>
        public static Color? ParseHex(string? text)
        {
            var s = (text ?? string.Empty).Trim();
            if (s.Length == 0 || s[0] != '#')
            {
                return null;
            }

            s = s.Substring(1);
            if (s.Length == 3)
            {
                s = string.Concat(s.Select(c => new string(c, 2)));
            }

            if ((s.Length != 6 && s.Length != 8) || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n))
            {
                return null;
            }

            return s.Length == 6
                ? Color.FromArgb(255, (int)(n >> 16) & 0xFF, (int)(n >> 8) & 0xFF, (int)n & 0xFF)
                : Color.FromArgb((int)n & 0xFF, (int)(n >> 24) & 0xFF, (int)(n >> 16) & 0xFF, (int)(n >> 8) & 0xFF);
        }

        /// <summary><c>#RRGGBB</c>, or <c>#RRGGBBAA</c> when not opaque (upper-case hex digits).</summary>
        public static string FormatHex(Color color) => color.A == 255
            ? string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B)
            : string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}{3:X2}", color.R, color.G, color.B, color.A);

        /// <summary>The current value of Windows system colour <paramref name="name"/>.</summary>
        public static Color SystemColor(string name) =>
            Enum.TryParse<KnownColor>(name, ignoreCase: true, out var known) ? Color.FromKnownColor(known) : Color.Empty;

        /// <summary>WCAG relative luminance of an opaque colour.</summary>
        public static double RelativeLuminance(Color color)
        {
            static double Channel(int c)
            {
                var s = c / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }

            return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
        }

        /// <summary><paramref name="top"/> drawn over <paramref name="bottom"/> (alpha compositing; the result is opaque when <paramref name="bottom"/> is).</summary>
        public static Color Over(Color top, Color bottom)
        {
            if (top.A == 255)
            {
                return top;
            }

            var a = top.A / 255.0;
            int Mix(int t, int b) => (int)Math.Round((t * a) + (b * (1 - a)));
            return Color.FromArgb(255, Mix(top.R, bottom.R), Mix(top.G, bottom.G), Mix(top.B, bottom.B));
        }

        /// <summary>The WCAG contrast ratio (1 to 21) of <paramref name="foreground"/> over <paramref name="background"/> (both composited over white first when translucent).</summary>
        public static double ContrastRatio(Color foreground, Color background)
        {
            var bg = Over(background, Color.White);
            var fg = Over(foreground, bg);
            var l1 = RelativeLuminance(fg);
            var l2 = RelativeLuminance(bg);
            return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
        }
    }
}
