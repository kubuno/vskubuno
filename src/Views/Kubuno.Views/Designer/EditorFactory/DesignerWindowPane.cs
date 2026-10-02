using System;
using System.ComponentModel.Design;
using System.Linq;
using Kubuno.Desktop.Designer.Toolbox;
using Microsoft.VisualStudio.Designer.Interfaces;
using Kubuno.Desktop.Designer.UI;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Threading;
using OleInterop = Microsoft.VisualStudio.OLE.Interop;

namespace Kubuno.Desktop.Designer.EditorFactory
{
    /// <summary>
    /// The <c>IVsWindowPane</c> (via the base <see cref="WindowPane"/>) shown for the Design view of a
    /// <c>.kbview</c> document opened through <see cref="KbviewEditorFactory"/>. All of its actual content -
    /// the Design/XML/Split tab strip and both panes - lives in <see cref="DesignerSplitView"/>; this class
    /// is the thin VS-hosting shell around it, plus the two per-document contracts Visual Studio's own tool
    /// windows query on the active document's pane, like they do on the WinForms designer's:
    /// <list type="bullet">
    /// <item><see cref="IVsToolboxUser"/> - the Toolbox asks it which items to show (only Kubuno components,
    /// <see cref="ToolboxItemFormat"/>) and hands it a double-clicked item (inserted into the selected
    /// container);</item>
    /// <item>the frame's <c>STrackSelection</c> service (reached through <see cref="WindowPane.GetService"/>,
    /// i.e. this pane's site) - where the selected element is published for the Properties window.</item>
    /// </list>
    /// </summary>
    public sealed class DesignerWindowPane : WindowPane, IVsToolboxUser, OleInterop.IOleCommandTarget
    {
        private readonly DesignerSplitView _view;

        public DesignerWindowPane(IVsTextLines textBuffer, OleInterop.IServiceProvider oleServiceProvider, DesignerViewMode initialMode = DesignerViewMode.Design, DesignSurface.DesignSurfaceDocument? document = null)
        {
            _view = new DesignerSplitView(textBuffer, oleServiceProvider, GetTrackSelection, initialMode, EnsureActiveDesigner, document);
            Content = _view;
        }

        /// <summary>
        /// This pane's design surface in Visual Studio's <see cref="System.ComponentModel.Design.DesignSurfaceManager"/>: component-less and
        /// never loaded - it only exists so that, while this document is active, the Properties window
        /// finds an active designer offering <see cref="IEventBindingService"/> and shows its Events tab (⚡).
        /// See <see cref="PropertyBrowser.KbviewEventBindingService"/> for the mechanism. Null when the manager is
        /// unavailable (the tab then simply does not appear).
        /// </summary>
        private System.ComponentModel.Design.DesignSurface? _designSurface;

        protected override void Initialize()
        {
            base.Initialize();
            ThreadHelper.ThrowIfNotOnUIThread();

            // Edit.Undo / Edit.Redo (Ctrl+Z / Ctrl+Y) while the designer - not the XML text view, which
            // handles them itself - has focus: every designer gesture is one text edit, so this is the
            // document's own text undo history, exactly what the XML view would undo.
            if (GetService(typeof(System.ComponentModel.Design.IMenuCommandService)) is OleMenuCommandService commands)
            {
                AddUndoCommand(commands, VSConstants.VSStd97CmdID.Undo, c => c.CanUndo, c => c.Undo());
                AddUndoCommand(commands, VSConstants.VSStd97CmdID.Redo, c => c.CanRedo, c => c.Redo());
            }
            try
            {
                if (GetService(typeof(DesignSurfaceManager)) is DesignSurfaceManager manager)
                {
                    _designSurface = manager.CreateDesignSurface(new ServiceProviderAdapter(this));
                    if (_designSurface?.GetService(typeof(IServiceContainer)) is IServiceContainer services)
                    {
                        services.AddService(typeof(IEventBindingService), PropertyBrowser.KbviewEventBindingService.Instance);
                    }
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
            {
                Kubuno.Desktop.Views.Logging.KubunoViewsLogHost.Current.WriteException("[designer] could not create the pane's design surface (no Events tab in the Properties window)", ex);
            }
        }

        /// <summary>
        /// Makes <see cref="_designSurface"/> the active designer while this pane's frame is the active document -
        /// what <c>VSDesignSurfaceManager</c> does by itself on every document-frame change, except for a
        /// document restored with the solution: its frame becomes active before it is loaded, so the manager
        /// finds no surface then and never looks again (found live: no Events tab until the user switched
        /// documents). Called whenever the selection is published.
        /// </summary>
        private void EnsureActiveDesigner()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_designSurface is null ||
                GetService(typeof(System.ComponentModel.Design.DesignSurfaceManager)) is not System.ComponentModel.Design.DesignSurfaceManager manager ||
                ReferenceEquals(manager.ActiveDesignSurface, _designSurface) ||
                GetService(typeof(SVsShellMonitorSelection)) is not IVsMonitorSelection selection ||
                GetService(typeof(SVsWindowFrame)) is not IVsWindowFrame ownFrame ||
                ErrorHandler.Failed(selection.GetCurrentElementValue((uint)VSConstants.VSSELELEMID.SEID_DocumentFrame, out var active)) ||
                !ReferenceEquals(active, ownFrame))
            {
                return;
            }

            manager.ActiveDesignSurface = _designSurface;
        }

