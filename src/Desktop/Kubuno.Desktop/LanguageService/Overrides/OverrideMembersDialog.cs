using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Kubuno.Desktop.Logic.Overrides;
using Kubuno.Desktop.Designer;
using Kubuno.Core.UI;
using Microsoft.VisualStudio.PlatformUI;

namespace Kubuno.Desktop.LanguageService.Overrides
{
    /// <summary>
    /// "Substituer des membres…" (docs/EVENTS.md EVT-7b): the overridable members of a Kubuno control class, grouped
    /// by level (nearest first), each with its exact signature and what its base behaviour does - Visual Studio's C#
    /// "Override members" dialog for the Kubuno hierarchy. The checked members are written overriding their base.
    /// </summary>
    internal sealed class OverrideMembersDialog : ThemedDialog
    {
        private readonly List<(CheckBox Box, OverridableMember Member)> _boxes = new List<(CheckBox, OverridableMember)>();

        public OverrideMembersDialog(OverrideContext context)
        {
            var french = DesignerText.IsFrench;
            Title = french ? "Substituer des membres" : "Override Members";
            Width = 640;
            Height = 560;
            ResizeMode = ResizeMode.CanResizeWithGrip;

            var root = new DockPanel { Margin = new Thickness(12) };

            var header = new TextBlock
            {
                Text = french
                    ? $"Membres de {string.Join(" → ", context.Chain.Skip(1))} que {context.TypeName} peut substituer (le corps appelle le comportement de base) :"
                    : $"Members of {string.Join(" → ", context.Chain.Skip(1))} that {context.TypeName} can override (the body calls the base behaviour):",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
            };
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            var buttons = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = false };
            var all = new Button { Content = french ? "Tout sélectionner" : "Select All", MinWidth = 110, MinHeight = 23, Margin = new Thickness(0, 0, 7, 0) };
            var none = new Button { Content = french ? "Tout désélectionner" : "Deselect All", MinWidth = 110, MinHeight = 23 };
            var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 75, MinHeight = 23, Margin = new Thickness(0, 0, 7, 0) };
            var cancel = new Button { Content = french ? "Annuler" : "Cancel", IsCancel = true, MinWidth = 75, MinHeight = 23 };
            all.Click += (_, _) => _boxes.ForEach(b => b.Box.IsChecked = true);
            none.Click += (_, _) => _boxes.ForEach(b => b.Box.IsChecked = false);
            ok.Click += (_, _) => DialogResult = true;
            cancel.Click += (_, _) => DialogResult = false;
            DockPanel.SetDock(all, Dock.Left);
            DockPanel.SetDock(none, Dock.Left);
            DockPanel.SetDock(cancel, Dock.Right);
            DockPanel.SetDock(ok, Dock.Right);
            buttons.Children.Add(all);
            buttons.Children.Add(none);
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            var list = new StackPanel();
            foreach (var level in context.Available.Select(m => m.Level).Distinct())
            {
                var levelHeader = new TextBlock { Text = level, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) };
                list.Children.Add(levelHeader);
                foreach (var member in context.Available.Where(m => m.Level == level))
                {
                    var signature = new TextBlock { Text = member.Signature, FontFamily = new FontFamily("Cascadia Mono, Consolas") };
                    TextBlock doc = ThemedControls.SecondaryText(member.LocalizedDoc(french));
                    var content = new StackPanel();
                    content.Children.Add(signature);
                    content.Children.Add(doc);
                    var box = new CheckBox { Content = content, Margin = new Thickness(8, 2, 0, 2), ToolTip = member.BaseCall };
                    _boxes.Add((box, member));
                    list.Children.Add(box);
                }
            }

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list };
            var frame = new Border { BorderThickness = new Thickness(1), Child = scroll, Padding = new Thickness(4) };
            frame.SetResourceReference(Border.BorderBrushProperty, ThemedDialogColors.ListBoxBorderBrushKey);
            frame.SetResourceReference(Border.BackgroundProperty, ThemedDialogColors.ListBoxBrushKey);
            root.Children.Add(frame);
            Content = root;
        }

        /// <summary>The checked members, in the list's order.</summary>
        public IReadOnlyList<OverridableMember> Selected => _boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Member).ToList();

        /// <summary>Checks the members named <paramref name="names"/> (tests, or a preselection).</summary>
        public void Check(IEnumerable<string> names)
        {
            var set = new HashSet<string>(names);
            foreach (var (box, member) in _boxes)
            {
                box.IsChecked = set.Contains(member.Name);
            }
        }
    }
}
