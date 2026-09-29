using System.Text;

namespace Kubuno.Cargo.Tests.Win32
{
    /// <summary>One resource parsed back from a RES file.</summary>
    internal sealed class ResEntry
    {
        public uint DataSize;
        public uint HeaderSize;
        public ushort Type;
        public ushort Id;
        public uint DataVersion;
        public ushort MemoryFlags;
        public ushort Language;
        public uint Version;
        public uint Characteristics;
        public long HeaderOffset;
        public long DataOffset;
        public byte[] Data = Array.Empty<byte>();
    }

    /// <summary>A minimal RES file reader used to verify the generator's output.</summary>
    internal static class ResReader
    {
        public static List<ResEntry> Read(byte[] bytes)
        {
            var list = new List<ResEntry>();
            long p = 0;
            while (p < bytes.Length)
            {
                Assert.Equal(0, p % 4);
                var e = new ResEntry
                {
                    HeaderOffset = p,
                    DataSize = BitConverter.ToUInt32(bytes, (int)p),
                    HeaderSize = BitConverter.ToUInt32(bytes, (int)p + 4),
                };
                int q = (int)p + 8;
                Assert.Equal(0xFFFF, BitConverter.ToUInt16(bytes, q));
                e.Type = BitConverter.ToUInt16(bytes, q + 2);
                Assert.Equal(0xFFFF, BitConverter.ToUInt16(bytes, q + 4));
                e.Id = BitConverter.ToUInt16(bytes, q + 6);
                e.DataVersion = BitConverter.ToUInt32(bytes, q + 8);
                e.MemoryFlags = BitConverter.ToUInt16(bytes, q + 12);
                e.Language = BitConverter.ToUInt16(bytes, q + 14);
                e.Version = BitConverter.ToUInt32(bytes, q + 16);
                e.Characteristics = BitConverter.ToUInt32(bytes, q + 20);
                Assert.Equal(32u, e.HeaderSize);
                e.DataOffset = p + e.HeaderSize;
                e.Data = bytes.AsSpan((int)e.DataOffset, (int)e.DataSize).ToArray();
                p = e.DataOffset + e.DataSize;
                p = (p + 3) & ~3L;
                list.Add(e);
            }

            Assert.Equal(bytes.Length, p);
            return list;
        }
    }

    /// <summary>A parsed VS_VERSIONINFO node.</summary>
    internal sealed class VersionNode
    {
        public string Key = "";
        public ushort Type;
        public ushort ValueLength;
        public byte[] Value = Array.Empty<byte>();
        public List<VersionNode> Children = new();
        public int Length;

        public string ValueText => Encoding.Unicode.GetString(Value).TrimEnd('\0');

        public static VersionNode Parse(byte[] data, int offset, out int end)
        {
            Assert.Equal(0, offset % 4);
            var n = new VersionNode
            {
                Length = BitConverter.ToUInt16(data, offset),
                ValueLength = BitConverter.ToUInt16(data, offset + 2),
                Type = BitConverter.ToUInt16(data, offset + 4),
            };
            int p = offset + 6;
            var sb = new StringBuilder();
            while (true)
            {
                char c = (char)BitConverter.ToUInt16(data, p);
                p += 2;
                if (c == 0)
                {
                    break;
                }

                sb.Append(c);
            }

            n.Key = sb.ToString();
            p = (p + 3) & ~3;
            int valueBytes = n.Type == 1 ? n.ValueLength * 2 : n.ValueLength;
            n.Value = data.AsSpan(p, valueBytes).ToArray();
            p += valueBytes;
            int limit = offset + n.Length;
            while (true)
            {
                int aligned = (p + 3) & ~3;
                if (aligned >= limit)
                {
                    break;
                }

                p = aligned;
                n.Children.Add(Parse(data, p, out p));
            }

            end = limit;
            return n;
        }

        public VersionNode Find(string key) => Children.Single(c => c.Key == key);
    }

    /// <summary>Builds .ico file bytes in memory.</summary>
    internal static class IcoBuilder
    {
        /// <summary>A 16x16 32bpp BMP-DIB image (BITMAPINFOHEADER + pixels + AND mask).</summary>
        public static byte[] Bmp16()
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(40); w.Write(16); w.Write(32); w.Write((ushort)1); w.Write((ushort)32);
            w.Write(0); w.Write(16 * 16 * 4); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            for (int i = 0; i < 256; i++) w.Write(0xFF3366CCu);
            w.Write(new byte[16 * 4]);
            return ms.ToArray();
        }

        /// <summary>A PNG-looking payload (signature plus filler; RT_ICON stores it opaquely).</summary>
        public static byte[] Png256()
        {
            var b = new byte[64];
            new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(b, 0);
            for (int i = 8; i < b.Length; i++) b[i] = (byte)i;
            return b;
        }

        /// <summary>Builds an .ico with a 16x16 BMP entry and a 256x256 PNG entry.</summary>
        public static byte[] Build()
        {
            byte[] bmp = Bmp16();
            byte[] png = Png256();
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)2);
            int off = 6 + 32;
            w.Write((byte)16); w.Write((byte)16); w.Write((byte)0); w.Write((byte)0); w.Write((ushort)1); w.Write((ushort)32);
            w.Write(bmp.Length); w.Write(off);
            w.Write((byte)0); w.Write((byte)0); w.Write((byte)0); w.Write((byte)0); w.Write((ushort)1); w.Write((ushort)32);
            w.Write(png.Length); w.Write(off + bmp.Length);
            w.Write(bmp); w.Write(png);
            return ms.ToArray();
        }
    }
}
