using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.VisualStudio.Core.QuickInfo;
using Kubuno.VisualStudio.Designer;
using Kubuno.VisualStudio.Designer.Registry;
using Kubuno.VisualStudio.Designer.Registry.Infrastructure;
using Kubuno.VisualStudio.Logging;
using Kubuno.VisualStudio.Views;
using Kubuno.VisualStudio.Views.LanguageService;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace Kubuno.VisualStudio.LanguageService.QuickInfo
{
    /// <summary>C#-style QuickInfo for Rust: icon + classified one-line signature + path + formatted rustdoc (rust-analyzer hover).</summary>
    [Export(typeof(IAsyncQuickInfoSourceProvider))]
    [Name("Kubuno Rust QuickInfo")]
    [ContentType(Constants.RustContentType)]
    internal sealed class RustQuickInfoSourceProvider : IAsyncQuickInfoSourceProvider
    {
        [Import]
        internal IClassificationTypeRegistryService ClassificationRegistry { get; set; } = null!;

        [Import]
        internal ITextDocumentFactoryService TextDocumentFactory { get; set; } = null!;

        public IAsyncQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer) =>
            textBuffer.Properties.GetOrCreateSingletonProperty(() => new LspQuickInfoSource(
                textBuffer,
                () => TextDocumentFactory.TryGetTextDocument(textBuffer, out var document) ? document.FilePath : null,
                () => RustLanguageClient.Instance?.Rpc,
                (markdown, _) => Task.FromResult<QuickInfoElement?>(RustHover.Parse(markdown)?.ToQuickInfo()),
                new QuickInfoElementFactory(ClassificationRegistry),
                KubunoLog.WriteLine));
    }

    /// <summary>
    /// C#-style QuickInfo for <c>.kbview</c> files: the Kubuno control icon (or Visual Studio's property/event
    /// glyph), <c>Button</c> / <c>Text: String = ""</c> / <c>event OnClick(MouseEventArgs)</c>, and the
    /// component registry's documentation in Visual Studio's language (kubuno-views-ls hover, localized
    /// through <c>kubuno/registry</c>).
    /// </summary>
    [Export(typeof(IAsyncQuickInfoSourceProvider))]
    [Name("Kubuno Views QuickInfo")]
    [ContentType(KbviewConstants.ContentType)]
    internal sealed class KbviewQuickInfoSourceProvider : IAsyncQuickInfoSourceProvider
    {
        private readonly SemaphoreSlim _registryLock = new SemaphoreSlim(1, 1);
        private ComponentRegistry? _registry;

        [Import]
        internal IClassificationTypeRegistryService ClassificationRegistry { get; set; } = null!;

        [Import]
        internal ITextDocumentFactoryService TextDocumentFactory { get; set; } = null!;

        public IAsyncQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer) =>
            textBuffer.Properties.GetOrCreateSingletonProperty(() => new LspQuickInfoSource(
                textBuffer,
                () => TextDocumentFactory.TryGetTextDocument(textBuffer, out var document) ? document.FilePath : null,
                () => KubunoViewsLanguageClient.Current?.ReadyRpc,
                ConvertAsync,
                new QuickInfoElementFactory(ClassificationRegistry),
                KubunoLog.WriteLine));

        private async Task<QuickInfoElement?> ConvertAsync(string markdown, CancellationToken cancellationToken)
        {
            var hover = KbviewHover.Parse(markdown);
            if (hover is null)
            {
                return DocumentationRenderer.Render(markdown);
            }

            var registry = await GetRegistryAsync(cancellationToken).ConfigureAwait(false);
            return hover.ToQuickInfo(Details(hover, registry));
        }

        /// <summary>The registry, fetched once from kubuno-views-ls (again later if the server was not ready).</summary>
        private async Task<ComponentRegistry> GetRegistryAsync(CancellationToken cancellationToken)
        {
            if (_registry != null)
            {
                return _registry;
            }

            await _registryLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_registry == null)
                {
                    var fetched = await JsonRpcRegistryClient.FetchAsync(KubunoViewsLanguageClient.Current?.ReadyRpc, cancellationToken).ConfigureAwait(false);
                    if (fetched.Components.Count > 0)
                    {
                        _registry = fetched;
                    }

                    return fetched;
                }

                return _registry;
            }
            finally
            {
                _registryLock.Release();
            }
        }

        internal static KbviewSymbolDetails Details(KbviewHover hover, ComponentRegistry registry)
        {
            var details = new KbviewSymbolDetails
            {
                ValidValuesLabel = DesignerText.IsFrench ? "Valeurs possibles :" : "Valid values:",
            };

            switch (hover.Kind)
            {
                case KbviewHoverKind.Element:
                    var element = registry.Find(hover.Name);
                    if (element != null)
                    {
                        details.Documentation = element.LocalizedDoc;
                        details.Container = DesignerText.ToolboxTabName(element.Family);
                    }

                    break;
                case KbviewHoverKind.Property:
                    var property = hover.Element is null ? null : registry.Find(hover.Element)?.Properties.Find(p => p.Name == hover.Name);
                    if (property != null)
                    {
                        details.Documentation = property.LocalizedDoc;
                        details.TypeName = TypeName(property.Kind);
                        details.Default = property.Default;
                        if (property.Kind.Tag == PropKindTag.Enum)
                        {
                            details.ValidValues = new List<string>(property.Kind.EnumVariants);
                        }
                    }

                    break;
                case KbviewHoverKind.Event:
                    var evt = hover.Element is null ? null : registry.Find(hover.Element)?.FindEvent(hover.Name);
                    if (evt != null)
                    {
                        details.Documentation = evt.LocalizedDoc;
                        details.EventArgs = evt.ArgsType;
                        details.EventCategory = evt.LocalizedCategory;
                    }

                    break;
                case KbviewHoverKind.CommonAttribute:
                    var doc = DesignerText.CommonAttributeDoc(hover.Name);
                    details.Documentation = doc.Length > 0 ? doc : null;
                    details.TypeName = hover.Name is "X" or "Y" or "Width" or "Height" ? "f32" : null;
                    break;
                case KbviewHoverKind.XName:
                    details.Documentation = DesignerText.NameDescription;
                    break;
            }

            return details;
        }

        private static string TypeName(PropKind kind)
        {
            switch (kind.Tag)
            {
                case PropKindTag.Bool: return "bool";
                case PropKindTag.F32: return "f32";
                case PropKindTag.Enum: return "enum";
                default: return "String";
            }
        }
    }
}
