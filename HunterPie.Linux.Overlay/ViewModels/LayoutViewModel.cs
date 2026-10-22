using Avalonia.Threading;
using HunterPie.Linux.Overlay.Services;
using System.Runtime.CompilerServices;

namespace HunterPie.Linux.Overlay.ViewModels;

/// <summary>
/// The overlay's layout settings (monitor, stacking, per-widget visibility, scale and opacity) as bindable properties.
/// Changes are written to <see cref="OverlaySettings"/> and saved shortly after, so dragging a slider is not a file
/// write per tick. <see cref="LayoutChanged"/> tells the overlay window to re-apply them.
/// </summary>
public sealed class LayoutViewModel : ViewModelBase
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(400);

    private readonly OverlaySettings _settings;
    private readonly DispatcherTimer _saveTimer;
    private bool _dirty;

    internal LayoutViewModel(OverlaySettings settings)
    {
        _settings = settings;
        _saveTimer = new DispatcherTimer { Interval = SaveDelay };
        _saveTimer.Tick += (_, _) => Flush();
    }

    /// <summary>Raised with the name of the property that changed, or "Reset"</summary>
    public event EventHandler<string>? LayoutChanged;

    public bool QuestEnabled { get => _settings.Quest.Enabled; set => Change(_settings.Quest.Enabled, value, () => _settings.Quest.Enabled = value); }
    public double QuestScale { get => _settings.Quest.Scale; set => ChangeScale(_settings.Quest, value); }
    public double QuestOpacity { get => _settings.Quest.Opacity; set => ChangeOpacity(_settings.Quest, value); }

    public bool MonstersEnabled { get => _settings.Monsters.Enabled; set => Change(_settings.Monsters.Enabled, value, () => _settings.Monsters.Enabled = value); }
    public double MonstersScale { get => _settings.Monsters.Scale; set => ChangeScale(_settings.Monsters, value); }
    public double MonstersOpacity { get => _settings.Monsters.Opacity; set => ChangeOpacity(_settings.Monsters, value); }

    public bool DamageEnabled { get => _settings.Damage.Enabled; set => Change(_settings.Damage.Enabled, value, () => _settings.Damage.Enabled = value); }
    public double DamageScale { get => _settings.Damage.Scale; set => ChangeScale(_settings.Damage, value); }
    public double DamageOpacity { get => _settings.Damage.Opacity; set => ChangeOpacity(_settings.Damage, value); }

    public int Screen { get => _settings.Screen; set => Change(_settings.Screen, value, () => _settings.Screen = value); }

    public bool OverrideRedirect { get => _settings.OverrideRedirect; set => Change(_settings.OverrideRedirect, value, () => _settings.OverrideRedirect = value); }

    /// <summary>Puts every widget back at its default position, scale and opacity (visibility and options stay)</summary>
    public void ResetLayout()
    {
        var defaults = new OverlaySettings();

        Copy(defaults.Quest, _settings.Quest);
        Copy(defaults.Monsters, _settings.Monsters);
        Copy(defaults.Damage, _settings.Damage);

        ScheduleSave();
        Refresh();
        LayoutChanged?.Invoke(this, "Reset");
    }

    /// <summary>The overlay changed a placement itself (mouse wheel), so the settings window needs to catch up</summary>
    public void Refresh() => Raise(string.Empty);

    public void Flush()
    {
        _saveTimer.Stop();

        if (!_dirty)
            return;

        _dirty = false;
        _settings.Save();
    }

    private static void Copy(WidgetPlacement from, WidgetPlacement to)
    {
        to.X = from.X;
        to.Y = from.Y;
        to.Scale = from.Scale;
        to.Opacity = from.Opacity;
    }

    private void ChangeScale(WidgetPlacement placement, double value, [CallerMemberName] string? name = null) =>
        Change(placement.Scale, Math.Clamp(Math.Round(value, 2), 0.4, 3.0), () => placement.Scale = Math.Clamp(Math.Round(value, 2), 0.4, 3.0), name);

    private void ChangeOpacity(WidgetPlacement placement, double value, [CallerMemberName] string? name = null) =>
        Change(placement.Opacity, Math.Clamp(Math.Round(value, 2), 0.1, 1.0), () => placement.Opacity = Math.Clamp(Math.Round(value, 2), 0.1, 1.0), name);

    private void Change<T>(T current, T value, Action apply, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
            return;

        apply();
        ScheduleSave();
        Raise(name);
        LayoutChanged?.Invoke(this, name ?? string.Empty);
    }

    private void ScheduleSave()
    {
        _dirty = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }
}
