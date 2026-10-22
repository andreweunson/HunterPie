using Avalonia.Media;
using HunterPie.Core.Game.Entity.Enemy;
using HunterPie.Linux.Overlay.Services;
using System.Diagnostics;

namespace HunterPie.Linux.Overlay.ViewModels;

/// <summary>
/// One ailment ring: the effect's timer (outer arc), the build-up towards triggering it (inner arc) and how many times
/// it has been inflicted. Like the Windows client it only appears while something about it is changing, and hides again
/// after <see cref="HideAfter"/> without a change. Polled from the UI timer; nothing is allocated unless a value moved.
/// </summary>
public sealed class AilmentViewModel : ViewModelBase
{
    private static readonly TimeSpan HideAfter = TimeSpan.FromSeconds(15);

    private float _timer = float.NaN;
    private float _maxTimer = float.NaN;
    private float _buildUp = float.NaN;
    private float _maxBuildUp = float.NaN;
    private int _count = -1;
    private int _lastWholeSeconds = -1;
    private long _lastChange;
    private int _lastLoggedStep = -1;

    private double _timerFraction;
    private double _buildUpFraction;
    private string _text = string.Empty;
    private bool _isTimerActive;
    private bool _isShown;

    public IMonsterAilment? Source { get; }

    public string Id { get; }

    /// <summary>
    /// The game's "exhaust" entries are being investigated: one of them behaves like a flinch gauge (it fills as the
    /// monster is hit, resets with a higher maximum when it flinches). Only these are logged with --verbose.
    /// </summary>
    private bool IsLogged => Id == "AILMENT_EXHAUST";

    public string Name { get; }

    public IBrush Brush { get; }

    /// <summary>0 to 1, how much of the effect is left</summary>
    public double TimerFraction { get => _timerFraction; private set => Set(ref _timerFraction, value); }

    /// <summary>0 to 1, how close the monster is to catching the effect</summary>
    public double BuildUpFraction { get => _buildUpFraction; private set => Set(ref _buildUpFraction, value); }

    public int Count => Math.Max(_count, 0);

    /// <summary>The time left while the effect is active, otherwise the build-up percentage</summary>
    public string Text { get => _text; private set => Set(ref _text, value); }

    public bool IsTimerActive { get => _isTimerActive; private set => Set(ref _isTimerActive, value); }

    public bool IsShown { get => _isShown; private set => Set(ref _isShown, value); }

    public AilmentViewModel(IMonsterAilment source)
    {
        Source = source;
        Id = source.Id;
        Name = AilmentStyle.NameFor(source.Id);
        Brush = AilmentStyle.BrushFor(source.Id);
        Refresh();
    }

    // Demo feed and previews
    public AilmentViewModel(string id, string name)
    {
        Id = id;
        Name = name;
        Brush = AilmentStyle.BrushFor(id);
    }

    /// <summary>Ailments the game lists but that have no name, and the enrage entry the monster reports separately</summary>
    public static bool IsDisplayable(IMonsterAilment ailment) =>
        !ailment.Definition.IsUnknown && ailment.Id != "AILMENT_ENRAGE";

    public void Refresh()
    {
        if (Source is { } source)
            Update(source.Timer, source.MaxTimer, source.BuildUp, source.MaxBuildUp, source.Counter);
        else
            UpdateVisibility();
    }

    public void Update(float timer, float maxTimer, float buildUp, float maxBuildUp, int count)
    {
        bool changed = false;

        if (count != _count)
        {
            _count = count;
            Raise(nameof(Count));
            changed = true;
        }

        if (timer != _timer || maxTimer != _maxTimer)
        {
            _timer = timer;
            _maxTimer = maxTimer;
            TimerFraction = maxTimer > 0 ? Math.Clamp(timer / maxTimer, 0, 1) : 0;
            IsTimerActive = maxTimer > 0 && timer > 0;
            changed = true;
        }

        if (buildUp != _buildUp || maxBuildUp != _maxBuildUp)
        {
            _buildUp = buildUp;
            _maxBuildUp = maxBuildUp;
            BuildUpFraction = maxBuildUp > 0 ? Math.Clamp(buildUp / maxBuildUp, 0, 1) : 0;
            changed = true;
        }

        // A value that merely sits at zero is not news (every ailment reports once when the monster is scanned)
        if (changed && (timer > 0 || buildUp > 0 || count > 0))
            _lastChange = Stopwatch.GetTimestamp();

        UpdateText(changed);
        UpdateVisibility();

        // Only exhaust is logged for now: its meaning is still being worked out, and logging every ailment drowns the output
        if (changed && App.Options.Verbose && IsLogged)
            LogChange();
    }

    // Debug output (--verbose, exhaust entries only): one line per 5% step of the gauge and on every timer or counter change
    private void LogChange()
    {
        int step = (int)(BuildUpFraction * 20);

        if (step == _lastLoggedStep && !IsTimerActive)
            return;

        _lastLoggedStep = step;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}][DEBUG] [Ailment] {Id} (game id {Source?.Definition.Id}): build-up {_buildUp:0}/{_maxBuildUp:0} ({BuildUpFraction * 100:0}%), timer {_timer:0.0}/{_maxTimer:0.0}, count {_count}");
    }

    private void UpdateText(bool changed)
    {
        if (IsTimerActive)
        {
            // Only the seconds are shown, so format once a second instead of on every tiny change
            int seconds = (int)Math.Ceiling(_timer);

            if (seconds != _lastWholeSeconds)
            {
                _lastWholeSeconds = seconds;
                Text = TimeSpan.FromSeconds(seconds).ToString(@"mm\:ss");
            }

            return;
        }

        _lastWholeSeconds = -1;

        if (changed)
            Text = _maxBuildUp > 0 && _buildUp > 0 ? $"{Math.Clamp(_buildUp / _maxBuildUp, 0, 1) * 100:0}%" : string.Empty;
    }

    private void UpdateVisibility() =>
        IsShown = _lastChange != 0 && (IsTimerActive || Stopwatch.GetElapsedTime(_lastChange) < HideAfter);
}
