using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MediumMetrics.Models;

namespace MediumMetrics.Views;

/// <summary>
/// A dependency-free per-day earnings line chart: plots each day's amount earned (the delta)
/// and, on hover, snaps to the nearest day with a crosshair plus a readout of that day's
/// earned amount and the cumulative total. Shared by the Reports "Earnings" tab and the
/// per-story Payout History chart so both look and behave identically. Feed it via
/// <see cref="SetData"/>.
/// </summary>
public partial class EarningsChart : UserControl
{
    private IReadOnlyList<DailyEarning> _daily = System.Array.Empty<DailyEarning>();

    // Hover state: screen position of each plotted day, the plot's vertical extent (for the
    // crosshair), and the overlay elements we add/remove on mouse move so we don't redraw the
    // whole chart each time.
    private readonly List<(double X, double Y, DailyEarning D)> _points = new();
    private double _plotTop, _plotBottom;
    private readonly List<UIElement> _hoverArtifacts = new();

    public EarningsChart()
    {
        InitializeComponent();
    }

    /// <summary>Sets the day series to plot and redraws. Pass an empty list to clear.</summary>
    public void SetData(IReadOnlyList<DailyEarning> daily)
    {
        _daily = daily ?? System.Array.Empty<DailyEarning>();
        DrawChart();
    }

    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();

