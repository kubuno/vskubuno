using System;
using System.Collections.Generic;

namespace Kubuno.Rust.Cargo.Toml
{
    /// <summary>The kind of a <see cref="TomlValue"/>.</summary>
    public enum TomlValueKind
    {
        /// <summary>A basic, literal or multi-line string.</summary>
        String,

        /// <summary>A 64-bit integer (decimal, hex, octal or binary in the source).</summary>
        Integer,

        /// <summary>A double-precision float (including <c>inf</c> and <c>nan</c>).</summary>
        Float,

        /// <summary><c>true</c> or <c>false</c>.</summary>
        Boolean,

        /// <summary>An offset/local date-time, date or time; only the raw source text is kept.</summary>
        DateTime,

        /// <summary>An array of values.</summary>
        Array,

        /// <summary>A table (a <c>[header]</c> table, an inline table or a dotted-key subtree).</summary>
        Table,
    }

    /// <summary>
    /// An immutable TOML value. Values read from a document carry their exact source text in
    /// <see cref="Raw"/>; values built in code have a <c>null</c> <see cref="Raw"/>. Equality is
    /// semantic (a literal <c>'x'</c> equals a basic <c>"x"</c>; table key order is irrelevant).
    /// </summary>
    public abstract class TomlValue : IEquatable<TomlValue>
    {
        private protected TomlValue(string? raw)
        {
            Raw = raw;
        }

        /// <summary>The kind of this value.</summary>
        public abstract TomlValueKind Kind { get; }

        /// <summary>The exact source text of a parsed value, or <c>null</c> for values created in code.</summary>
        public string? Raw { get; }

        /// <summary>Creates a string value.</summary>
        public static TomlValue String(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return new StringValue(value, null);
        }

        /// <summary>Creates an integer value.</summary>
        public static TomlValue Integer(long value) => new IntegerValue(value, null);

        /// <summary>Creates a float value.</summary>
        public static TomlValue Float(double value) => new FloatValue(value, null);

        /// <summary>Creates a boolean value.</summary>
        public static TomlValue Boolean(bool value) => new BooleanValue(value, null);

        /// <summary>Creates an array value.</summary>
        public static TomlValue Array(IEnumerable<TomlValue> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            return CreateArray(new List<TomlValue>(items), null);
        }

        /// <summary>Creates a table value; keys must be unique.</summary>
        public static TomlValue Table(IEnumerable<KeyValuePair<string, TomlValue>> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            return CreateTable(new List<KeyValuePair<string, TomlValue>>(entries), null);
        }

        /// <summary>The string content, or <c>null</c> when this is not a string.</summary>
        public virtual string? AsString() => null;

        /// <summary>The integer, or <c>null</c> when this is not an integer.</summary>
        public virtual long? AsInteger() => null;

        /// <summary>The float, or <c>null</c> when this is not a float.</summary>
        public virtual double? AsFloat() => null;

        /// <summary>The boolean, or <c>null</c> when this is not a boolean.</summary>
        public virtual bool? AsBoolean() => null;

        /// <summary>The date-time source text, or <c>null</c> when this is not a date-time.</summary>
        public virtual string? AsDateTime() => null;

        /// <summary>The items, or <c>null</c> when this is not an array.</summary>
        public virtual IReadOnlyList<TomlValue>? AsArray() => null;

        /// <summary>The entries in document order, or <c>null</c> when this is not a table.</summary>
        public virtual IReadOnlyList<KeyValuePair<string, TomlValue>>? AsTable() => null;

        /// <inheritdoc />
        public abstract bool Equals(TomlValue? other);

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is TomlValue v && Equals(v);

        /// <inheritdoc />
        public abstract override int GetHashCode();

        /// <summary>Single-line TOML rendering of this value.</summary>
        public override string ToString() => TomlValueFormatter.Format(this);

        internal static TomlValue CreateString(string v, string? raw) => new StringValue(v, raw);

        internal static TomlValue CreateInteger(long v, string? raw) => new IntegerValue(v, raw);

        internal static TomlValue CreateFloat(double v, string? raw) => new FloatValue(v, raw);

        internal static TomlValue CreateBoolean(bool v, string? raw) => new BooleanValue(v, raw);

        internal static TomlValue CreateDateTime(string raw) => new DateTimeValue(raw);

        internal static TomlValue CreateArray(List<TomlValue> items, string? raw)
        {
            foreach (var i in items)
            {
                if (i == null) throw new ArgumentException("Array items must not be null.", nameof(items));
            }

            return new ArrayValue(items, raw);
        }

