namespace HunterPie.Core.Domain.Process.Entity;

/// <summary>
/// Platform independent snapshot of a running process that might be a supported game.
/// Each platform watcher is responsible for filling this in, since things like window titles and
/// file versions are obtained differently on Windows and on Linux (Wine/Proton).
/// </summary>
/// <param name="Id">OS process id</param>
/// <param name="Name">Executable name without extension</param>
/// <param name="WindowTitle">Title of the main window, empty when not available</param>
/// <param name="FileVersion">Version resource of the main executable, null when not available</param>
/// <param name="BaseAddress">Address the main executable image is mapped at</param>
public sealed record ProcessCandidate(
    int Id,
    string Name,
    string WindowTitle,
    string? FileVersion,
    nint BaseAddress
);
