using HunterPie.Core.Observability.Logging;

namespace HunterPie.Linux.Logging;

/// <summary>
/// Receives everything HunterPie logs, debug included, and puts it in the session log (and the hunt log while one is
/// open). Registered next to the console writer, which decides for itself how much to print.
/// </summary>
internal sealed class FileLogWriter(LogSink sink) : ILogWriter
{
    public void Debug(string message) => Write("DEBUG", message);

    public void Info(string message) => Write("INFO", message);

    public void Warning(string message) => Write("WARN", message);

    public void Native(string message) => Write("NATIVE", message);

    public void Error(string message) => Write("ERROR", message);

    public void Benchmark(string message) => Write("BENCH", message);

    private void Write(string level, string message)
    {
        // The recorder logs the same game events (with the quest clock), so the console reporter's copy is left out
        if (message.StartsWith("[ConsoleReporter]", StringComparison.Ordinal))
            return;

        sink.Write($"[{DateTime.Now:HH:mm:ss.fff}][{level}] {message}");
    }
}
