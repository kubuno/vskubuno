using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Desktop.Designer.Editing;
using Kubuno.Desktop.Designer.Outline;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace Kubuno.Desktop.Designer.Selection.Infrastructure
{
    /// <summary>
    /// The real <c>kubuno/elementAtOffset</c>/<c>kubuno/rangeOfElement</c>/<c>textDocument/documentSymbol</c>
    /// caller, over the same <c>StreamJsonRpc.JsonRpc</c> object
    /// <c>Kubuno.Desktop.Views.LanguageService.KubunoViewsLanguageClient.Rpc</c> already exposes -
    /// the exact precedent <see cref="Handlers.Infrastructure.JsonRpcKubunoViewsLanguageServerClient"/>
    /// already establishes for DSG-10's own method (see that class's own doc comment for the full
    /// reasoning, unchanged here: why a plain camelCase anonymous object for the request, why a weakly-
    /// typed <see cref="JToken"/> result re-serialized and re-parsed by this project's OWN (fully tested)
    /// <see cref="SelectionResponseParser"/> rather than bound directly against Newtonsoft's formatter).
    ///
    /// Not unit-tested here - needs a live <c>JsonRpc</c> connected to a running <c>kubuno-views-ls</c>
    /// process, the same reasoning that sibling class gives; a manual round trip in the experimental
    /// instance is this class's test strategy, per docs/DESIGNER.md's DSG-8 row ("Manual, visual check").
    /// </summary>
    public sealed class JsonRpcViewsSelectionLanguageServerClient : IViewsSelectionLanguageServerClient
    {
        private readonly JsonRpc _rpc;

        public JsonRpcViewsSelectionLanguageServerClient(JsonRpc rpc)
        {
            _rpc = rpc ?? throw new ArgumentNullException(nameof(rpc));
        }

        public async Task<ElementAtOffsetResponse?> ElementAtOffsetAsync(string documentUri, LspPosition position, CancellationToken cancellationToken)
        {
            if (documentUri is null)
            {
                throw new ArgumentNullException(nameof(documentUri));
            }

            var argument = new
            {
                uri = documentUri,
                position = new { line = position.Line, character = position.Character },
            };

            var json = await InvokeAsync("kubuno/elementAtOffset", argument, cancellationToken);
            return json is null ? null : SelectionResponseParser.ParseElementAtOffset(json);
        }

        public async Task<LspRange?> RangeOfElementAsync(string documentUri, string elementId, CancellationToken cancellationToken)
        {
            if (documentUri is null)
            {
                throw new ArgumentNullException(nameof(documentUri));
            }

            if (elementId is null)
            {
                throw new ArgumentNullException(nameof(elementId));
            }

            var argument = new { uri = documentUri, elementId };
            var json = await InvokeAsync("kubuno/rangeOfElement", argument, cancellationToken);
            return json is null ? null : SelectionResponseParser.ParseRangeOfElement(json);
        }

        public async Task<IReadOnlyList<DocumentSymbolDto>?> DocumentSymbolAsync(string documentUri, CancellationToken cancellationToken)
        {
            if (documentUri is null)
            {
                throw new ArgumentNullException(nameof(documentUri));
            }

            // The standard LSP `DocumentSymbolParams` shape (`{textDocument: {uri}}`) -
            // `textDocument/documentSymbol` is not a custom `kubuno/*` method, but it is invoked exactly
            // the same way over this same `Rpc` object (docs/DESIGNER.md §1: "kubuno-views-ls/src/symbols.rs
            // ... already implemented").
            var argument = new { textDocument = new { uri = documentUri } };
            var json = await InvokeAsync("textDocument/documentSymbol", argument, cancellationToken);
            return json is null ? null : SelectionResponseParser.ParseDocumentSymbols(json);
        }

        private async Task<string?> InvokeAsync(string method, object argument, CancellationToken cancellationToken)
        {
            JToken? token;
            try
            {
                token = await _rpc.InvokeWithParameterObjectAsync<JToken?>(method, argument, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is RemoteInvocationException or ConnectionLostException or OperationCanceledException)
            {
                return null;
            }

            return token?.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
