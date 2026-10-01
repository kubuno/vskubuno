using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Kubuno.Desktop.Designer.Bindings
{
    /// <summary>
    /// A <c>{Binding …}</c> attribute value read part by part, without loss (docs/DESIGNER.md, "Data bindings"): every
    /// part keeps its text, its position and its key - known or not - so an editor can change one option (the path,
    /// the mode, the converter…) and write the rest back exactly as it was. The grammar is <c>kubuno_views::binding</c>'s
    /// (docs/VIEWS-SPEC.md §6.1): the first bare part is the path, the others are <c>Key=Value</c>, values with commas
    /// or quotes are quoted <c>'…'</c> (<c>''</c> is one quote). The language server stays the authority on what the
    /// runtime accepts; this class only reads and writes the text.
    /// </summary>
    public sealed class BindingMarkup
    {
        /// <summary>The keys the runtime knows, canonical names first (the aliases after).</summary>
        public static readonly IReadOnlyList<string> Keys = new[]
        {
            "Path", "Source", "Mode", "UpdateSourceTrigger", "Converter", "ConverterParameter", "FallbackValue", "StringFormat", "TargetNullValue",
            "ConverterCulture", "FormatString", "NullValue", "Culture", "FormatInfo",
        };

        /// <summary>The modes, as written.</summary>
        public static readonly IReadOnlyList<string> Modes = new[] { "OneWay", "TwoWay", "OneTime", "OneWayToSource" };

        /// <summary>The update triggers, as written.</summary>
        public static readonly IReadOnlyList<string> Triggers = new[] { "PropertyChanged", "LostFocus", "Explicit" };

        private readonly List<Part> _parts;
        private readonly string _original;
        private bool _changed;

        private BindingMarkup(List<Part> parts, string original)
        {
            _parts = parts;
            _original = original;
        }

        /// <summary>One part: its key (null for the bare path) and its value as written (quotes included).</summary>
        private sealed class Part
        {
            public Part(string? key, string value)
            {
                Key = key;
                Value = value;
            }

            public string? Key { get; }

            public string Value { get; set; }
        }

        /// <summary>A new binding of <paramref name="path"/> (empty for none yet).</summary>
        public static BindingMarkup Create(string? path)
        {
            var markup = new BindingMarkup(new List<Part>(), string.Empty) { _changed = true };
            if (!string.IsNullOrWhiteSpace(path))
            {
                markup._parts.Add(new Part(null, path!.Trim()));
            }

            return markup;
        }

        /// <summary>Parses <paramref name="raw"/>; false for anything that is not a <c>{Binding …}</c> (a literal, a <c>{Res …}</c>).</summary>
        public static bool TryParse(string? raw, out BindingMarkup? markup)
        {
            markup = null;
            var text = raw?.Trim();
            if (text is null || text.Length < "{Binding}".Length || text[0] != '{' || text[text.Length - 1] != '}')
            {
                return false;
            }

            var inner = text.Substring(1, text.Length - 2).TrimStart();
            if (!inner.StartsWith("Binding", StringComparison.Ordinal))
            {
                return false;
            }

            var body = inner.Substring("Binding".Length);
            if (body.Length > 0 && !char.IsWhiteSpace(body[0]) && body[0] != ',')
            {
                return false;
            }

            var parts = new List<Part>();
            foreach (var segment in Split(body))
            {
                var s = segment.Trim();
                if (s.Length == 0)
                {
                    continue;
                }

                var eq = s.IndexOf('=');
                parts.Add(eq < 0 ? new Part(null, s) : new Part(s.Substring(0, eq).Trim(), s.Substring(eq + 1).Trim()));
            }

            markup = new BindingMarkup(parts, text);
            return true;
        }

        /// <summary>Whether <paramref name="raw"/> is a markup extension the runtime resolves (<c>{Binding …}</c> or <c>{Res …}</c>).</summary>
        public static bool IsMarkupExtension(string? raw) => TryParse(raw, out _) || IsResource(raw);

        /// <summary>Whether <paramref name="raw"/> is a resource reference (<c>{Res key[, Source=set]}</c>).</summary>
        public static bool IsResource(string? raw)
        {
            var t = raw?.Trim();
            return t is { Length: > 6 } && t.StartsWith("{Res", StringComparison.Ordinal) && t.EndsWith("}", StringComparison.Ordinal) && (char.IsWhiteSpace(t[4]) || t[4] == ',');
        }

        /// <summary>The resource key of a <c>{Res key}</c>, else null.</summary>
        public static string? ResourceKey(string? raw)
        {
            if (!IsResource(raw))
            {
                return null;
            }

            var inner = raw!.Trim();
            inner = inner.Substring(4, inner.Length - 5);
            var first = Split(inner).Select(s => s.Trim()).FirstOrDefault(s => s.Length > 0 && s.IndexOf('=') < 0);
            return first;
        }

        /// <summary>Splits on the commas outside <c>'…'</c> quotes.</summary>
        private static IEnumerable<string> Split(string body)
        {
            var current = new StringBuilder();
            var quoted = false;
            foreach (var c in body)
            {
                if (c == '\'')
                {
                    quoted = !quoted;
                }

                if (c == ',' && !quoted)
                {
                    yield return current.ToString();
                    current.Clear();
                    continue;
                }

                current.Append(c);
            }

            yield return current.ToString();
        }

        /// <summary>The canonical name of a key (<c>StringFormat</c> for <c>FormatString</c>…), or the key itself when unknown.</summary>
        public static string Canonical(string key) => key switch
        {
            "FormatString" => "StringFormat",
            "NullValue" => "TargetNullValue",
            "Culture" or "FormatInfo" => "ConverterCulture",
            _ => key,
        };

        /// <summary>The keys of the parts, as written (the bare path included as <c>Path</c>).</summary>
        public IEnumerable<string> WrittenKeys => _parts.Select((p, i) => p.Key ?? (i == 0 ? "Path" : string.Empty));

        /// <summary>The keys written that the runtime does not know.</summary>
        public IEnumerable<string> UnknownKeys => _parts.Where(p => p.Key is not null && !Keys.Contains(p.Key)).Select(p => p.Key!);

        private int IndexOf(string key)
        {
            var canonical = Canonical(key);
            for (var i = 0; i < _parts.Count; i++)
            {
                var k = _parts[i].Key;
                if ((k is null && i == 0 && canonical == "Path") || (k is not null && Canonical(k) == canonical))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>The value of <paramref name="key"/> (any of its aliases), unquoted; null when absent.</summary>
        public string? Get(string key)
        {
            var i = IndexOf(key);
            return i < 0 ? null : Unquote(_parts[i].Value);
        }

        /// <summary>
        /// Sets <paramref name="key"/> to <paramref name="value"/> where it is written (under the alias in use), or appends
        /// it; null or empty removes it. The path stays the bare first part when it is one.
        /// </summary>
        public BindingMarkup Set(string key, string? value)
        {
            var i = IndexOf(key);
            var current = i < 0 ? null : Unquote(_parts[i].Value);
            if (string.Equals(current, string.IsNullOrEmpty(value) ? null : value, StringComparison.Ordinal))
            {
                return this;
            }

            _changed = true;
            if (string.IsNullOrEmpty(value))
            {
                if (i >= 0)
                {
                    _parts.RemoveAt(i);
                }

                return this;
            }

            var written = Quote(value!);
            if (i >= 0)
            {
                _parts[i].Value = written;
            }
            else if (Canonical(key) == "Path" && !_parts.Any(p => p.Key is null))
            {
                // A new path is the bare first part (`{Binding Title, Mode=TwoWay}`), unless a Source names it.
                if (Get("Source") is null)
                {
                    _parts.Insert(0, new Part(null, written));
                }
                else
                {
                    _parts.Add(new Part("Path", written));
                }
            }
            else
            {
                _parts.Add(new Part(Canonical(key), written));
            }

            return this;
        }

        /// <summary>The path as written (<c>Path=</c> or the bare first part).</summary>
        public string? Path => Get("Path");

        /// <summary>The data component named by <c>Source=</c>.</summary>
        public string? Source => Get("Source");

        /// <summary>The path the runtime resolves: <c>Source.Path</c>, or either alone.</summary>
        public string? FullPath => (Source, Path) switch
        {
            ({ } s, { } p) => s + "." + p,
            ({ } s, null) => s,
            (null, { } p) => p,
            _ => null,
        };

        /// <summary><c>Mode</c>, or null when not written (OneWay).</summary>
        public string? Mode => Get("Mode");

        /// <summary>
        /// Points the binding at <paramref name="source"/>/<paramref name="path"/> (a data component and its member, or a
        /// view-model path with a null source), keeping every other option.
        /// </summary>
        public BindingMarkup Retarget(string? source, string? path)
        {
            if (string.IsNullOrEmpty(source))
            {
                Set("Source", null);
                var i = IndexOf("Path");
                if (i >= 0 && _parts[i].Key is not null)
                {
                    // `Path=` moves back to the bare first part.
                    _parts.RemoveAt(i);
                    _changed = true;
                }

                Set("Path", path);
                return this;
            }

            var bare = _parts.FindIndex(p => p.Key is null);
            if (bare == 0)
            {
                // `{Binding Name, Source=x}` is not the documented shape: write `Source=x, Path=Name`.
                _parts.RemoveAt(0);
                _changed = true;
            }

            var sourceAt = IndexOf("Source");
            if (sourceAt < 0)
            {
                _parts.Insert(0, new Part("Source", Quote(source!)));
                _changed = true;
            }
            else
            {
                Set("Source", source);
            }

            if (IndexOf("Path") < 0 && !string.IsNullOrEmpty(path))
            {
                _parts.Insert(IndexOf("Source") + 1, new Part("Path", Quote(path!)));
                _changed = true;
            }
            else
            {
                Set("Path", path);
            }

            return this;
        }

        /// <summary>The value without its quotes (<c>''</c> is one quote).</summary>
        public static string Unquote(string v)
        {
            v = v.Trim();
            return v.Length >= 2 && v[0] == '\'' && v[v.Length - 1] == '\'' ? v.Substring(1, v.Length - 2).Replace("''", "'") : v;
        }

        /// <summary>The value quoted when it has to be (a comma, a quote, a brace, leading or trailing spaces).</summary>
        public static string Quote(string v) =>
            v.Length == 0 || v.IndexOfAny(new[] { ',', '\'', '{', '}', '=' }) >= 0 || v.Trim().Length != v.Length ? "'" + v.Replace("'", "''") + "'" : v;

        /// <summary>The expression: the original text when nothing changed, else the parts in their order.</summary>
        public override string ToString()
        {
            if (!_changed)
            {
                return _original;
            }

            if (_parts.Count == 0)
            {
                return string.Empty;
            }

            return "{Binding " + string.Join(", ", _parts.Select(p => p.Key is null ? p.Value : p.Key + "=" + p.Value)) + "}";
        }
    }
}
