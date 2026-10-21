using HunterPie.Core.Client;
using HunterPie.Core.Crypto;
using HunterPie.DI;
using HunterPie.DI.Module;
using HunterPie.Features.Scan.Service;
using HunterPie.Linux.Services;

namespace HunterPie.Linux.Bootstrap;

/// <summary>
/// The pieces the WPF client registers in HunterPieModule, ScanModule and LocalizationModule that do not need a UI.
/// </summary>
internal sealed class HostModule : IDependencyModule
{
    public void Register(IDependencyRegistry registry)
    {
        registry
            .WithSingle<CryptoService>()
            .WithSingle<ScanService>()
            .WithSingle<HeadlessLocalizationRepository>()
            .WithSingle<GameSessionController>()
            .WithSingle(static _ => ClientConfig.Config);
    }
}
