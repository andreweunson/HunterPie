using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Interactivity;
using HunterPie.Linux.Logging;
using HunterPie.Linux.Overlay.ViewModels;
using System.Diagnostics;

namespace HunterPie.Linux.Overlay.Views;

/// <summary>
/// The app's main window and the one place widget options live. Closing it quits HunterPie (and the overlay with it).
/// </summary>
public partial class SettingsWindow : Window
{
    /// <summary>Asks the app to toggle layout edit mode</summary>
    public event EventHandler? EditLayoutRequested;

    public SettingsWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => WatchEditing();
        Opened += (_, _) =>
        {
            FillMonitors();
            ShowLogLocation();
        };
    }

    private bool _fillingMonitors;

    private OverlayViewModel? ViewModel => DataContext as OverlayViewModel;

    private void FillMonitors()
    {
        IReadOnlyList<Screen> screens = Screens.All;

        _fillingMonitors = true;
        MonitorBox.Items.Clear();

        for (int i = 0; i < screens.Count; i++)
        {
            Screen screen = screens[i];
            MonitorBox.Items.Add($"Monitor {i + 1}: {screen.Bounds.Width}x{screen.Bounds.Height}{(screen.IsPrimary ? " (primary)" : "")}");
        }

        // The overlay falls back to the primary monitor when the saved one is gone
        int selected = ViewModel?.Layout.Screen ?? 0;
        if (selected >= screens.Count)
            selected = Math.Max(0, screens.ToList().FindIndex(it => it.IsPrimary));

        MonitorBox.SelectedIndex = screens.Count == 0 ? -1 : selected;
        _fillingMonitors = false;
    }

    private void OnMonitorChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_fillingMonitors && ViewModel is { } viewModel && MonitorBox.SelectedIndex >= 0)
            viewModel.Layout.Screen = MonitorBox.SelectedIndex;
    }

    private void OnAddHiddenAilment(object? sender, RoutedEventArgs e) => AddHiddenAilment();

    private void OnHiddenAilmentKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key != Avalonia.Input.Key.Enter)
            return;

        AddHiddenAilment();
        e.Handled = true;
    }

    private void AddHiddenAilment()
    {
        if (ViewModel is not { } viewModel)
            return;

        viewModel.MonsterOptions.Blacklist.Add(HiddenAilmentBox.Text);
        HiddenAilmentBox.Text = string.Empty;
    }

    private void OnRemoveHiddenAilment(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && (sender as Button)?.Tag is string pattern)
            viewModel.MonsterOptions.Blacklist.Remove(pattern);
    }

    private void OnResetHiddenAilments(object? sender, RoutedEventArgs e) => ViewModel?.MonsterOptions.Blacklist.ResetToDefaults();

    private string? _logDirectory;

    private void ShowLogLocation()
    {
        _logDirectory = LogLocation.Directory;
        LogsSection.IsVisible = App.Options.WriteLogs && !App.Options.Demo;
        LogPath.Text = _logDirectory;
    }

    private void OnOpenLogsClick(object? sender, RoutedEventArgs e)
    {
        if (_logDirectory is null)
            return;

        try
        {
            Process.Start(new ProcessStartInfo("xdg-open", [_logDirectory]) { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            LogPath.Text = $"{_logDirectory}\n(xdg-open is not available)";
        }
    }

    private void OnResetClick(object? sender, RoutedEventArgs e) => ViewModel?.Layout.ResetLayout();

    private void WatchEditing()
    {
        if (DataContext is not OverlayViewModel viewModel)
            return;

        UpdateEditButton(viewModel.IsEditing);
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OverlayViewModel.IsEditing))
                UpdateEditButton(viewModel.IsEditing);
        };
    }

    private void UpdateEditButton(bool editing) =>
        EditButton.Content = editing ? "Save layout and exit edit mode" : "Edit widget layout";

    private void OnEditClick(object? sender, RoutedEventArgs e) => EditLayoutRequested?.Invoke(this, EventArgs.Empty);

    private void OnQuitClick(object? sender, RoutedEventArgs e) => Close();
}
