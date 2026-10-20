using HunterPie.DI;
using HunterPie.DI.Module;
using HunterPie.Platforms.Linux.Logging;
using HunterPie.Platforms.Linux.Process;
using HunterPie.Platforms.Linux.Storage;

namespace HunterPie.Platforms.Linux;

internal class LinuxPlatformModule : IDependencyModule
{
    public void Register(IDependencyRegistry registry)
    {
        if (!OperatingSystem.IsLinux())
            return;

        registry
            .WithSingle<LinuxProcessWatcher>()
            .WithSingle<LinuxLocalRegistry>()
            .WithSingle<LinuxCredentialVault>()
            .WithSingle<LinuxNativeLogWriter>();
    }
}
