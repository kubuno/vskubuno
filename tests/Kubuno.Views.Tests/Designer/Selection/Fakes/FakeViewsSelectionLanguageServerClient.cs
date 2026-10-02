using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Views.Designer.Editing;
using Kubuno.Views.Designer.Outline;
using Kubuno.Views.Designer.Selection;

namespace Kubuno.Views.Tests.Designer.Selection.Fakes
{
    /// <summary>A scripted <see cref="IViewsSelectionLanguageServerClient"/> - each method returns whatever the matching dictionary/field was set up with, <see langword="null"/> by default (mirroring the server's own "unresolved -&gt; null" contract).</summary>
    internal sealed class FakeViewsSelectionLanguageServerClient : IViewsSelectionLanguageServerClient
    {
        public Dictionary<string, LspRange> RangesByElementId { get; } = new Dictionary<string, LspRange>();

        public ElementAtOffsetResponse? ElementAtOffsetResponse { get; set; }

        public IReadOnlyList<DocumentSymbolDto>? DocumentSymbols { get; set; }

        public int ElementAtOffsetCallCount { get; private set; }

        public int RangeOfElementCallCount { get; private set; }

        public List<string> RangeOfElementRequests { get; } = new List<string>();

        public Task<ElementAtOffsetResponse?> ElementAtOffsetAsync(string documentUri, LspPosition position, CancellationToken cancellationToken)
        {
            ElementAtOffsetCallCount++;
            return Task.FromResult(ElementAtOffsetResponse);
        }

        public Task<LspRange?> RangeOfElementAsync(string documentUri, string elementId, CancellationToken cancellationToken)
        {
            RangeOfElementCallCount++;
            RangeOfElementRequests.Add(elementId);
            return Task.FromResult(RangesByElementId.TryGetValue(elementId, out var range) ? (LspRange?)range : null);
        }

        public Task<IReadOnlyList<DocumentSymbolDto>?> DocumentSymbolAsync(string documentUri, CancellationToken cancellationToken) =>
            Task.FromResult(DocumentSymbols);
    }
}
