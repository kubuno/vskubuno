using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.VisualStudio.Designer.DesignSurface;
using Kubuno.VisualStudio.Designer.Editing;
using Kubuno.VisualStudio.Designer.Properties;
using Kubuno.VisualStudio.Designer.Registry;
using Kubuno.VisualStudio.Views.Logging;

namespace Kubuno.VisualStudio.Designer.Selection
{
    /// <summary>
    /// DSG-8's C# half (docs/DESIGNER.md §6/§8/§9): keeps the design surface, the XML text view, the
    /// Document Outline and the Properties panel showing the SAME selected element, whichever of the
    /// first three a gesture originated on.
    ///
    /// <b>The three participants and their roles</b> (docs/DESIGNER.md §1/§9): the design surface reports
    /// a selection via <see cref="IDesignSurfaceHost.SelectionChanged"/> and accepts one via
    /// <see cref="IDesignSurfaceSelectionTarget.Select"/> (the DSG-6 protocol's <c>selectionChanged</c>/
    /// <c>select</c> messages); the XML text view reports a caret move via
    /// <see cref="ITextViewSelectionAdapter.CaretMoved"/> and accepts a selection via
    /// <see cref="ITextViewSelectionAdapter.SelectElementRange"/>; the Document Outline (optional -
    /// <see cref="IOutlineSelectionTarget"/> may be <see langword="null"/> when no Outline tool window is
    /// open) only ever ACCEPTS a highlight from here - a user click there is <see cref="SelectFromOutlineAsync"/>'s
    /// own, separate entry point (<c>Outline.OutlineViewModel.NodeActivated</c> is the seam a caller
    /// wires to it, see this library's own INTEGRATION.md). Resolving an id -&gt; range, and a
    /// position -&gt; id, always goes through <c>kubuno-views-ls</c> (<see cref="IViewsSelectionLanguageServerClient"/>,
    /// docs/DESIGNER.md §8's "DSG-2 protocol") - this class never re-implements that resolution itself.
    ///
    /// <b>Selected-element range: the whole element, caret at its start.</b> docs/DESIGNER.md §1 leaves
    /// the exact XML-pane selection shape ("caret to start tag, selection of the start tag or whole
    /// element - pick, document") to this package. Chosen: <see cref="ITextViewSelectionAdapter.SelectElementRange"/>
    /// selects the FULL element range <c>kubuno/rangeOfElement</c>/<c>kubuno/elementAtOffset</c> already
    /// return (start tag through end tag, or the whole self-closing tag) - closer to the XAML designer's
    /// own "select the element's markup" than WinForms' bare caret-jump, and the range this service
    /// already has on hand for every origin (no extra call needed to also highlight just the start tag).
    ///
    /// <b>Loop prevention.</b> A selection that originates on one of the three participants must not
    /// bounce back to that SAME participant as if it were a fresh gesture (docs/DESIGNER.md §8's own
    /// phrasing: "a selection originating from one side must not bounce back"). Two independent guards,
    /// for two different failure modes:
    /// <list type="bullet">
    /// <item>Per-origin skip: <see cref="ApplySelectionAsync"/> (the one method every entry point funnels
    /// through) only ever pushes to the OTHER participants, never the one <paramref name="origin"/>
    /// names - so applying a surface-originated selection to the text view can never itself trigger a
    /// second, redundant push back to the surface, even before considering timing.</item>
    /// <item>Synchronous re-entrancy: <see cref="_applyingRemoteSelection"/> is set for the exact
    /// duration of the one call (<see cref="ITextViewSelectionAdapter.SelectElementRange"/>) that could
    /// plausibly raise <see cref="ITextViewSelectionAdapter.CaretMoved"/> SYNCHRONOUSLY as a side effect
    /// (an in-process COM call, unlike the design surface's own asynchronous, cross-process
    /// <c>selectionChanged</c> echo, which the per-origin skip above already suppresses without needing
    /// this second guard) - belt and braces against a caret-changed re-entry this service never asked for.
    /// </list>
    /// A no-op short circuit (<c>elementId == _currentElementId</c>) also covers the case the two guards
    /// above do not: an asynchronous echo arriving from the SAME participant that just originated the
    /// change (e.g. the design surface's own <c>selectionChanged</c> confirming what <c>select</c> just
    /// told it) is simply "nothing changed" from this service's point of view, so it degrades to a no-op
    /// exactly like an unresolved element id does (docs/DESIGNER.md §8's own "never an error ... degrade
    /// to no-op" rule, mirrored here one layer up).
    ///
    /// Every asynchronous entry point (<see cref="ApplySelectionAsync"/>'s three callers) is fire-and-
    /// forget from its raising event's point of view but never lets an exception escape unobserved - the
    /// same <c>_ = HandleAsync(...)</c> + try/catch-and-log shape
    /// <see cref="Handlers.EventsTabHandlerCreationBridge"/> already establishes for exactly this
    /// situation (a <see langword="void"/> event handler that must kick off async work). Deliberately no
    /// <c>ConfigureAwait(false)</c> anywhere here, for the same reason
    /// <see cref="Handlers.HandlerCreationService"/>'s own doc comment gives: every await's continuation
    /// touches a VS/WPF-thread-affine collaborator (<see cref="ITextViewSelectionAdapter"/>,
    /// <see cref="IDesignSurfaceSelectionTarget"/>, <see cref="PropertiesPanelViewModel"/>) and must
    /// resume on the context it started on.
    ///
    /// Pure orchestration over its five collaborator SEAMS (none of them concrete VS/JsonRpc types) -
    /// unit-tested with fakes (tests/Kubuno.VisualStudio.Designer.Tests/Selection/SelectionSyncServiceTests.cs),
    /// no live VS/JsonRpc/design-surface process needed, mirroring <see cref="Handlers.HandlerCreationService"/>'s
    /// own test-strategy note.
    /// </summary>
    public sealed class SelectionSyncService : IDisposable
    {
        private readonly IDesignSurfaceHost _surfaceHost;
        private readonly IDesignSurfaceSelectionTarget _surfaceTarget;
        private readonly ITextViewSelectionAdapter _textView;
        private readonly IViewsSelectionLanguageServerClient _languageClient;
        private ComponentRegistry _registry;
        private readonly PropertiesPanelViewModel _propertiesPanel;
        private readonly IOutlineSelectionTarget? _outline;
        private readonly string _documentUri;

