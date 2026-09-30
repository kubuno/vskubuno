using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Kubuno.Cargo.Commands;
using Kubuno.Cargo.Metadata;
using Kubuno.Cargo.Registry;
using Kubuno.VisualStudio.Commands;
using Kubuno.VisualStudio.Core.SolutionExplorer;
using Kubuno.VisualStudio.Logging;
using Kubuno.VisualStudio.SolutionExplorer;
using Kubuno.VisualStudio.UI;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;

namespace Kubuno.VisualStudio.CrateManager
{
    /// <summary>
    /// The crate manager's UI, laid out like NuGet's Package Manager: Browse / Installed / Updates tabs
    /// with a search box, the crate list on the left, the selected crate's details on the right
    /// (version picker, dependency type, default features and feature checkboxes, Install / Update /
    /// Uninstall). Browse and the version/feature data come from crates.io (<see cref="CratesIoClient"/>);
    /// Installed comes from <c>cargo metadata</c>, so it works offline. Every change is a
    /// <c>cargo add</c>/<c>cargo remove</c> (<see cref="CrateInstallPlanner"/>) whose output goes to the
    /// "Kubuno" Output pane. Built in code (this project has no XAML build), themed with VS's own
    /// dialog styles and environment colors, so it follows the light/dark theme.
    /// </summary>
    internal sealed class CrateManagerControl : UserControl
    {
        private const int SearchPageSize = 40;

        private readonly RadioButton _browseTab;
        private readonly RadioButton _installedTab;
        private readonly RadioButton _updatesTab;
        private readonly TextBlock _header;
        private readonly TextBox _search;
        private readonly CheckBox _prerelease;
        private readonly Button _updateAll;
        private readonly ListBox _list;
        private readonly TextBlock _listStatus;
        private readonly StackPanel _details;
        private readonly DispatcherTimer _searchDebounce;

        private RsprojProjectContext? _context;
        private List<DependencyItem> _installed = new List<DependencyItem>();
        private bool _installedLoaded;
        private CancellationTokenSource? _searchCancellation;
        private CancellationTokenSource? _detailsCancellation;
        private List<CrateSearchResult> _searchResults = new List<CrateSearchResult>();
        private string? _searchError;
        private string? _pendingSelection;
        private bool _busy;
        private readonly TextBlock _searchHint;

        public CrateManagerControl()
        {
            ThemedDialogStyleLoader.SetUseDefaultThemedDialogStyles(this, true);
            // Visual Studio's themed control styles for everything below, template-generated items included
            // (docs/ARCHITECTURE.md, "Themed dialogs").
            ThemedControls.AddImplicitStyles(Resources);
            SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
            SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);

            var root = new DockPanel { Margin = new Thickness(10, 6, 10, 6) };

            // Tabs + project header.
            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            _header = new TextBlock { FontSize = 16, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(_header, Dock.Right);
            top.Children.Add(_header);
            _browseTab = Tab(DependenciesText.BrowseTab);
            _installedTab = Tab(DependenciesText.InstalledTab);
            _updatesTab = Tab(DependenciesText.UpdatesTab(0));
            var tabs = new StackPanel { Orientation = Orientation.Horizontal };
            tabs.Children.Add(_browseTab);
            tabs.Children.Add(_installedTab);
            tabs.Children.Add(_updatesTab);
            top.Children.Add(tabs);
            DockPanel.SetDock(top, Dock.Top);
            root.Children.Add(top);

            // Search row.
            var searchRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var refresh = new Button { Content = new CrispImage { Moniker = KnownMonikers.Refresh, Width = 16, Height = 16 }, ToolTip = DependenciesText.Refresh, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(4, 2, 4, 2), MinWidth = 0 };
            System.Windows.Automation.AutomationProperties.SetName(refresh, DependenciesText.Refresh);
            refresh.Click += (_, _) => RunUi(RefreshAllAsync);
            _prerelease = new CheckBox { Content = DependenciesText.IncludePrerelease, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            _prerelease.Click += (_, _) => RunUi(ReloadSelectionAsync);
            _updateAll = new Button { Content = DependenciesText.UpdateAllButton, Margin = new Thickness(12, 0, 0, 0), Visibility = Visibility.Collapsed };
            _updateAll.Click += (_, _) => RunUi(UpdateAllAsync);
            DockPanel.SetDock(_updateAll, Dock.Right);
            searchRow.Children.Add(_updateAll);
            _search = new TextBox { VerticalContentAlignment = VerticalAlignment.Center, ToolTip = DependenciesText.SearchCratesIo };
            _search.TextChanged += (_, _) => { _searchDebounce!.Stop(); _searchDebounce.Start(); };
            _search.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    _searchDebounce!.Stop();
                    RunUi(SearchAsync);
                }
            };
            Grid searchBox = ThemedControls.WithPlaceholder(_search, DependenciesText.SearchCratesIo);
            searchBox.Width = 320;
            _searchHint = (TextBlock)searchBox.Children[1];
            searchRow.Children.Add(searchBox);
            searchRow.Children.Add(refresh);
            searchRow.Children.Add(_prerelease);
            searchRow.Children.Add(new Border());
            DockPanel.SetDock(searchRow, Dock.Top);
            root.Children.Add(searchRow);

