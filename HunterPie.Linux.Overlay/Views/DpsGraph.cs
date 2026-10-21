using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using HunterPie.Linux.Overlay.ViewModels;

namespace HunterPie.Linux.Overlay.Views;

/// <summary>
/// One DPS line per member, read straight from the rows' history arrays. It only repaints when <see cref="Version"/>
/// changes, which the view model bumps once per sample (about once a second), so its cost is a few hundred line
/// segments per second regardless of the refresh rate of the rest of the overlay.
/// </summary>
public sealed class DpsGraph : Control
{
    public static readonly StyledProperty<IReadOnlyList<PlayerDamageViewModel>?> RowsProperty =
        AvaloniaProperty.Register<DpsGraph, IReadOnlyList<PlayerDamageViewModel>?>(nameof(Rows));

    public static readonly StyledProperty<int> CountProperty =
        AvaloniaProperty.Register<DpsGraph, int>(nameof(Count));

    public static readonly StyledProperty<double> MaxProperty =
        AvaloniaProperty.Register<DpsGraph, double>(nameof(Max), 10);

    public static readonly StyledProperty<int> VersionProperty =
        AvaloniaProperty.Register<DpsGraph, int>(nameof(Version));

    private static readonly IBrush Track = new SolidColorBrush(Color.Parse("#14FFFFFF"));
    private static readonly Pen Grid = new(new SolidColorBrush(Color.Parse("#22FFFFFF")), 1);
    private static readonly Pen[] Lines = Enumerable.Range(0, 5).Select(slot => new Pen(SlotPalette.Line(slot), 1.5, lineJoin: PenLineJoin.Round)).ToArray();
    private static readonly Pen[] SelfLines = Enumerable.Range(0, 5).Select(slot => new Pen(SlotPalette.Line(slot), 2.4, lineJoin: PenLineJoin.Round)).ToArray();

    static DpsGraph()
    {
        AffectsRender<DpsGraph>(RowsProperty, CountProperty, MaxProperty, VersionProperty);
    }

    public IReadOnlyList<PlayerDamageViewModel>? Rows
    {
        get => GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    /// <summary>How many slots of each history are valid</summary>
    public int Count
    {
        get => GetValue(CountProperty);
        set => SetValue(CountProperty, value);
    }

    /// <summary>DPS at the top edge</summary>
    public double Max
    {
        get => GetValue(MaxProperty);
        set => SetValue(MaxProperty, value);
    }

    public int Version
    {
        get => GetValue(VersionProperty);
        set => SetValue(VersionProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var area = new Rect(Bounds.Size);
        context.DrawRectangle(Track, null, new RoundedRect(area, 4));
        context.DrawLine(Grid, new Point(0, area.Height / 2), new Point(area.Width, area.Height / 2));

        if (Rows is not { Count: > 0 } rows || Count < 2 || Max <= 0)
            return;

        // Leaders are drawn last so their lines are on top; the local player's line is thicker and goes over everything
        for (int i = rows.Count - 1; i >= 0; i--)
            if (!rows[i].IsSelf)
                DrawLine(context, area, rows[i], Lines);

        for (int i = 0; i < rows.Count; i++)
            if (rows[i].IsSelf)
                DrawLine(context, area, rows[i], SelfLines);
    }

    private void DrawLine(DrawingContext context, Rect area, PlayerDamageViewModel row, Pen[] pens)
    {
        float[] history = row.History;
        int count = Math.Min(Count, history.Length);
        double xStep = area.Width / (count - 1);
        double top = 2;
        double height = area.Height - top * 2;
        var geometry = new StreamGeometry();
        bool open = false;

        using (StreamGeometryContext path = geometry.Open())
        {
            for (int t = 0; t < count; t++)
            {
                float value = history[t];

                // NaN marks the time before this member joined
                if (float.IsNaN(value))
                    continue;

                var point = new Point(t * xStep, top + height - Math.Clamp(value / Max, 0, 1) * height);

                if (!open)
                {
                    path.BeginFigure(point, false);
                    open = true;
                }
                else
                    path.LineTo(point);
            }

            if (open)
                path.EndFigure(false);
        }

        if (open)
            context.DrawGeometry(null, pens[Math.Clamp(row.Slot, 0, pens.Length - 1)], geometry);
    }
}
