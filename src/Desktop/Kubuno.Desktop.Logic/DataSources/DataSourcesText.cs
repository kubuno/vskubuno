using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Kubuno.VisualStudio.Core.Data;

namespace Kubuno.VisualStudio.Core.DataSources
{
    /// <summary>
    /// User-visible strings of the Data Sources window, the "Add Data Source" wizard and the drops, in English and French (Visual
    /// Studio's own French names: "Sources de données", "Ajouter une nouvelle source de données", "Détails"...). Picked from the UI
    /// culture like <see cref="DataText"/>.
    /// </summary>
    public static class DataSourcesText
    {
        /// <summary>Test seam: forces a language instead of reading the current UI culture.</summary>
        public static bool? ForceFrench { get; set; }

        public static bool IsFrench => ForceFrench ?? string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "fr", StringComparison.OrdinalIgnoreCase);

        public static string T(string english, string french) => IsFrench ? french : english;

        // ---- Tool window and commands ----
        public static string DataSources => T("Data Sources", "Sources de données");

        public static string AddDataSourceCommand => T("Add New Data Source...", "Ajouter une source de données...");

        public static string ConfigureCommand => T("Configure Data Source with Wizard...", "Configurer la source de données avec l'Assistant...");

        public static string RefreshCommand => T("Refresh", "Actualiser");

        public static string EditFileCommand => T("Edit the .kbdata File", "Modifier le fichier .kbdata");

        public static string InsertCommand => T("Add to the Open View", "Ajouter à la vue ouverte");

        public static string GridMode => T("DataTable (grid)", "DataTable (grille)");

        public static string DetailsMode => T("Details", "Détails");

        public static string ControlName(DataControlKind kind) => kind switch
        {
            DataControlKind.None => T("[None]", "[Aucun]"),
            _ => kind.ToString(),
        };

        public static string NoProject => T(
            "Select a Rust project in Solution Explorer (or open one of its files) to see its data sources.",
            "Sélectionnez un projet Rust dans l'Explorateur de solutions (ou ouvrez l'un de ses fichiers) pour afficher ses sources de données.");

        public static string NoDataSources => T(
            "Your project has no data source yet. Add one to drag its tables onto a view: click \"Add New Data Source...\".",
            "Votre projet n'a pas encore de source de données. Ajoutez-en une pour glisser ses tables sur une vue : cliquez sur « Ajouter une source de données... ».");

        public static string Loading => T("Loading...", "Chargement...");

        public static string ProjectHeader(string package) => T("Project: ", "Projet : ") + package;

        public static string SourceError(string file, string message) => file + " - " + message;

        public static string TableToolTip(string table, bool isView, int columns, string mode) =>
            (isView ? T("View ", "Vue ") : T("Table ", "Table ")) + table + " · " + columns.ToString(CultureInfo.CurrentCulture) + T(" columns", " colonnes") + "\n" +
            T("Drag it onto a view: ", "Faites-la glisser sur une vue : ") + mode + T(" (change it with the arrow).", " (changez-le avec la flèche).");

        public static string ColumnToolTip(string column, string dbType, string rustType, bool nullable, string control) =>
            column + " : " + dbType + " (" + rustType + ")" + (nullable ? T(", nullable", ", nullable") : string.Empty) + "\n" + T("Dropped as ", "Déposée comme ") + control;

        public static string DropDownToolTip => T("Choose how it is dropped onto a view", "Choisir comment la déposer sur une vue");

        public static string NoDesigner => T(
            "Open a view (.kbview) in the designer first, then drag a table or a column onto it.",
            "Ouvrez d'abord une vue (.kbview) dans le concepteur, puis faites-y glisser une table ou une colonne.");

        // ---- Wizard ----
        public static string WizardTitle => T("Data Source Configuration Wizard", "Assistant Configuration de source de données");

        public static string Previous => T("< Previous", "< Précédent");

        public static string Next => T("Next >", "Suivant >");

        public static string Finish => T("Finish", "Terminer");

        public static string Cancel => T("Cancel", "Annuler");

        public static string StepTitle(DataSourceWizardStep step) => step switch
        {
            DataSourceWizardStep.Connection => T("Choose Your Data Connection", "Choisir votre connexion de données"),
            DataSourceWizardStep.Application => T("Save the Connection String in the Application", "Enregistrer la chaîne de connexion dans l'application"),
            DataSourceWizardStep.Objects => T("Choose Your Database Objects", "Choisir vos objets de base de données"),
            _ => T("Name the Data Source", "Nommer la source de données"),
        };

        public static string StepDescription(DataSourceWizardStep step) => step switch
        {
            DataSourceWizardStep.Connection => T(
                "Which connection should the application use to reach the database? The list is the Data Explorer's.",
                "Quelle connexion l'application doit-elle utiliser pour se connecter à la base de données ? La liste est celle de l'Explorateur de données."),
            DataSourceWizardStep.Application => T(
                "The application finds its connection string by name (ConnectionStrings:<name>) in its own secret store.",
                "L'application retrouve sa chaîne de connexion par son nom (ConnectionStrings:<nom>) dans son propre magasin de secrets."),
            DataSourceWizardStep.Objects => T(
                "Which tables and views do you want in your data source? Each one gets a typed row struct.",
                "Quels objets de base de données voulez-vous dans votre source de données ? Chacun obtient une structure de ligne typée."),
            _ => T("The data source is a Rust module of the project (src/data/<name>.rs).", "La source de données est un module Rust du projet (src/data/<nom>.rs)."),
        };

