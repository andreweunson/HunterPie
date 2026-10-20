using HunterPie.DI;
using HunterPie.DI.Module;
using HunterPie.Platforms.Windows.Logging;
using HunterPie.Platforms.Windows.Process;
using HunterPie.Platforms.Windows.Registry;
using HunterPie.Platforms.Windows.Vault;

namespace HunterPie.Platforms.Windows;

internal class WindowsPlatformModule : IDependencyModule
{
    public void Register(IDependencyRegistry registry)
    {
        if (!OperatingSystem.IsWindows())
            return;

        registry
            .WithSingle<WindowsProcessWatcher>()
            .WithSingle<WindowsRegistryAsync>()
            .WithSingle<WindowsCredentialVault>()
            .WithSingle<WindowsNativeLogWriter>();
    }
}