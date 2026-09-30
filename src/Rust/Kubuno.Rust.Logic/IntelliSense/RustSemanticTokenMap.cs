using System;
using System.Collections.Generic;
using System.Linq;

namespace Kubuno.VisualStudio.Core.IntelliSense
{
    /// <summary>
    /// Maps rust-analyzer's semantic tokens onto the classifications Visual Studio colors C# with.
    /// <para>
    /// Visual Studio's LSP client turns a token type into a classification by name: a handful of standard LSP
    /// names get coarse classifications (<c>struct</c> → "type", <c>variable</c> → "identifier"...), while
    /// any Roslyn classification name (<c>struct name</c>, <c>local name</c>, <c>keyword - control</c>...) is
    /// used as is. Token modifiers are looked up directly by name in the classification registry, and an
    /// unknown one is dropped. So the legend rust-analyzer announces is replaced (<see cref="Legend"/>) by
    /// Roslyn names as token types, and by the Kubuno classifications (<see cref="Modifiers"/>) plus
    /// Roslyn's additive <c>static symbol</c> as modifiers; every token of every response is then remapped
    /// with <see cref="Remap(IList{int})"/> - a per-token rewrite of the (type, modifiers) pair, which keeps
    /// rust-analyzer's delta edits valid (they are aligned on whole tokens).
    /// </para>
    /// </summary>
    public sealed class RustSemanticTokenMap
    {
        /// <summary>Classification of a <c>mut</c> binding (underlined, like rust-analyzer's default in other editors).</summary>
        public const string Mutable = "Rust - mutable";

        /// <summary>Classification of an unsafe operation.</summary>
        public const string Unsafe = "Rust - unsafe";

        /// <summary>Classification of a macro name and its <c>!</c>.</summary>
        public const string Macro = "Rust - macro";

        /// <summary>Classification of a lifetime (<c>'a</c>).</summary>
        public const string Lifetime = "Rust - lifetime";

        /// <summary>Roslyn's additive classification for static members (associated functions, statics).</summary>
        public const string StaticSymbol = "static symbol";

        /// <summary>The modifiers announced to Visual Studio, in bit order.</summary>
        public static readonly IReadOnlyList<string> Modifiers = new[] { Mutable, Unsafe, Macro, Lifetime, StaticSymbol };

        private static readonly Dictionary<string, string> TypeNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["comment"] = "comment",
            ["decorator"] = "class name",
            ["enumMember"] = "enum member name",
            ["enum"] = "enum name",
            ["function"] = "method name",
            ["interface"] = "interface name",
            ["keyword"] = "keyword",
            ["macro"] = "text",
            ["method"] = "method name",
            ["namespace"] = "namespace name",
            ["number"] = "number",
            ["operator"] = "operator",
            ["parameter"] = "parameter name",
            ["property"] = "field name",
            ["string"] = "string",
            ["struct"] = "struct name",
            ["typeParameter"] = "type parameter name",
            ["variable"] = "local name",
            ["type"] = "class name",
            ["angle"] = "punctuation",
            ["arithmetic"] = "operator",
            ["attributeBracket"] = "punctuation",
            ["attribute"] = "class name",
            ["bitwise"] = "operator",
            ["boolean"] = "keyword",
            ["brace"] = "punctuation",
            ["bracket"] = "punctuation",
            ["builtinAttribute"] = "class name",
            ["builtinType"] = "keyword",
            ["character"] = "string",
            ["colon"] = "punctuation",
            ["comma"] = "punctuation",
            ["comparison"] = "operator",
            ["constParameter"] = "constant name",
            ["const"] = "constant name",
            ["deriveHelper"] = "class name",
            ["derive"] = "interface name",
            ["dot"] = "punctuation",
            ["escapeSequence"] = "string - escape character",
            ["formatSpecifier"] = "string - escape character",
            ["generic"] = "text",
            ["invalidEscapeSequence"] = "string - escape character",
            ["label"] = "label name",
            ["lifetime"] = "type parameter name",
            ["logical"] = "operator",
            ["macroBang"] = "text",
            ["negation"] = "operator",
            ["parenthesis"] = "punctuation",
            ["procMacro"] = "text",
            ["punctuation"] = "punctuation",
            ["selfKeyword"] = "keyword",
            ["selfTypeKeyword"] = "keyword",
            ["semicolon"] = "punctuation",
            ["static"] = "field name",
            ["toolModule"] = "namespace name",
            ["typeAlias"] = "class name",
            ["union"] = "struct name",
            ["unresolvedReference"] = "text",
        };

        private readonly int[] _baseType;
        private readonly int[] _typeModifierBits;
        private readonly int _controlFlowBit;
        private readonly int _documentationBit;
        private readonly int _mutableBit;
        private readonly int _unsafeBit;
        private readonly int _staticBit;
        private readonly int _keywordType;
        private readonly int _commentType;
        private readonly int _keywordControl;
        private readonly int _docComment;
        private readonly int[] _staticEligible;
        private readonly Dictionary<long, long> _cache = new Dictionary<long, long>();

