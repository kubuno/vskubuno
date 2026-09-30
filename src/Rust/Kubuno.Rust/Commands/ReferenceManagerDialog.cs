using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Kubuno.VisualStudio.Core.SolutionExplorer;
using Kubuno.VisualStudio.UI;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.VisualStudio.Commands
{
    /// <summary>Where a <see cref="ReferenceCandidate"/> was found: the Reference Manager's left-hand categories.</summary>
    internal enum ReferenceOrigin
    {
        /// <summary>Projets &gt; Solution: another <c>.rsproj</c> of the solution.</summary>
        Solution,

        /// <summary>Parcourir &gt; Récent: a crate folder picked with "Parcourir...", or an existing path dependency outside the solution.</summary>
        Browse,
    }

    /// <summary>One crate the Reference Manager can reference as a path dependency.</summary>
    internal sealed class ReferenceCandidate : INotifyPropertyChanged
    {
        private bool _isChecked;

        public ReferenceCandidate(string crateName, string manifestPath, bool isReferenced, ReferenceOrigin origin = ReferenceOrigin.Solution)
        {
            CrateName = crateName;
            ManifestPath = manifestPath;
            IsReferenced = _isChecked = isReferenced;
            Origin = origin;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string CrateName { get; }

        public string ManifestPath { get; }

        public string ProjectDirectory => Path.GetDirectoryName(ManifestPath) ?? string.Empty;

        /// <summary>Whether <c>Cargo.toml</c> already declares it as a path dependency (the initial checkbox state).</summary>
        public bool IsReferenced { get; }

        public ReferenceOrigin Origin { get; }

        /// <summary>The name <c>Cargo.toml</c> declares it under (its rename), when referenced - what <c>cargo remove</c> needs.</summary>
        public string? DeclaredName { get; set; }

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

        /// <summary>Also the list row's UI Automation name.</summary>
        public override string ToString() => CrateName;

        /// <summary>The <c>[package] name</c> of a <c>Cargo.toml</c> (a light read, no cargo process), else its folder name.</summary>
        public static string ReadCrateName(string manifestPath)
        {
            try
            {
                var text = File.ReadAllText(manifestPath);
                var package = Regex.Match(text, @"^\s*\[package\]\s*$(?<body>[\s\S]*?)(^\s*\[|\z)", RegexOptions.Multiline);
                var name = package.Success ? Regex.Match(package.Groups["body"].Value, @"^\s*name\s*=\s*""(?<name>[^""]+)""", RegexOptions.Multiline) : Match.Empty;
                if (name.Success)
                {
                    return name.Groups["name"].Value;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return Path.GetFileName(Path.GetDirectoryName(manifestPath)) ?? manifestPath;
        }
    }

    /// <summary>
    /// "Reference Manager" for a <c>.rsproj</c>, shaped like Visual Studio's own: categories on the left
    /// (Projects &gt; Solution, Browse &gt; Recent), a searchable checked list in the middle, the
    /// selected crate's details on the right, "Browse..." to reference any crate folder. OK turns the
    /// changed checkboxes into <c>cargo add --path</c>/<c>cargo remove</c>
    /// (<see cref="AddProjectReferenceCommand"/>).
    /// </summary>
    internal sealed class ReferenceManagerDialog : ThemedDialog
    {
        /// <summary>Crate folders picked with "Browse..." during this session (VS's "Recent" list).</summary>
        private static readonly List<string> RecentManifests = new List<string>();

        private readonly ObservableCollection<ReferenceCandidate> _all;
        private readonly ListView _list;
        private readonly TextBox _search;
        private readonly TextBlock _details;
        private readonly TextBlock _empty;
        private ReferenceOrigin _origin = ReferenceOrigin.Solution;

        public ReferenceManagerDialog(string projectName, IEnumerable<ReferenceCandidate> candidates)
        {
            _all = new ObservableCollection<ReferenceCandidate>(candidates);
            foreach (var recentManifest in RecentManifests.ToList())
            {
                if (!_all.Any(c => SamePath(c.ManifestPath, recentManifest)) && File.Exists(recentManifest))
                {
                    _all.Add(new ReferenceCandidate(ReferenceCandidate.ReadCrateName(recentManifest), recentManifest, isReferenced: false, ReferenceOrigin.Browse));
                }
            }

            Title = DependenciesText.ReferenceManagerTitle(projectName);
            Width = 820;
            Height = 520;
            MinWidth = 560;
            MinHeight = 360;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            HasMaximizeButton = false;

            var root = new Grid { Margin = new Thickness(10) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });

            // Search box, top right (like VS's "Search (Ctrl+E)").
            _search = new TextBox();
            _search.TextChanged += (_, _) => ApplyFilter();
            Grid searchPanel = ThemedControls.WithPlaceholder(_search, DependenciesText.SearchPlaceholder);
            searchPanel.Margin = new Thickness(8, 0, 0, 6);
            Grid.SetRow(searchPanel, 0);
            Grid.SetColumn(searchPanel, 2);
            root.Children.Add(searchPanel);

            // Categories.
            var tree = new TreeView { Margin = new Thickness(0, 0, 8, 0) };
            var projects = new TreeViewItem { Header = DependenciesText.Projects, IsExpanded = true };
            var solution = new TreeViewItem { Header = DependenciesText.SolutionNode, IsSelected = true, Tag = ReferenceOrigin.Solution };
            projects.Items.Add(solution);
            var browse = new TreeViewItem { Header = DependenciesText.BrowseNode, IsExpanded = true };
            var recent = new TreeViewItem { Header = DependenciesText.RecentNode, Tag = ReferenceOrigin.Browse };
            browse.Items.Add(recent);
            tree.Items.Add(projects);
            tree.Items.Add(browse);
            tree.SelectedItemChanged += (_, e) =>
            {
                if (e.NewValue is TreeViewItem { Tag: ReferenceOrigin origin })
                {
                    _origin = origin;
                    ApplyFilter();
                }
            };
            Grid.SetRow(tree, 1);
            Grid.SetColumn(tree, 0);
            root.Children.Add(tree);

            // Checked list: [x] Name | Path.
            var view = new GridView();
            _list = ThemedControls.GridListView(view);
            _list.SelectionMode = SelectionMode.Single;
            var check = new FrameworkElementFactory(typeof(CheckBox));
            check.SetBinding(ToggleButtonIsCheckedProperty, new Binding(nameof(ReferenceCandidate.IsChecked)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            view.Columns.Add(new GridViewColumn { Width = 30, CellTemplate = new DataTemplate { VisualTree = check } });
            view.Columns.Add(new GridViewColumn { Header = DependenciesText.NameColumn, Width = 170, DisplayMemberBinding = new Binding(nameof(ReferenceCandidate.CrateName)) });
            view.Columns.Add(new GridViewColumn { Header = DependenciesText.PathColumn, Width = 260, DisplayMemberBinding = new Binding(nameof(ReferenceCandidate.ProjectDirectory)) });
            _list.ItemsSource = _all;
            _list.SelectionChanged += (_, _) => UpdateDetails();
            _list.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Space && _list.SelectedItem is ReferenceCandidate candidate)
                {
                    candidate.IsChecked = !candidate.IsChecked;
                    e.Handled = true;
                }
            };
            _empty = ThemedControls.SecondaryText(DependenciesText.NoItemsFound);
            _empty.Margin = new Thickness(12, 30, 12, 12);
            _empty.Visibility = Visibility.Collapsed;
            _empty.IsHitTestVisible = false;
            var center = new Grid();
            center.Children.Add(_list);
            center.Children.Add(_empty);
            Grid.SetRow(center, 1);
            Grid.SetColumn(center, 1);
            root.Children.Add(center);

            // Details of the selected crate.
            _details = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 4, 0, 0) };
            Grid.SetRow(_details, 1);
            Grid.SetColumn(_details, 2);
            root.Children.Add(_details);

            // Browse... / OK / Cancel.
            var browseButton = new Button { Content = DependenciesText.BrowseButton };
            browseButton.Click += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                BrowseForCrate(recent);
            };
            var ok = new Button { Content = DependenciesText.Ok, IsDefault = true };
            ok.Click += (_, _) => DialogResult = true;
            var cancel = new Button { Content = DependenciesText.Cancel, IsCancel = true };
            StackPanel buttons = ThemedControls.ButtonRow(browseButton, ok, cancel);
            browseButton.Margin = new Thickness(0, 0, 11, 0);
            Grid.SetRow(buttons, 2);
            Grid.SetColumn(buttons, 0);
            Grid.SetColumnSpan(buttons, 3);
            root.Children.Add(buttons);

            Content = root;
            ApplyFilter();
            UpdateDetails();
            Loaded += (_, _) => _list.Focus();
        }

        /// <summary>Every candidate, checked or not (including crates picked with "Browse...").</summary>
        public IReadOnlyList<ReferenceCandidate> Candidates => _all;

        private static System.Windows.DependencyProperty ToggleButtonIsCheckedProperty => System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty;

        private static bool SamePath(string a, string b)
        {
            try
            {
                return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }
        }

        private void ApplyFilter()
        {
            var text = _search.Text.Trim();
            var view = CollectionViewSource.GetDefaultView(_all);
            view.Filter = o => o is ReferenceCandidate c
                && c.Origin == _origin
                && (text.Length == 0 || c.CrateName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 || c.ProjectDirectory.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
            view.Refresh();
            _empty.Visibility = view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateDetails()
        {
            _details.Text = _list.SelectedItem is ReferenceCandidate c
                ? DependenciesText.ReferenceDetails(c.CrateName, c.ProjectDirectory)
                : DependenciesText.NoItemSelected;
        }

        private void BrowseForCrate(TreeViewItem recentNode)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var picker = new Microsoft.Win32.OpenFileDialog
            {
                Title = DependenciesText.BrowseDialogTitle,
                Filter = DependenciesText.CargoManifestFilter,
                CheckFileExists = true,
                FileName = "Cargo.toml",
            };
            if (picker.ShowDialog(this) != true)
            {
                return;
            }

            var manifest = picker.FileName;
            var existing = _all.FirstOrDefault(c => SamePath(c.ManifestPath, manifest));
            if (existing is null)
            {
                existing = new ReferenceCandidate(ReferenceCandidate.ReadCrateName(manifest), manifest, isReferenced: false, ReferenceOrigin.Browse);
                _all.Add(existing);
            }

            if (!RecentManifests.Any(m => SamePath(m, manifest)))
            {
                RecentManifests.Insert(0, manifest);
            }

            existing.IsChecked = true;
            if (existing.Origin == ReferenceOrigin.Browse)
            {
                recentNode.IsSelected = true;
            }

            ApplyFilter();
            _list.SelectedItem = existing;
        }
    }
}
