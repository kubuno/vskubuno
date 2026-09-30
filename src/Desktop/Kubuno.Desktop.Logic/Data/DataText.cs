using System;
using System.Globalization;

namespace Kubuno.Desktop.Logic.Data
{
    /// <summary>
    /// User-visible strings of the Data Explorer, its dialogs and the query window, in English and French (Visual Studio's
    /// own French names where one exists: "Explorateur de serveurs", "Actualiser", "Nouvelle requête"...). Picked from the
    /// UI culture like <c>DependenciesText</c>.
    /// </summary>
    public static class DataText
    {
        /// <summary>Test seam: forces a language instead of reading the current UI culture.</summary>
        public static bool? ForceFrench { get; set; }

        public static bool IsFrench => ForceFrench ?? string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "fr", StringComparison.OrdinalIgnoreCase);

        public static string T(string english, string french) => IsFrench ? french : english;

        // Tool window and commands.
        public static string DataExplorer => T("Data Explorer", "Explorateur de données");

        public static string AddConnectionCommand => T("Add Connection...", "Ajouter une connexion...");

        public static string RefreshCommand => T("Refresh", "Actualiser");

        public static string NewQueryCommand => T("New Query", "Nouvelle requête");

        public static string DeleteCommand => T("Delete", "Supprimer");

        public static string DeleteConnectionCommand => T("Delete Connection", "Supprimer la connexion");

        public static string ShowDataCommand => T("Show Table Data", "Afficher les données");

        public static string GenerateScriptMenu => T("Generate Script", "Générer un script");

        public static string CopyNameCommand => T("Copy Name", "Copier le nom");

        public static string ScriptCommand(ScriptKind kind) => kind switch
        {
            ScriptKind.Select => "SELECT",
            ScriptKind.Insert => "INSERT",
            ScriptKind.Update => "UPDATE",
            ScriptKind.Delete => "DELETE",
            _ => "CREATE",
        };

        public static string ExecuteCommand => T("Execute", "Exécuter");

        public static string CancelQueryCommand => T("Cancel Query", "Annuler la requête");

        // Tree.
        public static string Loading => T("Loading...", "Chargement...");

        public static string NoConnections => T("No connection. Use \"Add Connection...\" to connect to a database.", "Aucune connexion. Utilisez « Ajouter une connexion... » pour vous connecter à une base de données.");

        public static string Tables => T("Tables", "Tables");

        public static string Views => T("Views", "Vues");

        public static string Functions => T("Functions", "Fonctions");

        public static string Procedures => T("Stored Procedures", "Procédures stockées");

        public static string Columns => T("Columns", "Colonnes");

        public static string Keys => T("Keys", "Clés");

        public static string Indexes => T("Indexes", "Index");

        public static string Empty => T("(none)", "(aucun)");

        public static string NotNull => T("not null", "non NULL");

        public static string Null => T("null", "NULL");

        public static string AutoIncrement => T("identity", "auto-incrément");

        public static string Unique => T("unique", "unique");

        public static string ErrorNode(string message) => T("Error: ", "Erreur : ") + message;

        public static string ConnectionToolTip(string provider, string display, string store) =>
            $"{ProviderName(provider)} - {display}\n" + T("Credentials: ", "Informations d'identification : ") + StoreName(store);

        public static string ProviderName(string provider) => DataToolNames.ParseProvider(provider) is { } kind ? ProviderName(kind) : provider;

        public static string ProviderName(DataProviderKind provider) => provider switch
        {
            DataProviderKind.Postgres => "PostgreSQL",
            DataProviderKind.Sqlite => "SQLite",
            DataProviderKind.MySql => "MySQL / MariaDB",
            _ => "SQL Server",
        };

        public static string StoreName(string store) =>
            DataToolNames.ParseStore(store) == CredentialStoreKind.UserSecrets ? T("user secrets", "secrets utilisateur") : T("Windows Credential Manager", "Gestionnaire d'identification Windows");

        public static string ConfirmDelete(string name) =>
            T($"Delete the connection '{name}'? Its stored credentials are removed too. The database itself is not changed.",
              $"Supprimer la connexion « {name} » ? Ses informations d'identification enregistrées sont également supprimées. La base de données elle-même n'est pas modifiée.");

        public static string ToolUnavailable(string message) =>
            T("The data helper (kubuno-data-tool) is not available: ", "L'assistant de données (kubuno-data-tool) n'est pas disponible : ") + message;

        // Add Connection dialog.
        public static string AddConnectionTitle => T("Add Connection", "Ajouter une connexion");

        public static string ConnectionName => T("Connection _name:", "_Nom de la connexion :");

        public static string Provider => T("_Data source:", "_Fournisseur :");

        public static string DatabaseFile => T("Database _file:", "Fic_hier de base de données :");

        public static string Browse => T("B_rowse...", "_Parcourir...");

        public static string CreateIfMissing => T("Create the file if it does not _exist", "_Créer le fichier s'il n'existe pas");

        public static string Server => T("Se_rver name:", "Nom du se_rveur :");

