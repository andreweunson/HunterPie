using HunterPie.Core.Domain.Interfaces;
using System.Text.Json;

namespace HunterPie.Platforms.Linux.Storage;

/// <summary>
/// Replacement for the HKCU\SOFTWARE\HunterPie registry key: a small JSON file in the XDG config directory.
/// Values keep their type (string, int, long, bytes) because callers rely on it (the secret key is a byte[]).
/// </summary>
internal class LinuxLocalRegistry : ILocalRegistryAsync, ILocalRegistry
{
    private readonly Lock _lock = new();
    private readonly string _path = Path.Combine(LinuxPaths.ConfigDirectory, "registry.json");
    private readonly Dictionary<string, Entry> _values;

    private record Entry(string Type, string Value);

    public LinuxLocalRegistry()
    {
        _values = Load();
    }

    public Task SetAsync<T>(string name, T value) => Task.Run(() => Set(name, value));

    public Task<bool> ExistsAsync(string name) => Task.Run(() => Exists(name));

    public Task<object?> GetAsync(string name) => Task.Run(() => Get(name));

    public Task<T?> GetAsync<T>(string name) => Task.Run(() => Get<T>(name));

    public Task DeleteAsync(string name) => Task.Run(() => Delete(name));

    public void Set<T>(string name, T value)
    {
        Entry entry = value switch
        {
            byte[] bytes => new Entry("bytes", Convert.ToBase64String(bytes)),
            int number => new Entry("int", number.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            long number => new Entry("long", number.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            null => throw new ArgumentNullException(nameof(value)),
            _ => new Entry("string", Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)
        };

        lock (_lock)
        {
            _values[name] = entry;
            Save();
        }
    }

    public bool Exists(string name)
    {
        lock (_lock)
            return _values.ContainsKey(name);
    }

    public object? Get(string name)
    {
        Entry? entry;

        lock (_lock)
            entry = _values.GetValueOrDefault(name);

        return entry?.Type switch
        {
            "bytes" => Convert.FromBase64String(entry.Value),
            "int" => int.Parse(entry.Value, System.Globalization.CultureInfo.InvariantCulture),
            "long" => long.Parse(entry.Value, System.Globalization.CultureInfo.InvariantCulture),
            "string" => entry.Value,
            _ => null
        };
    }

    public T? Get<T>(string name)
    {
        object? val = Get(name);

        if (val is null)
            return default;

        if (val is T direct)
            return direct;

        object? converted = Convert.ChangeType(val, typeof(T), System.Globalization.CultureInfo.InvariantCulture);

        return converted is T value ? value : default;
    }

    public void Delete(string name)
    {
        lock (_lock)
        {
            if (_values.Remove(name))
                Save();
        }
    }

    private Dictionary<string, Entry> Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new Dictionary<string, Entry>();

            return JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(_path))
                   ?? new Dictionary<string, Entry>();
        }
        catch (JsonException)
        {
            // A corrupted file is treated as empty, same as a fresh registry key
            return new Dictionary<string, Entry>();
        }
    }

    private void Save() => LinuxPaths.WritePrivateFile(_path, JsonSerializer.Serialize(_values));
}
