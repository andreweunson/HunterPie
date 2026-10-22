using HunterPie.Core.Observability.Logging;

namespace HunterPie.Linux.Logging;

/// <summary>
/// HunterPie's log level setting drops messages before any writer sees them, so to get debug lines into the log files
/// the level is always Debug and the console is filtered here instead: debug lines only with --verbose.
/// </summary>
internal sealed class ConsoleLogFilter(ILogWriter console, bool verbose) : ILogWriter
{
    public void Debug(string message)
    {
        if (verbose)
            console.Debug(message);
    }

    public void Info(string message) => console.Info(message);

    public void Warning(string message) => console.Warning(message);

    public void Native(string message) => console.Native(message);

    public void Error(string message) => console.Error(message);

    public void Benchmark(string message) => console.Benchmark(message);
}
