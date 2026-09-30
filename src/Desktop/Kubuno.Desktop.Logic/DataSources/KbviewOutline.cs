using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Kubuno.VisualStudio.Core.DataSources
{
    /// <summary>
    /// A read-only outline of a <c>.kbview</c>: its elements, their attributes and their stable ids (<c>""</c> = the root,
    /// <c>"0"</c> its first child element, <c>"0.2"</c>...: the ids of <c>kubuno-views-ls</c> and the design surface). A small
    /// tolerant scanner (comments, processing instructions and CDATA skipped; any prefix accepted), enough to plan a drop.
    /// </summary>
    public sealed class KbviewOutline
    {
        private KbviewOutline(KbviewElement root)
        {
            Root = root;
        }

        public KbviewElement Root { get; }

        /// <summary>Every element, in document order.</summary>
        public IEnumerable<KbviewElement> All()
        {
            var stack = new Stack<KbviewElement>();
            stack.Push(Root);
            while (stack.Count > 0)
            {
                var element = stack.Pop();
                yield return element;
                for (int i = element.Children.Count - 1; i >= 0; i--)
                {
                    stack.Push(element.Children[i]);
                }
            }
        }

        /// <summary>The element of stable id <paramref name="id"/>, or null.</summary>
        public KbviewElement? Find(string? id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return Root;
            }

            KbviewElement current = Root;
            foreach (var part in id!.Split('.'))
            {
                if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int index) || index < 0 || index >= current.Children.Count)
                {
                    return null;
                }

                current = current.Children[index];
            }

            return current;
        }

        /// <summary>Every <c>x:Name</c> of the view.</summary>
        public ISet<string> Names() => new HashSet<string>(All().Select(e => e.XName).Where(n => !string.IsNullOrEmpty(n))!, StringComparer.Ordinal);

        /// <summary>Parses <paramref name="text"/>; null when it has no well-formed root element.</summary>
        public static KbviewOutline? TryParse(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            var stack = new Stack<KbviewElement>();
            KbviewElement? root = null;
            int i = 0;
            string s = text!;
            while (i < s.Length)
            {
                int lt = s.IndexOf('<', i);
                if (lt < 0)
                {
                    break;
                }

                if (Starts(s, lt, "<!--"))
                {
                    int end = s.IndexOf("-->", lt + 4, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        return null;
                    }

                    i = end + 3;
                    continue;
                }

                if (Starts(s, lt, "<![CDATA["))
                {
                    int end = s.IndexOf("]]>", lt, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        return null;
                    }

                    i = end + 3;
                    continue;
                }

                if (Starts(s, lt, "<?") || Starts(s, lt, "<!"))
                {
                    int end = s.IndexOf('>', lt);
                    if (end < 0)
                    {
                        return null;
                    }

                    i = end + 1;
                    continue;
                }

                if (Starts(s, lt, "</"))
                {
                    int end = s.IndexOf('>', lt);
                    if (end < 0 || stack.Count == 0)
                    {
                        return null;
                    }

                    string name = s.Substring(lt + 2, end - lt - 2).Trim();
                    var open = stack.Pop();
                    if (!string.Equals(open.Name, name, StringComparison.Ordinal))
                    {
                        return null;
                    }

                    i = end + 1;
                    continue;
                }

                // A start tag.
                int p = lt + 1;
                int nameStart = p;
                while (p < s.Length && !char.IsWhiteSpace(s[p]) && s[p] != '>' && s[p] != '/')
                {
                    p++;
                }

                if (p == nameStart)
                {
                    return null;
                }

                var element = new KbviewElement(s.Substring(nameStart, p - nameStart));
                bool selfClosing = false;
                while (true)
                {
                    while (p < s.Length && char.IsWhiteSpace(s[p]))
                    {
                        p++;
                    }

                    if (p >= s.Length)
                    {
                        return null;
                    }

                    if (s[p] == '>')
                    {
                        p++;
                        break;
                    }

                    if (s[p] == '/' && p + 1 < s.Length && s[p + 1] == '>')
                    {
                        selfClosing = true;
                        p += 2;
                        break;
                    }

                    int attrStart = p;
                    while (p < s.Length && s[p] != '=' && !char.IsWhiteSpace(s[p]) && s[p] != '>' && s[p] != '/')
                    {
                        p++;
                    }

                    string attrName = s.Substring(attrStart, p - attrStart);
                    while (p < s.Length && char.IsWhiteSpace(s[p]))
                    {
                        p++;
                    }

                    if (attrName.Length == 0 || p >= s.Length || s[p] != '=')
                    {
                        return null;
                    }

                    p++;
                    while (p < s.Length && char.IsWhiteSpace(s[p]))
                    {
                        p++;
                    }

                    if (p >= s.Length || (s[p] != '"' && s[p] != '\''))
                    {
                        return null;
                    }

                    char quote = s[p];
                    int valueEnd = s.IndexOf(quote, p + 1);
                    if (valueEnd < 0)
                    {
                        return null;
                    }

                    element.Attributes[attrName] = Decode(s.Substring(p + 1, valueEnd - p - 1));
                    p = valueEnd + 1;
                }

                if (stack.Count == 0)
                {
                    if (root != null)
                    {
                        return null;
                    }

                    root = element;
                }
                else
                {
                    var parent = stack.Peek();
                    element.Id = parent.Id.Length == 0
                        ? parent.Children.Count.ToString(CultureInfo.InvariantCulture)
                        : parent.Id + "." + parent.Children.Count.ToString(CultureInfo.InvariantCulture);
                    parent.Children.Add(element);
                }

                if (!selfClosing)
                {
                    stack.Push(element);
                }

                i = p;
            }

            return root != null && stack.Count == 0 ? new KbviewOutline(root) : null;
        }

        private static bool Starts(string s, int index, string prefix) => string.CompareOrdinal(s, index, prefix, 0, prefix.Length) == 0;

        private static string Decode(string value)
        {
            if (value.IndexOf('&') < 0)
            {
                return value;
            }

            var builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] == '&')
                {
                    int semi = value.IndexOf(';', i);
                    if (semi > i)
                    {
                        string entity = value.Substring(i + 1, semi - i - 1);
                        string? decoded = entity switch
                        {
                            "amp" => "&",
                            "lt" => "<",
                            "gt" => ">",
                            "quot" => "\"",
                            "apos" => "'",
                            _ when entity.StartsWith("#x", StringComparison.Ordinal) && int.TryParse(entity.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hex) => char.ConvertFromUtf32(hex),
                            _ when entity.StartsWith("#", StringComparison.Ordinal) && int.TryParse(entity.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out int dec) => char.ConvertFromUtf32(dec),
                            _ => null,
                        };
                        if (decoded != null)
                        {
                            builder.Append(decoded);
                            i = semi;
                            continue;
                        }
                    }
                }

                builder.Append(value[i]);
            }

            return builder.ToString();
        }
    }

    /// <summary>One element of a <see cref="KbviewOutline"/>.</summary>
    public sealed class KbviewElement
    {
        internal KbviewElement(string name)
        {
            Name = name;
        }

        /// <summary>The tag name (<c>DataTable</c>).</summary>
        public string Name { get; }

        /// <summary>The stable id (<c>""</c> for the root).</summary>
        public string Id { get; internal set; } = string.Empty;

        /// <summary>The attributes, decoded, in document order.</summary>
        public Dictionary<string, string> Attributes { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        public List<KbviewElement> Children { get; } = new List<KbviewElement>();

        /// <summary>The <c>x:Name</c> (or <c>Name</c>), null when absent.</summary>
        public string? XName => Attribute("x:Name") ?? Attribute("Name");

        public string? Attribute(string name) => Attributes.TryGetValue(name, out var value) ? value : null;

        /// <summary>A numeric attribute (<c>X</c>, <c>Height</c>...), null when absent or not a number.</summary>
        public double? Number(string name) =>
            Attribute(name) is { } text && double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : (double?)null;
    }
}