        public RustSemanticTokenMap(IReadOnlyList<string> serverTypes, IReadOnlyList<string> serverModifiers)
        {
            ServerTypes = serverTypes ?? throw new ArgumentNullException(nameof(serverTypes));
            ServerModifiers = serverModifiers ?? throw new ArgumentNullException(nameof(serverModifiers));

            var legend = new List<string>();
            int Index(string name)
            {
                int i = legend.IndexOf(name);
                if (i < 0)
                {
                    legend.Add(name);
                    i = legend.Count - 1;
                }

                return i;
            }

            _baseType = serverTypes.Select(t => Index(TypeNames.TryGetValue(t, out var name) ? name : "text")).ToArray();
            _keywordControl = Index("keyword - control");
            _docComment = Index("xml doc comment - text");
            Index("text");
            Legend = legend;

            _typeModifierBits = serverTypes.Select(t =>
            {
                switch (t)
                {
                    case "macro":
                    case "macroBang":
                    case "procMacro":
                        return 1 << Modifiers.IndexOf(Macro);
                    case "lifetime":
                        return 1 << Modifiers.IndexOf(Lifetime);
                    default:
                        return 0;
                }
            }).ToArray();
            _staticEligible = serverTypes.Select(t => t is "function" or "method" or "static" or "const" ? 1 : 0).ToArray();

            _controlFlowBit = Bit(serverModifiers, "controlFlow");
            _documentationBit = Bit(serverModifiers, "documentation");
            _mutableBit = Bit(serverModifiers, "mutable");
            _unsafeBit = Bit(serverModifiers, "unsafe");
            _staticBit = Bit(serverModifiers, "static");
            _keywordType = IndexOf(serverTypes, "keyword");
            _commentType = IndexOf(serverTypes, "comment");
        }

        public IReadOnlyList<string> ServerTypes { get; }

        public IReadOnlyList<string> ServerModifiers { get; }

        /// <summary>The token types announced to Visual Studio instead of rust-analyzer's.</summary>
        public IReadOnlyList<string> Legend { get; }

        /// <summary>The new (type, modifiers) of a token rust-analyzer sent as (<paramref name="type"/>, <paramref name="modifiers"/>).</summary>
        public (int Type, int Modifiers) Map(int type, int modifiers)
        {
            long key = ((long)type << 32) | (uint)modifiers;
            lock (_cache)
            {
                if (_cache.TryGetValue(key, out var hit))
                {
                    return ((int)(hit >> 32), (int)(uint)hit);
                }
            }

            int newType;
            int newModifiers = 0;
            if (type < 0 || type >= _baseType.Length)
            {
                newType = IndexOfLegend("text");
            }
            else
            {
                newType = _baseType[type];
                newModifiers |= _typeModifierBits[type];
                if (type == _keywordType && Has(modifiers, _controlFlowBit))
                {
                    newType = _keywordControl;
                }
                else if (type == _commentType && Has(modifiers, _documentationBit))
                {
                    newType = _docComment;
                }

                if (Has(modifiers, _staticBit) && _staticEligible[type] == 1)
                {
                    newModifiers |= 1 << Modifiers.IndexOf(StaticSymbol);
                }
            }

            if (Has(modifiers, _mutableBit))
            {
                newModifiers |= 1 << Modifiers.IndexOf(Mutable);
            }

            if (Has(modifiers, _unsafeBit))
            {
                newModifiers |= 1 << Modifiers.IndexOf(Unsafe);
            }

            lock (_cache)
            {
                _cache[key] = ((long)newType << 32) | (uint)newModifiers;
            }

            return (newType, newModifiers);
        }

        /// <summary>Rewrites, in place, the (type, modifiers) of every token of a <c>data</c> array (5 integers per token).</summary>
        public void Remap(IList<int> data)
        {
            if (data is null)
            {
                return;
            }

            for (int i = 0; i + 4 < data.Count; i += 5)
            {
                var (type, modifiers) = Map(data[i + 3], data[i + 4]);
                data[i + 3] = type;
                data[i + 4] = modifiers;
            }
        }

        private int IndexOfLegend(string name)
        {
            for (int i = 0; i < Legend.Count; i++)
            {
                if (Legend[i] == name)
                {
                    return i;
                }
            }

            return 0;
        }

        private static bool Has(int modifiers, int bit) => bit >= 0 && (modifiers & (1 << bit)) != 0;

        private static int Bit(IReadOnlyList<string> names, string name) => IndexOf(names, name);

        private static int IndexOf(IReadOnlyList<string> names, string name)
        {
            for (int i = 0; i < names.Count; i++)
            {
                if (names[i] == name)
                {
                    return i;
                }
            }

            return -1;
        }
    }

    internal static class ReadOnlyListExtensions
    {
        public static int IndexOf(this IReadOnlyList<string> list, string value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == value)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
