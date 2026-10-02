using System;
using System.ComponentModel;
using System.Drawing.Design;
using System.Linq;
using Kubuno.Desktop.Designer.PropertyBrowser;
using Kubuno.Desktop.Designer.Selection;
using Kubuno.Desktop.Views.Logging;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Desktop.Designer.Ribbon
{
    /// <summary>
    /// A tab's <c>ScalingPolicy</c> as a Properties-window row (docs/RIBBON.md section 5): "(Collection)", edited with the
    /// collection editor - one <c>Scale</c> step per line, its group and the size it brings the group to, in order.
    /// </summary>
    public sealed class RibbonScalingPolicyDescriptor : PropertyDescriptor
    {
        public RibbonScalingPolicyDescriptor(string category)
            : base("ScalingPolicy", new Attribute[]
            {
                new CategoryAttribute(category),
                new DescriptionAttribute(DesignerText.IsFrench
                    ? "Les étapes appliquées dans l'ordre quand l'onglet ne tient pas en largeur : chacune réduit un groupe (Medium, Small, puis Collapsed). Sans étape : le groupe le plus à droite d'abord."
                    : "The steps applied in order while the tab does not fit: each shrinks one group (Medium, Small, then Collapsed). Without steps: the right-most group first."),
                new DisplayNameAttribute("ScalingPolicy"),
                new RefreshPropertiesAttribute(RefreshProperties.Repaint),
            })
        {
        }

        public override Type ComponentType => typeof(KbviewElementObject);

        public override bool IsReadOnly => false;

        public override Type PropertyType => typeof(string);

        public override object? GetEditor(Type editorBaseType) => editorBaseType == typeof(UITypeEditor) ? new RibbonScalingPolicyEditor() : base.GetEditor(editorBaseType);

        public override object GetValue(object? component) => DesignerText.CollectionValue;

        public override void SetValue(object? component, object? value)
        {
        }

        public override bool CanResetValue(object component) => false;

        public override void ResetValue(object component)
        {
        }

        public override bool ShouldSerializeValue(object component) =>
            component is KbviewElementObject element && ViewDocument.Find(ViewDocument.Parse(element.Host.GetCurrentText()), element.ElementId) is { } tab && RibbonScalingPolicy.Steps(tab).Count > 0;
    }

    /// <summary>The editor of <see cref="RibbonScalingPolicyDescriptor"/>.</summary>
    public sealed class RibbonScalingPolicyEditor : UITypeEditor
    {
        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context) =>
            RichEditors.Elements(context).Count == 1 ? UITypeEditorEditStyle.Modal : UITypeEditorEditStyle.None;

        public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider? provider, object? value)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var elements = RichEditors.Elements(context);
            if (elements.Count == 1 && elements[0].Host is IKbviewDesignServices services)
            {
                Edit(elements[0].Host, services, elements[0].ElementId, provider);
            }

            return value;
        }

        /// <summary>Opens the editor on tab <paramref name="tabId"/> and applies the new policy as ONE undo unit.</summary>
        public static void Edit(IKbviewElementHost host, IKbviewDesignServices services, string tabId, IServiceProvider? provider = null)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var version = host.CurrentVersion;
            var text = host.GetCurrentText();
            if (ViewDocument.Find(ViewDocument.Parse(text), tabId) is not { } tab)
            {
                return;
            }

            var groups = RibbonScalingPolicy.GroupNames(tab);
            var originals = RibbonScalingPolicy.Steps(tab);
            var dialog = new UI.CollectionEditorDialog("ScalingPolicy", RibbonScalingPolicy.StepTag, RibbonScalingPolicy.StepMeta(groups), originals);
            if (!RichEditors.ShowDialog(provider, dialog))
            {
                return;
            }

            var steps = RibbonScalingPolicy.Merge(originals, dialog.Members);
            foreach (var problem in RibbonScalingPolicy.Diagnostics(steps, groups))
            {
                KubunoViewsLogHost.Current.WriteLine("[designer] ScalingPolicy: " + problem);
            }

            var edits = RibbonScalingPolicy.Plan(text, tabId, steps);
            if (edits.Count > 0)
            {
                services.ApplyTextEdits(version, edits, "Edit ScalingPolicy");
            }
        }
    }
}