        private string? _currentElementId;
        private IReadOnlyList<string> _currentElementIds = Array.Empty<string>();
        private bool _applyingRemoteSelection;
        private bool _disposed;

        public SelectionSyncService(
            IDesignSurfaceHost surfaceHost,
            IDesignSurfaceSelectionTarget surfaceTarget,
            ITextViewSelectionAdapter textView,
            IViewsSelectionLanguageServerClient languageClient,
            ComponentRegistry registry,
            PropertiesPanelViewModel propertiesPanel,
            string documentUri,
            IOutlineSelectionTarget? outline = null)
        {
            _surfaceHost = surfaceHost ?? throw new ArgumentNullException(nameof(surfaceHost));
            _surfaceTarget = surfaceTarget ?? throw new ArgumentNullException(nameof(surfaceTarget));
            _textView = textView ?? throw new ArgumentNullException(nameof(textView));
            _languageClient = languageClient ?? throw new ArgumentNullException(nameof(languageClient));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _propertiesPanel = propertiesPanel ?? throw new ArgumentNullException(nameof(propertiesPanel));
            _documentUri = documentUri ?? throw new ArgumentNullException(nameof(documentUri));
            _outline = outline;

            _surfaceHost.SelectionChanged += OnSurfaceSelectionChanged;
            _textView.CaretMoved += OnCaretMoved;
        }

        /// <summary>
        /// Raised after a selection change was applied to every participant (null: nothing selected) -
        /// the designer pane publishes it to Visual Studio's native Properties window from here.
        /// </summary>
        public event EventHandler<string?>? SelectionApplied;

        /// <summary>Uses <paramref name="registry"/> from now on (the project's controls changed, docs/EVENTS.md EVT-7b).</summary>
        public void UpdateRegistry(ComponentRegistry registry) => _registry = registry ?? throw new ArgumentNullException(nameof(registry));

        /// <summary>The currently selected element's stable id, or null.</summary>
        public string? CurrentElementId => _currentElementId;

        /// <summary>
        /// The whole selection (docs/DESIGNER.md §13): the primary (<see cref="CurrentElementId"/>) first, then the other
        /// elements of a multi-selection made on the design surface; empty when nothing is selected.
        /// </summary>
        public IReadOnlyList<string> CurrentElementIds => _currentElementIds;

