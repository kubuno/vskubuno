using System;
using System.Collections.Generic;
using System.Linq;
using Kubuno.Views.Designer.PropertyBrowser;
using Kubuno.Views.Designer.Registry;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Views.Designer.Bindings
{
    /// <summary>
    /// What the binding UI does to an element (docs/DESIGNER.md, "Data bindings"): read its schema, open the « Liaison de
    /// données » dialog, apply attribute edits (in one dispatcher turn: one undo unit), remove a binding, go to the
    /// definition. Shared by the value cell's drop-down, the Properties window's context menu, « (DataBindings) » and the
    /// collection editor.
    /// </summary>
    public static class BindingActions
    {
        /// <summary>Shows <paramref name="dialog"/> (a method group for callers that only run from a click, on the UI thread).</summary>
        internal static bool Ask(DataBindingDialog dialog)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return dialog.Ask();
        }

        public static IKbviewDesignServices? Services(KbviewElementObject element) => element.Host as IKbviewDesignServices;

        public static BindingSourceSchema Schema(KbviewElementObject element)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return Services(element)?.GetBindingSources(element.ElementId) ?? BindingSourceSchema.Empty;
        }

        public static PropertyMeta? Meta(KbviewElementObject element, string attribute) =>
            element.Component.Properties.FirstOrDefault(p => p.Name == attribute || p.AttributeNames.Contains(attribute));

        /// <summary>The shape the property <paramref name="attribute"/> of <paramref name="element"/> wants.</summary>
        public static BindingShape? Want(KbviewElementObject element, string attribute, PropKind? kind = null) => BindingShapes.Of(Meta(element, attribute), attribute, kind);

        /// <summary>Applies <paramref name="edits"/> (a null value removes the attribute): one undo unit.</summary>
        public static void Apply(KbviewElementObject element, IEnumerable<BindingAttributeEdit> edits)
        {
            foreach (var edit in edits)
            {
                if (edit.Value is null)
                {
                    if (element.GetRawValue(edit.Attribute) is not null)
                    {
                        element.RemoveAttribute(edit.Attribute);
                    }
                }
                else
                {
                    element.SetAttribute(edit.Attribute, edit.Value);
                }
            }
        }

        /// <summary>« Supprimer la liaison (rétablir la valeur) ».</summary>
        public static void RemoveBinding(KbviewElementObject element, string attribute) =>
            Apply(element, BindingEditModel.RemoveBinding(attribute, element.GetRawValue("d:" + attribute)));

        /// <summary>Opens « Liaison de données » for <paramref name="attribute"/>; true when edits were applied.</summary>
        public static bool ShowDialog(KbviewElementObject element, string attribute, PropKind? kind = null)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var schema = Schema(element);
            var services = Services(element);
            var meta = Meta(element, attribute);
            var model = new BindingEditModel(attribute, element.GetRawValue(attribute), element.GetRawValue("d:" + attribute));
            var type = (meta?.Kind ?? kind)?.Tag.ToString() ?? string.Empty;
            Func<string, string?, BindingShape, BindingShape?, BindingPreview>? preview = services is null ? null : services.PreviewBinding;
            var dialog = new DataBindingDialog(model, schema, Want(element, attribute, kind), type, schema.IssuesOf(attribute), preview);
            if (!dialog.Ask() || dialog.Edits.Count == 0)
            {
                return false;
            }

            Apply(element, dialog.Edits);
            return true;
        }

        /// <summary>« Aller à la définition » (F12) of the binding of <paramref name="attribute"/>.</summary>
        public static void GoToDefinition(KbviewElementObject element, string attribute)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Services(element)?.GoToBindingDefinition(element.ElementId, attribute) != true)
            {
                Microsoft.VisualStudio.Shell.Interop.IVsStatusbar? bar = Package.GetGlobalService(typeof(Microsoft.VisualStudio.Shell.Interop.SVsStatusbar)) as Microsoft.VisualStudio.Shell.Interop.IVsStatusbar;
                bar?.SetText(BindingStrings.NoDefinition);
            }
        }
    }
}