        /// <summary>Answers <c>IVSMDDesigner</c> with <see cref="_designSurface"/> - how Visual Studio's <c>VSDesignSurfaceManager</c> finds the design surface of the active document frame.</summary>
        protected override object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IVSMDDesigner) && _designSurface is not null)
            {
                return _designSurface;
            }

            return base.GetService(serviceType);
        }

        private void AddUndoCommand(OleMenuCommandService commands, VSConstants.VSStd97CmdID id, Func<DesignSurface.DesignSurfaceEditingCoordinator, bool> canExecute, Action<DesignSurface.DesignSurfaceEditingCoordinator> execute)
        {
            var command = new OleMenuCommand(
                (_, _) =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    if (_view.EditingCoordinator is { } coordinator)
                    {
                        execute(coordinator);
                    }
                },
                new CommandID(VSConstants.GUID_VSStandardCommandSet97, (int)id));
            command.BeforeQueryStatus += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                command.Supported = true;
                command.Enabled = _view.EditingCoordinator is { } coordinator && canExecute(coordinator);
            };
            commands.AddCommand(command);
        }

        // ---- IOleCommandTarget: the design surface's context-menu command set ----

        /// <summary>
        /// Re-implemented so the items of the design surface's CASCADING context submenus reach the menu's own command
        /// target: Visual Studio routes them through the ordinary command chain (this pane) instead of the target given
        /// to <c>ShowContextMenu</c>. Everything else goes to the pane's own command service, as before.
        /// </summary>
        int OleInterop.IOleCommandTarget.QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OleInterop.OLECMD[] prgCmds, IntPtr pCmdText)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (pguidCmdGroup == DesignSurface.DesignerCommandIds.CommandSet && _view.EditingCoordinator?.ActiveMenuTarget is { } menu)
            {
                return menu.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText);
            }

            // The Layout toolbar / Format commands (docs/DESIGNER.md §13): VS's own standard commands, enabled
            // from the current selection.
            if (pguidCmdGroup == VSConstants.GUID_VSStandardCommandSet97 && _view.EditingCoordinator is { } coordinator &&
                prgCmds is { Length: > 0 } && cCmds > 0 &&
                prgCmds.Take((int)Math.Min(cCmds, (uint)prgCmds.Length)).All(c => Editing.DesignerLayoutCommands.TryFromStandardCommand(c.cmdID, out _)))
            {
                for (var i = 0; i < cCmds && i < prgCmds.Length; i++)
                {
                    Editing.DesignerLayoutCommands.TryFromStandardCommand(prgCmds[i].cmdID, out var layout);
                    var flags = OleInterop.OLECMDF.OLECMDF_SUPPORTED;
                    if (coordinator.IsLayoutCommandEnabled(layout))
                    {
                        flags |= OleInterop.OLECMDF.OLECMDF_ENABLED;
                    }

                    prgCmds[i].cmdf = (uint)flags;
                }

                return VSConstants.S_OK;
            }

            return GetService(typeof(System.ComponentModel.Design.IMenuCommandService)) is OleInterop.IOleCommandTarget commands
                ? commands.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText)
                : (int)OleInterop.Constants.OLECMDERR_E_NOTSUPPORTED;
        }

        int OleInterop.IOleCommandTarget.Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (pguidCmdGroup == DesignSurface.DesignerCommandIds.CommandSet && _view.EditingCoordinator?.ActiveMenuTarget is { } menu)
            {
                return menu.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
            }

            if (pguidCmdGroup == VSConstants.GUID_VSStandardCommandSet97 && _view.EditingCoordinator is { } coordinator &&
                Editing.DesignerLayoutCommands.TryFromStandardCommand(nCmdID, out var layout))
            {
                if (!coordinator.IsLayoutCommandEnabled(layout))
                {
                    return (int)OleInterop.Constants.OLECMDERR_E_DISABLED;
                }

                coordinator.RunLayoutCommand(layout);
                return VSConstants.S_OK;
            }

            return GetService(typeof(System.ComponentModel.Design.IMenuCommandService)) is OleInterop.IOleCommandTarget commands
                ? commands.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut)
                : (int)OleInterop.Constants.OLECMDERR_E_NOTSUPPORTED;
        }

        /// <summary>Hands <see cref="WindowPane.GetService"/> (protected) to the design surface as its parent provider.</summary>
        private sealed class ServiceProviderAdapter : IServiceProvider
        {
            private readonly DesignerWindowPane _pane;

            public ServiceProviderAdapter(DesignerWindowPane pane) => _pane = pane;

            public object? GetService(Type serviceType) => serviceType == typeof(IVSMDDesigner) ? null : _pane.GetService(serviceType);
        }

        /// <summary>
        /// The XML pane's text view once it has been created (null before the pane is first laid out). Setting
        /// its caret selects the element under it on the design surface, through the designer's own
        /// selection sync - used by Solution Explorer's element nodes (docs/RSPROJ.md lot 8).
        /// </summary>
        public IVsTextView? XmlTextView => _view.XmlTextView;

        /// <summary>Current Design/XML/Split orientation of this window.</summary>
        public DesignerViewMode Mode
        {
            get => _view.Mode;
            set => _view.Mode = value;
        }

        /// <summary>The frame's <c>STrackSelection</c> service (this pane's site), where the Properties window reads the selection.</summary>
        private ITrackSelection? GetTrackSelection()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return GetService(typeof(STrackSelection)) as ITrackSelection;
        }

        /// <summary>S_OK for a Kubuno component (the Toolbox then shows it), S_FALSE for anything else (hidden, or greyed with "Show All").</summary>
        public int IsSupported(OleInterop.IDataObject pDO) =>
            ToolboxDataObjectReader.HasComponent(pDO) ? VSConstants.S_OK : VSConstants.S_FALSE;

        /// <summary>A double-click on a Toolbox item: insert it into the selected container.</summary>
        public int ItemPicked(OleInterop.IDataObject pDO)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!ToolboxDataObjectReader.TryGetComponent(pDO, out var component) || _view.EditingCoordinator is not { } coordinator)
            {
                return VSConstants.S_FALSE;
            }

#pragma warning disable VSSDK007 // no package-owned JoinableTaskFactory here - same reasoning as DesignSurfaceEditingCoordinator.
            ThreadHelper.JoinableTaskFactory.RunAsync(() => coordinator.InsertFromToolboxAsync(component)).FileAndForget("Kubuno/Designer/ToolboxItemPicked");
#pragma warning restore VSSDK007
            if (Package.GetGlobalService(typeof(SVsToolbox)) is IVsToolbox toolbox)
            {
                toolbox.DataUsed();
            }

            return VSConstants.S_OK;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Dispose(true) only comes from ClosePane, which the shell always calls on the UI thread
                // (DesignerSplitView.Dispose asserts it); an unconditional assert here would also fire
                // on a finalizer-driven Dispose(false), hence the suppression instead.
#pragma warning disable VSTHRD010
                _view.Dispose();
#pragma warning restore VSTHRD010
                _designSurface?.Dispose();
                _designSurface = null;
            }

            base.Dispose(disposing);
        }
    }
}
