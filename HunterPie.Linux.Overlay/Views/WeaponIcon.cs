using Avalonia;
using HunterPie.Core.Game.Enums;

namespace HunterPie.Linux.Overlay.Views;

/// <summary>A weapon icon from Assets/WeaponIcons.axaml, see <see cref="VectorIcon"/></summary>
public sealed class WeaponIcon : VectorIcon
{
    public static readonly StyledProperty<Weapon> WeaponProperty =
        AvaloniaProperty.Register<WeaponIcon, Weapon>(nameof(Weapon), Weapon.None);

    static WeaponIcon()
    {
        AffectsRender<WeaponIcon>(WeaponProperty);
    }

    public Weapon Weapon
    {
        get => GetValue(WeaponProperty);
        set => SetValue(WeaponProperty, value);
    }

    protected override string? ResourceKey => Enum.IsDefined(Weapon) ? $"Weapon.{Weapon}" : null;
}