        public static string Port => T("P_ort:", "P_ort :");

        public static string Database => T("Data_base:", "_Base de données :");

        public static string Authentication => T("_Authentication:", "_Authentification :");

        public static string WindowsAuthentication => T("Windows Authentication", "Authentification Windows");

        public static string SqlServerAuthentication => T("SQL Server Authentication", "Authentification SQL Server");

        public static string User => T("_User name:", "Nom d'_utilisateur :");

        public static string Password => T("_Password:", "_Mot de passe :");

        public static string Encryption => T("_Encryption (SSL/TLS):", "_Chiffrement (SSL/TLS) :");

        public static string TrustServerCertificate => T("Trust server _certificate", "Approuver le certificat du serv_eur");

        public static string Advanced => T("Advanced", "Avancé");

        public static string GeneratedConnectionString => T("Connection string (password masked):", "Chaîne de connexion (mot de passe masqué) :");

        public static string UseRawConnectionString => T("Enter the connection string _manually", "Sa_isir la chaîne de connexion manuellement");

        public static string RawConnectionStringHint => T("The whole string is stored in the chosen credential store, like the password.", "La chaîne entière est enregistrée dans le magasin d'identification choisi, comme le mot de passe.");

        public static string CredentialStorage => T("Credential storage", "Stockage des informations d'identification");

        public static string CredentialManagerOption => T("_Windows Credential Manager", "Gestionnaire d'identification _Windows");

        public static string UserSecretsOption => T("User _secrets (%APPDATA%\\Kubuno\\UserSecrets)", "_Secrets utilisateur (%APPDATA%\\Kubuno\\UserSecrets)");

        public static string CredentialExplanation => T(
            "The connection string is stored only there, never in the project or its files.",
            "La chaîne de connexion est enregistrée uniquement à cet endroit, jamais dans le projet ni dans ses fichiers.");

        public static string TestConnection => T("_Test Connection", "_Tester la connexion");

        public static string CancelTest => T("Cancel", "Annuler");

        public static string Testing => T("Testing the connection...", "Test de la connexion...");

        public static string TestSucceeded(string version, long ms) => T($"Test connection succeeded - server {version} ({ms} ms).", $"La connexion de test a réussi - serveur {version} ({ms} ms).");

        public static string TestFailed(string message) => T("Test connection failed: ", "Échec de la connexion de test : ") + message;

        public static string TestCancelled => T("Test cancelled.", "Test annulé.");

        public static string Saving => T("Saving the connection...", "Enregistrement de la connexion...");

        public static string Ok => T("OK", "OK");

        public static string Cancel => T("Cancel", "Annuler");

        public static string NameRequired => T("Enter a connection name.", "Entrez un nom de connexion.");

        public static string NameInvalid => T("The name may only contain letters, digits, spaces, '_', '.' and '-' (64 characters at most).", "Le nom ne peut contenir que des lettres, des chiffres, des espaces, « _ », « . » et « - » (64 caractères au plus).");

        public static string NameTaken(string name) => T($"A connection named '{name}' already exists.", $"Une connexion nommée « {name} » existe déjà.");

        public static string FileRequired => T("Choose the database file.", "Choisissez le fichier de base de données.");

        public static string FileMissing => T("The file does not exist. Check \"Create the file if it does not exist\" to create it.", "Le fichier n'existe pas. Cochez « Créer le fichier s'il n'existe pas » pour le créer.");

        public static string ServerRequired => T("Enter the server name.", "Entrez le nom du serveur.");

        public static string InvalidPort => T("The port must be a number between 1 and 65535.", "Le port doit être un nombre entre 1 et 65535.");

        public static string UserRequired => T("Enter the user name.", "Entrez le nom d'utilisateur.");

        public static string ConnectionStringRequired => T("Enter the connection string.", "Entrez la chaîne de connexion.");

        public static string SqliteFilter => T("SQLite databases (*.db;*.sqlite;*.sqlite3)|*.db;*.sqlite;*.sqlite3|All files (*.*)|*.*", "Bases de données SQLite (*.db;*.sqlite;*.sqlite3)|*.db;*.sqlite;*.sqlite3|Tous les fichiers (*.*)|*.*");

        // Query window.
        public static string QueryCaption(string connection) => T("Query - ", "Requête – ") + connection;

        public static string DataCaption(string table) => T("Data - ", "Données – ") + table;

        public static string Connection => T("Connection:", "Connexion :");

        public static string Messages => T("Messages", "Messages");

        public static string Results => T("Results", "Résultats");

        public static string ResultSet(int index) => T($"Results {index}", $"Résultats {index}");

        public static string Executing => T("Executing query...", "Exécution de la requête...");

        public static string Cancelled => T("Query cancelled.", "Requête annulée.");

        public static string QueryFailed => T("Query failed.", "Échec de la requête.");

        public static string NoResult => T("Commands completed successfully.", "Les commandes se sont terminées correctement.");

        public static string Ready => T("Ready", "Prêt");
    }
}
