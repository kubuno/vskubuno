using System;
using System.Globalization;

namespace Kubuno.Rust.Logic.SolutionExplorer
{
    /// <summary>
    /// User-visible strings of the Dependencies node, its commands, the Properties window rows and the
    /// Reference/Crate managers, in the two UI languages this extension is used with. The French names
    /// are the ones Visual Studio's own .NET project system uses ("Dépendances", "Projets", "Chemin
    /// d'accès"...), so a Rust project reads like a C# one. Picked from Visual Studio's UI culture
    /// (<see cref="CultureInfo.CurrentUICulture"/>); a small code table, like <c>DesignerText</c>.
    /// </summary>
    public static class DependenciesText
    {
        /// <summary>Test seam: forces a language instead of reading the current UI culture.</summary>
        public static bool? ForceFrench { get; set; }

        public static bool IsFrench => ForceFrench ?? string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "fr", StringComparison.OrdinalIgnoreCase);

        public static string T(string english, string french) => IsFrench ? french : english;

        // Tree.
        public static string Dependencies => T("Dependencies", "Dépendances");

        public static string ProcMacros => T("Procedural macros", "Macros procédurales");

        public static string Toolchain => T("Toolchain", "Chaîne d'outils");

        public static string Crates => T("Crates", "Crates");

        public static string Projects => T("Projects", "Projets");

        public static string Git => T("Git", "Git");

        public static string Loading => T("Loading...", "Chargement...");

        public static string DevBadge => "dev";

        public static string BuildBadge => "build";

        public static string DependenciesToolTip => T(
            "Cargo dependencies of Cargo.toml, resolved by cargo metadata.",
            "Dépendances Cargo de Cargo.toml, résolues par cargo metadata.");

        public static string UnresolvedToolTip => T(
            "This dependency could not be resolved. Build the project or run \"cargo fetch\" for details.",
            "Cette dépendance n'a pas pu être résolue. Générez le projet ou exécutez \"cargo fetch\" pour plus de détails.");

        public static string InactiveToolTip => T(
            "Optional dependency, not enabled by any active feature.",
            "Dépendance facultative, activée par aucune fonctionnalité active.");

        public static string OtherPlatformToolTip(string target) => T(
            $"Only used when building for {target}, not for this platform.",
            $"Utilisée seulement pour {target}, pas pour cette plateforme.");

        public static string MissingPathToolTip(string path) => T(
            $"The folder of this path dependency does not exist: {path}",
            $"Le dossier de cette dépendance de chemin n'existe pas : {path}");

        public static string YankedToolTip(string version) => T(
            $"Version {version} has been yanked from crates.io.",
            $"La version {version} a été retirée (yanked) de crates.io.");

        public static string UpdateAvailableToolTip(string version) => T(
            $"An update is available: {version}.",
            $"Une mise à jour est disponible : {version}.");

        public static string MetadataFailed(string message) => T(
            $"cargo metadata failed: {message}",
            $"cargo metadata a échoué : {message}");

        // Context menus (the English texts are also the .vsct fallbacks).
        public static string AddProjectReferenceCommand => T("Add Project Reference...", "Ajouter une référence de projet...");

        public static string ManageCratesCommand => T("Manage Crates...", "Gérer les crates...");

        public static string UpdateAllCommand => T("Update Crates", "Mettre à jour les crates");

        public static string RemoveUnusedCommand => T("Remove Unused Dependencies...", "Supprimer les dépendances inutilisées...");

        public static string OpenDocumentationCommand => T("Open Documentation", "Ouvrir la documentation");

        public static string OpenSourceCommand => T("Open Source Code", "Ouvrir le code source");

        public static string UpdateCommand => T("Update", "Mettre à jour");

        public static string RemoveCommand => T("Remove", "Supprimer");

        public static string CopyPathCommand => T("Copy Full Path", "Copier le chemin d'accès complet");

        public static string OpenFolderCommand => T("Open Folder in File Explorer", "Ouvrir le dossier dans l'Explorateur de fichiers");

        // Messages.
        public static string CargoFailed(string command) => T(
            $"{command} failed - see the \"Kubuno\" Output pane for details.",
            $"{command} a échoué - voir le panneau de sortie \"Kubuno\" pour le détail.");

