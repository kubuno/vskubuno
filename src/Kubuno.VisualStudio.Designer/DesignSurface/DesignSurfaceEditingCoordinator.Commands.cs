using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Kubuno.VisualStudio.Designer.EditorFactory;
using Kubuno.VisualStudio.Designer.Editing;
using Kubuno.VisualStudio.Designer.Editing.Infrastructure;
using Kubuno.VisualStudio.Designer.Registry.Infrastructure;
using Kubuno.VisualStudio.Designer.Selection;
using Kubuno.VisualStudio.Designer.Selection.Infrastructure;
using Kubuno.VisualStudio.Designer.UI;
using Kubuno.VisualStudio.Views.Logging;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using OleInterop = Microsoft.VisualStudio.OLE.Interop;

namespace Kubuno.VisualStudio.Designer.DesignSurface
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
                var model = DesignerMenuModel.Build(GetCurrentText(), e.ElementId, Registry, DesignerClipboard.PeekTag());
                var target = new DesignerContextMenuCommandTarget(model, this);
                // Cascading submenus are routed like any command, to the active pane - which forwards them here.
                ActiveMenuTarget = target;
                if (new ServiceProvider(_oleServiceProvider).GetService(typeof(SVsUIShell)) is not IVsUIShell shell)
                {
                    return;
                }

                var group = DesignerCommandIds.CommandSet;
                var menu = model.IsView ? DesignerCommandIds.ViewContextMenu : DesignerCommandIds.ElementContextMenu;
                var points = new[] { new POINTS { x = (short)e.ScreenX, y = (short)e.ScreenY } };
                ErrorHandler.ThrowOnFailure(shell.ShowContextMenu(0, ref group, menu, points, target));
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException)
            {
                KubunoViewsLogHost.Current.WriteException("[designer] showing the design surface's context menu failed", ex);
            }
        }

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

        public void Copy(string elementId) => Run(() => CopyAsync(elementId), "Copy");

        public void Cut(string elementId)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (elementId.Length == 0)
            {
                ShowStatus(DesignerText.StatusRootNotRemovable);
                return;
            }

            Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (await CopyAsync(elementId))
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    await ApplyEncodedOpsAsync(new object[] { new { kind = "removeElement", elementId } }, "Cut");
                }
            }, "Cut");
        }

        public void Delete(string elementId)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (elementId.Length == 0)
            {
                ShowStatus(DesignerText.StatusRootNotRemovable);
                return;
            }

            RunEdit(new { kind = "removeElement", elementId }, "Delete");
        }

        public void Paste(string? targetId)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var fragment = DesignerClipboard.Read();
            var tag = DesignerFragment.RootTag(fragment);
            if (fragment is null || tag is null)
            {
                ShowStatus(DesignerText.StatusClipboardEmpty);
                return;
            }

            var target = string.IsNullOrEmpty(targetId) ? null : targetId;
            if (DesignerStructurePlanner.PlanPaste(GetCurrentText(), target, tag, Registry) is not { } plan)
            {
                ShowStatus(DesignerText.StatusPasteRefused(tag));
                return;
            }

            InsertFragment(plan, fragment, "Paste");
        }

        public void Duplicate(string elementId)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
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

        /// <summary>Puts <paramref name="elementId"/>'s XML on the clipboard; false when it could not be read.</summary>
        private async Task<bool> CopyAsync(string elementId)
        {
            if (await ReadElementFragmentAsync(elementId) is not { } fragment)
            {
                return false;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            return DesignerClipboard.Write(fragment);
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
