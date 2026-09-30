using System;
using System.Globalization;

namespace Kubuno.VisualStudio.Core.Migrations
{
    /// <summary>
    /// User-visible strings of the migration and SQLx cache tooling (docs/DATA.md DATA-7), in English and French, picked
    /// from the UI culture like <c>DataText</c>.
    /// </summary>
    public static class MigrationText
    {
        /// <summary>Test seam: forces a language instead of reading the current UI culture.</summary>
        public static bool? ForceFrench { get; set; }

        public static bool IsFrench => ForceFrench ?? string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "fr", StringComparison.OrdinalIgnoreCase);

        public static CultureInfo Culture => IsFrench ? CultureInfo.GetCultureInfo("fr-FR") : CultureInfo.GetCultureInfo("en-US");

        public static string T(string english, string french) => IsFrench ? french : english;

        // Menus and commands.
        public static string DatabaseMenu => T("Database", "Base de données");

        public static string AddMigrationCommand => T("Add Migration...", "Ajouter une migration...");

        public static string RunMigrationsCommand => T("Apply Migrations", "Appliquer les migrations");

        public static string RevertMigrationCommand => T("Revert Last Migration", "Annuler la dernière migration");

        public static string PrepareSqlxCommand => T("Update SQLx Cache", "Mettre à jour le cache SQLx");

        public static string RefreshStatusCommand => T("Refresh Status", "Actualiser l'état");

        public static string OpenMigrationCommand => T("Open", "Ouvrir");

        // Solution Explorer.
        public static string MigrationsNode => "Migrations";

        public static string MigrationsNodePending(int pending) => pending <= 0
            ? MigrationsNode
            : T($"Migrations ({pending} pending)", $"Migrations ({pending} en attente)");

        public static string MigrationsToolTip => T(
            "The sqlx migrations of this crate (migrations folder) and their state in the database of its connection.",
            "Les migrations sqlx de ce crate (dossier migrations) et leur état dans la base de données de sa connexion.");

        public static string Loading => T("Loading...", "Chargement...");

        public static string NoMigrations => T("No migration yet: use Database > Add Migration...", "Aucune migration : utilisez Base de données > Ajouter une migration...");

        public static string Unavailable(string message) => T($"Status unavailable: {message}", $"État indisponible : {message}");

        public static string StateApplied => T("Applied", "Appliquée");

        public static string StateAppliedAt(string when) => T($"Applied {when}", $"Appliquée le {when}");

        public static string StatePending => T("Pending", "En attente");

        public static string StateChecksumMismatch => T("Checksum changed", "Somme de contrôle modifiée");

        public static string StateFailed => T("Failed (dirty)", "Échec (incomplète)");

        public static string StateMissing => T("Applied, file missing", "Appliquée, fichier absent");

        public static string ChecksumToolTip => T(
            "This migration was edited after it was applied: the database no longer matches the file. Revert it and apply it again, or restore the file (sqlx refuses to run the migrations while a checksum differs).",
            "Cette migration a été modifiée après avoir été appliquée : la base ne correspond plus au fichier. Annulez-la puis réappliquez-la, ou restaurez le fichier (sqlx refuse d'exécuter les migrations tant qu'une somme de contrôle diffère).");

        public static string FailedToolTip => T(
            "This migration failed part-way: fix the database by hand, then remove its row from _sqlx_migrations.",
            "Cette migration a échoué en cours de route : réparez la base à la main puis supprimez sa ligne de _sqlx_migrations.");

        public static string MissingToolTip => T(
            "Applied in the database but its file is no longer in the migrations folder.",
            "Appliquée dans la base mais son fichier n'est plus dans le dossier migrations.");

        public static string NotReversible => T("no .down.sql: cannot be reverted", "pas de .down.sql : ne peut pas être annulée");

        public static string SqlxUpToDate(int queryFiles) => T(
            $"SQLx cache: up to date ({queryFiles} {(queryFiles == 1 ? "query" : "queries")})",
            $"Cache SQLx : à jour ({queryFiles} {(queryFiles == 1 ? "requête" : "requêtes")})");

