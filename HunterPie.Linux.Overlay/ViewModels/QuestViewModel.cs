using HunterPie.Core.Game;

namespace HunterPie.Linux.Overlay.ViewModels;

public sealed class QuestViewModel : ViewModelBase
{
    private bool _isActive;
    private string _name = string.Empty;
    private string _timeLeft = "--:--";
    private string _elapsed = "--:--";

    /// <summary>True while a quest is running</summary>
    public bool IsActive { get => _isActive; private set => Set(ref _isActive, value); }

    public string Name { get => _name; private set => Set(ref _name, value); }

    public bool HasName => Name.Length > 0;

    public string TimeLeft { get => _timeLeft; private set => Set(ref _timeLeft, value); }

    public string Elapsed { get => _elapsed; private set => Set(ref _elapsed, value); }

    public void Refresh(Context? context)
    {
        if (context?.Game.Quest is not { } quest)
        {
            IsActive = false;
            return;
        }

        IsActive = true;
        Name = quest.Name.Trim();
        Raise(nameof(HasName));
        TimeLeft = Format(quest.TimeLeft);
        Elapsed = Format(TimeSpan.FromSeconds(Math.Max(0, context.Game.TimeElapsed)));
    }

    public void Set(string name, TimeSpan left, TimeSpan elapsed)
    {
        IsActive = true;
        Name = name;
        Raise(nameof(HasName));
        TimeLeft = Format(left);
        Elapsed = Format(elapsed);
    }

    private static string Format(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
            time = TimeSpan.Zero;

        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes:00}:{time.Seconds:00}";
    }
}
