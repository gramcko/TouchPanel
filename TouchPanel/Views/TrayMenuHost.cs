using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using TouchPanel.Interop;
using TouchPanel.Services.Tray;

namespace TouchPanel.Views;

/// <summary>
/// Presentation-only host for the tray context menu (plan section 1.1): renders the
/// UI-framework-agnostic menu model from <see cref="TrayService"/> as a WPF
/// <see cref="ContextMenu"/> when the icon raises a menu request, and forwards the selected
/// command back to the service. All WPF objects are created on the UI thread.
/// <para>
/// The menu is user-initiated (a click on the tray icon). Opening it must never activate the panel
/// (RF-018/RF-024); it may foreground only the hidden, non-activating <b>tray</b> window so the
/// popup can receive the click that dismisses it. Command handling is owned by the composition
/// root.
/// </para>
/// </summary>
public sealed class TrayMenuHost : IDisposable
{
    private readonly TrayService _trayService;
    private readonly Dispatcher _dispatcher;
    private ContextMenu? _openMenu;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrayMenuHost"/> class and subscribes to the
    /// tray menu requests. Must be created on the UI thread (or given that thread's dispatcher).
    /// </summary>
    /// <param name="trayService">The tray service whose model and events this host renders.</param>
    /// <param name="dispatcher">UI dispatcher used to marshal menu requests; defaults to the current dispatcher.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="trayService"/> is <see langword="null"/>.</exception>
    public TrayMenuHost(TrayService trayService, Dispatcher? dispatcher = null)
    {
        ArgumentNullException.ThrowIfNull(trayService);

        _trayService = trayService;
        _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
        _trayService.MenuRequested += OnMenuRequested;
    }

    /// <summary>
    /// Unsubscribes and closes an open menu. Idempotent and never throws; called from the ordered
    /// shutdown, so it must not block.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _trayService.MenuRequested -= OnMenuRequested;

        try
        {
            if (_dispatcher.CheckAccess())
            {
                CloseMenu();
            }
            else
            {
                _dispatcher.BeginInvoke(CloseMenu);
            }
        }
        catch (Exception)
        {
            // The dispatcher may already be shutting down; an abandoned menu dies with the process.
        }
    }

    /// <summary>
    /// Builds the WPF menu from the model. Internal so the mapping (headers, checked/disabled
    /// state, command dispatch) is unit-testable without opening a native menu.
    /// </summary>
    /// <param name="items">Menu items in display order.</param>
    /// <param name="onCommandSelected">Callback invoked with the command when an item is clicked.</param>
    /// <returns>A closed, ready-to-open context menu.</returns>
    internal static ContextMenu BuildMenu(
        IReadOnlyList<TrayMenuItem> items,
        Action<TrayMenuCommand> onCommandSelected)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(onCommandSelected);

        var menu = new ContextMenu();

        foreach (var item in items)
        {
            var menuItem = new MenuItem
            {
                Header = item.Text,
                IsChecked = item.IsChecked,
                IsCheckable = item.IsCheckable,
                IsEnabled = item.IsEnabled,
            };

            var command = item.Command;
            menuItem.Click += (_, _) => onCommandSelected(command);
            menu.Items.Add(menuItem);
        }

        return menu;
    }

    private void OnMenuRequested(object? sender, TrayMenuRequestedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (_dispatcher.CheckAccess())
        {
            ShowMenu();
            return;
        }

        // The tray event normally arrives on the UI thread already; marshal defensively so a
        // different pumping thread can never build WPF objects off the UI thread.
        _dispatcher.BeginInvoke(() =>
        {
            if (!_disposed)
            {
                ShowMenu();
            }
        });
    }

    private void ShowMenu()
    {
        CloseMenu();

        var menu = BuildMenu(_trayService.GetMenuItems(), _trayService.SelectCommand);
        menu.Placement = PlacementMode.MousePoint;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_openMenu, menu))
            {
                _openMenu = null;
            }
        };

        _openMenu = menu;

        // Standard tray-menu pattern: make the icon's hidden window the foreground window so the
        // popup can receive the dismissing click; without it the menu may not open or may not
        // close. The window is WS_EX_NOACTIVATE, so the panel never activates (RF-018/RF-024);
        // only the user-initiated tray click transfers foreground. PlacementMode.MousePoint is
        // used because the v4 callback coordinates are physical pixels while a WPF popup offset
        // expects device-independent units; the cursor is already on the icon for a user click.
        TrayMenuNative.SetForegroundWindow(_trayService.WindowHandle);
        menu.IsOpen = true;
    }

    private void CloseMenu()
    {
        var menu = _openMenu;
        _openMenu = null;

        if (menu is not null)
        {
            menu.IsOpen = false;
        }
    }
}
