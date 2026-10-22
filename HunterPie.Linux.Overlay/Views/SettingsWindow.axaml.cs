using Avalonia.Controls;
using Avalonia.Interactivity;
using HunterPie.Linux.Overlay.ViewModels;

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
    }

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
