using System.Text.Json;
using System.Text.Json.Serialization;

namespace HunterPie.Linux.Overlay.Services;

public sealed class WidgetPlacement
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Scale { get; set; } = 1.0;
    public double Opacity { get; set; } = 1.0;

    public WidgetPlacement()
    {
    }

    public WidgetPlacement(double x, double y)
    {
        X = x;
        Y = y;
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

    public WidgetPlacement Monsters { get; set; } = new(40, 150);

    public WidgetPlacement Damage { get; set; } = new(40, 360);

    /// <summary>Index of the monitor the game is on (0 = primary)</summary>
    public int Screen { get; set; }

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
        foreach (WidgetPlacement placement in new[] { settings.Quest, settings.Monsters })
        {
            placement.X = double.IsFinite(placement.X) ? Math.Max(0, placement.X) : 0;
            placement.Y = double.IsFinite(placement.Y) ? Math.Max(0, placement.Y) : 0;
            placement.Scale = double.IsFinite(placement.Scale) ? Math.Clamp(placement.Scale, 0.4, 3.0) : 1.0;
            placement.Opacity = double.IsFinite(placement.Opacity) ? Math.Clamp(placement.Opacity, 0.1, 1.0) : 1.0;
        }

        settings.Screen = Math.Max(0, settings.Screen);

        return settings;
    }
}
