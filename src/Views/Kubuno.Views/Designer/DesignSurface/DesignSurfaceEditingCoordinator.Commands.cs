using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.Desktop.Designer.EditorFactory;
using Kubuno.Desktop.Designer.Editing;
using Kubuno.Desktop.Designer.Editing.Infrastructure;
using Kubuno.Desktop.Designer.Registry.Infrastructure;
using Kubuno.Desktop.Designer.Selection;
using Kubuno.Desktop.Designer.Selection.Infrastructure;
using Kubuno.Desktop.Designer.UI;
using Kubuno.Desktop.Views.Logging;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using OleInterop = Microsoft.VisualStudio.OLE.Interop;

namespace Kubuno.Desktop.Designer.DesignSurface
{
    /// <summary>
    /// The design surface's context menus and keyboard commands (docs/DESIGNER.md §12): a right-click on the
    /// surface arrives as <c>contextMenu</c> (the element already selected), and is shown as a native Visual
    /// Studio context menu (<c>KubunoCommands.vsct</c>) through <see cref="IVsUIShell.ShowContextMenu"/> with a
    /// <see cref="DesignerContextMenuCommandTarget"/>; Ctrl+C/X/V/D arrive as <c>command</c>. Every action that
    /// changes the view is ONE <c>kubuno/applyEdit</c> request (or one batch) applied as one undo unit; a gesture
    /// the registry forbids is refused with a status-bar message instead.
    /// </summary>
    internal sealed partial class DesignSurfaceEditingCoordinator : IDesignerMenuActions
    {
        // ---- Showing the menus ----

        /// <summary>
        /// The command target of the last context menu shown. Visual Studio routes the items of a CASCADING submenu
        /// (Create Handler ›, Select ›, Wrap In ›) through the ordinary command chain - the active pane - rather than
        /// to the target given to <c>ShowContextMenu</c> (found live: their text and state came from the .vsct);
        /// <see cref="EditorFactory.DesignerWindowPane"/> forwards our command set here.
        /// </summary>
        internal DesignerContextMenuCommandTarget? ActiveMenuTarget { get; private set; }

        private void OnContextMenuRequested(object? sender, DesignSurfaceContextMenuEventArgs e) =>
            Run(() => ShowContextMenuAsync(e), "ContextMenu");

        private async Task ShowContextMenuAsync(DesignSurfaceContextMenuEventArgs e)
        {
            await EnsureRegistryAsync();
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_disposed || _oleServiceProvider is null)
            {
                return;
            }

            try
            {
                // A ribbon's "+" glyph ("add") or smart tag ("tasks") - docs/RIBBON.md section 9 - has its own menu.
                var ribbonMenu = e.ElementId is not null && e.Menu is "add" or "tasks";
                var model = ribbonMenu
                    ? DesignerMenuModel.BuildRibbon(GetCurrentText(), e.ElementId!, Registry, DesignerClipboard.PeekTag(), SelectionIds(), addOnly: e.Menu == "add")
                    : DesignerMenuModel.Build(GetCurrentText(), e.ElementId, Registry, DesignerClipboard.PeekTag(), SelectionIds());
                var target = new DesignerContextMenuCommandTarget(model, this);
                // Cascading submenus are routed like any command, to the active pane - which forwards them here.
                ActiveMenuTarget = target;
                if (new ServiceProvider(_oleServiceProvider).GetService(typeof(SVsUIShell)) is not IVsUIShell shell)
                {
                    return;
                }

                var group = DesignerCommandIds.CommandSet;
                var menu = ribbonMenu ? (e.Menu == "add" ? DesignerCommandIds.RibbonAddContextMenu : DesignerCommandIds.RibbonTasksMenu)
                    : model.IsView ? DesignerCommandIds.ViewContextMenu : DesignerCommandIds.ElementContextMenu;
                var points = new[] { new POINTS { x = (short)e.ScreenX, y = (short)e.ScreenY } };
                ErrorHandler.ThrowOnFailure(shell.ShowContextMenu(0, ref group, menu, points, target));
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException)
            {
                KubunoViewsLogHost.Current.WriteException("[designer] showing the design surface's context menu failed", ex);
            }
        }

