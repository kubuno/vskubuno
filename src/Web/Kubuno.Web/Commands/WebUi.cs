using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Kubuno.Core.UI;
using Kubuno.Web.Logic.Generation;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Kubuno.Web.Commands
{
    /// <summary>Shared UI of the web commands: which repository they act on, messages, folder choice.</summary>
    internal static class WebUi
    {
        private const string Title = "Kubuno";

        /// <summary>
        /// The Kubuno repository of the active document, else of the open solution, else one the user picks (unless
        /// <paramref name="quiet"/>). Null when none.
        /// </summary>
        public static WebRepository? ResolveRepository(AsyncPackage package, string? browseTitle, bool quiet = false)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var starts = new List<string>();
            if (Package.GetGlobalService(typeof(EnvDTE.DTE)) is EnvDTE.DTE dte)
            {
                try
                {
                    if (dte.ActiveDocument?.FullName is { Length: > 0 } document)
                    {
                        starts.Add(Path.GetDirectoryName(document)!);
                    }

                    if (dte.Solution?.FullName is { Length: > 0 } solution)
                    {
                        starts.Add(Path.GetDirectoryName(solution)!);
                    }
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    // No document or solution.
                }
            }

            foreach (var start in starts)
            {
                for (var directory = start; directory is not null; directory = Path.GetDirectoryName(directory))
                {
                    var repository = WebRepository.Detect(directory, out _);
                    if (repository is not null)
                    {
                        return repository;
                    }
                }
            }

            if (quiet || browseTitle is null)
            {
                return null;
            }

            var chosen = BrowseFolder(package, browseTitle);
            if (chosen is null)
            {
                return null;
            }

            var picked = WebRepository.Detect(chosen, out var reason);
            if (picked is null)
            {
                ShowMessage(package, "Not a Kubuno repository: " + reason, error: true);
            }

            return picked;
        }

        /// <summary>The open solution's folder when it holds the core repository (a multi-repository solution such as <c>Z:\src\Kubuno.Web.slnx</c>), else null.</summary>
        public static string? OpenSolutionFolderWithCore()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (Package.GetGlobalService(typeof(EnvDTE.DTE)) is EnvDTE.DTE dte && dte.Solution?.FullName is { Length: > 0 } solution)
                {
                    var folder = Path.GetDirectoryName(solution)!;
                    return WebRepository.Detect(Path.Combine(folder, "core"), out _) is not null ? folder : null;
                }
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // No solution.
            }

            return null;
        }

        public static string? BrowseFolder(AsyncPackage package, string title)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = title, ShowNewFolderButton = false };
            return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
        }

        public static void ShowMessage(AsyncPackage package, string message, bool error = false)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            VsShellUtilities.ShowMessageBox(package, message, Title, error ? OLEMSGICON.OLEMSGICON_WARNING : OLEMSGICON.OLEMSGICON_INFO, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }

        public static bool Ask(AsyncPackage package, string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return VsShellUtilities.ShowMessageBox(package, message, Title, OLEMSGICON.OLEMSGICON_QUERY, OLEMSGBUTTON.OLEMSGBUTTON_YESNO, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST) == 6; // IDYES
        }
    }

    /// <summary>The checked list of repositories of the multi-repository solution (Visual Studio-themed).</summary>
    internal sealed class RepositoryPickerDialog : ThemedDialog
    {
        private readonly List<CheckBox> _boxes = new List<CheckBox>();

        public RepositoryPickerDialog(string parent, IReadOnlyList<(string Name, string Description, bool Selected)> repositories)
        {
            Title = "Kubuno Core Web: Multi-Repository Solution";
            Width = 520;
            Height = 480;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            HasMaximizeButton = false;

            var root = new DockPanel { Margin = new Thickness(12) };
            var caption = new TextBlock
            {
                Text = "Repositories to put in " + Path.Combine(parent, "Kubuno.Web.slnx") + ". Visual Studio's Git tools follow every repository of the solution.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
            };
            DockPanel.SetDock(caption, Dock.Top);
            root.Children.Add(caption);

            var ok = new Button { Content = "Generate", IsDefault = true };
            var cancel = new Button { Content = "Cancel", IsCancel = true };
            ok.Click += (_, _) => DialogResult = true;
            var buttons = ThemedControls.ButtonRow(ok, cancel);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            var list = new StackPanel();
            foreach (var repository in repositories)
            {
                var box = new CheckBox { Content = repository.Name, IsChecked = repository.Selected, Margin = new Thickness(4, 3, 4, 0), Tag = repository.Name };
                _boxes.Add(box);
                list.Children.Add(box);
                var description = ThemedControls.SecondaryText(repository.Description);
                description.Margin = new Thickness(24, 0, 4, 3);
                list.Children.Add(description);
            }

            var frame = new Border { BorderThickness = new Thickness(1), Child = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
            frame.SetResourceReference(Border.BorderBrushProperty, ThemedDialogColors.ListBoxBorderBrushKey);
            frame.SetResourceReference(Border.BackgroundProperty, ThemedDialogColors.ListBoxBrushKey);
            root.Children.Add(frame);
            Content = root;
        }

        public IReadOnlyCollection<string> SelectedNames => new HashSet<string>(_boxes.Where(box => box.IsChecked == true).Select(box => (string)box.Tag), StringComparer.OrdinalIgnoreCase);
    }
}
