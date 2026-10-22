using HunterPie.Core.Client.Localization;
using HunterPie.Core.Game.Entity.Enemy;
using HunterPie.DI;
using System.Collections.Concurrent;
using System.Globalization;

namespace HunterPie.Linux.Services;

/// <summary>Display names for monster ailments, shared by the overlay and the hunt logs</summary>
internal static class AilmentNames
{
    private static readonly ConcurrentDictionary<string, string> Names = new();

    /// <summary>
    /// The game's ailment table calls entry 14 "exhaust", but on real hunts it is a gauge that fills as the monster is
    /// hit, decays slowly, and resets with a larger maximum when it flinches; its timer never runs. The real exhaust is
    /// the monster's stamina object, so this entry is the flinch gauge.
    /// </summary>
    public static bool IsFlinchGauge(IMonsterAilment ailment) =>
        ailment.Id == "AILMENT_EXHAUST" && ailment.Definition.Id == 14;

    /// <summary>Ailments worth showing or logging: named ones, except the game's duplicate enrage entry</summary>
    public static bool IsDisplayable(IMonsterAilment ailment) =>
        !ailment.Definition.IsUnknown && ailment.Id != "AILMENT_ENRAGE";

    public static string NameFor(IMonsterAilment ailment) => IsFlinchGauge(ailment) ? "Flinch" : NameFor(ailment.Id);

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
    public static string Prettify(string id)
    {
        int separator = id.IndexOf('_');
        string body = separator >= 0 ? id[(separator + 1)..] : id;

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(body.Replace('_', ' ').ToLowerInvariant());
    }
}
