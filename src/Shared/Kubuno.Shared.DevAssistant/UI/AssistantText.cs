using Kubuno.Shared.Logic.Localization;

namespace Kubuno.Shared.DevAssistant.UI
{
    /// <summary>UI strings of the assistant, French or English after Visual Studio's UI language.</summary>
    internal static class AssistantText
    {
        public static bool IsFrench => UiLanguage.IsFrench;

        public static string S(string french, string english) => IsFrench ? french : english;

        public static string WindowTitle => S("Assistant de développement Kubuno", "Kubuno Dev Assistant");

        public static string You => S("Vous", "You");

        public static string Assistant => S("Assistant", "Assistant");

        public static string Send => S("Envoyer", "Send");

        public static string Stop => S("Arrêter", "Stop");

        public static string NewConversation => S("Nouvelle conversation", "New conversation");

        public static string History => S("Historique", "History");

        public static string SettingsTitle => S("Paramètres de l'assistant", "Assistant settings");

        public static string InputPlaceholder => S("Décrivez ce que vous voulez… (# pour une référence, / pour une commande)", "Describe what you want… (# for a reference, / for a command)");

        public static string ViewRequest => S("Voir la requête", "View request");

        public static string ReviewChanges => S("Examiner les modifications", "Review changes");

        public static string Copy => S("Copier", "Copy");

        public static string InsertAtCursor => S("Insérer au curseur", "Insert at cursor");

        public static string Thinking => S("Réflexion", "Reasoning");

        public static string Effort(string effort) => effort switch
        {
            "low" => S("faible", "low"),
            "high" => S("élevé", "high"),
            _ => S("moyen", "medium"),
        };
    }
}
