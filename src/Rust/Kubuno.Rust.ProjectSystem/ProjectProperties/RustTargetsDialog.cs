using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Kubuno.Core.UI;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Rust.ProjectSystem.ProjectProperties
{
    /// <summary>One rustup target in <see cref="RustTargetsDialog"/>.</summary>
    internal sealed class RustTargetRow : INotifyPropertyChanged
    {
        private bool _isChecked;

        public RustTargetRow(string target, bool installed)
        {
            Target = target;
            Installed = _isChecked = installed;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Target { get; }

        public bool Installed { get; }

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
                }
            }
        }

        public override string ToString() => Target;
    }

    /// <summary>
    /// "Install other targets..." of the Application page (docs/RSPROJ.md, "Project properties like .NET"), the
    /// counterpart of .NET's "Install other frameworks" link: a filterable checked list of every target rustup
    /// knows, installed ones checked; Apply runs <c>rustup target add</c> / <c>remove</c> for the changes. Themed
    /// like Visual Studio's own dialogs, UI built in code (this assembly has no XAML build).
    /// </summary>
    internal sealed class RustTargetsDialog : ThemedDialog
    {
        private readonly ObservableCollection<RustTargetRow> _rows = new ObservableCollection<RustTargetRow>();
        private readonly ListView _list;
        private readonly TextBox _filter;
        private readonly TextBlock _status;
        private readonly Button _apply;

        public RustTargetsDialog()
        {
            Title = PropertiesText.TargetsTitle;
            Width = 560;
            Height = 520;
            MinWidth = 420;
            MinHeight = 320;
            ResizeMode = ResizeMode.CanResizeWithGrip;

            var root = new Grid { Margin = new Thickness(12) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new TextBlock { Text = PropertiesText.TargetsHeader, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            _filter = new TextBox();
            _filter.TextChanged += (s, e) => CollectionViewSource.GetDefaultView(_rows).Refresh();
            Grid filterBox = ThemedControls.WithPlaceholder(_filter, PropertiesText.TargetsFilter);
            filterBox.Margin = new Thickness(0, 0, 0, 8);
            Grid.SetRow(filterBox, 1);
            root.Children.Add(filterBox);

            var check = new FrameworkElementFactory(typeof(CheckBox));
            check.SetBinding(ToggleButton_IsChecked, new Binding(nameof(RustTargetRow.IsChecked)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            check.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty, new Binding(nameof(RustTargetRow.Target)));
            var view = new GridView();
            view.Columns.Add(new GridViewColumn { Header = PropertiesText.InstalledColumn, Width = 70, CellTemplate = new DataTemplate { VisualTree = check } });
            view.Columns.Add(new GridViewColumn { Header = PropertiesText.TargetColumn, Width = 440, DisplayMemberBinding = new Binding(nameof(RustTargetRow.Target)) });
            _list = ThemedControls.GridListView(view);
            _list.ItemsSource = _rows;
            ICollectionView collection = CollectionViewSource.GetDefaultView(_rows);
            collection.Filter = item => _filter.Text.Length == 0 || ((RustTargetRow)item).Target.IndexOf(_filter.Text, System.StringComparison.OrdinalIgnoreCase) >= 0;
            Grid.SetRow(_list, 2);
            root.Children.Add(_list);

            _status = ThemedControls.SecondaryText(PropertiesText.TargetsLoading);
            _status.Margin = new Thickness(0, 8, 0, 0);
            Grid.SetRow(_status, 3);
            root.Children.Add(_status);

            _apply = new Button { Content = PropertiesText.TargetsApply, IsEnabled = false };
            _apply.Click += async (s, e) => await ApplyAsync();
            var close = new Button { Content = PropertiesText.Close, IsCancel = true };
            StackPanel buttons = ThemedControls.ButtonRow(_apply, close);
            Grid.SetRow(buttons, 4);
            root.Children.Add(buttons);

            Content = root;
            Loaded += async (s, e) => await LoadAsync();
        }

        private static DependencyProperty ToggleButton_IsChecked => System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty;

        private async Task LoadAsync()
        {
            IReadOnlyList<(string Target, bool Installed)>? all = await Task.Run(RustupTargets.ListAll);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (all is null)
            {
                _status.Text = PropertiesText.RustupMissing;
                return;
            }
            _rows.Clear();
            foreach (var (target, installed) in all.OrderByDescending(t => t.Installed).ThenBy(t => t.Target))
            {
                _rows.Add(new RustTargetRow(target, installed));
            }
            _status.Text = string.Empty;
            _apply.IsEnabled = true;
        }

        private async Task ApplyAsync()
        {
            var add = _rows.Where(r => r.IsChecked && !r.Installed).Select(r => r.Target).ToList();
            var remove = _rows.Where(r => !r.IsChecked && r.Installed).Select(r => r.Target).ToList();
            if (add.Count == 0 && remove.Count == 0)
            {
                return;
            }

            _apply.IsEnabled = false;
            _status.Text = PropertiesText.TargetsRunning;
            string? failure = await Task.Run(() =>
            {
                if (add.Count > 0 && RustupTargets.Change(add: true, add, out string addOutput) != 0)
                {
                    return addOutput.Trim();
                }
                if (remove.Count > 0 && RustupTargets.Change(add: false, remove, out string removeOutput) != 0)
                {
                    return removeOutput.Trim();
                }
                return null;
            });
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            RustTargetTriplesEnumProvider.Invalidate();
            await LoadAsync();
            _status.Text = failure is null ? PropertiesText.TargetsDone : PropertiesText.TargetsFailed(failure);
        }
    }
}
