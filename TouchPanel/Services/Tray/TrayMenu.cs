namespace TouchPanel.Services.Tray;

/// <summary>
/// Identifies one command of the tray menu. The M1 menu (RF-005) contains Show/Hide Panel,
/// Open Configuration Folder and Exit; T-12 appends the Edit Mode toggle. The command is a pure
/// data value: <see cref="TrayService"/> only raises it, and the composition root decides what
/// it means.
/// </summary>
public enum TrayMenuCommand
{
    /// <summary>Show the panel when hidden, hide it when visible (RF-005, RF-024).</summary>
    ShowHidePanel,

    /// <summary>Toggle edit mode (RF-005/RF-026), checkable to reflect the current state.</summary>
    EditMode,

    /// <summary>Open the settings window (T-17/RF-092), a deliberate activating window (M2-RF-924).</summary>
    Settings,

    /// <summary>
    /// Open the focused hotkey editor (M3/RF-061), a deliberate activating window (the D-6 analogue,
    /// M3-RF-611/M3-RF-616). Opening it never changes the panel's no-activate behavior.
    /// </summary>
    Hotkeys,

    /// <summary>Open the resolved configuration directory in Explorer (RF-005).</summary>
    OpenConfigurationFolder,

    /// <summary>Terminate the application through the ordered shutdown (RF-007).</summary>
    Exit,
}

/// <summary>
/// One entry of the tray menu model. State is expressed as data so the WPF host renders M1 items
/// and the T-12 Edit Mode toggle with the same code.
/// </summary>
/// <param name="Command">The command raised when the item is selected.</param>
/// <param name="Text">The display text of the item.</param>
/// <param name="IsChecked">Whether the item is currently checked (toggle state).</param>
/// <param name="IsCheckable">Whether the item shows a check mark at all.</param>
/// <param name="IsEnabled">Whether the item can be selected.</param>
public sealed record TrayMenuItem(
    TrayMenuCommand Command,
    string Text,
    bool IsChecked = false,
    bool IsCheckable = false,
    bool IsEnabled = true);

/// <summary>
/// Payload of <see cref="TrayService.MenuRequested"/>: the cursor position reported by the shell
/// when the user clicked the icon.
/// </summary>
public sealed class TrayMenuRequestedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TrayMenuRequestedEventArgs"/> class.
    /// </summary>
    /// <param name="x">Cursor X screen coordinate.</param>
    /// <param name="y">Cursor Y screen coordinate.</param>
    public TrayMenuRequestedEventArgs(int x, int y)
    {
        X = x;
        Y = y;
    }

    /// <summary>Gets the cursor X screen coordinate; 0 for callbacks without position data.</summary>
    public int X { get; }

    /// <summary>Gets the cursor Y screen coordinate; 0 for callbacks without position data.</summary>
    public int Y { get; }
}

/// <summary>Payload of <see cref="TrayService.CommandSelected"/>.</summary>
public sealed class TrayMenuCommandEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TrayMenuCommandEventArgs"/> class.
    /// </summary>
    /// <param name="command">The selected command.</param>
    public TrayMenuCommandEventArgs(TrayMenuCommand command)
    {
        Command = command;
    }

    /// <summary>Gets the selected command.</summary>
    public TrayMenuCommand Command { get; }
}
