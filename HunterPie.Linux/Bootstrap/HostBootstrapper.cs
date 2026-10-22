using HunterPie.Core.Client;
using HunterPie.Core.Client.Configuration.Enums;
using HunterPie.Core.Domain.Interfaces;
using HunterPie.Core.Domain.Mapper;
using HunterPie.Core.Domain.Mapper.Internal;
using HunterPie.Core.Observability.Logging;
using HunterPie.DI;
using HunterPie.DI.Module;
using HunterPie.DI.Registry;
using HunterPie.Integrations.Datasources.MonsterHunterRise.Services;
using HunterPie.Linux.Logging;
using HunterPie.Platforms.Common.Logging;
using System.Reflection;
using System.Security.Cryptography;

namespace HunterPie.Linux.Bootstrap;

/// <summary>
/// Headless equivalent of the WPF client's App.OnStartup + InitializerManager. Order matters and mirrors it:
/// mappers, DI modules, local registry, config, then logging.
/// </summary>
internal static class HostBootstrapper
{
    /// <summary>The log files of this run, or null with --no-logs or when the log directory cannot be used</summary>
    public static LogSink? LogSink { get; private set; }

    public static async Task InitializeAsync(HostOptions options)
    {
        RegisterMappers();
        LoadModules();
        await InitializeLocalRegistryAsync();

        ClientConfig.Initialize();
        ConfigManager.Initialize();

        // HunterPie's log level drops messages before any writer sees them, so it stays at Debug and the console writer
        // does its own filtering (debug lines only with --verbose). That is what lets the log files hold everything.
        ClientConfig.Config.Development.ClientLogLevel.Value = LogLevel.Debug;

        // Log output goes to the terminal, via the Linux writer registered by the platform module
        LoggerFactory.Add(new ConsoleLogFilter(DependencyContainer.Get<INativeLogWriter>(), options.Verbose));

        if (options.WriteLogs)
        {
            var sink = new LogSink(LogLocation.Directory, DateTime.Now);

            if (sink.SessionPath is not null)
            {
                LogSink = sink;
                LoggerFactory.Add(new FileLogWriter(sink));
                LoggerFactory.Create().Info($"Writing logs to {sink.Directory}");
            }
        }

        if (!File.Exists(Path.Combine(ClientInfo.LanguagesPath, "en-us.xml")))
            LoggerFactory.Create().Warning(
                "Localization files were not found, so monster and quest names will show as ids. " +
                "Run `git submodule update --init` and rebuild.");
    }

    // Same set as the WPF client's MapperFactoryInitializer
    private static void RegisterMappers()
    {
        MapFactory.Add(new GameTypeToGameProcessMapper());
        MapFactory.Add(new XmlNodeToAbnormalityDefinitionMapper(new MHRAbnormalityFlagTypeParser()));
        MapFactory.Add(new GameProcessToGameTypeMapper());
        MapFactory.Add(new XmlNodeToMonsterDefinitionMapper());
        MapFactory.Add(new XmlNodeToMonsterPartDefinitionMapper());
        MapFactory.Add(new XmlNodeToMonsterSizeDefinitionMapper());
        MapFactory.Add(new XmlNodeToAilmentDefinitionMapper());
    }

    // Same discovery as the WPF client's DependencyProvider, limited to the assemblies that matter
    private static void LoadModules()
    {
        Assembly[] assemblies =
        [
            typeof(ClientInfo).Assembly,
            Assembly.Load("HunterPie.Integrations"),
            Assembly.Load("HunterPie.Platforms"),
            typeof(HostBootstrapper).Assembly
        ];

        var registry = new DependencyRegistry();

        IEnumerable<IDependencyModule> modules = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(IDependencyModule).IsAssignableFrom(type) && type is { IsInterface: false, IsAbstract: false })
            .Select(type => (IDependencyModule)Activator.CreateInstance(type)!);

        foreach (IDependencyModule module in modules)
            module.Register(registry);

        DependencyContainer.SetRegistry(registry);
    }

    // Same as the WPF client's LocalConfigInitializer: the secret is what encrypts Secret values in config.json
    private static async Task InitializeLocalRegistryAsync()
    {
        var registry = DependencyContainer.Get<ILocalRegistryAsync>();

        if (!await registry.ExistsAsync("secret"))
            await registry.SetAsync("secret", RandomNumberGenerator.GetBytes(16));

        if (!await registry.ExistsAsync("client_id"))
            await registry.SetAsync("client_id", Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));
    }
}
