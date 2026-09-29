using System;
using System.Collections.Generic;

namespace Kubuno.Cargo.Toml
{
    /// <summary>
    /// A read-only view of one <c>[[array.of.tables]]</c> element. Keys resolve through the element's own
    /// key/values (dotted keys and inline tables included) and through its <c>[array.sub]</c> sub-tables.
    /// </summary>
    public sealed class TomlTableView
    {
        private readonly TomlResolver _resolver;

        internal TomlTableView(TomlResolver resolver)
        {
            _resolver = resolver;
        }

        /// <summary>The value at <paramref name="path"/> relative to this element, or <c>null</c> when absent.</summary>
        public TomlValue? GetValue(params string[] path) => _resolver.GetValue(path ?? new string[0]);

        /// <summary>The direct child key names of this element, in document order.</summary>
        public IReadOnlyList<string> GetKeys() => _resolver.GetKeys(new string[0]);
    }

    /// <summary>
    /// Resolves a path against every way TOML can express it: <c>[a.b]</c> headers, dotted keys,
    /// inline tables and implicit super-tables.
    /// </summary>
    internal sealed class TomlResolver
    {
        private readonly List<Item> _items;
        private readonly Func<string[], TomlValue?>? _arrays;

        public TomlResolver(List<Item> items, Func<string[], TomlValue?>? arrays)
        {
            items.Sort((a, b) => a.Order.CompareTo(b.Order));
            _items = items;
            _arrays = arrays;
        }

        internal sealed class Item
        {
            public Item(int order, string[] path, bool isHeader, ValueNode? node)
            {
                Order = order;
                Path = path;
                IsHeader = isHeader;
                Node = node;
            }

            public int Order { get; }

            public string[] Path { get; }

            public bool IsHeader { get; }

            public ValueNode? Node { get; }
        }

        public TomlValue? GetValue(string[] path)
        {
            if (path.Length > 0 && _arrays != null)
            {
                var a = _arrays(path);
                if (a != null) return a;
            }

            foreach (var it in _items)
            {
                if (!it.IsHeader && TomlPath.Eq(it.Path, path)) return it.Node!.Value;
            }

            foreach (var it in _items)
            {
                if (!it.IsHeader && it.Path.Length < path.Length && TomlPath.StartsWith(path, it.Path))
                {
                    return Navigate(it.Node!.Value, path, it.Path.Length);
                }
            }

            if (!TableExists(path)) return null;
            var pairs = new List<KeyValuePair<string, TomlValue>>();
            foreach (var key in ChildKeys(path))
            {
                var v = GetValue(TomlPath.Append(path, key));
                if (v != null) pairs.Add(new KeyValuePair<string, TomlValue>(key, v));
            }

            return TomlValue.CreateTable(pairs, null);
        }

        public IReadOnlyList<string> GetKeys(string[] path)
        {
            var v = GetValue(path);
            var t = v?.AsTable();
            var r = new List<string>();
            if (t == null) return r;
            foreach (var e in t) r.Add(e.Key);
            return r;
        }

        private static TomlValue? Navigate(TomlValue start, string[] path, int index)
        {
            TomlValue? cur = start;
            for (int i = index; i < path.Length && cur != null; i++) cur = cur.GetChild(path[i]);
            return cur;
        }

        private bool TableExists(string[] path)
        {
            if (path.Length == 0) return true;
            foreach (var it in _items)
            {
                if (it.Path.Length > path.Length && TomlPath.StartsWith(it.Path, path)) return true;
                if (it.IsHeader && TomlPath.Eq(it.Path, path)) return true;
            }

            return false;
        }

        private List<string> ChildKeys(string[] path)
        {
            var keys = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var it in _items)
            {
                if (it.Path.Length > path.Length && TomlPath.StartsWith(it.Path, path))
                {
                    string k = it.Path[path.Length];
                    if (seen.Add(k)) keys.Add(k);
                }
            }

            return keys;
        }
    }
}
