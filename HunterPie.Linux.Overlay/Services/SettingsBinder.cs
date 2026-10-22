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
        monsters.PropertyChanged += (_, _) =>
        {
            settings.Monsters.ShowIcons = monsters.ShowIcons;
            settings.Monsters.ShowStamina = monsters.ShowStamina;
            settings.Monsters.ShowWeaknesses = monsters.ShowWeaknesses;
            settings.Save();
        };

        DamageViewModel damage = viewModel.Damage;
        damage.ShowGraph = settings.Damage.ShowGraph;
        damage.ShowStats = settings.Damage.ShowStats;
        damage.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not (nameof(DamageViewModel.ShowGraph) or nameof(DamageViewModel.ShowStats)))
                return;

            settings.Damage.ShowGraph = damage.ShowGraph;
            settings.Damage.ShowStats = damage.ShowStats;
            settings.Save();
        };
    }
}
