using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Kubuno.Shared.DevAssistant.Logic.Changes;
using Kubuno.Shared.UI;
using Microsoft.VisualStudio.PlatformUI;

namespace Kubuno.Shared.DevAssistant.UI
{
    /// <summary>
    /// « Modifications proposées » (docs/AI-ASSISTANT.md section 5.5): the files of a change set and, for the selected
    /// file, each hunk with a little context and its own « Accepter » box. Nothing is written by the dialog: OK (« Appliquer
    /// la sélection ») returns true and the caller applies the accepted hunks to the buffers as one undo unit.
    /// </summary>
    internal sealed class ChangeReviewDialog : ThemedDialog
    {
        private const int ContextLines = 2;

        private readonly ChangeSet _changes;
        private readonly ListBox _files = new ListBox { MinWidth = 220 };
        private readonly StackPanel _hunks = new StackPanel();
        private readonly TextBlock _total = new TextBlock { VerticalAlignment = VerticalAlignment.Center };

        public ChangeReviewDialog(ChangeSet changes)
        {
            _changes = changes;
            Title = AssistantText.S("Modifications proposées", "Proposed changes");
            Width = 980;
            Height = 640;
            AutomationProperties.SetAutomationId(this, "KubunoDevAssistantReview");

            foreach (var file in changes.Files.Where(f => f.Hunks.Count > 0))
            {
                var item = new ListBoxItem { Content = $"{Path.GetFileName(file.Path)}   +{file.Hunks.Sum(h => h.Added)} −{file.Hunks.Sum(h => h.Removed)}{(file.IsNewFile ? AssistantText.S(" (nouveau)", " (new)") : string.Empty)}", Tag = file, ToolTip = file.Path };
                AutomationProperties.SetAutomationId(item, "file-" + Path.GetFileName(file.Path));
                _files.Items.Add(item);
            }

            _files.SelectionChanged += (_, _) => ShowFile();
            AutomationProperties.SetAutomationId(_files, "files");

            var root = new DockPanel { Margin = new Thickness(12) };
            var acceptAll = new Button { Content = AssistantText.S("Tout accepter", "Accept all"), Padding = new Thickness(8, 2, 8, 2) };
            AutomationProperties.SetAutomationId(acceptAll, "acceptAll");
            acceptAll.Click += (_, _) => SetAll(true);
            var rejectAll = new Button { Content = AssistantText.S("Tout refuser", "Reject all"), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(7, 0, 0, 0) };
            AutomationProperties.SetAutomationId(rejectAll, "rejectAll");
            rejectAll.Click += (_, _) => SetAll(false);
            var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            top.Children.Add(acceptAll);
            top.Children.Add(rejectAll);
            _total.Margin = new Thickness(14, 0, 0, 0);
            AutomationProperties.SetAutomationId(_total, "total");
            top.Children.Add(_total);
            DockPanel.SetDock(top, Dock.Top);
            root.Children.Add(top);

            var apply = new Button { Content = AssistantText.S("Appliquer la sélection", "Apply the selection"), IsDefault = true };
            AutomationProperties.SetAutomationId(apply, "apply");
            apply.Click += (_, _) => DialogResult = true;
            var cancel = new Button { Content = AssistantText.S("Annuler", "Cancel"), IsCancel = true };
            AutomationProperties.SetAutomationId(cancel, "cancel");
            var buttons = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            var note = ThemedControls.SecondaryText(AssistantText.S("Appliqué dans les éditeurs de Visual Studio, en une seule annulation (Ctrl+Z).", "Applied in Visual Studio's editors as a single undo (Ctrl+Z)."));
            note.VerticalAlignment = VerticalAlignment.Center;
            var row = ThemedControls.ButtonRow(apply, cancel);
            row.Margin = new Thickness(0);
            DockPanel.SetDock(row, Dock.Right);
            buttons.Children.Add(row);
            buttons.Children.Add(note);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(_files, 0);
            grid.Children.Add(_files);
            var scroll = new ScrollViewer { Content = _hunks, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(10, 0, 0, 0) };
            Grid.SetColumn(scroll, 1);
            grid.Children.Add(scroll);
            root.Children.Add(grid);
            Content = root;

            if (_files.Items.Count > 0)
            {
                _files.SelectedIndex = 0;
            }

            UpdateTotal();
        }

