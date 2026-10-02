using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using EnvDTE;
using EnvDTE80;
using Kubuno.Shared.DevAssistant.Logic.Changes;
using Kubuno.Shared.DevAssistant.Logic.Cost;
using Kubuno.Shared.DevAssistant.Logic.Prompts;
using Kubuno.Shared.DevAssistant.Logic.Protocol;
using Kubuno.Shared.DevAssistant.Session;
using Kubuno.Shared.DevAssistant.Settings;
using Kubuno.Shared.Logging;
using Kubuno.Shared.UI;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;

namespace Kubuno.Shared.DevAssistant.UI
{
    /// <summary>
    /// The content of the « Assistant de développement Kubuno » tool window (docs/AI-ASSISTANT.md section 5.1), built in
    /// code with native WPF and Visual Studio theme keys only: toolbar (new conversation, history, model, effort,
    /// settings), destination badge and cost, the streamed transcript (Markdown, code blocks, tool cards, change-set
    /// cards, « Voir la requête »), and the input with <c>#</c> / <c>/</c> completion, reference chips, Send and Stop.
    /// </summary>
    internal sealed class AssistantControl : UserControl, IAssistantView, IDisposable
    {
        private readonly AssistantController _controller;
        private readonly JoinableTaskFactory _jtf;
        private readonly StackPanel _transcript = new StackPanel { Margin = new Thickness(8, 4, 8, 8) };
        private readonly ScrollViewer _scroll;
        private readonly TextBox _input = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 54, MaxHeight = 200, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        private readonly Button _send = new Button();
        private readonly Button _stop = new Button();
        private readonly ComboBox _models = new ComboBox { MinWidth = 150, MaxWidth = 240 };
        private readonly ComboBox _effort = new ComboBox { MinWidth = 70 };
        private readonly TextBlock _badge = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        private readonly TextBlock _cost = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _status = new TextBlock { Margin = new Thickness(8, 2, 8, 2), TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly WrapPanel _chips = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        private readonly Popup _completion = new Popup { StaysOpen = false, Placement = PlacementMode.Top, AllowsTransparency = false };
        private readonly ListBox _completionList = new ListBox { MinWidth = 260, MaxHeight = 220 };
        private readonly DispatcherTimer _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        private readonly DispatcherTimer _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private AnswerItem? _answer;
        private int _answerCount;
        private DateTime _turnStarted;
        private bool _updatingModels;

        public AssistantControl(JoinableTaskFactory jtf)
        {
            _jtf = jtf;
            _controller = new AssistantController(this, jtf);
            ThemedControls.AddImplicitStyles(Resources);
            SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
            SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            TextElementForeground(this);
            AutomationProperties.SetAutomationId(this, "KubunoDevAssistant");

            var root = new DockPanel();

            // ---- Toolbar
            var toolbar = new DockPanel { Margin = new Thickness(6, 6, 6, 2) };
            var newConversation = ToolButton("+", AssistantText.NewConversation, "newConversation");
            newConversation.Click += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                _controller.StartNewConversation();
            };
            var history = ToolButton("⏱", AssistantText.History, "history");
            history.Click += (_, _) => ShowHistory(history);
            var settings = ToolButton("⚙", AssistantText.SettingsTitle, "settings");
            settings.Click += (_, _) => OpenSettings();
            DockPanel.SetDock(settings, Dock.Right);
            DockPanel.SetDock(history, Dock.Right);
            DockPanel.SetDock(newConversation, Dock.Right);
            toolbar.Children.Add(settings);
            toolbar.Children.Add(history);
            toolbar.Children.Add(newConversation);
            var pickers = new StackPanel { Orientation = Orientation.Horizontal };
            AutomationProperties.SetAutomationId(_models, "model");
            AutomationProperties.SetName(_models, AssistantText.S("Modèle", "Model"));
            _models.SelectionChanged += (_, _) =>
            {
                if (!_updatingModels && (_models.SelectedItem as ComboBoxItem)?.Tag is string id)
                {
                    _controller.Model = id;
                }
            };
            foreach (var effort in new[] { "low", "medium", "high" })
            {
                _effort.Items.Add(new ComboBoxItem { Content = AssistantText.S("effort : ", "effort: ") + AssistantText.Effort(effort), Tag = effort });
            }

            AutomationProperties.SetAutomationId(_effort, "effort");
            _effort.SelectionChanged += (_, _) =>
            {
                if ((_effort.SelectedItem as ComboBoxItem)?.Tag is string effort)
                {
                    _controller.Effort = effort;
                }
            };
            _effort.Margin = new Thickness(6, 0, 0, 0);
            pickers.Children.Add(_models);
            pickers.Children.Add(_effort);
            toolbar.Children.Add(pickers);
            DockPanel.SetDock(toolbar, Dock.Top);
            root.Children.Add(toolbar);

            var infoBar = new WrapPanel { Margin = new Thickness(8, 2, 8, 4) };
            AutomationProperties.SetAutomationId(_badge, "badge");
            AutomationProperties.SetAutomationId(_cost, "cost");
            _cost.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
            infoBar.Children.Add(_badge);
            infoBar.Children.Add(_cost);
            DockPanel.SetDock(infoBar, Dock.Top);
            root.Children.Add(infoBar);
            var separator = new Border { Height = 1 };
            separator.SetResourceReference(Border.BackgroundProperty, EnvironmentColors.ToolWindowBorderBrushKey);
            DockPanel.SetDock(separator, Dock.Top);
            root.Children.Add(separator);

            // ---- Input
            var inputArea = new DockPanel { Margin = new Thickness(6, 4, 6, 6) };
            _send.Content = AssistantText.Send;
            _send.Padding = new Thickness(10, 2, 10, 2);
            AutomationProperties.SetAutomationId(_send, "send");
            _send.Click += (_, _) => SendCurrent();
            _stop.Content = "■ " + AssistantText.Stop;
            _stop.Padding = new Thickness(8, 2, 8, 2);
            _stop.Margin = new Thickness(6, 0, 0, 0);
            _stop.IsEnabled = false;
            AutomationProperties.SetAutomationId(_stop, "stop");
            _stop.Click += (_, _) => _controller.Cancel();
            var hash = ToolButton("#", AssistantText.S("Insérer une référence", "Insert a reference"), "insertReference");
            hash.Click += (_, _) => ShowCompletion("#", fromButton: true);
            var slash = ToolButton("/", AssistantText.S("Insérer une commande", "Insert a command"), "insertCommand");
            slash.Click += (_, _) => ShowCompletion("/", fromButton: true);
            var buttons = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(hash);
            left.Children.Add(slash);
            DockPanel.SetDock(left, Dock.Left);
            buttons.Children.Add(left);
            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            right.Children.Add(_send);
            right.Children.Add(_stop);
            buttons.Children.Add(right);
            DockPanel.SetDock(buttons, Dock.Bottom);
            DockPanel.SetDock(_chips, Dock.Bottom);
            inputArea.Children.Add(buttons);
            inputArea.Children.Add(_chips);
            AutomationProperties.SetAutomationId(_input, "input");
            inputArea.Children.Add(ThemedControls.WithPlaceholder(_input, AssistantText.InputPlaceholder));
            _input.PreviewKeyDown += OnInputKeyDown;
            _input.TextChanged += (_, _) => OnInputChanged();
            DockPanel.SetDock(inputArea, Dock.Bottom);
            root.Children.Add(inputArea);
            AutomationProperties.SetAutomationId(_status, "status");
            _status.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
            DockPanel.SetDock(_status, Dock.Bottom);
            root.Children.Add(_status);

            // ---- Transcript
            AutomationProperties.SetAutomationId(_transcript, "transcript");
            _scroll = new ScrollViewer { Content = _transcript, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            root.Children.Add(_scroll);
            Content = root;

            _completionList.SetResourceReference(BackgroundProperty, EnvironmentColors.CommandBarMenuBackgroundGradientBrushKey);
            _completionList.SetResourceReference(ForegroundProperty, EnvironmentColors.CommandBarTextActiveBrushKey);
            AutomationProperties.SetAutomationId(_completionList, "completion");
            _completion.Child = _completionList;
            _completion.PlacementTarget = _input;
            _completionList.PreviewMouseLeftButtonUp += (_, _) => AcceptCompletion();
            _completionList.PreviewKeyDown += (_, e) =>
            {
                if (e.Key is Key.Enter or Key.Tab)
                {
                    AcceptCompletion();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    _completion.IsOpen = false;
                    _input.Focus();
                    e.Handled = true;
                }
            };

            _renderTimer.Tick += (_, _) =>
            {
                _renderTimer.Stop();
                _answer?.Render();
                ScrollToEnd();
            };
            _elapsedTimer.Tick += (_, _) => _status.Text = AssistantText.S("Réponse en cours… ", "Answering… ") + (int)(DateTime.Now - _turnStarted).TotalSeconds + " s";

            ShowWelcome();
            Loaded += (_, _) => _ = _jtf.RunAsync(async () =>
            {
                try
                {
                    await _controller.InitializeAsync();
                    await _jtf.SwitchToMainThreadAsync();
                    SelectEffort(_controller.Effort);
                }
                catch (Exception exception)
                {
                    KubunoLog.WriteException("Kubuno Dev Assistant: initialization failed", exception);
                    await _jtf.SwitchToMainThreadAsync();
                    ShowNotice(exception.Message);
                }
            });
        }

        private static void TextElementForeground(FrameworkElement element) =>
            element.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);

