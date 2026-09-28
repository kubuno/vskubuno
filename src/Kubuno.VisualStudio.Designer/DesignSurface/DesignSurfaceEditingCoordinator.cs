using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Kubuno.VisualStudio.Designer.Editing;
using Kubuno.VisualStudio.Designer.Editing.Infrastructure;
using Kubuno.VisualStudio.Designer.Outline;
using Kubuno.VisualStudio.Designer.Properties;
using Kubuno.VisualStudio.Designer.Registry;
using Kubuno.VisualStudio.Designer.Registry.Infrastructure;
using Kubuno.VisualStudio.Designer.Selection;
using Kubuno.VisualStudio.Designer.Selection.Infrastructure;
using Kubuno.VisualStudio.Designer.UI;
using Kubuno.VisualStudio.Views.LanguageService;
using Kubuno.VisualStudio.Views.Logging;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Threading;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;
using OleInterop = Microsoft.VisualStudio.OLE.Interop;

namespace Kubuno.VisualStudio.Designer.DesignSurface
{
    /// <summary>
    /// The VSIX integration step's own caller for <c>IDesignSurfaceHost.EditRequested</c>/
    /// <c>SetDocumentText</c> (INTEGRATION.md &sect;6/&sect;8): turns design mode on, forwards a
    /// Delete/nudge <see cref="DesignSurfaceEditOp"/> to <c>kubuno-views-ls</c>'s <c>kubuno/applyEdit</c>
    /// over the SAME <c>StreamJsonRpc.JsonRpc</c> object
    /// <c>Kubuno.VisualStudio.Views.LanguageService.KubunoViewsLanguageClient.Rpc</c> exposes once
    /// <c>AttachForCustomMessageAsync</c> has run (docs/DESIGNER.md &sect;3), and applies the resulting
    /// <c>{range, newText}</c> edits through <see cref="BufferEditApplier"/> - one <c>ITextEdit</c>, one
    /// undo unit, per <see cref="BufferEditApplier"/>'s own doc comment (a Delete/nudge is always a
    /// SINGLE <c>kubuno/applyEdit</c> round trip, so <see cref="Editing.CompoundEditCoordinator"/>'s
    /// multi-request coalescing is not needed here - see that class's own doc for when it would be).
    /// Also pushes the buffer's own text back to the surface (<c>kubuno/setText</c>) on every
    /// <c>ITextBuffer.Changed</c>, debounced, so the rendered surface follows the developer's own
    /// keystrokes in the XML pane exactly as docs/DESIGNER.md &sect;2 asks ("source of truth is the VS
    /// text buffer, never disk").
    ///
    /// Lives in this library (not the VSIX project) because every type it needs -
    /// <c>Microsoft.VisualStudio.ComponentModelHost.IComponentModel</c>,
    /// <c>Microsoft.VisualStudio.Editor.IVsEditorAdaptersFactoryService</c>,
    /// <c>Kubuno.VisualStudio.Views.LanguageService.KubunoViewsLanguageClient</c> - is already reachable
    /// from here (this project already references <c>Microsoft.VisualStudio.SDK</c> and
    /// <c>Kubuno.VisualStudio.Views</c>, see this project's own csproj top comment); no dependency on
    /// the VSIX assembly itself is needed. <see cref="UI.DesignerSplitView"/> only owns a small field of
    /// this type and forwards <see cref="Dispose"/> - see that class's own, minimal edit for this.
    ///
    /// Not unit-tested here - needs a live COM service provider, a live <c>IVsTextLines</c>/
    /// <c>ITextBuffer</c> and, for the RPC half, a running <c>kubuno-views-ls</c>; every failure mode
    /// (no component model, no editor-adapters service, no live language client yet, a stale buffer
    /// version, a malformed RPC response) degrades to "do nothing, log it" rather than throwing, so a
    /// half-initialized VS host never breaks the pane. Manual check in the experimental instance is this
    /// class's test strategy, matching every other real-VS adapter in this library
    /// (<see cref="Infrastructure.BufferEditApplier"/>/<see cref="Infrastructure.DesignerUndoScope"/>'s
    /// own doc comments).
    /// </summary>
    internal sealed partial class DesignSurfaceEditingCoordinator : IDisposable
    {
        private const string ApplyEditMethod = "kubuno/applyEdit";
        private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(200);

