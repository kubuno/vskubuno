using System;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using Kubuno.Shared.Logic.QuickInfo;
using Kubuno.Rust.Logic.QuickInfo;
using Kubuno.Shared.Logging;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace Kubuno.Rust.LanguageService.QuickInfo
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
}
