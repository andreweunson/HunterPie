using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace HunterPie.Linux.Overlay.Views;

/// <summary>
/// Two concentric arcs, drawn directly: the outer one is the ailment's remaining time in its own colour, the inner
/// one is the build-up in blue. Redraws only when one of the properties changes.
/// </summary>
public sealed class AilmentRing : Control
{
    public static readonly StyledProperty<double> TimerFractionProperty =
        AvaloniaProperty.Register<AilmentRing, double>(nameof(TimerFraction));

    public static readonly StyledProperty<double> BuildUpFractionProperty =
        AvaloniaProperty.Register<AilmentRing, double>(nameof(BuildUpFraction));

    public static readonly StyledProperty<IBrush?> TimerBrushProperty =
        AvaloniaProperty.Register<AilmentRing, IBrush?>(nameof(TimerBrush));

    private const double Stroke = 3;
    private static readonly IBrush Track = new ImmutableSolidColorBrush(Color.Parse("#33FFFFFF"));
    private static readonly IBrush BuildUpBrush = new ImmutableSolidColorBrush(Color.Parse("#5B9BFF"));
    private static readonly IPen TrackPen = new Pen(Track, Stroke);

    static AilmentRing()
    {
        AffectsRender<AilmentRing>(TimerFractionProperty, BuildUpFractionProperty, TimerBrushProperty);
    }

    public double TimerFraction { get => GetValue(TimerFractionProperty); set => SetValue(TimerFractionProperty, value); }

    public double BuildUpFraction { get => GetValue(BuildUpFractionProperty); set => SetValue(BuildUpFractionProperty, value); }

    public IBrush? TimerBrush { get => GetValue(TimerBrushProperty); set => SetValue(TimerBrushProperty, value); }

    public override void Render(DrawingContext context)
    {
        Point center = new(Bounds.Width / 2, Bounds.Height / 2);
        double outer = Math.Min(Bounds.Width, Bounds.Height) / 2 - Stroke / 2;
        double inner = outer - Stroke - 2;

        context.DrawEllipse(null, TrackPen, center, outer, outer);
        context.DrawEllipse(null, TrackPen, center, inner, inner);

        DrawArc(context, center, inner, BuildUpFraction, BuildUpBrush);
        DrawArc(context, center, outer, TimerFraction, TimerBrush ?? BuildUpBrush);
    }

    private static void DrawArc(DrawingContext context, Point center, double radius, double fraction, IBrush brush)
    {
        fraction = Math.Clamp(fraction, 0, 1);

        if (fraction <= 0.001 || radius <= 0)
            return;

        var pen = new Pen(brush, Stroke, lineCap: PenLineCap.Round);

        if (fraction >= 0.999)
        {
            context.DrawEllipse(null, pen, center, radius, radius);
            return;
        }

        // Clockwise from 12 o'clock
        double end = fraction * 2 * Math.PI;
        Point start = new(center.X, center.Y - radius);
        Point stop = new(center.X + radius * Math.Sin(end), center.Y - radius * Math.Cos(end));

        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(start, false);
            ctx.ArcTo(stop, new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }
}
