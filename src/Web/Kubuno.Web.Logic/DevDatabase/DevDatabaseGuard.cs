using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Kubuno.Web.Logic.DevDatabase
{
    /// <summary>
    /// The development database rule of a core started from Visual Studio (docs/WEB.md, "The development database").
    /// A core runs its SQL migrations when it starts, so pointing a debug session at the live database would migrate
    /// (and fill with test data) the instance people use. The connection string therefore never comes from a file of
    /// the repository: only from <see cref="UrlVariable"/> (the developer's environment, or the per-user debug
    /// environment of the project), and the database it names must look like a development one - a name with a
    /// <c>dev</c>/<c>test</c>/<c>local</c>... token (<c>kubuno_dev</c>, <c>kubuno-test</c>, <c>dev_kubuno2</c>) - unless
    /// <see cref="AllowAnyVariable"/> is set to <c>1</c>/<c>true</c> on purpose.
    /// This check lives in the Visual Studio launch and in Kubuno.Web.Sdk, never in the core itself.
    /// </summary>
    public static class DevDatabaseGuard
    {
        /// <summary>The variable holding the development database URL.</summary>
        public const string UrlVariable = "KUBUNO_DEV_DATABASE_URL";

        /// <summary>Set to 1/true to accept a database whose name does not look like a development one.</summary>
        public const string AllowAnyVariable = "KUBUNO_DEV_ALLOW_ANY_DATABASE";

        /// <summary>The example given in messages and documentation: a separate database on the team's server.</summary>
        public const string ExampleUrl = "postgres://kubuno:<password>@192.168.1.220:5432/kubuno_dev";

        /// <summary>Name tokens that make a database a development one (compared case-insensitively).</summary>
        public static readonly IReadOnlyList<string> DevTokens = new[]
        {
            "dev", "devel", "develop", "development", "test", "tests", "testing", "local", "sandbox", "scratch", "ci", "tmp", "temp",
        };

        private static readonly Regex TokenSplit = new Regex(@"[_\-.\s]+", RegexOptions.CultureInvariant);
        private static readonly Regex TrailingDigits = new Regex(@"\d+$", RegexOptions.CultureInvariant);

        /// <summary>
        /// Checks <paramref name="url"/> (the value of <see cref="UrlVariable"/>, null when unset) and
        /// <paramref name="allowAny"/> (the value of <see cref="AllowAnyVariable"/>).
        /// </summary>
        public static DevDatabaseCheck Check(string? url, string? allowAny)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return DevDatabaseCheck.Refused(
                    DevDatabaseVerdict.Missing,
                    null,
                    UrlVariable + " is not set. A core started from Visual Studio only connects to a DEVELOPMENT database, "
                    + "never the live one: it runs its migrations at startup. Create a separate database on the server "
                    + "(for example kubuno_dev) and set " + UrlVariable + " to it, e.g. " + ExampleUrl + ", as a user "
                    + "environment variable (setx " + UrlVariable + " \"...\", then restart Visual Studio) or in the project's "
                    + "Debug property page (Environment). See docs/WEB.md, \"The development database\".");
            }

            var parsed = DatabaseUrl.TryParse(url!);
            if (parsed is null)
            {
                return DevDatabaseCheck.Refused(
                    DevDatabaseVerdict.Invalid,
                    null,
                    UrlVariable + " is not a database URL (expected postgres://user:password@host:port/database). Its value is not shown: it may contain a password.");
            }

            if (string.IsNullOrEmpty(parsed.Database))
            {
                return DevDatabaseCheck.Refused(
                    DevDatabaseVerdict.Invalid,
                    parsed,
                    UrlVariable + " (" + parsed.Redacted + ") names no database. Add the development database's name: " + ExampleUrl + ".");
            }

            if (LooksLikeDevDatabase(parsed.Database!))
            {
                return DevDatabaseCheck.Accepted(DevDatabaseVerdict.DevName, parsed);
            }

            if (IsTruthy(allowAny))
            {
                return DevDatabaseCheck.Accepted(DevDatabaseVerdict.Overridden, parsed);
            }

            return DevDatabaseCheck.Refused(
                DevDatabaseVerdict.NotDevName,
                parsed,
                "The database '" + parsed.Database + "' (" + parsed.Redacted + ") does not look like a development database, and a core "
                + "started from Visual Studio runs its migrations against it. Use a separate database whose name says so "
                + "(kubuno_dev, kubuno_test...). If this really is a throw-away database, set " + AllowAnyVariable + "=1.");
        }

        /// <summary>True when one of the name's tokens (split on <c>_ - .</c>, trailing digits ignored) is a <see cref="DevTokens"/> entry.</summary>
        public static bool LooksLikeDevDatabase(string databaseName)
        {
            if (string.IsNullOrWhiteSpace(databaseName))
            {
                return false;
            }

            return TokenSplit.Split(databaseName.Trim())
                .Select(token => TrailingDigits.Replace(token, string.Empty))
                .Any(token => DevTokens.Contains(token, StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>1, true, yes, on (any case).</summary>
        public static bool IsTruthy(string? value)
        {
            var trimmed = value?.Trim();
            return string.Equals(trimmed, "1", StringComparison.Ordinal)
                || string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "yes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "on", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Outcome of <see cref="DevDatabaseGuard.Check"/>.</summary>
    public enum DevDatabaseVerdict
    {
        /// <summary>The URL is not set.</summary>
        Missing,

        /// <summary>The URL cannot be parsed or names no database.</summary>
        Invalid,

        /// <summary>The database name does not look like a development one and no override is set.</summary>
        NotDevName,

        /// <summary>Accepted: the database name looks like a development one.</summary>
        DevName,

        /// <summary>Accepted because <see cref="DevDatabaseGuard.AllowAnyVariable"/> is set.</summary>
        Overridden,
    }

    /// <summary>The verdict, the parsed URL when there is one, and the message to show when refused.</summary>
    public sealed class DevDatabaseCheck
    {
        private DevDatabaseCheck(DevDatabaseVerdict verdict, DatabaseUrl? url, string? message)
        {
            Verdict = verdict;
            Url = url;
            Message = message;
        }

        public DevDatabaseVerdict Verdict { get; }

        public DatabaseUrl? Url { get; }

        /// <summary>Why the launch is refused (null when accepted). Never contains the password.</summary>
        public string? Message { get; }

        public bool IsAccepted => Verdict == DevDatabaseVerdict.DevName || Verdict == DevDatabaseVerdict.Overridden;

        internal static DevDatabaseCheck Accepted(DevDatabaseVerdict verdict, DatabaseUrl url) => new DevDatabaseCheck(verdict, url, null);

        internal static DevDatabaseCheck Refused(DevDatabaseVerdict verdict, DatabaseUrl? url, string message) => new DevDatabaseCheck(verdict, url, message);
    }

    /// <summary>
    /// The parts of a <c>scheme://user:password@host:port/database?options</c> URL the guard needs - parsed by hand
    /// rather than with <see cref="Uri"/>, which rejects passwords with characters a connection string accepts.
    /// </summary>
    public sealed class DatabaseUrl
    {
        private DatabaseUrl(string original, string scheme, string? user, bool hasPassword, string host, string? port, string? database, string? query)
        {
            Original = original;
            Scheme = scheme;
            User = user;
            HasPassword = hasPassword;
            Host = host;
            Port = port;
            Database = database;
            Query = query;
        }

        /// <summary>The URL as given (with its password): only ever passed to the launched process, never shown.</summary>
        public string Original { get; }

        public string Scheme { get; }

        public string? User { get; }

        public bool HasPassword { get; }

        public string Host { get; }

        public string? Port { get; }

        /// <summary>The database name, percent-decoded; null when the URL has no path.</summary>
        public string? Database { get; }

        public string? Query { get; }

        /// <summary>The URL with its password replaced by <c>***</c>: what messages and logs show.</summary>
        public string Redacted
        {
            get
            {
                var credentials = User is null ? string.Empty : User + (HasPassword ? ":***" : string.Empty) + "@";
                var port = Port is null ? string.Empty : ":" + Port;
                var database = Database is null ? string.Empty : "/" + Database;
                return Scheme + "://" + credentials + Host + port + database;
            }
        }

        public static DatabaseUrl? TryParse(string url)
        {
            if (url is null)
            {
                return null;
            }

            var text = url.Trim();
            var schemeEnd = text.IndexOf("://", StringComparison.Ordinal);
            if (schemeEnd <= 0)
            {
                return null;
            }

            var scheme = text.Substring(0, schemeEnd);
            if (!Regex.IsMatch(scheme, "^[A-Za-z][A-Za-z0-9+.-]*$"))
            {
                return null;
            }

            var rest = text.Substring(schemeEnd + 3);
            string? query = null;
            var queryStart = rest.IndexOf('?');
            if (queryStart >= 0)
            {
                query = rest.Substring(queryStart + 1);
                rest = rest.Substring(0, queryStart);
            }

            // The credentials end at the LAST '@' (a raw '@' in a password is common enough); the path starts at the
            // first '/' after them.
            var at = rest.LastIndexOf('@');
            string? user = null;
            var hasPassword = false;
            var hostAndPath = rest;
            if (at >= 0)
            {
                var credentials = rest.Substring(0, at);
                hostAndPath = rest.Substring(at + 1);
                var colon = credentials.IndexOf(':');
                user = Uri.UnescapeDataString(colon >= 0 ? credentials.Substring(0, colon) : credentials);
                hasPassword = colon >= 0;
            }

            var slash = hostAndPath.IndexOf('/');
            var authority = slash >= 0 ? hostAndPath.Substring(0, slash) : hostAndPath;
            var path = slash >= 0 ? hostAndPath.Substring(slash + 1) : null;
            if (authority.Length == 0)
            {
                return null;
            }

            string host = authority;
            string? port = null;
            var portColon = authority.LastIndexOf(':');
            if (portColon > 0 && !authority.EndsWith("]", StringComparison.Ordinal))
            {
                host = authority.Substring(0, portColon);
                port = authority.Substring(portColon + 1);
            }

            var database = string.IsNullOrEmpty(path) ? null : Uri.UnescapeDataString(path!.TrimEnd('/'));
            return new DatabaseUrl(text, scheme, user, hasPassword, host, port, string.IsNullOrEmpty(database) ? null : database, query);
        }
    }
}
