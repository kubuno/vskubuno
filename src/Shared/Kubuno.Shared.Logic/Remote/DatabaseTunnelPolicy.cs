using System;
using System.Globalization;

namespace Kubuno.Shared.Logic.Remote
{
    /// <summary>When the Kubuno Core launch profiles open the development database tunnel (setting <c>kubuno.remote.devDatabase.tunnel</c>).</summary>
    public enum DatabaseTunnelMode
    {
        /// <summary>When <c>KUBUNO_DEV_DATABASE_URL</c> points at the tunnel's local end (<c>localhost:55432</c>).</summary>
        Auto,

        /// <summary>At every launch, whatever the URL says.</summary>
        Always,

        /// <summary>Never (the database is reached directly, or the tunnel is opened by hand).</summary>
        Never,
    }

    /// <summary>The tunnel decision of F5 / Ctrl+F5 (docs/WEB.md, "The development database").</summary>
    public static class DatabaseTunnelPolicy
    {
        /// <summary>
        /// Whether the launch needs the tunnel: <see cref="DatabaseTunnelMode.Always"/>, or <see cref="DatabaseTunnelMode.Auto"/>
        /// with a database URL on this machine's loopback (<c>localhost</c>, <c>127.0.0.1</c>, <c>::1</c>) at the tunnel's local port.
        /// </summary>
        public static bool IsNeeded(DatabaseTunnelMode mode, string? databaseHost, string? databasePort, int localPort)
        {
            switch (mode)
            {
                case DatabaseTunnelMode.Always:
                    return true;
                case DatabaseTunnelMode.Never:
                    return false;
                default:
                    return IsLoopback(databaseHost)
                        && int.TryParse(databasePort, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
                        && port == localPort;
            }
        }

        public static bool IsLoopback(string? host)
        {
            var name = (host ?? string.Empty).Trim().TrimStart('[').TrimEnd(']');
            return string.Equals(name, "localhost", StringComparison.OrdinalIgnoreCase) || name == "127.0.0.1" || name == "::1";
        }

        /// <summary>The mode of a stored setting value (<c>auto</c>, <c>always</c>, <c>never</c>; anything else is <see cref="DatabaseTunnelMode.Auto"/>).</summary>
        public static DatabaseTunnelMode ParseMode(string? value) =>
            string.Equals(value, "always", StringComparison.OrdinalIgnoreCase) ? DatabaseTunnelMode.Always
            : string.Equals(value, "never", StringComparison.OrdinalIgnoreCase) ? DatabaseTunnelMode.Never
            : DatabaseTunnelMode.Auto;
    }
}
