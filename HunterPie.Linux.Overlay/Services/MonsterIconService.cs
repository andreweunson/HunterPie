using Avalonia.Media.Imaging;
using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;

namespace HunterPie.Linux.Overlay.Services;

/// <summary>
/// Monster portraits. HunterPie's icons are not part of the source tree: the Windows client downloads each one the
/// first time it is needed from its CDN and keeps it on disk, and so does this. Files go to
/// $XDG_CACHE_HOME/HunterPie/monster-icons, never next to the executable.
///
/// A download is accepted only if the monster id is one we built ourselves (<c>Wilds_20</c>), the server answers 200
/// within a few seconds, the body is a small file starting with the PNG signature and it decodes. A missing icon (404)
/// is remembered for the session; a network failure is not, so the next monster of that kind tries again.
/// </summary>
internal static partial class MonsterIconService
{
    private const string DefaultBaseUrl = "https://cdn.hunterpie.com";
    private const string BaseUrlVariable = "HUNTERPIE_ICON_CDN";
    private const int MaxBytes = 512 * 1024;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly ConcurrentDictionary<string, Task<Bitmap?>> Pending = new();
    private static readonly HttpClient Client = CreateClient();

    [GeneratedRegex(@"^(Wilds|World|Rise)_\d{1,4}$")]
    private static partial Regex ValidId();

    private static string BaseUrl => Environment.GetEnvironmentVariable(BaseUrlVariable) is { Length: > 0 } custom
        ? custom.TrimEnd('/')
        : DefaultBaseUrl;

    internal static string CacheDirectory
    {
        get
        {
            string? xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            string root = !string.IsNullOrWhiteSpace(xdg) && Path.IsPathRooted(xdg)
                ? xdg
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");

            return Path.Combine(root, "HunterPie", "monster-icons");
        }
    }

    /// <summary>The icon for an id like <c>Wilds_20</c>, or null if there is none (yet). Never throws</summary>
    public static Task<Bitmap?> GetAsync(string id)
    {
        if (!ValidId().IsMatch(id))
            return Task.FromResult<Bitmap?>(null);

        // One load per id at a time, however many monsters of that kind spawn
        return Pending.GetOrAdd(id, key => LoadAsync(key));
    }

    private static async Task<Bitmap?> LoadAsync(string id)
    {
        try
        {
            string path = Path.Combine(CacheDirectory, $"{id}.png");

            if (File.Exists(path) && TryDecode(path) is { } cached)
                return cached;

            (byte[]? data, bool permanent) = await DownloadAsync(id).ConfigureAwait(false);

            if (data is null)
            {
                // Network trouble: allow a retry when the next monster of this kind spawns
                if (!permanent)
                    Pending.TryRemove(id, out _);

                return null;
            }

            Directory.CreateDirectory(CacheDirectory);
            string temp = $"{path}.{Environment.ProcessId}.tmp";
            await File.WriteAllBytesAsync(temp, data).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);

            Bitmap? icon = TryDecode(path);

            // A file that downloaded fine but does not decode will not get better by asking again
            if (icon is null)
                File.Delete(path);

            return icon;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Pending.TryRemove(id, out _);
            return null;
        }
    }

    private static async Task<(byte[]? Data, bool Permanent)> DownloadAsync(string id)
    {
        try
        {
            using HttpResponseMessage response = await Client
                .GetAsync($"{BaseUrl}/Assets/Monsters/Icons/{id}.png", HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return (null, true);

            if (!response.IsSuccessStatusCode)
                return (null, false);

            if (response.Content.Headers.ContentLength is > MaxBytes)
                return (null, true);

            // The length header is optional and not trusted: cap what is actually read
            await using Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[16 * 1024];
            int read;

            while ((read = await stream.ReadAsync(chunk).ConfigureAwait(false)) > 0)
            {
                buffer.Write(chunk, 0, read);

                if (buffer.Length > MaxBytes)
                    return (null, true);
            }

            byte[] data = buffer.ToArray();

            return data.AsSpan().StartsWith(PngSignature) ? (data, false) : (null, true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return (null, false);
        }
    }

    private static Bitmap? TryDecode(string path)
    {
        try
        {
            return new Bitmap(path);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("HunterPie-Linux");

        return client;
    }
}
