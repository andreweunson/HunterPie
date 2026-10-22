using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace HunterPie.Linux.Overlay.Views;

/// <summary>A thin bar for the monster's stamina, drawn directly like <see cref="HealthBar"/></summary>
public sealed class StaminaBar : Control
{
    public static readonly StyledProperty<double> PercentProperty =
        AvaloniaProperty.Register<StaminaBar, double>(nameof(Percent));

    public static readonly StyledProperty<bool> IsExhaustedProperty =
        AvaloniaProperty.Register<StaminaBar, bool>(nameof(IsExhausted));

    private static readonly IBrush ExhaustedFill = new SolidColorBrush(Color.Parse("#74D4B1"));
    private static readonly IBrush Track = new SolidColorBrush(Color.Parse("#33FFFFFF"));
    private static readonly IBrush Fill = new SolidColorBrush(Color.Parse("#F5D06B"));

    private const double Radius = 2;

    static StaminaBar()
    {
        AffectsRender<StaminaBar>(PercentProperty, IsExhaustedProperty);
    }

    public double Percent
    {
        get => GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    /// <summary>The bar shows the exhaustion running out instead of the stamina gauge</summary>
    public bool IsExhausted
    {
        get => GetValue(IsExhaustedProperty);
        set => SetValue(IsExhaustedProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var area = new Rect(Bounds.Size);
        context.DrawRectangle(Track, null, new RoundedRect(area, Radius));

        double percent = Math.Clamp(Percent, 0, 100);

        if (percent > 0)
            context.DrawRectangle(IsExhausted ? ExhaustedFill : Fill, null, new RoundedRect(new Rect(0, 0, Math.Max(area.Width * percent / 100, Radius * 2), area.Height), Radius));
    }
}
