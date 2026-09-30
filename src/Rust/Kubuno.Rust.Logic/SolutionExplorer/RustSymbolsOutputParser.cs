using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Kubuno.VisualStudio.Core.SolutionExplorer
{
    /// <summary>
    /// Parses the output of <c>rust-analyzer symbols</c> (source on stdin, one
    /// <c>StructureNode { ... }</c> line per item on stdout) into a <see cref="SolutionSymbol"/> tree.
    ///
    /// <para><b>Why this command</b> (docs/RSPROJ.md lot 8): rust-analyzer answers
    /// <c>textDocument/documentSymbol</c> with <c>Analysis::file_structure</c>, a purely syntactic
    /// walk of one file; <c>rust-analyzer symbols</c> prints exactly that same list without starting a
    /// language server or loading the Cargo workspace. The Solution Explorer tree therefore gets the
    /// editor's own document symbols without a second rust-analyzer server and whether or not the
    /// editor's server has started yet.</para>
    ///
    /// <para>The lines are Rust <c>Debug</c> output, not a stable API: parsing is tolerant (an
    /// unknown kind becomes <see cref="SolutionSymbolKind.Other"/>, an unparsable line is skipped
    /// while keeping parent indices aligned). Ranges are UTF-8 byte offsets into the text that was
    /// piped in, which is why the source bytes are needed to compute lines, columns and
    /// visibility.</para>
    /// </summary>
    public static class RustSymbolsOutputParser
    {
        private static readonly Regex NodeLine = new Regex(
            @"^StructureNode \{ parent: (?:None|Some\((?<parent>\d+)\)), label: ""(?<label>(?:[^""\\]|\\.)*)"", " +
            @"navigation_range: (?<ns>\d+)\.\.(?<ne>\d+), node_range: (?<rs>\d+)\.\.(?<re>\d+), " +
            @"kind: (?:SymbolKind\((?<kind>\w+)\)|(?<kind>\w+)(?:\([^)]*\))?), " +
            @"detail: (?:None|Some\(""(?<detail>(?:[^""\\]|\\.)*)""\))",
            RegexOptions.CultureInvariant);

        /// <param name="output">The command's stdout.</param>
        /// <param name="source">The exact bytes piped to its stdin (UTF-8, no BOM).</param>
        public static IReadOnlyList<SolutionSymbol> Parse(string output, byte[] source)
        {
            if (output is null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var positions = new PositionIndex(source);
            var nodes = new List<SolutionSymbol?>();
            var roots = new List<SolutionSymbol>();

            foreach (var rawLine in output.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                if (!line.StartsWith("StructureNode", StringComparison.Ordinal))
                {
                    continue;
                }

                var match = NodeLine.Match(line);
                if (!match.Success)
                {
                    nodes.Add(null);
                    continue;
                }

                SolutionSymbol? parent = null;
                if (match.Groups["parent"].Success)
                {
                    int parentIndex = int.Parse(match.Groups["parent"].Value, CultureInfo.InvariantCulture);
                    parent = parentIndex < nodes.Count ? nodes[parentIndex] : null;
                }

                var label = Unescape(match.Groups["label"].Value);
                var detail = match.Groups["detail"].Success ? Unescape(match.Groups["detail"].Value) : null;
                int nameStart = ParseOffset(match, "ns", source.Length);
                int nodeStart = Math.Min(ParseOffset(match, "rs", source.Length), nameStart);

                // Locals (`let` bindings inside a fn) are an outline detail, not a member: Roslyn's
                // Solution Explorer tree stops at members too.
                if (match.Groups["kind"].Value == "Local")
                {
                    nodes.Add(null);
                    continue;
                }

                var kind = MapKind(match.Groups["kind"].Value, label);
                var prefix = Encoding.UTF8.GetString(source, nodeStart, nameStart - nodeStart);
                var visibility = RustVisibilityReader.Read(prefix, out bool isMacroExport);
                visibility = InheritedVisibility(kind, parent, visibility, isMacroExport);

                var (lineNumber, column) = positions.ToLineColumn(nameStart);
                var symbol = new SolutionSymbol(label, kind, visibility, detail, lineNumber, column);
                nodes.Add(symbol);

                if (match.Groups["parent"].Success)
                {
                    if (parent != null)
                    {
                        parent.Children.Add(symbol);
                    }

                    // An orphan (its parent line failed to parse) is dropped rather than misplaced.
                }
                else
                {
                    roots.Add(symbol);
                }
            }

            return roots;
        }

        private static int ParseOffset(Match match, string group, int max)
        {
            var value = long.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);
            return (int)Math.Max(0, Math.Min(value, max));
        }

        /// <summary>rust-analyzer's <c>SymbolKind</c>/<c>StructureNodeKind</c> names to ours.</summary>
        public static SolutionSymbolKind MapKind(string kind, string label)
        {
            switch (kind)
            {
                case "Struct": return SolutionSymbolKind.Struct;
                case "Enum": return SolutionSymbolKind.Enum;
                case "Union": return SolutionSymbolKind.Union;
                case "Trait":
                case "TraitAlias": return SolutionSymbolKind.Trait;
                case "Impl":
                    // "impl Display for Foo" vs. the inherent "impl Foo".
                    return Regex.IsMatch(label, @"\sfor\s") ? SolutionSymbolKind.TraitImpl : SolutionSymbolKind.Impl;
                case "Function": return SolutionSymbolKind.Function;
                case "Method": return SolutionSymbolKind.Method;
                case "Field": return SolutionSymbolKind.Field;
                case "Variant": return SolutionSymbolKind.Variant;
                case "Const": return SolutionSymbolKind.Const;
                case "Static": return SolutionSymbolKind.Static;
                case "Module": return SolutionSymbolKind.Module;
                case "TypeAlias": return SolutionSymbolKind.TypeAlias;
                case "Macro":
                case "ProcMacro":
                case "Attribute":
                case "Derive": return SolutionSymbolKind.Macro;
                case "Region": return SolutionSymbolKind.Region;
                case "ExternBlock": return SolutionSymbolKind.ExternBlock;
                default: return SolutionSymbolKind.Other;
            }
        }

        /// <summary>
        /// Items whose visibility is not written on them: trait members and trait-impl members are
        /// as visible as the trait (shown public, like Roslyn's interface members), enum variants as
        /// the enum; an impl block has no visibility of its own; <c>#[macro_export]</c> makes a
        /// <c>macro_rules!</c> public.
        /// </summary>
        private static SymbolVisibility InheritedVisibility(SolutionSymbolKind kind, SolutionSymbol? parent, SymbolVisibility written, bool isMacroExport)
        {
            if (kind == SolutionSymbolKind.Impl || kind == SolutionSymbolKind.TraitImpl || kind == SolutionSymbolKind.Variant
                || kind == SolutionSymbolKind.Region || kind == SolutionSymbolKind.ExternBlock)
            {
                return SymbolVisibility.Public;
            }

            if (kind == SolutionSymbolKind.Macro && isMacroExport)
            {
                return SymbolVisibility.Public;
            }

            if (parent != null && (parent.Kind == SolutionSymbolKind.Trait || parent.Kind == SolutionSymbolKind.TraitImpl || parent.Kind == SolutionSymbolKind.Variant))
            {
                return SymbolVisibility.Public;
            }

            return written;
        }

        /// <summary>Undoes Rust's <c>Debug</c> string escaping (<c>\"</c>, <c>\\</c>, <c>\n</c>, <c>\u{..}</c>...).</summary>
        public static string Unescape(string value)
        {
            if (value.IndexOf('\\') < 0)
            {
                return value;
            }

            var result = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c != '\\' || i + 1 >= value.Length)
                {
                    result.Append(c);
                    continue;
                }

                char e = value[++i];
                switch (e)
                {
                    case 'n': result.Append('\n'); break;
                    case 'r': result.Append('\r'); break;
                    case 't': result.Append('\t'); break;
                    case '0': result.Append('\0'); break;
                    case 'u':
                        int close = value.IndexOf('}', i);
                        if (i + 1 < value.Length && value[i + 1] == '{' && close > i
                            && int.TryParse(value.Substring(i + 2, close - i - 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int codePoint))
                        {
                            result.Append(char.ConvertFromUtf32(codePoint));
                            i = close;
                        }
                        else
                        {
                            result.Append(e);
                        }

                        break;
                    default: result.Append(e); break;
                }
            }

            return result.ToString();
        }

        /// <summary>UTF-8 byte offset to zero-based line / UTF-16 column.</summary>
        private sealed class PositionIndex
        {
            private readonly byte[] _source;
            private readonly List<int> _lineStarts = new List<int> { 0 };

            public PositionIndex(byte[] source)
            {
                _source = source;
                for (int i = 0; i < source.Length; i++)
                {
                    if (source[i] == (byte)'\n')
                    {
                        _lineStarts.Add(i + 1);
                    }
                }
            }

            public (int Line, int Column) ToLineColumn(int offset)
            {
                int line = _lineStarts.BinarySearch(offset);
                if (line < 0)
                {
                    line = ~line - 1;
                }

                int lineStart = _lineStarts[line];
                int column = Encoding.UTF8.GetCharCount(_source, lineStart, offset - lineStart);
                return (line, column);
            }
        }
    }
}
