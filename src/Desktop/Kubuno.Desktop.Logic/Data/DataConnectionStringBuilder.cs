using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Kubuno.Desktop.Logic.Data
{
    /// <summary>What the Add Connection dialog collects (the password only for the instant a string is built).</summary>
    public sealed class DataConnectionSettings
    {
        public DataProviderKind Provider { get; set; } = DataProviderKind.Postgres;

        /// <summary>Host name or address (SQL Server: may include <c>\INSTANCE</c>).</summary>
        public string Server { get; set; } = string.Empty;

        /// <summary>Port; <see langword="null"/> or the provider default leaves it out.</summary>
        public int? Port { get; set; }

        public string Database { get; set; } = string.Empty;

        /// <summary>SQLite: the database file.</summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>SQLite: create the file if missing (<c>mode=rwc</c>) instead of requiring it (<c>mode=rw</c>).</summary>
        public bool CreateIfMissing { get; set; }

        /// <summary>SQL Server: Windows integrated authentication instead of a SQL login.</summary>
        public bool IntegratedSecurity { get; set; }

        public string User { get; set; } = string.Empty;

        /// <summary>PostgreSQL <c>sslmode</c> / MySQL <c>SslMode</c>; empty = kubuno-data's default (TLS required for a non-local host).</summary>
        public string SslMode { get; set; } = string.Empty;

        /// <summary>SQL Server <c>Encrypt</c>: empty = default (required for a non-local server), <c>true</c>, <c>false</c>.</summary>
        public string Encrypt { get; set; } = string.Empty;

        /// <summary>SQL Server <c>TrustServerCertificate=true</c>.</summary>
        public bool TrustServerCertificate { get; set; }
    }

    /// <summary>
    /// Builds connection strings in the forms kubuno-data parses (<c>conn_string.rs</c>'s <c>ConnectionStringBuilder</c>,
    /// then sqlx / tiberius): the ADO.NET-like <c>Key=Value;</c> form for PostgreSQL, MySQL/MariaDB and SQL Server, a
    /// <c>sqlite:</c> URL for SQLite. Values are quoted the way that parser reads them: <c>'…'</c> with doubled quotes when
    /// they contain <c>;</c>, a quote, or leading/trailing spaces. The password is never part of <see cref="Display"/>
    /// (<c>***</c>), and no exception of this class ever contains it.
    /// </summary>
    public static class DataConnectionStringBuilder
    {
        public const string PasswordMask = "***";

        public static int DefaultPort(DataProviderKind provider) => provider switch
        {
            DataProviderKind.Postgres => 5432,
            DataProviderKind.MySql => 3306,
            DataProviderKind.SqlServer => 1433,
            _ => 0,
        };

        /// <summary>The provider's SSL/encryption choices (value, English label, French label); the first is the default.</summary>
        public static IReadOnlyList<(string Value, string English, string French)> SslModes(DataProviderKind provider) => provider switch
        {
            DataProviderKind.Postgres => new[]
            {
                (string.Empty, "Default (require for a remote server)", "Par défaut (require pour un serveur distant)"),
                ("disable", "disable", "disable"),
                ("prefer", "prefer", "prefer"),
                ("require", "require", "require"),
                ("verify-full", "verify-full", "verify-full"),
            },
            DataProviderKind.MySql => new[]
            {
                (string.Empty, "Default (REQUIRED for a remote server)", "Par défaut (REQUIRED pour un serveur distant)"),
                ("DISABLED", "DISABLED", "DISABLED"),
                ("PREFERRED", "PREFERRED", "PREFERRED"),
                ("REQUIRED", "REQUIRED", "REQUIRED"),
                ("VERIFY_IDENTITY", "VERIFY_IDENTITY", "VERIFY_IDENTITY"),
            },
            DataProviderKind.SqlServer => new[]
            {
                (string.Empty, "Default (encrypted for a remote server)", "Par défaut (chiffré pour un serveur distant)"),
                ("true", "Encrypt=true", "Encrypt=true"),
                ("false", "Encrypt=false (login only)", "Encrypt=false (connexion seule)"),
            },
            _ => Array.Empty<(string, string, string)>(),
        };

        /// <summary>The connection string, with <paramref name="password"/> in clear: only for the tool (test / store), never shown or logged.</summary>
        public static string Build(DataConnectionSettings settings, string? password) => Render(settings, password, mask: false);

        /// <summary>The connection string with the password masked (<c>***</c>), for display.</summary>
        public static string Display(DataConnectionSettings settings, bool hasPassword) => Render(settings, hasPassword ? PasswordMask : null, mask: true);

        /// <summary>The fields a provider needs that are missing, as <see cref="DataText"/> messages (empty when valid).</summary>
        public static IReadOnlyList<string> Validate(DataConnectionSettings settings, bool hasPassword)
        {
            var errors = new List<string>();
            if (settings.Provider == DataProviderKind.Sqlite)
            {
                if (string.IsNullOrWhiteSpace(settings.FilePath))
                {
                    errors.Add(DataText.FileRequired);
                }

                return errors;
            }

            if (string.IsNullOrWhiteSpace(settings.Server))
            {
                errors.Add(DataText.ServerRequired);
            }

            if (settings.Port is { } port && (port < 1 || port > 65535))
            {
                errors.Add(DataText.InvalidPort);
            }

            if (settings.Provider != DataProviderKind.SqlServer || !settings.IntegratedSecurity)
            {
                if (string.IsNullOrWhiteSpace(settings.User))
                {
                    errors.Add(DataText.UserRequired);
                }
            }

            return errors;
        }

        /// <summary>
        /// A connection name the tool accepts: 1-64 characters among letters, digits, space, <c>_</c>, <c>.</c> and <c>-</c>.
        /// </summary>
        public static bool IsValidConnectionName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name) || name!.Length > 64 || name.Trim().Length != name.Length)
            {
                return false;
            }

            foreach (char c in name)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == ' ' || c == '_' || c == '.' || c == '-';
                if (!ok)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// SQLite "create the file if it does not exist": kubuno-data-tool never creates a missing database file (it
        /// refuses it, so a typo does not silently create an empty database), so the dialog creates an empty file - a
        /// valid, empty SQLite database - itself. Returns true when a file was created.
        /// </summary>
        public static bool EnsureSqliteFile(DataConnectionSettings settings)
        {
            if (settings.Provider != DataProviderKind.Sqlite || !settings.CreateIfMissing)
            {
                return false;
            }

            string path = System.IO.Path.GetFullPath(settings.FilePath.Trim());
            if (System.IO.File.Exists(path))
            {
                return false;
            }

            string? directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }

            using (new System.IO.FileStream(path, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write))
            {
            }

            return true;
        }

        /// <summary>Quotes an ADO value the way kubuno-data's parser reads it back.</summary>
        public static string QuoteValue(string value)
        {
            if (value.IndexOf(';') >= 0 || value.IndexOf('\'') >= 0 || value.IndexOf('"') >= 0 || value.Trim() != value)
            {
                return "'" + value.Replace("'", "''") + "'";
            }

            return value;
        }

        /// <summary>A <c>sqlite:</c> URL for <paramref name="path"/> (sqlx percent-decodes the path: <c>%</c>, <c>?</c> and <c>#</c> are escaped).</summary>
        public static string SqliteUrl(string path, bool createIfMissing)
        {
            var escaped = new StringBuilder("sqlite:");
            foreach (char c in path.Trim())
            {
                switch (c)
                {
                    case '%':
                        escaped.Append("%25");
                        break;
                    case '?':
                        escaped.Append("%3F");
                        break;
                    case '#':
                        escaped.Append("%23");
                        break;
                    default:
                        escaped.Append(c);
                        break;
                }
            }

            escaped.Append(createIfMissing ? "?mode=rwc" : "?mode=rw");
            return escaped.ToString();
        }

        private static string Render(DataConnectionSettings s, string? password, bool mask)
        {
            if (s.Provider == DataProviderKind.Sqlite)
            {
                return SqliteUrl(s.FilePath, s.CreateIfMissing);
            }

            var parts = new List<KeyValuePair<string, string>>();
            void Add(string key, string? value)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    parts.Add(new KeyValuePair<string, string>(key, value!));
                }
            }

            string server = s.Server.Trim();
            string? port = s.Port is { } p && p != DefaultPort(s.Provider) ? p.ToString(CultureInfo.InvariantCulture) : null;
            switch (s.Provider)
            {
                case DataProviderKind.Postgres:
                    Add("Host", server);
                    Add("Port", port);
                    Add("Database", s.Database.Trim());
                    Add("Username", s.User.Trim());
                    Add("Password", password);
                    Add("SslMode", s.SslMode);
                    break;
                case DataProviderKind.MySql:
                    Add("Server", server);
                    Add("Port", port);
                    Add("Database", s.Database.Trim());
                    Add("User Id", s.User.Trim());
                    Add("Password", password);
                    Add("SslMode", s.SslMode);
                    break;
                case DataProviderKind.SqlServer:
                    // tiberius: `tcp:host,port`; a named instance (host\INSTANCE) is resolved through SQL Browser.
                    Add("Server", port != null && server.IndexOf('\\') < 0 ? $"tcp:{server},{port}" : server);
                    Add("Database", s.Database.Trim());
                    if (s.IntegratedSecurity)
                    {
                        Add("Integrated Security", "true");
                    }
                    else
                    {
                        Add("User Id", s.User.Trim());
                        Add("Password", password);
                    }

                    Add("Encrypt", s.Encrypt);
                    if (s.TrustServerCertificate)
                    {
                        Add("TrustServerCertificate", "true");
                    }

                    break;
            }

            var text = new StringBuilder();
            foreach (var part in parts)
            {
                text.Append(part.Key).Append('=');
                bool isPassword = part.Key == "Password";
                text.Append(isPassword && mask ? PasswordMask : QuoteValue(part.Value));
                text.Append(';');
            }

            return text.ToString();
        }
    }
}
