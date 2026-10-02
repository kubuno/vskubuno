using System;
using System.Collections.Generic;
using System.Linq;
using Kubuno.Shared.Logic.QuickInfo;
using Kubuno.Rust.SolutionExplorer;
using Microsoft.VisualStudio.Core.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Classification;

namespace Kubuno.Rust.LanguageService.QuickInfo
{
    /// <summary>
    /// Turns the SDK-free QuickInfo model (Kubuno.Shared.Logic.QuickInfo) into Visual Studio's own
    /// tooltip elements - <see cref="ContainerElement"/>, <see cref="ClassifiedTextElement"/>,
    /// <see cref="ImageElement"/> - the same ones Roslyn builds for C#. Runs are classified with Roslyn's
    /// classification names (<c>keyword</c>, <c>struct name</c>, <c>method name</c>...) so the colors
    /// follow Tools &gt; Options &gt; Fonts and Colors exactly like a C# tooltip; a name that is not
    /// registered (no C# support installed) falls back to a predefined one.
    /// </summary>
    public sealed class QuickInfoElementFactory
    {
        private readonly IClassificationTypeRegistryService _registry;
        private readonly Dictionary<QuickInfoTextKind, string> _names = new Dictionary<QuickInfoTextKind, string>();

        public QuickInfoElementFactory(IClassificationTypeRegistryService registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public object Create(QuickInfoElement element)
        {
            switch (element)
            {
                case QuickInfoContainer container:
                    return new ContainerElement((ContainerElementStyle)(int)container.Style, container.Children.Select(Create));
                case QuickInfoText text:
                    return new ClassifiedTextElement(text.Runs.Select(CreateRun));
                case QuickInfoImage image:
                    var moniker = image.ImageGuid != Guid.Empty
                        ? new Microsoft.VisualStudio.Imaging.Interop.ImageMoniker { Guid = image.ImageGuid, Id = image.ImageId }
                        : KubunoTreeItem.Moniker(image.MonikerName ?? "Type");
                    return new ImageElement(new ImageId(moniker.Guid, moniker.Id));
                default:
                    throw new ArgumentOutOfRangeException(nameof(element), element?.GetType().Name, "Unknown QuickInfo element.");
            }
        }

        private ClassifiedTextRun CreateRun(QuickInfoRun run)
        {
            var style = ClassifiedTextRunStyle.Plain;
            if ((run.Style & QuickInfoRunStyle.Bold) != 0)
            {
                style |= ClassifiedTextRunStyle.Bold;
            }

            if ((run.Style & QuickInfoRunStyle.Italic) != 0)
            {
                style |= ClassifiedTextRunStyle.Italic;
            }

            if ((run.Style & QuickInfoRunStyle.Code) != 0)
            {
                style |= ClassifiedTextRunStyle.UseClassificationFont;
            }

            var classification = ClassificationName(run.Kind);
            if (run.Url != null)
            {
                var url = run.Url;
                return new ClassifiedTextRun(classification, run.Text, () => OpenUrl(url), url, style);
            }

            return new ClassifiedTextRun(classification, run.Text, style);
        }

        private static void OpenUrl(string url)
        {
            if (Kubuno.Shared.Logic.QuickInfo.Markdown.IsWebUrl(url))
            {
                VsShellUtilities.OpenSystemBrowser(url);
            }
        }

        /// <summary>The classification of a kind: the Roslyn name when it is registered, else a predefined fallback.</summary>
        private string ClassificationName(QuickInfoTextKind kind)
        {
            lock (_names)
            {
                if (!_names.TryGetValue(kind, out var name))
                {
                    var (preferred, fallback) = Names(kind);
                    name = _registry.GetClassificationType(preferred) != null ? preferred : fallback;
                    _names[kind] = name;
                }

                return name;
            }
        }

        private static (string Preferred, string Fallback) Names(QuickInfoTextKind kind)
        {
            switch (kind)
            {
                case QuickInfoTextKind.Muted: return ("excluded code", "comment");
                case QuickInfoTextKind.Keyword: return ("keyword", "keyword");
                case QuickInfoTextKind.Namespace: return ("namespace name", "identifier");
                case QuickInfoTextKind.Class: return ("class name", "type");
                case QuickInfoTextKind.Struct: return ("struct name", "type");
                case QuickInfoTextKind.Enum: return ("enum name", "type");
                case QuickInfoTextKind.Trait: return ("interface name", "type");
                case QuickInfoTextKind.TypeParameter: return ("type parameter name", "type");
                case QuickInfoTextKind.Method: return ("method name", "identifier");
                case QuickInfoTextKind.Field: return ("field name", "identifier");
                case QuickInfoTextKind.Property: return ("property name", "identifier");
                case QuickInfoTextKind.Event: return ("event name", "identifier");
                case QuickInfoTextKind.Parameter: return ("parameter name", "identifier");
                case QuickInfoTextKind.Local: return ("local name", "identifier");
                case QuickInfoTextKind.Constant: return ("constant name", "identifier");
                case QuickInfoTextKind.EnumMember: return ("enum member name", "identifier");
                case QuickInfoTextKind.Macro: return ("preprocessor keyword", "keyword");
                case QuickInfoTextKind.Identifier: return ("identifier", "identifier");
                case QuickInfoTextKind.Punctuation: return ("punctuation", "text");
                case QuickInfoTextKind.Operator: return ("operator", "operator");
                case QuickInfoTextKind.Number: return ("number", "literal");
                case QuickInfoTextKind.String: return ("string", "string");
                case QuickInfoTextKind.Comment: return ("comment", "comment");
                default: return ("text", "text");
            }
        }
    }
}
