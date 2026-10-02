using System;
using System.ComponentModel.Design;
using System.Windows.Documents;
using System.Windows.Input;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using OleInterop = Microsoft.VisualStudio.OLE.Interop;

namespace Kubuno.Views.Resources.Editor
{
    /// <summary>
    /// The window pane of the resource editor for one <c>.kbres</c> document: hosts <see cref="ResourceEditorView"/>,
    /// owns the <see cref="KbresDocumentSession"/> that keeps the model and the text buffers in step, and routes
    /// Undo, Redo, Delete and Save to the model while it has the focus.
    /// </summary>
    public sealed class KbresEditorPane : WindowPane, IResourceEditorHost
    {
        private readonly ResourceEditorView _view;
        private readonly KbresDocumentSession _session;
        private readonly ServiceProvider _services;
        private readonly string _path;

        public KbresEditorPane(IVsTextLines textBuffer, OleInterop.IServiceProvider oleServiceProvider, string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _path = path;
            _services = new ServiceProvider(oleServiceProvider);
            _view = new ResourceEditorView(this);
            _session = new KbresDocumentSession(textBuffer, path, oleServiceProvider, ShowError);
            _session.ModelReady += (_, _) => _view.SetModel(_session.Model);
            Content = _view;
            _session.Start();
        }

        protected override void Initialize()
        {
            base.Initialize();
            ThreadHelper.ThrowIfNotOnUIThread();
            if (GetService(typeof(IMenuCommandService)) is not OleMenuCommandService commands)
            {
                return;
            }

            // Edit.Undo / Edit.Redo while the editor has the focus: the model's history (a cell being edited keeps the
            // text box's own undo).
            AddCommand(commands, VSConstants.VSStd97CmdID.Undo, () => _view.IsEditingText || _view.CanUndo, () =>
            {
                if (_view.IsEditingText)
                {
                    ApplicationCommands.Undo.Execute(null, Keyboard.FocusedElement);
                }
                else
                {
                    _view.Undo();
                }
            });
            AddCommand(commands, VSConstants.VSStd97CmdID.Redo, () => _view.IsEditingText || _view.CanRedo, () =>
            {
                if (_view.IsEditingText)
                {
                    ApplicationCommands.Redo.Execute(null, Keyboard.FocusedElement);
                }
                else
                {
                    _view.Redo();
                }
            });
            AddCommand(commands, VSConstants.VSStd97CmdID.Delete, () => _view.IsEditingText || _view.HasSelection, () =>
            {
                if (_view.IsEditingText)
                {
                    EditingCommands.Delete.Execute(null, Keyboard.FocusedElement);
                }
                else
                {
                    _view.RemoveSelected();
                }
            });

            // Save: also enabled (and saving every file of the set) when only a culture file has unsaved changes.
            AddCommand(commands, VSConstants.VSStd97CmdID.Save, () => _session.AnyDirty(), _session.SaveAll);
            AddCommand(commands, VSConstants.VSStd97CmdID.SaveProjectItem, () => _session.AnyDirty(), _session.SaveAll);
        }

        private static void AddCommand(OleMenuCommandService commands, VSConstants.VSStd97CmdID id, Func<bool> enabled, Action execute)
        {
            var command = new OleMenuCommand(
                (_, _) =>
                {
                    ThreadHelper.ThrowIfNotOnUIThread();
                    execute();
                },
                new CommandID(VSConstants.GUID_VSStandardCommandSet97, (int)id));
            command.BeforeQueryStatus += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                command.Supported = true;
                command.Enabled = enabled();
            };
            commands.AddCommand(command);
        }

        // ---- IResourceEditorHost ----

        public void OpenFile(string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                VsShellUtilities.OpenDocument(_services, path);
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
            {
                ShowError(ex.Message);
            }
        }

        public void ShowError(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            VsShellUtilities.ShowMessageBox(_services, message, ResourceText.EditorTitle, Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }

        public bool Confirm(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            const int idYes = 6;
            return VsShellUtilities.ShowMessageBox(_services, message, ResourceText.EditorTitle, Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_QUERY, OLEMSGBUTTON.OLEMSGBUTTON_YESNO, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND) == idYes;
        }

        public void ViewCode()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                VsShellUtilities.OpenDocument(_services, _path, VSConstants.LOGVIEWID_Code, out _, out _, out var frame);
                frame?.Show();
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
            {
                ShowError(ex.Message);
            }
        }

        protected override void Dispose(bool disposing)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (disposing)
            {
                _view.SetModel(null);
                _session.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
