using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Kubuno.Core.Logic.Remote
{
    /// <summary>
    /// The command lines Kubuno gives Windows' OpenSSH (<c>C:\Windows\System32\OpenSSH\ssh.exe</c>) to reach the remote
    /// Linux host (docs/WEB.md, "The development database"). Every connection is non-interactive and checks the host key:
    /// <list type="bullet">
    /// <item><c>BatchMode=yes</c>: never a password or passphrase prompt - a key that is not authorised fails at once;</item>
    /// <item><c>StrictHostKeyChecking=yes</c> against the Kubuno-specific known_hosts file only: an unknown host fails and
    /// Visual Studio asks the user to accept its fingerprint (<see cref="KnownHosts"/>), a changed key always fails;</item>
    /// <item><c>-F none</c> and <c>IdentitiesOnly=yes</c>: the user's own ssh configuration and agent keys do not change
    /// what is tried.</item>
    /// </list>
    /// </summary>
    public static class SshCommandLine
    {
        /// <summary>The PostgreSQL port the tunnel forwards to, on the remote host's loopback.</summary>
        public const int DefaultRemoteDatabasePort = 5432;

        /// <summary>The local end of the development database tunnel (<c>KUBUNO_DEV_DATABASE_URL=postgres://...@localhost:55432/kubuno_dev</c>).</summary>
        public const int DefaultTunnelLocalPort = 55432;

        /// <summary>Windows' own OpenSSH client.</summary>
        public static string DefaultSshExecutable => Path.Combine(SystemDirectory(), "OpenSSH", "ssh.exe");

        /// <summary>Windows' own ssh-keyscan (reads a host's public keys, to show their fingerprint before they are accepted).</summary>
        public static string DefaultKeyscanExecutable => Path.Combine(SystemDirectory(), "OpenSSH", "ssh-keyscan.exe");

        /// <summary>
        /// <c>ssh -N -L &lt;local&gt;:localhost:&lt;remote&gt; ...</c>: the tunnel to a port of the remote host's loopback
        /// (PostgreSQL listens on localhost only). <c>ExitOnForwardFailure</c> makes a busy local port an error instead of
        /// a connection without its tunnel; the keep-alives end the process when the host goes away.
        /// </summary>
        public static IReadOnlyList<string> TunnelArguments(RemoteHostSettings settings, int localPort, int remotePort)
        {
            if (localPort < 1 || localPort > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(localPort));
            }

            if (remotePort < 1 || remotePort > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(remotePort));
            }

            var arguments = new List<string>
            {
                "-N",
                "-L",
                localPort.ToString(CultureInfo.InvariantCulture) + ":localhost:" + remotePort.ToString(CultureInfo.InvariantCulture),
            };
            arguments.AddRange(ConnectionArguments(settings));
            arguments.Add("-o");
            arguments.Add("ExitOnForwardFailure=yes");
            arguments.Add("-o");
            arguments.Add("ServerAliveInterval=15");
            arguments.Add("-o");
            arguments.Add("ServerAliveCountMax=3");
            arguments.Add(settings.Destination);
            return arguments;
        }

        /// <summary>The options of every Kubuno connection (identity, port, batch mode, host key checking), without the destination.</summary>
        public static IReadOnlyList<string> ConnectionArguments(RemoteHostSettings settings)
        {
            var problem = settings.Validate();
            if (problem is not null)
            {
                throw new ArgumentException("Remote Linux host settings: " + problem + ".", nameof(settings));
            }

            return new[]
            {
                "-F", "none",
                "-i", settings.PrivateKeyPath,
                "-p", settings.Port.ToString(CultureInfo.InvariantCulture),
                "-o", "BatchMode=yes",
                "-o", "IdentitiesOnly=yes",
                "-o", "StrictHostKeyChecking=yes",
                "-o", "UserKnownHostsFile=" + ConfigPath(settings.KnownHostsPath),
                "-o", "ConnectTimeout=10",
            };
        }

        /// <summary><c>ssh-keyscan -p &lt;port&gt; -t ed25519,ecdsa,rsa &lt;host&gt;</c>.</summary>
        public static IReadOnlyList<string> KeyscanArguments(RemoteHostSettings settings) => new[]
        {
            "-p", settings.Port.ToString(CultureInfo.InvariantCulture),
            "-T", "10",
            "-t", "ed25519,ecdsa,rsa",
            settings.Host,
        };

        /// <summary>
        /// A path as an ssh option value: forward slashes (Windows' OpenSSH accepts them, and backslashes are escapes in
        /// its option parser) and double quotes around a path with spaces.
        /// </summary>
        public static string ConfigPath(string path)
        {
            var slashed = path.Replace('\\', '/');
            return slashed.IndexOf(' ') >= 0 ? "\"" + slashed + "\"" : slashed;
        }

        /// <summary>The arguments as one Windows command line (the rules of CommandLineToArgvW / the C runtime).</summary>
        public static string ToCommandLine(IEnumerable<string> arguments) => string.Join(" ", arguments.Select(Quote));

        /// <summary>One argument quoted for a Windows command line when it needs to be.</summary>
        public static string Quote(string argument)
        {
            if (argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
            {
                return argument;
            }

            var builder = new StringBuilder("\"");
            var backslashes = 0;
            foreach (var character in argument)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (character == '"')
                {
                    builder.Append('\\', (backslashes * 2) + 1);
                }
                else
                {
                    builder.Append('\\', backslashes);
                }

                backslashes = 0;
                builder.Append(character);
            }

            builder.Append('\\', backslashes * 2);
            return builder.Append('"').ToString();
        }

        private static string SystemDirectory()
        {
            // A 32-bit process sees SysWOW64 as System32, where there is no OpenSSH: go through Sysnative then.
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var sysnative = Path.Combine(windows, "Sysnative");
            return !Environment.Is64BitProcess && Environment.Is64BitOperatingSystem && Directory.Exists(sysnative)
                ? sysnative
                : Environment.GetFolderPath(Environment.SpecialFolder.System);
        }
    }
}
