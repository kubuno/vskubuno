using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.VisualStudio.Commands
{
    /// <summary>
    /// A small, VS-styled name prompt for the five "Ajouter" item-template entries
    /// (<see cref="AddProjectItemCommands"/>). Visual Studio 2026's own "Ajouter &gt; Nouvel
    /// élément..." no longer opens the classic browsable template dialog either - live-verified in
    /// the experimental instance, it is now the same kind of small "type a file name" affordance this
    /// dialog mirrors (<see cref="IContextMenuPattern"/> tree/full-catalog browsing stayed reserved
    /// for the "Ajouter" submenu's item list itself, not the per-template quick-add) - so this keeps
    /// the same modern shape rather than reviving <c>IVsAddProjectItemDlg2</c>'s own legacy template
    /// browser, which was live-verified to never finish loading its template list in this VS build
    /// (stuck on "Chargement des modèles..." even after a cold cache rebuild - see the commit history
    /// for the full investigation). Themed like <see cref="CargoDependencyDialog"/>/<see cref="ProjectReferenceDialog"/>.
    /// </summary>
    internal sealed class NewItemNameDialog : DialogWindow
    {
        private readonly TextBox _nameBox;

        public NewItemNameDialog(string title, string label, string defaultName)
        {
            Title = title;
            Width = 420;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            SetResourceReference(BackgroundProperty, VsBrushes.WindowKey);
            SetResourceReference(ForegroundProperty, VsBrushes.WindowTextKey);

            var grid = new Grid { Margin = new Thickness(12) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var labelBlock = new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6) };
            labelBlock.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.WindowTextKey);
            Grid.SetRow(labelBlock, 0);
            grid.Children.Add(labelBlock);

            _nameBox = new TextBox { Text = defaultName, Margin = new Thickness(0, 0, 0, 12) };
            _nameBox.SetResourceReference(BackgroundProperty, VsBrushes.ComboBoxBackgroundKey);
            _nameBox.SetResourceReference(ForegroundProperty, VsBrushes.WindowTextKey);
            _nameBox.SetResourceReference(BorderBrushProperty, VsBrushes.ComboBoxBorderKey);
            Grid.SetRow(_nameBox, 1);
            grid.Children.Add(_nameBox);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "Ajouter", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 6, 0) };
            var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 80 };
            ok.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(_nameBox.Text))
                {
                    _nameBox.Focus();
                    return;
                }

                DialogResult = true;
            };
            cancel.Click += (_, _) => { DialogResult = false; };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            Grid.SetRow(buttons, 2);
            grid.Children.Add(buttons);

            Content = grid;
            _nameBox.Loaded += (_, _) =>
            {
                _nameBox.Focus();
                _nameBox.SelectAll();
            };
        }

        /// <summary>The entered name, trimmed (never empty - OK is disabled/no-op otherwise).</summary>
        public string EnteredName => _nameBox.Text.Trim();
    }
}