        private static Button ToolButton(string glyph, string tooltip, string automationId)
        {
            var button = new Button { Content = glyph, ToolTip = tooltip, MinWidth = 26, Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(4, 0, 0, 0) };
            AutomationProperties.SetAutomationId(button, automationId);
            AutomationProperties.SetName(button, tooltip);
            return button;
        }

        private void SelectEffort(string effort)
        {
            foreach (ComboBoxItem item in _effort.Items)
            {
                if ((string)item.Tag == effort)
                {
                    _effort.SelectedItem = item;
                }
            }
        }

        private void ShowWelcome()
        {
            var welcome = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 8),
                Text = AssistantText.S(
                    "Posez une question sur le code Kubuno, ou utilisez /vue pour créer ou modifier une vue, /expliquer pour une erreur. Joignez du contexte avec #fichier, #sélection ou #élément. Les secrets détectés sont masqués avant l'envoi ; les modifications proposées sont examinées bloc par bloc avant d'être appliquées.",
                    "Ask about the Kubuno code, or use /vue to create or change a view, /expliquer for an error. Attach context with #file, #selection or #element. Detected secrets are masked before sending; proposed changes are reviewed hunk by hunk before they are applied."),
            };
            welcome.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
            _transcript.Children.Add(welcome);
        }

        // ---------------- IAssistantView

        public void Clear()
        {
            _transcript.Children.Clear();
            _answer = null;
            _answerCount = 0;
            _cost.Text = string.Empty;
            ShowWelcome();
        }

        public void AddUserMessage(string text, IReadOnlyList<string> references)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 4) };
            panel.Children.Add(new TextBlock { Text = AssistantText.You, FontWeight = FontWeights.SemiBold });
            panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            if (references.Count > 0)
            {
                var refs = new TextBlock { Text = string.Join("   ", references), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
                refs.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
                AutomationProperties.SetAutomationId(refs, "references");
                panel.Children.Add(refs);
            }

            var border = new Border { Child = panel, Padding = new Thickness(8, 4, 8, 6), BorderThickness = new Thickness(0, 0, 0, 1) };
            border.SetResourceReference(Border.BorderBrushProperty, EnvironmentColors.ToolWindowBorderBrushKey);
            AutomationProperties.SetAutomationId(border, "userMessage");
            _transcript.Children.Add(border);
            ScrollToEnd();
        }

        public void BeginAnswer()
        {
            _answer = new AnswerItem(++_answerCount, InsertAtCursor);
            _transcript.Children.Add(_answer.Root);
            ScrollToEnd();
        }

        public void AppendDelta(string kind, string text, int round)
        {
            _answer?.Append(kind, text, round);
            if (!_renderTimer.IsEnabled)
            {
                _renderTimer.Start();
            }
        }

        public void AddToolCall(string toolUseId, string name, string summary)
        {
            _answer?.AddTool(toolUseId, name, summary);
            ScrollToEnd();
        }

        public void CompleteToolCall(string toolUseId, bool isError, string summary) => _answer?.CompleteTool(toolUseId, isError, summary);

        public void UpdateCost(decimal sessionCostUsd, UsageInfo lastUsage)
        {
            var culture = CultureInfo.CurrentUICulture;
            var hit = CostLedger.CacheHitRate(lastUsage);
            var input = lastUsage.InputTokens + lastUsage.CacheReadInputTokens + lastUsage.CacheCreationInputTokens;
            _cost.Text = string.Format(culture, "{0:0.000} $ · {1:N0} k↑ {2:N0} ↓ · cache {3:P0} ({4})", sessionCostUsd, input / 1000.0, lastUsage.OutputTokens, hit, AssistantText.S("estimation", "estimate"));
            AutomationProperties.SetName(_cost, _cost.Text);
        }

        public void EndAnswer(string stopReason, string? error, ChangeSet? changes, IReadOnlyList<string> requestBodies)
        {
            if (_answer is null)
            {
                return;
            }

            _renderTimer.Stop();
            _answer.Render();
            _answer.Finish(stopReason, error, changes, requestBodies, Review, ViewRequest);
            ScrollToEnd();
        }

        public void ShowNotice(string text)
        {
            var notice = MarkdownRenderer.Render(text, InsertAtCursor);
            notice.Margin = new Thickness(0, 6, 0, 6);
            _transcript.Children.Add(notice);
            ScrollToEnd();
        }

        public void SetBusy(bool busy)
        {
            _send.IsEnabled = !busy;
            _stop.IsEnabled = busy;
            if (busy)
            {
                _turnStarted = DateTime.Now;
                _status.Text = AssistantText.S("Réponse en cours…", "Answering…");
                _elapsedTimer.Start();
            }
            else
            {
                _elapsedTimer.Stop();
                _status.Text = string.Empty;
            }
        }

        public void ShowModels(ModelsListResult models, string selected)
        {
            _updatingModels = true;
            try
            {
                _models.Items.Clear();
                foreach (var model in models.Models)
                {
                    var item = new ComboBoxItem { Content = model.DisplayName, Tag = model.Id, ToolTip = model.Id };
                    _models.Items.Add(item);
                    if (model.Id == selected)
                    {
                        _models.SelectedItem = item;
                    }
                }

                if (_models.SelectedItem is null && _models.Items.Count > 0)
                {
                    _models.SelectedIndex = 0;
                    _controller.Model = (string)((ComboBoxItem)_models.SelectedItem!).Tag;
                }
            }
            finally
            {
                _updatingModels = false;
            }

            _badge.Text = (models.IsLocal ? AssistantText.S("● Local : ", "● Local: ") : AssistantText.S("● Cloud : ", "● Cloud: ")) + models.Endpoint;
            _badge.ToolTip = models.Error is null
                ? (models.FromProvider ? AssistantText.S("Modèles lus par l'API du fournisseur", "Models read from the provider's API") : null)
                : AssistantText.S("Liste hors ligne : ", "Offline list: ") + models.Error;
            AutomationProperties.SetName(_badge, _badge.Text);
        }

        // ---------------- Input

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (_completion.IsOpen && e.Key is Key.Down or Key.Up)
            {
                _completionList.Focus();
                if (_completionList.SelectedIndex < 0 && _completionList.Items.Count > 0)
                {
                    _completionList.SelectedIndex = 0;
                }

                e.Handled = true;
                return;
            }

            if (_completion.IsOpen && e.Key is Key.Tab)
            {
                AcceptCompletion();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape && _completion.IsOpen)
            {
                _completion.IsOpen = false;
                e.Handled = true;
                return;
            }

            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            bool control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (e.Key == Key.Enter && ((_controller.Settings.SendWithEnter && !shift && !control) || (!_controller.Settings.SendWithEnter && control)))
            {
                e.Handled = true;
                SendCurrent();
            }
        }

        private void OnInputChanged()
        {
            UpdateChips();
            var caret = _input.CaretIndex;
            if (caret > 0 && caret <= _input.Text.Length)
            {
                var previous = _input.Text[caret - 1];
                var atWordStart = caret == 1 || char.IsWhiteSpace(_input.Text[caret - 2]);
                if (atWordStart && (previous == '#' || (previous == '/' && caret == 1)))
                {
                    ShowCompletion(previous.ToString(), fromButton: false);
                    return;
                }
            }

            _completion.IsOpen = false;
        }

        private void ShowCompletion(string trigger, bool fromButton)
        {
            _completionList.Items.Clear();
            if (trigger == "#")
            {
                foreach (var (token, description) in new[]
                {
                    ("#fichier", AssistantText.S("le document actif (ou #fichier:chemin)", "the active document (or #fichier:path)")),
                    ("#sélection", AssistantText.S("la sélection de l'éditeur", "the editor selection")),
                    ("#élément", AssistantText.S("l'élément sélectionné dans le concepteur", "the element selected in the designer")),
                })
                {
                    _completionList.Items.Add(new ListBoxItem { Content = token + "  —  " + description, Tag = token });
                }
            }
            else
            {
                foreach (var command in _controller.Commands)
                {
                    _completionList.Items.Add(new ListBoxItem { Content = "/" + command.Name + "  —  " + (AssistantText.IsFrench ? command.DescriptionFr : command.DescriptionEn), Tag = "/" + command.Name });
                }
            }

            _completionList.Tag = fromButton ? null : trigger;
            _completionList.SelectedIndex = 0;
            _completion.IsOpen = _completionList.Items.Count > 0;
        }

        private void AcceptCompletion()
        {
            if ((_completionList.SelectedItem as ListBoxItem)?.Tag is not string token)
            {
                return;
            }

            _completion.IsOpen = false;
            var caret = _input.CaretIndex;
            var text = _input.Text;
            if (_completionList.Tag is string trigger && caret > 0 && text.Substring(caret - 1, 1) == trigger)
            {
                text = text.Remove(caret - 1, 1);
                caret--;
            }

            var insertion = token + " ";
            _input.Text = text.Insert(caret, insertion);
            _input.CaretIndex = caret + insertion.Length;
            _input.Focus();
        }

        private void UpdateChips()
        {
            _chips.Children.Clear();
            var parsed = PromptParser.Parse(_input.Text, _controller.CommandAliases());
            if (parsed.Command is { } command)
            {
                _chips.Children.Add(Chip("/" + command, null));
            }

            foreach (var reference in parsed.References)
            {
                _chips.Children.Add(Chip(reference.ToString(), reference));
            }
        }

        private Border Chip(string text, PromptReference? reference)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            if (reference is not null)
            {
                var remove = new Button { Content = "×", Padding = new Thickness(3, -2, 3, -1), Margin = new Thickness(4, 0, 0, 0), MinWidth = 0, MinHeight = 0, FontSize = 10, ToolTip = AssistantText.S("Retirer", "Remove") };
                remove.Click += (_, _) =>
                {
                    var token = reference.ToString();
                    var index = _input.Text.IndexOf(token, StringComparison.OrdinalIgnoreCase);
                    if (index >= 0)
                    {
                        _input.Text = _input.Text.Remove(index, token.Length);
                    }
                };
                panel.Children.Add(remove);
            }

            var chip = new Border { Child = panel, Padding = new Thickness(6, 1, 4, 1), Margin = new Thickness(0, 0, 4, 2), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2) };
            chip.SetResourceReference(Border.BorderBrushProperty, EnvironmentColors.ToolWindowBorderBrushKey);
            AutomationProperties.SetName(chip, text);
            return chip;
        }

        private void SendCurrent()
        {
            var text = _input.Text;
            if (string.IsNullOrWhiteSpace(text) || _controller.IsBusy)
            {
                return;
            }

            _input.Clear();
            _ = _jtf.RunAsync(async () =>
            {
                try
                {
                    await _controller.SendAsync(text);
                }
                catch (Exception exception)
                {
                    KubunoLog.WriteException("Kubuno Dev Assistant: send failed", exception);
                    await _jtf.SwitchToMainThreadAsync();
                    ShowNotice(exception.Message);
                }
            });
        }

        // ---------------- Actions

        private void OpenSettings()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var settings = DevAssistantSettings.Load();
            if (new DevAssistantSettingsDialog(settings).ShowModal() == true)
            {
                _controller.ApplySettings(settings);
                SelectEffort(settings.Effort);
            }
        }

        private void ShowHistory(FrameworkElement anchor)
        {
            var store = _controller.Store;
            var list = new ListBox { MinWidth = 320, MaxHeight = 360 };
            list.SetResourceReference(BackgroundProperty, EnvironmentColors.CommandBarMenuBackgroundGradientBrushKey);
            list.SetResourceReference(ForegroundProperty, EnvironmentColors.CommandBarTextActiveBrushKey);
            AutomationProperties.SetAutomationId(list, "historyList");
            var popup = new Popup { Child = list, PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false };
            var entries = store?.List() ?? Array.Empty<Logic.Conversations.ConversationSummary>();
            foreach (var entry in entries)
            {
                list.Items.Add(new ListBoxItem
                {
                    Content = string.Format(CultureInfo.CurrentUICulture, "{0}   ({1:g}, {2}, {3:0.000} $)", string.IsNullOrEmpty(entry.Title) ? "…" : entry.Title, entry.UpdatedUtc.ToLocalTime(), entry.Model, entry.CostUsd),
                    Tag = entry.Id,
                });
            }

            if (entries.Count > 0)
            {
                list.Items.Add(new ListBoxItem { Content = AssistantText.S("Tout effacer…", "Delete all…"), Tag = "*" });
            }
            else
            {
                list.Items.Add(new ListBoxItem { Content = AssistantText.S("(aucune conversation enregistrée pour cette solution)", "(no saved conversation for this solution)"), IsEnabled = false });
            }

            list.SelectionChanged += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if ((list.SelectedItem as ListBoxItem)?.Tag is not string id)
                {
                    return;
                }

                popup.IsOpen = false;
                if (id == "*")
                {
                    var answer = VsShellUtilities.ShowMessageBox(ServiceProvider.GlobalProvider, AssistantText.S("Effacer toutes les conversations de cette solution ?", "Delete every conversation of this solution?"), AssistantText.WindowTitle, Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_QUERY, Microsoft.VisualStudio.Shell.Interop.OLEMSGBUTTON.OLEMSGBUTTON_YESNO, Microsoft.VisualStudio.Shell.Interop.OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND);
                    if (answer == 6)
                    {
                        store?.DeleteAll();
                        _controller.StartNewConversation();
                    }

                    return;
                }

                _controller.LoadConversation(id);
            };
            popup.IsOpen = true;
        }

        private void Review(ChangeSet changes, AnswerItem answer)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (new ChangeReviewDialog(changes).ShowModal() != true)
            {
                return;
            }

            ApplyReport report;
            try
            {
                report = _controller.Apply(changes);
            }
            catch (Exception exception)
            {
                KubunoLog.WriteException("Kubuno Dev Assistant: applying the changes failed", exception);
                answer.AddFooterLine(AssistantText.S("Échec de l'application : ", "Apply failed: ") + exception.Message);
                return;
            }

            var line = AssistantText.S(
                $"{report.AppliedHunks} bloc(s) appliqué(s) dans {report.FilesTouched} fichier(s) - Ctrl+Z annule l'ensemble.",
                $"{report.AppliedHunks} hunk(s) applied to {report.FilesTouched} file(s) - Ctrl+Z undoes all of it.");
            foreach (var conflict in report.Conflicts)
            {
                line += AssistantText.S($"\nConflit, non appliqué : {System.IO.Path.GetFileName(conflict.Path)} bloc {conflict.HunkIndex + 1} ({conflict.Reason}).", $"\nConflict, not applied: {System.IO.Path.GetFileName(conflict.Path)} hunk {conflict.HunkIndex + 1} ({conflict.Reason}).");
            }

            answer.AddFooterLine(line);
        }

        private void ViewRequest(IReadOnlyList<string> bodies) => new RequestViewerDialog(bodies).ShowModal();

        private void InsertAtCursor(string code)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Package.GetGlobalService(typeof(DTE)) is DTE2 { ActiveDocument: { } document } && document.Selection is TextSelection selection)
            {
                selection.Insert(code);
                document.Activate();
            }
        }

        private void ScrollToEnd() => _scroll.ScrollToEnd();

        public void Dispose()
        {
            _renderTimer.Stop();
            _elapsedTimer.Stop();
            _controller.Dispose();
        }

        /// <summary>One answer of the transcript: text segments (one per model call), tool cards, footer.</summary>
        private sealed class AnswerItem
        {
            private readonly Action<string> _insertAtCursor;
            private readonly StackPanel _body = new StackPanel();
            private readonly StackPanel _footer = new StackPanel();
            private readonly List<Segment> _segments = new List<Segment>();
            private readonly Dictionary<string, TextBlock> _toolStatus = new Dictionary<string, TextBlock>(StringComparer.Ordinal);
            private readonly StringBuilder _thinking = new StringBuilder();
            private readonly Expander _thinkingExpander;
            private readonly TextBlock _thinkingText = new TextBlock { TextWrapping = TextWrapping.Wrap, FontStyle = FontStyles.Italic };

            public AnswerItem(int number, Action<string> insertAtCursor)
            {
                Number = number;
                _insertAtCursor = insertAtCursor;
                var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 4) };
                panel.Children.Add(new TextBlock { Text = AssistantText.Assistant, FontWeight = FontWeights.SemiBold });
                _thinkingText.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
                _thinkingExpander = new Expander { Header = AssistantText.Thinking, Content = _thinkingText, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 2, 0, 2) };
                _thinkingExpander.SetResourceReference(ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
                panel.Children.Add(_thinkingExpander);
                panel.Children.Add(_body);
                panel.Children.Add(_footer);
                Root = new Border { Child = panel, Padding = new Thickness(8, 4, 8, 6) };
                AutomationProperties.SetAutomationId(Root, "answer-" + number);
            }

            public int Number { get; }

            public Border Root { get; }

            public void Append(string kind, string text, int round)
            {
                if (kind == ChatBlockTypes.Thinking)
                {
                    _thinking.Append(text);
                    _thinkingExpander.Visibility = Visibility.Visible;
                    return;
                }

                var last = _segments.LastOrDefault();
                if (last is null || last.Round != round || last.Tool is not null)
                {
                    last = new Segment { Round = round, Host = new ContentControl() };
                    _segments.Add(last);
                    _body.Children.Add(last.Host);
                }

                last.Text.Append(text);
                last.Dirty = true;
            }

            public void AddTool(string toolUseId, string name, string summary)
            {
                var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14, 0, 0, 0) };
                status.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
                var card = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
                card.Children.Add(new TextBlock { Text = "▸ " + name + (summary.Length > 0 ? "  (" + summary + ")" : string.Empty), TextWrapping = TextWrapping.Wrap, FontFamily = new System.Windows.Media.FontFamily("Consolas"), FontSize = 11.5 });
                card.Children.Add(status);
                AutomationProperties.SetAutomationId(card, "tool-" + name);
                _toolStatus[toolUseId] = status;
                _segments.Add(new Segment { Tool = name });
                _body.Children.Add(card);
            }

            public void CompleteTool(string toolUseId, bool isError, string summary)
            {
                if (_toolStatus.TryGetValue(toolUseId, out var status))
                {
                    status.Text = (isError ? "✗ " : "✓ ") + summary;
                }
            }

            public void Render()
            {
                _thinkingText.Text = _thinking.ToString();
                foreach (var segment in _segments.Where(s => s.Dirty && s.Host is not null))
                {
                    segment.Dirty = false;
                    segment.Host!.Content = MarkdownRenderer.Render(segment.Text.ToString(), _insertAtCursor);
                }
            }

            public void AddFooterLine(string text)
            {
                var line = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
                AutomationProperties.SetAutomationId(line, "applyResult");
                AutomationProperties.SetName(line, text);
                _footer.Children.Add(line);
            }

            public void Finish(string stopReason, string? error, ChangeSet? changes, IReadOnlyList<string> requestBodies, Action<ChangeSet, AnswerItem> review, Action<IReadOnlyList<string>> viewRequest)
            {
                if (error is not null || stopReason is not (StopReasons.EndTurn))
                {
                    var note = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
                    note.Text = error ?? stopReason switch
                    {
                        StopReasons.Cancelled => AssistantText.S("Arrêté.", "Stopped."),
                        StopReasons.CostCap => AssistantText.S("Arrêté : plafond de coût de la session atteint (voir les paramètres).", "Stopped: the session's cost cap was reached (see the settings)."),
                        StopReasons.MaxRounds => AssistantText.S("Arrêté : nombre maximal d'appels atteint.", "Stopped: maximum number of calls reached."),
                        StopReasons.MaxTokens => AssistantText.S("Réponse tronquée (limite de jetons).", "Answer cut (token limit)."),
                        StopReasons.Refusal => AssistantText.S("Le modèle a refusé la demande.", "The model declined the request."),
                        _ => stopReason,
                    };
                    note.SetResourceReference(TextBlock.ForegroundProperty, error is null ? EnvironmentColors.SystemGrayTextBrushKey : EnvironmentColors.ControlLinkTextBrushKey);
                    AutomationProperties.SetAutomationId(note, "stopNote");
                    AutomationProperties.SetName(note, note.Text);
                    _footer.Children.Add(note);
                }

                var actions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
                if (changes is not null)
                {
                    var files = string.Join(", ", changes.Files.Where(f => f.Hunks.Count > 0).Select(f => $"{System.IO.Path.GetFileName(f.Path)} (+{f.Hunks.Sum(h => h.Added)} −{f.Hunks.Sum(h => h.Removed)})"));
                    var summary = new TextBlock { Text = AssistantText.S("Modifications proposées : ", "Proposed changes: ") + files, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
                    _footer.Children.Add(summary);
                    var reviewButton = new Button { Content = AssistantText.ReviewChanges, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 0) };
                    AutomationProperties.SetAutomationId(reviewButton, "review-" + Number);
                    reviewButton.Click += (_, _) => review(changes, this);
                    actions.Children.Add(reviewButton);
                }

                var request = new Button { Content = AssistantText.ViewRequest, Padding = new Thickness(8, 2, 8, 2) };
                AutomationProperties.SetAutomationId(request, "viewRequest-" + Number);
                request.Click += (_, _) => viewRequest(requestBodies);
                request.IsEnabled = requestBodies.Count > 0;
                actions.Children.Add(request);
                _footer.Children.Add(actions);
            }

            private sealed class Segment
            {
                public int Round { get; set; }

                public string? Tool { get; set; }

                public ContentControl? Host { get; set; }

                public StringBuilder Text { get; } = new StringBuilder();

                public bool Dirty { get; set; }
            }
        }
    }
}
