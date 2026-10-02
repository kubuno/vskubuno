using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Views.Designer.Editing;
using Kubuno.Views.Designer.Outline;

namespace Kubuno.Views.Designer.Selection
{
    /// <summary>
    /// The seam <see cref="SelectionSyncService"/> (and <see cref="Outline"/>'s tree builder's caller)
    /// depend on instead of a raw <c>StreamJsonRpc.JsonRpc</c> - mirroring
    /// <see cref="Handlers.IKubunoViewsLanguageServerClient"/>'s own reasoning (unit-testable with a
    /// fake, see tests/Kubuno.Desktop.Tests/Designer/Selection/Fakes/FakeViewsSelectionLanguageServerClient.cs).
    ///
    /// Deliberately a SEPARATE interface from <see cref="Handlers.IKubunoViewsLanguageServerClient"/>
    /// rather than extending that one: that interface's own doc comment scopes it to DSG-10's
    /// <c>kubuno/createHandler</c> only, and this package (DSG-8) calls three unrelated methods -
    /// docs/DESIGNER.md §8's <c>kubuno/elementAtOffset</c>/<c>kubuno/rangeOfElement</c> (the "DSG-2
    /// protocol") plus the STANDARD LSP <c>textDocument/documentSymbol</c> the Document Outline needs
    /// (§1: "reuses kubuno-views-ls/src/symbols.rs ... verbatim"). Both interfaces are real, JsonRpc-
    /// dependent adapters over the SAME underlying <c>StreamJsonRpc.JsonRpc</c> object
    /// (<c>KubunoViewsLanguageClient.Rpc</c>, docs/DESIGNER.md §3) - see
    /// <see cref="Infrastructure.JsonRpcViewsSelectionLanguageServerClient"/>.
    /// </summary>
    public interface IViewsSelectionLanguageServerClient
    {
        /// <summary>
        /// <c>kubuno/elementAtOffset</c> (docs/DESIGNER.md §8, DSG-2 protocol): the innermost element
        /// whose subtree contains <paramref name="position"/> - the XML pane -&gt; design surface
        /// selection-sync direction. <see langword="null"/> when the call itself failed (server
        /// unreachable, a JSON-RPC error, an unparsable response) OR the server's own result was
        /// <see langword="null"/> (no document open, or the position lands on nothing) - never throws
        /// for either case, so a caller can treat "nothing to select" uniformly regardless of which it
        /// was, the same "degrade to no usable result" contract <see cref="Handlers.IKubunoViewsLanguageServerClient.CreateHandlerAsync"/>
        /// already documents for its own call.
        /// </summary>
        Task<ElementAtOffsetResponse?> ElementAtOffsetAsync(string documentUri, LspPosition position, CancellationToken cancellationToken);

        /// <summary>
        /// <c>kubuno/rangeOfElement</c> (docs/DESIGNER.md §8): the inverse direction - the design
        /// surface -&gt; XML pane selection sync. <see langword="null"/> for the same reasons
        /// <see cref="ElementAtOffsetAsync"/> documents (a stale/unresolved <paramref name="elementId"/>
        /// degrades to this, never an exception).
        /// </summary>
        Task<LspRange?> RangeOfElementAsync(string documentUri, string elementId, CancellationToken cancellationToken);

        /// <summary>
        /// The standard LSP <c>textDocument/documentSymbol</c> (docs/DESIGNER.md §1's Document Outline:
        /// "reuses kubuno-views-ls/src/symbols.rs ... verbatim; no new Rust is needed"). <see langword="null"/>
        /// for the same "degrade to nothing usable" reasons as the other two methods here (an empty list
        /// is a valid, different result - a document with no root element - from a failed call).
        /// </summary>
        Task<IReadOnlyList<DocumentSymbolDto>?> DocumentSymbolAsync(string documentUri, CancellationToken cancellationToken);
    }
}
