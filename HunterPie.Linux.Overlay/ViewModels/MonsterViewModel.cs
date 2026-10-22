using Avalonia.Media;
using Avalonia.Threading;
using System.Collections.ObjectModel;
using HunterPie.Core.Game.Entity.Enemy;
using HunterPie.Core.Game.Enums;
using HunterPie.Linux.Overlay.Services;

namespace HunterPie.Linux.Overlay.ViewModels;

/// <summary>
/// What the monster widget shows for one monster. The game raises events from its scan threads, so instead of
/// reacting to each one this is refreshed on the UI timer, which also caps redraws at the timer rate. Every value is
/// compared with the previous one first: text is only formatted, and properties only raised, when something moved.
/// </summary>
public sealed class MonsterViewModel : ViewModelBase, IDisposable
{
    private static readonly IBrush NormalBrush = new SolidColorBrush(Color.Parse("#3A3E40"));
    private static readonly IBrush TemperedBrush = new SolidColorBrush(Color.Parse("#8C44D4"));
    private static readonly IBrush ArchTemperedBrush = new SolidColorBrush(Color.Parse("#FF7A05"));
    private static readonly IBrush FrenzyBrush = new SolidColorBrush(Color.Parse("#FA027E"));

    private static readonly Element[] NoWeaknesses = [];

    private string _name = string.Empty;
    private string? _lastRawName;
    private double _percent;
    private double _lastHealth = double.NaN;
    private double _lastMaxHealth = double.NaN;
    private string _healthText = string.Empty;
    private bool _isEnraged;

    private double _lastStamina = double.NaN;
    private double _lastMaxStamina = double.NaN;
    private double _staminaPercent;
    private string _staminaText = string.Empty;
    private bool _hasStamina;

    private double _lastThreshold = double.NaN;
    private double _captureAt;
    private bool _isCapturable;

    private bool _hasAilments;

    private Element[] _weaknesses = NoWeaknesses;
    private VariantType _variant = VariantType.Normal;
    private IBrush _variantBrush = NormalBrush;
    private IImage? _icon;
    private bool _iconRequested;
    private readonly string? _iconId;

    public IMonster? Source { get; }

    /// <summary>Enrage first, then the ailments the game reports for this monster</summary>
    public ObservableCollection<AilmentViewModel> Ailments { get; } = new();

    /// <summary>At least one ailment ring is on screen right now (the row is hidden otherwise)</summary>
    public bool HasAilments { get => _hasAilments; private set => Set(ref _hasAilments, value); }

    public MonsterOptions Options { get; }

    public string Name { get => _name; private set => Set(ref _name, value); }

    /// <summary>0 to 100</summary>
    public double Percent
    {
        get => _percent;
        private set
        {
            if (!Set(ref _percent, value))
                return;

            Raise(nameof(PercentText));
            Raise(nameof(IsLow));
            Raise(nameof(IsMedium));
            UpdateCapturable();
        }
    }

    public string PercentText => $"{Percent:0}%";

    public string HealthText { get => _healthText; private set => Set(ref _healthText, value); }

    public bool IsEnraged { get => _isEnraged; private set => Set(ref _isEnraged, value); }

    /// <summary>Below 25%: the bar turns red</summary>
    public bool IsLow => Percent < 25;

    /// <summary>Below 50%: the bar turns amber</summary>
    public bool IsMedium => Percent is >= 25 and < 50;

    /// <summary>0 to 100, how full the stamina bar is</summary>
    public double StaminaPercent { get => _staminaPercent; private set => Set(ref _staminaPercent, value); }

    public string StaminaText { get => _staminaText; private set => Set(ref _staminaText, value); }

    /// <summary>False for monsters that report no stamina (the bar is hidden then)</summary>
    public bool HasStamina { get => _hasStamina; private set => Set(ref _hasStamina, value); }

    /// <summary>Health percentage (0-100) at which the monster can be captured, 0 if it cannot be</summary>
    public double CaptureAt
    {
        get => _captureAt;
        private set
        {
            if (Set(ref _captureAt, value))
                UpdateCapturable();
        }
    }

    /// <summary>The monster's health is at or below its capture threshold right now</summary>
    public bool IsCapturable { get => _isCapturable; private set => Set(ref _isCapturable, value); }

    public Element[] Weaknesses { get => _weaknesses; private set { if (Set(ref _weaknesses, value)) Raise(nameof(HasWeaknesses)); } }

    public bool HasWeaknesses => Weaknesses.Length > 0;

    /// <summary>Outline of the portrait: grey for normal monsters, coloured for tempered, arch-tempered and frenzied</summary>
    public IBrush VariantBrush { get => _variantBrush; private set => Set(ref _variantBrush, value); }

    /// <summary>The portrait, once downloaded. Null until then and for monsters that have none</summary>
    public IImage? Icon { get => _icon; private set => Set(ref _icon, value); }

    public MonsterViewModel(IMonster source, MonsterOptions options)
    {
        Source = source;
        Options = options;
        _iconId = BuildIconId(source);

        // Subscribe first, then take the snapshot: an ailment found in between is deduplicated in AddAilment
        source.OnNewAilmentFound += OnNewAilmentFound;
        if (source.Enrage is { } enrage)
            AddAilment(enrage);

        foreach (IMonsterAilment ailment in SnapshotAilments(source))
            AddAilment(ailment);

        Refresh();
        EnsureIcon();
    }

    // Used by the demo feed and previews
    public MonsterViewModel(string name, double health, double maxHealth, bool isEnraged, MonsterOptions options, string? iconId = null)
    {
        Options = options;
        _iconId = iconId;
        Set(name, health, maxHealth, isEnraged);
    }

