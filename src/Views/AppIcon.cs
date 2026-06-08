using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MediumMetrics.Views;

/// <summary>
/// Builds the app's window icon at runtime — no .ico asset required. A green rounded tile
/// with a white rising line-chart glyph (echoing the earnings chart), used as the main
/// window's <see cref="Window.Icon"/> so the taskbar / Alt+Tab / title bar / minimized
/// thumbnail show a distinctive image instead of the default .NET icon.
/// </summary>
public static class AppIcon
{
    private static BitmapSource? _cached;

    /// <summary>A frozen 256×256 icon bitmap (Windows downscales it for the 16/32px slots). Cached.</summary>
    public static BitmapSource Get() => _cached ??= Render();

    private static BitmapSource Render()
    {
        const int size = 256;
        var green = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // Rounded tile background.
            dc.DrawRoundedRectangle(green, null, new Rect(12, 12, size - 24, size - 24), 44, 44);

            // Rising, spiky line-chart glyph in white (like the earnings delta chart).
            var pen = new Pen(Brushes.White, 20)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
            var pts = new[]
            {
                new Point(58, 178),
                new Point(106, 150),
                new Point(150, 176),
                new Point(198, 80),
            };
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(pts[0], false, false);
                g.PolyLineTo(pts[1..], true, true);
            }
            geo.Freeze();
            dc.DrawGeometry(null, pen, geo);

            // A dot at the peak.
            dc.DrawEllipse(Brushes.White, null, pts[^1], 16, 16);
        }

        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    /// <summary>
    /// A full-bleed branded card (green background + white rising-chart glyph + "Medium
    /// Metrics" label) at the requested size, for the DWM taskbar thumbnail / Aero-Peek
    /// preview. Fully opaque, so alpha handling is irrelevant when handed to GDI/DWM.
    /// </summary>
    public static BitmapSource RenderThumbnail(int width, int height)
    {
        width = Math.Clamp(width, 1, 1024);
        height = Math.Clamp(height, 1, 1024);
        var green = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(green, null, new Rect(0, 0, width, height));

            double g = Math.Min(width, height);
            double cx = width / 2.0, cy = height / 2.0;
            double box = g * 0.46;
            double left = cx - box / 2, top = cy - box / 2 - g * 0.10;
            Point P(double fx, double fy) => new(left + fx * box, top + fy * box);

            var pen = new Pen(Brushes.White, Math.Max(2, g * 0.07))
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
            var pts = new[] { P(0.05, 0.72), P(0.37, 0.50), P(0.62, 0.70), P(0.95, 0.10) };
            var geo = new StreamGeometry();
            using (var gc = geo.Open())
            {
                gc.BeginFigure(pts[0], false, false);
                gc.PolyLineTo(pts[1..], true, true);
            }
            geo.Freeze();
            dc.DrawGeometry(null, pen, geo);
            dc.DrawEllipse(Brushes.White, null, pts[^1], g * 0.05, g * 0.05);

            double fontSize = g * 0.12;
            if (fontSize >= 8 && height > g * 0.7)
            {
                var ft = new FormattedText("Medium Metrics", CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"),
                    fontSize, Brushes.White, 1.0);
                dc.DrawText(ft, new Point(cx - ft.Width / 2, top + box + g * 0.05));
            }
        }

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }
}
