using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using HunterPie.DI;
using HunterPie.Linux.Bootstrap;
using HunterPie.Linux.Overlay.Services;
using HunterPie.Linux.Overlay.ViewModels;
using HunterPie.Linux.Overlay.Views;
using HunterPie.Linux.Services;
using System.Runtime.InteropServices;

namespace HunterPie.Linux.Overlay;

public partial class App : Application
{
    // SIGUSR1 on Linux. .NET's PosixSignal enum accepts raw signal numbers
    private const int SigUsr1 = 10;

    private OverlayWindow? _window;
    private SettingsWindow? _settingsWindow;
    private OverlayViewModel? _viewModel;
    private OverlaySettings? _settings;
    private PosixSignalRegistration? _editSignal;
    private DemoFeed? _demo;
    private GameSessionController? _session;

    internal static OverlayOptions Options { get; set; } = new(false, false, false, false, null);

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _settings = OverlaySettings.Load();

            if (Options.Screen is { } screen)
                _settings.Screen = screen;

            // The command line flag wins and is remembered; it can be switched off in the settings window
            if (Options.OverrideRedirect)
                _settings.OverrideRedirect = true;

            _viewModel = new OverlayViewModel(_settings) { IsEditing = Options.Edit };
            SettingsBinder.Bind(_settings, _viewModel);

            // The settings window is the app; the overlay is a second, click-through window that follows it
            _window = new OverlayWindow(_settings) { DataContext = _viewModel };
            _settingsWindow = new SettingsWindow { DataContext = _viewModel };
            _settingsWindow.EditLayoutRequested += (_, _) => ToggleEditing();

            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
            desktop.MainWindow = _settingsWindow;
            _window.Show();
            desktop.ShutdownRequested += (_, _) => Shutdown();

            _viewModel.Start();

            InstanceControl.Register();
            Console.WriteLine($"Overlay running (pid {Environment.ProcessId}). Layout edit mode: run `./HunterPie.Linux.Overlay --toggle-edit` from another terminal, or `kill -USR1 {Environment.ProcessId}`.");

            _editSignal = PosixSignalRegistration.Create((PosixSignal)SigUsr1, context =>
            {
                context.Cancel = true;
                Dispatcher.UIThread.Post(ToggleEditing);
            });

            if (Options.Demo)
                _demo = new DemoFeed(_viewModel);
            else
                _ = Task.Run(StartHostAsync);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ToggleEditing()
    {
        if (_viewModel is null || _window is null)
            return;

        // Leaving edit mode is what commits the layout
        if (_viewModel.IsEditing)
            _window.SaveSettings();

        _viewModel.IsEditing = !_viewModel.IsEditing;
        _window.ApplyLayout();
    }

    private async Task StartHostAsync()
    {
        try
        {
            await HostBootstrapper.InitializeAsync(new HostOptions(false, false, Options.Verbose, null));

            _session = DependencyContainer.Get<GameSessionController>();
            _session.ReportToConsole = Options.Verbose;
            _session.ContextStarted += (_, context) => _viewModel!.Attach(context);
            _session.ContextEnded += (_, _) => _viewModel!.Detach();
            _session.Start();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to start the game host: {ex}");
        }
    }

    private void Shutdown()
    {
        InstanceControl.Unregister();
        _demo?.Dispose();
        _viewModel?.Stop();
        _viewModel?.Layout.Flush();
        _editSignal?.Dispose();
        _session?.Dispose();
    }
}
