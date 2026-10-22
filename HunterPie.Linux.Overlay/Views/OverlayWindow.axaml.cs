using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using HunterPie.Linux.Overlay.Platform;
using HunterPie.Linux.Overlay.Services;
using HunterPie.Linux.Overlay.ViewModels;

namespace HunterPie.Linux.Overlay.Views;

/// <summary>
/// A transparent, always-on-top window covering one monitor. Outside edit mode it takes no input, so the game keeps
/// the mouse and keyboard. In edit mode widgets can be dragged and scaled with the mouse wheel.
/// </summary>
public partial class OverlayWindow : Window
{
    private readonly OverlaySettings _settings;
    private OverlayViewModel ViewModel => (OverlayViewModel)DataContext!;

    // Parameterless constructor for the XAML loader / previewer
    public OverlayWindow() : this(new OverlaySettings())
    {
    }

    public OverlayWindow(OverlaySettings settings)
    {
        _settings = settings;
        InitializeComponent();

        Opened += (_, _) => OnOpened();
        DataContextChanged += (_, _) => WireEditing();
    }

    /// <summary>Re-applies positions, scale and click-through; call after changing edit mode or settings</summary>
    public void ApplyLayout()
    {
        Apply(QuestHost, _settings.Quest);
        Apply(MonstersHost, _settings.Monsters);
        Apply(DamageHost, _settings.Damage);
        UpdateClickThrough();
    }

    public void SaveSettings() => _settings.Save();

    /// <summary>Debug output for input and edit-mode problems, only with --verbose</summary>
    private static void Trace(string message)
    {
        if (App.Options.Verbose)
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}][DEBUG] [Overlay] {message}");
    }

    private void OnOpened()
    {
        CoverScreen();

        if (App.Options.OverrideRedirect)
        {
            if (TryGetPlatformHandle() is { HandleDescriptor: "XID" } handle && X11OverrideRedirect.TryEnable(handle.Handle))
                Console.WriteLine("Override-redirect enabled: the overlay is unmanaged and has no keyboard focus.");
            else
                Console.Error.WriteLine("--override-redirect could not be applied (not an X11/XWayland window?)");
        }

        ApplyLayout();
    }

    /// <summary>Saves the layout and returns to click-through mode</summary>
    private void LeaveEditing()
    {
        if (DataContext is not OverlayViewModel { IsEditing: true } viewModel)
            return;

        SaveSettings();
        viewModel.IsEditing = false;
        ApplyLayout();
    }

    private void OnDoneClick(object? sender, RoutedEventArgs e)
    {
        Trace("Save & exit button clicked");
        LeaveEditing();
    }

    private void OnQuitClick(object? sender, RoutedEventArgs e)
    {
        Trace("Quit button clicked");
        QuitApplication();
    }

    private static void QuitApplication() =>
        (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape)
            LeaveEditing();
        else if (e.Key == Key.Q && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            QuitApplication();
    }

    private void WireEditing()
    {
        if (DataContext is not OverlayViewModel viewModel)
            return;

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OverlayViewModel.IsEditing))
                UpdateClickThrough();
        };

        MakeDraggable(QuestHost, _settings.Quest);
        MakeDraggable(MonstersHost, _settings.Monsters);
        MakeDraggable(DamageHost, _settings.Damage);
    }

    private void CoverScreen()
    {
        IReadOnlyList<Screen> screens = Screens.All;

        if (screens.Count == 0)
            return;

        Screen screen = _settings.Screen < screens.Count ? screens[_settings.Screen] : Screens.Primary ?? screens[0];
        double scaling = screen.Scaling > 0 ? screen.Scaling : 1;

        Position = screen.Bounds.Position;
        Width = screen.Bounds.Width / scaling;
        Height = screen.Bounds.Height / scaling;

        Trace($"Covering screen {screens.ToList().IndexOf(screen)} of {screens.Count}: {screen.Bounds.Width}x{screen.Bounds.Height} at {screen.Bounds.X},{screen.Bounds.Y}, scaling {scaling}, handle {TryGetPlatformHandle()?.HandleDescriptor ?? "none"}");
    }

    private static void Apply(Control host, WidgetPlacement placement)
    {
        Canvas.SetLeft(host, placement.X);
        Canvas.SetTop(host, placement.Y);
        host.Opacity = placement.Opacity;
        host.RenderTransformOrigin = RelativePoint.TopLeft;
        host.RenderTransform = new ScaleTransform(placement.Scale, placement.Scale);
    }

    private void UpdateClickThrough()
    {
        bool editing = DataContext is OverlayViewModel { IsEditing: true };
        Trace($"Edit mode {(editing ? "on" : "off")}, input region {(editing ? "interactive" : "click-through")}");

        // ShowActivated=false keeps the game focused when the overlay appears; edit mode needs real input though
        if (editing)
            Activate();

        if (TryGetPlatformHandle() is not { } handle || handle.HandleDescriptor != "XID")
            return;

        if (!X11InputShape.TrySetClickThrough(handle.Handle, clickThrough: !editing))
            Console.Error.WriteLine("Could not change the overlay's input region (libXext missing?), clicks may not pass through");
    }

    private void MakeDraggable(Control host, WidgetPlacement placement)
    {
        Point? grab = null;
        string name = host.Name ?? "widget";

        void EndDrag(string reason)
        {
            if (grab is null)
                return;

            grab = null;
            Trace($"Drag of {name} ended ({reason}) at {placement.X:0},{placement.Y:0}");
            SaveSettings();
        }

        host.PointerPressed += (_, e) =>
        {
            PointerPointProperties properties = e.GetCurrentPoint(host).Properties;
            Trace($"Pointer pressed on {name}: left={properties.IsLeftButtonPressed}, editing={ViewModel.IsEditing}");

            if (!ViewModel.IsEditing || !properties.IsLeftButtonPressed)
                return;

            grab = e.GetPosition(this) - new Point(placement.X, placement.Y);
            e.Pointer.Capture(host);
            e.Handled = true;
        };

        host.PointerMoved += (_, e) =>
        {
            if (grab is not { } offset)
                return;

            // A release that never reached us (focus change, grab stolen) must not leave the widget glued to the cursor
            if (!e.GetCurrentPoint(host).Properties.IsLeftButtonPressed)
            {
                EndDrag("button no longer pressed");
                return;
            }

            Point position = e.GetPosition(this) - offset;
            placement.X = Math.Clamp(position.X, 0, Math.Max(0, Bounds.Width - 40));
            placement.Y = Math.Clamp(position.Y, 0, Math.Max(0, Bounds.Height - 40));
            Canvas.SetLeft(host, placement.X);
            Canvas.SetTop(host, placement.Y);
        };

        host.PointerReleased += (_, e) =>
        {
            if (grab is null)
                return;

            EndDrag("released");
            e.Pointer.Capture(null);
        };

        host.PointerCaptureLost += (_, _) => EndDrag("pointer capture lost");

        host.PointerWheelChanged += (_, e) =>
        {
            if (!ViewModel.IsEditing)
                return;

            placement.Scale = Math.Clamp(Math.Round(placement.Scale + Math.Sign(e.Delta.Y) * 0.05, 2), 0.4, 3.0);
            Apply(host, placement);
            SaveSettings();
            e.Handled = true;
        };
    }
}
