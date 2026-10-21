using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace HunterPie.Linux.Overlay.Views;

/// <summary>
/// The background of a damage row: a faint track with a coloured fill as wide as the member's share of the team's
/// damage. Drawn directly so a change only repaints this control and never triggers layout.
/// </summary>
public sealed class ShareBar : Control
{
    public static readonly StyledProperty<double> PercentProperty =
        AvaloniaProperty.Register<ShareBar, double>(nameof(Percent));

    public static readonly StyledProperty<int> SlotProperty =
        AvaloniaProperty.Register<ShareBar, int>(nameof(Slot));

    public static readonly StyledProperty<bool> IsSelfProperty =
        AvaloniaProperty.Register<ShareBar, bool>(nameof(IsSelf));

    private static readonly IBrush Track = new SolidColorBrush(Color.Parse("#22FFFFFF"));
    private static readonly Pen SelfOutline = new(new SolidColorBrush(Color.Parse("#99FFFFFF")), 1);

    private const double Radius = 4;

    static ShareBar()
    {
        AffectsRender<ShareBar>(PercentProperty, SlotProperty, IsSelfProperty);
    }

    public double Percent
    {
        get => GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    public int Slot
    {
        get => GetValue(SlotProperty);
        set => SetValue(SlotProperty, value);
    }

    public bool IsSelf
    {
        get => GetValue(IsSelfProperty);
        set => SetValue(IsSelfProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var area = new Rect(Bounds.Size);

        context.DrawRectangle(Track, IsSelf ? SelfOutline : null, new RoundedRect(area, Radius));

        double percent = Math.Clamp(Percent, 0, 100);

        if (percent <= 0)
            return;

        IBrush fill = SlotPalette.Fill(Slot);
        double width = Math.Max(area.Width * percent / 100, Radius * 2);

        context.DrawRectangle(fill, null, new RoundedRect(new Rect(0, 0, width, area.Height), Radius));
    }
}
