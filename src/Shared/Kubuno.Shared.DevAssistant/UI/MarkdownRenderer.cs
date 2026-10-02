using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using Kubuno.Shared.DevAssistant.Logic.Markdown;
using Microsoft.VisualStudio.PlatformUI;

namespace Kubuno.Shared.DevAssistant.UI
{
    /// <summary>
    /// Renders Markdown into native, theme-following WPF (docs/AI-ASSISTANT.md section 5.1 and Q6): text blocks with
    /// inline styles, and code blocks with a monospace read-only box and « Copier » / « Insérer au curseur » buttons.
    /// Every colour is a Visual Studio theme key.
    /// </summary>
    internal static class MarkdownRenderer
    {
        public static FrameworkElement Render(string markdown, Action<string> insertAtCursor)
        {
            var panel = new StackPanel();
            foreach (var block in MarkdownParser.Parse(markdown))
            {
                panel.Children.Add(RenderBlock(block, insertAtCursor));
            }

            return panel;
        }

        private static FrameworkElement RenderBlock(MarkdownBlock block, Action<string> insertAtCursor)
        {
            switch (block.Kind)
            {
                case MarkdownBlockKind.Code:
                    return CodeBlock(block.Text, block.Language, insertAtCursor);
                case MarkdownBlockKind.Heading:
                    var heading = Inlines(block);
                    heading.FontWeight = FontWeights.SemiBold;
                    heading.FontSize = block.Level <= 1 ? 15 : block.Level == 2 ? 14 : 13;
                    heading.Margin = new Thickness(0, 6, 0, 4);
                    return heading;
                case MarkdownBlockKind.Bullet:
                case MarkdownBlockKind.Numbered:
                    var row = new DockPanel { Margin = new Thickness(6, 1, 0, 1) };
                    var marker = new TextBlock { Text = block.Kind == MarkdownBlockKind.Bullet ? "•" : block.Level + ".", Width = 18 };
                    DockPanel.SetDock(marker, Dock.Left);
                    row.Children.Add(marker);
                    row.Children.Add(Inlines(block));
                    return row;
                case MarkdownBlockKind.Quote:
                    var quote = Inlines(block);
                    var border = new Border { Child = quote, BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(8, 2, 0, 2), Margin = new Thickness(0, 2, 0, 2) };
                    border.SetResourceReference(Border.BorderBrushProperty, EnvironmentColors.SystemGrayTextBrushKey);
                    return border;
                case MarkdownBlockKind.Rule:
                    var rule = new Border { Height = 1, Margin = new Thickness(0, 6, 0, 6) };
                    rule.SetResourceReference(Border.BackgroundProperty, EnvironmentColors.ToolWindowBorderBrushKey);
                    return rule;
                default:
                    var paragraph = Inlines(block);
                    paragraph.Margin = new Thickness(0, 2, 0, 6);
                    return paragraph;
            }
        }

        private static TextBlock Inlines(MarkdownBlock block)
        {
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap };
            foreach (var inline in block.Inlines)
            {
                switch (inline.Kind)
                {
                    case MarkdownInlineKind.Bold:
                        text.Inlines.Add(new Bold(new Run(inline.Text)));
                        break;
                    case MarkdownInlineKind.Italic:
                        text.Inlines.Add(new Italic(new Run(inline.Text)));
                        break;
                    case MarkdownInlineKind.Code:
                        var code = new Run(inline.Text) { FontFamily = new FontFamily("Consolas") };
                        code.SetResourceReference(TextElement.BackgroundProperty, EnvironmentColors.CommandBarMenuBackgroundGradientBrushKey);
                        text.Inlines.Add(code);
                        break;
                    case MarkdownInlineKind.Link:
                        var link = new Run(inline.Text) { TextDecorations = TextDecorations.Underline, ToolTip = inline.Url };
                        link.SetResourceReference(TextElement.ForegroundProperty, EnvironmentColors.ControlLinkTextBrushKey);
                        text.Inlines.Add(link);
                        break;
                    default:
                        text.Inlines.Add(new Run(inline.Text));
                        break;
                }
            }

            return text;
        }

        private static FrameworkElement CodeBlock(string code, string? language, Action<string> insertAtCursor)
        {
            var header = new DockPanel { Margin = new Thickness(6, 2, 4, 2) };
            var copy = new Button { Content = AssistantText.Copy, Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(4, 0, 0, 0), FontSize = 11 };
            copy.Click += (_, _) =>
            {
                try
                {
                    Clipboard.SetText(code);
                }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                }
            };
            var insert = new Button { Content = AssistantText.InsertAtCursor, Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(4, 0, 0, 0), FontSize = 11 };
            insert.Click += (_, _) => insertAtCursor(code);
            AutomationProperties.SetName(copy, AssistantText.Copy);
            DockPanel.SetDock(insert, Dock.Right);
            DockPanel.SetDock(copy, Dock.Right);
            header.Children.Add(insert);
            header.Children.Add(copy);
            var label = new TextBlock { Text = string.IsNullOrEmpty(language) ? "code" : language, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 };
            label.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
            header.Children.Add(label);

            var box = new TextBox
            {
                Text = code,
                IsReadOnly = true,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                TextWrapping = TextWrapping.NoWrap,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(6, 2, 6, 6),
            };
            box.SetResourceReference(Control.ForegroundProperty, EnvironmentColors.CommandBarTextActiveBrushKey);
            box.SetResourceReference(TextBoxBase.SelectionBrushProperty, EnvironmentColors.SystemHighlightBrushKey);
            AutomationProperties.SetName(box, "code " + (language ?? string.Empty));

            var panel = new DockPanel();
            DockPanel.SetDock(header, Dock.Top);
            panel.Children.Add(header);
            panel.Children.Add(box);
            var border = new Border { Child = panel, BorderThickness = new Thickness(1), Margin = new Thickness(0, 4, 0, 8) };
            border.SetResourceReference(Border.BackgroundProperty, EnvironmentColors.CommandBarMenuBackgroundGradientBrushKey);
            border.SetResourceReference(Border.BorderBrushProperty, EnvironmentColors.CommandBarMenuBorderBrushKey);
            return border;
        }
    }
}