        /// <summary>
        /// A double-click on an element (docs/EVENTS.md §5.2, like the WinForms designer): creates its DEFAULT event
        /// handler - <c>OnClick</c> for a <c>Button</c>, <c>OnCheckedChanged</c> for a <c>Switch</c>, <c>OnLoad</c> for the
        /// view's root element... - or opens it when the element already has one, through the same
        /// <c>kubuno/createHandler</c> path as the Events tab.
        /// </summary>
        private void OnElementDoubleClicked(object? sender, DesignSurfaceDoubleClickEventArgs e) =>
            Run(async () =>
            {
                await EnsureRegistryAsync();
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (_disposed)
                {
                    return;
                }

                var tag = ElementAttributeReader.Read(GetCurrentText(), e.ElementId)?.TagName;
                var defaultEvent = tag is null ? null : Registry.Find(tag)?.DefaultEventFor(e.ElementId.Length == 0);
                if (defaultEvent is null)
                {
                    return;
                }

                KubunoViewsLogHost.Current.WriteLine($"[designer] double-click on '{e.ElementId}' <{tag}>: default event {defaultEvent.Name}");
                CreateOrShowHandler(e.ElementId, defaultEvent.Name, suggestedName: null);
            }, "DoubleClick");

        private void OnSurfaceCommandRequested(object? sender, DesignSurfaceCommandEventArgs e) =>
            Run(async () =>
            {
                await EnsureRegistryAsync();
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                RunSurfaceCommand(e);
            }, "Command");

        private void RunSurfaceCommand(DesignSurfaceCommandEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var id = e.ElementId;
            switch (e.Command)
            {
                case DesignSurfaceCommand.Copy when id is not null:
                    Copy(id);
                    break;
                case DesignSurfaceCommand.Cut when id is not null:
                    Cut(id);
                    break;
                case DesignSurfaceCommand.Paste:
                    Paste(id);
                    break;
                case DesignSurfaceCommand.Duplicate when id is not null:
                    Duplicate(id);
                    break;
            }
        }

        // ---- IDesignerMenuActions ----

