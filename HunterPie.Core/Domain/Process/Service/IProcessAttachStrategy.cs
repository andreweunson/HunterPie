using HunterPie.Core.Domain.Process.Entity;

namespace HunterPie.Core.Domain.Process.Service;

public interface IProcessAttachStrategy : IGameWatcher
{
    public bool CanAttach(ProcessCandidate process);

    public void SetStatus(ProcessStatus status);
}
