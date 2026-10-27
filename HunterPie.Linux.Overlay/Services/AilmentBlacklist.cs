using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace HunterPie.Linux.Overlay.Services;

/// <summary>
/// Ailments the monster cards never draw. Each entry is a pattern matched, ignoring case, against an ailment's id
/// (AILMENT_FLAYER_PL1) and its display name (Flayer PL1); <c>*</c> stands for any run of characters and <c>?</c> for
/// one, so <c>AILMENT_FLAYER_*</c> or <c>Flayer*</c> hides all four flayer rings. Lives in overlay.json under
/// Monsters.HiddenAilments, and is edited from the settings window.
/// </summary>
public sealed class AilmentBlacklist
{
    /// <summary>Hidden until the user says otherwise: the game reports a flayer ailment for every hunter in the party</summary>
    public static IReadOnlyList<string> Defaults { get; } = ["AILMENT_FLAYER_*"];

    // Ids the Wilds data defines, offered as suggestions; anything the game reports later is added as it is seen
    private static readonly string[] SeedIds =
    [
        "AILMENT_BLAST", "AILMENT_CONVERT_ELEMENT", "AILMENT_DUNG", "AILMENT_FLASH", "AILMENT_FLAYER_PL1",
        "AILMENT_FLAYER_PL2", "AILMENT_FLAYER_PL3", "AILMENT_FLAYER_PL4", "AILMENT_MOUNT", "AILMENT_OFFSET",
        "AILMENT_PARALYSIS", "AILMENT_POISON", "AILMENT_POWERCLASH", "AILMENT_SLEEP", "AILMENT_STUN",
        "AILMENT_TRANQUILIZE"
    ];

    private int _version;

    public AilmentBlacklist(IEnumerable<string>? patterns = null)
    {
        foreach (string id in SeedIds)
            Known.Add(id);

        foreach (string pattern in patterns ?? Defaults)
        {
            if (Clean(pattern) is { } clean && !Contains(clean))
                Patterns.Add(clean);
        }

        // Subscribed after the initial fill so loading does not count as an edit
        Patterns.CollectionChanged += OnPatternsChanged;
    }

    /// <summary>The entries, in the order they were added</summary>
    public ObservableCollection<string> Patterns { get; } = [];

    /// <summary>Ailment ids to suggest when adding an entry: the seeded ones plus every id seen on a monster</summary>
    public ObservableCollection<string> Known { get; } = [];

    /// <summary>Raised after the entries changed (on the thread that changed them)</summary>
    public event EventHandler? Changed;

    /// <summary>Goes up on every change, so a card can tell cheaply whether its answers are out of date</summary>
    public int Version => _version;

    public bool IsHidden(string id, string name)
    {
        // Index loop on purpose: the monster cards ask this from the UI timer
        for (int i = 0; i < Patterns.Count; i++)
        {
            string pattern = Patterns[i];

            if (Matches(pattern, id) || Matches(pattern, name))
                return true;
        }

        return false;
    }

    /// <summary>Adds an entry. False when it is empty or already there</summary>
    public bool Add(string? pattern)
    {
        if (Clean(pattern) is not { } clean || Contains(clean))
            return false;

        Patterns.Add(clean);

        return true;
    }

    public bool Remove(string pattern)
    {
        for (int i = 0; i < Patterns.Count; i++)
        {
            if (!string.Equals(Patterns[i], pattern, StringComparison.OrdinalIgnoreCase))
                continue;

            Patterns.RemoveAt(i);

            return true;
        }

        return false;
    }

    public void ResetToDefaults()
    {
        Patterns.Clear();

        foreach (string pattern in Defaults)
            Patterns.Add(pattern);
    }

    /// <summary>Remembers an id the game reported, so it shows up in the suggestions</summary>
    public void Learn(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;

        foreach (string known in Known)
        {
            if (string.Equals(known, id, StringComparison.OrdinalIgnoreCase))
                return;
        }

        Known.Add(id);
    }

    /// <summary>
    /// Glob match, ignoring case: <c>*</c> is any run of characters (also none), <c>?</c> exactly one
    /// </summary>
    public static bool Matches(string pattern, string text)
    {
        if (pattern.Length == 0)
            return false;

        int p = 0, t = 0, star = -1, resume = 0;

        while (t < text.Length)
        {
            if (p < pattern.Length && pattern[p] != '*' && (pattern[p] == '?' || char.ToUpperInvariant(pattern[p]) == char.ToUpperInvariant(text[t])))
            {
                p++;
                t++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                resume = t;
            }
            else if (star >= 0)
            {
                // Let the last star swallow one more character and try again
                p = star + 1;
                t = ++resume;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
            p++;

        return p == pattern.Length;
    }

    private bool Contains(string pattern)
    {
        foreach (string existing in Patterns)
        {
            if (string.Equals(existing, pattern, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string? Clean(string? pattern)
    {
        string? trimmed = pattern?.Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private void OnPatternsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _version++;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
