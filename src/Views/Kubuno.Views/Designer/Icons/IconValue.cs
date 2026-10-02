using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Kubuno.Views.Designer.Icons
{
    /// <summary>What an icon attribute value names (docs/ICONS.md).</summary>
    public enum IconValueKind
    {
        /// <summary>Empty: no icon.</summary>
        None,

        /// <summary>A glyph of the Kubuno icon set, or one of its aliases (<c>Save</c>, <c>trash</c>).</summary>
        Glyph,

        /// <summary>An image file, relative to the view (<c>resources/save.svg</c>).</summary>
        File,

        /// <summary>A resource of the project (<c>{Res Logo}</c>).</summary>
        Resource,

        /// <summary>A binding (<c>{Binding Icon}</c>).</summary>
        Binding,
    }

    /// <summary>
    /// The icon attribute syntax, shared by the Properties window, the icon picker and the tests: a glyph name
    /// (<c>Icon="Save"</c>), an image file relative to the view like <c>BackgroundImage</c>
    /// (<c>Icon="resources/save.svg"</c>), a resource (<c>Icon="{Res Logo}"</c>) or a binding. Mirrors
    /// <c>kubuno_views::icon</c> and <c>drive_app_controls::icon_source</c> on the Rust side. Pure.
    /// </summary>
    public static class IconValue
    {
        /// <summary>The image file extensions an icon may name - the same list as the runtime's.</summary>
        public static IReadOnlyList<string> Extensions { get; } = new[] { ".svg", ".png", ".ico", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp" };

        public static bool IsImageFile(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                return Extensions.Contains(Path.GetExtension(path!.Trim()).ToLowerInvariant());
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        public static IconValueKind Classify(string? value)
        {
            var v = (value ?? string.Empty).Trim();
            if (v.Length == 0)
            {
                return IconValueKind.None;
            }

            if (v.StartsWith("{", StringComparison.Ordinal) && v.EndsWith("}", StringComparison.Ordinal))
            {
                return ResourceKey(v) is null ? IconValueKind.Binding : IconValueKind.Resource;
            }

            return IsImageFile(v) ? IconValueKind.File : IconValueKind.Glyph;
        }

        /// <summary>The key of a <c>{Res key}</c> (or <c>{Res key, Source=set}</c>) value; null for anything else.</summary>
        public static string? ResourceKey(string? value)
        {
            var v = (value ?? string.Empty).Trim();
            if (!v.StartsWith("{", StringComparison.Ordinal) || !v.EndsWith("}", StringComparison.Ordinal))
            {
                return null;
            }

            var inner = v.Substring(1, v.Length - 2).Trim();
            if (!inner.StartsWith("Res", StringComparison.Ordinal) || inner.Length < 4 || !char.IsWhiteSpace(inner[3]))
            {
                return null;
            }

            var key = inner.Substring(4).Split(',')[0].Trim();
            return key.Length == 0 ? null : key;
        }

        /// <summary>The text written for <paramref name="text"/> typed in the Properties window: trimmed, a file path with forward slashes.</summary>
        public static string Normalize(string? text)
        {
            var v = (text ?? string.Empty).Trim();
            return Classify(v) == IconValueKind.File ? v.Replace('\\', '/') : v;
        }

        /// <summary>The glyph name a value shows in the grid and the picker: the alias target (<c>trash</c> to <c>Trash2</c>), else the value.</summary>
        public static string GlyphName(string value, IconCatalog catalog) =>
            catalog.Aliases.TryGetValue(value.Trim(), out var target) ? target : value.Trim();
    }
}
