namespace HunterPie.Linux.Overlay.ViewModels;

/// <summary>Which moment the damage meter starts counting seconds from. Same choices as the Windows client.</summary>
public enum DpsStrategy
{
    /// <summary>From when the hunter was first seen in the party (the quest start for the ones there from the beginning)</summary>
    RelativeToJoin,

    /// <summary>From the start of the quest, however late the hunter arrived</summary>
    RelativeToQuest,

    /// <summary>From the hunter's first hit, so time spent running to the monster does not count</summary>
    RelativeToFirstHit
}