        public void ViewCode()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_oleServiceProvider is null)
            {
                return;
            }

            var group = VSConstants.GUID_VSStandardCommandSet97;
            new DesignerViewSwitchCommandTarget(new ServiceProvider(_oleServiceProvider)).Exec(ref group, (uint)VSConstants.VSStd97CmdID.ViewCode, 0, IntPtr.Zero, IntPtr.Zero);
        }

        public void CreateHandler(string elementId, string eventName) => CreateOrShowHandler(elementId, eventName, suggestedName: null);

        public void Copy(string elementId) => Run(() => CopyAsync(GroupFor(elementId)), "Copy");

        public void Cut(string elementId)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var group = MultiSelection.TopLevel(GroupFor(elementId));
            if (group.Count == 0)
            {
                ShowStatus(DesignerText.StatusRootNotRemovable);
                return;
            }

            Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (await CopyAsync(group))
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    await ApplyEncodedOpsAsync(RemoveOps(group), "Cut");
                }
            }, "Cut");
        }

        public void Delete(string elementId)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var group = MultiSelection.TopLevel(GroupFor(elementId));
            if (group.Count == 0)
            {
                ShowStatus(DesignerText.StatusRootNotRemovable);
                return;
            }

            RunEdit(RemoveOps(group), group.Count == 1 ? "Delete" : "Delete " + group.Count + " controls");
        }

        public void Paste(string? targetId)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var fragment = DesignerClipboard.Read();
            var tags = DesignerFragment.RootTags(fragment);
            if (fragment is null || tags.Count == 0)
            {
                ShowStatus(DesignerText.StatusClipboardEmpty);
                return;
            }

            var target = string.IsNullOrEmpty(targetId) ? null : targetId;
            if (DesignerStructurePlanner.PlanPasteMany(GetCurrentText(), target, tags, Registry) is not { } plan)
            {
                ShowStatus(DesignerText.StatusPasteRefused(string.Join(">, <", tags.Distinct())));
                return;
            }

            InsertFragment(plan, fragment, "Paste");
        }

        public void Duplicate(string elementId)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var group = MultiSelection.TopLevel(GroupFor(elementId));
            if (group.Count > 1)
            {
                // A multi-selection: every copy, in document order, after the last selected element of the
                // primary's container, in ONE insertion (docs/DESIGNER.md §13).
                if (DesignerStructurePlanner.PlanDuplicateMany(GetCurrentText(), group, elementId, Registry) is not { } many)
                {
                    ShowStatus(DesignerText.StatusDuplicateRefused(string.Join(">, <", group.Select(id => ElementAttributeReader.Read(GetCurrentText(), id)?.TagName ?? "?").Distinct())));
                    return;
                }

                Run(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    if (await ReadFragmentsAsync(group) is { } fragments)
                    {
                        await InsertFragmentAsync(many, fragments, "Duplicate");
                    }
                }, "Duplicate");
                return;
            }

            var text = GetCurrentText();
            var tag = ElementAttributeReader.Read(text, elementId)?.TagName ?? string.Empty;
            if (DesignerStructurePlanner.PlanDuplicate(text, elementId, Registry) is not { } plan)
            {
                ShowStatus(elementId.Length == 0 ? DesignerText.StatusRootNotRemovable : DesignerText.StatusDuplicateRefused(tag));
                return;
            }

            Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (await ReadElementFragmentAsync(elementId) is { } fragment)
                {
                    await InsertFragmentAsync(plan, fragment, "Duplicate");
                }
            }, "Duplicate");
        }

        public void SelectElement(string elementId)
        {
            if (_selectionSync is { } sync)
            {
                Run(() => sync.SelectElementAsync(elementId), "Select");
            }
        }

        public void MoveWithinParent(string elementId, int index)
        {
            if (DesignerStructurePlanner.TrySplit(elementId, out var parentId, out _))
            {
                Run(async () =>
                {
                    if (await ApplyEncodedOpsAsync(new object[] { new { kind = "moveElement", elementId, newParentId = parentId, index } }, "Move control"))
                    {
                        await SelectInsertedAsync(StableElementId.Child(parentId, index));
                    }
                }, "Move control");
            }
        }

        public void Wrap(string elementId, string container)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var text = GetCurrentText();
            if (!DesignerStructurePlanner.CanWrap(text, elementId, container, Registry))
            {
                ShowStatus(DesignerText.StatusWrapRefused(ElementAttributeReader.Read(text, elementId)?.TagName ?? "?", container));
                return;
            }

            Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                // The wrapper takes the element's place, hence its id.
                if (await ApplyEncodedOpsAsync(new object[] { new { kind = "wrapElement", elementId, wrapper = container } }, "Wrap in " + container))
                {
                    await SelectInsertedAsync(elementId);
                }
            }, "Wrap");
        }

        public void Unwrap(string elementId)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var text = GetCurrentText();
            if (!DesignerStructurePlanner.CanUnwrap(text, elementId, Registry))
            {
                ShowStatus(DesignerText.StatusUnwrapRefused(ElementAttributeReader.Read(text, elementId)?.TagName ?? "?"));
                return;
            }

            Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                // Its first child takes its place, hence its id.
                if (await ApplyEncodedOpsAsync(new object[] { new { kind = "unwrapElement", elementId } }, "Remove container"))
                {
                    await SelectInsertedAsync(elementId);
                }
            }, "Unwrap");
        }

        public void ShowProperties(string? elementId)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_selectionSync is { } sync)
            {
                Run(() => sync.SelectElementAsync(elementId ?? StableElementId.Root), "Select");
            }

            if (_oleServiceProvider is not null &&
                new ServiceProvider(_oleServiceProvider).GetService(typeof(SVsUIShell)) is IVsUIShell shell)
            {
                var properties = VSConstants.StandardToolWindows.Properties;
                if (ErrorHandler.Succeeded(shell.FindToolWindow((uint)__VSFINDTOOLWIN.FTW_fForceCreate, ref properties, out var frame)) && frame is not null)
                {
                    frame.Show();
                }
            }
        }

        public void EditDesignSize()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var current = DesignSizeInfo.Read(GetCurrentText());
            if (!DesignSizeDialog.TryAsk(current.Width, current.Height, out var width, out var height))
            {
                return;
            }

            SetDesignSize(width, height);
        }

        /// <summary>Writes a new design size (one undo unit): per axis, to the attribute <see cref="DesignSizeInfo"/> says holds it.</summary>
        private void SetDesignSize(double width, double height)
        {
            var current = DesignSizeInfo.Read(GetCurrentText());
            var ops = new List<object>();
            if (Math.Round(width) != Math.Round(current.Width))
            {
                ops.Add(new { kind = "setAttribute", elementId = StableElementId.Root, name = current.WidthAttribute, value = DesignSizeInfo.Format(width) });
            }

            if (Math.Round(height) != Math.Round(current.Height))
            {
                ops.Add(new { kind = "setAttribute", elementId = StableElementId.Root, name = current.HeightAttribute, value = DesignSizeInfo.Format(height) });
            }

            if (ops.Count > 0)
            {
                RunEdit(ops, "Design size");
            }
        }

        // ---- helpers ----

        /// <summary>
        /// Loads the component registry when selection sync has not done it yet (it waits for the XML view, which a
        /// pane showing only its Design tab has not created): the menus and gestures need it to apply the registry rules.
        /// </summary>
        private async Task EnsureRegistryAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_registry is not null || _resolveLanguageClient()?.ReadyRpc is not { } rpc)
            {
                return;
            }

            var registry = await JsonRpcRegistryClient.FetchAsync(rpc, CancellationToken.None);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            _registry ??= registry;
        }

        private void InsertFragment(StructurePlacement plan, string fragment, string description) =>
            Run(() => InsertFragmentAsync(plan, fragment, description), description);

        private async Task InsertFragmentAsync(StructurePlacement plan, string fragment, string description)
        {
            var op = new { kind = "insertFragment", parentId = plan.ParentId, index = plan.Index, xml = fragment };
            if (await ApplyEncodedOpsAsync(new object[] { op }, description))
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                await SelectInsertedAsync(plan.NewElementId);
            }
        }

        /// <summary>Puts the elements' XML on the clipboard (one after the other, in document order); false when one could not be read.</summary>
        private async Task<bool> CopyAsync(IReadOnlyList<string> elementIds)
        {
            if (await ReadFragmentsAsync(elementIds) is not { } fragment)
            {
                return false;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            return DesignerClipboard.Write(fragment);
        }

        /// <summary>The elements' XML as one fragment (<see cref="DesignerFragment.Join"/>, document order), or null when one could not be read.</summary>
        private async Task<string?> ReadFragmentsAsync(IReadOnlyList<string> elementIds)
        {
            var fragments = new List<string>();
            foreach (var id in MultiSelection.InDocumentOrder(elementIds))
            {
                if (await ReadElementFragmentAsync(id) is not { } fragment)
                {
                    return null;
                }

                fragments.Add(fragment);
            }

            return fragments.Count == 0 ? null : DesignerFragment.Join(fragments);
        }

        /// <summary>
        /// The elements a command on <paramref name="elementId"/> applies to: the whole multi-selection when
        /// <paramref name="elementId"/> is part of it (a context menu or a keyboard command on a multi-selection,
        /// docs/DESIGNER.md §13), else the element alone.
        /// </summary>
        private IReadOnlyList<string> GroupFor(string elementId)
        {
            var ids = SelectionIds();
            return ids.Count > 1 && ids.Contains(elementId) ? ids : new[] { elementId };
        }

        /// <summary>The current selection (primary first), empty when selection sync is not up yet.</summary>
        private IReadOnlyList<string> SelectionIds() => _selectionSync?.CurrentElementIds ?? Array.Empty<string>();

        private static IReadOnlyList<object> RemoveOps(IEnumerable<string> elementIds) =>
            elementIds.Select(id => (object)new { kind = "removeElement", elementId = id }).ToList();

        // ---- Layout toolbar / Format menu (docs/DESIGNER.md §13) ----

        private (int Version, string Selection, LayoutSelectionInfo Info)? _layoutCache;

        /// <summary>What the Layout commands can do with the current selection - cached per buffer version and selection (Visual Studio asks on every idle).</summary>
        internal LayoutSelectionInfo CurrentLayout()
        {
            var ids = SelectionIds();
            var key = string.Join("|", ids);
            var version = CurrentVersion;
            if (_layoutCache is { } cache && cache.Version == version && cache.Selection == key)
            {
                return cache.Info;
            }

            var info = _registry is null ? LayoutSelectionInfo.Empty : LayoutSelectionInfo.Build(GetCurrentText(), ids, _selectionSync?.CurrentElementId, Registry);
            _layoutCache = (version, key, info);
            return info;
        }

        /// <summary>Whether a Layout command is enabled (the toolbar buttons, the pane and the context submenus).</summary>
        internal bool IsLayoutCommandEnabled(DesignerLayoutCommand command) => CurrentLayout().IsEnabled(command);

        // ---- Ribbon tasks (docs/RIBBON.md section 9) ----

        /// <summary>"Add" › / a "+" glyph: a new <paramref name="tag"/> (with what its drop gives it) at the end of <paramref name="parentId"/>, then selected.</summary>
        public void AddChild(string parentId, string tag) =>
            Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var plan = Toolbox.ToolboxInsertionPlanner.Plan(GetCurrentText(), parentId, tag, Registry);
                if (plan is null || plan.ParentId != parentId)
                {
                    ShowStatus(DesignerText.StatusPasteRefused(tag));
                    return;
                }

                if (await ApplyEncodedOpsAsync(new object[] { new { kind = "insertChild", parentId = plan.ParentId, index = plan.Index, xml = plan.Xml } }, "Add " + tag, formatInsertion: true))
                {
                    await SelectInsertedAsync(plan.NewElementId);
                }
            }, "RibbonAdd");

        /// <summary>"Create Command from this Button": one undo unit (the new <c>Command</c> and the element's <c>Command</c> attribute).</summary>
        public void CreateCommandFrom(string elementId)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var version = CurrentVersion;
            var edits = Ribbon.RibbonDesignerTasks.PlanCreateCommand(GetCurrentText(), elementId, out var name);
            if (edits.Count > 0)
            {
                ApplyTextEdits(version, edits, "Create Command " + name);
            }
        }

        /// <summary>"Edit Items..." on a ribbon element: its polymorphic collection editor.</summary>
        public void EditCollection(string elementId, Ribbon.RibbonCollection collection)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            PropertyBrowser.KbviewCollectionEditor.Edit(this, this, elementId, collection.Row, collection.Tags);
        }

        /// <summary>"Edit Label..." on a ribbon element: its Label (or Header) in a small input box, one undo unit.</summary>
        public void EditLabel(string elementId, string attribute)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var current = ElementAttributeReader.Read(GetCurrentText(), elementId)?.Attributes is { } attributes && attributes.TryGetValue(attribute, out var value) ? value ?? string.Empty : string.Empty;
            if (Kubuno.Desktop.Resources.Editor.TextPromptDialog.TryAsk(DesignerText.MenuRibbonEditLabel.TrimEnd('.'), attribute + " :", current, null, out var text) && text != current)
            {
                SetAttribute(elementId, attribute, text);
            }
        }

        /// <summary>"Size" › Large / Small on a ribbon element (Small is the default: the attribute goes).</summary>
        public void SetRibbonSize(string elementId, string size)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (size == "Small")
            {
                RemoveAttribute(elementId, "Size");
            }
            else
            {
                SetAttribute(elementId, "Size", size);
            }
        }

        /// <summary>"Choose Icon..." on a ribbon element: the icon picker (docs/ICONS.md) on its icon attribute, one undo unit.</summary>
        public void ChooseIcon(string elementId, string attribute)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var current = ElementAttributeReader.Read(GetCurrentText(), elementId)?.Attributes is { } attributes && attributes.TryGetValue(attribute, out var value) ? value : null;
            var dialog = new Icons.IconPickerDialog(ViewFilePath, current, this);
            if (!dialog.Ask())
            {
                return;
            }

            if (string.IsNullOrEmpty(dialog.Result))
            {
                RemoveAttribute(elementId, attribute);
            }
            else
            {
                SetAttribute(elementId, attribute, dialog.Result!);
            }
        }

        /// <summary>
        /// Runs a Layout command: align/size/spacing/center are computed by the design surface from its painted
        /// layout (<c>format</c> → one <c>editRequests</c> batch); Bring to Front / Send to Back reorder the XML
        /// (<c>reorderChildren</c>), then keep the moved elements selected. One undo unit either way.
        /// </summary>
        public void RunLayoutCommand(DesignerLayoutCommand command)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var info = CurrentLayout();
            if (!info.IsEnabled(command))
            {
                return;
            }

            if (DesignerLayoutCommands.FormatName(command) is { } name)
            {
                if (_host is RustDesignSurfaceHost rustHost)
                {
                    FlushPendingPush();
                    rustHost.Format(name);
                }

                return;
            }

            var order = command == DesignerLayoutCommand.BringToFront ? info.FrontOrder : info.BackOrder;
            if (order is null || info.OrderParentId is not { } parentId)
            {
                return;
            }

            // Where each selected element of that container ends up, to keep it selected.
            var moved = SelectionIds()
                .Select(id => DesignerStructurePlanner.TrySplit(id, out var p, out var index) && p == parentId && order.Contains(index)
                    ? (Old: id, New: StableElementId.Child(parentId, order.ToList().IndexOf(index)))
                    : (Old: id, New: (string?)null))
                .Where(m => m.New is not null)
                .ToList();
            var primary = _selectionSync?.CurrentElementId;
            var newPrimary = moved.FirstOrDefault(m => m.Old == primary).New ?? moved.FirstOrDefault().New;
            Run(async () =>
            {
                if (await ApplyEncodedOpsAsync(new object[] { new { kind = "reorderChildren", parentId, order } }, command == DesignerLayoutCommand.BringToFront ? "Bring to Front" : "Send to Back") &&
                    _host is RustDesignSurfaceHost rustHost && newPrimary is not null)
                {
                    await Task.Delay(350).ConfigureAwait(true);
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    FlushPendingPush();
                    rustHost.SelectMany(moved.Select(m => m.New!).ToList(), newPrimary);
                }
            }, "ZOrder");
        }

        /// <summary>The element's own XML from the current buffer, as a column-0 fragment (<see cref="DesignerFragment.Normalize"/>).</summary>
        private async Task<string?> ReadElementFragmentAsync(string elementId)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var uri = _documentUri ?? TryGetDocumentUri();
            var client = _viewsSelectionClient ??
                (_resolveLanguageClient()?.ReadyRpc is { } rpc ? new JsonRpcViewsSelectionLanguageServerClient(rpc) : null);
            if (client is null || uri is null)
            {
                KubunoViewsLogHost.Current.WriteLine("[designer] copy skipped: the Kubuno Views language client is not attached yet.");
                return null;
            }

            // The language server resolves the id against its own, undebounced copy of the buffer.
            var range = await client.RangeOfElementAsync(uri, elementId, CancellationToken.None);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (range is not { } r)
            {
                return null;
            }

            var text = GetCurrentText();
            var (start, end) = LspPositionMapper.ToOffsetRange(text, r);
            if (start < 0 || end > text.Length || end <= start)
            {
                return null;
            }

            return DesignerFragment.Normalize(text.Substring(start, end - start), DesignerFragment.LinePrefix(text, start));
        }

        private static void Run(Func<Task> action, string description)
        {
#pragma warning disable VSSDK007 // see the constructor's own identical precedent/comment.
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await action();
                }
                catch (Exception ex)
                {
                    KubunoViewsLogHost.Current.WriteException($"[designer] '{description}' failed", ex);
                }
            }).FileAndForget("Kubuno/Designer/" + description);
#pragma warning restore VSSDK007
        }

        /// <summary>Shows <paramref name="message"/> in Visual Studio's status bar (and the Kubuno output pane).</summary>
        private static void ShowStatus(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            KubunoViewsLogHost.Current.WriteLine("[designer] " + message);
            if (Package.GetGlobalService(typeof(SVsStatusbar)) is IVsStatusbar statusBar &&
                ErrorHandler.Succeeded(statusBar.IsFrozen(out var frozen)) && frozen == 0)
            {
                statusBar.SetText(message);
            }
        }
    }
}
