using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Kubuno.Desktop.Logic.DesignSurface
{
    /// <summary>
    /// The file names of <c>kubuno_ui</c>, the Kubuno desktop design system shipped as a Rust dylib
    /// (docs/DESIGNER.md, "One file name per kubuno_ui build"). A Rust dylib has no stable ABI, so every
    /// build is named after itself, <c>kubuno_ui-&lt;16 hex digits&gt;.dll</c>, and a program imports the
    /// exact name it was linked against: the name to ship next to a program is read from the program
    /// itself (<see cref="ImportedBy"/>), never assumed. Cargo still leaves a plain <c>kubuno_ui.dll</c>
    /// (an alias of the latest build, which rustc reads the crate's metadata from); builds made before
    /// per-build names import that plain name.
    /// </summary>
    public static class KubunoUiLibrary
    {
        public const string FileStem = "kubuno_ui";

        /// <summary>The unversioned name: Cargo's alias, and what builds before per-build names import.</summary>
        public const string PlainFileName = FileStem + ".dll";

        /// <summary><c>kubuno_ui-&lt;16 hex digits&gt;.dll</c>, or the plain <c>kubuno_ui.dll</c> (case-insensitive).</summary>
        public static bool IsLibraryFileName(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            if (string.Equals(name, PlainFileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return IsHashedFileName(name);
        }

        /// <summary><c>kubuno_ui-&lt;16 hex digits&gt;.dll</c> only.</summary>
        public static bool IsHashedFileName(string? name)
        {
            const string prefix = FileStem + "-";
            const string suffix = ".dll";
            if (name is null || name.Length != prefix.Length + 16 + suffix.Length
                || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || !name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return name.Substring(prefix.Length, 16).All(Uri.IsHexDigit);
        }

        /// <summary>The <c>kubuno_ui</c> DLL name <paramref name="program"/> imports, or <see langword="null"/> (none, or not a readable PE file).</summary>
        public static string? ImportedBy(string program) =>
            ReadImportedDllNames(program).FirstOrDefault(IsLibraryFileName);

        /// <summary>
        /// The DLL names in the import directory of the PE file <paramref name="path"/> (PE32 or PE32+), in
        /// table order; empty when the file is not a readable PE image. Delay-loaded imports are not listed.
        /// </summary>
        public static IReadOnlyList<string> ReadImportedDllNames(string path)
        {
            try
            {
                // Only the headers, the import descriptors and the names are read, not the whole (large) image.
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return ReadImports(new StreamImage(stream));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return Array.Empty<string>();
            }
        }

        /// <summary><see cref="ReadImportedDllNames(string)"/> over the file's bytes.</summary>
        public static IReadOnlyList<string> ReadImports(byte[] image) =>
            image is null ? Array.Empty<string>() : ReadImports(new ArrayImage(image));

        private static IReadOnlyList<string> ReadImports(IImage image)
        {
            var names = new List<string>();
            if (image.Length < 0x40 || image[0] != (byte)'M' || image[1] != (byte)'Z')
            {
                return names;
            }

            var pe = ReadInt32(image, 0x3C);
            if (pe < 0 || pe + 24 > image.Length || image[pe] != (byte)'P' || image[pe + 1] != (byte)'E' || image[pe + 2] != 0 || image[pe + 3] != 0)
            {
                return names;
            }

            var coff = pe + 4;
            int sectionCount = ReadUInt16(image, coff + 2);
            int optionalSize = ReadUInt16(image, coff + 16);
            var optional = coff + 20;
            if (optional + optionalSize > image.Length || optionalSize < 2)
            {
                return names;
            }

            // The data directories follow the fields that differ between PE32 (0x10b) and PE32+ (0x20b).
            var magic = ReadUInt16(image, optional);
            var directories = magic switch
            {
                0x10b => optional + 96,
                0x20b => optional + 112,
                _ => -1,
            };
            const int ImportDirectory = 1;
            if (directories < 0 || directories + ((ImportDirectory + 1) * 8) > optional + optionalSize)
            {
                return names;
            }

            var importRva = ReadInt32(image, directories + (ImportDirectory * 8));
            if (importRva <= 0)
            {
                return names;
            }

            var sections = optional + optionalSize;
            int ToOffset(int rva)
            {
                for (var i = 0; i < sectionCount; i++)
                {
                    var header = sections + (i * 40);
                    if (header + 40 > image.Length)
                    {
                        return -1;
                    }

                    var virtualSize = ReadInt32(image, header + 8);
                    var virtualAddress = ReadInt32(image, header + 12);
                    var rawSize = ReadInt32(image, header + 16);
                    var rawPointer = ReadInt32(image, header + 20);
                    var span = Math.Max(virtualSize, rawSize);
                    if (rva >= virtualAddress && rva < virtualAddress + span)
                    {
                        var offset = rawPointer + (rva - virtualAddress);
                        return offset >= 0 && offset < image.Length ? offset : -1;
                    }
                }

                return -1;
            }

            var descriptor = ToOffset(importRva);
            // IMAGE_IMPORT_DESCRIPTOR: 20 bytes, Name RVA at +12; the table ends with an all-zero entry.
            for (var guard = 0; descriptor >= 0 && descriptor + 20 <= image.Length && guard < 4096; guard++, descriptor += 20)
            {
                var nameRva = ReadInt32(image, descriptor + 12);
                if (nameRva == 0)
                {
                    break;
                }

                var nameOffset = ToOffset(nameRva);
                if (nameOffset < 0)
                {
                    continue;
                }

                var name = new StringBuilder();
                for (var at = nameOffset; at < image.Length && image[at] != 0 && name.Length < 512; at++)
                {
                    name.Append((char)image[at]);
                }

                names.Add(name.ToString());
            }

            return names;
        }

        private static int ReadInt32(IImage image, int offset) =>
            offset < 0 || offset + 4 > image.Length ? -1 : image[offset] | (image[offset + 1] << 8) | (image[offset + 2] << 16) | (image[offset + 3] << 24);

        private static int ReadUInt16(IImage image, int offset) =>
            offset < 0 || offset + 2 > image.Length ? 0 : image[offset] | (image[offset + 1] << 8);

        /// <summary>Random access to a PE file's bytes.</summary>
        private interface IImage
        {
            long Length { get; }

            /// <summary>The byte at <paramref name="offset"/> (0 past the end).</summary>
            byte this[int offset] { get; }
        }

        private sealed class ArrayImage : IImage
        {
            private readonly byte[] _bytes;

            public ArrayImage(byte[] bytes) => _bytes = bytes;

            public long Length => _bytes.Length;

            public byte this[int offset] => offset >= 0 && offset < _bytes.Length ? _bytes[offset] : (byte)0;
        }

        /// <summary>A file read in 4 KiB pages on demand (the few pages the headers and the import table live in).</summary>
        private sealed class StreamImage : IImage
        {
            private const int PageSize = 4096;
            private readonly Stream _stream;
            private readonly Dictionary<long, byte[]> _pages = new Dictionary<long, byte[]>();

            public StreamImage(Stream stream)
            {
                _stream = stream;
                Length = stream.Length;
            }

            public long Length { get; }

            public byte this[int offset]
            {
                get
                {
                    if (offset < 0 || offset >= Length)
                    {
                        return 0;
                    }

                    var index = offset / PageSize;
                    if (!_pages.TryGetValue(index, out var page))
                    {
                        page = new byte[PageSize];
                        _stream.Position = (long)index * PageSize;
                        var read = 0;
                        while (read < PageSize)
                        {
                            var n = _stream.Read(page, read, PageSize - read);
                            if (n <= 0)
                            {
                                break;
                            }

                            read += n;
                        }

                        _pages[index] = page;
                    }

                    return page[offset % PageSize];
                }
            }
        }
    }
}
