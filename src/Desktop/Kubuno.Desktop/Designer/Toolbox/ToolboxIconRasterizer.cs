using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Kubuno.VisualStudio.Designer.Toolbox
{
    /// <summary>
    /// Renders a Kubuno control icon (the <c>Viewbox</c> › <c>Canvas</c> of 24-unit Lucide shapes of
    /// <c>KubunoControls.imagemanifest</c>) as a <b>pixel-hinted 16x16 bitmap</b> for Visual Studio's Toolbox -
    /// drawn for the pixel grid like the Windows Forms toolbox bitmaps, not as a scaled-down vector.
    ///
    /// <para>Why: the legacy Toolbox API (<c>TBXITEMINFO</c>) only draws a 16x16 bitmap - found live that a
    /// 28x28/32x32/48x48 one gets a wider slot but is never painted, whatever <c>iImageWidth</c>/<c>iImageIndex</c> -
    /// and every designer (WinForms, XAML: <c>ToolboxUtilities.GetBitmapForType</c> scales its icons to 16x16)
    /// hands it 16x16 bitmaps. At 175 % Visual Studio upscales it (<c>DpiHelper</c>'s default
    /// <c>MixedNearestNeighborHighQualityBicubic</c>: nearest-neighbour to 200 %, then bicubic down), which keeps
    /// pixel art crisp but turns a vector drawn at 16 px with 1.33-1.5 px strokes into uneven, soft lines.</para>
    ///
    /// <para>Hinting rules: the Lucide content box (2..22 units) maps to the pixel centres 1.5..14.5, so the
    /// glyph fills a 14x14 box with a transparent 1-px margin; every stroke is exactly 1 px; every on-curve
    /// point is snapped to the centre of its pixel (a Bézier's control points move with their end point, arcs
    /// keep their scaled radius); ellipses and rectangles snap their four edges, so circles are symmetric
    /// pixel circles; square caps and mitred joins end lines on pixel edges; a shape smaller than 3 px (a
    /// Lucide dot) becomes a solid 1x1/2x2 pixel block.</para>
    /// </summary>
    public static class ToolboxIconRasterizer
    {
        /// <summary>The bitmap size the Toolbox draws.</summary>
        public const int Size = 16;

        /// <summary>Lucide's canvas is 24 units; its content box is 2..22.</summary>
        private const double ContentMin = 2.0;

        /// <summary>Pixels per Lucide unit: 20 units (2..22) span the 13 px between the centres 1.5 and 14.5.</summary>
        private const double Scale = 13.0 / 20.0;

        private const double FirstCentre = 1.5;

        /// <summary>A Lucide coordinate in pixels, unsnapped.</summary>
        public static double Map(double units) => FirstCentre + ((units - ContentMin) * Scale);

        /// <summary>The centre of the pixel containing <paramref name="pixels"/>.</summary>
        public static double Snap(double pixels) => Math.Floor(pixels) + 0.5;

        /// <summary>Renders <paramref name="icon"/> (a Viewbox or Canvas of shapes) as premultiplied BGRA, <see cref="Size"/>x<see cref="Size"/>.</summary>
        public static byte[] Render(FrameworkElement icon)
        {
            if (icon is null)
            {
                throw new ArgumentNullException(nameof(icon));
            }

            var canvas = icon as Canvas ?? (icon as Viewbox)?.Child as Canvas
                ?? throw new ArgumentException("An icon is a Viewbox holding a Canvas of shapes.", nameof(icon));

            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                // The 1-px margin is kept even for a shape that bulges past Lucide's content box (Badge).
                context.PushClip(new RectangleGeometry(new Rect(1, 1, Size - 2, Size - 2)));
                foreach (var child in canvas.Children)
                {
                    if (child is Shape shape && shape.Stroke is Brush brush)
                    {
                        DrawShape(context, shape, brush);
                    }
                }

                context.Pop();
            }

            var target = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);
            var pixels = new byte[Size * Size * 4];
            target.CopyPixels(pixels, Size * 4, 0);
            return pixels;
        }

        private static Pen PenFor(Brush brush)
        {
            var pen = new Pen(brush, 1.0)
            {
                StartLineCap = PenLineCap.Square,
                EndLineCap = PenLineCap.Square,
                LineJoin = PenLineJoin.Miter,
                MiterLimit = 2.0,
            };
            pen.Freeze();
            return pen;
        }

        private static void DrawShape(DrawingContext context, Shape shape, Brush brush)
        {
            var pen = PenFor(brush);
            switch (shape)
            {
                case Ellipse ellipse:
                    DrawEllipse(context, BoxOf(ellipse), brush, pen);
                    break;
                case Rectangle rectangle:
                    DrawRectangle(context, BoxOf(rectangle), rectangle.RadiusX, brush, pen);
                    break;
                case Polyline polyline:
                    DrawPath(context, PolylineGeometry(polyline), brush, pen);
                    break;
                case Line line:
                    DrawPath(context, new LineGeometry(new Point(line.X1, line.Y1), new Point(line.X2, line.Y2)), brush, pen);
                    break;
                case Path path when path.Data is not null:
                    DrawPath(context, path.Data, brush, pen);
                    break;
            }
        }

        private static Rect BoxOf(Shape shape)
        {
            var left = Canvas.GetLeft(shape);
            var top = Canvas.GetTop(shape);
            return new Rect(double.IsNaN(left) ? 0 : left, double.IsNaN(top) ? 0 : top, shape.Width, shape.Height);
        }

        /// <summary>A shape box in pixels with each edge on a pixel centre (so a 1-px stroke covers whole pixels).</summary>
        private static Rect SnapBox(Rect units)
        {
            var left = Snap(Map(units.Left));
            var top = Snap(Map(units.Top));
            var right = Math.Max(left, Snap(Map(units.Right)));
            var bottom = Math.Max(top, Snap(Map(units.Bottom)));
            return new Rect(left, top, right - left, bottom - top);
        }

        /// <summary>A Lucide dot (a shape whose painted extent, 2-unit stroke included, is under 3 px): a solid pixel block.</summary>
        private static bool IsDot(Rect units) => ((Math.Max(units.Width, units.Height) + 2.0) * Scale) < 3.0;

        private static void DrawDot(DrawingContext context, Point centreInUnits, Rect units, Brush brush)
        {
            var x = Map(centreInUnits.X);
            var y = Map(centreInUnits.Y);
            var size = DotSize(x, y, (int)Math.Round((Math.Max(units.Width, units.Height) + 2.0) * Scale));
            context.DrawRectangle(brush, null, new Rect(DotStart(x, size), DotStart(y, size), size, size));
        }

        /// <summary>
        /// A square dot of at least <paramref name="size"/> pixels that can be centred on <paramref name="x"/>,
        /// <paramref name="y"/>: an even size when either centre is on a pixel boundary (a 1-px dot there would be
        /// off-centre), else odd - rounded down.
        /// </summary>
        public static int DotSize(double x, double y, int size)
        {
            size = Math.Max(1, size);
            var onBoundary = IsOnBoundary(x) || IsOnBoundary(y);
            return onBoundary ? Math.Max(2, size - (size % 2)) : (size % 2 == 1 ? size : size - 1);
        }

        /// <summary>The first pixel of a <paramref name="size"/>-pixel run centred as closely as possible on <paramref name="centre"/>.</summary>
        public static double DotStart(double centre, int size) => Math.Round(centre - (size / 2.0));

        private static bool IsOnBoundary(double c) => Math.Abs(c - Math.Round(c)) < 0.25;
        private static void DrawEllipse(DrawingContext context, Rect units, Brush brush, Pen pen)
        {
            if (IsDot(units))
            {
                DrawDot(context, new Point(units.Left + (units.Width / 2), units.Top + (units.Height / 2)), units, brush);
                return;
            }

            var box = SnapBox(units);
            var centre = new Point(box.Left + (box.Width / 2), box.Top + (box.Height / 2));
            if (Math.Abs(box.Width - box.Height) < 0.01 && brush is SolidColorBrush solid)
            {
                // A circle is rasterized analytically (8x8 samples per pixel, symmetric positions): WPF's
                // Bézier-approximated ellipse is visibly lopsided at 16 px.
                context.DrawImage(CircleRing(centre, box.Width / 2, solid.Color), new Rect(0, 0, Size, Size));
                return;
            }

            context.DrawEllipse(null, pen, centre, box.Width / 2, box.Height / 2);
        }

        /// <summary>A 1-px ring of radius <paramref name="radius"/> (stroke centre) as a <see cref="Size"/>x<see cref="Size"/> image.</summary>
        private static BitmapSource CircleRing(Point centre, double radius, Color color)
        {
            const int samples = 8;
            var pixels = new byte[Size * Size * 4];
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var hits = 0;
                    for (var sy = 0; sy < samples; sy++)
                    {
                        for (var sx = 0; sx < samples; sx++)
                        {
                            var px = x + ((sx + 0.5) / samples) - centre.X;
                            var py = y + ((sy + 0.5) / samples) - centre.Y;
                            if (Math.Abs(Math.Sqrt((px * px) + (py * py)) - radius) <= 0.5)
                            {
                                hits++;
                            }
                        }
                    }

                    var alpha = hits * 255 / (samples * samples);
                    var o = ((y * Size) + x) * 4;
                    pixels[o] = (byte)(color.B * alpha / 255);
                    pixels[o + 1] = (byte)(color.G * alpha / 255);
                    pixels[o + 2] = (byte)(color.R * alpha / 255);
                    pixels[o + 3] = (byte)alpha;
                }
            }

            var image = BitmapSource.Create(Size, Size, 96, 96, PixelFormats.Pbgra32, null, pixels, Size * 4);
            image.Freeze();
            return image;
        }

        private static void DrawRectangle(DrawingContext context, Rect units, double radiusUnits, Brush brush, Pen pen)
        {
            if (IsDot(units))
            {
                DrawDot(context, new Point(units.Left + (units.Width / 2), units.Top + (units.Height / 2)), units, brush);
                return;
            }

            var box = SnapBox(units);
            // A small rounded corner of a 1-px outline becomes one pixel cut off the corner; a large one (a
            // pill, e.g. Switch) keeps its scaled radius.
            var scaled = radiusUnits * Scale;
            var radius = radiusUnits <= 0 ? 0 : scaled < 2.0 ? 1.5 : Math.Min(scaled, Math.Min(box.Width, box.Height) / 2);
            context.DrawRoundedRectangle(null, pen, box, radius, radius);
        }

        private static Geometry PolylineGeometry(Polyline polyline)
        {
            var figure = new PathFigure { IsClosed = false, IsFilled = false };
            if (polyline.Points.Count > 0)
            {
                figure.StartPoint = polyline.Points[0];
                for (var i = 1; i < polyline.Points.Count; i++)
                {
                    figure.Segments.Add(new LineSegment(polyline.Points[i], true));
                }
            }

            return new PathGeometry(new[] { figure });
        }

        private static void DrawPath(DrawingContext context, Geometry geometry, Brush brush, Pen pen)
        {
            var source = PathGeometry.CreateFromGeometry(geometry);
            var hinted = new PathGeometry();
            foreach (var figure in source.Figures)
            {
                var bounds = new PathGeometry(new[] { figure }).Bounds;
                // Only a true dot (Lucide's "h.01"): a short dash stays a line.
                if (!bounds.IsEmpty && Math.Max(bounds.Width, bounds.Height) < 1.0)
                {
                    DrawDot(context, new Point(bounds.Left + (bounds.Width / 2), bounds.Top + (bounds.Height / 2)), bounds, brush);
                    continue;
                }

                hinted.Figures.Add(HintFigure(figure));
            }

            if (hinted.Figures.Count > 0)
            {
                context.DrawGeometry(null, pen, hinted);
            }
        }

        /// <summary>A mapped point and how far snapping moved it (control points of a Bézier move by their end point's delta).</summary>
        private static (Point Snapped, Vector Delta) HintPoint(Point units)
        {
            var mapped = new Point(Map(units.X), Map(units.Y));
            var snapped = new Point(Snap(mapped.X), Snap(mapped.Y));
            return (snapped, snapped - mapped);
        }

        private static Point MapPoint(Point units) => new Point(Map(units.X), Map(units.Y));

        private static PathFigure HintFigure(PathFigure figure)
        {
            var (start, startDelta) = HintPoint(figure.StartPoint);
            var result = new PathFigure { StartPoint = start, IsClosed = figure.IsClosed, IsFilled = false };
            var previousDelta = startDelta;
            foreach (var segment in figure.Segments)
            {
                switch (segment)
                {
                    case LineSegment line:
                    {
                        var (p, d) = HintPoint(line.Point);
                        result.Segments.Add(new LineSegment(p, true));
                        previousDelta = d;
                        break;
                    }

                    case PolyLineSegment poly:
                    {
                        var points = new PointCollection();
                        foreach (var point in poly.Points)
                        {
                            var (p, d) = HintPoint(point);
                            points.Add(p);
                            previousDelta = d;
                        }

                        result.Segments.Add(new PolyLineSegment(points, true));
                        break;
                    }

                    case BezierSegment bezier:
                    {
                        var (p3, d3) = HintPoint(bezier.Point3);
                        result.Segments.Add(new BezierSegment(MapPoint(bezier.Point1) + previousDelta, MapPoint(bezier.Point2) + d3, p3, true));
                        previousDelta = d3;
                        break;
                    }

                    case PolyBezierSegment polyBezier:
                    {
                        var points = new PointCollection();
                        for (var i = 0; i + 2 < polyBezier.Points.Count; i += 3)
                        {
                            var (p3, d3) = HintPoint(polyBezier.Points[i + 2]);
                            points.Add(MapPoint(polyBezier.Points[i]) + previousDelta);
                            points.Add(MapPoint(polyBezier.Points[i + 1]) + d3);
                            points.Add(p3);
                            previousDelta = d3;
                        }

                        result.Segments.Add(new PolyBezierSegment(points, true));
                        break;
                    }

                    case QuadraticBezierSegment quadratic:
                    {
                        var (p2, d2) = HintPoint(quadratic.Point2);
                        result.Segments.Add(new QuadraticBezierSegment(MapPoint(quadratic.Point1) + ((previousDelta + d2) / 2), p2, true));
                        previousDelta = d2;
                        break;
                    }

                    case PolyQuadraticBezierSegment polyQuadratic:
                    {
                        var points = new PointCollection();
                        for (var i = 0; i + 1 < polyQuadratic.Points.Count; i += 2)
                        {
                            var (p2, d2) = HintPoint(polyQuadratic.Points[i + 1]);
                            points.Add(MapPoint(polyQuadratic.Points[i]) + ((previousDelta + d2) / 2));
                            points.Add(p2);
                            previousDelta = d2;
                        }

                        result.Segments.Add(new PolyQuadraticBezierSegment(points, true));
                        break;
                    }

                    case ArcSegment arc:
                    {
                        var (p, d) = HintPoint(arc.Point);
                        var size = new Size(arc.Size.Width * Scale, arc.Size.Height * Scale);
                        result.Segments.Add(new ArcSegment(p, size, arc.RotationAngle, arc.IsLargeArc, arc.SweepDirection, true));
                        previousDelta = d;
                        break;
                    }
                }
            }

            return result;
        }

        /// <summary>The pixels of <paramref name="premultipliedBgra"/> with any coverage (tests / previews).</summary>
        public static IEnumerable<(int X, int Y, byte Alpha)> CoveredPixels(byte[] premultipliedBgra)
        {
            for (var i = 0; i < Size * Size; i++)
            {
                var alpha = premultipliedBgra[(i * 4) + 3];
                if (alpha > 0)
                {
                    yield return (i % Size, i / Size, alpha);
                }
            }
        }
    }
}
