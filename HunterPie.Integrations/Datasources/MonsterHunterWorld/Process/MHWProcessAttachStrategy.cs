using HunterPie.Core.Address.Map;
using HunterPie.Core.Client;
using HunterPie.Core.Domain.Enums;
using HunterPie.Core.Domain.Process;
using HunterPie.Core.Domain.Process.Entity;
using HunterPie.Core.Domain.Process.Exceptions;
using HunterPie.Core.Domain.Process.Service;
using HunterPie.Core.Extensions;
using HunterPie.Core.Game.Events;

namespace HunterPie.Integrations.Datasources.MonsterHunterWorld.Process;

internal class MHWProcessAttachStrategy : IProcessAttachStrategy
{
    public string Name => SupportedGameNames.MONSTER_HUNTER_WORLD;

    public GameProcessType Game => GameProcessType.MonsterHunterWorld;

    public ProcessStatus Status
    {
        get;
        private set
        {
            if (field == value)
                return;

            ProcessStatus oldStatus = field;
            field = value;

            this.Dispatch(
                StatusChange,
                new SimpleValueChangeEventArgs<ProcessStatus>(oldStatus, value)
            );
        }
    }

    public event EventHandler<SimpleValueChangeEventArgs<ProcessStatus>>? StatusChange;

    public bool CanAttach(ProcessCandidate process)
    {
        if (string.IsNullOrEmpty(process.WindowTitle))
            throw new UnauthorizedAccessException("Failed to read the Monster Hunter World window title, which is where the game version comes from. On Linux install xdotool.");

        if (!process.WindowTitle.ToUpperInvariant().StartsWith("MONSTER HUNTER: WORLD"))
            return false;

        string? rawVersion = process.WindowTitle.Split('(')
            .ElementAtOrDefault(1)
            ?.TrimEnd(')');
        bool isValidVersion = int.TryParse(rawVersion, out int version);

        if (!isValidVersion)
            throw new UnauthorizedAccessException("Failed to get Monster Hunter World version");

        string mapPath = Path.Combine(ClientInfo.AddressPath, $"MonsterHunterWorld.{rawVersion}.map");

        if (!IsIce(version) && !Path.Exists(mapPath))
            throw new UnsupportedGamePatchException(Name, version.ToString());

        bool hasLoaded = IsIce(version)
            ? AddressMap.ParseLatest(ClientInfo.AddressPath)
            : AddressMap.Parse(mapPath);

        if (!hasLoaded)
            throw new Exception($"Failed to load address for Monster Hunter World v{version}");

        return true;
    }

    public void SetStatus(ProcessStatus status) => Status = status;

    private static bool IsIce(int version) => version is >= 300_000 and <= 400_000;
}