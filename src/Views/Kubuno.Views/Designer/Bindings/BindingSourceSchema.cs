using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Kubuno.Views.Designer.Bindings
{
    /// <summary>The shape of a value as a property sees it (<c>binding_sources::Shape</c> of the language server).</summary>
    public enum BindingShape
    {
        Any,
        Bool,
        Number,
        Text,
        List,
        Object,
    }

    /// <summary>A file position the language server points at (LSP, 0-based).</summary>
    public sealed class BindingLocation
    {
        public BindingLocation(string uri, int line, int character)
        {
            Uri = uri;
            Line = line;
            Character = character;
        }

        public string Uri { get; }

        public int Line { get; }

        public int Character { get; }
    }

    /// <summary>One thing a binding can name: a field, a property, a row field, a data component and its members, a resource.</summary>
    public sealed class BindingMember
    {
        public string Name { get; set; } = string.Empty;

        public string Path { get; set; } = string.Empty;

        /// <summary>What to write to bind it alone (<c>{Binding Title}</c>, <c>{Binding Source=customers, Path=Name}</c>, <c>{Res key}</c>).</summary>
        public string Expression { get; set; } = string.Empty;

        /// <summary><c>field</c>, <c>property</c>, <c>path</c>, <c>rowField</c>, <c>component</c>, <c>column</c>, <c>state</c>, <c>resource</c>.</summary>
        public string Kind { get; set; } = string.Empty;

        public string? RustType { get; set; }

        public BindingShape Shape { get; set; }

        public bool Writable { get; set; } = true;

        public string Doc { get; set; } = string.Empty;

        public BindingLocation? Location { get; set; }

        public IReadOnlyList<BindingMember> Children { get; set; } = Array.Empty<BindingMember>();

        /// <summary>The type shown next to the name (the Rust type, else the shape).</summary>
        public string TypeText => RustType is { Length: > 0 } t ? t : Shape.ToString();
    }

    /// <summary>One level of resolution (the view's data context, the row of a template).</summary>
    public sealed class BindingContext
    {
        public string Label { get; set; } = string.Empty;

        public IReadOnlyList<BindingMember> Members { get; set; } = Array.Empty<BindingMember>();

        /// <summary>Whether it may answer paths the scan cannot list.</summary>
        public bool Open { get; set; }
    }

    /// <summary>A converter a binding can name.</summary>
    public sealed class BindingConverterInfo
    {
        public string Name { get; set; } = string.Empty;

        public BindingShape Output { get; set; }

        public bool TwoWay { get; set; }

        public string Doc { get; set; } = string.Empty;

        public bool Project { get; set; }
    }

    /// <summary>A binding problem of one attribute of the selected element (the Properties window's markers).</summary>
    public sealed class BindingIssue
    {
        public string ElementId { get; set; } = string.Empty;

        public string Attribute { get; set; } = string.Empty;

        /// <summary><c>error</c>, <c>warning</c> or <c>information</c>.</summary>
        public string Severity { get; set; } = "warning";

        public string Code { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// What a binding of one element can name - the answer of <c>kubuno/bindingSources</c> (docs/DESIGNER.md, "Data
    /// bindings"): the view's data context, the row of the template the element is in, the data components, the
    /// resources, the converters, and the binding problems of the element's attributes.
    /// </summary>
    public sealed class BindingSourceSchema
    {
        public static readonly BindingSourceSchema Empty = new BindingSourceSchema();

        public BindingContext Context { get; set; } = new BindingContext();

        public BindingContext? Item { get; set; }

        public IReadOnlyList<BindingMember> Components { get; set; } = Array.Empty<BindingMember>();

        public IReadOnlyList<BindingMember> Resources { get; set; } = Array.Empty<BindingMember>();

        public IReadOnlyList<BindingConverterInfo> Converters { get; set; } = Array.Empty<BindingConverterInfo>();

        public IReadOnlyList<BindingIssue> Issues { get; set; } = Array.Empty<BindingIssue>();

        /// <summary>The problems of attribute <paramref name="attribute"/>.</summary>
        public IEnumerable<BindingIssue> IssuesOf(string attribute) => Issues.Where(i => string.Equals(i.Attribute, attribute, StringComparison.Ordinal));

        /// <summary>Every member a path can name (row, context, components and their members).</summary>
        public IEnumerable<BindingMember> AllMembers()
        {
            foreach (var m in Item?.Members ?? Array.Empty<BindingMember>())
            {
                yield return m;
            }

            foreach (var m in Context.Members)
            {
                yield return m;
            }

            foreach (var c in Components)
            {
                yield return c;
                foreach (var m in c.Children)
                {
                    yield return m;
                }
            }
        }

        /// <summary>The member a binding names (by its full path), when the schema lists it.</summary>
        public BindingMember? Find(string? fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
            {
                return null;
            }

            var inItem = Item?.Members.FirstOrDefault(m => m.Path == fullPath);
            return inItem ?? AllMembers().FirstOrDefault(m => m.Path == fullPath) ?? Resources.FirstOrDefault(r => r.Path == fullPath);
        }

        /// <summary>Reads the JSON answer of <c>kubuno/bindingSources</c> (<c>{ schema, issues }</c>); <see cref="Empty"/> for anything else.</summary>
        public static BindingSourceSchema Parse(JToken? token)
        {
            if (token is not JObject root || root["schema"] is not JObject s)
            {
                return Empty;
            }

            return new BindingSourceSchema
            {
                Context = ParseContext(s["context"] as JObject) ?? new BindingContext { Open = true },
                Item = ParseContext(s["item"] as JObject),
                Components = ParseMembers(s["components"]),
                Resources = ParseMembers(s["resources"]),
                Converters = (s["converters"] as JArray)?.OfType<JObject>().Select(c => new BindingConverterInfo
                {
                    Name = (string?)c["name"] ?? string.Empty,
                    Output = ParseShape((string?)c["output"]),
                    TwoWay = (bool?)c["twoWay"] ?? false,
                    Doc = (string?)c["doc"] ?? string.Empty,
                    Project = (bool?)c["project"] ?? false,
                }).ToList() ?? (IReadOnlyList<BindingConverterInfo>)Array.Empty<BindingConverterInfo>(),
                Issues = (root["issues"] as JArray)?.OfType<JObject>().Select(i => new BindingIssue
                {
                    ElementId = (string?)i["elementId"] ?? string.Empty,
                    Attribute = (string?)i["attribute"] ?? string.Empty,
                    Severity = (string?)i["severity"] ?? "warning",
                    Code = (string?)i["code"] ?? string.Empty,
                    Message = (string?)i["message"] ?? string.Empty,
                }).ToList() ?? (IReadOnlyList<BindingIssue>)Array.Empty<BindingIssue>(),
            };
        }

        private static BindingContext? ParseContext(JObject? o) => o is null ? null : new BindingContext
        {
            Label = (string?)o["label"] ?? string.Empty,
            Members = ParseMembers(o["members"]),
            Open = (bool?)o["open"] ?? false,
        };

        private static IReadOnlyList<BindingMember> ParseMembers(JToken? token) =>
            (token as JArray)?.OfType<JObject>().Select(ParseMember).ToList() ?? (IReadOnlyList<BindingMember>)Array.Empty<BindingMember>();

        private static BindingMember ParseMember(JObject m) => new BindingMember
        {
            Name = (string?)m["name"] ?? string.Empty,
            Path = (string?)m["path"] ?? string.Empty,
            Expression = (string?)m["expression"] ?? string.Empty,
            Kind = (string?)m["kind"] ?? string.Empty,
            RustType = (string?)m["rustType"],
            Shape = ParseShape((string?)m["shape"]),
            Writable = (bool?)m["writable"] ?? true,
            Doc = (string?)m["doc"] ?? string.Empty,
            Location = m["location"] is JObject l && (string?)l["uri"] is { } uri && l["range"]?["start"] is JObject start
                ? new BindingLocation(uri, (int?)start["line"] ?? 0, (int?)start["character"] ?? 0)
                : null,
            Children = ParseMembers(m["children"]),
        };

        public static BindingShape ParseShape(string? s) => Enum.TryParse<BindingShape>(s, out var shape) ? shape : BindingShape.Any;
    }
}