        internal static TomlValue CreateTable(List<KeyValuePair<string, TomlValue>> entries, string? raw)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in entries)
            {
                if (e.Key == null || e.Value == null) throw new ArgumentException("Table keys and values must not be null.", nameof(entries));
                if (!seen.Add(e.Key)) throw new ArgumentException("Duplicate table key '" + e.Key + "'.", nameof(entries));
            }

            return new TableValue(entries, raw);
        }

        internal TomlValue? GetChild(string key)
        {
            var t = AsTable();
            if (t == null) return null;
            foreach (var e in t)
            {
                if (string.Equals(e.Key, key, StringComparison.Ordinal)) return e.Value;
            }

            return null;
        }

        private sealed class StringValue : TomlValue
        {
            private readonly string _v;

            public StringValue(string v, string? raw)
                : base(raw)
            {
                _v = v;
            }

            public override TomlValueKind Kind => TomlValueKind.String;

            public override string? AsString() => _v;

            public override bool Equals(TomlValue? other) => other is StringValue s && string.Equals(s._v, _v, StringComparison.Ordinal);

            public override int GetHashCode() => _v.GetHashCode();
        }

        private sealed class IntegerValue : TomlValue
        {
            private readonly long _v;

            public IntegerValue(long v, string? raw)
                : base(raw)
            {
                _v = v;
            }

            public override TomlValueKind Kind => TomlValueKind.Integer;

            public override long? AsInteger() => _v;

            public override bool Equals(TomlValue? other) => other is IntegerValue i && i._v == _v;

            public override int GetHashCode() => _v.GetHashCode();
        }

        private sealed class FloatValue : TomlValue
        {
            private readonly double _v;

            public FloatValue(double v, string? raw)
                : base(raw)
            {
                _v = v;
            }

            public override TomlValueKind Kind => TomlValueKind.Float;

            public override double? AsFloat() => _v;

            public override bool Equals(TomlValue? other) => other is FloatValue f && f._v.Equals(_v);

            public override int GetHashCode() => _v.GetHashCode();
        }

        private sealed class BooleanValue : TomlValue
        {
            private readonly bool _v;

            public BooleanValue(bool v, string? raw)
                : base(raw)
            {
                _v = v;
            }

            public override TomlValueKind Kind => TomlValueKind.Boolean;

            public override bool? AsBoolean() => _v;

            public override bool Equals(TomlValue? other) => other is BooleanValue b && b._v == _v;

            public override int GetHashCode() => _v ? 1 : 0;
        }

        private sealed class DateTimeValue : TomlValue
        {
            private readonly string _v;

            public DateTimeValue(string raw)
                : base(raw)
            {
                _v = raw;
            }

            public override TomlValueKind Kind => TomlValueKind.DateTime;

            public override string? AsDateTime() => _v;

            public override bool Equals(TomlValue? other) => other is DateTimeValue d && string.Equals(d._v, _v, StringComparison.Ordinal);

            public override int GetHashCode() => _v.GetHashCode();
        }

        private sealed class ArrayValue : TomlValue
        {
            private readonly List<TomlValue> _items;

            public ArrayValue(List<TomlValue> items, string? raw)
                : base(raw)
            {
                _items = items;
            }

            public override TomlValueKind Kind => TomlValueKind.Array;

            public override IReadOnlyList<TomlValue>? AsArray() => _items;

            public override bool Equals(TomlValue? other)
            {
                if (!(other is ArrayValue a) || a._items.Count != _items.Count) return false;
                for (int i = 0; i < _items.Count; i++)
                {
                    if (!_items[i].Equals(a._items[i])) return false;
                }

                return true;
            }

            public override int GetHashCode()
            {
                int h = 17;
                foreach (var i in _items) h = (h * 31) + i.GetHashCode();
                return h;
            }
        }

        private sealed class TableValue : TomlValue
        {
            private readonly List<KeyValuePair<string, TomlValue>> _entries;

            public TableValue(List<KeyValuePair<string, TomlValue>> entries, string? raw)
                : base(raw)
            {
                _entries = entries;
            }

            public override TomlValueKind Kind => TomlValueKind.Table;

            public override IReadOnlyList<KeyValuePair<string, TomlValue>>? AsTable() => _entries;

            public override bool Equals(TomlValue? other)
            {
                if (!(other is TableValue t) || t._entries.Count != _entries.Count) return false;
                foreach (var e in _entries)
                {
                    var o = t.GetChild(e.Key);
                    if (o == null || !o.Equals(e.Value)) return false;
                }

                return true;
            }

            public override int GetHashCode()
            {
                int h = 0;
                foreach (var e in _entries) h += (e.Key.GetHashCode() * 31) ^ e.Value.GetHashCode();
                return h;
            }
        }
    }
}
