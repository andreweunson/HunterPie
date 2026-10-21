using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using HunterPie.Core.Game.Enums;

namespace HunterPie.Linux.Overlay.Views;

/// <summary>
/// Draws a weapon icon from Assets/WeaponIcons.axaml. An <c>Image</c> cannot be used: Avalonia sizes these drawings
/// from their raw path coordinates, ignoring the transforms inside them, so they come out about 1000 units tall. The
/// drawings' clip rectangle holds the real artwork size, so the control scales the drawing itself from that size
/// (DrawImage with a source rectangle draws nothing for these drawings).
/// Each drawing is looked up once and reused for every row and every redraw.
/// </summary>
public sealed class WeaponIcon : Control
{
    public static readonly StyledProperty<Weapon> WeaponProperty =
        AvaloniaProperty.Register<WeaponIcon, Weapon>(nameof(Weapon), Weapon.None);

    private static readonly Dictionary<Weapon, (Drawing Drawing, Rect Source)?> Cache = new();

    static WeaponIcon()
    {
        AffectsRender<WeaponIcon>(WeaponProperty);
    }

    public Weapon Weapon
    {
        get => GetValue(WeaponProperty);
        set => SetValue(WeaponProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Find(Weapon) is not { } icon || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        // Uniform fit, centred
        double scale = Math.Min(Bounds.Width / icon.Source.Width, Bounds.Height / icon.Source.Height);
        double width = icon.Source.Width * scale;
        double height = icon.Source.Height * scale;
        var destination = new Rect((Bounds.Width - width) / 2, (Bounds.Height - height) / 2, width, height);

        using (context.PushClip(destination))
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(destination.X, destination.Y)))
            icon.Drawing.Draw(context);
    }

    private static (Drawing Drawing, Rect Source)? Find(Weapon weapon)
    {
        if (Cache.TryGetValue(weapon, out var cached))
            return cached;

        (Drawing, Rect)? icon = null;

        if (Enum.IsDefined(weapon)
            && Application.Current is { } app
            && app.TryGetResource($"Weapon.{weapon}", app.ActualThemeVariant, out object? resource)
            && resource is DrawingImage { Drawing: DrawingGroup { ClipGeometry: { } clip } drawing })
            icon = (drawing, clip.Bounds);

        Cache[weapon] = icon;
        return icon;
    }
}