        public static string ConfirmRemove(string name) => T(
            $"'{name}' will be removed from Cargo.toml (cargo remove).",
            $"'{name}' va être supprimée de Cargo.toml (cargo remove).");

        public static string NoUnusedTool => T(
            "Finding unused dependencies needs cargo-machete (or cargo-udeps, which needs a nightly toolchain), and neither is installed.\n\nInstall one of them from a terminal, then try again:\n\n    cargo install cargo-machete\n\n    cargo install cargo-udeps --locked   (then: rustup toolchain install nightly)",
            "La recherche des dépendances inutilisées nécessite cargo-machete (ou cargo-udeps, qui demande une chaîne d'outils nightly), et aucun des deux n'est installé.\n\nInstallez l'un des deux depuis un terminal, puis réessayez :\n\n    cargo install cargo-machete\n\n    cargo install cargo-udeps --locked   (puis : rustup toolchain install nightly)");

        public static string NoUnusedFound => T(
            "No unused dependency was found.",
            "Aucune dépendance inutilisée n'a été trouvée.");

        public static string UnusedToolFailed(string tool) => T(
            $"{tool} failed - see the \"Kubuno\" Output pane for details.",
            $"{tool} a échoué - voir le panneau de sortie \"Kubuno\" pour le détail.");

        public static string NoOtherProjects => T(
            "No other .rsproj project was found in the solution. Use Browse... to reference a crate folder.",
            "Aucun autre projet .rsproj n'a été trouvé dans la solution. Utilisez Parcourir... pour référencer un dossier de crate.");

        public static string NoFolder => T("This dependency has no folder on disk yet.", "Cette dépendance n'a pas encore de dossier sur le disque.");

        // Reference Manager.
        public static string ReferenceManagerTitle(string project) => T($"Reference Manager - {project}", $"Gestionnaire de références - {project}");

        public static string SolutionNode => T("Solution", "Solution");

        public static string BrowseNode => T("Browse", "Parcourir");

        public static string RecentNode => T("Recent", "Récent");

        public static string NameColumn => T("Name", "Nom");

        public static string PathColumn => T("Path", "Chemin d'accès");

        public static string VersionColumn => T("Version", "Version");

        public static string SearchPlaceholder => T("Search", "Rechercher");

        public static string BrowseButton => T("Browse...", "Parcourir...");

        public static string BrowseDialogTitle => T("Select the Cargo.toml of the crate to reference", "Sélectionnez le Cargo.toml de la crate à référencer");

        public static string CargoManifestFilter => T("Cargo manifest (Cargo.toml)|Cargo.toml", "Manifeste Cargo (Cargo.toml)|Cargo.toml");

        public static string NoItemSelected => T("No item selected", "Aucun élément sélectionné");

        public static string NoItemsFound => T("No items found.", "Aucun élément trouvé.");

        public static string ReferenceDetails(string name, string path) => T($"Name:\n{name}\n\nPath:\n{path}", $"Nom :\n{name}\n\nChemin d'accès :\n{path}");

        public static string ReferenceFailures(int count, string commands) => T(
            $"{count} cargo command(s) failed - see the \"Kubuno\" Output pane for details.\n\n{commands}",
            $"{count} commande(s) cargo ont échoué - voir le panneau de sortie \"Kubuno\" pour le détail.\n\n{commands}");

        // Crate manager (the NuGet Package Manager's counterpart).
        public static string CrateManagerCaption(string project) => T($"Crates: {project}", $"Crates : {project}");

        public static string CrateManagerHeader(string project) => T($"Crate Manager: {project}", $"Gestionnaire de crates : {project}");

        public static string BrowseTab => T("Browse", "Parcourir");

        public static string InstalledTab => T("Installed", "Installé");

        public static string UpdatesTab(int count) => count > 0 ? T($"Updates {count}", $"Mises à jour {count}") : T("Updates", "Mises à jour");

        public static string IncludePrerelease => T("Include prerelease", "Inclure la préversion");

        public static string Refresh => T("Refresh", "Actualiser");

        public static string SearchCratesIo => T("Search crates.io", "Rechercher sur crates.io");

        public static string Installed => T("Installed:", "Installé :");

