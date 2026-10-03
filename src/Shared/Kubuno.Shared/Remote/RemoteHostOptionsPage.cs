using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Kubuno.Shared.Logic.Remote;
using Kubuno.Shared.Settings;
using Microsoft.VisualStudio.Shell;

namespace Kubuno.Shared.Remote
{
    /// <summary>
    /// Tools &gt; Options &gt; Kubuno &gt; Remote Linux host (docs/WEB.md, "The development database"): the machine that
    /// holds the development database and, later, the Linux builds. Stored in Visual Studio's unified settings
    /// (<c>kubuno.remote.*</c>), never in a repository. Read through <see cref="Current"/>.
    /// </summary>
    [Guid("6a0c4f3e-2b8d-4d71-9e55-3f1c7a9d2b64")]
    public sealed class RemoteHostOptionsPage : KubunoDialogPage
    {
        // The type was Kubuno.Core.Remote.RemoteHostOptionsPage before the Core layer became Kubuno.Shared: the classic settings
        // key DialogPage derives from the type's full name keeps that name, so values saved by an earlier version still load.
        public RemoteHostOptionsPage()
            : base(legacyTypeFullName: "Kubuno.Core.Remote.RemoteHostOptionsPage")
        {
        }

        [Category("Connection")]
        [DisplayName("Host")]
        [Description("Name or IP address of the remote Linux host.")]
        [DefaultValue(RemoteHostSettings.DefaultHost)]
        [UnifiedSetting("kubuno.remote.connection.host")]
        public string Host { get; set; } = RemoteHostSettings.DefaultHost;

        [Category("Connection")]
        [DisplayName("User")]
        [Description("The account Kubuno connects as (its ~/.ssh/authorized_keys holds the public key).")]
        [DefaultValue(RemoteHostSettings.DefaultUser)]
        [UnifiedSetting("kubuno.remote.connection.user")]
        public string User { get; set; } = RemoteHostSettings.DefaultUser;

        [Category("Connection")]
        [DisplayName("SSH port")]
        [Description("The port of the host's SSH server.")]
        [DefaultValue(RemoteHostSettings.DefaultPort)]
        [UnifiedSetting("kubuno.remote.connection.port")]
        public int Port { get; set; } = RemoteHostSettings.DefaultPort;

        [Category("Connection")]
        [DisplayName("Private key")]
        [Description("The private key Kubuno authenticates with (no passphrase: Kubuno never asks for one).")]
        [DefaultValue(RemoteHostSettings.DefaultPrivateKeyPath)]
        [UnifiedSetting("kubuno.remote.connection.privateKeyPath")]
        public string PrivateKeyPath { get; set; } = RemoteHostSettings.DefaultPrivateKeyPath;

        [Category("Connection")]
        [DisplayName("Known hosts file")]
        [Description("The host keys accepted in Visual Studio, kept apart from the user's own known_hosts.")]
        [DefaultValue(RemoteHostSettings.DefaultKnownHostsPath)]
        [UnifiedSetting("kubuno.remote.connection.knownHostsPath")]
        public string KnownHostsPath { get; set; } = RemoteHostSettings.DefaultKnownHostsPath;

        [Category("Development database")]
        [DisplayName("SSH tunnel")]
        [Description("When the Kubuno Core launch profiles open the tunnel to the host's PostgreSQL.")]
        [DefaultValue(DatabaseTunnelMode.Auto)]
        [UnifiedSetting("kubuno.remote.devDatabase.tunnel")]
        public DatabaseTunnelMode DevDatabaseTunnel { get; set; } = DatabaseTunnelMode.Auto;

        [Category("Development database")]
        [DisplayName("Local port")]
        [Description("The tunnel's local end (KUBUNO_DEV_DATABASE_URL then uses localhost and this port).")]
        [DefaultValue(SshCommandLine.DefaultTunnelLocalPort)]
        [UnifiedSetting("kubuno.remote.devDatabase.localPort")]
        public int TunnelLocalPort { get; set; } = SshCommandLine.DefaultTunnelLocalPort;

        [Category("Development database")]
        [DisplayName("PostgreSQL port on the host")]
        [Description("The port PostgreSQL listens on, on the host's loopback.")]
        [DefaultValue(SshCommandLine.DefaultRemoteDatabasePort)]
        [UnifiedSetting("kubuno.remote.devDatabase.remotePort")]
        public int RemoteDatabasePort { get; set; } = SshCommandLine.DefaultRemoteDatabasePort;

        /// <summary>The page of the loaded package (UI thread), or a page with the defaults.</summary>
        public static RemoteHostOptionsPage Current
        {
            get
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                try
                {
                    if (KubunoHost.GetDialogPage<RemoteHostOptionsPage>() is RemoteHostOptionsPage page)
                    {
                        return page;
                    }
                }
                catch (Exception)
                {
                    // Fall back to the defaults.
                }

                return new RemoteHostOptionsPage();
            }
        }

        /// <summary>The connection settings, defaults applied and paths expanded.</summary>
        public RemoteHostSettings ToSettings() => RemoteHostSettings.Create(Host, User, Port, PrivateKeyPath, KnownHostsPath);

        /// <summary>The tunnel's local port, or the default when the stored value is out of range.</summary>
        public int EffectiveTunnelLocalPort => TunnelLocalPort is >= 1 and <= 65535 ? TunnelLocalPort : SshCommandLine.DefaultTunnelLocalPort;

        /// <summary>The PostgreSQL port, or the default when the stored value is out of range.</summary>
        public int EffectiveRemoteDatabasePort => RemoteDatabasePort is >= 1 and <= 65535 ? RemoteDatabasePort : SshCommandLine.DefaultRemoteDatabasePort;
    }
}