        public static string NewConnection => T("New Connection...", "Nouvelle connexion...");

        public static string ConnectionLabel => T("Connection:", "Connexion :");

        public static string ConnectionDetails => T("Connection details:", "Détails de la connexion :");

        public static string ConnectionStringName => T("Connection string name:", "Nom de la chaîne de connexion :");

        public static string SecretStore => T("Store the connection string in:", "Stocker la chaîne de connexion dans :");

        public static string StoreUserSecrets => T("User secrets of the project (%APPDATA%\\Kubuno\\UserSecrets)", "Secrets utilisateur du projet (%APPDATA%\\Kubuno\\UserSecrets)");

        public static string StoreCredentialManager => T("Windows Credential Manager", "Gestionnaire d'informations d'identification Windows");

        public static string NeverInProject => T(
            "The connection string is never written into the project: only its name is (in the .kbdata and the views).",
            "La chaîne de connexion n'est jamais écrite dans le projet : seul son nom l'est (dans le .kbdata et les vues).");

        public static string KeyPreview(string name) => "ConnectionStrings:" + name;

        public static string ModuleSchemaNote(string schema) => T(
            "Kubuno module: only the tables of its own schema \"" + schema + "\" can be used.",
            "Module Kubuno : seules les tables de son propre schéma « " + schema + " » peuvent être utilisées.");

        public static string Tables => T("Tables", "Tables");

        public static string Views => T("Views", "Vues");

        public static string SelectAll => T("Select all", "Tout sélectionner");

        public static string DataSourceName => T("Data source name:", "Nom de la source de données :");

        public static string SummaryHeader => T("Finish will:", "Terminer va :");

        public static string LoadingSchema => T("Reading the database schema...", "Lecture du schéma de la base de données...");

        public static string Working => T("Writing the data source...", "Écriture de la source de données...");

        // ---- Validation ----
        public static string NameRequired => T("Enter a name for the data source.", "Entrez un nom pour la source de données.");

        public static string NameNotIdentifier(string name) => T(
            "\"" + name + "\" is not a Rust identifier: use lower-case letters, digits and _, starting with a letter.",
            "« " + name + " » n'est pas un identificateur Rust : utilisez des minuscules, des chiffres et _, en commençant par une lettre.");

        public static string NameNotSnakeCase(string name, string suggestion) => T(
            "A Rust module name is in snake_case: \"" + suggestion + "\" instead of \"" + name + "\".",
            "Un nom de module Rust s'écrit en snake_case : « " + suggestion + " » au lieu de « " + name + " ».");

        public static string NameReserved(string name) => T(
            "\"" + name + "\" is reserved in the data module; choose another name.",
            "« " + name + " » est réservé dans le module data ; choisissez un autre nom.");

        public static string SourceExists(string name) => T(
            "The project already has a data source \"" + name + "\" (use Configure... to change it).",
            "Le projet a déjà une source de données « " + name + " » (utilisez Configurer... pour la modifier).");

        public static string ConnectionNameRequired => T("Enter the name of the connection string.", "Entrez le nom de la chaîne de connexion.");

        public static string ConnectionNameInvalid => T(
            "The connection string name is 1 to 64 characters: letters, digits, _, - and ., starting with a letter.",
            "Le nom de la chaîne de connexion comporte 1 à 64 caractères : lettres, chiffres, _, - et ., et commence par une lettre.");

        public static string ChooseConnection => T("Choose a connection (or create one).", "Choisissez une connexion (ou créez-en une).");

        public static string SchemaNotLoaded => T("The database schema is not loaded yet.", "Le schéma de la base de données n'est pas encore chargé.");

        public static string ChooseObjects => T("Select at least one table or view.", "Sélectionnez au moins une table ou une vue.");

        public static string NoTables => T("The database has no table or view.", "La base de données n'a aucune table ni vue.");

        public static string ModuleSchemaMissing(string schema) => T(
            "The database has no schema \"" + schema + "\" (the Kubuno module's own). Create it with a migration first.",
            "La base de données n'a pas de schéma « " + schema + " » (celui du module Kubuno). Créez-le d'abord avec une migration.");

        // ---- Summary ----
        public static string SummaryWriteKbdata(string name) => T("write src/data/" + name + ".kbdata (the tables: regenerated by Configure...)", "écrire src/data/" + name + ".kbdata (les tables : régénéré par Configurer...)");

        public static string SummaryRewriteKbdata(string name) => T("rewrite src/data/" + name + ".kbdata (its header comments are kept; " + name + ".rs is not touched)", "réécrire src/data/" + name + ".kbdata (ses commentaires d'en-tête sont conservés ; " + name + ".rs n'est pas modifié)");

