using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Kubuno.Shared.UI;

namespace Kubuno.Shared.DevAssistant.UI
{
    /// <summary>
    /// « Voir la requête » (docs/AI-ASSISTANT.md section 8.2): the exact request bodies the host sent for one answer -
    /// system prompt, tool list and messages - as captured from the HTTP layer, already masked. The API key is a header
    /// and never appears here.
    /// </summary>
    internal sealed class RequestViewerDialog : ThemedDialog
    {
        public RequestViewerDialog(IReadOnlyList<string> bodies)
        {
            Title = AssistantText.ViewRequest;
            Width = 900;
            Height = 640;
            AutomationProperties.SetAutomationId(this, "KubunoDevAssistantRequest");

            var root = new DockPanel { Margin = new Thickness(12) };
            var note = ThemedControls.SecondaryText(AssistantText.S(
                "Corps exact envoyé au fournisseur, appel par appel (les secrets détectés sont déjà masqués ; la clé API voyage dans un en-tête et n'apparaît jamais ici).",
                "Exact body sent to the provider, call by call (detected secrets are already masked; the API key travels in a header and never appears here)."));
            note.Margin = new Thickness(0, 0, 0, 4);
            DockPanel.SetDock(note, Dock.Top);
            root.Children.Add(note);

            // What the masking replaced, so the developer can check it at a glance (the values themselves never appear).
            var placeholders = bodies
                .SelectMany(b => Regex.Matches(b,"«secret:[a-z\\-]+#[0-9a-f]{6}»").Cast<Match>().Select(m => m.Value))
                .Distinct()
                .ToList();
            var masked = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
                FontWeight = FontWeights.SemiBold,
                Text = placeholders.Count == 0
                    ? AssistantText.S("Aucun secret détecté dans cette requête.", "No secret detected in this request.")
                    : AssistantText.S("Secrets masqués dans cette requête : ", "Secrets masked in this request: ") + string.Join(", ", placeholders),
            };
            AutomationProperties.SetAutomationId(masked, "maskedSummary");
            AutomationProperties.SetName(masked, masked.Text);
            DockPanel.SetDock(masked, Dock.Top);
            root.Children.Add(masked);

            var close = new Button { Content = AssistantText.S("Fermer", "Close"), IsCancel = true, IsDefault = true };
            AutomationProperties.SetAutomationId(close, "close");
            var buttons = ThemedControls.ButtonRow(close);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            var text = new TextBox
            {
                Text = bodies.Count == 0 ? AssistantText.S("(aucune requête pour cette réponse)", "(no request for this answer)") : string.Join("\n\n", bodies),
                IsReadOnly = true,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                TextWrapping = TextWrapping.NoWrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalContentAlignment = VerticalAlignment.Top,
            };
            AutomationProperties.SetAutomationId(text, "requestBody");
            root.Children.Add(text);
            Content = root;
        }
    }
}