    public void Refresh()
    {
        if (Source is not { } monster)
            return;

        Set(monster.Name, monster.Health, monster.MaxHealth, monster.IsEnraged);
        SetStamina(monster.Stamina, monster.MaxStamina);
        SetCapture(monster.CaptureThreshold);
        SetDetails(monster.Weaknesses, monster.Variant);
        RefreshAilments();
    }

    /// <summary>Used by the demo feed, where there is no game to report ailments</summary>
    public void AddAilment(AilmentViewModel ailment) => Ailments.Add(ailment);

    public void RefreshAilments()
    {
        bool any = false;

        // Index loop on purpose: no enumerator allocation on a 10 Hz path
        for (int i = 0; i < Ailments.Count; i++)
        {
            AilmentViewModel ailment = Ailments[i];
            ailment.Refresh();
            any |= ailment.IsShown;
        }

        HasAilments = any;
    }

    public void Dispose()
    {
        if (Source is { } monster)
            monster.OnNewAilmentFound -= OnNewAilmentFound;
    }

    // Raised from the game's scan thread
    private void OnNewAilmentFound(object? sender, IMonsterAilment ailment) => Dispatcher.UIThread.Post(() => AddAilment(ailment));

    private void AddAilment(IMonsterAilment ailment)
    {
        if (!AilmentViewModel.IsDisplayable(ailment))
            return;

        for (int i = 0; i < Ailments.Count; i++)
        {
            if (ReferenceEquals(Ailments[i].Source, ailment))
                return;
        }

        Ailments.Add(new AilmentViewModel(ailment));
    }

    private static IMonsterAilment[] SnapshotAilments(IMonster monster)
    {
        // The game mutates its list from the scan thread
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return monster.Ailments is { } ailments ? [.. ailments] : [];
            }
            catch (InvalidOperationException)
            {
            }
        }

        return [];
    }

    public void Set(string name, double health, double maxHealth, bool isEnraged)
    {
        // The game formats names with padding; trimming allocates, so only do it when the name object changed
        if (!ReferenceEquals(name, _lastRawName))
        {
            _lastRawName = name;
            Name = name.Trim();
        }

        IsEnraged = isEnraged;

        // Refreshed 10 times a second while health rarely moves: skip the formatting when nothing changed
        if (health == _lastHealth && maxHealth == _lastMaxHealth)
            return;

        _lastHealth = health;
        _lastMaxHealth = maxHealth;
        HealthText = maxHealth > 0 ? $"{health:N0} / {maxHealth:N0}" : "-";
        Percent = maxHealth > 0 ? Math.Clamp(health / maxHealth * 100, 0, 100) : 0;
    }

    public void SetStamina(double stamina, double maxStamina)
    {
        if (stamina == _lastStamina && maxStamina == _lastMaxStamina)
            return;

        _lastStamina = stamina;
        _lastMaxStamina = maxStamina;
        HasStamina = maxStamina > 0;
        StaminaPercent = maxStamina > 0 ? Math.Clamp(stamina / maxStamina * 100, 0, 100) : 0;
        StaminaText = maxStamina > 0 ? $"{stamina:N0} / {maxStamina:N0}" : string.Empty;
    }

    /// <summary>The threshold is a fraction of max health (0.35 = capturable below 35%)</summary>
    public void SetCapture(double threshold)
    {
        if (threshold == _lastThreshold)
            return;

        _lastThreshold = threshold;
        // The game stores a float: 0.35f is 34.9999994 as a double, so round before comparing it with health
        CaptureAt = threshold <= 0 ? 0 : Math.Round(Math.Clamp(threshold > 1 ? threshold : threshold * 100, 0, 100), 3);
    }

    public void SetDetails(Element[]? weaknesses, VariantType variant)
    {
        // The game hands out the same array each time, so this is a pointer compare per refresh
        Element[] value = weaknesses ?? NoWeaknesses;

        if (!ReferenceEquals(value, _weaknesses))
            Weaknesses = value;

        if (variant == _variant)
            return;

        _variant = variant;
        VariantBrush = variant.HasFlag(VariantType.ArchTempered) ? ArchTemperedBrush
            : variant.HasFlag(VariantType.Tempered) ? TemperedBrush
            : variant.HasFlag(VariantType.Frenzy) ? FrenzyBrush
            : NormalBrush;
    }

    /// <summary>Starts loading the portrait if the option is on and it has not been requested yet</summary>
    public void EnsureIcon()
    {
        if (_iconRequested || _iconId is null || !Options.ShowIcons)
            return;

        _iconRequested = true;
        _ = LoadIconAsync(_iconId);
    }

    private async Task LoadIconAsync(string id)
    {
        IImage? icon = await MonsterIconService.GetAsync(id).ConfigureAwait(false);

        if (icon is null)
        {
            // Not there (yet): a later EnsureIcon may try again, e.g. after the network comes back
            _iconRequested = false;
            return;
        }

        Dispatcher.UIThread.Post(() => Icon = icon);
    }

    private void UpdateCapturable() => IsCapturable = CaptureAt > 0 && Percent > 0 && Percent <= CaptureAt + 1e-6;

    private static string? BuildIconId(IMonster monster) => monster.GetType().Name switch
    {
        "MHWildsMonster" => $"Wilds_{monster.Id:00}",
        "MHWMonster" => $"World_{monster.Id:00}",
        "MHRMonster" => $"Rise_{monster.Id:00}",
        _ => null
    };
}