        public static string SummaryWriteUserFile(string name) => T("create src/data/" + name + ".rs (your code: never rewritten)", "créer src/data/" + name + ".rs (votre code : jamais réécrit)");

        public static string SummaryCreateMod(string name) => T("create src/data/mod.rs (pub mod " + name + ";)", "créer src/data/mod.rs (pub mod " + name + ";)");

        public static string SummaryEditMod(string name) => T("add pub mod " + name + "; to src/data/mod.rs", "ajouter pub mod " + name + "; à src/data/mod.rs");

        public static string SummaryEditMain => T("add mod data; and the registration of the user secrets to src/main.rs", "ajouter mod data; et l'enregistrement des secrets utilisateur à src/main.rs");

        public static string SummaryCargoFeature => T("enable the \"data\" feature of kubuno in Cargo.toml", "activer la fonctionnalité « data » de kubuno dans Cargo.toml");

        public static string SummaryCargoSecretsId => T("add [package.metadata.kubuno] user-secrets-id to Cargo.toml", "ajouter [package.metadata.kubuno] user-secrets-id à Cargo.toml");

        public static string SummarySecret(string name, CredentialStoreKind store) => T(
            "copy the connection string to ConnectionStrings:" + name + " in " + (store == CredentialStoreKind.UserSecrets ? "the project's user secrets" : "the Windows Credential Manager"),
            "copier la chaîne de connexion dans ConnectionStrings:" + name + " (" + (store == CredentialStoreKind.UserSecrets ? "secrets utilisateur du projet" : "Gestionnaire d'informations d'identification") + ")");

        public static string SummaryTables(IEnumerable<string> tables) => T("tables and views: ", "tables et vues : ") + string.Join(", ", tables);

        public static string SummarySqlx => T(
            "then refresh the offline query cache (.sqlx) in the background (cargo builds the crate: the Kubuno Output pane shows it)",
            "puis actualiser le cache de requêtes hors ligne (.sqlx) en arrière-plan (cargo compile la crate : le volet Sortie Kubuno l'affiche)");

        public static string ConfirmRewrite(string name) => T(
            "Rewrite src/data/" + name + ".kbdata from the database? Its header comments are kept, and " + name + ".rs (your code) is not touched.",
            "Réécrire src/data/" + name + ".kbdata à partir de la base de données ? Ses commentaires d'en-tête sont conservés et " + name + ".rs (votre code) n'est pas modifié.");

        public static string DirtyFile(string file) => T(
            file + " has unsaved changes in the editor. Save or close it, then click Finish again.",
            file + " a des modifications non enregistrées dans l'éditeur. Enregistrez-le ou fermez-le, puis cliquez à nouveau sur Terminer.");

        public static string NoKubunoDependency => T(
            "Cargo.toml has no kubuno dependency: add kubuno (with its \"data\" feature) or kubuno-data yourself.",
            "Cargo.toml n'a pas de dépendance kubuno : ajoutez kubuno (avec sa fonctionnalité « data ») ou kubuno-data vous-même.");

        public static string SqlxStarted(string package) => T("Refreshing the offline query cache (.sqlx) of " + package + "...", "Actualisation du cache de requêtes hors ligne (.sqlx) de " + package + "...");

        public static string SqlxDone(int files) => T("Offline query cache refreshed: " + files + " query file(s).", "Cache de requêtes hors ligne actualisé : " + files + " fichier(s) de requête.");

        public static string SqlxFailed(string message) => T("Refreshing the offline query cache failed: ", "L'actualisation du cache de requêtes hors ligne a échoué : ") + message;

        public static string SqlServerNoSqlx => T(
            "SQL Server has no sqlx driver: no offline query cache is needed (the typed reads are checked at run time).",
            "SQL Server n'a pas de pilote sqlx : aucun cache de requêtes hors ligne n'est nécessaire (les lectures typées sont vérifiées à l'exécution).");

        // ---- Drops ----
        public static string ViewNotReadable => T("The view is not well-formed XML right now: fix it, then drop again.", "La vue n'est pas du XML bien formé pour l'instant : corrigez-la, puis déposez à nouveau.");

        public static string UnknownColumn(string column, string table) => T("The table " + table + " has no column " + column + ".", "La table " + table + " n'a pas de colonne " + column + ".");

        public static string OtherProject(string sourceProject, string viewProject) => T(
            "This data source belongs to " + sourceProject + ": drop it onto a view of that project, not of " + viewProject + ".",
            "Cette source de données appartient à " + sourceProject + " : déposez-la sur une vue de ce projet, pas de " + viewProject + ".");

        public static string NothingToInsert => T("Nothing to add: every column is set to [None].", "Rien à ajouter : chaque colonne est réglée sur [Aucun].");

        public static string DropTableUndo(string table) => T("Add data table " + table, "Ajouter la table de données " + table);

        public static string DropColumnUndo(string column) => T("Add data field " + column, "Ajouter le champ de données " + column);
    }
}
