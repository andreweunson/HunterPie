using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace HunterPie.Linux.Overlay.Views;

/// <summary>
/// A health bar drawn directly (track + fill) so its width tracks a 0-100 value without templates or converters.
/// Green above 50%, amber from 25%, red below. When the monster can be captured, a marker sits on the bar at the health
/// percentage where that becomes possible: a notch with a small arrow above it, dim while the monster is still above
/// the threshold and bright once it is at or below.
/// </summary>
public sealed class HealthBar : Control
{
    public static readonly StyledProperty<double> PercentProperty =
        AvaloniaProperty.Register<HealthBar, double>(nameof(Percent));

    public static readonly StyledProperty<double> CaptureAtProperty =
        AvaloniaProperty.Register<HealthBar, double>(nameof(CaptureAt));

    public static readonly StyledProperty<bool> IsCapturableProperty =
        AvaloniaProperty.Register<HealthBar, bool>(nameof(IsCapturable));

    private static readonly IBrush MarkerDim = new SolidColorBrush(Color.Parse("#B0985A"));
    private static readonly IBrush MarkerBright = new SolidColorBrush(Color.Parse("#FFD54F"));
    private static readonly Pen MarkerOutline = new(new SolidColorBrush(Color.Parse("#CC000000")), 1);

    private static readonly IBrush Track = new SolidColorBrush(Color.Parse("#33FFFFFF"));
    private static readonly IBrush High = new SolidColorBrush(Color.Parse("#4CC38A"));
    private static readonly IBrush Medium = new SolidColorBrush(Color.Parse("#E8B04A"));
    private static readonly IBrush Low = new SolidColorBrush(Color.Parse("#E5564F"));

    private const double Radius = 3;

    static HealthBar()
    {
        AffectsRender<HealthBar>(PercentProperty, CaptureAtProperty, IsCapturableProperty);
    }

    public double Percent
    {
        get => GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    /// <summary>Health percentage (0-100) of the capture threshold, 0 for none</summary>
    public double CaptureAt
    {
        get => GetValue(CaptureAtProperty);
        set => SetValue(CaptureAtProperty, value);
    }

    public bool IsCapturable
    {
        get => GetValue(IsCapturableProperty);
        set => SetValue(IsCapturableProperty, value);
    }

    // Room above and below the bar for the capture marker, so it overhangs the bar instead of covering it
    private const double Overhang = 5;

    public override void Render(DrawingContext context)
    {
        var area = new Rect(0, Overhang, Bounds.Width, Math.Max(0, Bounds.Height - Overhang * 2));

        context.DrawRectangle(Track, null, new RoundedRect(area, Radius));

        double percent = Math.Clamp(Percent, 0, 100);

        if (percent > 0)
            DrawFill(context, area, percent);

        DrawCaptureMarker(context, area);
    }

    private void DrawFill(DrawingContext context, Rect area, double percent)
    {
        IBrush fill = percent switch
        {
            < 25 => Low,
            < 50 => Medium,
            _ => High
        };

        // Keep the rounded fill at least as wide as its corners so tiny amounts of health stay visible
        double width = Math.Max(area.Width * percent / 100, Radius * 2);
        context.DrawRectangle(fill, null, new RoundedRect(new Rect(0, area.Y, width, area.Height), Radius));
    }

    private void DrawCaptureMarker(DrawingContext context, Rect area)
    {
        if (CaptureAt <= 0)
            return;

        IBrush brush = IsCapturable ? MarkerBright : MarkerDim;
        double x = Math.Clamp(area.Width * CaptureAt / 100, 1, area.Width - 1);

        // Notch through the bar and a small downward arrow above it
        context.DrawRectangle(brush, MarkerOutline, new Rect(x - 1.5, area.Y - 1, 3, area.Height + 2));

        var arrow = new StreamGeometry();

        using (StreamGeometryContext path = arrow.Open())
        {
            path.BeginFigure(new Point(x - 5, 0), true);
            path.LineTo(new Point(x + 5, 0));
            path.LineTo(new Point(x, Overhang + 1.5));
            path.EndFigure(true);
        }

        context.DrawGeometry(brush, MarkerOutline, arrow);
    }
}
