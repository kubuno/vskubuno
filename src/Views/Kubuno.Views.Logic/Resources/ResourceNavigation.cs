using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kubuno.Desktop.Logic.Resources
{
    /// <summary>
    /// Go To Definition from the code <c>resources!</c> generates (docs/RESOURCES.md): rust-analyzer sends
    /// <c>Resources::logo()</c> to the <c>resources!("resources.kbres")</c> call that generated it; this finds the entry
    /// the accessor stands for in the <c>.kbres</c> file instead (like F12 on <c>Properties.Resources.Logo</c> opening the
    /// .resx entry).
    /// </summary>
    public static class ResourceNavigation
    {
        private static readonly Regex Call = new Regex(@"resources!\s*\(\s*(?:(?:pub(?:\s*\([^)]*\))?\s+)?(?<type>[A-Za-z_][A-Za-z0-9_]*)\s*,\s*)?""(?<path>[^""]+)""", RegexOptions.Compiled);

        /// <summary>A <c>resources!</c> call: its file argument and the type it generates.</summary>
        public sealed class MacroCall
        {
            public MacroCall(string path, string typeName)
            {
                Path = path;
                TypeName = typeName;
            }

            public string Path { get; }

            public string TypeName { get; }
        }

        /// <summary>The <c>resources!</c> call on <paramref name="line"/>, or null.</summary>
        public static MacroCall? ParseCall(string line)
        {
            var m = Call.Match(line);
            if (!m.Success)
            {
                return null;
            }

            var path = m.Groups["path"].Value;
            var stem = ResourceNames.SplitFileName(System.IO.Path.GetFileName(path))?.Stem ?? System.IO.Path.GetFileNameWithoutExtension(path);
            var type = m.Groups["type"].Success ? m.Groups["type"].Value : ResourceNames.TypeName(stem);
            return new MacroCall(path, type);
        }

        /// <summary>The identifier at <paramref name="offset"/> of <paramref name="text"/>, and the path segment before it (<c>Resources</c> in <c>Resources::logo</c>).</summary>
        public static (string Identifier, string? Qualifier)? IdentifierAt(string text, int offset)
        {
            if (offset < 0 || offset > text.Length)
            {
                return null;
            }

            static bool IsIdent(char c) => char.IsLetterOrDigit(c) || c == '_';
            var start = offset;
            while (start > 0 && IsIdent(text[start - 1]))
            {
                start--;
            }

            var end = offset;
            while (end < text.Length && IsIdent(text[end]))
            {
                end++;
            }

            if (end == start)
            {
                return null;
            }

            var ident = text.Substring(start, end - start);
            string? qualifier = null;
            var q = start;
            while (q > 0 && char.IsWhiteSpace(text[q - 1]))
            {
                q--;
            }

            if (q >= 2 && text[q - 1] == ':' && text[q - 2] == ':')
            {
                var qe = q - 2;
                while (qe > 0 && char.IsWhiteSpace(text[qe - 1]))
                {
                    qe--;
                }

                var qs = qe;
                while (qs > 0 && IsIdent(text[qs - 1]))
                {
                    qs--;
                }

                if (qe > qs)
                {
                    qualifier = text.Substring(qs, qe - qs);
                }
            }

            return (ident, qualifier);
        }

        /// <summary>
        /// The <c>.kbres</c> file a <c>resources!</c> call in <paramref name="rustFile"/> names: relative to that file, then
        /// to the package's <c>src</c> folder and root (the macro's own order).
        /// </summary>
        public static string? ResolveFile(string rustFile, string relative, Func<string, bool>? exists = null)
        {
            exists ??= File.Exists;
            if (System.IO.Path.IsPathRooted(relative))
            {
                return exists(relative) ? relative : null;
            }

            var candidates = new List<string> { System.IO.Path.Combine(System.IO.Path.GetDirectoryName(rustFile) ?? string.Empty, relative) };
            var root = ProjectResources.ProjectRoot(rustFile);
            candidates.Add(System.IO.Path.Combine(root, "src", relative));
            candidates.Add(System.IO.Path.Combine(root, relative));
            return candidates.Select(System.IO.Path.GetFullPath).FirstOrDefault(exists);
        }

        /// <summary>
        /// Where F12 on <paramref name="identifier"/> (qualified by <paramref name="qualifier"/>) goes in the file of
        /// <paramref name="call"/>: the entry whose generated accessor it is - (line, column) 0-based of its name - or the
        /// top of the file for the type itself; null when it is neither.
        /// </summary>
        public static (int Line, int Character)? Target(MacroCall call, string kbresText, string identifier, string? qualifier)
        {
            if (identifier == call.TypeName)
            {
                return (0, 0);
            }

            if (qualifier is not null && qualifier != call.TypeName && qualifier != "Self")
            {
                return null;
            }

            var entry = KbresFile.Parse(kbresText).Entries.FirstOrDefault(e => ResourceNames.RustName(e.Name) == identifier);
            if (entry is null)
            {
                return null;
            }

            var lines = kbresText.Replace("\r\n", "\n").Split('\n');
            var line = Math.Max(0, entry.Line - 1);
            var col = line < lines.Length ? lines[line].IndexOf("Name=\"" + KbresFile.EscapeAttr(entry.Name) + "\"", StringComparison.Ordinal) : -1;
            return (line, col < 0 ? 0 : col + 6);
        }
    }
}
