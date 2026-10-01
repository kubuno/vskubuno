using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kubuno.Desktop.Resources.Editor
{
    /// <summary>Writes the blank files "New Image" / "New Icon" create (before Visual Studio's image editor opens them).</summary>
    public static class BlankResources
    {
        /// <summary>The sizes of an icon's frames (the usual small, medium and large).</summary>
        public static readonly int[] IconSizes = { 16, 32, 48 };

        public static readonly IReadOnlyList<string> ImageFormats = new[] { "png", "bmp", "gif", "jpg" };

        /// <summary>A blank image: transparent for PNG and GIF, white for BMP and JPEG (no usable alpha there).</summary>
        public static byte[] Image(string format, int size = 32)
        {
            var transparent = format is "png" or "gif";
            var pixels = new byte[size * size * 4];
            if (!transparent)
            {
                for (var i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = 255;
                }
            }

            var source = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
            BitmapEncoder encoder = format switch
            {
                "bmp" => new BmpBitmapEncoder(),
                "gif" => new GifBitmapEncoder(),
                "jpg" or "jpeg" => new JpegBitmapEncoder(),
                _ => new PngBitmapEncoder(),
            };
            BitmapSource frame = transparent ? source : new FormatConvertedBitmap(source, PixelFormats.Bgr32, null, 0);
            encoder.Frames.Add(BitmapFrame.Create(frame));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }

        /// <summary>A blank icon with a transparent frame for each of <see cref="IconSizes"/>.</summary>
        public static byte[] Icon()
        {
            var frames = new List<(int, byte[])>();
            foreach (var size in IconSizes)
            {
                frames.Add((size, Image("png", size)));
            }

            return IcoWriter.Write(frames);
        }

        /// <summary>The first free <c>dir\name.ext</c> (<c>name2.ext</c>, <c>name3.ext</c>...).</summary>
        public static string FreeFile(string directory, string name, string extension)
        {
            var candidate = Path.Combine(directory, name + "." + extension);
            for (var i = 2; File.Exists(candidate); i++)
            {
                candidate = Path.Combine(directory, name + i.ToString(CultureInfo.InvariantCulture) + "." + extension);
            }

            return candidate;
        }
    }

    /// <summary>A minimal <c>.ico</c> container whose frames are PNG images (supported since Windows Vista).</summary>
    public static class IcoWriter
    {
        public static byte[] Write(IReadOnlyList<(int Size, byte[] Png)> frames)
        {
            if (frames.Count == 0)
            {
                throw new ArgumentException("an icon needs at least one frame", nameof(frames));
            }

            using var stream = new MemoryStream();
            using var w = new BinaryWriter(stream);
            w.Write((ushort)0);
            w.Write((ushort)1);
            w.Write((ushort)frames.Count);
            var offset = 6 + 16 * frames.Count;
            foreach (var (size, png) in frames)
            {
                var dim = size >= 256 ? (byte)0 : (byte)size;
                w.Write(dim);
                w.Write(dim);
                w.Write((byte)0);
                w.Write((byte)0);
                w.Write((ushort)1);
                w.Write((ushort)32);
                w.Write((uint)png.Length);
                w.Write((uint)offset);
                offset += png.Length;
            }

            foreach (var (_, png) in frames)
            {
                w.Write(png);
            }

            w.Flush();
            return stream.ToArray();
        }
    }
}
