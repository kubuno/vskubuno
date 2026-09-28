using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.VisualStudio.Designer.Editing;
using Kubuno.VisualStudio.Designer.Editing.Infrastructure;
using Kubuno.VisualStudio.Designer.Handlers;
using Kubuno.VisualStudio.Designer.Handlers.Infrastructure;
using Kubuno.VisualStudio.Designer.Outline;
using Kubuno.VisualStudio.Designer.PropertyBrowser;
using Kubuno.VisualStudio.Designer.Registry;
using Kubuno.VisualStudio.Designer.Selection;
using Kubuno.VisualStudio.Designer.Toolbox;
using Kubuno.VisualStudio.Views.Logging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace Kubuno.VisualStudio.Designer.DesignSurface
{
    /// <summary>
    /// The half of <see cref="DesignSurfaceEditingCoordinator"/> that plugs the designer into Visual
    /// Studio's OWN tool windows, like the WinForms designer (docs/DESIGNER.md §11):
    /// <list type="bullet">
    /// <item><b>Properties window (F4)</b> - the selection is published through the pane's
    /// <c>ITrackSelection</c> (<see cref="PropertiesWindowPublisher"/>) as registry-described
    /// <see cref="KbviewElementObject"/>s; this class is their <see cref="IKbviewElementHost"/>, turning a
    /// property edit into <c>kubuno/applyEdit</c> <c>setAttribute</c>/<c>removeAttribute</c> and an Events
    /// tab double-click into <c>kubuno/createHandler</c> (DSG-10);</item>
    /// <item><b>Toolbox</b> - fills Visual Studio's Toolbox once the live registry is known
    /// (<see cref="NativeToolboxInstaller"/>), inserts a double-clicked item into the selected container
    /// (<see cref="InsertFromToolboxAsync"/>), and applies what a drag-and-drop on the surface produces;</item>
    /// <item><b>DSG-9 edits</b> - a Flow reorder/toolbox drop (<c>moveElement</c>/<c>insertChild</c>) and a
    /// move/resize batch, the latter as ONE undo unit (all its <c>setAttribute</c>s are computed against the
    /// same buffer version and applied in a single <c>ITextEdit</c>).</item>
    /// </list>
    /// </summary>
    internal sealed partial class DesignSurfaceEditingCoordinator : IKbviewElementHost
    {
        private static readonly TimeSpan HandlerRequestWindow = TimeSpan.FromSeconds(3);

        private readonly Dictionary<string, DateTime> _handlerRequests = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private PropertiesWindowPublisher? _propertiesPublisher;
        private ComponentRegistry? _registry;
        private IReadOnlyList<(string Id, string Tag)> _outlineElements = Array.Empty<(string, string)>();

        // ---- IKbviewElementHost ----

        public ComponentRegistry Registry => _registry ?? ComponentRegistry.Empty;

        public int CurrentVersion => _buffer.CurrentSnapshot.Version.VersionNumber;

        public string GetCurrentText() => _buffer.CurrentSnapshot.GetText();

        public void SetAttribute(string elementId, string name, string value) =>
            RunEdit(new { kind = "setAttribute", elementId, name, value }, "Set " + name);

        public void RemoveAttribute(string elementId, string name) =>
            RunEdit(new { kind = "removeAttribute", elementId, name }, "Reset " + name);

        public bool IsHandlerRequestRecent(string elementId, string eventName) =>
            _handlerRequests.TryGetValue(elementId + "|" + eventName, out var at) && DateTime.UtcNow - at < HandlerRequestWindow;

        public void CreateOrShowHandler(string elementId, string eventName, string? suggestedName)
        {
            _handlerRequests[elementId + "|" + eventName] = DateTime.UtcNow;
#pragma warning disable VSSDK007 // see the constructor's own identical precedent/comment.
            ThreadHelper.JoinableTaskFactory.RunAsync(() => CreateHandlerAsync(elementId, eventName, suggestedName)).FileAndForget("Kubuno/Designer/CreateHandler");
#pragma warning restore VSSDK007
        }

        // ---- Properties window ----

        /// <summary>Called once selection sync exists (the registry and the language server are live).</summary>
        private void AttachNativeWindows(SelectionSyncService selectionSync, ComponentRegistry registry)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _registry = registry;
            NativeToolboxInstaller.EnsureInstalled(registry);

            if (_trackSelection is not null)
            {
                _propertiesPublisher = new PropertiesWindowPublisher(_trackSelection);
                _propertiesPublisher.ElementPicked += (_, id) => _ = selectionSync.SelectElementAsync(id);
            }

            selectionSync.SelectionApplied += (_, id) => PublishSelection(id);
            PublishSelection(selectionSync.CurrentElementId);
        }

        /// <summary>Shows <paramref name="elementId"/> (the root element when nothing is selected, like WinForms shows the form) in the Properties window.</summary>
        private void PublishSelection(string? elementId)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_propertiesPublisher is null || _disposed)
            {
                return;
            }

            // The Events tab needs this pane's design surface to be Visual Studio's active designer.
            _ensureActiveDesigner?.Invoke();
            var selected = CreateElementObject(elementId ?? string.Empty);
            var selectable = _outlineElements
                .Select(e => CreateElementObject(e.Id, e.Tag))
                .Where(o => o is not null)
                .Cast<KbviewElementObject>()
                .ToList();
            _propertiesPublisher.Publish(selected, selectable);
        }

        private KbviewElementObject? CreateElementObject(string elementId, string? knownTag = null)
        {
            var tag = knownTag ?? ElementAttributeReader.Read(GetCurrentText(), elementId)?.TagName;
            return tag is not null && Registry.Find(tag) is { } component ? new KbviewElementObject(this, elementId, component) : null;
        }

        /// <summary>Keeps the Properties window's element combo box in step with the Outline (called after every outline refresh).</summary>
        private void UpdateSelectableElements(IReadOnlyList<OutlineNode> roots)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var flat = new List<(string Id, string Tag)>();
            void Walk(IEnumerable<OutlineNode> nodes)
            {
                foreach (var node in nodes)
                {
                    flat.Add((node.ElementId, node.Detail ?? node.Name));
                    Walk(node.Children);
                }
            }

            Walk(roots);
            var changed = !flat.SequenceEqual(_outlineElements);
            _outlineElements = flat;
            if (changed)
            {
                PublishSelection(_selectionSync?.CurrentElementId);
            }
            else if (_propertiesPublisher?.Selected is not null)
            {
                PropertiesWindowPublisher.RefreshPropertyBrowser();
            }
        }

        // ---- Toolbox ----

        /// <summary><c>IVsToolboxUser.ItemPicked</c>: inserts <paramref name="component"/> into the selected container (or the nearest one that accepts it), then selects it.</summary>
        internal async Task InsertFromToolboxAsync(string component)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var plan = ToolboxInsertionPlanner.Plan(GetCurrentText(), _selectionSync?.CurrentElementId, component, Registry);
            if (plan is null)
            {
                KubunoViewsLogHost.Current.WriteLine($"[designer] Toolbox: no container in this view accepts a <{component}> here.");
                return;
            }

            if (await ApplyEncodedOpsAsync(new object[] { new { kind = "insertChild", parentId = plan.ParentId, index = plan.Index, xml = plan.Xml } }, "Add " + component, formatInsertion: true))
            {
                await SelectInsertedAsync(plan.NewElementId);
            }
        }

        /// <summary>Tells the Toolbox its selected item was consumed (it goes back to the pointer, like WinForms) - after a drop, whose OLE drop target lives in the surface process itself (docs/DESIGNER.md section 11).</summary>
        private static void MarkToolboxItemUsed()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Package.GetGlobalService(typeof(SVsToolbox)) is IVsToolbox toolbox)
            {
                toolbox.DataUsed();
            }
        }

        // ---- DSG-9 edits from the surface ----

        private void OnDragDropEditRequested(object? sender, DesignSurfaceDragDropEditRequestedEventArgs e)
        {
            var op = e.Op;
            object encoded = op.Kind == DesignSurfaceDragDropOpKind.InsertChild
                ? new { kind = "insertChild", parentId = op.ParentId, index = op.Index ?? 0, xml = op.Xml }
                : new { kind = "moveElement", elementId = op.ElementId, newParentId = op.NewParentId, index = op.Index ?? 0 };
            var description = op.Kind == DesignSurfaceDragDropOpKind.InsertChild ? "Add control" : "Move control";

#pragma warning disable VSSDK007 // see the constructor's own identical precedent/comment.
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (op.Kind == DesignSurfaceDragDropOpKind.InsertChild)
                {
                    MarkToolboxItemUsed();
                }

                if (await ApplyEncodedOpsAsync(new[] { encoded }, description, formatInsertion: op.Kind == DesignSurfaceDragDropOpKind.InsertChild) && op.Kind == DesignSurfaceDragDropOpKind.InsertChild && op.ParentId is not null && op.Index is { } index)
                {
                    await SelectInsertedAsync(op.ParentId.Length == 0 ? index.ToString(CultureInfo.InvariantCulture) : op.ParentId + "." + index.ToString(CultureInfo.InvariantCulture));
                }
            }).FileAndForget("Kubuno/Designer/DragDropEdit");
