using Avalonia.Media;
using Avalonia.Media.Immutable;
using HunterPie.Core.Client.Localization;
using HunterPie.DI;
using System.Collections.Concurrent;
using System.Globalization;

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

    private static readonly ConcurrentDictionary<string, string> Names = new();

    public static IBrush FlinchBrush { get; } = Brush("#E3A857");

    public static IBrush BrushFor(string id) => Brushes.GetValueOrDefault(id, Unknown);

    /// <summary>Localised name from the language file, or a readable version of the id when there is none</summary>
    public static string NameFor(string id)
    {
        if (Names.TryGetValue(id, out string? cached))
            return cached;

        try
        {
            // The Windows client reads every game's ailments from the Rise section of the language file
            var repository = DependencyContainer.Get<ILocalizationRepository>();
            string path = $"//Strings/Ailments/Rise/Ailment[@Id='{id}']";
            string name = repository.ExistsBy(path) ? repository.FindStringBy(path) : Prettify(id);

            return Names.GetOrAdd(id, name);
        }
        catch (Exception ex) when (ex is NullReferenceException or InvalidOperationException or KeyNotFoundException)
        {
            // The host is not running (demo mode, tests): use the id, and do not remember it so a later lookup can succeed
            return Prettify(id);
        }
    }

    /// <summary>AILMENT_CLAW_FLINCH -> Claw Flinch</summary>
    internal static string Prettify(string id)
    {
        int separator = id.IndexOf('_');
        string body = separator >= 0 ? id[(separator + 1)..] : id;

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(body.Replace('_', ' ').ToLowerInvariant());
    }

    private static IBrush Brush(string color) => new ImmutableSolidColorBrush(Color.Parse(color));
}