        private void SetAll(bool accept)
        {
            foreach (var file in _changes.Files)
            {
                file.Accepted.Clear();
                if (accept)
                {
                    foreach (var hunk in file.Hunks)
                    {
                        file.Accepted.Add(hunk.Index);
                    }
                }
            }

            ShowFile();
            UpdateTotal();
        }

        private void UpdateTotal()
        {
            int total = _changes.Files.Sum(f => f.Hunks.Count);
            int accepted = _changes.Files.Sum(f => f.Accepted.Count);
            _total.Text = AssistantText.S($"{accepted} bloc(s) accepté(s) sur {total}", $"{accepted} of {total} hunk(s) accepted");
            AutomationProperties.SetName(_total, _total.Text);
        }

        private void ShowFile()
        {
            _hunks.Children.Clear();
            if ((_files.SelectedItem as ListBoxItem)?.Tag is not FileChange file)
            {
                return;
            }

            var header = new TextBlock { Text = file.Path, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
            _hunks.Children.Add(header);
            var original = LineDiff.SplitLines(file.OriginalText);
            foreach (var hunk in file.Hunks)
            {
                var box = new CheckBox
                {
                    Content = AssistantText.S(
                        $"Accepter le bloc {hunk.Index + 1} (ligne {hunk.OldStart + 1}, +{hunk.Added} −{hunk.Removed})",
                        $"Accept hunk {hunk.Index + 1} (line {hunk.OldStart + 1}, +{hunk.Added} −{hunk.Removed})"),
                    IsChecked = file.Accepted.Contains(hunk.Index),
                    Margin = new Thickness(0, 6, 0, 4),
                };
                AutomationProperties.SetAutomationId(box, "hunk-" + hunk.Index);
                var index = hunk.Index;
                box.Checked += (_, _) =>
                {
                    file.Accepted.Add(index);
                    UpdateTotal();
                };
                box.Unchecked += (_, _) =>
                {
                    file.Accepted.Remove(index);
                    UpdateTotal();
                };
                _hunks.Children.Add(box);
                _hunks.Children.Add(DiffBlock(original, hunk));
            }
        }

        private static Border DiffBlock(System.Collections.Generic.IReadOnlyList<string> original, DiffHunk hunk)
        {
            var text = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 12 };
            void Line(string prefix, string line, bool removed, bool added)
            {
                var run = new Run(prefix + line.TrimEnd('\r', '\n') + "\n");
                if (removed)
                {
                    run.TextDecorations = TextDecorations.Strikethrough;
                    run.SetResourceReference(TextElement.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
                }
                else if (added)
                {
                    run.FontWeight = FontWeights.SemiBold;
                    run.SetResourceReference(TextElement.ForegroundProperty, EnvironmentColors.ControlLinkTextBrushKey);
                }

                text.Inlines.Add(run);
            }

            for (int i = Math.Max(0, hunk.OldStart - ContextLines); i < hunk.OldStart; i++)
            {
                Line("  ", original[i], false, false);
            }

            foreach (var line in hunk.OldLines)
            {
                Line("− ", line, true, false);
            }

            foreach (var line in hunk.NewLines)
            {
                Line("+ ", line, false, true);
            }

            for (int i = hunk.OldStart + hunk.OldCount; i < Math.Min(original.Count, hunk.OldStart + hunk.OldCount + ContextLines); i++)
            {
                Line("  ", original[i], false, false);
            }

            var border = new Border { Child = text, Padding = new Thickness(8, 4, 8, 4), BorderThickness = new Thickness(1) };
            border.SetResourceReference(Border.BackgroundProperty, EnvironmentColors.CommandBarMenuBackgroundGradientBrushKey);
            border.SetResourceReference(Border.BorderBrushProperty, EnvironmentColors.CommandBarMenuBorderBrushKey);
            text.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.CommandBarTextActiveBrushKey);
            return border;
        }
    }
}
