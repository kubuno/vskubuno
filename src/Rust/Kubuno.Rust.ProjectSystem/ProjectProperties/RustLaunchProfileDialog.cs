using System.Windows;
using System.Windows.Controls;
using Kubuno.Shared.UI;

namespace Kubuno.Rust.ProjectSystem.ProjectProperties
{
    /// <summary>The values <see cref="RustLaunchProfileDialog"/> edits (the <c>RustDebugger*</c> user-file properties F5 reads).</summary>
    internal sealed class RustLaunchProfile
    {
        public string Executable { get; set; } = string.Empty;

        public string Arguments { get; set; } = string.Empty;

        public string WorkingDirectory { get; set; } = string.Empty;

        /// <summary>One <c>NAME=value</c> per line.</summary>
        public string Environment { get; set; } = string.Empty;

        public bool Backtrace { get; set; }
    }

    /// <summary>
    /// "Open debug launch profile UI" of the Debug page (docs/RSPROJ.md, "Project properties like .NET"): the
    /// counterpart of .NET's launch profiles window for the single Rust launch profile of a <c>.rsproj</c> -
    /// the executable F5 starts, its arguments, working directory and environment. VS-themed, UI built in code.
    /// </summary>
    internal sealed class RustLaunchProfileDialog : ThemedDialog
    {
        private readonly TextBox _arguments;
        private readonly TextBox _workingDirectory;
        private readonly TextBox _environment;
        private readonly CheckBox _backtrace;

        public RustLaunchProfileDialog(RustLaunchProfile profile)
        {
            Title = PropertiesText.LaunchTitle;
            Width = 620;
            Height = 520;
            MinWidth = 460;
            MinHeight = 400;
            ResizeMode = ResizeMode.CanResizeWithGrip;

            var root = new Grid { Margin = new Thickness(12) };
            for (int i = 0; i < 9; i++)
            {
                root.RowDefinitions.Add(new RowDefinition { Height = i == 7 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            }

            AddLabel(root, 0, PropertiesText.LaunchExecutable);
            var executable = new TextBox { Text = profile.Executable, IsReadOnly = true, Margin = new Thickness(0, 2, 0, 10) };
            System.Windows.Automation.AutomationProperties.SetName(executable, PropertiesText.LaunchExecutable);
            Grid.SetRow(executable, 1);
            root.Children.Add(executable);

            AddLabel(root, 2, PropertiesText.LaunchArguments);
            _arguments = new TextBox { Text = profile.Arguments, Margin = new Thickness(0, 2, 0, 10) };
            System.Windows.Automation.AutomationProperties.SetName(_arguments, PropertiesText.LaunchArguments);
            Grid.SetRow(_arguments, 3);
            root.Children.Add(_arguments);

            AddLabel(root, 4, PropertiesText.LaunchWorkingDirectory);
            _workingDirectory = new TextBox { Text = profile.WorkingDirectory, Margin = new Thickness(0, 2, 0, 10) };
            System.Windows.Automation.AutomationProperties.SetName(_workingDirectory, PropertiesText.LaunchWorkingDirectory);
            Grid.SetRow(_workingDirectory, 5);
            root.Children.Add(_workingDirectory);

            AddLabel(root, 6, PropertiesText.LaunchEnvironment);
            _environment = new TextBox
            {
                Text = profile.Environment,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(0, 2, 0, 10),
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            };
            System.Windows.Automation.AutomationProperties.SetName(_environment, PropertiesText.LaunchEnvironment);
            Grid.SetRow(_environment, 7);
            root.Children.Add(_environment);

            var bottom = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 12, 0, 0) };
            _backtrace = new CheckBox { Content = PropertiesText.LaunchBacktrace, IsChecked = profile.Backtrace, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(_backtrace, Dock.Left);
            bottom.Children.Add(_backtrace);
            var cancel = new Button { Content = PropertiesText.Cancel, MinWidth = 75, MinHeight = 23, IsCancel = true };
            DockPanel.SetDock(cancel, Dock.Right);
            var ok = new Button { Content = PropertiesText.OK, MinWidth = 75, MinHeight = 23, IsDefault = true, Margin = new Thickness(0, 0, 7, 0) };
            ok.Click += (s, e) => DialogResult = true;
            DockPanel.SetDock(ok, Dock.Right);
            bottom.Children.Add(cancel);
            bottom.Children.Add(ok);
            Grid.SetRow(bottom, 8);
            root.Children.Add(bottom);

            Content = root;
        }

        /// <summary>The edited values (valid after <c>ShowModal() == true</c>).</summary>
        public RustLaunchProfile Result => new RustLaunchProfile
        {
            Arguments = _arguments.Text,
            WorkingDirectory = _workingDirectory.Text.Trim(),
            Environment = _environment.Text,
            Backtrace = _backtrace.IsChecked == true,
        };

        private static void AddLabel(Grid grid, int row, string text)
        {
            var label = new TextBlock { Text = text };
            Grid.SetRow(label, row);
            grid.Children.Add(label);
        }
    }
}
