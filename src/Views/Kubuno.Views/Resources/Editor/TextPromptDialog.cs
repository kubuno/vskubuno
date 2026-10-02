using System;
using System.Windows;
using System.Windows.Controls;
using Kubuno.Shared.UI;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Views.Resources.Editor
{
    /// <summary>
    /// A small themed prompt: one line of text with a label and an inline validation message (a culture name, a new
    /// resource name, a comment). Derives from <see cref="ThemedDialog"/> like every dialog of the extension.
    /// </summary>
    internal sealed class TextPromptDialog : ThemedDialog
    {
        private readonly TextBox _box;
        private readonly TextBlock _error;
        private readonly Func<string, string?>? _validate;

        private TextPromptDialog(string title, string prompt, string initial, Func<string, string?>? validate)
        {
            _validate = validate;
            Title = title;
            Width = 400;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;

            var panel = new StackPanel { Margin = new Thickness(12) };
            panel.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            _box = new TextBox { Text = initial };
            System.Windows.Automation.AutomationProperties.SetName(_box, prompt);
            panel.Children.Add(_box);
            _error = new TextBlock { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
            _error.SetResourceReference(ForegroundProperty, VsBrushes.ControlLinkTextKey);
            panel.Children.Add(_error);

            var ok = new Button { Content = ResourceText.Ok, IsDefault = true };
            var cancel = new Button { Content = ResourceText.Cancel, IsCancel = true };
            ok.Click += (_, _) =>
            {
                var message = _validate?.Invoke(_box.Text);
                if (message is null)
                {
                    DialogResult = true;
                    return;
                }

                _error.Text = message;
                _error.Visibility = Visibility.Visible;
            };
            panel.Children.Add(ThemedControls.ButtonRow(ok, cancel));
            Content = panel;
            Loaded += (_, _) =>
            {
                _box.Focus();
                _box.SelectAll();
            };
        }

        /// <summary>Asks for a line of text; <paramref name="validate"/> returns an error message or null when acceptable. False when cancelled.</summary>
        public static bool TryAsk(string title, string prompt, string initial, Func<string, string?>? validate, out string value)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dialog = new TextPromptDialog(title, prompt, initial, validate);
            var accepted = dialog.ShowModal() == true;
            value = accepted ? dialog._box.Text : initial;
            return accepted;
        }
    }
}
