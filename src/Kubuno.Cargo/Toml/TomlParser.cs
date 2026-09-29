using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Kubuno.Cargo.Toml
{
    /// <summary>
    /// Recursive-descent TOML parser that records the source span of every header, key and value so
    /// the document can be edited surgically. It is lenient in a few places (newlines and trailing
    /// commas inside inline tables, optional seconds in times) but rejects anything Cargo would.
    /// </summary>
    internal sealed class TomlParser
    {
        private const int MaxDepth = 100;

        private static readonly Regex DateTimeRx = new Regex(
            @"^([0-9]{4}-[0-9]{2}-[0-9]{2}([Tt ][0-9]{2}:[0-9]{2}(:[0-9]{2}(\.[0-9]+)?)?([Zz]|[+-][0-9]{2}:[0-9]{2})?)?|[0-9]{2}:[0-9]{2}(:[0-9]{2}(\.[0-9]+)?)?)$",
            RegexOptions.CultureInvariant);

        private static readonly Regex DateOnlyRx = new Regex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}$", RegexOptions.CultureInvariant);
        private static readonly Regex HexRx = new Regex(@"^0x[0-9A-Fa-f]+(_[0-9A-Fa-f]+)*$", RegexOptions.CultureInvariant);
        private static readonly Regex OctRx = new Regex(@"^0o[0-7]+(_[0-7]+)*$", RegexOptions.CultureInvariant);
        private static readonly Regex BinRx = new Regex(@"^0b[01]+(_[01]+)*$", RegexOptions.CultureInvariant);
        private static readonly Regex IntRx = new Regex(@"^[+-]?(0|[1-9](_?[0-9])*)$", RegexOptions.CultureInvariant);
        private static readonly Regex FloatRx = new Regex(@"^[+-]?(0|[1-9](_?[0-9])*)(\.[0-9](_?[0-9])*)?([eE][+-]?[0-9](_?[0-9])*)?$", RegexOptions.CultureInvariant);
        private static readonly Regex SpecialFloatRx = new Regex(@"^[+-]?(inf|nan)$", RegexOptions.CultureInvariant);

        private readonly string _s;
        private int _p;
        private int _depth;
        private int _nextId = 1;
        private HeaderNode? _cur;
        private readonly List<HeaderNode> _headers = new List<HeaderNode>();
        private readonly List<KvNode> _kvs = new List<KvNode>();
        private readonly List<HeaderNode> _arrayHeaders = new List<HeaderNode>();
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);

        private TomlParser(string s)
        {
            _s = s;
        }

        public static TomlModel Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var p = new TomlParser(text);
            p.Run();
            return new TomlModel(text, p._headers, p._kvs);
        }

        private void Run()
        {
            if (_s.Length > 0 && _s[0] == '﻿') _p = 1;
            while (_p < _s.Length)
            {
                int lineStart = _p;
                SkipWs();
                if (_p >= _s.Length) break;
                char c = _s[_p];
                if (c == '#' || c == '\n' || c == '\r')
                {
                    EndLine();
                    continue;
                }

                if (c == '[') ParseHeader(lineStart);
                else ParseKeyValue(lineStart);
            }
        }

        // ---------------------------------------------------------------- errors / low-level scanning

        private TomlParseException Error(string message, int pos)
        {
            if (pos > _s.Length) pos = _s.Length;
            int line = 1;
            int lineStart = 0;
            for (int i = 0; i < pos; i++)
            {
                if (_s[i] == '\n')
                {
                    line++;
                    lineStart = i + 1;
                }
            }

            int col = pos - lineStart + 1;
            if (line == 1 && _s.Length > 0 && _s[0] == '﻿' && pos > 0) col--;
            return new TomlParseException(message, line, col);
        }

        private void SkipWs()
        {
            while (_p < _s.Length && (_s[_p] == ' ' || _s[_p] == '\t')) _p++;
        }

        private bool StartsWithAt(string lit) => string.CompareOrdinal(_s, _p, lit, 0, lit.Length) == 0;

        private void SkipComment()
        {
            _p++;
            while (_p < _s.Length)
            {
                char c = _s[_p];
                if (c == '\n') break;
                if (c == '\r')
                {
                    if (_p + 1 < _s.Length && _s[_p + 1] == '\n') break;
                    throw Error("Bare carriage return is not allowed", _p);
                }

                if ((c < 0x20 && c != '\t') || c == 0x7f) throw Error("Control character in comment", _p);
                _p++;
            }
        }

        /// <summary>Consumes trailing whitespace, an optional comment and the line terminator; returns the offset after it.</summary>
        private int EndLine()
        {
            SkipWs();
            if (_p < _s.Length && _s[_p] == '#') SkipComment();
            if (_p >= _s.Length) return _p;
            if (_s[_p] == '\n')
            {
                _p++;
                return _p;
            }

            if (_s[_p] == '\r' && _p + 1 < _s.Length && _s[_p + 1] == '\n')
            {
                _p += 2;
                return _p;
            }

            throw Error("Unexpected character '" + _s[_p] + "'; expected end of line", _p);
        }

        /// <summary>Skips whitespace, line breaks and comments (inside arrays / inline tables).</summary>
        private void SkipBlank()
        {
            while (_p < _s.Length)
            {
                char c = _s[_p];
                if (c == ' ' || c == '\t' || c == '\n') _p++;
                else if (c == '\r')
                {
                    if (_p + 1 < _s.Length && _s[_p + 1] == '\n') _p += 2;
                    else throw Error("Bare carriage return is not allowed", _p);
                }
                else if (c == '#') SkipComment();
                else break;
            }
        }

        private void Expect(char c)
        {
            if (_p >= _s.Length || _s[_p] != c)
            {
                string found = _p >= _s.Length ? "end of file" : "'" + _s[_p] + "'";
                throw Error("Expected '" + c + "' but found " + found, _p);
            }

            _p++;
        }

        // ---------------------------------------------------------------- headers and key/values

        private void ParseHeader(int lineStart)
        {
            int start = _p;
            bool arr = _p + 1 < _s.Length && _s[_p + 1] == '[';
            _p += arr ? 2 : 1;
            var segs = new List<string>();
            var ends = new List<int>();
            ParseKey(segs, ends);
            SkipWs();
            Expect(']');
            if (arr) Expect(']');
            int end = _p;
            int lineEnd = EndLine();

            var h = new HeaderNode
            {
                Id = _nextId++,
                Path = segs.ToArray(),
                IsArray = arr,
                Start = start,
                End = end,
                LineStart = lineStart,
                LineEnd = lineEnd,
            };
            for (int i = _arrayHeaders.Count - 1; i >= 0; i--)
            {
                var a = _arrayHeaders[i];
                if (h.Path.Length > a.Path.Length && TomlPath.StartsWith(h.Path, a.Path))
                {
                    h.ArrayOwner = a;
                    break;
                }
            }

            if (arr && h.ArrayOwner == null) _arrayHeaders.Add(h);
            h.Scope = arr ? h.Id : (h.ArrayOwner != null ? h.ArrayOwner.Id : 0);
            if (!arr && !_seen.Add(h.Scope.ToString(CultureInfo.InvariantCulture) + "\u0001" + string.Join("\u0000", h.Path)))
            {
                throw Error("Duplicate table [" + TomlPath.Display(h.Path) + "]", start);
            }

            _headers.Add(h);
            _cur = h;
        }

        private void ParseKeyValue(int lineStart)
        {
            int keyStart = _p;
            var segs = new List<string>();
            var ends = new List<int>();
            ParseKey(segs, ends);
            int keyEnd = ends[ends.Count - 1];
            SkipWs();
            Expect('=');
            SkipWs();
            var value = ParseValue();
            int lineEnd = EndLine();

            var kv = new KvNode
            {
                KeyPath = segs.ToArray(),
                SegEnds = ends.ToArray(),
                KeyStart = keyStart,
                KeyEnd = keyEnd,
                Value = value,
                LineStart = lineStart,
                LineEnd = lineEnd,
                Owner = _cur,
            };
            kv.AbsPath = _cur == null ? kv.KeyPath : TomlPath.Concat(_cur.Path, kv.KeyPath);
            int scope = _cur == null ? 0 : _cur.Scope;
            if (!_seen.Add(scope.ToString(CultureInfo.InvariantCulture) + "\u0001" + string.Join("\u0000", kv.AbsPath)))
            {
                throw Error("Duplicate key '" + TomlPath.Display(kv.AbsPath) + "'", keyStart);
            }

            _kvs.Add(kv);
            _cur?.Kvs.Add(kv);
        }

        /// <summary>Parses a possibly dotted key; <paramref name="ends"/> receives the offset after each segment.</summary>
        private void ParseKey(List<string> segs, List<int> ends)
        {
            while (true)
            {
                SkipWs();
                segs.Add(ParseKeySegment());
                ends.Add(_p);
                SkipWs();
                if (_p < _s.Length && _s[_p] == '.')
                {
                    _p++;
                    continue;
                }

                break;
            }
        }

        private string ParseKeySegment()
        {
            if (_p >= _s.Length) throw Error("Expected a key", _p);
            char c = _s[_p];
            if (c == '"')
            {
                if (StartsWithAt("\"\"\"")) throw Error("Multi-line strings cannot be used as keys", _p);
                return ParseBasicString();
            }

            if (c == '\'')
            {
                if (StartsWithAt("'''")) throw Error("Multi-line strings cannot be used as keys", _p);
                return ParseLiteralString();
            }

            int start = _p;
            while (_p < _s.Length && IsBareKeyChar(_s[_p])) _p++;
            if (_p == start) throw Error("Expected a key but found '" + c + "'", _p);
            return _s.Substring(start, _p - start);
        }

        private static bool IsBareKeyChar(char c) =>
            (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-';

        // ---------------------------------------------------------------- values

        private ValueNode ParseValue()
        {
            if (_p >= _s.Length) throw Error("Expected a value", _p);
            if (++_depth > MaxDepth) throw Error("Value nesting is too deep", _p);
            try
            {
                char c = _s[_p];
                int start = _p;
                switch (c)
                {
                    case '"':
                        {
                            string v = StartsWithAt("\"\"\"") ? ParseMultiBasicString() : ParseBasicString();
                            return new ValueNode(TomlValue.CreateString(v, _s.Substring(start, _p - start)), start, _p);
                        }

                    case '\'':
                        {
                            string v = StartsWithAt("'''") ? ParseMultiLiteralString() : ParseLiteralString();
                            return new ValueNode(TomlValue.CreateString(v, _s.Substring(start, _p - start)), start, _p);
                        }

                    case '[':
                        return ParseArray();
                    case '{':
                        return ParseInlineTable();
                    default:
                        return ParseScalar();
                }
            }
            finally
            {
                _depth--;
            }
        }

        private static bool IsValueDelimiter(char c) =>
            c == ' ' || c == '\t' || c == ',' || c == ']' || c == '}' || c == '#' || c == '\n' || c == '\r';

        private ValueNode ParseScalar()
        {
            int start = _p;
            while (_p < _s.Length && !IsValueDelimiter(_s[_p])) _p++;
            string token = _s.Substring(start, _p - start);
            if (token.Length == 0) throw Error("Expected a value", start);

            // A date and a time may be separated by a single space.
            if (DateOnlyRx.IsMatch(token) && _p + 3 < _s.Length && _s[_p] == ' ' && char.IsDigit(_s[_p + 1]) && char.IsDigit(_s[_p + 2]) && _s[_p + 3] == ':')
            {
                _p++;
                while (_p < _s.Length && !IsValueDelimiter(_s[_p])) _p++;
                token = _s.Substring(start, _p - start);
            }

            if (token == "true") return new ValueNode(TomlValue.CreateBoolean(true, token), start, _p);
            if (token == "false") return new ValueNode(TomlValue.CreateBoolean(false, token), start, _p);
            if (DateTimeRx.IsMatch(token)) return new ValueNode(TomlValue.CreateDateTime(token), start, _p);
            if (SpecialFloatRx.IsMatch(token))
            {
                double d = token.EndsWith("nan", StringComparison.Ordinal) ? double.NaN : (token[0] == '-' ? double.NegativeInfinity : double.PositiveInfinity);
                return new ValueNode(TomlValue.CreateFloat(d, token), start, _p);
            }

            if (HexRx.IsMatch(token))
            {
                if (!ulong.TryParse(token.Substring(2).Replace("_", string.Empty), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong u) || u > long.MaxValue)
                {
                    throw Error("Integer is out of range", start);
                }

                return new ValueNode(TomlValue.CreateInteger((long)u, token), start, _p);
            }

            if (OctRx.IsMatch(token)) return new ValueNode(TomlValue.CreateInteger(ParseRadix(token, 8, start), token), start, _p);
            if (BinRx.IsMatch(token)) return new ValueNode(TomlValue.CreateInteger(ParseRadix(token, 2, start), token), start, _p);
            if (IntRx.IsMatch(token))
            {
                if (!long.TryParse(token.Replace("_", string.Empty), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long l))
                {
                    throw Error("Integer is out of range", start);
                }

                return new ValueNode(TomlValue.CreateInteger(l, token), start, _p);
            }

            if (FloatRx.IsMatch(token))
            {
                if (!double.TryParse(token.Replace("_", string.Empty), NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                {
                    throw Error("Invalid float", start);
                }

                return new ValueNode(TomlValue.CreateFloat(d, token), start, _p);
            }

            throw Error("Invalid value '" + token + "'", start);
        }

        private long ParseRadix(string token, int radix, int errorPos)
        {
            ulong acc = 0;
            foreach (char c in token.Substring(2))
            {
                if (c == '_') continue;
                ulong digit = (ulong)(c - '0');
                if (acc > (ulong)long.MaxValue / (ulong)radix) throw Error("Integer is out of range", errorPos);
                acc = (acc * (ulong)radix) + digit;
                if (acc > long.MaxValue) throw Error("Integer is out of range", errorPos);
            }

            return (long)acc;
        }

        private ValueNode ParseArray()
        {
            int start = _p;
            _p++;
            var items = new List<ValueNode>();
            bool lastWasComma = false;
            bool trailing = false;
            while (true)
            {
                SkipBlank();
                if (_p >= _s.Length) throw Error("Unterminated array", start);
                if (_s[_p] == ']')
                {
                    _p++;
                    trailing = lastWasComma;
                    break;
                }

                items.Add(ParseValue());
                lastWasComma = false;
                SkipBlank();
                if (_p >= _s.Length) throw Error("Unterminated array", start);
                if (_s[_p] == ',')
                {
                    _p++;
                    lastWasComma = true;
                    continue;
                }

                if (_s[_p] == ']')
                {
                    _p++;
                    break;
                }

                throw Error("Expected ',' or ']' in array but found '" + _s[_p] + "'", _p);
            }

            var values = new List<TomlValue>(items.Count);
            foreach (var i in items) values.Add(i.Value);
            var node = new ValueNode(TomlValue.CreateArray(values, _s.Substring(start, _p - start)), start, _p)
            {
                Items = items,
                TrailingComma = trailing,
            };
            return node;
        }

        private ValueNode ParseInlineTable()
        {
            int start = _p;
            _p++;
            var entries = new List<InlineEntry>();
            while (true)
            {
                SkipBlank();
                if (_p >= _s.Length) throw Error("Unterminated inline table", start);
                if (_s[_p] == '}')
                {
                    _p++;
                    break;
                }

                int keyStart = _p;
                var segs = new List<string>();
                var ends = new List<int>();
                ParseKey(segs, ends);
                int keyEnd = ends[ends.Count - 1];
                SkipWs();
                Expect('=');
                SkipWs();
                var v = ParseValue();
                entries.Add(new InlineEntry { KeyPath = segs.ToArray(), KeyStart = keyStart, KeyEnd = keyEnd, Value = v });
                SkipBlank();
                if (_p >= _s.Length) throw Error("Unterminated inline table", start);
                if (_s[_p] == ',')
                {
                    _p++;
                    continue;
                }

                if (_s[_p] == '}')
                {
                    _p++;
                    break;
                }

                throw Error("Expected ',' or '}' in inline table but found '" + _s[_p] + "'", _p);
            }

            var pairs = new List<KeyValuePair<string[], TomlValue>>();
            foreach (var e in entries) pairs.Add(new KeyValuePair<string[], TomlValue>(e.KeyPath, e.Value.Value));
            string? err;
            var table = TomlTables.Build(pairs, 0, _s.Substring(start, _p - start), out err);
            if (table == null) throw Error(err ?? "Invalid inline table", start);
            return new ValueNode(table, start, _p) { Entries = entries };
        }

        // ---------------------------------------------------------------- strings

        private string ParseBasicString()
        {
            int start = _p;
            _p++;
            var sb = new StringBuilder();
            while (true)
            {
                if (_p >= _s.Length) throw Error("Unterminated string", start);
                char c = _s[_p];
                if (c == '"')
                {
                    _p++;
                    return sb.ToString();
                }

                if (c == '\n' || c == '\r') throw Error("Unterminated string", start);
                if (c == '\\')
                {
                    ParseEscape(sb);
                    continue;
                }

                if ((c < 0x20 && c != '\t') || c == 0x7f) throw Error("Control character in string", _p);
                sb.Append(c);
                _p++;
            }
        }

        private string ParseLiteralString()
        {
            int start = _p;
            _p++;
            int contentStart = _p;
            while (true)
            {
                if (_p >= _s.Length) throw Error("Unterminated string", start);
                char c = _s[_p];
                if (c == '\'')
                {
                    string r = _s.Substring(contentStart, _p - contentStart);
                    _p++;
                    return r;
                }

                if (c == '\n' || c == '\r') throw Error("Unterminated string", start);
                if ((c < 0x20 && c != '\t') || c == 0x7f) throw Error("Control character in string", _p);
                _p++;
            }
        }

        private string ParseMultiBasicString()
        {
            int start = _p;
            _p += 3;
            SkipFirstNewline();
            var sb = new StringBuilder();
            while (true)
            {
                if (_p >= _s.Length) throw Error("Unterminated multi-line string", start);
                char c = _s[_p];
                if (c == '"')
                {
                    int q = 0;
                    while (_p + q < _s.Length && _s[_p + q] == '"') q++;
                    if (q >= 3)
                    {
                        if (q > 5) throw Error("Too many quotes in multi-line string", _p);
                        sb.Append('"', q - 3);
                        _p += q;
                        return sb.ToString();
                    }

                    sb.Append('"', q);
                    _p += q;
                    continue;
                }

                if (c == '\\')
                {
                    // Line-ending backslash: trim the newline and all following whitespace.
                    int k = _p + 1;
                    while (k < _s.Length && (_s[k] == ' ' || _s[k] == '\t')) k++;
                    if (k < _s.Length && (_s[k] == '\n' || (_s[k] == '\r' && k + 1 < _s.Length && _s[k + 1] == '\n')))
                    {
                        _p = k;
                        while (_p < _s.Length && (_s[_p] == ' ' || _s[_p] == '\t' || _s[_p] == '\n' || (_s[_p] == '\r' && _p + 1 < _s.Length && _s[_p + 1] == '\n')))
                        {
                            _p++;
                        }

                        continue;
                    }

                    ParseEscape(sb);
                    continue;
                }

                AppendMultiLineChar(sb, c);
            }
        }

        private string ParseMultiLiteralString()
        {
            int start = _p;
            _p += 3;
            SkipFirstNewline();
            var sb = new StringBuilder();
            while (true)
            {
                if (_p >= _s.Length) throw Error("Unterminated multi-line string", start);
                char c = _s[_p];
                if (c == '\'')
                {
                    int q = 0;
                    while (_p + q < _s.Length && _s[_p + q] == '\'') q++;
                    if (q >= 3)
                    {
                        if (q > 5) throw Error("Too many quotes in multi-line string", _p);
                        sb.Append('\'', q - 3);
                        _p += q;
                        return sb.ToString();
                    }

                    sb.Append('\'', q);
                    _p += q;
                    continue;
                }

                AppendMultiLineChar(sb, c);
            }
        }

        private void SkipFirstNewline()
        {
            if (_p < _s.Length && _s[_p] == '\n') _p++;
            else if (_p + 1 < _s.Length && _s[_p] == '\r' && _s[_p + 1] == '\n') _p += 2;
        }

        private void AppendMultiLineChar(StringBuilder sb, char c)
        {
            if (c == '\r')
            {
                if (_p + 1 < _s.Length && _s[_p + 1] == '\n')
                {
                    sb.Append('\n');
                    _p += 2;
                    return;
                }

                throw Error("Bare carriage return is not allowed", _p);
            }

            if ((c < 0x20 && c != '\t' && c != '\n') || c == 0x7f) throw Error("Control character in string", _p);
            sb.Append(c);
            _p++;
        }

        private void ParseEscape(StringBuilder sb)
        {
            int start = _p;
            _p++;
            if (_p >= _s.Length) throw Error("Unterminated escape sequence", start);
            char c = _s[_p++];
            switch (c)
            {
                case 'b': sb.Append('\b'); break;
                case 't': sb.Append('\t'); break;
                case 'n': sb.Append('\n'); break;
                case 'f': sb.Append('\f'); break;
                case 'r': sb.Append('\r'); break;
                case 'e': sb.Append('\u001b'); break;
                case '"': sb.Append('"'); break;
                case '\\': sb.Append('\\'); break;
                case 'x': AppendCodePoint(sb, ReadHex(2, start), start); break;
                case 'u': AppendCodePoint(sb, ReadHex(4, start), start); break;
                case 'U': AppendCodePoint(sb, ReadHex(8, start), start); break;
                default: throw Error("Invalid escape sequence '\\" + c + "'", start);
            }
        }

        private long ReadHex(int digits, int errorPos)
        {
            if (_p + digits > _s.Length) throw Error("Incomplete escape sequence", errorPos);
            long v = 0;
            for (int i = 0; i < digits; i++)
            {
                char h = _s[_p + i];
                int d;
                if (h >= '0' && h <= '9') d = h - '0';
                else if (h >= 'a' && h <= 'f') d = h - 'a' + 10;
                else if (h >= 'A' && h <= 'F') d = h - 'A' + 10;
                else throw Error("Invalid hexadecimal digit in escape sequence", errorPos);
                v = (v * 16) + d;
            }

            _p += digits;
            return v;
        }

        private void AppendCodePoint(StringBuilder sb, long cp, int errorPos)
        {
            if (cp > 0x10FFFF || (cp >= 0xD800 && cp <= 0xDFFF)) throw Error("Invalid Unicode scalar value in escape sequence", errorPos);
            sb.Append(char.ConvertFromUtf32((int)cp));
        }
    }

    internal static class TomlTables
    {
        /// <summary>Builds a table from (possibly dotted) key paths, grouping siblings; returns null and an error on conflicts.</summary>
        public static TomlValue? Build(List<KeyValuePair<string[], TomlValue>> entries, int level, string? raw, out string? error)
        {
            error = null;
            var order = new List<string>();
            var groups = new Dictionary<string, List<KeyValuePair<string[], TomlValue>>>(StringComparer.Ordinal);
            foreach (var e in entries)
            {
                string k = e.Key[level];
                if (!groups.TryGetValue(k, out var g))
                {
                    g = new List<KeyValuePair<string[], TomlValue>>();
                    groups[k] = g;
                    order.Add(k);
                }

                g.Add(e);
            }

            var pairs = new List<KeyValuePair<string, TomlValue>>();
            foreach (var k in order)
            {
                var g = groups[k];
                if (g.Count == 1 && g[0].Key.Length == level + 1)
                {
                    pairs.Add(new KeyValuePair<string, TomlValue>(k, g[0].Value));
                    continue;
                }

                foreach (var e in g)
                {
                    if (e.Key.Length == level + 1)
                    {
                        error = "Duplicate or conflicting key '" + k + "'";
                        return null;
                    }
                }

                var sub = Build(g, level + 1, null, out error);
                if (sub == null) return null;
                pairs.Add(new KeyValuePair<string, TomlValue>(k, sub));
            }

            return TomlValue.CreateTable(pairs, raw);
        }
    }
}
