using System;
using System.IO;
using System.Linq;
using Kubuno.Shared.Logic.Localization;
using Kubuno.Shared.Logic.Remote;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Kubuno.Shared.Tests.Remote
{
    /// <summary>The remote Linux host: settings, the ssh command lines, known_hosts, the failure messages and the tunnel decision (docs/WEB.md, "The development database").</summary>
    [TestClass]
    public sealed class RemoteHostTests
    {
        private static string Expand(string text) => text.Replace("%USERPROFILE%", @"C:\Users\dev");

        private static RemoteHostSettings Defaults() => RemoteHostSettings.Defaults(Expand);

        [TestMethod]
        public void DefaultsAreTheTeamHostAndTheDedicatedKey()
        {
            var settings = Defaults();
            Assert.AreEqual("192.168.1.220", settings.Host);
            Assert.AreEqual("martinien", settings.User);
            Assert.AreEqual(22, settings.Port);
            Assert.AreEqual(@"C:\Users\dev\.ssh\id_ed25519_kubuno", settings.PrivateKeyPath);
            Assert.AreEqual(@"C:\Users\dev\.ssh\known_hosts_kubuno", settings.KnownHostsPath);
            Assert.AreEqual("martinien@192.168.1.220", settings.Destination);
            Assert.IsNull(settings.Validate());
        }

        [TestMethod]
        public void EmptyValuesFallBackToTheDefaults()
        {
            var settings = RemoteHostSettings.Create("  ", "", 0, null, " ", Expand);
            Assert.AreEqual("192.168.1.220", settings.Host);
            Assert.AreEqual("martinien", settings.User);
            Assert.AreEqual(22, settings.Port);
            Assert.AreEqual(@"C:\Users\dev\.ssh\known_hosts_kubuno", settings.KnownHostsPath);
        }

        [DataTestMethod]
        [DataRow("-oProxyCommand=calc", "martinien", 22)]
        [DataRow("host name", "martinien", 22)]
        [DataRow("192.168.1.220", "-l", 22)]
        [DataRow("192.168.1.220", "a b", 22)]
        [DataRow("192.168.1.220", "martinien", 70000)]
        public void SettingsThatCouldInjectSshOptionsAreRefused(string host, string user, int port)
        {
            var settings = new RemoteHostSettings(host, user, port, @"C:\k", @"C:\h");
            Assert.IsNotNull(settings.Validate());
            Assert.ThrowsExactly<ArgumentException>(() => SshCommandLine.TunnelArguments(settings, 55432, 5432));
        }

        [TestMethod]
        public void TunnelArgumentsForwardTheLocalPortInBatchModeWithStrictHostKeys()
        {
            var arguments = SshCommandLine.TunnelArguments(Defaults(), 55432, 5432).ToList();
            CollectionAssert.AreEqual(new[] { "-N", "-L", "55432:localhost:5432" }, arguments.Take(3).ToArray());
            Assert.AreEqual("martinien@192.168.1.220", arguments.Last());

            string Option(string name) => arguments.Where((a, i) => i > 0 && arguments[i - 1] == "-o" && a.StartsWith(name + "=", StringComparison.Ordinal)).Single().Substring(name.Length + 1);
            Assert.AreEqual("yes", Option("BatchMode"));
            Assert.AreEqual("yes", Option("ExitOnForwardFailure"));
            Assert.AreEqual("yes", Option("StrictHostKeyChecking"), "the host key is never accepted silently");
            Assert.AreEqual("yes", Option("IdentitiesOnly"));
            Assert.AreEqual("C:/Users/dev/.ssh/known_hosts_kubuno", Option("UserKnownHostsFile"));
            Assert.AreEqual(@"C:\Users\dev\.ssh\id_ed25519_kubuno", arguments[arguments.IndexOf("-i") + 1]);
            Assert.AreEqual("22", arguments[arguments.IndexOf("-p") + 1]);
            Assert.AreEqual("none", arguments[arguments.IndexOf("-F") + 1]);
            Assert.IsFalse(arguments.Any(a => a.IndexOf("StrictHostKeyChecking=no", StringComparison.OrdinalIgnoreCase) >= 0));
        }

        [TestMethod]
        public void OtherPortsAndPathsWithSpacesAreQuoted()
        {
            var settings = new RemoteHostSettings("db.example.lan", "kubuno", 2222, @"C:\Users\Jean Dupont\.ssh\id_ed25519_kubuno", @"C:\Users\Jean Dupont\.ssh\known_hosts_kubuno");
            var arguments = SshCommandLine.TunnelArguments(settings, 6000, 5433);
            Assert.AreEqual("6000:localhost:5433", arguments[2]);
            Assert.IsTrue(arguments.Contains("UserKnownHostsFile=\"C:/Users/Jean Dupont/.ssh/known_hosts_kubuno\""));
            var commandLine = SshCommandLine.ToCommandLine(arguments);
            StringAssert.Contains(commandLine, "-p 2222");
            StringAssert.Contains(commandLine, "-i \"C:\\Users\\Jean Dupont\\.ssh\\id_ed25519_kubuno\"");
            StringAssert.Contains(commandLine, "\"UserKnownHostsFile=\\\"C:/Users/Jean Dupont/.ssh/known_hosts_kubuno\\\"\"");
            StringAssert.EndsWith(commandLine, "kubuno@db.example.lan");
        }

        [DataTestMethod]
        [DataRow("plain", "plain")]
        [DataRow("two words", "\"two words\"")]
        [DataRow("", "\"\"")]
        [DataRow("a\"b", "\"a\\\"b\"")]
        [DataRow(@"C:\dir with space\", "\"C:\\dir with space\\\\\"")]
        public void QuoteFollowsTheWindowsCommandLineRules(string argument, string expected)
        {
            Assert.AreEqual(expected, SshCommandLine.Quote(argument));
        }

        [TestMethod]
        public void KeyscanReadsThePortAndAllKeyTypes()
        {
            var arguments = SshCommandLine.KeyscanArguments(new RemoteHostSettings("192.168.1.220", "martinien", 2222, "k", "h"));
            CollectionAssert.AreEqual(new[] { "-p", "2222", "-T", "10", "-t", "ed25519,ecdsa,rsa", "192.168.1.220" }, arguments.ToArray());
        }

        // The team host's real ED25519 key (public) and the fingerprint ssh-keygen -lf prints for it.
        private const string TeamHostKey = "AAAAC3NzaC1lZDI1NTE5AAAAIHaTzixgXH9PpxK6+rHBl8bXfjWGHM9Oe7+KDa7hBeKu";

        [TestMethod]
        public void FingerprintIsTheOneSshKeygenPrints()
        {
            Assert.AreEqual("SHA256:RPWPHwLhY7665tnblmhod4TToDI+SPuRZmSYi5C+zhg", KnownHosts.Fingerprint(TeamHostKey));
            Assert.IsNull(KnownHosts.Fingerprint("not base64!"));
        }

        [TestMethod]
        public void KeyscanOutputPrefersEd25519()
        {
            var output = "# 192.168.1.220:22 SSH-2.0-OpenSSH_9.6\n"
                + "192.168.1.220 ssh-rsa AAAAB3NzaC1yc2EAAAADAQABAAABAQ==\n"
                + "192.168.1.220 ecdsa-sha2-nistp256 AAAAE2VjZHNhLXNoYTItbmlzdHAyNTY=\n"
                + "192.168.1.220 ssh-ed25519 " + TeamHostKey + "\n";
            var keys = KnownHosts.Parse(output);
            Assert.AreEqual(3, keys.Count);
            var preferred = KnownHosts.Preferred(keys)!;
            Assert.AreEqual("ED25519", preferred.ShortType);
            Assert.AreEqual("SHA256:RPWPHwLhY7665tnblmhod4TToDI+SPuRZmSYi5C+zhg", preferred.Fingerprint);
        }

        [TestMethod]
        public void HostPatternUsesBracketsForOtherPorts()
        {
            Assert.AreEqual("192.168.1.220", KnownHosts.HostPattern("192.168.1.220", 22));
            Assert.AreEqual("[192.168.1.220]:2222", KnownHosts.HostPattern("192.168.1.220", 2222));
        }

        [TestMethod]
        public void AcceptAppendsTheKeyOnceAndCreatesTheFolder()
        {
            var directory = Path.Combine(Path.GetTempPath(), "kubuno-known-hosts-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(directory, ".ssh", "known_hosts_kubuno");
            try
            {
                var key = new HostKey("192.168.1.220", "ssh-ed25519", TeamHostKey);
                KnownHosts.Accept(path, key, "[192.168.1.220]:2222");
                KnownHosts.Accept(path, key, "[192.168.1.220]:2222");
                var text = File.ReadAllText(path);
                Assert.AreEqual("[192.168.1.220]:2222 ssh-ed25519 " + TeamHostKey + "\n", text);
                Assert.IsTrue(KnownHosts.Knows(text, "[192.168.1.220]:2222"));
                Assert.IsFalse(KnownHosts.Knows(text, "192.168.1.220"));

                File.WriteAllText(path, "other.lan ssh-ed25519 " + TeamHostKey);
                KnownHosts.Accept(path, key, "192.168.1.220");
                Assert.AreEqual("other.lan ssh-ed25519 " + TeamHostKey + "\n192.168.1.220 ssh-ed25519 " + TeamHostKey + "\n", File.ReadAllText(path));
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }

        [DataTestMethod]
        [DataRow("martinien@192.168.1.220: Permission denied (publickey).", SshFailureKind.KeyNotAuthorized)]
        [DataRow("No ED25519 host key is known for 192.168.1.220 and you have requested strict checking.\r\nHost key verification failed.", SshFailureKind.UnknownHostKey)]
        [DataRow("@@@@@@@@@@@\r\n@    WARNING: REMOTE HOST IDENTIFICATION HAS CHANGED!     @\r\nHost key verification failed.", SshFailureKind.HostKeyChanged)]
        [DataRow("ssh: connect to host 192.168.1.220 port 22: Connection timed out", SshFailureKind.HostUnreachable)]
        [DataRow("ssh: connect to host 192.168.1.220 port 22: Connection refused", SshFailureKind.HostUnreachable)]
        [DataRow("ssh: Could not resolve hostname nowhere.lan: No such host is known. ", SshFailureKind.NameNotResolved)]
        [DataRow("bind [127.0.0.1]:55432: Address already in use\r\nchannel_setup_fwd_listener_tcpip: cannot listen to port: 55432\r\nCould not request local forwarding.", SshFailureKind.LocalPortInUse)]
        [DataRow("Warning: Identity file C:/Users/dev/.ssh/missing not accessible: No such file or directory.\r\nmartinien@192.168.1.220: Permission denied (publickey).", SshFailureKind.KeyFileMissing)]
        [DataRow("WARNING: UNPROTECTED PRIVATE KEY FILE!", SshFailureKind.KeyFilePermissions)]
        [DataRow("kex_exchange_identification: something unexpected", SshFailureKind.Other)]
        public void FailuresAreClassified(string errorOutput, SshFailureKind expected)
        {
            Assert.AreEqual(expected, SshFailure.Classify(errorOutput));
        }

        [TestMethod]
        public void MessagesNameTheFix()
        {
            var previous = UiLanguage.ForceFrench;
            try
            {
                UiLanguage.ForceFrench = true;
                var fr = SshFailure.Describe(SshFailureKind.KeyNotAuthorized, Defaults(), 55432, null);
                StringAssert.Contains(fr, "n'est pas autorisée sur martinien@192.168.1.220");
                StringAssert.Contains(fr, "authorized_keys");
                StringAssert.Contains(SshFailure.Describe(SshFailureKind.HostUnreachable, Defaults(), 55432, null), "injoignable");

                UiLanguage.ForceFrench = false;
                StringAssert.Contains(SshFailure.Describe(SshFailureKind.LocalPortInUse, Defaults(), 55432, null), "55432");
                StringAssert.Contains(SshFailure.Describe(SshFailureKind.Other, Defaults(), 55432, "\nkex_exchange_identification: x\n"), "kex_exchange_identification: x");
            }
            finally
            {
                UiLanguage.ForceFrench = previous;
            }
        }

        [DataTestMethod]
        [DataRow(DatabaseTunnelMode.Auto, "localhost", "55432", true)]
        [DataRow(DatabaseTunnelMode.Auto, "127.0.0.1", "55432", true)]
        [DataRow(DatabaseTunnelMode.Auto, "[::1]", "55432", true)]
        [DataRow(DatabaseTunnelMode.Auto, "localhost", "5432", false)]
        [DataRow(DatabaseTunnelMode.Auto, "localhost", null, false)]
        [DataRow(DatabaseTunnelMode.Auto, "192.168.1.220", "55432", false)]
        [DataRow(DatabaseTunnelMode.Always, "192.168.1.220", "5432", true)]
        [DataRow(DatabaseTunnelMode.Never, "localhost", "55432", false)]
        public void TheTunnelOpensForTheLocalEndOrWhenAskedTo(DatabaseTunnelMode mode, string host, string? port, bool expected)
        {
            Assert.AreEqual(expected, DatabaseTunnelPolicy.IsNeeded(mode, host, port, 55432));
        }

        [TestMethod]
        public void StoredModesParse()
        {
            Assert.AreEqual(DatabaseTunnelMode.Always, DatabaseTunnelPolicy.ParseMode("always"));
            Assert.AreEqual(DatabaseTunnelMode.Never, DatabaseTunnelPolicy.ParseMode("Never"));
            Assert.AreEqual(DatabaseTunnelMode.Auto, DatabaseTunnelPolicy.ParseMode("auto"));
            Assert.AreEqual(DatabaseTunnelMode.Auto, DatabaseTunnelPolicy.ParseMode("garbage"));
        }
    }
}
