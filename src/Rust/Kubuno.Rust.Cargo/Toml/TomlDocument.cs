using System;
using System.Collections.Generic;

namespace Kubuno.Rust.Cargo.Toml
{
    /// <summary>
    /// A lossless ("format-preserving") TOML document. Reading resolves a path through every way TOML can
    /// express it (<c>[a.b]</c> headers, dotted keys, inline tables, implicit super-tables). Editing
    /// (<see cref="SetValue"/>, <see cref="Remove"/>) is surgical: comments, key order, blank lines, spacing,
    /// quoting style, line endings and a leading BOM are kept; only the touched lines change, and every edit is
    /// reported as one minimal <see cref="TomlEdit"/>.
    /// </summary>
    /// <remarks>
    /// Limitations: arrays of tables (<c>[[bin]]</c>) are read-only; comments inside an array that is
    /// replaced are lost; comments inside an inline table that loses an entry may be lost; the document is
    /// not thread-safe.
    /// </remarks>
    public sealed class TomlDocument
    {
        private TomlModel _model;
        private TomlResolver? _resolver;

        private TomlDocument(TomlModel model)
        {
            _model = model;
        }

        /// <summary>Parses <paramref name="text"/>; throws <see cref="TomlParseException"/> when it is not valid TOML.</summary>
        public static TomlDocument Parse(string text) => new TomlDocument(TomlParser.Parse(text));

        /// <summary>The current full text; identical to the parsed input until edited.</summary>
        public string Text => _model.Text;

        /// <inheritdoc />
        public override string ToString() => Text;

        /// <summary>The value at <paramref name="path"/> (table path + key path, dotted keys expanded), or <c>null</c> when absent.</summary>
        public TomlValue? GetValue(params string[] path) => Resolver.GetValue(path ?? new string[0]);

        /// <summary>True when a table exists at <paramref name="path"/> (header, dotted keys, inline table or implicit super-table).</summary>
        public bool ContainsTable(params string[] path) => GetValue(path)?.Kind == TomlValueKind.Table;

        /// <summary>Direct child key names of a table, in document order, de-duplicated (empty path = root).</summary>
        public IReadOnlyList<string> GetKeys(params string[] tablePath) => Resolver.GetKeys(tablePath ?? new string[0]);

        /// <summary>The <c>[[path]]</c> elements, in document order (empty when there are none).</summary>
        public IReadOnlyList<TomlTableView> GetArrayOfTables(params string[] path)
        {
            path = path ?? new string[0];
            var result = new List<TomlTableView>();
            foreach (var h in _model.Headers)
            {
                if (h.IsArray && !h.InArray && TomlPath.Eq(h.Path, path)) result.Add(new TomlTableView(ElementResolver(h)));
            }

            return result;
        }

        /// <summary>
        /// Sets the value at <paramref name="path"/>. Returns <c>null</c> (text untouched) when the value is already
        /// equal; otherwise the single minimal edit that was applied to <see cref="Text"/>.
        /// </summary>
        /// <exception cref="NotSupportedException">The path is inside an array of tables.</exception>
        /// <exception cref="InvalidOperationException">An ancestor of the path holds a non-table value.</exception>
        public TomlEdit? SetValue(TomlValue value, params string[] path)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            CheckPath(path);
            var current = GetValue(path);
            if (current != null && current.Equals(value)) return null;
            return Commit(new TomlEditor(_model).Set(value, path));
        }

        /// <summary>
        /// Removes the key, dotted subtree or table at <paramref name="path"/> (whole lines, trailing comment
        /// included). A <c>[table]</c> left with no entry, comment or sub-table is removed too. Returns <c>null</c>
        /// when the path is absent.
        /// </summary>
        public TomlEdit? Remove(params string[] path)
        {
            CheckPath(path);
            string? newText = new TomlEditor(_model).Remove(path);
            return newText == null ? (TomlEdit?)null : Commit(newText);
        }

        private static void CheckPath(string[] path)
        {
            if (path == null || path.Length == 0) throw new ArgumentException("The path must have at least one segment.", nameof(path));
            foreach (var s in path)
            {
                if (s == null) throw new ArgumentException("Path segments must not be null.", nameof(path));
            }
        }

        private TomlEdit? Commit(string newText)
        {
            string old = _model.Text;
            if (string.Equals(old, newText, StringComparison.Ordinal)) return null;
            var model = TomlParser.Parse(newText);
            var edit = TomlEdit.Diff(old, newText);
            _model = model;
            _resolver = null;
            return edit;
        }

        private TomlResolver Resolver => _resolver ??= BuildResolver();

        private TomlResolver BuildResolver()
        {
            var items = new List<TomlResolver.Item>();
            foreach (var h in _model.Headers)
            {
                if (!h.InArray) items.Add(new TomlResolver.Item(h.Start, h.Path, true, null));
            }

            foreach (var kv in _model.Kvs)
            {
                if (!kv.InArray) items.Add(new TomlResolver.Item(kv.KeyStart, kv.AbsPath, false, kv.Value));
            }

            return new TomlResolver(items, ArrayLookup);
        }

        private TomlValue? ArrayLookup(string[] path)
        {
            List<TomlValue>? tables = null;
            foreach (var h in _model.Headers)
            {
                if (!h.IsArray || h.InArray || !TomlPath.Eq(h.Path, path)) continue;
                tables = tables ?? new List<TomlValue>();
                tables.Add(ElementResolver(h).GetValue(new string[0]) ?? TomlValue.Table(new KeyValuePair<string, TomlValue>[0]));
            }

            return tables == null ? null : TomlValue.CreateArray(tables, null);
        }

        private TomlResolver ElementResolver(HeaderNode element)
        {
            var items = new List<TomlResolver.Item>
            {
                new TomlResolver.Item(element.Start, new string[0], true, null),
            };
            foreach (var kv in element.Kvs)
            {
                items.Add(new TomlResolver.Item(kv.KeyStart, kv.KeyPath, false, kv.Value));
            }

            foreach (var sub in _model.Headers)
            {
                if (sub.ArrayOwner != element) continue;
                var rel = TomlPath.Slice(sub.Path, element.Path.Length);
                items.Add(new TomlResolver.Item(sub.Start, rel, true, null));
                if (sub.IsArray) continue;
                foreach (var kv in sub.Kvs)
                {
                    items.Add(new TomlResolver.Item(kv.KeyStart, TomlPath.Concat(rel, kv.KeyPath), false, kv.Value));
                }
            }

            return new TomlResolver(items, null);
        }
    }
}
