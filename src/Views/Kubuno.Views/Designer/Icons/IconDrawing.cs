using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kubuno.Views.Designer.Icons
{
    /// <summary>
    /// Draws icons with WPF for the Properties window and the icon picker: a glyph of the catalog from its own paths (stroked
    /// like Lucide, with round caps and joins, or filled), and an image file through WIC (PNG, ICO, JPEG, BMP, GIF, TIFF, WebP
    /// when the codec is installed). An SVG file is drawn by the language server instead (<see cref="IKbviewIconServices"/>),
    /// exactly as the application draws it.
    /// </summary>
    public static class IconDrawing
    {
        /// <summary><paramref name="glyph"/> as a drawing <paramref name="size"/> units square, in <paramref name="foreground"/> (accent layers in <paramref name="accent"/>).</summary>
        public static Drawing? ToDrawing(IconGlyph glyph, Color foreground, Color accent, double size)
        {
            if (glyph.Layers.Count == 0 || glyph.ViewBox <= 0)
            {
                return null;
            }

            var group = new DrawingGroup();
            var k = size / glyph.ViewBox;
            foreach (var layer in glyph.Layers)
            {
                Geometry geometry;
                try
                {
                    geometry = Geometry.Parse(layer.Data).CloneCurrentValue();
                }
                catch (FormatException)
                {
                    continue;
                }

                var t = layer.Transform.Count == 6 ? layer.Transform : new[] { 1.0, 0, 0, 1, 0, 0 };
                geometry.Transform = new MatrixTransform(t[0] * k, t[1] * k, t[2] * k, t[3] * k, t[4] * k, t[5] * k);
                var color = ParseColor(layer.Color) ?? layer.Role switch
                {
                    "accent" => accent,
                    "accentcontrast" => Colors.White,
                    _ => foreground,
                };
                var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(color.A * Math.Max(0, Math.Min(1, layer.Opacity))), color.R, color.G, color.B));
                brush.Freeze();
                if (layer.Stroke is { } width)
                {
                    var pen = new Pen(brush, width * k) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                    pen.Freeze();
                    group.Children.Add(new GeometryDrawing(null, pen, geometry));
                }
                else
                {
                    group.Children.Add(new GeometryDrawing(brush, null, geometry));
                }
            }

            // The full square, so a glyph that does not reach its edges keeps its place in it.
            group.Children.Insert(0, new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, size, size))));
            group.Freeze();
            return group;
        }

        /// <summary><paramref name="glyph"/> as an image source of <paramref name="size"/> device-independent pixels.</summary>
        public static ImageSource? ToImage(IconGlyph glyph, Color foreground, Color accent, double size)
        {
            var drawing = ToDrawing(glyph, foreground, accent, size);
            if (drawing is null)
            {
                return null;
            }

            var image = new DrawingImage(drawing);
            image.Freeze();
            return image;
        }

        /// <summary>An image file decoded by WIC (the frame closest to <paramref name="size"/> pixels for an icon file); null when it cannot be read.</summary>
        public static BitmapSource? LoadFile(string path, int size = 32)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreImageCache, BitmapCacheOption.OnLoad);
                BitmapFrame? best = null;
                foreach (var frame in decoder.Frames)
                {
                    if (best is null
                        || (Math.Max(frame.PixelWidth, frame.PixelHeight) >= size && (Math.Max(best.PixelWidth, best.PixelHeight) < size || frame.PixelWidth < best.PixelWidth))
                        || (Math.Max(best.PixelWidth, best.PixelHeight) < size && frame.PixelWidth > best.PixelWidth))
                    {
                        best = frame;
                    }
                }

                best?.Freeze();
                return best;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException or FileFormatException or InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                return null;
            }
        }

        /// <summary>Straight-alpha BGRA rows (what <c>kubuno/renderIcon</c> answers) as a frozen bitmap.</summary>
        public static BitmapSource? FromBgra(int width, int height, byte[] bgra)
        {
            if (width <= 0 || height <= 0 || bgra.Length < width * height * 4)
            {
                return null;
            }

            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4);
            bitmap.Freeze();
            return bitmap;
        }

        /// <summary>Renders <paramref name="image"/> into a GDI+ bitmap of <paramref name="width"/> x <paramref name="height"/> pixels (the Properties window's 16 px swatch).</summary>
        public static System.Drawing.Bitmap? ToGdi(ImageSource image, int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
                context.DrawImage(image, new Rect(0, 0, width, height));
            }

            var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);
            var pixels = new byte[width * height * 4];
            target.CopyPixels(pixels, width * 4, 0);
            var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, width, height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            try
            {
                for (var y = 0; y < height; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(pixels, y * width * 4, data.Scan0 + (y * data.Stride), width * 4);
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return bitmap;
        }

        /// <summary><c>#rrggbb</c> or <c>#rrggbbaa</c>; null otherwise.</summary>
        public static Color? ParseColor(string? text)
        {
            var t = (text ?? string.Empty).Trim();
            if (!t.StartsWith("#", StringComparison.Ordinal) || (t.Length != 7 && t.Length != 9))
            {
                return null;
            }

            if (!uint.TryParse(t.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            {
                return null;
            }

            return t.Length == 7
                ? Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v)
                : Color.FromArgb((byte)v, (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8));
        }
    }
}
