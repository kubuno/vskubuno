using System;
using Kubuno.Core.Logic.Localization;

namespace Kubuno.Core.Logic.Remote
{
    /// <summary>Why an ssh connection to the remote Linux host ended, read from ssh's error output.</summary>
    public enum SshFailureKind
    {
        /// <summary>Nothing recognisable (the raw error output is shown).</summary>
        Other,

        /// <summary><c>Permission denied (publickey)</c>: the key is not in the account's authorized_keys (or the user is wrong).</summary>
        KeyNotAuthorized,

        /// <summary><c>Host key verification failed</c> with no key known: the user must accept the host's fingerprint.</summary>
        UnknownHostKey,

        /// <summary>The host presents another key than the accepted one: refused, never accepted from Visual Studio.</summary>
        HostKeyChanged,

        /// <summary>Timeout, refused connection, no route: the host or its ssh server cannot be reached.</summary>
        HostUnreachable,

        /// <summary>The host name does not resolve.</summary>
        NameNotResolved,

        /// <summary>The tunnel's local port is taken by another program.</summary>
        LocalPortInUse,

        /// <summary>The private key file does not exist or cannot be read.</summary>
        KeyFileMissing,

        /// <summary>The private key file is readable by other users (OpenSSH refuses it).</summary>
        KeyFilePermissions,

        /// <summary>The private key has a passphrase (batch mode never asks for it).</summary>
        KeyHasPassphrase,
    }

    /// <summary>Classifies ssh's error output and words the info bar / Output pane message (French or English, <see cref="UiLanguage"/>).</summary>
    public static class SshFailure
    {
        public static SshFailureKind Classify(string? errorOutput)
        {
            var text = errorOutput ?? string.Empty;
            bool Has(string fragment) => text.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;

            // Order matters: a changed key also prints "Host key verification failed".
            if (Has("REMOTE HOST IDENTIFICATION HAS CHANGED") || Has("POSSIBLE DNS SPOOFING"))
            {
                return SshFailureKind.HostKeyChanged;
            }

            if (Has("Host key verification failed") || Has("host key is known for") || Has("No ED25519 host key is known") || Has("No ECDSA host key is known") || Has("No RSA host key is known"))
            {
                return SshFailureKind.UnknownHostKey;
            }

            if (Has("UNPROTECTED PRIVATE KEY FILE") || Has("bad permissions"))
            {
                return SshFailureKind.KeyFilePermissions;
            }

            // A missing -i file is only a warning ("Identity file ... not accessible"); the connection then fails with
            // "Permission denied", hence before it.
            if (Has("no such identity") || (Has("Identity file") && Has("not accessible")))
            {
                return SshFailureKind.KeyFileMissing;
            }

            if (Has("incorrect passphrase") || Has("Enter passphrase"))
            {
                return SshFailureKind.KeyHasPassphrase;
            }

            if (Has("Permission denied"))
            {
                return SshFailureKind.KeyNotAuthorized;
            }

            if (Has("Could not resolve hostname") || Has("No such host is known") || Has("Name or service not known"))
            {
                return SshFailureKind.NameNotResolved;
            }

            if (Has("Address already in use") || Has("cannot listen to port") || Has("Could not request local forwarding") || Has("bind [") || Has("bind:"))
            {
                return SshFailureKind.LocalPortInUse;
            }

            if (Has("Connection timed out") || Has("Connection refused") || Has("No route to host") || Has("Network is unreachable") || Has("Operation timed out")
                || Has("Connection reset") || Has("Connection closed by") || Has("Software caused connection abort"))
            {
                return SshFailureKind.HostUnreachable;
            }

            return SshFailureKind.Other;
        }

