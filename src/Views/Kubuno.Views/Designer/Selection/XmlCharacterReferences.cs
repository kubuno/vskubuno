using System.Globalization;
using System.Text;

namespace Kubuno.Views.Designer.Selection
{
    /// <summary>
    /// Decodes the XML character references of an attribute value as <c>kubuno_views::ast::decode_entities</c> does
    /// (the runtime reads <c>Text="&amp;amp;Save"</c> as <c>&amp;Save</c>, and the designer writes a typed <c>&amp;</c>
    /// as <c>&amp;amp;</c>): the five predefined entities and numeric references. Anything else starting with
    /// <c>&amp;</c> (a bare ampersand, an unknown name) is kept as written.
    /// </summary>
    public static class XmlCharacterReferences
    {
        public static string Decode(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('&') < 0)
            {
                return text;
            }

            var sb = new StringBuilder(text.Length);
            var i = 0;
            while (i < text.Length)
            {
                var c = text[i];
                if (c == '&')
                {
                    var end = text.IndexOf(';', i + 1);
                    if (end > i && end - i <= 12 && TryDecode(text.Substring(i + 1, end - i - 1), out var decoded))
                    {
                        sb.Append(decoded);
                        i = end + 1;
                        continue;
                    }
                }

                sb.Append(c);
                i++;
            }

            return sb.ToString();
        }

        private static bool TryDecode(string name, out string decoded)
        {
            switch (name)
            {
                case "amp": decoded = "&"; return true;
                case "lt": decoded = "<"; return true;
                case "gt": decoded = ">"; return true;
                case "quot": decoded = "\""; return true;
                case "apos": decoded = "'"; return true;
            }

            decoded = string.Empty;
            if (name.Length < 2 || name[0] != '#')
            {
                return false;
            }

            var hex = name[1] == 'x' || name[1] == 'X';
            var digits = hex ? name.Substring(2) : name.Substring(1);
            if (!int.TryParse(digits, hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None, CultureInfo.InvariantCulture, out var code)
                || code < 0 || code > 0x10FFFF || (code >= 0xD800 && code <= 0xDFFF))
            {
                return false;
            }

            decoded = char.ConvertFromUtf32(code);
            return true;
        }
    }
}
