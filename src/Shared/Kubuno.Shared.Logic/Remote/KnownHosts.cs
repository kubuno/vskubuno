using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Kubuno.Core.Logic.Remote
{
    /// <summary>One public host key: <c>&lt;host pattern&gt; &lt;type&gt; &lt;base64 key&gt;</c> (a known_hosts or ssh-keyscan line).</summary>
    public sealed class HostKey
    {
        public HostKey(string hostPattern, string keyType, string base64Key)
        {
            HostPattern = hostPattern;
            KeyType = keyType;
            Base64Key = base64Key;
        }

        public string HostPattern { get; }

        /// <summary><c>ssh-ed25519</c>, <c>ecdsa-sha2-nistp256</c>, <c>ssh-rsa</c>...</summary>
        public string KeyType { get; }

        public string Base64Key { get; }

        /// <summary><c>ED25519</c>, <c>ECDSA</c>, <c>RSA</c> (the short name ssh-keygen -l prints).</summary>
        public string ShortType =>
            KeyType.IndexOf("ed25519", StringComparison.OrdinalIgnoreCase) >= 0 ? "ED25519"
            : KeyType.StartsWith("ecdsa", StringComparison.OrdinalIgnoreCase) ? "ECDSA"
            : KeyType.IndexOf("rsa", StringComparison.OrdinalIgnoreCase) >= 0 ? "RSA"
            : KeyType;

        /// <summary><c>SHA256:...</c>, as <c>ssh-keygen -lf</c> and the ssh client print it (null when the key is not base64).</summary>
        public string? Fingerprint => KnownHosts.Fingerprint(Base64Key);

        /// <summary>The known_hosts line.</summary>
        public string ToLine() => HostPattern + " " + KeyType + " " + Base64Key;
    }

    /// <summary>
    /// The Kubuno-specific known_hosts file (<c>%USERPROFILE%\.ssh\known_hosts_kubuno</c>): the host keys the user accepted
    /// in Visual Studio after seeing their fingerprint. Only plain (not hashed) entries are written and recognised.
    /// </summary>
    public static class KnownHosts
    {
        /// <summary>The host as known_hosts names it: <c>host</c> on port 22, <c>[host]:port</c> otherwise.</summary>
        public static string HostPattern(string host, int port) =>
            port == 22 ? host : "[" + host + "]:" + port.ToString(CultureInfo.InvariantCulture);

        /// <summary>The <c>SHA256:</c> fingerprint of a base64 public key blob (unpadded base64 of its SHA-256), or null.</summary>
        public static string? Fingerprint(string base64Key)
        {
            byte[] blob;
            try
            {
                blob = Convert.FromBase64String(base64Key);
            }
            catch (FormatException)
            {
                return null;
            }

            using var sha = SHA256.Create();
            return "SHA256:" + Convert.ToBase64String(sha.ComputeHash(blob)).TrimEnd('=');
        }

        /// <summary>The keys of a known_hosts text or of ssh-keyscan's output (comments, markers and malformed lines skipped).</summary>
        public static IReadOnlyList<HostKey> Parse(string text)
        {
            var keys = new List<HostKey>();
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith("@", StringComparison.Ordinal))
                {
                    continue;
                }

                var fields = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length >= 3 && Fingerprint(fields[2]) is not null)
                {
                    keys.Add(new HostKey(fields[0], fields[1], fields[2]));
                }
            }

            return keys;
        }

        /// <summary>The key to show and accept among a host's keys: ED25519, then ECDSA, then RSA (the order the client prefers).</summary>
        public static HostKey? Preferred(IEnumerable<HostKey> keys)
        {
            var list = keys.ToList();
            return list.FirstOrDefault(k => k.ShortType == "ED25519")
                ?? list.FirstOrDefault(k => k.ShortType == "ECDSA")
                ?? list.FirstOrDefault(k => k.ShortType == "RSA")
                ?? list.FirstOrDefault();
        }

        /// <summary>Whether the text already has a key for this host pattern (a comma-separated list of names counts).</summary>
        public static bool Knows(string knownHostsText, string hostPattern) =>
            Parse(knownHostsText).Any(key => key.HostPattern.Split(',').Any(name => string.Equals(name, hostPattern, StringComparison.OrdinalIgnoreCase)));

        /// <summary>
        /// Adds an accepted key to the file (created with its folder when missing). The key's host pattern is rewritten to
        /// <paramref name="hostPattern"/>; a line already there is not added twice.
        /// </summary>
        public static void Accept(string knownHostsPath, HostKey key, string hostPattern)
        {
            var line = new HostKey(hostPattern, key.KeyType, key.Base64Key).ToLine();
            var existing = File.Exists(knownHostsPath) ? File.ReadAllText(knownHostsPath) : string.Empty;
            if (Parse(existing).Any(k => k.ToLine() == line))
            {
                return;
            }

            var directory = Path.GetDirectoryName(knownHostsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var separator = existing.Length > 0 && !existing.EndsWith("\n", StringComparison.Ordinal) ? "\n" : string.Empty;
            File.AppendAllText(knownHostsPath, separator + line + "\n");
        }
    }
}