        /// <summary>
        /// Selects <paramref name="elementId"/> everywhere (surface, XML, Outline, Properties) - used by the
        /// Properties window's element combo box and after a Toolbox insertion. <paramref name="force"/>
        /// re-applies even when the id did not change (an insertion can shift a different element onto the
        /// selected id).
        /// </summary>
        public Task SelectElementAsync(string? elementId, bool force = false)
        {
            if (force)
            {
                _currentElementId = null;
            }

            return RunGuardedAsync(() => ApplySelectionAsync(elementId, range: null, SelectionOrigin.External));
        }

        /// <summary>The Document Outline's own entry point (docs/DESIGNER.md §1: "click -&gt; select in both views") - wired from <c>Outline.OutlineViewModel.NodeActivated</c>, see this library's own INTEGRATION.md.</summary>
        public Task SelectFromOutlineAsync(string? elementId) => RunGuardedAsync(() => ApplySelectionAsync(elementId, range: null, SelectionOrigin.Outline));

        private void OnSurfaceSelectionChanged(object? sender, DesignSurfaceSelectionChangedEventArgs e)
        {
            // The primary comes first; the others are the rest of a multi-selection (docs/DESIGNER.md §13).
            var elementId = e.ElementIds.Count > 0 ? e.ElementIds[0] : null;
            _ = RunGuardedAsync(() => ApplySelectionAsync(elementId, range: null, SelectionOrigin.Surface, e.ElementIds));
        }

        private void OnCaretMoved(object? sender, EventArgs e)
        {
            _ = RunGuardedAsync(HandleCaretMovedAsync);
        }

        private async Task HandleCaretMovedAsync()
        {
            if (_applyingRemoteSelection)
            {
                // Our own SelectElementRange call synchronously moved the caret - not a user gesture.
                return;
            }

            var position = _textView.GetCaretPosition();
            var result = await _languageClient.ElementAtOffsetAsync(_documentUri, position, CancellationToken.None);
            await ApplySelectionAsync(result?.ElementId, result?.Range, SelectionOrigin.TextView);
        }

        /// <summary>
        /// The one place every origin funnels through - see this class's own doc comment for the
        /// loop-prevention contract this method implements. <paramref name="range"/> is already known for
        /// <see cref="SelectionOrigin.TextView"/> (elementAtOffset's own result carries it - no reason to
        /// pay for a second <c>kubuno/rangeOfElement</c> round trip); <see langword="null"/> for the other
        /// two origins, resolved here.
        /// </summary>
        private async Task ApplySelectionAsync(string? elementId, LspRange? range, SelectionOrigin origin, IReadOnlyList<string>? elementIds = null)
        {
            var ids = NormalizeIds(elementId, elementIds);
            if (elementId == _currentElementId)
            {
                // A caret move inside the primary element (e.g. the caret our own SelectElementRange put there) keeps
                // the multi-selection; the same primary with a different set of elements (a Ctrl+click on the surface,
                // or a single-element select collapsing a multi-selection) only updates the set.
                if (origin == SelectionOrigin.TextView || ids.SequenceEqual(_currentElementIds))
                {
                    return;
                }

                _currentElementIds = ids;
                if (origin != SelectionOrigin.Surface)
                {
                    _surfaceTarget.Select(elementId);
                }

                SelectionApplied?.Invoke(this, elementId);
                return;
            }

            _currentElementId = elementId;
            _currentElementIds = ids;

            if (elementId is null)
            {
                if (origin != SelectionOrigin.Surface)
                {
                    _surfaceTarget.Select(null);
                }

                if (origin != SelectionOrigin.Outline)
                {
                    _outline?.Select(null);
                }

                _propertiesPanel.ClearSelection();
                SelectionApplied?.Invoke(this, null);
                return;
            }

            LspRange resolvedRange;
            if (range is { } knownRange)
            {
                resolvedRange = knownRange;
            }
            else
            {
                var fetched = await _languageClient.RangeOfElementAsync(_documentUri, elementId, CancellationToken.None);
                if (fetched is null)
                {
                    // A stale/unresolved id - degrade to "nothing selected" rather than partially syncing
                    // the other views against an id that no longer means anything (docs/DESIGNER.md §8's
                    // "never an error ... degrade to no-op" rule).
                    _currentElementId = null;
                    _currentElementIds = Array.Empty<string>();
                    return;
                }

                resolvedRange = fetched.Value;
            }

            if (origin != SelectionOrigin.TextView)
            {
                _applyingRemoteSelection = true;
                try
                {
                    _textView.SelectElementRange(resolvedRange);
                }
                finally
                {
                    _applyingRemoteSelection = false;
                }
            }

            if (origin != SelectionOrigin.Surface)
            {
                _surfaceTarget.Select(elementId);
            }

            if (origin != SelectionOrigin.Outline)
            {
                _outline?.Select(elementId);
            }

            ApplyPropertiesPanel(elementId);
            SelectionApplied?.Invoke(this, elementId);
        }

