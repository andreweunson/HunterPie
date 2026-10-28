using HunterPie.Linux.Overlay.ViewModels;

namespace HunterPie.Linux.Overlay.Services;

/// <summary>
/// Connects the widget options in <see cref="OverlaySettings"/> to the view models: loads them once, then writes the
/// file whenever one changes. Any window (settings, edit mode) just binds to the view models.
/// </summary>
internal static class SettingsBinder
{
    public static void Bind(OverlaySettings settings, OverlayViewModel viewModel)
    {
        MonsterOptions monsters = viewModel.MonsterOptions;
        monsters.ShowIcons = settings.Monsters.ShowIcons;
        monsters.ShowStamina = settings.Monsters.ShowStamina;
        monsters.ShowWeaknesses = settings.Monsters.ShowWeaknesses;
        monsters.ShowAilments = settings.Monsters.ShowAilments;
        monsters.PropertyChanged += (_, _) =>
        {
            settings.Monsters.ShowIcons = monsters.ShowIcons;
            settings.Monsters.ShowStamina = monsters.ShowStamina;
            settings.Monsters.ShowWeaknesses = monsters.ShowWeaknesses;
            settings.Monsters.ShowAilments = monsters.ShowAilments;
            settings.Save();
        };

        monsters.Blacklist.Patterns.Clear();
        foreach (string pattern in settings.Monsters.HiddenAilments)
            monsters.Blacklist.Add(pattern);

        monsters.Blacklist.Changed += (_, _) =>
        {
            settings.Monsters.HiddenAilments = [.. monsters.Blacklist.Patterns];
            settings.Save();
        };

        DamageViewModel damage = viewModel.Damage;
        damage.ShowGraph = settings.Damage.ShowGraph;
        damage.ShowStats = settings.Damage.ShowStats;
        damage.GraphMode = settings.Damage.GraphMode;
        damage.WindowSeconds = settings.Damage.GraphWindowSeconds;
        damage.Strategy = settings.Damage.DpsStrategy;
        damage.VisibleSeconds = settings.Damage.GraphVisibleSeconds;
        damage.DiscardOldPoints = settings.Damage.DiscardOldPoints;
        damage.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not (nameof(DamageViewModel.ShowGraph) or nameof(DamageViewModel.ShowStats)
                or nameof(DamageViewModel.GraphMode) or nameof(DamageViewModel.WindowSeconds)
                or nameof(DamageViewModel.Strategy) or nameof(DamageViewModel.DiscardOldPoints) or nameof(DamageViewModel.VisibleSeconds)))
                return;

            settings.Damage.ShowGraph = damage.ShowGraph;
            settings.Damage.ShowStats = damage.ShowStats;
            settings.Damage.GraphMode = damage.GraphMode;
            settings.Damage.GraphWindowSeconds = damage.WindowSeconds;
            settings.Damage.DpsStrategy = damage.Strategy;
            settings.Damage.DiscardOldPoints = damage.DiscardOldPoints;
            settings.Damage.GraphVisibleSeconds = damage.VisibleSeconds;
            settings.Save();
        };
    }
}
