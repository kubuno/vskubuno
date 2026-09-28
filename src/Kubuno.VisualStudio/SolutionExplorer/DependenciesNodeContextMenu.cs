using System;
using System.Collections.Generic;
using System.Windows;
using Kubuno.VisualStudio.Commands;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace Kubuno.VisualStudio.SolutionExplorer
{
    /// <summary>
    /// Shows the Dependencies node's own floating context menu
    /// (<c>KubunoDependenciesNodeContextMenu</c> in <c>KubunoCommands.vsct</c>) via
    /// <see cref="IVsUIShell.ShowContextMenu"/> - the mechanism
    /// <see cref="Microsoft.Internal.VisualStudio.PlatformUI.IContextMenuPattern"/> items
    /// (<see cref="DependenciesTreeItem"/>, <see cref="DependencyTreeItem"/>) use, since neither is a
    /// real <c>IVsHierarchy</c> item with a shell-registered context menu of its own.
    /// </summary>
    internal static class DependenciesNodeContextMenu
    {
        public static IContextMenuController ForAdd(IVsHierarchy hierarchy) => new Controller(hierarchy, crateNameToRemove: null);

        public static IContextMenuController ForRemove(IVsHierarchy hierarchy, string crateName) => new Controller(hierarchy, crateNameToRemove: crateName);

        private sealed class Controller : IContextMenuController
        {
            private readonly IVsHierarchy _hierarchy;
            private readonly string? _crateNameToRemove;

            public Controller(IVsHierarchy hierarchy, string? crateNameToRemove)
            {
                _hierarchy = hierarchy;
                _crateNameToRemove = crateNameToRemove;
            }

            public bool ShowContextMenu(IEnumerable<object> items, Point point)
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                if (ServiceProvider.GlobalProvider.GetService(typeof(SVsUIShell)) is not IVsUIShell uiShell)
                {
                    return false;
                }

                var groupGuid = PackageGuids.KubunoCommandSet;
                var target = new DependenciesNodeCommandTarget(_hierarchy, _crateNameToRemove);
                var points = new[] { new POINTS { x = (short)point.X, y = (short)point.Y } };
                return ErrorHandler.Succeeded(uiShell.ShowContextMenu(0, ref groupGuid, PackageIds.KubunoDependenciesNodeContextMenu, points, target));
            }
        }
    }

    /// <summary>
    /// Routes the two commands of <see cref="DependenciesNodeContextMenu"/>'s floating menu - only
    /// one of "Add"/"Remove" is ever enabled for a given show, matching which
    /// <see cref="Microsoft.Internal.VisualStudio.PlatformUI.IContextMenuPattern"/> node it was shown
    /// for (<see cref="DependenciesTreeItem"/> only ever asks for "Add", <see cref="DependencyTreeItem"/>
    /// only ever asks for "Remove").
    /// </summary>
    internal sealed class DependenciesNodeCommandTarget : IOleCommandTarget
    {
        private readonly IVsHierarchy _hierarchy;
        private readonly string? _crateNameToRemove;

        public DependenciesNodeCommandTarget(IVsHierarchy hierarchy, string? crateNameToRemove)
        {
            _hierarchy = hierarchy;
            _crateNameToRemove = crateNameToRemove;
        }

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            if (pguidCmdGroup != PackageGuids.KubunoCommandSet || prgCmds is null || cCmds == 0)
            {
                return (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
            }

            for (var i = 0; i < cCmds; i++)
            {
                var isAdd = prgCmds[i].cmdID == PackageIds.AddCargoDependencyFromNodeCommand;
                var isRemove = prgCmds[i].cmdID == PackageIds.RemoveCargoDependencyCommand;
                var applicable = (isAdd && _crateNameToRemove is null) || (isRemove && _crateNameToRemove is not null);
                prgCmds[i].cmdf = applicable
                    ? (uint)(OLECMDF.OLECMDF_SUPPORTED | OLECMDF.OLECMDF_ENABLED)
                    : (uint)OLECMDF.OLECMDF_INVISIBLE;
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

            var context = RsprojProjectContext.TryCreate(_hierarchy);
            if (context is null)
            {
                return VSConstants.S_OK;
            }

#pragma warning disable VSSDK007 // deliberately fire-and-forget from IOleCommandTarget.Exec; FileAndForget reports any fault to the "Kubuno" pane instead of dropping it silently.
            if (nCmdID == PackageIds.AddCargoDependencyFromNodeCommand && _crateNameToRemove is null)
            {
                ThreadHelper.JoinableTaskFactory.RunAsync(() => AddCargoDependencyCommand.ShowDialogAndAddAsync(context)).FileAndForget("Kubuno/DependenciesNode/Add");
                return VSConstants.S_OK;
            }

            if (nCmdID == PackageIds.RemoveCargoDependencyCommand && _crateNameToRemove is not null)
            {
                ThreadHelper.JoinableTaskFactory.RunAsync(() => AddCargoDependencyCommand.RemoveAsync(context, _crateNameToRemove)).FileAndForget("Kubuno/DependenciesNode/Remove");
                return VSConstants.S_OK;
            }
#pragma warning restore VSSDK007

            return (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;
        }
    }
}
