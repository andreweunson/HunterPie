namespace HunterPie.Linux.Overlay.ViewModels;

/// <summary>What the damage widget's graph plots. Same three choices as the Windows client's damage plot strategy.</summary>
public enum GraphMode
{
    /// <summary>The DPS number shown in the rows: damage divided by the time since the hunter joined</summary>
    DamagePerSecond,

    /// <summary>Damage dealt in the last few seconds, divided by those seconds</summary>
    MovingAverage,

    /// <summary>Damage dealt so far</summary>
    TotalDamage
}
