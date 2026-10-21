using Avalonia.Media;

namespace HunterPie.Linux.Overlay.Views;

/// <summary>Colour per party slot, shared by the damage bars and the DPS graph so a member keeps one colour</summary>
internal static class SlotPalette
{
    // Players in party order, then support hunters
    private static readonly Color[] Colors =
    [
        Color.Parse("#5B9BFF"),
        Color.Parse("#E5564F"),
        Color.Parse("#E8B04A"),
        Color.Parse("#B48CFF"),
        Color.Parse("#7C8791"),
    ];

    private static readonly IBrush[] Solid = Colors.Select(it => (IBrush)new SolidColorBrush(it)).ToArray();
    private static readonly IBrush[] Translucent = Colors.Select(it => (IBrush)new SolidColorBrush(it, 0.6)).ToArray();

    public static IBrush Line(int slot) => Solid[Math.Clamp(slot, 0, Solid.Length - 1)];

    public static IBrush Fill(int slot) => Translucent[Math.Clamp(slot, 0, Translucent.Length - 1)];
}
