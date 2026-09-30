using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Kubuno.VisualStudio.Commands;
using Kubuno.VisualStudio.Core.SolutionExplorer;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace Kubuno.VisualStudio.SolutionExplorer
{
    /// <summary>
    /// Shows the floating context menus of the Dependencies tree (<c>KubunoDependenciesNodeContextMenu</c>
    /// for "Dependencies" and its category nodes, <c>KubunoDependencyItemContextMenu</c> for one
    /// dependency, in <c>KubunoCommands.vsct</c>) via <see cref="IVsUIShell.ShowContextMenu"/> - the
    /// mechanism <see cref="IContextMenuPattern"/> items use, since none of these nodes is a real
    /// <c>IVsHierarchy</c> item with a shell-registered menu. Solution Explorer's own "Scope to This",
    /// "New Solution Explorer View" and "Properties" are placed in the same menus and fall through
    /// <see cref="DependenciesNodeCommandTarget"/> to Solution Explorer itself.
    /// </summary>
    internal static class DependenciesNodeContextMenu
    {
        public static IContextMenuController ForNode(ProjectDependenciesSource owner, DependencyCategory? category) =>
            new Controller(PackageIds.KubunoDependenciesNodeContextMenu, () => new DependenciesNodeCommandTarget(owner, category, item: null));

        public static IContextMenuController ForItem(ProjectDependenciesSource owner, DependencyTreeItem item) =>
            new Controller(PackageIds.KubunoDependencyItemContextMenu, () => new DependenciesNodeCommandTarget(owner, null, item.Item));

        private sealed class Controller : IContextMenuController
        {
            private readonly int _menuId;
            private readonly Func<IOleCommandTarget> _target;

            public Controller(int menuId, Func<IOleCommandTarget> target)
            {
                _menuId = menuId;
                _target = target;
            }

            public bool ShowContextMenu(IEnumerable<object> items, Point point)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (ServiceProvider.GlobalProvider.GetService(typeof(SVsUIShell)) is not IVsUIShell uiShell)
                {
                    return false;
                }

                var groupGuid = PackageGuids.KubunoCommandSet;
                var points = new[] { new POINTS { x = (short)point.X, y = (short)point.Y } };
                return ErrorHandler.Succeeded(uiShell.ShowContextMenu(0, ref groupGuid, _menuId, points, _target()));
            }
        }
    }

    /// <summary>
    /// Answers the state and (VS UI language) text of the Dependencies menus' commands for the node
    /// they were opened on, and runs them (<see cref="DependencyCommands"/>).
    /// </summary>
    internal sealed class DependenciesNodeCommandTarget : IOleCommandTarget
    {
        private readonly ProjectDependenciesSource _owner;
        private readonly DependencyCategory? _category;
        private readonly DependencyItem? _item;

        public DependenciesNodeCommandTarget(ProjectDependenciesSource owner, DependencyCategory? category, DependencyItem? item)
        {
            _owner = owner;
            _category = category;
            _item = item;
        }

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            if (pguidCmdGroup != PackageGuids.KubunoCommandSet || prgCmds is null || cCmds == 0)
            {
                return (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
            }

            for (var i = 0; i < cCmds; i++)
            {
                var (visible, enabled, text) = Status((int)prgCmds[i].cmdID);
                prgCmds[i].cmdf = !visible
                    ? (uint)(OLECMDF.OLECMDF_SUPPORTED | OLECMDF.OLECMDF_INVISIBLE)
                    : (uint)(OLECMDF.OLECMDF_SUPPORTED | (enabled ? OLECMDF.OLECMDF_ENABLED : 0));
                if (i == 0 && text != null)
                {
                    SetCommandText(pCmdText, text);
                }
            }

            return VSConstants.S_OK;
        }

        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (pguidCmdGroup != PackageGuids.KubunoCommandSet)
            {
                return (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
            }

            var (visible, enabled, _) = Status((int)nCmdID);
            if (!visible || !enabled)
            {
                return (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
            }

            var context = RsprojProjectContext.TryCreate(_owner.Project);
            if (context is null)
            {
                return VSConstants.S_OK;
            }

            var item = _item;
            Func<System.Threading.Tasks.Task>? work = null;
            switch ((int)nCmdID)
            {
                case PackageIds.DependenciesAddProjectReferenceCommand:
                    work = () => AddProjectReferenceCommand.ShowReferenceManagerAsync(context);
                    break;
                case PackageIds.DependenciesManageCratesCommand:
                    work = () => CrateManager.CrateManagerToolWindow.ShowAsync(context, crateName: null);
                    break;
                case PackageIds.DependencyManageCommand:
                    work = () => CrateManager.CrateManagerToolWindow.ShowAsync(context, item?.Name);
                    break;
                case PackageIds.DependenciesUpdateAllCommand:
                    work = () => DependencyCommands.UpdateAsync(context, item: null);
                    break;
                case PackageIds.DependenciesRemoveUnusedCommand:
                    work = () => DependencyCommands.RemoveUnusedAsync(context, _owner.Model);
                    break;
                case PackageIds.DependencyUpdateCommand:
                    work = () => DependencyCommands.UpdateAsync(context, item);
                    break;
                case PackageIds.RemoveCargoDependencyCommand:
                    work = () => DependencyCommands.RemoveAsync(context, item!);
                    break;
                case PackageIds.DependencyOpenDocumentationCommand:
                    work = () => DependencyCommands.OpenDocumentationAsync(context, item!);
                    break;
                case PackageIds.DependencyOpenSourceCommand:
                    DependencyCommands.OpenSource(item!);
                    break;
                case PackageIds.DependencyCopyPathCommand:
                    DependencyCommands.CopyPath(item!);
                    break;
                case PackageIds.DependencyOpenFolderCommand:
                    DependencyCommands.OpenFolder(item!);
                    break;
                default:
                    return (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
            }

            if (work != null)
            {
#pragma warning disable VSSDK007 // deliberately fire-and-forget from IOleCommandTarget.Exec; FileAndForget reports any fault instead of dropping it silently.
                ThreadHelper.JoinableTaskFactory.RunAsync(work).FileAndForget("Kubuno/Dependencies/" + nCmdID);
#pragma warning restore VSSDK007
            }

            return VSConstants.S_OK;
        }

        /// <summary>Whether a command shows on this node, whether it is enabled, and its text.</summary>
        private (bool Visible, bool Enabled, string? Text) Status(int id)
        {
            var item = _item;
            var crate = item?.ItemKind == DependencyItemKind.Crate ? item : null;
            var direct = crate != null && !crate.IsTransitive;
            switch (id)
            {
                // "Dependencies" and the category nodes.
                case PackageIds.DependenciesAddProjectReferenceCommand:
                    return (item is null && (_category is null || _category == DependencyCategory.Projects), true, DependenciesText.AddProjectReferenceCommand);
                case PackageIds.DependenciesManageCratesCommand:
                    return (item is null && _category != DependencyCategory.Projects && _category != DependencyCategory.Toolchain, true, DependenciesText.ManageCratesCommand);
                case PackageIds.DependenciesUpdateAllCommand:
                    return (item is null && _category != DependencyCategory.Toolchain, true, DependenciesText.UpdateAllCommand);
                case PackageIds.DependenciesRemoveUnusedCommand:
                    return (item is null && _category is null, true, DependenciesText.RemoveUnusedCommand);

                // One dependency.
                case PackageIds.DependencyOpenDocumentationCommand:
                    return (item != null, true, DependenciesText.OpenDocumentationCommand);
                case PackageIds.DependencyOpenSourceCommand:
                    return (item != null, item?.LibrarySourcePath != null || item?.Directory != null, DependenciesText.OpenSourceCommand);
                case PackageIds.DependencyManageCommand:
                    return (direct && crate!.IsRegistry, true, DependenciesText.ManageCratesCommand);
                case PackageIds.DependencyUpdateCommand:
                    return (crate != null && crate.Category != DependencyCategory.Projects, crate?.Package != null, DependenciesText.UpdateCommand);
                case PackageIds.RemoveCargoDependencyCommand:
                    return (direct, true, DependenciesText.RemoveCommand);
                case PackageIds.DependencyCopyPathCommand:
                    return (item != null, item?.Directory != null, DependenciesText.CopyPathCommand);
                case PackageIds.DependencyOpenFolderCommand:
                    return (item != null, item?.Directory != null && Directory.Exists(item.Directory), DependenciesText.OpenFolderCommand);
                default:
                    return (false, false, null);
            }
        }

        /// <summary>Writes <paramref name="text"/> into an <c>OLECMDTEXT</c> asking for the command's name.</summary>
        private static void SetCommandText(IntPtr pCmdText, string text)
        {
            if (pCmdText == IntPtr.Zero)
            {
                return;
            }

            // OLECMDTEXT: DWORD cmdtextf; ULONG cwActual; ULONG cwBuf; WCHAR rgwz[cwBuf].
            var flags = (uint)Marshal.ReadInt32(pCmdText, 0);
            if ((flags & (uint)OLECMDTEXTF.OLECMDTEXTF_NAME) == 0)
            {
                return;
            }

            var capacity = Marshal.ReadInt32(pCmdText, 8);
            if (capacity <= 0)
            {
                return;
            }

            var count = Math.Min(text.Length, capacity - 1);
            var buffer = IntPtr.Add(pCmdText, 12);
            for (var i = 0; i < count; i++)
            {
                Marshal.WriteInt16(buffer, i * 2, text[i]);
            }

            Marshal.WriteInt16(buffer, count * 2, 0);
            Marshal.WriteInt32(pCmdText, 4, count + 1);
        }
    }
}