        public static string SqlxStale(string reason) => T($"SQLx cache: stale ({reason})", $"Cache SQLx : périmé ({reason})");

        public static string SqlxStaleShort => T("SQLx cache: stale", "Cache SQLx : périmé");

        public static string SqlxToolTip => T(
            "The offline query cache (.sqlx) the data_source! macro checks the queries against at build time. Update it after changing a .kbdata file or adding a migration.",
            "Le cache de requêtes hors ligne (.sqlx) avec lequel la macro data_source! vérifie les requêtes à la compilation. Mettez-le à jour après avoir modifié un fichier .kbdata ou ajouté une migration.");

        // Connections.
        public static string NoConnection => T(
            "no connection: add a data source (a .kbdata file naming its connection) or run Database > Refresh Status to choose one",
            "aucune connexion : ajoutez une source de données (un fichier .kbdata qui nomme sa connexion) ou choisissez-en une avec Base de données > Actualiser l'état");

        public static string SeveralConnections(string names) => T(
            $"several connections ({names}): choose one with Database > Refresh Status",
            $"plusieurs connexions ({names}) : choisissez-en une avec Base de données > Actualiser l'état");

        public static string ConnectionDialogTitle => T("Migrations: Connection", "Migrations : connexion");

        public static string ConnectionDialogIntro(string crate) => T(
            $"Which connection do the migrations of \"{crate}\" apply to? The name is looked up as ConnectionStrings:<name> in the crate's user secrets (or the environment variable ConnectionStrings__<name>, or the Windows Credential Manager).",
            $"À quelle connexion s'appliquent les migrations de « {crate} » ? Le nom est cherché comme ConnectionStrings:<nom> dans les secrets utilisateur du crate (ou la variable d'environnement ConnectionStrings__<nom>, ou le Gestionnaire d'informations d'identification Windows).");

        public static string ConnectionNameLabel => T("Connection name:", "Nom de la connexion :");

        public static string ConnectionNameInvalid => T(
            "A connection name is 1 to 64 letters, digits, spaces, '_', '.' or '-'.",
            "Un nom de connexion comporte 1 à 64 lettres, chiffres, espaces, « _ », « . » ou « - ».");

        public static string SetConnectionStringLink => T("Set the connection string...", "Définir la chaîne de connexion...");

        public static string SetConnectionStringIntro(string key) => T(
            $"Copy a Data Explorer connection's string into this crate's user secrets as {key}:",
            $"Copier la chaîne d'une connexion de l'Explorateur de données dans les secrets utilisateur de ce crate, sous {key} :");

        public static string ExplorerConnectionLabel => T("Data Explorer connection:", "Connexion de l'Explorateur de données :");

        public static string CopyToSecrets => T("Copy to User Secrets", "Copier dans les secrets utilisateur");

        public static string NoExplorerConnections => T(
            "The Data Explorer has no connection yet (View > Data Explorer > Add Connection...).",
            "L'Explorateur de données n'a encore aucune connexion (Affichage > Explorateur de données > Ajouter une connexion...).");

        public static string NoUserSecretsId => T(
            "This crate has no user secrets id: add [package.metadata.kubuno] user-secrets-id = \"...\" to its Cargo.toml.",
            "Ce crate n'a pas d'identifiant de secrets utilisateur : ajoutez [package.metadata.kubuno] user-secrets-id = \"...\" à son Cargo.toml.");

        public static string SecretCopied(string key) => T($"Stored as {key} in the crate's user secrets.", $"Enregistrée sous {key} dans les secrets utilisateur du crate.");

        public static string ChooseConnectionIntro(string crate) => T(
            $"The data sources of \"{crate}\" use several connections. Which one do the migrations apply to?",
            $"Les sources de données de « {crate} » utilisent plusieurs connexions. À laquelle s'appliquent les migrations ?");

        // Add Migration dialog.
        public static string AddMigrationTitle => T("Add Migration", "Ajouter une migration");

        public static string DescriptionLabel => T("Description:", "Description :");

