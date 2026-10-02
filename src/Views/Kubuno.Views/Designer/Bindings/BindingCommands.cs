using System;
using System.ComponentModel.Design;
using Kubuno.Shared;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Views.Designer.Bindings
{
    /// <summary>
    /// The binding entries of the Properties window's context menu (docs/DESIGNER.md, "Data bindings"): « Créer une
    /// liaison… » on an unbound bindable row, « Modifier la liaison… », « Supprimer la liaison (rétablir la valeur) » and
    /// « Aller à la définition » on a bound one - each visible only where it applies. F12 in the Properties window on a bound
    /// row goes to the definition too (<see cref="GoToDefinitionTarget"/>).
    /// </summary>
    internal static class BindingCommands
    {
        public static void Initialize(OleMenuCommandService commandService)
        {
            Add(commandService, ViewsCommandIds.BindingCreateCommand, BindingStrings.CreateBinding, bound: false, row =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                BindingActions.ShowDialog(row.Element, row.Attribute);
            });
            Add(commandService, ViewsCommandIds.BindingEditCommand, BindingStrings.EditBinding, bound: true, row =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                BindingActions.ShowDialog(row.Element, row.Attribute);
            });
            Add(commandService, ViewsCommandIds.BindingRemoveCommand, BindingStrings.RemoveBinding, bound: true, row => BindingActions.RemoveBinding(row.Element, row.Attribute));
            Add(commandService, ViewsCommandIds.BindingGoToDefinitionCommand, BindingStrings.GoToDefinition, bound: true, row =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                BindingActions.GoToDefinition(row.Element, row.Attribute);
            });
        }

        private static void Add(OleMenuCommandService service, int id, string text, bool bound, Action<(PropertyBrowser.KbviewElementObject Element, string Attribute)> run)
        {
#pragma warning disable VSTHRD010 // OleMenuCommand invoke/query events fire on the UI thread.
            var command = new OleMenuCommand((_, _) =>
            {
                if (PropertiesGridBindings.SelectedRow() is { } row)
                {
                    run((row.Element, row.Attribute));
                }
            }, new CommandID(KubunoGuids.CommandSet, id));
            command.BeforeQueryStatus += (sender, _) =>
            {
                var c = (OleMenuCommand)sender!;
                c.Text = text;
                var row = PropertiesGridBindings.SelectedRow();
                var isBound = row is { } r && BindingMarkup.IsMarkupExtension(r.Element.GetRawValue(r.Attribute));
                c.Visible = c.Enabled = row is not null && isBound == bound;
            };
#pragma warning restore VSTHRD010
            service.AddCommand(command);
        }

        /// <summary>F12 (Edit.GoToDefinition) while the Properties window is active on a bound row: the binding's Rust member.</summary>
        internal sealed class GoToDefinitionTarget : IOleCommandTarget
        {
            private static readonly Guid Std97 = VSConstants.GUID_VSStandardCommandSet97;

            public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (pguidCmdGroup == Std97 && cCmds == 1 && prgCmds[0].cmdID == (uint)VSConstants.VSStd97CmdID.GotoDefn && Applies())
                {
                    prgCmds[0].cmdf = (uint)(OLECMDF.OLECMDF_SUPPORTED | OLECMDF.OLECMDF_ENABLED);
                    return VSConstants.S_OK;
                }

                return (int)Constants.OLECMDERR_E_NOTSUPPORTED;
            }

            public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (pguidCmdGroup == Std97 && nCmdID == (uint)VSConstants.VSStd97CmdID.GotoDefn && Applies() && PropertiesGridBindings.SelectedRow() is { } row)
                {
                    BindingActions.GoToDefinition(row.Element, row.Attribute);
                    return VSConstants.S_OK;
                }

                return (int)Constants.OLECMDERR_E_NOTSUPPORTED;
            }

            private static bool Applies()
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                return PropertiesGridBindings.HasFocus() && PropertiesGridBindings.SelectedRow() is { } row && BindingMarkup.IsMarkupExtension(row.Element.GetRawValue(row.Attribute));
            }
        }
    }
}
