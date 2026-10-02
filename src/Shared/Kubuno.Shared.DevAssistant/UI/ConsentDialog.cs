using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Kubuno.Shared.UI;

namespace Kubuno.Shared.DevAssistant.UI
{
    /// <summary>
    /// The data notice shown before the first message to a cloud provider (docs/AI-ASSISTANT.md section 8.2): where the
    /// data goes, what is sent, what is not, and that nothing goes to Kubuno or anyone else.
    /// </summary>
    internal sealed class ConsentDialog : ThemedDialog
    {
        public ConsentDialog()
        {
            Title = AssistantText.S("Envoi de données à Anthropic", "Sending data to Anthropic");
            Width = 560;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            AutomationProperties.SetAutomationId(this, "KubunoDevAssistantConsent");

            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock
            {
                Text = AssistantText.S("Destination : api.anthropic.com (cloud)", "Destination: api.anthropic.com (cloud)"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8),
            });
            panel.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = AssistantText.S(
                    "Sont envoyés : vos messages, les références que vous joignez (#fichier, #sélection, #élément), le contenu des fichiers que l'assistant lit avec ses outils, les résultats de ces outils et le condensé des règles Kubuno.\n\n" +
                    "Ne sont pas envoyés : les secrets détectés (remplacés par «secret:…» avant l'envoi), les fichiers refusés (.env, clés, certificats…) et tout ce qui est hors de la solution.\n\n" +
                    "Rien n'est envoyé à Kubuno ni à quiconque d'autre ; il n'y a aucune télémétrie. « Voir la requête » montre exactement ce qui part. Un secret d'un format inconnu peut échapper au détecteur : relisez « Voir la requête » en cas de doute.\n\n" +
                    "Les données sont traitées selon la politique d'Anthropic (https://www.anthropic.com/legal/privacy).",
                    "Sent: your messages, the references you attach (#file, #selection, #element), the content of files the assistant reads with its tools, those tools' results and the Kubuno rules digest.\n\n" +
                    "Not sent: detected secrets (replaced by «secret:…» before sending), denied files (.env, keys, certificates…) and anything outside the solution.\n\n" +
                    "Nothing is sent to Kubuno or anyone else; there is no telemetry. “View request” shows exactly what leaves. A secret in an unknown format can escape the detector: check “View request” when in doubt.\n\n" +
                    "The data is processed under Anthropic's policy (https://www.anthropic.com/legal/privacy)."),
            });

            var accept = new Button { Content = AssistantText.S("J'ai compris, envoyer", "I understand, send"), IsDefault = true };
            AutomationProperties.SetAutomationId(accept, "accept");
            accept.Click += (_, _) => DialogResult = true;
            var cancel = new Button { Content = AssistantText.S("Annuler", "Cancel"), IsCancel = true };
            panel.Children.Add(ThemedControls.ButtonRow(accept, cancel));
            Content = panel;
        }
    }
}
