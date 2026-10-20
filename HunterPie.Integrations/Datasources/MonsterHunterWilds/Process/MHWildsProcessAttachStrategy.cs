using HunterPie.Core.Address.Map;
using HunterPie.Core.Client;
using HunterPie.Core.Domain.Enums;
using HunterPie.Core.Domain.Process;
using HunterPie.Core.Domain.Process.Entity;
using HunterPie.Core.Domain.Process.Exceptions;
using HunterPie.Core.Domain.Process.Service;
using HunterPie.Core.Extensions;
using HunterPie.Core.Game.Events;

namespace HunterPie.Integrations.Datasources.MonsterHunterWilds.Process;

internal class MHWildsProcessAttachStrategy : IProcessAttachStrategy
{
    public string Name => SupportedGameNames.MONSTER_HUNTER_WILDS;

    public GameProcessType Game => GameProcessType.MonsterHunterWilds;

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
        // The title is empty on Linux when no window inspector is available, the executable name already matched in that case
        if (process.WindowTitle.Length > 0 && !process.WindowTitle.ToUpperInvariant().StartsWith("MONSTER HUNTER WILDS"))
            return false;

        string? version = process.FileVersion;

        if (version is not { })
            throw new UnauthorizedAccessException("Failed to get Monster Hunter Wilds version, missing permissions");

        string mapPath = Path.Combine(ClientInfo.AddressPath, $"MonsterHunterWilds.{version}.map");

        if (!Path.Exists(mapPath))
            throw new UnsupportedGamePatchException(Name, version);

        bool hasLoaded = AddressMap.Parse(
            filePath: mapPath
        );

        if (!hasLoaded)
            throw new Exception($"Failed to load address for Monster Hunter Wilds v{version}");

        return true;
    }

    public void SetStatus(ProcessStatus status) => Status = status;
}