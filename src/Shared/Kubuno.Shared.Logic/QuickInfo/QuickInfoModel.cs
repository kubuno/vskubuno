using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Kubuno.Shared.Logic.QuickInfo
{
    /// <summary>
    /// What a run of QuickInfo text stands for. The VSIX maps each value onto an editor classification
    /// (the Roslyn names Visual Studio already colors for C#: <c>keyword</c>, <c>struct name</c>,
    /// <c>method name</c>...), so a Rust or <c>.kbview</c> tooltip is colored exactly like a C# one.
    /// </summary>
    public enum QuickInfoTextKind
    {
        /// <summary>Plain text (documentation prose).</summary>
        Text,

        /// <summary>Discreet grey text (the containing path, memory layout notes, "...").</summary>
        Muted,
        Keyword,
        Namespace,

        /// <summary>A type whose exact kind is unknown (a type reference in a signature).</summary>
        Class,
        Struct,
        Enum,
        Trait,
        TypeParameter,
        Method,
        Field,
        Property,
        Event,
        Parameter,
        Local,
        Constant,
        EnumMember,
        Macro,
        Identifier,
        Punctuation,
        Operator,
        Number,
        String,
        Comment,
    }

    /// <summary>Presentation flags of a run (mapped onto <c>ClassifiedTextRunStyle</c>).</summary>
    [Flags]
    public enum QuickInfoRunStyle
    {
        None = 0,
        Bold = 1,
        Italic = 2,

        /// <summary>Code font (inline code, code blocks).</summary>
        Code = 4,
    }

    /// <summary>One classified piece of text; a run with a <see cref="Url"/> is a clickable link.</summary>
    public sealed class QuickInfoRun
    {
        public QuickInfoRun(QuickInfoTextKind kind, string text, QuickInfoRunStyle style = QuickInfoRunStyle.None, string? url = null)
        {
            Kind = kind;
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Style = style;
            Url = url;
        }

        public QuickInfoTextKind Kind { get; }

        public string Text { get; }

        public QuickInfoRunStyle Style { get; }

        /// <summary>The http(s) target of a link, null for plain text.</summary>
        public string? Url { get; }

        public override string ToString() => Text;
    }

    /// <summary>
    /// A node of the tooltip, mirroring Visual Studio's <c>Microsoft.VisualStudio.Text.Adornments</c>
    /// elements one to one (<see cref="QuickInfoContainer"/> = <c>ContainerElement</c>,
    /// <see cref="QuickInfoText"/> = <c>ClassifiedTextElement</c>, <see cref="QuickInfoImage"/> =
    /// <c>ImageElement</c>), without depending on the VS SDK so the conversion is unit-testable.
    /// </summary>
    public abstract class QuickInfoElement
    {
        /// <summary>The plain text of this element (tests, logging, accessibility).</summary>
        public abstract string ToPlainText();
    }

    /// <summary>A line (or wrapped paragraph) of classified runs.</summary>
    public sealed class QuickInfoText : QuickInfoElement
    {
        public QuickInfoText(IEnumerable<QuickInfoRun> runs)
        {
            Runs = (runs ?? throw new ArgumentNullException(nameof(runs))).ToList();
        }

        public QuickInfoText(params QuickInfoRun[] runs)
            : this((IEnumerable<QuickInfoRun>)runs)
        {
        }

        public IReadOnlyList<QuickInfoRun> Runs { get; }

        public override string ToPlainText() => string.Concat(Runs.Select(r => r.Text));
    }

    /// <summary>
    /// An icon: a <c>KnownMonikers</c> property name, or an image of one of the extension's image manifests (e.g. the
    /// desktop layer's Kubuno control icon of a <c>.kbview</c> tag).
    /// </summary>
    public sealed class QuickInfoImage : QuickInfoElement
    {
        private QuickInfoImage(string? monikerName, Guid imageGuid, int imageId, string? tag)
        {
            MonikerName = monikerName;
            ImageGuid = imageGuid;
            ImageId = imageId;
            Tag = tag;
        }

        /// <summary>A <c>KnownMonikers</c> property name (e.g. <c>MethodPrivate</c>), or null.</summary>
        public string? MonikerName { get; }

        /// <summary>The image manifest GUID of an extension image, <see cref="Guid.Empty"/> for a <see cref="MonikerName"/>.</summary>
        public Guid ImageGuid { get; }

        /// <summary>The image manifest ID of an extension image.</summary>
        public int ImageId { get; }

        /// <summary>What an extension image stands for (e.g. the <c>.kbview</c> element tag of a control icon), or null.</summary>
        public string? Tag { get; }

        public static QuickInfoImage Moniker(string name) => new QuickInfoImage(name ?? throw new ArgumentNullException(nameof(name)), Guid.Empty, 0, null);

        /// <summary>An image of an image manifest (<paramref name="imageGuid"/>, <paramref name="imageId"/>), standing for <paramref name="tag"/>.</summary>
        public static QuickInfoImage Image(Guid imageGuid, int imageId, string? tag = null) => new QuickInfoImage(null, imageGuid, imageId, tag);

        public override string ToPlainText() => string.Empty;
    }

    /// <summary>Layout of a <see cref="QuickInfoContainer"/> (same values as <c>ContainerElementStyle</c>).</summary>
    [Flags]
    public enum QuickInfoContainerStyle
    {
        /// <summary>Children side by side, wrapping.</summary>
        Wrapped = 0,

        /// <summary>Children one below the other.</summary>
        Stacked = 1,

        /// <summary>Extra vertical space between stacked children.</summary>
        VerticalPadding = 2,
    }

    public sealed class QuickInfoContainer : QuickInfoElement
    {
        public QuickInfoContainer(QuickInfoContainerStyle style, IEnumerable<QuickInfoElement> children)
        {
            Style = style;
            Children = (children ?? throw new ArgumentNullException(nameof(children))).ToList();
        }

        public QuickInfoContainer(QuickInfoContainerStyle style, params QuickInfoElement[] children)
            : this(style, (IEnumerable<QuickInfoElement>)children)
        {
        }

        public QuickInfoContainerStyle Style { get; }

        public IReadOnlyList<QuickInfoElement> Children { get; }

        public override string ToPlainText()
        {
            var parts = Children.Select(c => c.ToPlainText()).Where(t => t.Length > 0);
            return string.Join((Style & QuickInfoContainerStyle.Stacked) != 0 ? "\n" : string.Empty, parts);
        }
    }

    /// <summary>Builds lists of runs, merging adjacent runs of identical presentation.</summary>
    public sealed class RunBuilder
    {
        private readonly List<QuickInfoRun> _runs = new List<QuickInfoRun>();
        private readonly StringBuilder _pending = new StringBuilder();
        private QuickInfoTextKind _kind;
        private QuickInfoRunStyle _style;
        private string? _url;

        public bool IsEmpty => _runs.Count == 0 && _pending.Length == 0;

        public void Add(QuickInfoTextKind kind, string text, QuickInfoRunStyle style = QuickInfoRunStyle.None, string? url = null)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (_pending.Length > 0 && (kind != _kind || style != _style || !string.Equals(url, _url, StringComparison.Ordinal)))
            {
                Flush();
            }

            _kind = kind;
            _style = style;
            _url = url;
            _pending.Append(text);
        }

        public List<QuickInfoRun> Build()
        {
            Flush();
            return _runs;
        }

        private void Flush()
        {
            if (_pending.Length > 0)
            {
                _runs.Add(new QuickInfoRun(_kind, _pending.ToString(), _style, _url));
                _pending.Clear();
            }
        }
    }
}
