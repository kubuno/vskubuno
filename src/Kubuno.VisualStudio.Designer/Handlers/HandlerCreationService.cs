using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.VisualStudio.Designer.Editing;

namespace Kubuno.VisualStudio.Designer.Handlers
{
    /// <summary>
    /// DSG-10's C# half (docs/DESIGNER.md §1/§6/§8): calls <c>kubuno/createHandler</c>
    /// (<see cref="IKubunoViewsLanguageServerClient"/>) and applies the resulting edit through the
    /// existing <c>Editing/</c> services - <see cref="BufferEditCore"/>, exactly like DSG-5's own
    /// single-request path (one <c>ITextEdit</c> = one undo unit "for free", per
    /// <see cref="Infrastructure.BufferEditApplier"/>'s own doc comment) - applied ONCE PER FILE in the
    /// response's <c>edit.changes</c> map. Deliberately NOT <see cref="CompoundEditCoordinator"/>, which
    /// links several requests into ONE undo unit: here the two files (the <c>.kbview</c> attribute edit,
    /// the code-behind <c>.rs</c> stub) must stay two SEPARATE undo units, per this package's own brief
    /// ("one undo unit per file").
    ///
    /// After a successful create, opens the code-behind file at the new <c>fn</c> (docs/DESIGNER.md §6:
    /// "applies the WorkspaceEdit ... and opens the .rs at the new fn") - <see cref="IHandlerDocumentHost.NavigateTo"/>.
    /// On the "already exists" short circuit, navigates straight to the existing definition instead.
    ///
    /// Pure orchestration over <see cref="IKubunoViewsLanguageServerClient"/>/<see cref="IHandlerDocumentHost"/> -
    /// unit-tested with fakes (tests/Kubuno.VisualStudio.Designer.Tests/Handlers/HandlerCreationServiceTests.cs),
    /// no live VS/JsonRpc needed - mirroring <see cref="CompoundEditCoordinator"/>'s own test-strategy note.
    ///
    /// <see cref="CreateAsync"/> must be called (and awaited) already on the VS UI thread when wired to
    /// the real <see cref="Infrastructure.VsHandlerDocumentHost"/> - the RPC round trip is awaited WITHOUT
    /// <c>ConfigureAwait(false)</c> specifically so the tail of this method (which touches
    /// <see cref="IHandlerDocumentHost"/>, VS-thread-affine in the real implementation) resumes on the
    /// same context it started on, the same convention <c>Debugging/DebugRustTestAtCursorCommand.cs</c>'s
    /// own <c>ExecuteAsync</c> already follows (<c>await ThreadHelper.JoinableTaskFactory
    /// .SwitchToMainThreadAsync()</c> before touching anything VS-owned).
    /// </summary>
    public sealed class HandlerCreationService
    {
        private readonly IKubunoViewsLanguageServerClient _client;
        private readonly IHandlerDocumentHost _documentHost;

        public HandlerCreationService(IKubunoViewsLanguageServerClient client, IHandlerDocumentHost documentHost)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _documentHost = documentHost ?? throw new ArgumentNullException(nameof(documentHost));
        }

        public async Task<HandlerCreationResult> CreateAsync(CreateHandlerRequest request, CancellationToken cancellationToken)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            // Deliberately no ConfigureAwait(false) - see the class doc comment.
            var response = await _client.CreateHandlerAsync(request, cancellationToken);
            if (response is null)
            {
                return HandlerCreationResult.RequestFailed();
            }

            if (response.Location is not null)
            {
                _documentHost.NavigateTo(response.Location.Uri, response.Location.Range.Start);
                return HandlerCreationResult.OpenedExisting(response.HandlerName);
            }

            var changes = response.Edit?.Changes;
            if (changes is null || changes.Count == 0)
            {
                return HandlerCreationResult.NothingToDo();
            }

            // Apply every file's edits as its own buffer edit / undo unit, in a stable order so a test
            // (and a human diffing two runs) sees deterministic file-application order.
            foreach (var fileUri in changes.Keys.OrderBy(uri => uri, StringComparer.Ordinal))
            {
                var edits = changes[fileUri];
                var buffer = _documentHost.OpenBuffer(fileUri);
                var applyRequest = new ApplyEditRequest(buffer.CurrentVersion, edits);
                var result = BufferEditCore.TryApply(buffer, applyRequest);
                if (!result.Succeeded)
                {
                    return HandlerCreationResult.ApplyFailed(response.HandlerName, fileUri, result.Outcome);
                }
            }

            var stub = FindHandlerStubLocation(changes, response.HandlerName);
            if (stub is not null)
            {
                _documentHost.NavigateTo(stub.Value.Uri, stub.Value.Position);
            }

            return HandlerCreationResult.Created(response.HandlerName);
        }

        /// <summary>
        /// The file/position of the new <c>fn &lt;handlerName&gt;(...)</c> stub: always the edit whose
        /// inserted text contains the literal <c>"fn &lt;handlerName&gt;("</c> - <c>kubuno-views-ls</c>'s
        /// <c>handler_insert</c> module always creates a real <c>fn</c> (never a closure-only table
        /// entry), precisely so "go to definition" keeps working. Its position is unaffected by the
        /// OTHER edit optionally inserted into the same file (the <c>handlers!</c> table registration,
        /// always placed at a strictly later offset - see that module's own doc), so the position the
        /// server reported is still exactly where the text landed once applied.
        /// </summary>
        private static (string Uri, LspPosition Position)? FindHandlerStubLocation(
            IReadOnlyDictionary<string, IReadOnlyList<TextEditDto>> changes,
            string handlerName)
        {
            var needle = $"fn {handlerName}(";
            foreach (var pair in changes)
            {
                var stub = pair.Value.FirstOrDefault(e => e.NewText.Contains(needle));
                if (stub is not null)
                {
                    return (pair.Key, stub.Range.Start);
                }
            }

            return null;
        }
    }
}
