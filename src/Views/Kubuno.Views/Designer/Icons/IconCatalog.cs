using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Kubuno.Desktop.Designer.Icons
{
    /// <summary>One layer of a glyph: SVG path data, stroked (Lucide, in design units) or filled, its themed role or fixed colour.</summary>
    public sealed class IconLayer
    {
        public IconLayer(string data, double? stroke, string role, double opacity, string? color, IReadOnlyList<double> transform)
        {
            Data = data;
            Stroke = stroke;
            Role = role;
            Opacity = opacity;
            Color = color;
            Transform = transform;
        }

        public string Data { get; }

        public double? Stroke { get; }

        /// <summary><c>base</c>, <c>alt</c>, <c>accent</c> or <c>accentcontrast</c>.</summary>
        public string Role { get; }

        public double Opacity { get; }

        /// <summary>A fixed colour (<c>#rrggbb</c>, module logos), or null for the themed role.</summary>
        public string? Color { get; }

        /// <summary>The layer's group transform <c>[a, b, c, d, e, f]</c>.</summary>
        public IReadOnlyList<double> Transform { get; }
    }

    /// <summary>A glyph of the Kubuno icon set.</summary>
    public sealed class IconGlyph
    {
        public IconGlyph(string name, string set, string? aliasOf, IReadOnlyList<string> keywords, double viewBox, IReadOnlyList<IconLayer> layers)
        {
            Name = name;
            Set = set;
            AliasOf = aliasOf;
            Keywords = keywords;
            ViewBox = viewBox;
            Layers = layers;
        }

        public string Name { get; }

        /// <summary><c>Lucide</c>, <c>Kubuno</c> (the themed icons) or <c>Modules</c> (the module logos).</summary>
        public string Set { get; }

        public string? AliasOf { get; }

        public IReadOnlyList<string> Keywords { get; }

        public double ViewBox { get; }

        public IReadOnlyList<IconLayer> Layers { get; }

        public override string ToString() => Name;
    }

    /// <summary>
    /// The Kubuno icon set as the views language server exports it (<c>kubuno/icons</c>, <c>kubuno_views::icon::catalog_json</c>):
    /// every glyph with its paths, the short aliases and the named sizes - the one source of truth the runtime also draws from.
    /// </summary>
    public sealed class IconCatalog
    {
        public static IconCatalog Empty { get; } = new IconCatalog(Array.Empty<IconGlyph>(), new Dictionary<string, string>(), new Dictionary<string, double>());

        /// <summary>The sets in the order the picker lists them.</summary>
        public static IReadOnlyList<string> SetOrder { get; } = new[] { "Lucide", "Kubuno", "Modules" };

        private readonly Dictionary<string, IconGlyph> _byName;

        public IconCatalog(IReadOnlyList<IconGlyph> icons, IReadOnlyDictionary<string, string> aliases, IReadOnlyDictionary<string, double> sizes)
        {
            Icons = icons;
            Aliases = aliases;
            Sizes = sizes;
            _byName = new Dictionary<string, IconGlyph>(StringComparer.Ordinal);
            foreach (var icon in icons)
            {
                if (!_byName.ContainsKey(icon.Name))
                {
                    _byName.Add(icon.Name, icon);
                }
            }
        }

        public IReadOnlyList<IconGlyph> Icons { get; }

        public IReadOnlyDictionary<string, string> Aliases { get; }

        public IReadOnlyDictionary<string, double> Sizes { get; }

        public bool IsEmpty => Icons.Count == 0;

        /// <summary>Reads a <c>kubuno/icons</c> answer; <see cref="Empty"/> for anything malformed.</summary>
        public static IconCatalog FromJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return Empty;
            }

            try
            {
                var root = JObject.Parse(json!);
                var icons = new List<IconGlyph>();
                foreach (var icon in root["icons"] as JArray ?? new JArray())
                {
                    var name = (string?)icon["name"];
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    var layers = (icon["layers"] as JArray ?? new JArray())
                        .Select(l => new IconLayer(
                            (string?)l["d"] ?? string.Empty,
                            l["stroke"]?.Type == JTokenType.Float || l["stroke"]?.Type == JTokenType.Integer ? (double?)l["stroke"] : null,
                            (string?)l["role"] ?? "base",
                            l["opacity"]?.Type == JTokenType.Float || l["opacity"]?.Type == JTokenType.Integer ? (double)l["opacity"]! : 1.0,
                            (string?)l["color"],
                            (l["transform"] as JArray)?.Select(v => (double)v).ToArray() ?? new[] { 1.0, 0, 0, 1, 0, 0 }))
                        .Where(l => l.Data.Length > 0)
                        .ToArray();
                    var keywords = (icon["keywords"] as JArray)?.Select(k => (string?)k).Where(k => !string.IsNullOrEmpty(k)).Select(k => k!).ToArray() ?? Array.Empty<string>();
                    var viewBox = icon["viewBox"]?.Type is JTokenType.Float or JTokenType.Integer ? (double)icon["viewBox"]! : 24.0;
                    icons.Add(new IconGlyph(name!, (string?)icon["set"] ?? "Lucide", (string?)icon["aliasOf"], keywords, viewBox, layers));
                }

                var aliases = (root["aliases"] as JObject)?.Properties().ToDictionary(p => p.Name, p => (string?)p.Value ?? string.Empty, StringComparer.Ordinal)
                    ?? new Dictionary<string, string>();
                var sizes = (root["sizes"] as JObject)?.Properties().ToDictionary(p => p.Name, p => (double)p.Value, StringComparer.Ordinal)
                    ?? new Dictionary<string, double>();
                return new IconCatalog(icons, aliases, sizes);
            }
            catch (Exception ex) when (ex is Newtonsoft.Json.JsonException or InvalidCastException or ArgumentException or FormatException)
            {
                return Empty;
            }
        }

        /// <summary>The glyph <paramref name="name"/> names: itself, or the target of an alias (<c>trash</c>), following <c>Name -&gt; Target</c> sections.</summary>
        public IconGlyph? Find(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var n = name!.Trim();
            if (Aliases.TryGetValue(n, out var target))
            {
                n = target;
            }

            for (var hop = 0; hop < 4 && _byName.TryGetValue(n, out var glyph); hop++)
            {
                if (glyph.Layers.Count > 0 || glyph.AliasOf is null)
                {
                    return glyph;
                }

                n = glyph.AliasOf;
            }

            return null;
        }

        /// <summary>
        /// The glyphs matching <paramref name="query"/> (case-insensitive, against the name and the keywords) in
        /// <paramref name="set"/> (null: every set), best first: the exact name, then names starting with the query, then names
        /// containing it, then keyword matches; each group in the sets' order, then by name. An empty query lists everything.
        /// </summary>
        public IReadOnlyList<IconGlyph> Search(string? query, string? set = null)
        {
            var q = (query ?? string.Empty).Trim();
            var words = q.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            int Rank(IconGlyph g)
            {
                if (q.Length == 0)
                {
                    return 0;
                }

                if (string.Equals(g.Name, q, StringComparison.OrdinalIgnoreCase))
                {
                    return 0;
                }

                if (g.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase))
                {
                    return 1;
                }

                if (g.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return 2;
                }

                bool Matches(string w) => g.Name.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0
                    || g.Keywords.Any(k => k.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
                return words.Length > 0 && words.All(Matches) ? 3 : -1;
            }

            int SetIndex(IconGlyph g)
            {
                var i = Array.IndexOf(SetOrder.ToArray(), g.Set);
                return i < 0 ? SetOrder.Count : i;
            }

            return Icons
                .Where(g => g.Layers.Count > 0 && (set is null || g.Set == set))
                .Select(g => (Glyph: g, Rank: Rank(g)))
                .Where(x => x.Rank >= 0)
                .OrderBy(x => x.Rank)
                .ThenBy(x => SetIndex(x.Glyph))
                .ThenBy(x => x.Glyph.Name, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Glyph)
                .ToList();
        }
    }
}
