using Kubuno.Shared.Logic.Localization;

namespace Kubuno.Views.Settings
{
    /// <summary>
    /// The user-visible strings of the <c>.kbsettings</c> settings editor (docs/STORAGE-COMPONENTS.md §5.3): English and
    /// French, picked from the extension's UI language - same pattern as <c>Resources\ResourceText</c>.
    /// </summary>
    public static class SettingsText
    {
        private static string T(string english, string french) => UiLanguage.IsFrench ? french : english;

        public static string EditorTitle => T("Kubuno Settings Editor", "Éditeur de paramètres Kubuno");

        public static string AddSetting => T("Add Setting", "Ajouter un paramètre");

        public static string RemoveSetting => T("Remove Setting", "Supprimer le paramètre");

        public static string ViewCode => T("View Code", "Afficher le code");

        public static string Version => T("Version:", "Version :");

        public static string VersionTip => T(
            "The schema version: raise it when you rename (add the old name to Previous Names), retype or remove a setting; the stored values are upgraded when the app opens them.",
            "La version du schéma : augmentez-la quand vous renommez (ajoutez l'ancien nom aux Noms précédents), changez le type ou supprimez un paramètre ; les valeurs enregistrées sont mises à niveau à l'ouverture par l'application.");

        public static string App => T("App id:", "Id d'application :");

        public static string AppTip => T(
            "The app the values are stored under (lower case: kubuno-notes). Empty: the Cargo package name.",
            "L'application sous laquelle les valeurs sont enregistrées (en minuscules : kubuno-notes). Vide : le nom du paquet Cargo.");

        public static string AccountScoped => T("Per account", "Par compte");

        public static string AccountScopedTip => T(
            "The values belong to the signed-in Kubuno account: each account has its own, and signing out deletes them.",
            "Les valeurs appartiennent au compte Kubuno connecté : chaque compte a les siennes, et la déconnexion les supprime.");

        public static string ColumnName =>T("Name", "Nom");

        public static string ColumnType => T("Type", "Type");

        public static string ColumnScope => T("Scope", "Portée");

        public static string ColumnRoaming => T("Roaming", "Itinérant");

        public static string ColumnValue => T("Value", "Valeur");

        public static string ColumnValues => T("Accepted Values", "Valeurs admises");

        public static string ColumnPreviousNames => T("Previous Names", "Noms précédents");

        public static string ColumnDescription => T("Description", "Description");

        public static string Hint => T(
            "Settings of the app: typed values with a default, User (read-write, per user; Roaming follows the user between machines) or Application (machine-wide, read-only). Code: kubuno_desktop::settings!(\"{0}\") generates the typed class; views bind them through a <Settings> component. Lists: items separated by |.",
            "Paramètres de l'application : valeurs typées avec un défaut, Utilisateur (lecture-écriture, par utilisateur ; Itinérant suit l'utilisateur d'une machine à l'autre) ou Application (pour toute la machine, lecture seule). Code : kubuno_desktop::settings!(\"{0}\") génère la classe typée ; les vues s'y lient par un composant <Settings>. Listes : éléments séparés par |.");

        public static string Problems(int errors, int warnings) => T($"{errors} error(s), {warnings} warning(s): ", $"{errors} erreur(s), {warnings} avertissement(s) : ");

        public static string NoProblem(int count) => T($"{count} setting(s).", $"{count} paramètre(s).");

        public static string InvalidFile => T("The file is not valid XML, fix it in the code view:", "Le fichier n'est pas un XML valide, corrigez-le dans la vue code :");

        public static string ReadOnlyFile => T("The file is read-only.", "Le fichier est en lecture seule.");
    }
}
