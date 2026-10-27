using Avalonia.Media;
using HunterPie.Core.Game.Entity.Enemy;
using HunterPie.Linux.Overlay.Services;
using HunterPie.Linux.Services;
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

    private double _timerFraction;
    private double _buildUpFraction;
    private string _text = string.Empty;
    private bool _isTimerActive;
    private bool _isShown;
    private bool _isActive;
    private bool _isBlacklisted;

    public IMonsterAilment? Source { get; }

    public string Id { get; }


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

    /// <summary>The ring is drawn: something about it changed recently, and it is not on the blacklist</summary>
    public bool IsShown { get => _isShown; private set => Set(ref _isShown, value); }

    /// <summary>On the user's list of ailments to leave out, see <see cref="AilmentBlacklist"/></summary>
    public bool IsBlacklisted => _isBlacklisted;

    public void SetBlacklisted(bool blacklisted)
    {
        if (_isBlacklisted == blacklisted)
            return;

        _isBlacklisted = blacklisted;
        Raise(nameof(IsBlacklisted));
        UpdateVisibility();
    }

    public AilmentViewModel(IMonsterAilment source)
    {
        Source = source;
        Id = source.Id;
        bool flinch = AilmentNames.IsFlinchGauge(source);
        Name = AilmentNames.NameFor(source);
        Brush = flinch ? AilmentStyle.FlinchBrush : AilmentStyle.BrushFor(source.Id);
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
    public static bool IsDisplayable(IMonsterAilment ailment) => AilmentNames.IsDisplayable(ailment);

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

    private void UpdateVisibility()
    {
        _isActive = _lastChange != 0 && (IsTimerActive || Stopwatch.GetElapsedTime(_lastChange) < HideAfter);
        IsShown = _isActive && !_isBlacklisted;
    }
}
