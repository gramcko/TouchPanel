# TouchPanel

A borderless, touch-first control panel for a small secondary touchscreen on Windows (Stream-Deck-like) that never steals focus from games or apps.

TouchPanel renders a configurable grid of touch widgets on a monitor of your choice and drives them with a real action engine and live PC telemetry. It is built as a WPF `.NET 10` desktop utility: lightweight, tray-resident, config-driven, and designed so that operating it never pulls keyboard/mouse focus away from the application you are actually using.

## Features

- **Borderless fullscreen panel** on a configurable monitor (full monitor bounds, including the taskbar area).
- **Never activates / never steals focus** — `WS_EX_NOACTIVATE` + `WS_EX_TOOLWINDOW`, `ShowActivated=false`, and no `SetForegroundWindow`/`Activate` in normal mode. Not in the taskbar or Alt-Tab.
- **Touch widgets**: Button, Toggle, Label, Gauge, Bar, Sparkline, Slider, Clock, ImageButton, Image.
- **Actions**: launch programs; send key combinations (e.g. `Alt+A`); media keys; volume; mute; run a hotkey.
- **Global hotkeys** that fire while a game has focus and do not steal focus.
- **Live telemetry** at ~1 Hz (CPU/GPU/RAM/disk/network, temperatures and fan speeds via LibreHardwareMonitor — optional), with a configurable polling interval.
- **Themes** (dark / light / high-contrast) plus accent colour and icon packs, with per-widget appearance overrides.
- **Edit mode**: add/remove/duplicate widgets, move/resize, property and action editors with executable browse, and grid snap with alignment guides.
- **System tray** lifecycle with Show/Hide Panel, Edit Mode, Settings…, Hotkeys…, Open Configuration Folder, and Exit.
- **Start with Windows** (per-user, no elevation) and **single instance** (`--exit`/second launch signals the running instance).
- **JSON configuration** with schema versioning, ordered migrations, atomic save, and backup/recovery.
- **Hot reload** — edit `panel.json` while the app runs and valid changes apply live.
- **Import / export** configuration with validation and automatic backup.
- **No elevation required** — core operation runs as a standard user and never prompts for UAC.

## Requirements

- **Windows 10 21H2+ or Windows 11, x64.**
- **.NET 10 SDK** to build from source.
- **No administrator rights** needed to build, run, or use the app.

> Note: some CPU temperature and fan sensors are only readable by LibreHardwareMonitor when the process is elevated. Because TouchPanel never elevates, those specific sensors may be reported as **"Unavailable"**. All core functionality — the panel, widgets, actions, global hotkeys, and the rest of the telemetry — works without elevation.

## Install / build

If `dotnet` is on your `PATH`, run the commands directly. Otherwise use the full path to the SDK, e.g. `& "C:\Program Files (x86)\dotnet\dotnet.exe" ...`.

Build the solution:

```powershell
dotnet build TouchPanel.sln -c Release
```

Run the development build:

```text
TouchPanel\bin\Release\net10.0-windows\TouchPanel.exe
```

Self-contained, single-file publish (no .NET install needed on the target machine):

