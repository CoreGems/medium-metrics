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
}
