using Avalonia.Media;
using Avalonia.Media.Immutable;
using HunterPie.Linux.Services;

namespace HunterPie.Linux.Overlay.Services;

/// <summary>Display names and ring colours for monster ailments, same ids and colours as the Windows client</summary>
internal static class AilmentStyle
{
    private static readonly IBrush Unknown = Brush("#73ABEE");

    // The Windows client uses gradients for some of these; a single colour from the gradient reads fine on a thin ring
    private static readonly Dictionary<string, IBrush> Brushes = new()
    {
        ["STATUS_ENRAGE"] = Brush("#EF473A"),
        ["AILMENT_EXHAUST"] = Brush("#74D4B1"),
        ["AILMENT_POISON"] = Brush("#B037F5"),
        ["AILMENT_PARALYSIS"] = Brush("#EAD86E"),
        ["AILMENT_SLEEP"] = Brush("#6BE8A5"),
        ["AILMENT_BLAST"] = Brush("#DB7E53"),
        ["AILMENT_MOUNT"] = Brush("#5380DB"),
        ["AILMENT_STUN"] = Brush("#DEB83C"),
        ["AILMENT_TRANQUILIZE"] = Brush("#C96BA0"),
        ["AILMENT_FLASH"] = Brush("#F7C052"),
        ["AILMENT_DUNG"] = Brush("#8A5A2B"),
        ["AILMENT_WATER"] = Brush("#8FC6FF"),
        ["AILMENT_FIRE"] = Brush("#F0503F"),
        ["AILMENT_ICE"] = Brush("#BDE9FF"),
        ["AILMENT_THUNDER"] = Brush("#FFC060"),
        ["AILMENT_SMOKING"] = Brush("#9C9C9C"),
        ["AILMENT_KNOCKDOWN"] = Brush("#5380DB"),
        ["AILMENT_CLAW"] = Brush("#6AB7D4"),
        ["AILMENT_ELDERSEAL"] = Brush("#9B5BD0"),
    };

    public static IBrush FlinchBrush { get; } = Brush("#E3A857");

    public static IBrush BrushFor(string id) => Brushes.GetValueOrDefault(id, Unknown);

    public static string NameFor(string id) => AilmentNames.NameFor(id);

    internal static string Prettify(string id) => AilmentNames.Prettify(id);

    private static IBrush Brush(string color) => new ImmutableSolidColorBrush(Color.Parse(color));
}
