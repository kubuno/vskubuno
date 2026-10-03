using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Kubuno.Desktop.Logic.Overrides
{
    /// <summary>
    /// One overridable member of the Kubuno control hierarchy (docs/EVENTS.md EVT-7a/EVT-7b): a method of a level
    /// trait (<c>Control</c>, <c>ButtonBase</c>…) with its exact signature and the body that runs the base behaviour
    /// (<c>self.base_mut().on_click(e);</c>, WinForms' <c>base.OnClick(e)</c>).
    /// </summary>
    public sealed class OverridableMember
    {
        public OverridableMember(string level, string name, string signature, string baseCall, string? @event, string doc, string docFr)
        {
            Level = level;
            Name = name;
            Signature = signature;
            BaseCall = baseCall;
            Event = @event;
            Doc = doc;
            DocFr = docFr;
        }

        /// <summary>The level trait declaring it (<c>"Control"</c>).</summary>
        public string Level { get; }

        public string Name { get; }

        /// <summary><c>fn on_click(&amp;mut self, e: &amp;mut EventCx&lt;'_, MouseEventArgs&gt;)</c>.</summary>
        public string Signature { get; }

        public string BaseCall { get; }

        /// <summary>The event its base behaviour raises (<c>"OnClick"</c>), if any.</summary>
        public string? Event { get; }

        public string Doc { get; }

        public string DocFr { get; }

        public string LocalizedDoc(bool french) => french && DocFr.Length > 0 ? DocFr : Doc;

        /// <summary>The method overriding its base: signature, then the base call, indented by <paramref name="indent"/>.</summary>
        public string Stub(string indent, string newline) =>
            indent + Signature + " {" + newline + indent + "    " + BaseCall + newline + indent + "}" + newline;

        public override string ToString() => Level + "::" + Name;
    }

    /// <summary>
    /// The catalogue of overridable members and the class chains of the built-in classes and levels, generated from
    /// <c>kubuno_desktop_views::component::overrides</c> (the Rust test <c>write_overrides_fixture</c> writes
    /// <c>OverridableMembers.json</c>, embedded here), so the table always matches the real traits.
    /// </summary>
    public sealed class OverrideCatalog
    {
        private OverrideCatalog(IReadOnlyDictionary<string, IReadOnlyList<string>> chains, IReadOnlyList<OverridableMember> members)
        {
            Chains = chains;
            Members = members;
        }

        /// <summary>Built-in class or level name → its chain, itself first, <c>"Component"</c> last.</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<string>> Chains { get; }

        public IReadOnlyList<OverridableMember> Members { get; }

        /// <summary>The level trait names (every level declaring a member, plus the chains' levels).</summary>
        public IEnumerable<string> Levels => Members.Select(m => m.Level).Distinct(StringComparer.Ordinal);

        private static readonly Lazy<OverrideCatalog> s_default = new Lazy<OverrideCatalog>(LoadEmbedded);

        /// <summary>The catalogue shipped with this assembly.</summary>
        public static OverrideCatalog Default => s_default.Value;

        private static OverrideCatalog LoadEmbedded()
        {
            using var stream = typeof(OverrideCatalog).Assembly.GetManifestResourceStream("Kubuno.OverridableMembers.json")
                ?? throw new InvalidOperationException("the overridable members catalogue is not embedded");
            using var reader = new StreamReader(stream);
            return FromJson(reader.ReadToEnd());
        }

        /// <summary>Reads the catalogue's JSON (<c>{ chains: {name: [...]}, members: [{level, name, signature, base_call, event, doc, doc_fr}] }</c>).</summary>
        public static OverrideCatalog FromJson(string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var chains = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            if (root.TryGetProperty("chains", out var chainsElement))
            {
                foreach (var entry in chainsElement.EnumerateObject())
                {
                    chains[entry.Name] = entry.Value.EnumerateArray().Select(v => v.GetString() ?? string.Empty).ToList();
                }
            }

            var members = new List<OverridableMember>();
            if (root.TryGetProperty("members", out var membersElement))
            {
                foreach (var m in membersElement.EnumerateArray())
                {
                    string S(string name) => m.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
                    var @event = m.TryGetProperty("event", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
                    members.Add(new OverridableMember(S("level"), S("name"), S("signature"), S("base_call"), @event, S("doc"), S("doc_fr")));
                }
            }

            return new OverrideCatalog(chains, members);
        }

        /// <summary>The level traits of the hierarchy (<c>kubuno_desktop_views_meta::LEVELS</c>).</summary>
        public static readonly IReadOnlyList<string> LevelNames = new[]
        {
            "Component", "Control", "ScrollableControl", "ContainerControl", "UserControl", "View",
            "ButtonBase", "TextBoxBase", "ListControl", "LabelBase", "ContainerBase", "RangeBase",
        };

        /// <summary>Whether <paramref name="name"/> is a level trait (<c>Control</c>, <c>ButtonBase</c>…).</summary>
        public static bool IsLevel(string name) => LevelNames.Contains(name, StringComparer.Ordinal);

        /// <summary>The members a class of chain <paramref name="chain"/> can override, nearest level first.</summary>
        public IReadOnlyList<OverridableMember> ForChain(IEnumerable<string> chain)
        {
            var result = new List<OverridableMember>();
            foreach (var level in chain)
            {
                result.AddRange(Members.Where(m => m.Level == level));
            }

            return result;
        }
    }
}
