using System.Globalization;

namespace Kubuno.VisualStudio.Designer.Selection
{
    /// <summary>
    /// Pure helpers over the stable element id scheme docs/DESIGNER.md §8 documents ("a dot-separated
    /// path of child-ordinal indices from the document root ... independent of x:Name") -
    /// [`kubuno_views::ast::Element::stable_id`]/<c>Document::resolve_id</c>'s exact C# mirror, needed on
    /// this side of the wire wherever a caller must build or walk an id itself rather than just carrying
    /// one around opaquely: <see cref="ElementAttributeReader"/> (walking down TO an id) and
    /// <see cref="Outline.DocumentSymbolTreeBuilder"/> (assigning one to each node of a
    /// <c>textDocument/documentSymbol</c> response, which carries no id of its own).
    /// </summary>
    public static class StableElementId
    {
        /// <summary>The document root element's own id - the empty path (docs/DESIGNER.md §8: "The document's root element is the empty string").</summary>
        public const string Root = "";

        /// <summary><paramref name="index"/>-th child's id under <paramref name="parentId"/>: <c>"2.0.3"</c>'s child 1 is <c>"2.0.3.1"</c>; the root's (<c>""</c>) child 3 is <c>"3"</c>, not <c>".3"</c> - mirrors <c>edit_bridge::split_parent</c>'s own inverse operation.</summary>
        public static string Child(string parentId, int index)
        {
            var suffix = index.ToString(CultureInfo.InvariantCulture);
            return string.IsNullOrEmpty(parentId) ? suffix : parentId + "." + suffix;
        }

        /// <summary>
        /// Splits <paramref name="elementId"/> into its dot-separated ordinal path, one entry per level
        /// from the document root - e.g. <c>"2.0.3"</c> -&gt; <c>[2, 0, 3]</c>, <c>""</c> (the root
        /// itself) -&gt; an empty array. <see langword="false"/> for a malformed id (a non-numeric or
        /// negative segment) - never throws, mirroring <c>Document::resolve_id</c>'s own "None for a
        /// malformed id ... never a panic" contract.
        /// </summary>
        public static bool TryParseSegments(string elementId, out int[] segments)
        {
            if (elementId is null)
            {
                segments = System.Array.Empty<int>();
                return false;
            }

            if (elementId.Length == 0)
            {
                segments = System.Array.Empty<int>();
                return true;
            }

            var parts = elementId.Split('.');
            var result = new int[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                {
                    segments = System.Array.Empty<int>();
                    return false;
                }

                result[i] = value;
            }

            segments = result;
            return true;
        }
    }
}
