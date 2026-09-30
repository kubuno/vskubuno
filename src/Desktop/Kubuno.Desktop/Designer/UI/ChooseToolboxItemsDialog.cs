using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Kubuno.Desktop.Designer.Registry;
using Kubuno.Core.UI;
using Microsoft.VisualStudio.PlatformUI;

namespace Kubuno.Desktop.Designer.UI
{
    /// <summary>
    /// "Choisir des éléments…" (docs/EVENTS.md EVT-7b, WinForms' "Choose Toolbox Items"): the controls the project's
    /// other crates declare (compiled into its last design build), each with a check box; the checked ones join the
    /// Toolbox's "&lt;Project&gt; Composants" tab.
    /// </summary>
    internal sealed class ChooseToolboxItemsDialog : ThemedDialog
    {
        private readonly List<(CheckBox Box, string Key)> _boxes = new List<(CheckBox, string)>();

        public ChooseToolboxItemsDialog(IReadOnlyList<ComponentMeta> components, ISet<string> chosen)
        {
            var french = DesignerText.IsFrench;
            Title = french ? "Choisir des éléments de la boîte à outils" : "Choose Toolbox Items";
            Width = 560;
            Height = 460;
            ResizeMode = ResizeMode.CanResizeWithGrip;

            var root = new DockPanel { Margin = new Thickness(12) };
            var header = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
                Text = components.Count == 0
                    ? (french
                        ? "Aucun contrôle d'une autre crate du projet : ajoutez une crate de contrôles à ses dépendances (Cargo.toml), puis générez le projet."
                        : "No control from another crate of the project: add a control crate to its dependencies (Cargo.toml), then build the project.")
                    : (french
                        ? "Contrôles des autres crates du projet (après sa dernière génération). Les éléments cochés apparaissent dans l'onglet du projet de la boîte à outils ; l'application doit lier leur crate (use <crate> as _;)."
                        : "Controls of the project's other crates (as of its last build). The checked ones appear in the project's Toolbox tab; the application must link their crate (use <crate> as _;)."),
            };
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            var ok = new Button { Content = "OK", IsDefault = true };
            var cancel = new Button { Content = french ? "Annuler" : "Cancel", IsCancel = true };
            ok.Click += (_, _) => DialogResult = true;
            cancel.Click += (_, _) => DialogResult = false;
            StackPanel buttons = ThemedControls.ButtonRow(ok, cancel);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            var list = new StackPanel();
            foreach (var group in components.GroupBy(c => c.CrateName ?? "?").OrderBy(g => g.Key))
            {
                var crate = new TextBlock { Text = group.Key, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2) };
                list.Children.Add(crate);
                foreach (var component in group.OrderBy(c => c.Name))
                {
                    var key = ProjectComponentsFile.ChoiceKey(component);
                    var box = new CheckBox
                    {
                        Content = component.Name + (string.IsNullOrEmpty(component.LocalizedDoc) ? string.Empty : "  —  " + component.LocalizedDoc),
                        IsChecked = chosen.Contains(key),
                        Margin = new Thickness(8, 2, 0, 2),
                    };
                    _boxes.Add((box, key));
                    list.Children.Add(box);
                }
            }

            var frame = new Border { BorderThickness = new Thickness(1), Padding = new Thickness(4), Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list } };
            frame.SetResourceReference(Border.BorderBrushProperty, ThemedDialogColors.ListBoxBorderBrushKey);
            frame.SetResourceReference(Border.BackgroundProperty, ThemedDialogColors.ListBoxBrushKey);
            root.Children.Add(frame);
            Content = root;
        }

        /// <summary>The checked controls' keys (<c>crate::Name</c>).</summary>
        public IReadOnlyList<string> Chosen => _boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Key).ToList();
    }
}