        public static string ReversibleLabel => T("Reversible (up/down)", "Réversible (up/down)");

        public static string AddButton => T("Add", "Ajouter");

        public static string OkButton => "OK";

        public static string CancelButton => T("Cancel", "Annuler");

        public static string FilesPreview(string files) => T($"Creates {files}", $"Crée {files}");

        public static string SchemaNote(string schema) => T(
            $"The first migration of the module also creates its schema: CREATE SCHEMA IF NOT EXISTS {schema};",
            $"La première migration du module crée aussi son schéma : CREATE SCHEMA IF NOT EXISTS {schema};");

        public static string DescriptionEmpty => T("Enter a description (e.g. create customers).", "Saisissez une description (par ex. create customers).");

        public static string DescriptionTooLong => T("A description is at most 100 characters.", "Une description comporte au plus 100 caractères.");

        public static string DescriptionInvalidCharacters => T(
            "Use only letters (without accents), digits, spaces, '_' and '-'.",
            "Utilisez seulement des lettres (sans accents), des chiffres, des espaces, « _ » et « - ».");

        public static string DescriptionNeedsAlphanumeric => T("A description needs at least one letter or digit.", "Une description doit contenir au moins une lettre ou un chiffre.");

        // Results.
        public static string Added(string file) => T($"Migration added: {file}", $"Migration ajoutée : {file}");

        public static string AppliedNone => T("No pending migration: the database is up to date.", "Aucune migration en attente : la base est à jour.");

        public static string AppliedSome(int count, string versions) => T(
            $"{count} migration(s) applied: {versions}",
            $"{count} migration(s) appliquée(s) : {versions}");

        public static string RevertedNone => T("No applied migration to revert.", "Aucune migration appliquée à annuler.");

        public static string Reverted(string migration) => T($"Migration reverted: {migration}", $"Migration annulée : {migration}");

        public static string ConfirmRevert(string migration, string connection) => T(
            $"Revert the last applied migration ({migration}) on the connection \"{connection}\"?\n\nIts .down.sql runs now: whatever it drops (tables, columns, data) is lost.",
            $"Annuler la dernière migration appliquée ({migration}) sur la connexion « {connection} » ?\n\nSon .down.sql s'exécute maintenant : ce qu'il supprime (tables, colonnes, données) est perdu.");

        public static string ConfirmRevertUnknown(string connection) => T(
            $"Revert the last applied migration on the connection \"{connection}\"?\n\nIts .down.sql runs now: whatever it drops (tables, columns, data) is lost.",
            $"Annuler la dernière migration appliquée sur la connexion « {connection} » ?\n\nSon .down.sql s'exécute maintenant : ce qu'il supprime (tables, colonnes, données) est perdu.");

        public static string PrepareCaption => T("Update SQLx Cache", "Mettre à jour le cache SQLx");

        public static string PrepareMessage(string crate) => T(
            $"Building \"{crate}\" against the database to regenerate its offline query cache (.sqlx)...",
            $"Compilation de « {crate} » avec la base de données pour régénérer son cache de requêtes hors ligne (.sqlx)...");

        public static string Prepared(int queryFiles) => T($"SQLx cache updated: {queryFiles} query file(s).", $"Cache SQLx mis à jour : {queryFiles} fichier(s) de requête.");

        public static string PrepareCancelled => T("SQLx cache update cancelled.", "Mise à jour du cache SQLx annulée.");

        public static string Failed(string what, string message) => T($"{what} failed: {message}", $"{what} : échec. {message}");

        public static string SeeOutput => T("See the Kubuno output pane for details.", "Voir le volet de sortie « Kubuno » pour le détail.");

        public static string StaleInfoBar(string crate) => T($"The SQLx cache of {crate} is stale.", $"Le cache SQLx de {crate} est périmé.");

        public static string UpdateAction => T("Update", "Mettre à jour");

        public static string NotAMigrationsTarget => T(
            "Migrations need sqlx (PostgreSQL, SQLite, MySQL).",
            "Les migrations nécessitent sqlx (PostgreSQL, SQLite, MySQL).");
    }
}
