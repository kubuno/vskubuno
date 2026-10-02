using System;
using System.Text.RegularExpressions;

namespace Kubuno.Shared.Logic.Remote
{
    /// <summary>
    /// The remote Linux host of Tools &gt; Options &gt; Kubuno &gt; Remote Linux host (docs/WEB.md, "The development
    /// database"): the machine that holds the development database (reached through an SSH tunnel) and, later, the Linux
    /// builds. Values come from Visual Studio's unified settings, never from a repository; empty values fall back to the
    /// defaults and <c>%VARIABLE%</c> references in the paths are expanded.
    /// </summary>
    public sealed class RemoteHostSettings
    {
        public const string DefaultHost = "192.168.1.220";
        public const string DefaultUser = "martinien";
        public const int DefaultPort = 22;
        public const string DefaultPrivateKeyPath = @"%USERPROFILE%\.ssh\id_ed25519_kubuno";
        public const string DefaultKnownHostsPath = @"%USERPROFILE%\.ssh\known_hosts_kubuno";

        // Host names, IPv4 and IPv6 literals; never a leading '-' (it would be read as an ssh option).
        private static readonly Regex HostPattern = new Regex(@"^(?!-)[A-Za-z0-9._:\-\[\]%]{1,253}$", RegexOptions.CultureInvariant);
        private static readonly Regex UserPattern = new Regex(@"^(?!-)[A-Za-z0-9._\-]{1,64}$", RegexOptions.CultureInvariant);

        public RemoteHostSettings(string host, string user, int port, string privateKeyPath, string knownHostsPath)
        {
            Host = host;
            User = user;
            Port = port;
            PrivateKeyPath = privateKeyPath;
            KnownHostsPath = knownHostsPath;
        }

        public string Host { get; }

        public string User { get; }

        public int Port { get; }

        /// <summary>The private key, expanded (<c>C:\Users\me\.ssh\id_ed25519_kubuno</c>).</summary>
        public string PrivateKeyPath { get; }

        /// <summary>The Kubuno-specific known_hosts file, expanded: the host keys accepted for this host only.</summary>
        public string KnownHostsPath { get; }

        /// <summary><c>user@host</c>.</summary>
        public string Destination => User + "@" + Host;

        /// <summary>The settings with the defaults applied to empty values and the environment variables of the paths expanded.</summary>
        public static RemoteHostSettings Create(string? host, string? user, int port, string? privateKeyPath, string? knownHostsPath, Func<string, string>? expand = null)
        {
            expand ??= Environment.ExpandEnvironmentVariables;
            static string Or(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value!.Trim();
            return new RemoteHostSettings(
                Or(host, DefaultHost),
                Or(user, DefaultUser),
                port <= 0 ? DefaultPort : port,
                expand(Or(privateKeyPath, DefaultPrivateKeyPath)),
                expand(Or(knownHostsPath, DefaultKnownHostsPath)));
        }

        public static RemoteHostSettings Defaults(Func<string, string>? expand = null) => Create(null, null, DefaultPort, null, null, expand);

        /// <summary>What makes these settings unusable (null when they are fine). Checked before anything is given to ssh.</summary>
        public string? Validate()
        {
            if (!HostPattern.IsMatch(Host))
            {
                return "the host name '" + Host + "' is not valid";
            }

            if (!UserPattern.IsMatch(User))
            {
                return "the user name '" + User + "' is not valid";
            }

            if (Port < 1 || Port > 65535)
            {
                return "the port " + Port + " is not between 1 and 65535";
            }

            if (PrivateKeyPath.IndexOf('"') >= 0 || KnownHostsPath.IndexOf('"') >= 0)
            {
                return "a path contains a double quote";
            }

            return null;
        }
    }
}