        /// <summary>The sentence that explains the failure and its fix (no trailing period handling needed: a full sentence).</summary>
        public static string Describe(SshFailureKind kind, RemoteHostSettings settings, int localPort, string? errorOutput)
        {
            var fr = UiLanguage.IsFrench;
            var destination = settings.Destination + (settings.Port == 22 ? string.Empty : ":" + settings.Port);
            switch (kind)
            {
                case SshFailureKind.KeyNotAuthorized:
                    return fr
                        ? "La clé " + settings.PrivateKeyPath + " n'est pas autorisée sur " + destination + ". Ajoutez sa partie publique (" + settings.PrivateKeyPath + ".pub) au fichier ~/.ssh/authorized_keys du compte " + settings.User + ", ou corrigez l'utilisateur dans Outils > Options > Kubuno > Hôte Linux distant."
                        : "The key " + settings.PrivateKeyPath + " is not authorised on " + destination + ". Add its public half (" + settings.PrivateKeyPath + ".pub) to ~/.ssh/authorized_keys of the account " + settings.User + ", or fix the user in Tools > Options > Kubuno > Remote Linux host.";
                case SshFailureKind.UnknownHostKey:
                    return fr
                        ? "L'hôte " + settings.Host + " n'est pas encore connu de Kubuno (" + settings.KnownHostsPath + ")."
                        : "The host " + settings.Host + " is not known to Kubuno yet (" + settings.KnownHostsPath + ").";
                case SshFailureKind.HostKeyChanged:
                    return fr
                        ? "La clé d'hôte de " + settings.Host + " a CHANGÉ depuis son acceptation : connexion refusée. Si le serveur a été réinstallé, vérifiez sa nouvelle empreinte puis retirez l'ancienne ligne de " + settings.KnownHostsPath + "."
                        : "The host key of " + settings.Host + " has CHANGED since it was accepted: connection refused. If the server was reinstalled, check its new fingerprint, then remove the old line from " + settings.KnownHostsPath + ".";
                case SshFailureKind.HostUnreachable:
                    return fr
                        ? "L'hôte " + settings.Host + " (port " + settings.Port + ") est injoignable. Vérifiez qu'il est allumé, sur le réseau, et que son serveur SSH écoute."
                        : "The host " + settings.Host + " (port " + settings.Port + ") cannot be reached. Check that it is on, on the network, and that its SSH server is listening.";
                case SshFailureKind.NameNotResolved:
                    return fr
                        ? "Le nom d'hôte " + settings.Host + " est introuvable. Corrigez-le dans Outils > Options > Kubuno > Hôte Linux distant."
                        : "The host name " + settings.Host + " does not resolve. Fix it in Tools > Options > Kubuno > Remote Linux host.";
                case SshFailureKind.LocalPortInUse:
                    return fr
                        ? "Le port local " + localPort + " est déjà pris par un autre programme : le tunnel ne peut pas l'écouter."
                        : "The local port " + localPort + " is already taken by another program: the tunnel cannot listen on it.";
                case SshFailureKind.KeyFileMissing:
                    return fr
                        ? "La clé privée " + settings.PrivateKeyPath + " est introuvable. Créez-la (ssh-keygen -t ed25519 -f \"" + settings.PrivateKeyPath + "\" -N \"\") ou corrigez son chemin dans les options."
                        : "The private key " + settings.PrivateKeyPath + " does not exist. Create it (ssh-keygen -t ed25519 -f \"" + settings.PrivateKeyPath + "\" -N \"\") or fix its path in the options.";
                case SshFailureKind.KeyFilePermissions:
                    return fr
                        ? "OpenSSH refuse la clé " + settings.PrivateKeyPath + " : d'autres comptes Windows peuvent la lire. Limitez ses droits à votre seul compte."
                        : "OpenSSH refuses the key " + settings.PrivateKeyPath + ": other Windows accounts can read it. Restrict its permissions to your account.";
                case SshFailureKind.KeyHasPassphrase:
                    return fr
                        ? "La clé " + settings.PrivateKeyPath + " est protégée par une phrase secrète, que Kubuno ne demande jamais. Utilisez une clé dédiée sans phrase secrète."
                        : "The key " + settings.PrivateKeyPath + " has a passphrase, which Kubuno never asks for. Use a dedicated key without one.";
                default:
                    var detail = FirstLine(errorOutput);
                    return fr
                        ? "La connexion SSH à " + destination + " a échoué" + (detail.Length > 0 ? " : " + detail : ".")
                        : "The SSH connection to " + destination + " failed" + (detail.Length > 0 ? ": " + detail : ".");
            }
        }

        private static string FirstLine(string? text)
        {
            foreach (var line in (text ?? string.Empty).Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0 && !trimmed.StartsWith("Warning: Permanently added", StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed;
                }
            }

            return string.Empty;
        }
    }
}
