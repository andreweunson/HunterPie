using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace HunterPie.Linux.Overlay.Views;

/// <summary>
/// A health bar drawn directly (track + fill) so its width tracks a 0-100 value without templates or converters.
/// Green above 50%, amber from 25%, red below.
/// </summary>
public sealed class HealthBar : Control
{
    public static readonly StyledProperty<double> PercentProperty =
        AvaloniaProperty.Register<HealthBar, double>(nameof(Percent));

    private static readonly IBrush Track = new SolidColorBrush(Color.Parse("#33FFFFFF"));
    private static readonly IBrush High = new SolidColorBrush(Color.Parse("#4CC38A"));
    private static readonly IBrush Medium = new SolidColorBrush(Color.Parse("#E8B04A"));
    private static readonly IBrush Low = new SolidColorBrush(Color.Parse("#E5564F"));

    private const double Radius = 3;

    static HealthBar()
    {
        AffectsRender<HealthBar>(PercentProperty);
    }

    public double Percent
    {
        get => GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var area = new Rect(Bounds.Size);

        context.DrawRectangle(Track, null, new RoundedRect(area, Radius));

        double percent = Math.Clamp(Percent, 0, 100);

        if (percent <= 0)
            return;

        IBrush fill = percent switch
        {
            < 25 => Low,
            < 50 => Medium,
            _ => High
        };

        // Keep the rounded fill at least as wide as its corners so tiny amounts of health stay visible
        double width = Math.Max(area.Width * percent / 100, Radius * 2);
        context.DrawRectangle(fill, null, new RoundedRect(new Rect(0, 0, width, area.Height), Radius));
    }
}