#pragma warning restore VSSDK007
        }

        private void OnEditRequestsReceived(object? sender, DesignSurfaceEditRequestsReceivedEventArgs e)
        {
            var ops = e.Ops.Select(EncodeOp).ToList();
            RunEdit(ops, e.Gesture == DesignSurfaceGesture.Resize ? "Resize control" : "Move control");
        }

        /// <summary>Selects a just-inserted element once the language server has seen the edit (its <c>didChange</c> trails the buffer by a moment).</summary>
        private async Task SelectInsertedAsync(string elementId)
        {
            await Task.Delay(350).ConfigureAwait(true);
            if (_selectionSync is not null && !_disposed)
            {
                FlushPendingPush();
                await _selectionSync.SelectElementAsync(elementId, force: true);
            }
        }

        // ---- the one edit pipeline ----

        private void RunEdit(object encodedOp, string description) => RunEdit(new[] { encodedOp }, description);

        private void RunEdit(IReadOnlyList<object> encodedOps, string description)
        {
#pragma warning disable VSSDK007 // see the constructor's own identical precedent/comment.
            ThreadHelper.JoinableTaskFactory.RunAsync(() => ApplyEncodedOpsAsync(encodedOps, description)).FileAndForget("Kubuno/Designer/ApplyEdit");
#pragma warning restore VSSDK007
        }

        /// <summary>
        /// Computes every op with <c>kubuno/applyEdit</c> against the SAME buffer version and applies all
        /// the resulting <c>{range, newText}</c> edits as one <c>ITextEdit</c> - one undo unit, whatever the
        /// number of ops (a move/resize batch sets X and Y together). A buffer that changed during the round
        /// trips makes the whole batch stale: nothing is applied (docs/DESIGNER.md §2). Never throws.
        /// </summary>
        private async Task<bool> ApplyEncodedOpsAsync(IReadOnlyList<object> encodedOps, string description, bool formatInsertion = false)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            try
            {
                var rpc = _resolveLanguageClient()?.ReadyRpc;
                var uri = TryGetDocumentUri();
                if (rpc is null || uri is null)
                {
                    KubunoViewsLogHost.Current.WriteLine($"[designer] '{description}' skipped: the Kubuno Views language client or this document's URI is not available yet.");
                    return false;
                }

                var baseVersion = _buffer.CurrentSnapshot.Version.VersionNumber;
                var edits = new List<TextEditDto>();
                foreach (var op in encodedOps)
                {
                    JToken? result;
                    try
                    {
                        result = await rpc.InvokeWithParameterObjectAsync<JToken?>(ApplyEditMethod, new { uri, op }).ConfigureAwait(true);
                    }
                    catch (Exception ex) when (ex is RemoteInvocationException or ConnectionLostException or OperationCanceledException)
                    {
                        KubunoViewsLogHost.Current.WriteException("kubuno/applyEdit RPC call failed", ex);
                        return false;
                    }

                    edits.AddRange(ParseEdits(result));
                }

                if (edits.Count == 0)
                {
                    KubunoViewsLogHost.Current.WriteLine($"[designer] '{description}': kubuno/applyEdit returned no edit (stale element id?).");
                    return false;
                }

                if (formatInsertion && edits.Count == 1)
                {
                    edits[0] = FormatInsertion(edits[0]);
                }

                var applyResult = new BufferEditApplier(_buffer).Apply(new ApplyEditRequest(baseVersion, edits));
                if (!applyResult.Succeeded)
                {
                    KubunoViewsLogHost.Current.WriteLine($"[designer] '{description}' could not be applied: {applyResult.Outcome}.");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                KubunoViewsLogHost.Current.WriteException($"[designer] '{description}' failed", ex);
                return false;
            }
        }

        // ---- Undo / Redo from the designer (Ctrl+Z / Ctrl+Y while the surface or the pane has focus) ----

        /// <summary>The document's undo history (the text buffer's - every designer gesture is a text edit), or null.</summary>
        private Microsoft.VisualStudio.Text.Operations.ITextUndoHistory? UndoHistory()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_oleServiceProvider is null ||
                new ServiceProvider(_oleServiceProvider).GetService(typeof(Microsoft.VisualStudio.ComponentModelHost.SComponentModel)) is not Microsoft.VisualStudio.ComponentModelHost.IComponentModel componentModel)
            {
                return null;
            }

            var registry = componentModel.GetService<Microsoft.VisualStudio.Text.Operations.ITextUndoHistoryRegistry>();
            return registry is not null && registry.TryGetHistory(_buffer, out var history) ? history : null;
        }

        internal bool CanUndo
        {
            get
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                return UndoHistory()?.CanUndo == true;
            }
        }

        internal bool CanRedo
        {
            get
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                return UndoHistory()?.CanRedo == true;
            }
        }

        internal void Undo()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var history = UndoHistory();
            if (history?.CanUndo == true)
            {
                history.Undo(1);
            }
        }

        private void OnUnhandledSurfaceKey(object? sender, DesignSurfaceKeyEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (e.IsUndo)
            {
                Undo();
            }
            else if (e.IsRedo)
            {
                Redo();
            }
        }

        internal void Redo()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var history = UndoHistory();
            if (history?.CanRedo == true)
            {
                history.Redo(1);
            }
        }

        /// <summary>A pure insertion of a new element, re-indented onto its own line (<see cref="InsertChildFormatter"/>).</summary>
        private TextEditDto FormatInsertion(TextEditDto edit)
        {
            var snapshot = _buffer.CurrentSnapshot;
            if (!edit.Range.Start.Equals(edit.Range.End) || edit.Range.Start.Line >= snapshot.LineCount)
            {
                return edit;
            }

            var line = snapshot.GetLineFromLineNumber(edit.Range.Start.Line);
            var text = line.GetText();
            var column = Math.Min(edit.Range.Start.Character, text.Length);
            string? previous = null;
            for (var number = line.LineNumber - 1; number >= 0 && previous is null; number--)
            {
                var candidate = snapshot.GetLineFromLineNumber(number).GetText();
                if (candidate.Trim().Length > 0)
                {
                    previous = candidate;
                }
            }

            var newLine = line.GetLineBreakText();
            if (string.IsNullOrEmpty(newLine))
            {
                newLine = line.LineNumber > 0 ? snapshot.GetLineFromLineNumber(line.LineNumber - 1).GetLineBreakText() : Environment.NewLine;
            }

            var formatted = InsertChildFormatter.Format(text.Substring(0, column), text.Substring(column), previous, edit.NewText, newLine);
            return new TextEditDto(edit.Range, formatted);
        }

        // ---- DSG-10 ----

        private async Task CreateHandlerAsync(string elementId, string eventName, string? suggestedName)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var rpc = _resolveLanguageClient()?.ReadyRpc;
            var uri = TryGetDocumentUri();
            if (rpc is null || uri is null || _oleServiceProvider is null)
            {
                KubunoViewsLogHost.Current.WriteLine("[designer] kubuno/createHandler skipped: the Kubuno Views language client is not attached yet.");
                return;
            }

            // Flush first: kubuno-views-ls resolves the element id against its own (undebounced) copy of
            // the buffer, and so must the surface when it later selects the element.
            FlushPendingPush();
            var documentHost = new PaneHandlerDocumentHost(uri, new BufferEditApplier(_buffer), new VsHandlerDocumentHost(new ServiceProvider(_oleServiceProvider)));
            var service = new HandlerCreationService(new JsonRpcKubunoViewsLanguageServerClient(rpc), documentHost);
            try
            {
                var result = await service.CreateAsync(new CreateHandlerRequest(uri, elementId, eventName, suggestedName), CancellationToken.None);
                KubunoViewsLogHost.Current.WriteLine($"[designer] kubuno/createHandler '{eventName}' on '{elementId}': {result.Outcome} ({result.HandlerName}).");
                if (result.Outcome == HandlerCreationOutcome.NothingToDo)
                {
                    // An already-bound event whose handler the server's `fn` search did not find - e.g. a
                    // closure registered directly in the `handlers!` table. Still show the code, like
                    // WinForms: the handler's first mention in the code-behind file.
                    NavigateToHandlerMention(uri, ElementAttributeReader.Read(GetCurrentText(), elementId)?.Attributes.TryGetValue(eventName, out var bound) == true ? bound : null, documentHost);
                }
            }
            catch (Exception ex)
            {
                KubunoViewsLogHost.Current.WriteException("[designer] kubuno/createHandler failed", ex);
            }
        }

        /// <summary>Opens the code-behind <c>.rs</c> (same folder, same stem) at <c>fn name</c>, else at the name's first mention.</summary>
        private static void NavigateToHandlerMention(string kbviewUri, string? handlerName, IHandlerDocumentHost documentHost)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (string.IsNullOrEmpty(handlerName))
            {
                return;
            }

            var codeBehind = System.IO.Path.ChangeExtension(new Uri(kbviewUri).LocalPath, ".rs");
            if (!System.IO.File.Exists(codeBehind))
            {
                return;
            }

            var lines = System.IO.File.ReadAllLines(codeBehind);
            int Find(string needle) => Array.FindIndex(lines, l => l.IndexOf(needle, StringComparison.Ordinal) >= 0);
            var line = Find("fn " + handlerName + "(");
            if (line < 0)
            {
                line = Find("\"" + handlerName + "\"");
            }

            if (line >= 0)
            {
                var column = Math.Max(0, lines[line].IndexOf(handlerName!, StringComparison.Ordinal));
                documentHost.NavigateTo(new Uri(codeBehind).AbsoluteUri, new LspPosition(line, column));
            }
        }

        /// <summary>
        /// Routes <see cref="HandlerCreationService"/>'s <c>.kbview</c> edit to THIS pane's own buffer (the
        /// document is open in the designer, not in a text view <see cref="VsHandlerDocumentHost"/> could
        /// find), and everything else (the code-behind <c>.rs</c>) to the regular document host.
        /// </summary>
        private sealed class PaneHandlerDocumentHost : IHandlerDocumentHost
        {
            private readonly string _kbviewPath;
            private readonly IEditableTextBuffer _kbviewBuffer;
            private readonly IHandlerDocumentHost _fallback;

            public PaneHandlerDocumentHost(string kbviewUri, IEditableTextBuffer kbviewBuffer, IHandlerDocumentHost fallback)
            {
                _kbviewPath = new Uri(kbviewUri).LocalPath;
                _kbviewBuffer = kbviewBuffer;
                _fallback = fallback;
            }

            public IEditableTextBuffer OpenBuffer(string fileUri) => IsKbview(fileUri) ? _kbviewBuffer : _fallback.OpenBuffer(fileUri);

            public void NavigateTo(string fileUri, LspPosition position)
            {
                if (!IsKbview(fileUri))
                {
                    _fallback.NavigateTo(fileUri, position);
                }
            }

            private bool IsKbview(string fileUri)
            {
                try
                {
                    return string.Equals(new Uri(fileUri).LocalPath, _kbviewPath, StringComparison.OrdinalIgnoreCase);
                }
                catch (UriFormatException)
                {
                    return false;
                }
            }
        }
    }
}