        public static string VersionLabel => T("Version:", "Version :");

        public static string KindLabel => T("Dependency type:", "Type de dépendance :");

        public static string KindNormalShort => T("Normal", "Normale");

        public static string KindDevShort => T("Development", "Développement");

        public static string KindBuildShort => T("Build", "Build");

        public static string DefaultFeatures => T("Default features", "Fonctionnalités par défaut");

        public static string FeaturesLabel => T("Features:", "Fonctionnalités :");

        public static string NoFeatures => T("This version has no optional features.", "Cette version n'a pas de fonctionnalité facultative.");

        public static string Install => T("Install", "Installer");

        public static string Uninstall => T("Uninstall", "Désinstaller");

        public static string UpdateButton => T("Update", "Mettre à jour");

        public static string UpdateAllButton => T("Update all", "Tout mettre à jour");

        public static string DescriptionLabel => T("Description", "Description");

        public static string LicenseLabel => T("License:", "Licence :");

        public static string DownloadsLabel => T("Downloads:", "Téléchargements :");

        public static string RepositoryLabel => T("Repository:", "Dépôt :");

        public static string DocumentationLabel => T("Documentation:", "Documentation :");

        public static string CratesIoPage => T("crates.io page", "Page crates.io");

        public static string MinimumRustLabel => T("Minimum Rust version:", "Version minimale de Rust :");

        public static string Downloads(string count) => T($"{count} downloads", $"{count} téléchargements");

        public static string LatestVersion(string version) => T($"Latest: {version}", $"Dernière : {version}");

        public static string YankedSuffix => T(" (yanked)", " (retirée)");

        public static string Searching => T("Searching crates.io...", "Recherche sur crates.io...");

        public static string LoadingInstalled => T("Reading the project's dependencies...", "Lecture des dépendances du projet...");

        public static string CheckingUpdates => T("Checking crates.io for updates...", "Recherche de mises à jour sur crates.io...");

        public static string NoResults => T("No crates found.", "Aucune crate trouvée.");

        public static string NoUpdates => T("All crates are up to date.", "Toutes les crates sont à jour.");

        public static string NoInstalled => T("No crates.io dependency in this project.", "Aucune dépendance crates.io dans ce projet.");

        public static string Offline(string message) => T(
            $"crates.io is unreachable ({message}). Installed crates are still listed.",
            $"crates.io est injoignable ({message}). Les crates installées restent listées.");

        public static string Working(string command) => T($"Running {command}...", $"Exécution de {command}...");

        // Remove Unused Dependencies dialog.
        public static string RemoveUnusedTitle => T("Remove Unused Dependencies", "Supprimer les dépendances inutilisées");

        public static string RemoveUnusedCaption(string tool) => T(
            $"{tool} found these dependencies unused. Check the ones to remove from Cargo.toml (static analysis: a dependency only used through a macro may be reported by mistake).",
            $"{tool} a trouvé ces dépendances inutilisées. Cochez celles à supprimer de Cargo.toml (analyse statique : une dépendance utilisée seulement via une macro peut être signalée à tort).");

        public static string Ok => T("OK", "OK");

        public static string Cancel => T("Cancel", "Annuler");

        public static string Remove => T("Remove", "Supprimer");

        // Properties window.
        public static string CrateClassName => T("Crate reference properties", "Propriétés de référence de crate");

        public static string ToolchainClassName => T("Toolchain properties", "Propriétés de la chaîne d'outils");

        public static string Misc => T("Misc", "Divers");

        public static string Yes => T("True", "True");

        public static string No => T("False", "False");

        public static string KindNormal => T("Normal", "Normale");

        public static string KindDev => T("Development (dev-dependencies)", "Développement (dev-dependencies)");

        public static string KindBuild => T("Build script (build-dependencies)", "Script de build (build-dependencies)");

        public static string SourceRegistry(string registry) => T($"Registry ({registry})", $"Registre ({registry})");

        public static string SourcePath => T("Path", "Chemin");

        public static string SourceGit(string url, string? reference) => reference is null ? $"Git ({url})" : $"Git ({url}, {reference})";

        public static string SourceSysroot => T("Rust toolchain (sysroot)", "Chaîne d'outils Rust (sysroot)");
    }
}
