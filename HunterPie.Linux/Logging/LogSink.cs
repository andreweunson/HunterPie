namespace HunterPie.Linux.Logging;

/// <summary>
/// The two files everything is written to: the session log (always) and the current hunt's log (between "quest
/// started" and "quest ended"). Lines written after entering a hunting zone but before the quest clock starts are kept
/// in memory and put at the top of the hunt file, because the fight usually begins before the quest does.
/// Files are flushed on every line so a crash or `tail -f` sees everything. A failing disk never throws into the game
/// reading code: the file is dropped and one warning goes to stderr.
/// </summary>
internal sealed class LogSink : IDisposable
{
    private const int PreHuntCapacity = 500;

    private readonly Lock _lock = new();
    private readonly string _directory;
    private readonly Queue<string> _preHunt = new();
    private StreamWriter? _session;
    private StreamWriter? _hunt;
    private bool _buffering;
    private int _dropped;

    public string Directory => _directory;

    public string? SessionPath { get; private set; }

    public string? HuntPath { get; private set; }

    public bool IsHuntOpen
    {
        get { lock (_lock) return _hunt is not null; }
    }

    public LogSink(string directory, DateTime start)
    {
        _directory = directory;

        try
        {
            System.IO.Directory.CreateDirectory(directory);
            LogLocation.Prune(directory);
            SessionPath = LogLocation.SessionFile(directory, start);
            _session = Open(SessionPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Warn($"Could not create the log directory {directory} ({ex.Message}), log files are off");
        }
    }

    /// <summary>
    /// While on, lines are remembered for the next hunt file as well as written to the session log. Turn it on when the
    /// player is in a hunting zone without a quest running, off otherwise (which forgets them).
    /// </summary>
    public void SetBuffering(bool on)
    {
        lock (_lock)
        {
            if (!on)
                _preHunt.Clear();

            _buffering = on;
        }
    }

    public void Write(string line)
    {
        lock (_lock)
        {
            Append(ref _session, line);

            if (_hunt is not null)
            {
                Append(ref _hunt, line);
            }
            else if (_buffering)
            {
                if (_preHunt.Count >= PreHuntCapacity)
                {
                    _preHunt.Dequeue();
                    _dropped++;
                }

                _preHunt.Enqueue(line);
            }
        }
    }

    /// <summary>Starts the hunt file with a header and the lines buffered since the hunting zone was entered</summary>
    public string? BeginHunt(DateTime start, string questName, IEnumerable<string> header)
    {
        lock (_lock)
        {
            EndHuntCore();

            try
            {
                HuntPath = LogLocation.HuntFile(_directory, start, questName);
                _hunt = Open(HuntPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Warn($"Could not create the hunt log ({ex.Message})");
                HuntPath = null;
                return null;
            }

            foreach (string line in header)
                Append(ref _hunt, line);

            if (_preHunt.Count > 0 || _dropped > 0)
            {
                Append(ref _hunt, $"--- before the quest clock started ({_preHunt.Count} lines{(_dropped > 0 ? $", {_dropped} older ones left out" : "")}) ---");

                foreach (string line in _preHunt)
                    Append(ref _hunt, line);

                Append(ref _hunt, "--- quest started ---");
            }

            _preHunt.Clear();
            _dropped = 0;
            _buffering = false;

            return HuntPath;
        }
    }

    /// <summary>
    /// Adds lines to the session log and to a hunt file that has already been closed (what the My Hunt Report mod saves
    /// a few seconds after the quest ends). A hunt file that is gone is skipped.
    /// </summary>
    public void AppendAfterHunt(string? huntPath, IEnumerable<string> lines)
    {
        string[] all = [string.Empty, .. lines];

        lock (_lock)
        {
            foreach (string line in all)
                Append(ref _session, line);

            if (huntPath is null || !File.Exists(huntPath))
                return;

            try
            {
                File.AppendAllLines(huntPath, all);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Warn($"Could not add to the hunt log ({ex.Message})");
            }
        }
    }

    /// <summary>Appends the summary to the hunt file (and the session log) and closes the hunt file</summary>
    public void EndHunt(IEnumerable<string> summary)
    {
        lock (_lock)
        {
            foreach (string line in summary)
            {
                Append(ref _hunt, line);
                Append(ref _session, line);
            }

            EndHuntCore();
        }
    }

    private void EndHuntCore()
    {
        _hunt?.Dispose();
        _hunt = null;
    }

    private static StreamWriter Open(string path) =>
        new(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read), new System.Text.UTF8Encoding(false)) { AutoFlush = true };

    private static void Append(ref StreamWriter? writer, string line)
    {
        if (writer is null)
            return;

        try
        {
            writer.WriteLine(line);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            Warn($"Writing the log failed ({ex.Message}), this log file is off");
            writer = null;
        }
    }

    private static void Warn(string message) => Console.Error.WriteLine($"[HunterPie] {message}");

    public void Dispose()
    {
        lock (_lock)
        {
            EndHuntCore();
            _session?.Dispose();
            _session = null;
        }
    }
}