        /// <summary>
        /// Feeds <see cref="PropertiesPanelViewModel.SetSelection"/> from a synchronous,
        /// no-round-trip read of the CURRENT buffer text (<see cref="ElementAttributeReader"/>) - see
        /// that class's own doc comment for why this reads the buffer directly instead of a new LS
        /// method. Degrades to <see cref="PropertiesPanelViewModel.ClearSelection"/> for an unreadable
        /// element or an unregistered tag name (not in <see cref="_registry"/>) - never throws.
        ///
        /// <see cref="Registry.EventMeta.Name"/> is already the FULL <c>.kbview</c> attribute name (e.g.
        /// <c>"OnClick"</c>, not a bare <c>"Click"</c>) - verified directly against the real DSG-1 export
        /// fixture (tests/Kubuno.VisualStudio.Designer.Tests/Fixtures/registry.sample.json: every
        /// <c>events[]</c> entry's <c>"name"</c> is already <c>"OnClick"</c>/<c>"OnToggled"</c>/
        /// <c>"OnChanged"</c>), which disagrees with that type's OWN doc comment example
        /// (<c>{ Name = "Click" }</c>) and with <c>Properties.EventRowViewModel.AttributeName</c>'s
        /// <c>"On" + Event.Name</c> computation (which would produce <c>"OnOnClick"</c> against the real
        /// fixture) - flagged in this package's own INTEGRATION.md/report for the Properties owner, not
        /// fixed here ("Properties/: minimal additive changes only" - this file lives in Selection/, not
        /// Properties/). This method looks attribute values up by <c>ev.Name</c> DIRECTLY (the real,
        /// verified convention), so it is correct regardless of that other, pre-existing discrepancy.
        /// </summary>
        private void ApplyPropertiesPanel(string elementId)
        {
            var attributes = ElementAttributeReader.Read(_textView.GetCurrentText(), elementId);
            if (attributes is null)
            {
                _propertiesPanel.ClearSelection();
                return;
            }

            var component = _registry.Find(attributes.TagName);
            if (component is null)
            {
                _propertiesPanel.ClearSelection();
                return;
            }

            var eventHandlers = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var @event in component.Events)
            {
                // The handler may be written under an older alias of the event (OnToggled for OnCheckedChanged).
                eventHandlers[@event.Name] = @event.AttributeNames
                    .Select(n => attributes.Attributes.TryGetValue(n, out var value) ? value : null)
                    .FirstOrDefault(v => v is not null);
            }

            // "Available handler names" enumeration is Events-tab/DSG-10's own concern
            // (`Handlers.EventsTabHandlerCreationBridge`'s own doc comment; docs/DESIGNER.md §1: "a
            // dropdown of handler names already found via the language server, see §3") - out of scope
            // here, so an empty list for now; this library's INTEGRATION.md records it as still open.
            _propertiesPanel.SetSelection(component, attributes.Attributes, eventHandlers, Array.Empty<string>(), _documentUri, elementId);
        }

        /// <summary>The selection list: <paramref name="primary"/> first, then the other ids (duplicates dropped); empty for none.</summary>
        private static IReadOnlyList<string> NormalizeIds(string? primary, IReadOnlyList<string>? ids)
        {
            if (primary is null)
            {
                return Array.Empty<string>();
            }

            var list = new List<string> { primary };
            foreach (var id in ids ?? Array.Empty<string>())
            {
                if (id is not null && !list.Contains(id))
                {
                    list.Add(id);
                }
            }

            return list;
        }

        private static async Task RunGuardedAsync(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                KubunoViewsLogHost.Current.WriteException("DSG-8 selection sync: applying a selection change failed", ex);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _surfaceHost.SelectionChanged -= OnSurfaceSelectionChanged;
            _textView.CaretMoved -= OnCaretMoved;
        }

        private enum SelectionOrigin
        {
            Surface,
            TextView,
            Outline,
            External,
        }
    }
}