```powershell
dotnet publish TouchPanel\TouchPanel.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

This produces a single `TouchPanel.exe` in the publish output folder; run it directly.

Run the test suite:

```powershell
dotnet test TouchPanel.sln
```

## Usage

### Tray menu

While TouchPanel is running it shows a notification-area icon with:

- **Show/Hide Panel**
- **Edit Mode** (checkable toggle)
- **Settings…**
- **Hotkeys…**
- **Open Configuration Folder**
- **Exit**

Closing the panel window (for example `Alt+F4`) **hides it to the tray** rather than quitting. TouchPanel keeps running in the background; use the tray **Exit** action to terminate it.

### Pinning your touchscreen

By default the panel targets the primary monitor. To pin it to your touchscreen, edit `%APPDATA%\TouchPanel\panel.json`:

```json
{
  "monitor": {
    "match": { "hardwareId": "..." },
    "fallback": "none"
  }
}
```

`monitor.match` accepts stable identity fields such as `hardwareId` (EDID manufacturer + product + serial), `devicePath`, or `friendlyName`. Matching uses those identity fields first (never a bare monitor index). Alternatively use **Settings… → monitor picker** to select the target without hand-editing JSON. When no descriptor matches, the panel follows the `fallback` policy (`none` keeps it hidden with a notification; `primary` shows it on the primary monitor with a notification) — never silently on an unexpected monitor.

### Edit Mode

Enter Edit Mode from the tray, by **long-pressing a widget**, or via the edit toolbar. In Edit Mode you can:

- move and resize widgets (touch or mouse);
- add, remove, and duplicate widgets from the palette;
- edit the label, colours, icon, action, and sensor-bound properties;
- use grid snap and alignment guides.

Leave Edit Mode with the **Exit Edit Mode** button or press `Esc`. Layout changes persist on exit. Tapping widgets while editing selects/moves them and does not fire their actions.

### Actions

Each widget binds to exactly one action:

- **Launch program** — a discrete executable path (with a **Browse** button), plus argument list and optional working directory.
- **Send key combination** — for example `Alt+A`, delivered to the foreground application.
- **Media key** — play/pause, next, previous, stop.
- **Volume** — set level or step up/down.
- **Mute** — toggle.
- **Run hotkey** — trigger a configured hotkey.

### Global hotkeys

Create global hotkeys in the **Hotkeys…** window. Each hotkey requires at least one modifier plus a key, and binds to one action. They work while a game or another application has focus and do not steal focus.

### Widgets and sensors

Gauges, Bars, and Sparklines pick a **sensor from a list**. Byte/rate sensors offer a display unit selector (Auto / B / KB / MB / GB). Sparklines can show or hide the current value. The Image widget displays a **PNG** loaded from a path chosen with **Browse**.

### Appearance

Choose a theme (dark / light / high-contrast), an accent colour, and an icon pack app-wide, with per-widget appearance overrides on top.

### Configuration and logs

```text
%APPDATA%\TouchPanel\panel.json          # user-editable configuration
%APPDATA%\TouchPanel\state.json          # runtime state (e.g. toggle persistence)
%APPDATA%\TouchPanel\panel.backup.json   # last known valid backup
%LOCALAPPDATA%\TouchPanel\logs           # structured rotating logs
```

Invalid JSON is preserved as `panel.corrupt-<timestamp>.json` and the app recovers from the backup or generated defaults, so a bad edit never destroys a working panel. Saves are atomic. Editing `panel.json` while the app runs triggers a **hot reload** (validated as a whole; on failure the running configuration is kept).

**Start with Windows** is opt-in and per-user (no elevation).

## Project layout

- `TouchPanel/` — the application.
  - `Views/` — XAML windows, overlays, and pickers.
  - `ViewModels/` — presentation state and per-widget logic.
  - `Widgets/` — widget templates, registry, and controls.
  - `Actions/` — WPF-free action execution engine.
  - `Input/` — global hotkeys and keyboard input.
  - `Monitoring/` — telemetry service and the LibreHardwareMonitor adapter.
  - `Config/` — configuration model, validation, migrations, and store.
  - `Interop/` — isolated Win32 P/Invoke (window styles, tray, hotkeys, input).
  - `Services/` — lifecycle, tray, single instance, startup registration, settings, and reload.
  - `Theme/` — themes, colours, and styles.
- `TouchPanel.Tests/` — the xUnit test suite.
- `specs/` — product specifications, plans, and tasks.

## Contributing / notes

- This is a **personal utility**; there is no public support commitment.
- **Windows-only**, x64, with no elevation and no UAC prompt.
- **Configuration is untrusted input**: it is validated and treated strictly as data, and no configuration value can trigger execution beyond the explicitly modelled action types.
- Widgets, actions, telemetry, and configuration are layered so the WPF-free core (`Actions/`, `Monitoring/`, `Config/`, `Input/`) stays testable without hardware, drivers, or elevation.
