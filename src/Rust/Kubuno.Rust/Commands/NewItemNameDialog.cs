using System.Windows;
using System.Windows.Controls;
using Kubuno.VisualStudio.UI;

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
    /// for the full investigation). Themed like Visual Studio's own dialogs (<see cref="ThemedDialog"/>).
    /// </summary>
    internal sealed class NewItemNameDialog : ThemedDialog
    {
        private readonly TextBox _nameBox;

        public NewItemNameDialog(string title, string label, string defaultName)
        {
            Title = title;
            Width = 420;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;

            var grid = new Grid { Margin = new Thickness(12) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var labelBlock = new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6) };
            Grid.SetRow(labelBlock, 0);
            grid.Children.Add(labelBlock);

            _nameBox = new TextBox { Text = defaultName };
            System.Windows.Automation.AutomationProperties.SetName(_nameBox, label);
            Grid.SetRow(_nameBox, 1);
            grid.Children.Add(_nameBox);

            var ok = new Button { Content = "Ajouter", IsDefault = true };
            var cancel = new Button { Content = "Annuler", IsCancel = true };
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
            StackPanel buttons = ThemedControls.ButtonRow(ok, cancel);
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