            // List | details.
            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 220 });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 260 });

            var listHost = new Grid();
            _list = new ListBox { HorizontalContentAlignment = HorizontalAlignment.Stretch, BorderThickness = new Thickness(1) };
            ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
            _list.SelectionChanged += (_, _) => RunUi(ShowSelectionAsync);
            _listStatus = new TextBlock { Margin = new Thickness(12), TextWrapping = TextWrapping.Wrap, IsHitTestVisible = false };
            _listStatus.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
            listHost.Children.Add(_list);
            listHost.Children.Add(_listStatus);
            Grid.SetColumn(listHost, 0);
            body.Children.Add(listHost);

            var splitter = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent };
            Grid.SetColumn(splitter, 1);
            body.Children.Add(splitter);

            _details = new StackPanel { Margin = new Thickness(12, 4, 8, 8) };
            var detailsScroll = new ScrollViewer { Content = _details, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Grid.SetColumn(detailsScroll, 2);
            body.Children.Add(detailsScroll);
            root.Children.Add(body);

            Content = root;

            _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            _searchDebounce.Tick += (_, _) =>
            {
                _searchDebounce.Stop();
                RunUi(SearchAsync);
            };

            _browseTab.Checked += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                OnTabChanged();
            };
            _installedTab.Checked += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                OnTabChanged();
            };
            _updatesTab.Checked += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                OnTabChanged();
            };
        }

        private enum CrateTab
        {
            Browse,
            Installed,
            Updates,
        }

        private CrateTab CurrentTab => _installedTab.IsChecked == true ? CrateTab.Installed : _updatesTab.IsChecked == true ? CrateTab.Updates : CrateTab.Browse;

        /// <summary>Binds the control to a project (again, when reopened) and optionally selects a crate.</summary>
        public void Initialize(RsprojProjectContext context, string? crateName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var sameProject = _context != null && string.Equals(_context.ManifestPath, context.ManifestPath, StringComparison.OrdinalIgnoreCase);
            _context = context;
            _header.Text = DependenciesText.CrateManagerHeader(context.Project.Name);
            _pendingSelection = crateName;
            if (crateName != null)
            {
                _installedTab.IsChecked = true;
            }
            else if (!sameProject)
            {
                _browseTab.IsChecked = true;
            }

            if (!sameProject || !_installedLoaded)
            {
                RunUi(RefreshAllAsync);
            }
            else
            {
                ShowList();
            }
        }

        private static RadioButton Tab(string text)
        {
            var border = new FrameworkElementFactory(typeof(Border), "border");
            border.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 2));
            border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            border.SetValue(Border.PaddingProperty, new Thickness(2, 2, 2, 3));
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            border.AppendChild(presenter);
            var template = new ControlTemplate(typeof(RadioButton)) { VisualTree = border };
            var checkedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            checkedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension(EnvironmentColors.SystemHighlightBrushKey), "border"));
            checkedTrigger.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            template.Triggers.Add(checkedTrigger);

            var tab = new RadioButton
            {
                Content = text,
                GroupName = "crateManagerTabs",
                Template = template,
                Margin = new Thickness(0, 0, 18, 0),
                FontSize = 14,
                Cursor = Cursors.Hand,
                Focusable = true,
            };
            tab.SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            return tab;
        }

        private void OnTabChanged()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _search.ToolTip = CurrentTab == CrateTab.Browse ? DependenciesText.SearchCratesIo : DependenciesText.SearchPlaceholder;
            _searchHint.Text = (string)_search.ToolTip;
            System.Windows.Automation.AutomationProperties.SetName(_search, _searchHint.Text);
            if (CurrentTab == CrateTab.Browse && _searchResults.Count == 0 && _searchError is null)
            {
                RunUi(SearchAsync);
            }

            ShowList();
        }

        private async Task RefreshAllAsync()
        {
            CratesIoClient.Shared.Invalidate();
            await LoadInstalledAsync();
            if (CurrentTab == CrateTab.Browse)
            {
                await SearchAsync();
            }
        }

        /// <summary>Installed = direct registry dependencies, from cargo metadata (offline first); then their crates.io status.</summary>
        private async Task LoadInstalledAsync()
        {
            var context = _context;
            if (context is null)
            {
                return;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (CurrentTab != CrateTab.Browse)
            {
                SetListStatus(DependenciesText.LoadingInstalled);
            }

            await TaskScheduler.Default;
            List<DependencyItem> installed;
            try
            {
                var declared = await DependencyDataLoader.ReadDeclaredAsync(context.ManifestPath, CancellationToken.None);
                var package = CargoDependencyGroups.FindPackage(declared, context.ManifestPath, context.PackageName);
                if (package is null)
                {
                    installed = new List<DependencyItem>();
                }
                else
                {
                    var toolchain = await DependencyDataLoader.GetToolchainAsync(context.ProjectDirectory, CancellationToken.None);
                    var (resolved, _) = await DependencyDataLoader.ReadResolvedAsync(context.ManifestPath, toolchain?.Host, CancellationToken.None);
                    var model = DependencyTreeBuilder.Build(package, resolved, null, null);
                    installed = model.AllItems.Where(i => i.ItemKind == DependencyItemKind.Crate && !i.IsTransitive && i.IsRegistry).ToList();
                }
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException("Crate manager: reading the installed crates", exception);
                installed = _installed;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            _installed = installed;
            _installedLoaded = true;
            ShowList();

            await TaskScheduler.Default;
            if (await DependencyDataLoader.ApplyRegistryStatusAsync(installed, CancellationToken.None))
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                ShowList();
            }
        }

        private async Task SearchAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (CurrentTab != CrateTab.Browse)
            {
                ShowList();
                return;
            }

            var cancellation = new CancellationTokenSource();
            Interlocked.Exchange(ref _searchCancellation, cancellation)?.Cancel();
            var query = _search.Text.Trim();
            SetListStatus(DependenciesText.Searching);
            _list.Items.Clear();

            await TaskScheduler.Default;
            List<CrateSearchResult> results;
            string? error = null;
            try
            {
                results = (await CratesIoClient.Shared.SearchAsync(query, 1, SearchPageSize, cancellation.Token)).Crates.ToList();
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                results = new List<CrateSearchResult>();
                error = exception.GetBaseException().Message;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            _searchResults = results;
            _searchError = error;
            ShowList();
        }

        /// <summary>Rebuilds the left-hand list for the current tab and search text.</summary>
        private void ShowList()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var updates = _installed.Where(i => i.IsOutdated || i.IsYanked).ToList();
            _updatesTab.Content = DependenciesText.UpdatesTab(updates.Count);
            _updateAll.Visibility = CurrentTab == CrateTab.Updates && updates.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            var filter = _search.Text.Trim();
            var selectedName = (_list.SelectedItem as ListBoxItem)?.Tag is CrateRow selected ? selected.Name : null;
            selectedName = _pendingSelection ?? selectedName;
            var rows = new List<CrateRow>();
            switch (CurrentTab)
            {
                case CrateTab.Browse:
                    rows.AddRange(_searchResults.Select(r => new CrateRow(r.Name, r.Description, r.Downloads, r.PreferredVersion, Installed(r.Name), r)));
                    break;
                case CrateTab.Installed:
                    rows.AddRange(_installed.Where(i => Matches(i, filter)).OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).Select(i => new CrateRow(i.Name, i.Package?.Description, null, i.LatestVersion, i, null)));
                    break;
                default:
                    rows.AddRange(updates.Where(i => Matches(i, filter)).OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).Select(i => new CrateRow(i.Name, i.Package?.Description, null, i.LatestVersion, i, null)));
                    break;
            }

            _list.Items.Clear();
            ListBoxItem? toSelect = null;
            foreach (var row in rows)
            {
                var item = new ListBoxItem { Content = RowView(row), Tag = row, Padding = new Thickness(4, 6, 4, 6) };
                System.Windows.Automation.AutomationProperties.SetName(item, row.Name);
                _list.Items.Add(item);
                if (selectedName != null && string.Equals(row.Name, selectedName, StringComparison.OrdinalIgnoreCase))
                {
                    toSelect = item;
                }
            }

            if (rows.Count == 0)
            {
                SetListStatus(CurrentTab switch
                {
                    CrateTab.Browse => _searchError != null ? DependenciesText.Offline(_searchError) : DependenciesText.NoResults,
                    CrateTab.Installed => _installedLoaded ? DependenciesText.NoInstalled : DependenciesText.LoadingInstalled,
                    _ => _installedLoaded ? DependenciesText.NoUpdates : DependenciesText.CheckingUpdates,
                });
            }
            else
            {
                SetListStatus(null);
            }

            if (toSelect != null)
            {
                _pendingSelection = null;
                _list.SelectedItem = toSelect;
                _list.ScrollIntoView(toSelect);
            }
            else if (_list.Items.Count > 0 && _list.SelectedItem is null)
            {
                _list.SelectedIndex = 0;
            }
            else if (_list.Items.Count == 0)
            {
                _details.Children.Clear();
            }
        }

        private static bool Matches(DependencyItem item, string filter) =>
            filter.Length == 0 || item.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

        private DependencyItem? Installed(string name) => _installed.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));

        private void SetListStatus(string? text)
        {
            _listStatus.Text = text ?? string.Empty;
            _listStatus.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        }

        private UIElement RowView(CrateRow row)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new CrispImage { Moniker = KnownMonikers.NuGetNoColor, Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 8, 0) };
            grid.Children.Add(icon);

            var text = new StackPanel();
            var title = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
            title.Inlines.Add(new Run(row.Name) { FontWeight = FontWeights.SemiBold });
            if (row.Downloads is long downloads)
            {
                title.Inlines.Add(new Run("  " + DependenciesText.Downloads(CrateInstallPlanner.FormatCount(downloads))) { FontSize = 11 });
            }

            text.Children.Add(title);
            if (!string.IsNullOrWhiteSpace(row.Description))
            {
                var description = new TextBlock { Text = row.Description!.Trim(), TextWrapping = TextWrapping.Wrap, MaxHeight = 34, TextTrimming = TextTrimming.WordEllipsis, Margin = new Thickness(0, 2, 0, 0) };
                text.Children.Add(description);
            }

            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            var versions = new StackPanel { Margin = new Thickness(8, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
            var installedVersion = row.Installed?.ResolvedVersion ?? row.Installed?.RequestedVersion;
            if (installedVersion != null)
            {
                versions.Children.Add(new TextBlock { Text = "v" + installedVersion, HorizontalAlignment = HorizontalAlignment.Right });
                if (row.Installed!.IsOutdated && row.Installed.LatestVersion != null)
                {
                    var latest = new TextBlock { Text = "v" + row.Installed.LatestVersion, HorizontalAlignment = HorizontalAlignment.Right };
                    latest.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ControlLinkTextBrushKey);
                    versions.Children.Add(latest);
                }
            }
            else if (row.Version != null)
            {
                versions.Children.Add(new TextBlock { Text = "v" + row.Version, HorizontalAlignment = HorizontalAlignment.Right });
            }

            Grid.SetColumn(versions, 2);
            grid.Children.Add(versions);
            return grid;
        }

        private async Task ReloadSelectionAsync() => await ShowSelectionAsync();

        /// <summary>The details pane: header, installed version, version picker, type, features, actions, metadata.</summary>
        private async Task ShowSelectionAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var cancellation = new CancellationTokenSource();
            Interlocked.Exchange(ref _detailsCancellation, cancellation)?.Cancel();
            if ((_list.SelectedItem as ListBoxItem)?.Tag is not CrateRow row)
            {
                return;
            }

            var installed = row.Installed;
            BuildDetails(row, versions: null, details: null, offline: null);

            await TaskScheduler.Default;
            IReadOnlyList<CrateIndexVersion>? versions = null;
            CrateDetails? details = null;
            string? offline = null;
            try
            {
                versions = await CratesIoClient.Shared.GetVersionsAsync(row.Name, cancellation.Token);
                if (installed?.Package is null)
                {
                    details = await CratesIoClient.Shared.GetDetailsAsync(row.Name, cancellation.Token);
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                offline = exception.GetBaseException().Message;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (!cancellation.IsCancellationRequested)
            {
                BuildDetails(row, versions, details, offline);
            }
        }

        private void BuildDetails(CrateRow row, IReadOnlyList<CrateIndexVersion>? versions, CrateDetails? details, string? offline)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _details.Children.Clear();
            var installed = row.Installed;
            var declaration = installed?.PrimaryDeclaration;

            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            header.Children.Add(new CrispImage { Moniker = KnownMonikers.NuGetNoColor, Width = 32, Height = 32, Margin = new Thickness(0, 0, 8, 0) });
            header.Children.Add(new TextBlock { Text = row.Name, FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            var page = Link(DependenciesText.CratesIoPage, "https://crates.io/crates/" + row.Name);
            page.Margin = new Thickness(12, 0, 0, 0);
            page.VerticalAlignment = VerticalAlignment.Center;
            header.Children.Add(page);
            _details.Children.Add(header);

            // Installed: version + Uninstall.
            if (installed != null)
            {
                var installedRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                var uninstall = new Button { Content = DependenciesText.Uninstall, MinWidth = 96, IsEnabled = !_busy };
                uninstall.Click += (_, _) => RunUi(() => ApplyAsync(CrateInstallPlanner.Uninstall(_context!.ManifestPath, _context.PackageName, installed.DisplayName, installed.Declarations)));
                DockPanel.SetDock(uninstall, Dock.Right);
                installedRow.Children.Add(uninstall);
                installedRow.Children.Add(Label(DependenciesText.Installed, 110));
                var kind = DependencyProperties.KindText(installed.Kinds);
                installedRow.Children.Add(new TextBlock { Text = (installed.ResolvedVersion ?? installed.RequestedVersion ?? "?") + "   (" + kind + ")", VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                _details.Children.Add(installedRow);
            }

            // Version picker + Install/Update.
            var includePre = _prerelease.IsChecked == true;
            var choices = (versions ?? Array.Empty<CrateIndexVersion>())
                .Where(v => v.Version != null && (!v.Yanked || string.Equals(v.Vers, installed?.ResolvedVersion, StringComparison.Ordinal)) && (includePre || !v.Version.IsPreRelease || string.Equals(v.Vers, installed?.ResolvedVersion, StringComparison.Ordinal)))
                .OrderByDescending(v => v.Version)
                .ToList();
            var versionBox = new ComboBox { MinWidth = 160, IsEnabled = !_busy };
            System.Windows.Automation.AutomationProperties.SetName(versionBox, DependenciesText.VersionLabel);
            foreach (var choice in choices)
            {
                versionBox.Items.Add(new ComboBoxItem { Content = choice.Vers + (choice.Yanked ? DependenciesText.YankedSuffix : string.Empty), Tag = choice });
            }

            if (versionBox.Items.Count == 0 && (installed?.ResolvedVersion ?? row.Version) is string known)
            {
                versionBox.Items.Add(new ComboBoxItem { Content = known, Tag = null });
            }

            var preferred = installed != null
                ? (installed.IsOutdated ? installed.LatestVersion : installed.ResolvedVersion)
                : row.Version;
            versionBox.SelectedItem = versionBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (i.Tag as CrateIndexVersion)?.Vers == preferred || (string?)i.Content == preferred)
                ?? versionBox.Items.Cast<ComboBoxItem>().FirstOrDefault();

            var action = new Button { Content = installed != null ? DependenciesText.UpdateButton : DependenciesText.Install, MinWidth = 96, IsEnabled = !_busy };
            var versionRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(action, Dock.Right);
            versionRow.Children.Add(action);
            versionRow.Children.Add(Label(DependenciesText.VersionLabel, 110));
            versionRow.Children.Add(new Border { Child = versionBox, HorizontalAlignment = HorizontalAlignment.Left });
            _details.Children.Add(versionRow);

            // Dependency type (for a new install; an installed crate keeps its table).
            var normal = new RadioButton { Content = DependenciesText.KindNormalShort, GroupName = "crateKind", IsChecked = true, Margin = new Thickness(0, 0, 12, 0) };
            var dev = new RadioButton { Content = DependenciesText.KindDevShort, GroupName = "crateKind", Margin = new Thickness(0, 0, 12, 0) };
            var build = new RadioButton { Content = DependenciesText.KindBuildShort, GroupName = "crateKind" };
            if (installed != null)
            {
                dev.IsChecked = (installed.Kinds & DependencyKinds.Normal) == 0 && (installed.Kinds & DependencyKinds.Dev) != 0;
                build.IsChecked = (installed.Kinds & DependencyKinds.Normal) == 0 && (installed.Kinds & DependencyKinds.Build) != 0;
                normal.IsChecked = dev.IsChecked != true && build.IsChecked != true;
                normal.IsEnabled = dev.IsEnabled = build.IsEnabled = false;
            }

            var kindRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            kindRow.Children.Add(Label(DependenciesText.KindLabel, 110));
            kindRow.Children.Add(normal);
            kindRow.Children.Add(dev);
            kindRow.Children.Add(build);
            _details.Children.Add(kindRow);

            // Features.
            var defaults = new CheckBox { Content = DependenciesText.DefaultFeatures, IsChecked = declaration?.UsesDefaultFeatures ?? true, Margin = new Thickness(0, 0, 0, 6) };
            _details.Children.Add(defaults);
            _details.Children.Add(new TextBlock { Text = DependenciesText.FeaturesLabel, Margin = new Thickness(0, 0, 0, 4) });
            var features = new WrapPanel { Margin = new Thickness(8, 0, 0, 10) };
            _details.Children.Add(features);
            var requested = new HashSet<string>(installed?.Declarations.SelectMany(d => d.Features) ?? Enumerable.Empty<string>(), StringComparer.Ordinal);

            void FillFeatures()
            {
                var checkedNow = features.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Content).ToList();
                var keep = features.Children.Count > 0 ? new HashSet<string>(checkedNow, StringComparer.Ordinal) : requested;
                features.Children.Clear();
                var selected = (versionBox.SelectedItem as ComboBoxItem)?.Tag as CrateIndexVersion;
                IEnumerable<string> names = selected?.SelectableFeatures
                    ?? installed?.Package?.Features.Keys.Where(k => k != "default").OrderBy(k => k, StringComparer.Ordinal)
                    ?? Enumerable.Empty<string>();
                foreach (var name in names)
                {
                    var box = new CheckBox { Content = name, IsChecked = keep.Contains(name), Margin = new Thickness(0, 2, 14, 2) };
                    if (selected?.Features.TryGetValue(name, out var enables) == true && enables.Count > 0)
                    {
                        box.ToolTip = string.Join(", ", enables);
                    }

                    features.Children.Add(box);
                }

                if (features.Children.Count == 0)
                {
                    features.Children.Add(new TextBlock { Text = DependenciesText.NoFeatures, FontStyle = FontStyles.Italic });
                }
            }

            FillFeatures();
            versionBox.SelectionChanged += (_, _) => FillFeatures();

            action.Click += (_, _) =>
            {
                var version = ((versionBox.SelectedItem as ComboBoxItem)?.Tag as CrateIndexVersion)?.Vers ?? (versionBox.SelectedItem as ComboBoxItem)?.Content as string;
                var chosen = features.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Content).ToList();
                var context = _context!;
                var commands = installed != null
                    ? CrateInstallPlanner.Update(context.ManifestPath, context.PackageName, installed, version, chosen, defaults.IsChecked == true)
                    : CrateInstallPlanner.Install(context.ManifestPath, context.PackageName, row.Name, version, chosen, defaults.IsChecked == true,
                        dev.IsChecked == true ? DependencyKinds.Dev : build.IsChecked == true ? DependencyKinds.Build : DependencyKinds.Normal);
                RunUi(() => ApplyAsync(commands));
            };

            // Metadata.
            var package = installed?.Package;
            var selectedVersion = ((versionBox.SelectedItem as ComboBoxItem)?.Tag as CrateIndexVersion)?.Vers;
            var description = package?.Description ?? details?.Description ?? row.Description;
            if (!string.IsNullOrWhiteSpace(description))
            {
                _details.Children.Add(new TextBlock { Text = DependenciesText.DescriptionLabel, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 2) });
                _details.Children.Add(new TextBlock { Text = description!.Trim(), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            }

            string? license = package?.License;
            if (license is null && details != null && selectedVersion != null)
            {
                details.Licenses.TryGetValue(selectedVersion, out license);
            }

            AddInfo(DependenciesText.LicenseLabel, license);
            var downloads = row.Downloads ?? details?.Downloads;
            AddInfo(DependenciesText.DownloadsLabel, downloads is long d && d > 0 ? CrateInstallPlanner.FormatCount(d) : null);
            var rustVersion = ((versionBox.SelectedItem as ComboBoxItem)?.Tag as CrateIndexVersion)?.RustVersion ?? package?.RustVersion;
            AddInfo(DependenciesText.MinimumRustLabel, rustVersion);
            AddLink(DependenciesText.RepositoryLabel, package?.Repository ?? details?.Repository ?? row.Search?.Repository);
            AddLink(DependenciesText.DocumentationLabel, package?.Documentation ?? details?.Documentation ?? row.Search?.Documentation ?? "https://docs.rs/" + row.Name);

            if (offline != null)
            {
                var note = new TextBlock { Text = DependenciesText.Offline(offline), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
                note.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
                _details.Children.Add(note);
            }
        }

        private void AddInfo(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };
            row.Children.Add(Label(label, 160));
            row.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap });
            _details.Children.Add(row);
        }

        private void AddLink(string label, string? url)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };
            row.Children.Add(Label(label, 160));
            row.Children.Add(Link(url!, url!));
            _details.Children.Add(row);
        }

        private static TextBlock Label(string text, double width) =>
            new TextBlock { Text = text, Width = width, VerticalAlignment = VerticalAlignment.Center };

        private static TextBlock Link(string text, string url)
        {
            var hyperlink = new Hyperlink(new Run(text)) { NavigateUri = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null };
            hyperlink.SetResourceReference(TextElement.ForegroundProperty, EnvironmentColors.ControlLinkTextBrushKey);
            hyperlink.RequestNavigate += (_, e) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (e.Uri != null && (e.Uri.Scheme == Uri.UriSchemeHttps || e.Uri.Scheme == Uri.UriSchemeHttp))
                {
                    VsShellUtilities.OpenSystemBrowser(e.Uri.AbsoluteUri);
                }

                e.Handled = true;
            };
            return new TextBlock(hyperlink) { TextTrimming = TextTrimming.CharacterEllipsis };
        }

        private async Task UpdateAllAsync()
        {
            var context = _context;
            if (context is null)
            {
                return;
            }

            var commands = _installed
                .Where(i => i.IsOutdated && i.LatestVersion != null)
                .SelectMany(i => CrateInstallPlanner.Update(context.ManifestPath, context.PackageName, i, i.LatestVersion, i.Declarations.SelectMany(d => d.Features).Distinct(), i.Declarations.All(d => d.UsesDefaultFeatures)))
                .ToList();
            await ApplyAsync(commands);
        }

        /// <summary>Runs the planned cargo commands one after the other, then re-reads the project.</summary>
        private async Task ApplyAsync(IReadOnlyList<CargoCommand> commands)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_busy || commands.Count == 0 || _context is null)
            {
                return;
            }

            _busy = true;
            IsEnabled = false;
            var selected = ((_list.SelectedItem as ListBoxItem)?.Tag as CrateRow)?.Name;
            try
            {
                foreach (var command in commands)
                {
                    if (!await CargoRunner.RunAsync(command, CrateInstallPlanner.Describe(command)))
                    {
                        break;
                    }
                }
            }
            finally
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _busy = false;
                IsEnabled = true;
            }

            ProjectDependenciesSource.For(_context.Hierarchy)?.Reload();
            _pendingSelection = selected;
            await LoadInstalledAsync();
            await ShowSelectionAsync();
        }

        /// <summary>Starts UI work from an event handler, reporting (not swallowing) failures.</summary>
        private static void RunUi(Func<Task> work)
        {
#pragma warning disable VSSDK007 // fire-and-forget from WPF event handlers; FileAndForget reports faults.
            ThreadHelper.JoinableTaskFactory.RunAsync(work).FileAndForget("Kubuno/CrateManager");
#pragma warning restore VSSDK007
        }

        private sealed class CrateRow
        {
            public CrateRow(string name, string? description, long? downloads, string? version, DependencyItem? installed, CrateSearchResult? search)
            {
                Name = name;
                Description = description;
                Downloads = downloads;
                Version = version;
                Installed = installed;
                Search = search;
            }

            public string Name { get; }

            public string? Description { get; }

            public long? Downloads { get; }

            /// <summary>The version to show (search: the preferred one; installed: the latest known).</summary>
            public string? Version { get; }

            public DependencyItem? Installed { get; }

            public CrateSearchResult? Search { get; }
        }
    }
}
