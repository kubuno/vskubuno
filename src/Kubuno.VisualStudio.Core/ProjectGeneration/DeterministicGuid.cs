using System;
using System.Security.Cryptography;
using System.Text;

namespace Kubuno.VisualStudio.Core.ProjectGeneration
{
    /// <summary>
    /// A stable GUID derived from a seed string (MD5 of its UTF-8 bytes, RFC 4122 §4.3-shaped as a
    /// version-3 "name-based" GUID). Used for a generated <c>.sln</c>'s per-project GUIDs
    /// (docs/RSPROJ.md work package 5) so re-running the generator against the same workspace
    /// members always proposes the same GUID for the same project path - a re-run is then a clean
    /// no-op diff instead of gratuitous GUID churn on every regeneration.
    /// </summary>
    public static class DeterministicGuid
    {
        public static Guid From(string seed)
        {
            if (seed is null)
            {
                throw new ArgumentNullException(nameof(seed));
            }

            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(seed));

            // Version 3 (name-based, MD5) and RFC 4122 variant bits, so the result is a
            // well-formed GUID and not just "16 hash bytes that happen to fit".
            hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
            hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
            return new Guid(hash);
        }
    }
}
