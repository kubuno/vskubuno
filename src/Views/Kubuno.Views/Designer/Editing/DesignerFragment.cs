using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Kubuno.Views.Designer.Selection;

namespace Kubuno.Views.Designer.Editing
{
    /// <summary>
    /// The designer's clipboard content (docs/DESIGNER.md §12): an element's own <c>.kbview</c> XML, copied
    /// under the private format <see cref="ClipboardFormat"/> AND as plain text (so it can be pasted into the
    /// XML or any editor, and XML copied from a text editor can be pasted onto the surface). Pure helpers.
    /// </summary>
    public static class DesignerFragment
    {
        /// <summary>The private clipboard format carrying a copied element (UTF-16 text, like CF_UNICODETEXT).</summary>
        public const string ClipboardFormat = "Kubuno.Views.Fragment";

        private static readonly Regex RootTagPattern = new Regex(@"^\s*(?:<!--.*?-->\s*)*<([A-Za-z_][A-Za-z0-9_.:\-]*)", RegexOptions.Singleline | RegexOptions.CultureInvariant);

        /// <summary>
        /// An element's source text as a self-contained fragment: its continuation lines lose the indentation
        /// of the line the element starts on (<paramref name="linePrefix"/>, the whitespace before it on that
        /// line), so the fragment reads as if written at column 0. Lines indented less are left as they are.
        /// </summary>
        public static string Normalize(string elementText, string? linePrefix)
        {
            if (elementText is null)
            {
                return string.Empty;
            }

            if (string.IsNullOrEmpty(linePrefix))
            {
                return elementText;
            }

            var lines = elementText.Split('\n');
            var builder = new StringBuilder(lines[0]);
            for (var i = 1; i < lines.Length; i++)
            {
                builder.Append('\n');
                var line = lines[i];
                builder.Append(line.StartsWith(linePrefix, StringComparison.Ordinal) ? line.Substring(linePrefix!.Length) : line);
            }

            return builder.ToString();
        }

        /// <summary>The whitespace between the start of <paramref name="offset"/>'s line and <paramref name="offset"/>, or null when something else precedes it on that line.</summary>
        public static string? LinePrefix(string text, int offset)
        {
            if (text is null || offset < 0 || offset > text.Length)
            {
                return null;
            }

            var lineStart = offset == 0 ? 0 : text.LastIndexOf('\n', offset - 1) + 1;
            var prefix = text.Substring(lineStart, offset - lineStart);
            foreach (var c in prefix)
            {
                if (c != ' ' && c != '\t')
                {
                    return null;
                }
            }

            return prefix;
        }

        /// <summary>The tag name of the fragment's (first) element, or null when the text does not start with an element.</summary>
        public static string? RootTag(string? fragment)
        {
            if (string.IsNullOrWhiteSpace(fragment))
            {
                return null;
            }

            var match = RootTagPattern.Match(fragment);
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// The tag names of the fragment's top-level elements, in order - several for a multi-selection copied
        /// together (docs/DESIGNER.md §13: the elements' fragments one after the other). Empty when the text
        /// holds no element.
        /// </summary>
        public static IReadOnlyList<string> RootTags(string? fragment)
        {
            if (string.IsNullOrWhiteSpace(fragment))
            {
                return Array.Empty<string>();
            }

            // Read inside a wrapper, so a sequence of elements is one document.
            var wrapper = ElementAttributeReader.Read("<Fragment>" + fragment + "</Fragment>", StableElementId.Root);
            return wrapper?.ChildTagNames.ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();
        }

        /// <summary>Several elements' fragments as one clipboard text (each at column 0, one after the other).</summary>
        public static string Join(IEnumerable<string> fragments) => string.Join("\n", fragments.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim()));
    }

    /// <summary>
    /// The view's design size as the designer reads and writes it (the C# mirror of
    /// <c>kubuno_desktop_views::design::design_size</c>): per axis, the root element's literal numeric
    /// <c>Width</c>/<c>Height</c>, else the design-time <c>DesignWidth</c>/<c>DesignHeight</c>, else 800×600.
    /// </summary>
    public sealed class DesignSizeInfo
    {
        public const double DefaultWidth = 800;
        public const double DefaultHeight = 600;

        private DesignSizeInfo(double width, double height, string widthAttribute, string heightAttribute)
        {
            Width = width;
            Height = height;
            WidthAttribute = widthAttribute;
            HeightAttribute = heightAttribute;
        }

        public double Width { get; }

        public double Height { get; }

        /// <summary><c>"Width"</c> or <c>"DesignWidth"</c>: where a new width is written.</summary>
        public string WidthAttribute { get; }

        /// <summary><c>"Height"</c> or <c>"DesignHeight"</c>.</summary>
        public string HeightAttribute { get; }

        public static DesignSizeInfo Read(string text)
        {
            var root = ElementAttributeReader.Read(text ?? string.Empty, StableElementId.Root);
            var (width, widthAttribute) = Axis(root, "Width", "DesignWidth", DefaultWidth);
            var (height, heightAttribute) = Axis(root, "Height", "DesignHeight", DefaultHeight);
            return new DesignSizeInfo(width, height, widthAttribute, heightAttribute);
        }

        private static (double, string) Axis(ElementAttributes? root, string real, string design, double fallback)
        {
            if (Literal(root, real) is { } value)
            {
                return (value, real);
            }

            return (Literal(root, design) ?? fallback, design);
        }

        private static double? Literal(ElementAttributes? root, string name) =>
            root is not null && root.Attributes.TryGetValue(name, out var raw) &&
            double.TryParse(raw?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0 && !double.IsInfinity(value)
                ? value
                : null;

        /// <summary>A size value as the attribute text the designer writes (whole DIP, invariant culture).</summary>
        public static string Format(double value) => Math.Round(value).ToString(CultureInfo.InvariantCulture);
    }
}
