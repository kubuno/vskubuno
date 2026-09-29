using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Kubuno.Cargo.Toml
{
    /// <summary>
    /// Computes the new text for a surgical edit of one parsed document. Every method returns the whole
    /// new text (or <c>null</c> when there is nothing to do); the caller diffs it into a minimal edit.
    /// </summary>
    internal sealed class TomlEditor
    {
        private readonly TomlModel _m;
        private readonly string _t;
        private readonly string _nl;
        private readonly int _bom;

        public TomlEditor(TomlModel model)
        {
            _m = model;
            _t = model.Text;
            int i = _t.IndexOf('\n');
            _nl = i > 0 && _t[i - 1] == '\r' ? "\r\n" : "\n";
            _bom = _t.Length > 0 && _t[0] == '﻿' ? 1 : 0;
        }

        // ================================================================ SET

        public string Set(TomlValue value, string[] path)
        {
            foreach (var h in _m.Headers)
            {
                if (!h.InArray && h.IsArray && TomlPath.StartsWith(path, h.Path))
                {
                    throw new NotSupportedException("Editing an array of tables ([[" + TomlPath.Display(h.Path) + "]]) is not supported.");
                }
            }

            // 1. The key already exists as "key = value": replace only the value span.
            foreach (var kv in _m.Kvs)
            {
                if (!kv.InArray && TomlPath.Eq(kv.AbsPath, path))
                {
                    return Replace(kv.Value.Start, kv.Value.End, FormatReplacement(kv.Value, value));
                }
            }

            // 2. The key lives (or must live) inside an inline table.
            foreach (var kv in _m.Kvs)
            {
                if (!kv.InArray && kv.AbsPath.Length < path.Length && TomlPath.StartsWith(path, kv.AbsPath))
                {
                    if (kv.Value.Entries == null)
                    {
                        throw new InvalidOperationException("'" + TomlPath.Display(kv.AbsPath) + "' is not a table; cannot set '" + TomlPath.Display(path) + "'.");
                    }

                    return SetInInline(kv.Value, TomlPath.Slice(path, kv.AbsPath.Length), value);
                }
            }

            // 3. The path currently holds a dotted-key subtree and/or [header] tables.
            var dotted = _m.Kvs
                .Where(k => !k.InArray && k.AbsPath.Length > path.Length && TomlPath.StartsWith(k.AbsPath, path)
                            && (k.Owner == null || k.Owner.Path.Length < path.Length))
                .ToList();
            bool hasHeaders = _m.Headers.Any(h => TomlPath.StartsWith(h.Path, path));
            if (dotted.Count > 0)
            {
                string t1 = ReplaceDottedSubtree(dotted, path, value);
                if (hasHeaders) t1 = new TomlEditor(TomlParser.Parse(t1)).RemoveHeaderBlocks(path) ?? t1;
                return t1;
            }

            if (hasHeaders)
            {
                string t1 = RemoveHeaderBlocks(path) ?? _t;
                return new TomlEditor(TomlParser.Parse(t1)).InsertMissing(value, path);
            }

            // 4. The key is missing.
            return InsertMissing(value, path);
        }

        private string ReplaceDottedSubtree(List<KvNode> dotted, string[] path, TomlValue value)
        {
            var first = dotted[0];
            int ownerLen = first.Owner == null ? 0 : first.Owner.Path.Length;
            int k = path.Length - ownerLen;
            int keepEnd = first.SegEnds[k - 1];
            string gap = _t.Substring(first.KeyEnd, first.Value.Start - first.KeyEnd);
            var edits = new List<Splice> { new Splice(keepEnd, first.Value.End, gap + TomlValueFormatter.Format(value)) };
            for (int i = 1; i < dotted.Count; i++) edits.Add(new Splice(dotted[i].LineStart, dotted[i].LineEnd, string.Empty));
            return ApplySplices(edits);
        }

        private string SetInInline(ValueNode table, string[] rest, TomlValue value)
        {
            var entries = table.Entries!;
            foreach (var e in entries)
            {
                if (TomlPath.Eq(e.KeyPath, rest)) return Replace(e.Value.Start, e.Value.End, FormatReplacement(e.Value, value));
            }

            foreach (var e in entries)
            {
                if (e.KeyPath.Length < rest.Length && TomlPath.StartsWith(rest, e.KeyPath))
                {
                    if (e.Value.Entries == null)
                    {
                        throw new InvalidOperationException("'" + TomlPath.Display(e.KeyPath) + "' is not a table.");
                    }

                    return SetInInline(e.Value, TomlPath.Slice(rest, e.KeyPath.Length), value);
                }
            }

            foreach (var e in entries)
            {
                if (e.KeyPath.Length > rest.Length && TomlPath.StartsWith(e.KeyPath, rest))
                {
                    throw new NotSupportedException("Replacing a dotted-key subtree inside an inline table is not supported.");
                }
            }

            string keyText = string.Join(".", rest.Select(TomlKey.Format));
            string valueText = TomlValueFormatter.Format(value);
            if (entries.Count == 0)
            {
                return Replace(table.Start + 1, table.End - 1, " " + keyText + " = " + valueText + " ");
            }

            var last = entries[entries.Count - 1];
            string between = _t.Substring(last.KeyEnd, last.Value.Start - last.KeyEnd);
            string eq = between.Trim() == "=" && between.IndexOf(' ') < 0 && between.IndexOf('\t') < 0 ? "=" : " = ";
            return _t.Substring(0, last.Value.End) + ", " + keyText + eq + valueText + _t.Substring(last.Value.End);
        }

        private string InsertMissing(TomlValue value, string[] path)
        {
            int n = path.Length;
            string keyLine = TomlKey.Format(path[n - 1]) + " = " + TomlValueFormatter.Format(value);
            if (n == 1) return InsertRoot(keyLine);

            var parent = TomlPath.Slice(path, 0, n - 1);

            // A [parent] header exists: append to its key/value lines.
            var hdr = _m.Headers.FirstOrDefault(h => !h.InArray && !h.IsArray && TomlPath.Eq(h.Path, parent));
            if (hdr != null)
            {
                if (hdr.Kvs.Count > 0)
                {
                    var last = hdr.Kvs[hdr.Kvs.Count - 1];
                    return InsertLineAt(last.LineEnd, IndentOf(last) + keyLine);
                }

                return InsertLineAt(hdr.LineEnd, _t.Substring(hdr.LineStart, hdr.Start - hdr.LineStart) + keyLine);
            }

            // The parent is built from dotted keys: add a sibling dotted key next to the last one.
            KvNode? sibling = null;
            foreach (var kv in _m.Kvs)
            {
                if (kv.InArray || kv.AbsPath.Length <= parent.Length || !TomlPath.StartsWith(kv.AbsPath, parent)) continue;
                int ownerLen = kv.Owner == null ? 0 : kv.Owner.Path.Length;
                if (ownerLen <= parent.Length) sibling = kv;
            }

            if (sibling != null)
            {
                int ownerLen = sibling.Owner == null ? 0 : sibling.Owner.Path.Length;
                string rel = string.Join(".", TomlPath.Slice(path, ownerLen).Select(TomlKey.Format));
                return InsertLineAt(sibling.LineEnd, IndentOf(sibling) + rel + " = " + TomlValueFormatter.Format(value));
            }

            return InsertTableBlock(parent, keyLine);
        }

        private string InsertRoot(string keyLine)
        {
            KvNode? last = null;
            foreach (var kv in _m.Kvs)
            {
                if (kv.Owner == null) last = kv;
            }

            if (last != null) return InsertLineAt(last.LineEnd, IndentOf(last) + keyLine);
            if (_m.Headers.Count > 0)
            {
                int pos = _m.Headers[0].LineStart;
                return _t.Substring(0, pos) + keyLine + _nl + _nl + _t.Substring(pos);
            }

            return InsertLineAt(_t.Length, keyLine);
        }

        private string InsertTableBlock(string[] table, string keyLine)
        {
            string header = "[" + string.Join(".", table.Select(TomlKey.Format)) + "]";
            HeaderNode? best = null;
            int bestLen = 0;
            foreach (var h in _m.Headers)
            {
                if (h.InArray) continue;
                int len = TomlPath.CommonPrefix(h.Path, table);
                if (len > 0 && len >= bestLen)
                {
                    best = h;
                    bestLen = len;
                }
            }

            int pos = best == null ? _t.Length : (best.Kvs.Count > 0 ? best.Kvs[best.Kvs.Count - 1].LineEnd : best.LineEnd);
            bool endsWithNl = pos == 0 || _t[pos - 1] == '\n';
            var sb = new StringBuilder();
            if (pos > 0)
            {
                if (!endsWithNl) sb.Append(_nl).Append(_nl);
                else if (!IsBlankLine(LineStartOf(pos - 1))) sb.Append(_nl);
            }

            sb.Append(header).Append(_nl).Append(keyLine);
            bool eof = pos >= _t.Length;
            if (!eof)
            {
                sb.Append(_nl);
                if (!IsBlankLine(pos)) sb.Append(_nl);
            }
            else if (endsWithNl)
            {
                sb.Append(_nl);
            }

            return _t.Substring(0, pos) + sb + _t.Substring(pos);
        }

        /// <summary>Inserts <paramref name="line"/> as a new line at <paramref name="pos"/> (a line boundary).</summary>
        private string InsertLineAt(int pos, string line)
        {
            if (pos >= _t.Length && pos > 0 && _t[pos - 1] != '\n') return _t + _nl + line;
            return _t.Substring(0, pos) + line + _nl + _t.Substring(pos);
        }

        private string IndentOf(KvNode kv) => _t.Substring(kv.LineStart, kv.KeyStart - kv.LineStart);

        // ---------------------------------------------------------------- value formatting

        private string FormatReplacement(ValueNode old, TomlValue v)
        {
            if (v.Kind == TomlValueKind.String && old.Value.Kind == TomlValueKind.String)
            {
                string? raw = old.Value.Raw;
                string s = v.AsString() ?? string.Empty;
                if (raw != null && raw.Length >= 2 && raw[0] == '\'' && !raw.StartsWith("'''", StringComparison.Ordinal) && CanBeLiteral(s))
                {
                    return "'" + s + "'";
                }
            }

            if (v.Kind == TomlValueKind.Array && old.Items != null && _t.IndexOf('\n', old.Start, old.End - old.Start) >= 0)
            {
                var items = v.AsArray()!;
                if (items.Count > 0) return FormatMultiLineArray(old, items);
            }

            return TomlValueFormatter.Format(v);
        }

        private static bool CanBeLiteral(string s)
        {
            foreach (var c in s)
            {
                if (c == '\'' || char.IsControl(c)) return false;
            }

            return true;
        }

        private string FormatMultiLineArray(ValueNode old, IReadOnlyList<TomlValue> items)
        {
            string? closeIndent = LeadingWhitespaceIfFirst(old.End - 1);
            closeIndent ??= LineIndent(old.Start);
            string? itemIndent = old.Items!.Count > 0 ? LeadingWhitespaceIfFirst(old.Items[0].Start) : null;
            itemIndent ??= closeIndent + "    ";
            var sb = new StringBuilder();
            sb.Append('[').Append(_nl);
            for (int i = 0; i < items.Count; i++)
            {
                sb.Append(itemIndent).Append(TomlValueFormatter.Format(items[i]));
                if (i < items.Count - 1 || old.TrailingComma) sb.Append(',');
                sb.Append(_nl);
            }

            sb.Append(closeIndent).Append(']');
            return sb.ToString();
        }

        /// <summary>The indentation of the line containing <paramref name="pos"/> when only whitespace precedes it, else <c>null</c>.</summary>
        private string? LeadingWhitespaceIfFirst(int pos)
        {
            int ls = Math.Max(LineStartOf(pos), _bom);
            for (int i = ls; i < pos; i++)
            {
                if (_t[i] != ' ' && _t[i] != '\t') return null;
            }

            return _t.Substring(ls, pos - ls);
        }

        private string LineIndent(int pos)
        {
            int ls = Math.Max(LineStartOf(pos), _bom);
            int i = ls;
            while (i < _t.Length && (_t[i] == ' ' || _t[i] == '\t')) i++;
            return _t.Substring(ls, i - ls);
        }

        // ================================================================ REMOVE

        public string? Remove(string[] path)
        {
            var blocks = new List<int[]>();
            var others = new List<int[]>();
            var owners = new List<string[]>();

            foreach (var h in _m.Headers)
            {
                if (TomlPath.StartsWith(h.Path, path)) blocks.Add(BlockRange(h, true));
            }

            foreach (var kv in _m.Kvs)
            {
                if (kv.InArray || kv.AbsPath.Length < path.Length || !TomlPath.StartsWith(kv.AbsPath, path)) continue;
                if (kv.Owner != null && kv.Owner.Path.Length >= path.Length) continue; // removed with its header block
                others.Add(new[] { kv.LineStart, kv.LineEnd });
                if (kv.Owner != null) owners.Add(kv.Owner.Path);
            }

            if (blocks.Count == 0 && others.Count == 0)
            {
                foreach (var kv in _m.Kvs)
                {
                    if (kv.InArray || kv.AbsPath.Length >= path.Length || !TomlPath.StartsWith(path, kv.AbsPath) || kv.Value.Entries == null) continue;
                    var r = InlineRemovalRange(kv.Value, TomlPath.Slice(path, kv.AbsPath.Length));
                    if (r != null) return _t.Substring(0, r[0]) + _t.Substring(r[1]);
                }

                return null;
            }

            var merged = ExtendAtEof(Merge(blocks));
            merged.AddRange(others);
            string text = ApplyRanges(Merge(merged));

            var done = new HashSet<string>(StringComparer.Ordinal);
            foreach (var o in owners)
            {
                if (!done.Add(string.Join("\u0000", o))) continue;
                var pruned = new TomlEditor(TomlParser.Parse(text)).TryPrune(o);
                if (pruned != null) text = pruned;
            }

            return text;
        }

        /// <summary>Removes every [header] block whose path starts with <paramref name="path"/>; null when there is none.</summary>
        public string? RemoveHeaderBlocks(string[] path)
        {
            var blocks = new List<int[]>();
            foreach (var h in _m.Headers)
            {
                if (TomlPath.StartsWith(h.Path, path)) blocks.Add(BlockRange(h, true));
            }

            if (blocks.Count == 0) return null;
            return ApplyRanges(ExtendAtEof(Merge(blocks)));
        }

        private string? TryPrune(string[] path)
        {
            var h = _m.Headers.FirstOrDefault(x => !x.InArray && !x.IsArray && TomlPath.Eq(x.Path, path));
            if (h == null || h.Kvs.Count > 0) return null;
            if (_m.Headers.Any(o => o != h && o.Path.Length > h.Path.Length && TomlPath.StartsWith(o.Path, h.Path))) return null;

            int idx = _m.Headers.IndexOf(h);
            int bodyEnd = idx + 1 < _m.Headers.Count ? AttachedStart(_m.Headers[idx + 1]) : _t.Length;
            for (int ls = h.LineEnd; ls < bodyEnd; ls = NextLineStart(ls))
            {
                if (IsCommentLine(ls)) return null;
            }

            // Comments directly above the header describe it: keep the header rather than orphan them.
            if (AttachedStart(h) != h.LineStart) return null;

            return ApplyRanges(ExtendAtEof(Merge(new List<int[]> { BlockRange(h, false) })));
        }

        private int[]? InlineRemovalRange(ValueNode table, string[] rest)
        {
            var entries = table.Entries!;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (TomlPath.Eq(e.KeyPath, rest))
                {
                    if (entries.Count == 1) return new[] { table.Start + 1, table.End - 1 };
                    if (i < entries.Count - 1) return new[] { e.KeyStart, entries[i + 1].KeyStart };
                    return new[] { entries[i - 1].Value.End, e.Value.End };
                }

                if (e.KeyPath.Length < rest.Length && TomlPath.StartsWith(rest, e.KeyPath) && e.Value.Entries != null)
                {
                    var r = InlineRemovalRange(e.Value, TomlPath.Slice(rest, e.KeyPath.Length));
                    if (r != null) return r;
                }
            }

            return null;
        }

        // ---------------------------------------------------------------- block / range helpers

        /// <summary>The header line through the line before the next header, optionally taking the comment lines directly above the header.</summary>
        private int[] BlockRange(HeaderNode h, bool includeAttachedAbove)
        {
            int start = includeAttachedAbove ? AttachedStart(h) : h.LineStart;
            int idx = _m.Headers.IndexOf(h);
            int end = idx + 1 < _m.Headers.Count ? AttachedStart(_m.Headers[idx + 1]) : _t.Length;
            return new[] { start, end };
        }

        /// <summary>Start of the comment lines directly above a header (no blank line in between), else the header line start.</summary>
        private int AttachedStart(HeaderNode h)
        {
            int s = h.LineStart;
            while (s > _bom)
            {
                int prev = Math.Max(LineStartOf(s - 1), _bom);
                if (IsCommentLine(prev)) s = prev;
                else break;
            }

            return s;
        }

        private static List<int[]> Merge(List<int[]> ranges)
        {
            var sorted = ranges.OrderBy(r => r[0]).ToList();
            var result = new List<int[]>();
            foreach (var r in sorted)
            {
                if (result.Count > 0 && r[0] <= result[result.Count - 1][1])
                {
                    var last = result[result.Count - 1];
                    last[1] = Math.Max(last[1], r[1]);
                }
                else
                {
                    result.Add(new[] { r[0], r[1] });
                }
            }

            return result;
        }

        /// <summary>A block removed up to the end of the file also takes the one blank line separating it from the previous content.</summary>
        private List<int[]> ExtendAtEof(List<int[]> merged)
        {
            if (merged.Count == 0) return merged;
            var last = merged[merged.Count - 1];
            if (last[1] >= _t.Length && last[0] > _bom)
            {
                int prev = LineStartOf(last[0] - 1);
                if (prev >= _bom && IsBlankLine(prev)) last[0] = prev;
            }

            return merged;
        }

        private string ApplyRanges(List<int[]> ranges)
        {
            var sb = new StringBuilder();
            int pos = 0;
            foreach (var r in ranges.OrderBy(x => x[0]))
            {
                sb.Append(_t, pos, r[0] - pos);
                pos = r[1];
            }

            sb.Append(_t, pos, _t.Length - pos);
            return sb.ToString();
        }

        private readonly struct Splice
        {
            public Splice(int start, int end, string text)
            {
                Start = start;
                End = end;
                Text = text;
            }

            public int Start { get; }

            public int End { get; }

            public string Text { get; }
        }

        private string ApplySplices(List<Splice> splices)
        {
            var sb = new StringBuilder();
            int pos = 0;
            foreach (var s in splices.OrderBy(x => x.Start))
            {
                sb.Append(_t, pos, s.Start - pos).Append(s.Text);
                pos = s.End;
            }

            sb.Append(_t, pos, _t.Length - pos);
            return sb.ToString();
        }

        private string Replace(int start, int end, string text) => _t.Substring(0, start) + text + _t.Substring(end);

        // ---------------------------------------------------------------- line helpers

        /// <summary>Start of the line containing the character at <paramref name="i"/>.</summary>
        private int LineStartOf(int i) => i <= 0 ? 0 : _t.LastIndexOf('\n', i - 1) + 1;

        private int NextLineStart(int ls)
        {
            int i = _t.IndexOf('\n', ls);
            return i < 0 ? _t.Length : i + 1;
        }

        private int FirstNonWs(int ls)
        {
            int i = ls;
            if (i < _bom) i = _bom;
            while (i < _t.Length && (_t[i] == ' ' || _t[i] == '\t')) i++;
            return i;
        }

        private bool IsCommentLine(int ls)
        {
            int i = FirstNonWs(ls);
            return i < _t.Length && _t[i] == '#';
        }

        private bool IsBlankLine(int ls)
        {
            int i = FirstNonWs(ls);
            return i >= _t.Length || _t[i] == '\n' || _t[i] == '\r';
        }
    }
}