    /// <summary>Draws the per-day earnings deltas as a simple line chart (no charting dependency).</summary>
    private void DrawChart()
    {
        var canvas = PlotCanvas;
        canvas.Children.Clear();
        _points.Clear();
        _hoverArtifacts.Clear();
        double w = canvas.ActualWidth, h = canvas.ActualHeight;
        if (w < 40 || h < 40) return;

        var gridBrush = new SolidColorBrush(Color.FromRgb(0xEC, 0xEC, 0xEC));
        var textBrush = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        var lineBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));

        if (_daily.Count == 0)
        {
            AddLabel("No earnings history yet.", w / 2, h / 2, textBrush, 12, TextAlignment.Center);
            return;
        }

        const double mL = 56, mR = 16, mT = 14, mB = 30;
        double pw = w - mL - mR, ph = h - mT - mB;
        if (pw < 10 || ph < 10) return;

        double max = (double)_daily.Max(d => d.Delta);
        if (max <= 0) max = 1; // flat/zero series — keep a sane axis

        int n = _daily.Count;
        double X(int i) => n == 1 ? mL + pw / 2 : mL + i / (double)(n - 1) * pw;
        double Y(decimal delta) => mT + ph - Math.Clamp((double)delta / max, 0, 1) * ph;
        _plotTop = mT;
        _plotBottom = mT + ph;

        // Y gridlines + $ labels (0 .. max in quarters).
        for (int i = 0; i <= 4; i++)
        {
            double frac = i / 4.0;
            double y = mT + ph - frac * ph;
            canvas.Children.Add(new Line { X1 = mL, X2 = mL + pw, Y1 = y, Y2 = y, Stroke = gridBrush, StrokeThickness = 1 });
            AddLabel((max * frac).ToString("C2", CultureInfo.CurrentCulture), mL - 6, y - 8, textBrush, 10, TextAlignment.Right);
        }

        // The line itself.
        var poly = new Polyline { Stroke = lineBrush, StrokeThickness = 2 };
        for (int i = 0; i < n; i++) poly.Points.Add(new Point(X(i), Y(_daily[i].Delta)));
        canvas.Children.Add(poly);

        // Dots (with tooltips) and a handful of date labels along the bottom.
        int labelStep = Math.Max(1, (int)Math.Ceiling(n / 6.0));
        for (int i = 0; i < n; i++)
        {
            var d = _daily[i];
            double x = X(i), y = Y(d.Delta);
            _points.Add((x, y, d));
            var dot = new Ellipse
            {
                Width = 6, Height = 6, Fill = lineBrush,
                ToolTip = $"{d.Day:yyyy-MM-dd}: +{d.Delta:C2}  (total {d.Total:C2})",
            };
            Canvas.SetLeft(dot, x - 3);
            Canvas.SetTop(dot, y - 3);
            canvas.Children.Add(dot);

            if (i == 0 || i == n - 1 || i % labelStep == 0)
                AddLabel(d.Day.ToString("MM-dd"), x, mT + ph + 4, textBrush, 10, TextAlignment.Center);
        }
    }

    /// <summary>
    /// On mouse move, snaps to the nearest plotted day and shows a crosshair plus a readout
    /// box with that day's date, what was earned that day, and the cumulative total to date.
    /// </summary>
    private void OnCanvasMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_points.Count == 0) return;

        double mx = e.GetPosition(PlotCanvas).X;

        // Nearest day by horizontal distance.
        int best = 0;
        double bestDist = double.MaxValue;
        for (int i = 0; i < _points.Count; i++)
        {
            double dist = Math.Abs(_points[i].X - mx);
            if (dist < bestDist) { bestDist = dist; best = i; }
        }

        var (px, py, d) = _points[best];
        bool isLatest = best == _points.Count - 1;
        ShowHover(px, py, d, isLatest);
    }

    private void OnCanvasMouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => ClearHover();

    private void ClearHover()
    {
        foreach (var el in _hoverArtifacts) PlotCanvas.Children.Remove(el);
        _hoverArtifacts.Clear();
    }

    /// <summary>Draws the crosshair, emphasized marker, and floating readout for one day.</summary>
    private void ShowHover(double px, double py, DailyEarning d, bool isLatest)
    {
        ClearHover();

        var accent = new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
        var crossBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x2E, 0x7D, 0x32));

        // Vertical crosshair through the plot.
        var cross = new Line
        {
            X1 = px, X2 = px, Y1 = _plotTop, Y2 = _plotBottom,
            Stroke = crossBrush, StrokeThickness = 1,
        };
        PlotCanvas.Children.Add(cross);
        _hoverArtifacts.Add(cross);

        // Emphasized marker on the point.
        var ring = new Ellipse
        {
            Width = 11, Height = 11, Stroke = accent, StrokeThickness = 2,
            Fill = new SolidColorBrush(Colors.White),
        };
        Canvas.SetLeft(ring, px - 5.5);
        Canvas.SetTop(ring, py - 5.5);
        PlotCanvas.Children.Add(ring);
        _hoverArtifacts.Add(ring);

        // Readout box.
        string dayLabel = isLatest ? $"Latest · {d.Day:yyyy-MM-dd}" : d.Day.ToString("yyyy-MM-dd");
        var text = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
        };
        text.Inlines.Add(new System.Windows.Documents.Run(dayLabel) { FontWeight = FontWeights.SemiBold });
        text.Inlines.Add(new System.Windows.Documents.LineBreak());
        text.Inlines.Add(new System.Windows.Documents.Run($"Earned: +{d.Delta:C2}"));
        text.Inlines.Add(new System.Windows.Documents.LineBreak());
        text.Inlines.Add(new System.Windows.Documents.Run($"Cumulative: {d.Total:C2}") { FontWeight = FontWeights.SemiBold });

        var box = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)),
            BorderBrush = accent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 4, 6, 4),
            Child = text,
        };
        box.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double bw = box.DesiredSize.Width, bh = box.DesiredSize.Height;

        // Anchor near the point, flipping to stay inside the canvas.
        double left = px + 10;
        if (left + bw > PlotCanvas.ActualWidth) left = px - 10 - bw;
        if (left < 0) left = 0;
        double top = py - bh - 8;
        if (top < 0) top = py + 8;

        Canvas.SetLeft(box, left);
        Canvas.SetTop(box, top);
        PlotCanvas.Children.Add(box);
        _hoverArtifacts.Add(box);
    }

    /// <summary>Adds a positioned text label to the chart canvas, aligned around (x, y).</summary>
    private void AddLabel(string text, double x, double y, Brush brush, double size, TextAlignment align)
    {
        var tb = new TextBlock { Text = text, Foreground = brush, FontSize = size };
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double left = align switch
        {
            TextAlignment.Center => x - tb.DesiredSize.Width / 2,
            TextAlignment.Right => x - tb.DesiredSize.Width,
            _ => x,
        };
        Canvas.SetLeft(tb, left);
        Canvas.SetTop(tb, y);
        PlotCanvas.Children.Add(tb);
    }
}
