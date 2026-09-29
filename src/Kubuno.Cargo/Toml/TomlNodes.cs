using System;
using System.Collections.Generic;

namespace Kubuno.Cargo.Toml
{
    /// <summary>A parsed <c>[header]</c> / <c>[[header]]</c> line together with the key/values that follow it.</summary>
    internal sealed class HeaderNode
    {
        public int Id;
        public string[] Path = new string[0];
        public bool IsArray;

        /// <summary>For a sub-table (or nested array) of an <c>[[array]]</c> element: the owning array header.</summary>
        public HeaderNode? ArrayOwner;

        public int Scope;

        /// <summary>Offset of the start of the header's line.</summary>
        public int LineStart;

        /// <summary>Offset of the opening bracket.</summary>
        public int Start;

        /// <summary>Offset just after the closing bracket(s).</summary>
        public int End;

        /// <summary>Offset just after the line terminator (or the end of the text).</summary>
        public int LineEnd;

        public List<KvNode> Kvs = new List<KvNode>();

        public bool InArray => ArrayOwner != null;
    }

    /// <summary>A parsed <c>key = value</c> line.</summary>
    internal sealed class KvNode
    {
        public string[] KeyPath = new string[0];
        public string[] AbsPath = new string[0];

        /// <summary>Offsets just after each key segment.</summary>
        public int[] SegEnds = new int[0];

        public int KeyStart;
        public int KeyEnd;
        public ValueNode Value = null!;
        public int LineStart;
        public int LineEnd;
        public HeaderNode? Owner;

        public bool InArray => Owner != null && (Owner.IsArray || Owner.InArray);
    }

    /// <summary>The source span and decoded value of a value; arrays and inline tables also expose their parts.</summary>
    internal sealed class ValueNode
    {
        public ValueNode(TomlValue value, int start, int end)
        {
            Value = value;
            Start = start;
            End = end;
        }

        public TomlValue Value;
        public int Start;
        public int End;

        /// <summary>Non-null for arrays.</summary>
        public List<ValueNode>? Items;

        public bool TrailingComma;

        /// <summary>Non-null for inline tables.</summary>
        public List<InlineEntry>? Entries;
    }

    /// <summary>A <c>key = value</c> entry inside an inline table.</summary>
    internal sealed class InlineEntry
    {
        public string[] KeyPath = new string[0];
        public int KeyStart;
        public int KeyEnd;
        public ValueNode Value = null!;
    }

    internal sealed class TomlModel
    {
        public TomlModel(string text, List<HeaderNode> headers, List<KvNode> kvs)
        {
            Text = text;
            Headers = headers;
            Kvs = kvs;
        }

        public string Text { get; }

        public List<HeaderNode> Headers { get; }

        /// <summary>Every key/value line of the document, in document order.</summary>
        public List<KvNode> Kvs { get; }
    }

    internal static class TomlPath
    {
        public static bool Eq(string[] a, string[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
            }

            return true;
        }

        /// <summary>True when <paramref name="path"/> begins with <paramref name="prefix"/> (equal paths included).</summary>
        public static bool StartsWith(string[] path, string[] prefix)
        {
            if (path.Length < prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++)
            {
                if (!string.Equals(path[i], prefix[i], StringComparison.Ordinal)) return false;
            }

            return true;
        }

        public static string[] Slice(string[] path, int start, int count)
        {
            var r = new string[count];
            System.Array.Copy(path, start, r, 0, count);
            return r;
        }

        public static string[] Slice(string[] path, int start) => Slice(path, start, path.Length - start);

        public static string[] Concat(string[] a, string[] b)
        {
            var r = new string[a.Length + b.Length];
            System.Array.Copy(a, 0, r, 0, a.Length);
            System.Array.Copy(b, 0, r, a.Length, b.Length);
            return r;
        }

        public static string[] Append(string[] a, string s) => Concat(a, new[] { s });

        public static int CommonPrefix(string[] a, string[] b)
        {
            int n = Math.Min(a.Length, b.Length);
            int i = 0;
            while (i < n && string.Equals(a[i], b[i], StringComparison.Ordinal)) i++;
            return i;
        }

        public static string Display(string[] path) => string.Join(".", path);
    }
}
