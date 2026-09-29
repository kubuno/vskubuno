using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Kubuno.Cargo.Win32
{
    /// <summary>
    /// Builder for a binary 32-bit RES file (the format produced by rc.exe and accepted directly
    /// by link.exe), so an executable can get an icon, a version block and a manifest without any
    /// resource compiler or build script.
    /// </summary>
    public sealed class Win32ResourceFile
    {
        /// <summary>RT_ICON.</summary>
        public const ushort RtIcon = 3;

        /// <summary>RT_GROUP_ICON.</summary>
        public const ushort RtGroupIcon = 14;

        /// <summary>RT_VERSION.</summary>
        public const ushort RtVersion = 16;

        /// <summary>RT_MANIFEST.</summary>
        public const ushort RtManifest = 24;

        /// <summary>Language id written on every resource (en-US).</summary>
        public const ushort LanguageEnglishUs = 0x0409;

        private const ushort FlagsIcon = 0x1010;      // MOVEABLE | DISCARDABLE (rc.exe RT_ICON)
        private const ushort FlagsGroupIcon = 0x1030; // MOVEABLE | PURE | DISCARDABLE (rc.exe RT_GROUP_ICON)
        private const ushort FlagsData = 0x0030;      // MOVEABLE | PURE (rc.exe RT_VERSION / RT_MANIFEST)

        private readonly List<Entry> _entries = new List<Entry>();
        private ushort _nextIconId = 1;

        private sealed class Entry
        {
            public ushort Type;
            public ushort Id;
            public ushort Flags;
            public byte[] Data = Array.Empty<byte>();
        }

        /// <summary>
        /// Adds an icon group from the bytes of a .ico file: one RT_ICON per image (ids unique in
        /// this file) and one RT_GROUP_ICON with id <paramref name="groupId"/>.
        /// </summary>
        /// <exception cref="InvalidDataException">The bytes are not a valid .ico file.</exception>
        public void AddIcon(ushort groupId, byte[] icoFileBytes)
        {
            if (icoFileBytes == null)
            {
                throw new ArgumentNullException(nameof(icoFileBytes));
            }

            if (icoFileBytes.Length < 6 || ReadU16(icoFileBytes, 0) != 0 || ReadU16(icoFileBytes, 2) != 1)
            {
                throw new InvalidDataException("Not a valid .ico file: the ICONDIR header is missing (reserved must be 0, type must be 1).");
            }

            int count = ReadU16(icoFileBytes, 4);
            if (count == 0)
            {
                throw new InvalidDataException("Not a valid .ico file: it contains no image.");
            }

            if (6 + count * 16 > icoFileBytes.Length)
            {
                throw new InvalidDataException("Not a valid .ico file: the directory is truncated.");
            }

            if (_nextIconId + count - 1 > ushort.MaxValue)
            {
                throw new InvalidDataException("Too many icon images.");
            }

            var group = new MemoryStream();
            WriteU16(group, 0);
            WriteU16(group, 1);
            WriteU16(group, (ushort)count);

            var images = new List<Entry>();
            for (int i = 0; i < count; i++)
            {
                int p = 6 + i * 16;
                long size = ReadU32(icoFileBytes, p + 8);
                long offset = ReadU32(icoFileBytes, p + 12);
                if (size == 0 || offset < 6 + count * 16 || offset + size > icoFileBytes.Length)
                {
                    throw new InvalidDataException($"Not a valid .ico file: image {i} lies outside the file.");
                }

                ushort id = (ushort)(_nextIconId + i);
                byte[] image = new byte[size];
                Buffer.BlockCopy(icoFileBytes, (int)offset, image, 0, (int)size);
                images.Add(new Entry { Type = RtIcon, Id = id, Flags = FlagsIcon, Data = image });

                // GRPICONDIRENTRY: same as ICONDIRENTRY but the 4-byte offset is replaced by a 2-byte resource id.
                group.Write(icoFileBytes, p, 12);
                WriteU16(group, id);
            }

            _nextIconId = (ushort)(_nextIconId + count);
            _entries.AddRange(images);
            _entries.Add(new Entry { Type = RtGroupIcon, Id = groupId, Flags = FlagsGroupIcon, Data = group.ToArray() });
        }

        /// <summary>Adds the RT_VERSION resource (id 1).</summary>
        public void AddVersionInfo(Win32VersionInfo info)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            Version fileVersion = Win32VersionInfo.ParseVersion(info.FileVersion);
            Version productVersion = Win32VersionInfo.ParseVersion(info.ProductVersion);

            var fixedInfo = new MemoryStream();
            WriteU32(fixedInfo, 0xFEEF04BD);
            WriteU32(fixedInfo, 0x00010000);
            WriteU32(fixedInfo, ((uint)fileVersion.Major << 16) | (uint)fileVersion.Minor);
            WriteU32(fixedInfo, ((uint)fileVersion.Build << 16) | (uint)fileVersion.Revision);
            WriteU32(fixedInfo, ((uint)productVersion.Major << 16) | (uint)productVersion.Minor);
            WriteU32(fixedInfo, ((uint)productVersion.Build << 16) | (uint)productVersion.Revision);
            WriteU32(fixedInfo, 0x3F);
            WriteU32(fixedInfo, 0);
            WriteU32(fixedInfo, 0x00040004);
            WriteU32(fixedInfo, info.IsDll ? 2u : 1u);
            WriteU32(fixedInfo, 0);
            WriteU32(fixedInfo, 0);
            WriteU32(fixedInfo, 0);
            byte[] fixedBytes = fixedInfo.ToArray();

            var strings = new List<byte[]>();
            AddString(strings, "CompanyName", info.CompanyName);
            AddString(strings, "FileDescription", info.FileDescription);
            AddString(strings, "FileVersion", info.FileVersion);
            AddString(strings, "InternalName", info.InternalName);
            AddString(strings, "LegalCopyright", info.LegalCopyright);
            AddString(strings, "LegalTrademarks", info.LegalTrademarks);
            AddString(strings, "OriginalFilename", info.OriginalFilename);
            AddString(strings, "ProductName", info.ProductName);
            AddString(strings, "ProductVersion", info.ProductVersion);
            AddString(strings, "Comments", info.Comments);

            byte[] table = Node("040904B0", 1, null, 0, strings);
            byte[] stringFileInfo = Node("StringFileInfo", 1, null, 0, new List<byte[]> { table });

            var translation = new MemoryStream();
            WriteU16(translation, 0x0409);
            WriteU16(translation, 0x04B0);
            byte[] var = Node("Translation", 0, translation.ToArray(), 4, new List<byte[]>());
            byte[] varFileInfo = Node("VarFileInfo", 1, null, 0, new List<byte[]> { var });

            byte[] root = Node("VS_VERSION_INFO", 0, fixedBytes, (ushort)fixedBytes.Length, new List<byte[]> { stringFileInfo, varFileInfo });
            _entries.Add(new Entry { Type = RtVersion, Id = 1, Flags = FlagsData, Data = root });
        }

        /// <summary>Adds an RT_MANIFEST resource; the text is stored as UTF-8 without BOM.</summary>
        public void AddManifest(string xml, ushort id = 1)
        {
            if (xml == null)
            {
                throw new ArgumentNullException(nameof(xml));
            }

            _entries.Add(new Entry { Type = RtManifest, Id = id, Flags = FlagsData, Data = new UTF8Encoding(false).GetBytes(xml) });
        }

        /// <summary>Serializes the RES file.</summary>
        public byte[] ToArray()
        {
            using (var ms = new MemoryStream())
            {
                Write(ms);
                return ms.ToArray();
            }
        }

        /// <summary>Writes the RES file to <paramref name="stream"/>.</summary>
        public void Write(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            // Mandatory leading empty resource (marks the file as 32-bit RES).
            WriteHeader(stream, 0, 0, 0, 0, 0, 0);
            foreach (Entry e in _entries)
            {
                WriteHeader(stream, (uint)e.Data.Length, e.Type, e.Id, e.Flags, LanguageEnglishUs, 0);
                stream.Write(e.Data, 0, e.Data.Length);
                Pad(stream, e.Data.Length);
            }
        }

        private static void WriteHeader(Stream s, uint dataSize, ushort type, ushort id, ushort flags, ushort lang, uint dataVersion)
        {
            WriteU32(s, dataSize);
            WriteU32(s, 32);
            WriteU16(s, 0xFFFF);
            WriteU16(s, type);
            WriteU16(s, 0xFFFF);
            WriteU16(s, id);
            WriteU32(s, dataVersion);
            WriteU16(s, flags);
            WriteU16(s, lang);
            WriteU32(s, 0); // Version
            WriteU32(s, 0); // Characteristics
        }

        private static void AddString(List<byte[]> list, string key, string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            byte[] data = Encoding.Unicode.GetBytes(value + "\0");
            list.Add(Node(key, 1, data, (ushort)(data.Length / 2), new List<byte[]>()));
        }

        // Builds one VS_VERSIONINFO-style node: wLength, wValueLength, wType, szKey, pad, value, pad, children.
        // Children are DWORD-aligned relative to the node start (which is itself DWORD-aligned in the parent).
        private static byte[] Node(string key, ushort type, byte[]? value, ushort valueLength, List<byte[]> children)
        {
            var ms = new MemoryStream();
            WriteU16(ms, 0); // wLength patched below
            WriteU16(ms, valueLength);
            WriteU16(ms, type);
            byte[] k = Encoding.Unicode.GetBytes(key + "\0");
            ms.Write(k, 0, k.Length);
            Pad(ms, (int)ms.Length);
            if (value != null)
            {
                ms.Write(value, 0, value.Length);
            }

            foreach (byte[] child in children)
            {
                Pad(ms, (int)ms.Length);
                ms.Write(child, 0, child.Length);
            }

            byte[] result = ms.ToArray();
            if (result.Length > ushort.MaxValue)
            {
                throw new InvalidDataException("Version resource is too large.");
            }

            result[0] = (byte)(result.Length & 0xFF);
            result[1] = (byte)(result.Length >> 8);
            return result;
        }

        private static void Pad(Stream s, long length)
        {
            int extra = (int)((4 - (length & 3)) & 3);
            for (int i = 0; i < extra; i++)
            {
                s.WriteByte(0);
            }
        }

        private static ushort ReadU16(byte[] b, int o) => (ushort)(b[o] | (b[o + 1] << 8));

        private static uint ReadU32(byte[] b, int o) => (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));

        private static void WriteU16(Stream s, ushort v)
        {
            s.WriteByte((byte)(v & 0xFF));
            s.WriteByte((byte)(v >> 8));
        }

        private static void WriteU32(Stream s, uint v)
        {
            s.WriteByte((byte)(v & 0xFF));
            s.WriteByte((byte)((v >> 8) & 0xFF));
            s.WriteByte((byte)((v >> 16) & 0xFF));
            s.WriteByte((byte)(v >> 24));
        }
    }
}
