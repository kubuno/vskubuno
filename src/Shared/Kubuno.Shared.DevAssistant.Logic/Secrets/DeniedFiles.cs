using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Kubuno.Shared.DevAssistant.Logic.Secrets
{
    /// <summary>
    /// Files the assistant never reads, attaches or writes (docs/AI-ASSISTANT.md sections 7.4 and 8.3), and the path
    /// rules every file tool applies: inside a solution root only, never under <c>.git\</c>, <c>.vs\</c>, <c>target\</c>,
    /// <c>node_modules\</c> or the user's <c>.ssh</c> folder.
    /// </summary>
    public static class DeniedFiles
    {
        private static readonly string[] DeniedNames =
        {
            ".git-credentials", ".npmrc", ".pypirc", "secrets.json", "id_rsa", "id_dsa", "id_ecdsa", "id_ed25519", ".netrc", "_netrc",
        };

        private static readonly string[] DeniedExtensions = { ".pem", ".key", ".pfx", ".p12", ".kdbx", ".jks", ".keystore", ".snk" };

        private static readonly string[] DeniedFolders = { ".git", ".vs", "target", "node_modules", ".ssh", "usersecrets", "user-secrets" };

        /// <summary>Why <paramref name="path"/> is denied (French or English text from the caller's table), or null when it is allowed.</summary>
        public static string? WhyDenied(string path, IReadOnlyCollection<string> solutionRoots)
        {
            if (solutionRoots is null || solutionRoots.Count == 0)
            {
                return "outside-solution";
            }

            string full;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return "invalid-path";
            }

            if (!solutionRoots.Any(root => IsUnder(full, root)))
            {
                return "outside-solution";
            }

            var name = Path.GetFileName(full).ToLowerInvariant();
            if (name.StartsWith(".env", StringComparison.Ordinal) || name.StartsWith("credentials", StringComparison.Ordinal) ||
                name.StartsWith("id_rsa", StringComparison.Ordinal) || DeniedNames.Contains(name) ||
                DeniedExtensions.Contains(Path.GetExtension(name)))
            {
                return "secret-file";
            }

            var root = solutionRoots.First(r => IsUnder(full, r));
            var relative = full.Substring(Path.GetFullPath(root).TrimEnd('\\', '/').Length).TrimStart('\\', '/');
            var segments = relative.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < segments.Length - 1; i++)
            {
                if (DeniedFolders.Contains(segments[i].ToLowerInvariant()))
                {
                    return "protected-folder";
                }
            }

            return null;
        }

        /// <summary>True when <paramref name="path"/> is (or is inside) <paramref name="root"/>.</summary>
        public static bool IsUnder(string path, string root)
        {
            if (string.IsNullOrEmpty(root))
            {
                return false;
            }

            var normalizedRoot = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            var normalizedPath = Path.GetFullPath(path).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }
    }
}
