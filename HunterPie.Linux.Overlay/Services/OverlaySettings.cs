using System.Text.Json;
using System.Text.Json.Serialization;

using HunterPie.Linux.Overlay.ViewModels;

namespace HunterPie.Linux.Overlay.Services;

public class WidgetPlacement
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Scale { get; set; } = 1.0;
    public double Opacity { get; set; } = 1.0;

    /// <summary>Widgets that are switched off are hidden, even in edit mode they stay visible so they can be placed</summary>
    public bool Enabled { get; set; } = true;

    public WidgetPlacement()
    {
    }

    public WidgetPlacement(double x, double y)
    {
        X = x;
        Y = y;
    }
}

/// <summary>The monster widget's position plus what it shows</summary>
public sealed class MonsterPlacement : WidgetPlacement
{
    public bool ShowIcons { get; set; } = true;
    public bool ShowStamina { get; set; } = true;
    public bool ShowWeaknesses { get; set; } = true;
    public bool ShowAilments { get; set; } = true;

    /// <summary>Ailment ids or names (with * and ? wildcards) that never get a ring, see <see cref="AilmentBlacklist"/></summary>
    public List<string> HiddenAilments { get; set; } = [.. AilmentBlacklist.Defaults];

    public MonsterPlacement()
    {
    }

    public MonsterPlacement(double x, double y) : base(x, y)
    {
    }
}

/// <summary>Reads the mode by name; a name this version does not know means the default rather than a lost settings file</summary>
internal sealed class NameOrDefaultConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && Enum.TryParse(reader.GetString(), true, out T value) && Enum.IsDefined(value)
            ? value
            : default;

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

/// <summary>The damage widget's position plus what it shows</summary>
public sealed class DamagePlacement : WidgetPlacement
{
    public bool ShowGraph { get; set; } = true;
    public bool ShowStats { get; set; } = true;

    /// <summary>What the graph plots: DamagePerSecond, MovingAverage or TotalDamage</summary>
    [JsonConverter(typeof(NameOrDefaultConverter<GraphMode>))]
    public GraphMode GraphMode { get; set; } = GraphMode.DamagePerSecond;

    /// <summary>Where DPS starts counting: RelativeToJoin, RelativeToQuest or RelativeToFirstHit</summary>
    [JsonConverter(typeof(NameOrDefaultConverter<DpsStrategy>))]
    public DpsStrategy DpsStrategy { get; set; } = DpsStrategy.RelativeToJoin;

    /// <summary>With the moving average: only draw the last GraphVisibleSeconds</summary>
    public bool DiscardOldPoints { get; set; }

    public int GraphVisibleSeconds { get; set; } = DamageViewModel.DefaultVisibleSeconds;

    /// <summary>Seconds the moving average looks back</summary>
    public int GraphWindowSeconds { get; set; } = DamageViewModel.DefaultWindowSeconds;

    public DamagePlacement()
    {
    }

    public DamagePlacement(double x, double y) : base(x, y)
    {
    }
}

/// <summary>
/// Where each widget sits, stored in $XDG_CONFIG_HOME/HunterPie/overlay.json. Positions are in device independent
/// pixels from the top-left of the screen the overlay covers.
/// </summary>
public sealed class OverlaySettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public WidgetPlacement Quest { get; set; } = new(40, 40);

    public MonsterPlacement Monsters { get; set; } = new(40, 150);

    public DamagePlacement Damage { get; set; } = new(40, 360);

    /// <summary>Index of the monitor the game is on (0 = primary)</summary>
    public int Screen { get; set; }

    /// <summary>Bypass the window manager so the overlay stays above fullscreen games (no keyboard focus)</summary>
    public bool OverrideRedirect { get; set; }

    [JsonIgnore]
    public static string FilePath
    {
        get
        {
            string? xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            string root = !string.IsNullOrWhiteSpace(xdg) && Path.IsPathRooted(xdg)
                ? xdg
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

            return Path.Combine(root, "HunterPie", "overlay.json");
        }
    }

    public static OverlaySettings Load(string? path = null)
    {
        path ??= FilePath;

        try
        {
            if (File.Exists(path))
                return Sanitize(JsonSerializer.Deserialize<OverlaySettings>(File.ReadAllText(path), JsonOptions) ?? new OverlaySettings());
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not read {path} ({ex.Message}), using defaults");
        }

        return new OverlaySettings();
    }

    public void Save(string? path = null)
    {
        path ??= FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        string temp = $"{path}.{Environment.ProcessId}.tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }

    // A hand-edited file can hold nonsense; keep widgets on screen and visible
    private static OverlaySettings Sanitize(OverlaySettings settings)
    {
        foreach (WidgetPlacement placement in new WidgetPlacement[] { settings.Quest, settings.Monsters, settings.Damage })
        {
            placement.X = double.IsFinite(placement.X) ? Math.Max(0, placement.X) : 0;
            placement.Y = double.IsFinite(placement.Y) ? Math.Max(0, placement.Y) : 0;
            placement.Scale = double.IsFinite(placement.Scale) ? Math.Clamp(placement.Scale, 0.4, 3.0) : 1.0;
            placement.Opacity = double.IsFinite(placement.Opacity) ? Math.Clamp(placement.Opacity, 0.1, 1.0) : 1.0;
        }

        // A file saved without the list keeps the defaults; one that lists nothing really hides nothing. Only junk goes.
        settings.Monsters.HiddenAilments = [.. (settings.Monsters.HiddenAilments ?? [.. AilmentBlacklist.Defaults])
            .Where(it => !string.IsNullOrWhiteSpace(it))
            .Select(it => it.Trim())];

        settings.Damage.GraphWindowSeconds = Math.Clamp(settings.Damage.GraphWindowSeconds, DamageViewModel.MinWindowSeconds, PlayerDamageViewModel.MaxWindowSeconds);
        settings.Damage.GraphVisibleSeconds = Math.Clamp(settings.Damage.GraphVisibleSeconds, DamageViewModel.MinVisibleSeconds, PlayerDamageViewModel.RecentCapacity);
        if (!Enum.IsDefined(settings.Damage.GraphMode))
            settings.Damage.GraphMode = GraphMode.DamagePerSecond;
        if (!Enum.IsDefined(settings.Damage.DpsStrategy))
            settings.Damage.DpsStrategy = DpsStrategy.RelativeToJoin;

        settings.Screen = Math.Max(0, settings.Screen);

        return settings;
    }
}