        private readonly IDesignSurfaceHost _host;
        private readonly ITextBuffer _buffer;
        private readonly IVsTextLines _textLines;
        private readonly CodeWindowHost _codeWindowHost;
        private readonly Func<KubunoViewsLanguageClient?> _resolveLanguageClient;
        private readonly DispatcherTimer _pushTimer;
        private readonly Func<ITrackSelection?>? _trackSelection;
        private readonly OleInterop.IServiceProvider? _oleServiceProvider;
        private readonly Action? _ensureActiveDesigner;
        private bool _disposed;

        // Set once SetupSelectionSyncAsync completes (INTEGRATION.md §9) - null until then, and
        // permanently null when the host is a PlaceholderDesignSurfaceHost or no live JsonRpc/laid-out
        // text view ever became available (degrades silently, never blocks the pane).
        private SelectionSyncService? _selectionSync;
        private VsTextViewSelectionAdapter? _selectionTextView;
        private IViewsSelectionLanguageServerClient? _viewsSelectionClient;
        private string? _documentUri;
        private readonly OutlineViewModel _outlineViewModel = OutlineToolWindowHost.Current;

        private DesignSurfaceEditingCoordinator(
            IDesignSurfaceHost host,
            ITextBuffer buffer,
            IVsTextLines textLines,
            CodeWindowHost codeWindowHost,
            Func<KubunoViewsLanguageClient?> resolveLanguageClient,
            Func<ITrackSelection?>? trackSelection,
            OleInterop.IServiceProvider? oleServiceProvider,
            Action? ensureActiveDesigner)
        {
            _host = host;
            _buffer = buffer;
            _textLines = textLines;
            _codeWindowHost = codeWindowHost;
            _resolveLanguageClient = resolveLanguageClient;
            _trackSelection = trackSelection;
            _oleServiceProvider = oleServiceProvider;
            _ensureActiveDesigner = ensureActiveDesigner;

            _pushTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = DebounceInterval };
            _pushTimer.Tick += OnPushTimerTick;

            _host.EditRequested += OnEditRequested;
            if (_host is RustDesignSurfaceHost rustHost)
            {
                // DSG-9: a Flow reorder / toolbox drop and a move/resize batch (see the .Native.cs half).
                rustHost.DragDropEditRequested += OnDragDropEditRequested;
                rustHost.EditRequestsReceived += OnEditRequestsReceived;
                rustHost.UnhandledSurfaceKey += OnUnhandledSurfaceKey;
            }

            _buffer.Changed += OnBufferChanged;
            _host.SetDesignMode(true);

            // INTEGRATION.md §9: DSG-8's selection sync, wired here because this class already resolves
            // every live collaborator (IComponentModel, the language client's JsonRpc, the document URI)
            // that both the editing pipeline above and selection sync need - see that section's own
            // "Putting it together" snippet, which this method follows directly.
#pragma warning disable VSSDK007 // no package-owned JoinableTaskFactory reachable from here - see ToolboxToolWindow.OnToolWindowCreated's own identical precedent/comment.
            ThreadHelper.JoinableTaskFactory.RunAsync(SetupSelectionSyncAsync).FileAndForget("Kubuno/Designer/SelectionSync");
#pragma warning restore VSSDK007
        }

        /// <summary>
        /// Builds a coordinator for one open designer pane, or <see langword="null"/> if the VS services
        /// it needs (component model, editor-adapters bridge, a data buffer for <paramref name="textLines"/>)
        /// are unavailable - logged, never thrown, since a half-available host must still let the pane
        /// open (docs/DESIGNER.md's own "degrade to no-op" posture, mirrored here on the C# side).
        /// </summary>
        internal static DesignSurfaceEditingCoordinator? TryCreate(IDesignSurfaceHost host, IVsTextLines textLines, CodeWindowHost codeWindowHost, OleInterop.IServiceProvider? oleServiceProvider, Func<ITrackSelection?>? trackSelection = null, Action? ensureActiveDesigner = null)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                if (host is null || textLines is null || codeWindowHost is null || oleServiceProvider is null)
                {
                    return null;
                }

