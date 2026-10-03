using System.Globalization;
using Kubuno.Rust.Logic.ProjectProperties;

namespace Kubuno.Rust.ProjectSystem.ProjectProperties
{
    /// <summary>
    /// Strings the Project Properties code itself produces (computed values, dialogs), in French when Visual
    /// Studio runs in French and in English otherwise - the same rule as the localized rule files
    /// (Kubuno.Rust.Sdk's Rules\fr) and the Dependencies node (lot 10's DependenciesText).
    /// </summary>
    internal static class PropertiesText
    {
        public static bool IsFrench => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr";

        private static string T(string english, string french) => IsFrench ? french : english;

        public static string DependencySummary(DependencyCounts counts)
        {
            string crates = IsFrench
                ? Plural(counts.Normal, "dépendance", "dépendances")
                : Plural(counts.Normal, "dependency", "dependencies");
            string dev = IsFrench
                ? Plural(counts.Dev, "dépendance de développement", "dépendances de développement")
                : Plural(counts.Dev, "dev-dependency", "dev-dependencies");
            string build = IsFrench
                ? Plural(counts.Build, "dépendance de build", "dépendances de build")
                : Plural(counts.Build, "build-dependency", "build-dependencies");
            string text = $"{crates}, {dev}, {build}";
            if (counts.Path > 0)
            {
                text += IsFrench
                    ? $" (dont {Plural(counts.Path, "projet local", "projets locaux")})"
                    : $" (of which {Plural(counts.Path, "local project", "local projects")})";
            }
            return text;
        }

        private static string Plural(int count, string one, string many) =>
            count.ToString(CultureInfo.CurrentCulture) + " " + (count == 1 || (IsFrench && count == 0) ? one : many);

        public static string HostPlatform => T("host platform", "plateforme hôte");

        // ----- Install targets dialog -----
        public static string TargetsTitle => T("Rust targets", "Cibles Rust");
        public static string TargetsHeader => T("Check the targets to install with rustup (standard library for cross-compiling), uncheck the ones to remove.",
            "Cochez les cibles à installer avec rustup (bibliothèque standard pour la compilation croisée), décochez celles à supprimer.");
        public static string TargetsFilter => T("Search targets", "Rechercher des cibles");
        public static string TargetColumn => T("Target", "Cible");
        public static string InstalledColumn => T("Installed", "Installée");
        public static string TargetsLoading => T("Reading the target list (rustup target list)...", "Lecture de la liste des cibles (rustup target list)...");
        public static string TargetsApply => T("Apply", "Appliquer");
        public static string Close => T("Close", "Fermer");
        public static string Cancel => T("Cancel", "Annuler");
        public static string OK => "OK";
        public static string TargetsRunning => T("Running rustup...", "Exécution de rustup...");
        public static string TargetsDone => T("Done.", "Terminé.");
        public static string TargetsFailed(string message) => T("rustup failed: ", "Échec de rustup : ") + message;
        public static string RustupMissing => T("rustup was not found on PATH. Install Rust from https://rustup.rs, then restart Visual Studio.",
            "rustup est introuvable dans le PATH. Installez Rust depuis https://rustup.rs, puis redémarrez Visual Studio.");

        // ----- Launch profile dialog -----
        public static string LaunchTitle => T("Rust debug launch profile", "Profil de lancement du débogage Rust");
        public static string LaunchArguments => T("Command line arguments", "Arguments de ligne de commande");
        public static string LaunchWorkingDirectory => T("Working directory", "Répertoire de travail");
        public static string LaunchEnvironment => T("Environment variables (one NAME=value per line)", "Variables d'environnement (une NOM=valeur par ligne)");
        public static string LaunchBacktrace => T("Backtraces on panic (RUST_BACKTRACE=1)", "Traces d’appels en cas de panique (RUST_BACKTRACE=1)");
        public static string LaunchExecutable => T("Executable", "Exécutable");
        public static string Browse => T("Browse...", "Parcourir...");

        // ----- .kbres -----
        public static string KbresNotAvailable => T(
            "Kubuno resource files (.kbres) are not available yet. Images and strings can be placed next to the views and loaded with kubuno_desktop_ui; the resource editor is planned (docs/EVENTS.md, \"Resources\").",
            "Les fichiers de ressources Kubuno (.kbres) ne sont pas encore disponibles. Les images et chaînes peuvent être placées à côté des vues et chargées avec kubuno_desktop_ui ; l'éditeur de ressources est prévu (docs/EVENTS.md, « Resources »).");

        public static string PropertyWriteFailed(string property, string message) =>
            T($"The property '{property}' could not be saved: {message}", $"La propriété « {property} » n'a pas pu être enregistrée : {message}");
    }
}
