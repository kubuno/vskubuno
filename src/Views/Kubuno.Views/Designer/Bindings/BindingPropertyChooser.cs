using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Kubuno.Desktop.Designer.UI;

namespace Kubuno.Desktop.Designer.Bindings
{
    /// <summary>
    /// « (Avancé) » of « (DataBindings) »: every property of the element, the bound ones in bold with their expression; OK (or a
    /// double-click) opens « Liaison de données » for the chosen one (docs/DESIGNER.md, "Data bindings").
    /// </summary>
    internal sealed class BindingPropertyChooser : ThemedEditorDialog
    {
        private readonly ListView _list;

        /// <param name="rows">Each property's attribute and its value as written.</param>
        public BindingPropertyChooser(IReadOnlyList<(string Attribute, string? Raw)> rows)
            : base(DesignerText.BindingsAdvancedTitle, 520, 460)
        {
            _list = MakeList();
            foreach (var (attribute, raw) in rows)
            {
                var bound = BindingMarkup.IsMarkupExtension(raw);
                var text = MakeText(bound ? attribute + "   " + raw : attribute);
                text.FontWeight = bound ? FontWeights.Bold : FontWeights.Normal;
                var item = new ListViewItem { Content = text, Tag = attribute };
                item.SetResourceReference(StyleProperty, Microsoft.VisualStudio.Shell.VsResourceKeys.ThemedDialogListViewItemStyleKey);
                item.MouseDoubleClick += (_, _) =>
                {
                    Chosen = attribute;
                    DialogResult = true;
                };
                _list.Items.Add(item);
            }

            if (_list.Items.Count > 0)
            {
                _list.SelectedIndex = 0;
            }

            SetBody(MakeFrame(_list));
            Loaded += (_, _) => _list.Focus();
        }

        /// <summary>The attribute chosen, or null.</summary>
        public string? Chosen { get; private set; }

        protected override bool Accept()
        {
            Chosen = (_list.SelectedItem as ListViewItem)?.Tag as string;
            return Chosen is not null;
        }
    }
}
