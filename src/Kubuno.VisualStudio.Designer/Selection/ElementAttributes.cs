using System;
using System.Collections.Generic;

namespace Kubuno.VisualStudio.Designer.Selection
{
    /// <summary>One element's tag name and raw attribute values, as <see cref="ElementAttributeReader.Read"/> reads them off the current buffer text.</summary>
    public sealed class ElementAttributes
    {
        public ElementAttributes(string tagName, IReadOnlyDictionary<string, string?> attributes)
        {
            TagName = tagName ?? throw new ArgumentNullException(nameof(tagName));
            Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
        }

        /// <summary>The element's tag name, e.g. <c>"Button"</c> - looked up against a <c>Registry.ComponentRegistry</c> to find its <c>ComponentMeta</c>.</summary>
        public string TagName { get; }

        /// <summary>
        /// Every attribute actually present on the start tag, keyed by name exactly as written (e.g.
        /// <c>"x:Name"</c>, <c>"OnClick"</c>) - raw values, quotes stripped, no entity decoding (mirrors
        /// <c>kubuno_views::ast::Attribute::value</c>'s own "quotes stripped, nothing else" contract
        /// verbatim, so this reader never shows a value the real edit engine would not also produce
        /// byte-for-byte). On a duplicate attribute name (malformed input), the FIRST occurrence wins -
        /// matching <c>Element::attribute</c>'s own <c>.find(...)</c> semantics.
        /// </summary>
        public IReadOnlyDictionary<string, string?> Attributes { get; }
    }
}
