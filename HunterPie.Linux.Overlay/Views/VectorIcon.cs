using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace HunterPie.Linux.Overlay.Views;

/// <summary>
/// Base for the weapon and element icons, which are vector drawings copied from the WPF client. An <c>Image</c> cannot
/// show them: Avalonia sizes these drawings from their raw path coordinates and ignores the transforms inside them
/// (a weapon came out about 1000 units tall), while DrawImage with a source rectangle draws nothing. The clip
/// rectangle of each drawing holds the real artwork size, so the drawing is scaled from that and drawn directly.
/// Every drawing is looked up once and then reused for all rows and all redraws.
/// </summary>
public abstract class VectorIcon : Control
{
    private static readonly Dictionary<string, (Drawing Drawing, Rect Source)?> Cache = new();

    /// <summary>Resource key in the merged icon dictionaries, or null for no icon</summary>
    protected abstract string? ResourceKey { get; }

    public override void Render(DrawingContext context)
    {
        if (ResourceKey is not { } key || Find(key) is not { } icon || Bounds.Width <= 0 || Bounds.Height <= 0)
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

    private static (Drawing Drawing, Rect Source)? Find(string key)
    {
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        (Drawing, Rect)? icon = null;

        if (Application.Current is { } app
            && app.TryGetResource(key, app.ActualThemeVariant, out object? resource)
            && resource is DrawingImage { Drawing: DrawingGroup { ClipGeometry: { } clip } drawing })
            icon = (drawing, clip.Bounds);

        Cache[key] = icon;
        return icon;
    }
}