                var serviceProvider = new ServiceProvider(oleServiceProvider);
                if (serviceProvider.GetService(typeof(SComponentModel)) is not IComponentModel componentModel)
                {
                    KubunoViewsLogHost.Current.WriteLine("[designer] editing coordinator: SComponentModel unavailable - EditRequested/buffer-push will not be wired for this pane.");
                    return null;
                }

                var editorAdapters = componentModel.GetService<IVsEditorAdaptersFactoryService>();
                var dataBuffer = editorAdapters?.GetDataBuffer(textLines);
                if (dataBuffer is null)
                {
                    KubunoViewsLogHost.Current.WriteLine("[designer] editing coordinator: no ITextBuffer for this document - EditRequested/buffer-push will not be wired for this pane.");
                    return null;
                }

                KubunoViewsLanguageClient? ResolveClient() =>
                    componentModel.DefaultExportProvider.GetExportedValues<ILanguageClient>().OfType<KubunoViewsLanguageClient>().FirstOrDefault();

                return new DesignSurfaceEditingCoordinator(host, dataBuffer, textLines, codeWindowHost, ResolveClient, trackSelection, oleServiceProvider, ensureActiveDesigner);
            }
            catch (Exception ex) when (ex is InvalidOperationException or COMException)
            {
                KubunoViewsLogHost.Current.WriteException("[designer] editing coordinator: failed to initialize", ex);
                return null;
            }
        }

        private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
        {
            // Coalesce a fast typist's keystrokes into one push (docs/DESIGNER.md §2: "~150-250ms, the
            // same order of magnitude as view_preview.rs's own 250ms repaint-nudge timer"): restart the
            // timer on every change instead of pushing immediately.
            _pushTimer.Stop();
            _pushTimer.Start();
        }

        private void OnPushTimerTick(object? sender, EventArgs e)
        {
            _pushTimer.Stop();
            try
            {
                _host.SetDocumentText(_buffer.CurrentSnapshot.GetText());
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
            {
                KubunoViewsLogHost.Current.WriteException("[designer] pushing buffer text to the design surface failed", ex);
            }

            // INTEGRATION.md §9's own "not wired by this package, left for whoever owns the surrounding
            // piece" note names THIS debounce as the natural place to also refresh the Document Outline
            // on every edit - reuses the SAME 200 ms cadence, no second timer. A no-op until
            // SetupSelectionSyncAsync has resolved a live client/document URI.
            if (_viewsSelectionClient is not null && _documentUri is not null)
            {
#pragma warning disable VSSDK007 // see the constructor's own identical precedent/comment.
                ThreadHelper.JoinableTaskFactory.RunAsync(RefreshOutlineAsync).FileAndForget("Kubuno/Designer/OutlineRefresh");
#pragma warning restore VSSDK007
            }
        }

        private async Task RefreshOutlineAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var client = _viewsSelectionClient;
            var documentUri = _documentUri;
            if (client is null || documentUri is null)
            {
                return;
            }

            try
            {
                var symbols = await client.DocumentSymbolAsync(documentUri, CancellationToken.None);
                var tree = DocumentSymbolTreeBuilder.Build(symbols);
                _outlineViewModel.Load(tree);
                UpdateSelectableElements(tree);
            }
            catch (Exception ex)
            {
                KubunoViewsLogHost.Current.WriteException("[designer] refreshing the Document Outline failed", ex);
            }
        }

        /// <summary>
        /// DSG-8's selection sync (INTEGRATION.md §9): wires <see cref="SelectionSyncService"/> once every
        /// collaborator it needs is actually available - a live <c>RustDesignSurfaceHost</c> (never the
        /// placeholder), a laid-out <see cref="CodeWindowHost.PrimaryView"/> (polled - see
        /// <see cref="WaitForPrimaryViewAsync"/>, that property's own doc: "tolerate null ... re-query
        /// later"), this document's own URI, and a live <c>JsonRpc</c>. Any one of these missing degrades
        /// to "no selection sync for this pane" rather than throwing or retrying forever - the pane
        /// itself (editing, the design surface) already works without it.
        /// </summary>
        private async Task SetupSelectionSyncAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                if (_host is not RustDesignSurfaceHost rustHost)
                {
                    // PlaceholderDesignSurfaceHost: nothing to select on the Design pane yet.
                    return;
                }

                var documentUri = TryGetDocumentUri();
                if (documentUri is null)
                {
                    KubunoViewsLogHost.Current.WriteLine("[designer] selection sync skipped: could not resolve this document's own URI.");
                    return;
                }

                var primaryView = await WaitForPrimaryViewAsync();
                if (primaryView is null)
                {
                    KubunoViewsLogHost.Current.WriteLine("[designer] selection sync skipped: the XML pane's IVsTextView never became available.");
                    return;
                }

                // A document restored at startup (or opened before the language server finished
                // initializing) gets here before the client is attached - wait for it instead of giving up
                // (found live: the designer restored with the solution had no selection sync at all).
                var rpc = await WaitForLanguageClientRpcAsync();
                if (rpc is null)
                {
                    KubunoViewsLogHost.Current.WriteLine("[designer] selection sync skipped: the Kubuno Views language client did not attach within 60 s.");
                    return;
                }

                var registry = await JsonRpcRegistryClient.FetchAsync(rpc, CancellationToken.None);
                var lsClient = new JsonRpcViewsSelectionLanguageServerClient(rpc);
                var textView = new VsTextViewSelectionAdapter(primaryView);
                // FlushBeforeSelectTarget (below), not DesignSurfaceSelectionTarget directly: a `select`
                // this triggers is resolved against kubuno-views-ls's own, UNDEBOUNCED parse of the buffer
                // (ElementAtOffsetAsync/RangeOfElementAsync - always current), while the design surface's
                // own text only follows the SAME buffer through OnBufferChanged's 200ms-debounced
                // OnPushTimerTick. A caret move (or an Outline click) landing inside that 200ms window would
                // otherwise send `select {id}` while the surface is still rendering the PREVIOUS text - an
                // id that is only valid post-edit can resolve to nothing, or worse, to whatever element now
                // occupies that same ordinal position in the stale tree (e.g. the moved-to element's own
                // parent) - see this pane's own INTEGRATION.md/report for the live symptom this fixes.
                var surfaceTarget = new FlushBeforeSelectTarget(new DesignSurfaceSelectionTarget(rustHost), this);

                _viewsSelectionClient = lsClient;
                _documentUri = documentUri;
                _selectionTextView = textView;
                _selectionSync = new SelectionSyncService(
                    _host, surfaceTarget, textView, lsClient, registry,
                    PropertiesToolWindowHost.Current, documentUri, _outlineViewModel);

                // Visual Studio's own Toolbox and Properties window (the .Native.cs half).
                AttachNativeWindows(_selectionSync, registry);

                // Initial Outline population (INTEGRATION.md §9 point 6: "Populate it ... once when the
                // pane opens, and again on every debounced buffer change" - the second half is
                // OnPushTimerTick's own job above, now that _viewsSelectionClient/_documentUri are set).
                await RefreshOutlineAsync();
            }
            catch (Exception ex)
            {
                KubunoViewsLogHost.Current.WriteException("[designer] setting up selection sync failed", ex);
            }
        }

        /// <summary>
        /// Polls <see cref="CodeWindowHost.PrimaryView"/> (docs comment: "may not have a primary view
        /// until the shell finishes laying it out") until it is non-null or <paramref name="timeout"/>
        /// elapses - there is no "view ready" event on <c>IVsCodeWindow</c> to await instead. 100 ms
        /// polling interval, matching the general order of magnitude of every other poll in this package
        /// (<see cref="VsTextViewSelectionAdapter"/>'s own 150 ms caret poll).
        /// </summary>
        /// <summary>Polls for the Kubuno Views language client's <c>JsonRpc</c> (250 ms steps, up to <paramref name="timeout"/>, 60 s by default); null when it never attaches or the pane closed meanwhile.</summary>
        private async Task<JsonRpc?> WaitForLanguageClientRpcAsync(TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(60));
            while (!_disposed)
            {
                if (_resolveLanguageClient()?.ReadyRpc is { } rpc)
                {
                    return rpc;
                }

                if (DateTime.UtcNow >= deadline)
                {
                    return null;
                }

                await Task.Delay(250).ConfigureAwait(true);
            }

            return null;
        }

        private async Task<IVsTextView?> WaitForPrimaryViewAsync(TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
            while (DateTime.UtcNow < deadline)
            {
                if (_codeWindowHost.PrimaryView is { } view)
                {
                    return view;
                }

                await Task.Delay(100).ConfigureAwait(true);
            }

            return _codeWindowHost.PrimaryView;
        }

        private void OnEditRequested(object? sender, DesignSurfaceEditRequestedEventArgs e)
        {
            _ = ApplyAsync(e.Op);
        }

        /// <summary>
        /// Synchronously fires <see cref="OnPushTimerTick"/> right now (and stops the timer, exactly as
        /// that handler's own first line already does) when a push is pending - a no-op otherwise. See
        /// <see cref="FlushBeforeSelectTarget"/>'s own doc for why <c>select</c> needs this: it guarantees
        /// the surface's text is never more than one already-in-flight edit behind the id a `select` names,
        /// closing the debounce-vs-immediate-selection gap without adding a second debounce or reworking
        /// <see cref="OnBufferChanged"/>'s own timer.
        /// </summary>
        internal void FlushPendingPush()
        {
            if (!_pushTimer.IsEnabled)
            {
                return;
            }

            OnPushTimerTick(this, EventArgs.Empty);
        }

        private async Task ApplyAsync(DesignSurfaceEditOp op)
        {
            // Explicit switch (mirrors HandlerCreationService's own documented convention): the tail of
            // this method touches the UI-thread-affine ITextBuffer/IVsTextLines, and OnEditRequested
            // already runs on the UI thread (dispatched via Dispatcher.BeginInvoke), but the analyzer
            // cannot see that through an event subscription - an explicit switch is both a real
            // guarantee and what silences VSTHRD010 on TryGetDocumentUri below.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                var client = _resolveLanguageClient();
                var rpc = client?.ReadyRpc;
                if (rpc is null)
                {
                    KubunoViewsLogHost.Current.WriteLine("[designer] kubuno/applyEdit skipped: the Kubuno Views language client is not attached yet.");
                    return;
                }

                var uri = TryGetDocumentUri();
                if (uri is null)
                {
                    KubunoViewsLogHost.Current.WriteLine("[designer] kubuno/applyEdit skipped: could not resolve this document's own URI.");
                    return;
                }

                // Captured BEFORE the round trip: BufferEditApplier/BufferEditCore reject the response if
                // the buffer changed underneath it (docs/DESIGNER.md §2's "reject if the buffer changed
                // since the request snapshot") - a stale response is silently dropped, not applied.
                var baseVersion = _buffer.CurrentSnapshot.Version.VersionNumber;

                var argument = new { uri, op = EncodeOp(op) };
                JToken? result;
                try
                {
                    result = await rpc.InvokeWithParameterObjectAsync<JToken?>(ApplyEditMethod, argument).ConfigureAwait(true);
                }
                catch (Exception ex) when (ex is RemoteInvocationException or ConnectionLostException or OperationCanceledException)
                {
                    KubunoViewsLogHost.Current.WriteException("kubuno/applyEdit RPC call failed", ex);
                    return;
                }

                var edits = ParseEdits(result);
                if (edits.Count == 0)
                {
                    return;
                }

                var request = new ApplyEditRequest(baseVersion, edits);
                var applyResult = new BufferEditApplier(_buffer).Apply(request);
                if (!applyResult.Succeeded)
                {
                    KubunoViewsLogHost.Current.WriteLine($"[designer] kubuno/applyEdit response could not be applied: {applyResult.Outcome}.");
                }
            }
            catch (Exception ex)
            {
                KubunoViewsLogHost.Current.WriteException("[designer] applying a design-surface edit request failed", ex);
            }
        }

        private static object EncodeOp(DesignSurfaceEditOp op) => op.Kind switch
        {
            DesignSurfaceEditOpKind.SetAttribute => new { kind = "setAttribute", elementId = op.ElementId, name = op.Name, value = op.Value },
            DesignSurfaceEditOpKind.RemoveElement => new { kind = "removeElement", elementId = op.ElementId },
            _ => throw new ArgumentOutOfRangeException(nameof(op), op.Kind, "Unknown DesignSurfaceEditOpKind."),
        };

        /// <summary>
        /// <c>kubuno/applyEdit</c>'s result is always <c>{ "edits": [ {range, newText}, ... ] }</c>
        /// (docs/DESIGNER.md's DSG-2 protocol section) - never an error, even for a stale/invalid
        /// element id (an empty array). A malformed/unexpected shape also degrades to an empty list here,
        /// never throws.
        /// </summary>
        private static System.Collections.Generic.IReadOnlyList<TextEditDto> ParseEdits(JToken? result)
        {
            var edits = new System.Collections.Generic.List<TextEditDto>();
            var editsToken = result?["edits"];
            if (editsToken is not JArray array)
            {
                return edits;
            }

            foreach (var edit in array)
            {
                var range = edit["range"];
                var start = range?["start"];
                var end = range?["end"];
                var newText = edit["newText"]?.Value<string>();
                if (start is null || end is null || newText is null)
                {
                    continue;
                }

                var startLine = start["line"]?.Value<int?>();
                var startCharacter = start["character"]?.Value<int?>();
                var endLine = end["line"]?.Value<int?>();
                var endCharacter = end["character"]?.Value<int?>();
                if (startLine is null || startCharacter is null || endLine is null || endCharacter is null)
                {
                    continue;
                }

                var lspRange = new LspRange(new LspPosition(startLine.Value, startCharacter.Value), new LspPosition(endLine.Value, endCharacter.Value));
                edits.Add(new TextEditDto(lspRange, newText));
            }

            return edits;
        }

        /// <summary>The same <c>file:///...</c> shape a VS LSP client normalizes a document's path to
        /// for <c>didOpen</c>/<c>didChange</c> - <c>kubuno-views-ls</c> keys its open-document map by
        /// this exact string (edit_bridge.rs), so a mismatch here degrades to "document not open" (an
        /// empty edits array), never a crash.</summary>
        private string? TryGetDocumentUri()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (_textLines is not Microsoft.VisualStudio.Shell.Interop.IPersistFileFormat persistFileFormat)
                {
                    return null;
                }

                ErrorHandler.ThrowOnFailure(persistFileFormat.GetCurFile(out var fileName, out _));
                return string.IsNullOrEmpty(fileName) ? null : new Uri(fileName).AbsoluteUri;
            }
            catch (COMException)
            {
                return null;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pushTimer.Stop();
            _pushTimer.Tick -= OnPushTimerTick;
            _host.EditRequested -= OnEditRequested;
            if (_host is RustDesignSurfaceHost rustHost)
            {
                rustHost.DragDropEditRequested -= OnDragDropEditRequested;
                rustHost.EditRequestsReceived -= OnEditRequestsReceived;
                rustHost.UnhandledSurfaceKey -= OnUnhandledSurfaceKey;
            }

            _propertiesPublisher?.Dispose();
            _buffer.Changed -= OnBufferChanged;
            _selectionSync?.Dispose();
            _selectionTextView?.Dispose();
        }
    }

    /// <summary>
    /// Wraps a real <see cref="Selection.IDesignSurfaceSelectionTarget"/> to flush the owning
    /// <see cref="DesignSurfaceEditingCoordinator"/>'s pending, debounced buffer-text push (if any)
    /// immediately BEFORE forwarding a <c>select</c> - see <see cref="DesignSurfaceEditingCoordinator
    /// .SetupSelectionSyncAsync"/>'s own comment on why this ordering matters (a `select` id is always
    /// resolved against kubuno-views-ls's undebounced parse, but the surface's own text only follows the
    /// buffer through a 200ms debounce - without this, the two can name "the same id" against two
    /// different document versions). <see langword="null"/> (clearing the selection) is forwarded
    /// unchanged - it never depends on which element is at a given position, so there is nothing to
    /// flush for.
    /// </summary>
    internal sealed class FlushBeforeSelectTarget : Selection.IDesignSurfaceSelectionTarget
    {
        private readonly Selection.IDesignSurfaceSelectionTarget _inner;
        private readonly DesignSurfaceEditingCoordinator _owner;

        public FlushBeforeSelectTarget(Selection.IDesignSurfaceSelectionTarget inner, DesignSurfaceEditingCoordinator owner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public void Select(string? elementId)
        {
            if (elementId != null)
            {
                _owner.FlushPendingPush();
            }

            _inner.Select(elementId);
        }
    }
}
