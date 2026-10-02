using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Kubuno.Desktop.Logic.Data;
using Kubuno.Shared.Logging;
using Kubuno.Desktop.Options;
using Kubuno.Shared.UI;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Desktop.DataExplorer
{
    /// <summary>
    /// The content of a query window (<see cref="QueryToolWindow"/>): a SQL editor (monospace, themed), the connection
    /// name, and the results - one themed grid per result set (tabs "Résultats 1..n") plus a Messages tab (rows affected,
    /// errors) - and a status line (rows, elapsed time, truncation). Execute runs the selection if any, else everything;
    /// Cancel sends <c>cancel</c> to the helper. Nothing blocks the UI thread.
    /// </summary>
    internal sealed class QueryControl : UserControl
    {
        private readonly string _tabGroup = "queryTabs" + Guid.NewGuid().ToString("N");
        private readonly TextBox _editor;
        private readonly TextBlock _connectionText;
        private readonly TextBlock _status;
        private readonly CrispImage _statusIcon;
        private readonly ProgressBar _progress;
        private readonly StackPanel _tabs;
        private readonly ContentControl _resultHost;
        private readonly TextBox _messages;
        private CancellationTokenSource? _running;

        public QueryControl()
        {
            ThemedControls.AddImplicitStyles(Resources);
            SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
            SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);

            var root = new DockPanel();

            // Connection strip.
            var strip = new DockPanel { Margin = new Thickness(6, 4, 6, 4) };
            _statusIcon = new CrispImage { Width = 16, Height = 16, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 4, 0) };
            _status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Text = DataText.Ready };
            _status.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
            System.Windows.Automation.AutomationProperties.SetAutomationId(_status, "KubunoDataStatus");
            var statusPanel = new StackPanel { Orientation = Orientation.Horizontal };
            statusPanel.Children.Add(_statusIcon);
            statusPanel.Children.Add(_status);
            DockPanel.SetDock(statusPanel, Dock.Right);
            strip.Children.Add(statusPanel);
            _connectionText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            System.Windows.Automation.AutomationProperties.SetName(_connectionText, DataText.Connection.TrimEnd(':', ' '));
            strip.Children.Add(_connectionText);
            DockPanel.SetDock(strip, Dock.Top);
            root.Children.Add(strip);

            _progress = new ProgressBar { IsIndeterminate = true, Height = 2, Visibility = Visibility.Hidden, BorderThickness = new Thickness(0) };
            _progress.SetResourceReference(Control.ForegroundProperty, EnvironmentColors.SystemHighlightBrushKey);
            _progress.Background = Brushes.Transparent;
            DockPanel.SetDock(_progress, Dock.Top);
            root.Children.Add(_progress);

            var body = new Grid();
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star), MinHeight = 60 });
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) });
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star), MinHeight = 60 });

            _editor = new TextBox
            {
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
                FontSize = 13,
                Padding = new Thickness(4),
                BorderThickness = new Thickness(0, 1, 0, 1),
            };
            System.Windows.Automation.AutomationProperties.SetName(_editor, "SQL");
            System.Windows.Automation.AutomationProperties.SetAutomationId(_editor, "KubunoQueryEditor");
            body.Children.Add(_editor);

            var splitter = new GridSplitter { Height = 5, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Background = Brushes.Transparent, ResizeDirection = GridResizeDirection.Rows };
            Grid.SetRow(splitter, 1);
            body.Children.Add(splitter);

            var results = new DockPanel();
            _tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 2, 6, 4) };
            DockPanel.SetDock(_tabs, Dock.Top);
            results.Children.Add(_tabs);
            _resultHost = new ContentControl { Focusable = false };
            results.Children.Add(_resultHost);
            Grid.SetRow(results, 2);
            body.Children.Add(results);

            _messages = new TextBox
            {
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4),
            };
            System.Windows.Automation.AutomationProperties.SetName(_messages, DataText.Messages);
            System.Windows.Automation.AutomationProperties.SetAutomationId(_messages, "KubunoQueryMessages");

            root.Children.Add(body);
            Content = root;
            ShowResults(Array.Empty<ResultSetInfo>(), new List<string>());
        }

        /// <summary>The Data Explorer connection this window queries.</summary>
        public string ConnectionName { get; private set; } = string.Empty;

        public DataProviderKind? Provider { get; private set; }

        public bool IsRunning => _running != null;

        /// <summary>The editor's text (tests and automation).</summary>
        public string SqlText => _editor.Text;

        /// <summary>Raised when <see cref="IsRunning"/> changes (the toolbar re-queries its commands).</summary>
        public event EventHandler? RunningChanged;

        public void Initialize(string connection, DataProviderKind? provider, string sql)
        {
            ConnectionName = connection;
            Provider = provider;
            _connectionText.Text = $"{DataText.Connection} {connection}" + (provider is { } p ? $" ({DataText.ProviderName(p)})" : string.Empty);
            _editor.Text = sql;
            _editor.CaretIndex = sql.Length;
        }

        public void FocusEditor() => _editor.Focus();

        /// <summary>Executes the selection, or the whole text.</summary>
        public async Task ExecuteAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_running != null)
            {
                return;
            }

            string sql = QueryText.ToExecute(_editor.Text, _editor.SelectedText);
            if (string.IsNullOrWhiteSpace(sql))
            {
                return;
            }

            var options = DataOptionsPage.Current;
            int maxRows = options.EffectiveMaxQueryRows;
            int timeout = options.EffectiveQueryTimeoutSeconds;
            await RunAsync(async token =>
            {
                var result = await DataToolHost.Service.ExecuteQueryAsync(DataConnectionTarget.Explorer(ConnectionName), sql, maxRows, timeout, token);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var messages = result.Messages.Select(QueryText.LocalizeMessage).ToList();
                if (result.ResultSets.Count == 0 && messages.Count == 0)
                {
                    messages.Add(DataText.NoResult);
                }

                ShowResults(result.ResultSets, messages);
                SetStatus(QueryText.Status(result, maxRows), KnownMonikers.StatusOK);
            });
        }

        /// <summary>"Show Table Data": shows the equivalent SELECT in the editor and the first rows (<c>data.top</c>).</summary>
        public async Task ShowDataAsync(string schema, string table)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            int limit = DataOptionsPage.Current.EffectiveShowDataRows;
            _editor.Text = QueryText.TopSelect(Provider, schema, table, limit);
            await RunAsync(async token =>
            {
                var watch = Stopwatch.StartNew();
                var result = await DataToolHost.Service.DataTopAsync(DataConnectionTarget.Explorer(ConnectionName), schema, table, limit, token);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                ShowResults(new[] { result }, new List<string>());
                SetStatus(QueryText.Status(result, watch.ElapsedMilliseconds, limit), KnownMonikers.StatusOK);
            });
        }

        public void Cancel() => _running?.Cancel();

        private async Task RunAsync(Func<CancellationToken, Task> work)
        {
            using var cts = new CancellationTokenSource();
            _running = cts;
            RunningChanged?.Invoke(this, EventArgs.Empty);
            _progress.Visibility = Visibility.Visible;
            SetStatus(DataText.Executing, null);
            try
            {
                await work(cts.Token);
            }
            catch (OperationCanceledException)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                ShowResults(Array.Empty<ResultSetInfo>(), new List<string> { DataText.Cancelled });
                SetStatus(DataText.Cancelled, KnownMonikers.StatusInformation);
            }
            catch (Exception exception)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (!(exception is DataToolException))
                {
                    KubunoLog.WriteException("Kubuno: query window", exception);
                }

                ShowResults(Array.Empty<ResultSetInfo>(), new List<string> { exception.Message });
                SetStatus(DataText.QueryFailed, KnownMonikers.StatusError);
            }
            finally
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _running = null;
                _progress.Visibility = Visibility.Hidden;
                RunningChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void ShowResults(IReadOnlyList<ResultSetInfo> sets, List<string> messages)
        {
            _tabs.Children.Clear();
            var pages = new List<(RadioButton Tab, FrameworkElement Content)>();
            for (int i = 0; i < sets.Count; i++)
            {
                var tab = DataUi.Tab(sets.Count == 1 ? DataText.Results : DataText.ResultSet(i + 1), _tabGroup);
                pages.Add((tab, DataUi.ResultGrid(sets[i])));
            }

            var builder = new StringBuilder();
            foreach (var message in messages)
            {
                builder.AppendLine(message);
            }

            _messages.Text = builder.ToString();
            pages.Add((DataUi.Tab(DataText.Messages, _tabGroup), _messages));
            foreach (var (tab, content) in pages)
            {
                var page = content;
                tab.Checked += (_, _) => _resultHost.Content = page;
                _tabs.Children.Add(tab);
            }

            // The first result set, or Messages when there is none.
            pages[0].Tab.IsChecked = true;
            _resultHost.Content = pages[0].Content;
        }

        private void SetStatus(string text, Microsoft.VisualStudio.Imaging.Interop.ImageMoniker? icon)
        {
            _status.Text = text;
            if (icon is { } moniker)
            {
                _statusIcon.Moniker = moniker;
                _statusIcon.Visibility = Visibility.Visible;
            }
            else
            {
                _statusIcon.Visibility = Visibility.Collapsed;
            }
        }
    }
}
