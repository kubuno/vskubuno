using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Kubuno.Web.Logic.WebDesigner
{
    /// <summary>
    /// The real spelling of a path (docs/WEB-VIEWS.md section 18): junctions, symbolic links and <c>subst</c> drives
    /// resolved and the on-disk case restored, so a project opened through a link (<c>C:\kubuno-build</c> → <c>E:\kubuno-build</c>)
    /// and the same project reached by its real path compare equal. Node and Vite hand out real paths (Vite serves
    /// <c>/@fs/E:/…</c>), so a dev server started from a link spelling cannot serve the project's own files: Visual Studio
    /// starts it from the real path, and every root comparison goes through <see cref="Canonical"/>.
    /// </summary>
    /// <remarks>
    /// Windows: <c>GetFinalPathNameByHandle</c> on the deepest existing ancestor (the rest of the path is appended as
    /// given), the <c>\\?\</c> prefix removed. A path on a mapped network drive keeps its drive letter (<c>Z:\src</c>, never
    /// <c>\\server\share\src</c>: <c>cmd.exe</c> refuses a UNC working directory). Elsewhere: the full path, symbolic
    /// links left as they are. Never throws.
    /// </remarks>
    public static class RealPaths
    {
        /// <summary>The real spelling of <paramref name="path"/> (see the class); the input's full path when it cannot be resolved.</summary>
        public static string Canonical(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            string full;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
            {
                return path;
            }

            if (!IsWindows)
            {
                return TrimTrailingSeparator(full);
            }

            // The deepest existing ancestor is resolved; the rest (a file not created yet) is kept as given.
            var existing = full;
            var rest = string.Empty;
            while (!string.IsNullOrEmpty(existing) && !File.Exists(existing) && !Directory.Exists(existing))
            {
                var parent = Path.GetDirectoryName(existing);
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, existing, StringComparison.OrdinalIgnoreCase))
                {
                    return TrimTrailingSeparator(full);
                }

                rest = rest.Length == 0 ? Path.GetFileName(existing) : Path.Combine(Path.GetFileName(existing), rest);
                existing = parent!;
            }

            var resolved = FinalPath(existing);
            if (resolved is null)
            {
                return TrimTrailingSeparator(full);
            }

            resolved = KeepMappedDrive(full, resolved);
            return TrimTrailingSeparator(rest.Length == 0 ? resolved : Path.Combine(resolved, rest));
        }

        /// <summary>Whether two paths name the same file or folder (after <see cref="Canonical"/>, case-insensitively on Windows).</summary>
        public static bool Same(string? a, string? b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            {
                return false;
            }

            var comparison = IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(TrimTrailingSeparator(Full(a!)), TrimTrailingSeparator(Full(b!)), comparison) ||
                   string.Equals(Canonical(a!), Canonical(b!), comparison);
        }

        /// <summary>
        /// <paramref name="path"/> relative to <paramref name="root"/> (native separators), whichever spelling each uses (link
        /// or real path); null when the path is not under the root.
        /// </summary>
        public static string? RelativeTo(string root, string path)
        {
            if (Under(Full(root), Full(path)) is { } direct)
            {
                return direct;
            }

            return Under(Canonical(root), Canonical(path));
        }

        private static string? Under(string root, string path)
        {
            var comparison = IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var prefix = TrimTrailingSeparator(root) + Path.DirectorySeparatorChar;
            if (string.Equals(TrimTrailingSeparator(path), TrimTrailingSeparator(root), comparison))
            {
                return string.Empty;
            }

            return path.StartsWith(prefix, comparison) ? path.Substring(prefix.Length) : null;
        }

        private static string Full(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
            {
                return path;
            }
        }

        private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        private static string TrimTrailingSeparator(string path)
        {
            // A drive root keeps its separator (C:\), every other folder loses it.
            var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (trimmed.Length == 0)
            {
                return path;
            }

            return trimmed.Length == 2 && trimmed[1] == ':' ? trimmed + Path.DirectorySeparatorChar : trimmed;
        }

        /// <summary>
        /// A path on a mapped network drive resolves to <c>\\server\share\…</c>: the drive letter is put back (the share's
        /// root resolves to the same prefix). Junctions and <c>subst</c> drives resolve to a local volume and stay resolved.
        /// </summary>
        private static string KeepMappedDrive(string input, string resolved)
        {
            if (!resolved.StartsWith(@"\\", StringComparison.Ordinal) || input.Length < 3 || input[1] != ':')
            {
                return resolved;
            }

            var driveRoot = input.Substring(0, 3);
            if (FinalPath(driveRoot) is { } shareRoot)
            {
                var prefix = TrimTrailingSeparator(shareRoot);
                if (resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var tail = resolved.Substring(prefix.Length).TrimStart('\\');
                    return driveRoot + tail;
                }
            }

            return resolved;
        }

        /// <summary><c>GetFinalPathNameByHandle</c> of an existing file or folder (DOS volume names, <c>\\?\</c> removed), or null.</summary>
        private static string? FinalPath(string path)
        {
            try
            {
                using var handle = CreateFile(path, 0, FileShareAll, IntPtr.Zero, OpenExisting, FileFlagBackupSemantics, IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    return null;
                }

                var buffer = new StringBuilder(512);
                var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
                if (length >= buffer.Capacity)
                {
                    buffer = new StringBuilder((int)length + 1);
                    length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
                }

                if (length == 0 || length >= buffer.Capacity)
                {
                    return null;
                }

                var result = buffer.ToString(0, (int)length);
                if (result.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                {
                    return @"\\" + result.Substring(8);
                }

                return result.StartsWith(@"\\?\", StringComparison.Ordinal) ? result.Substring(4) : result;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or Win32Exception or IOException)
            {
                return null;
            }
        }

        private const uint FileShareAll = 0x1 | 0x2 | 0x4;
        private const uint OpenExisting = 3;
        private const uint FileFlagBackupSemantics = 0x02000000;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint length, uint flags);
    }
}
