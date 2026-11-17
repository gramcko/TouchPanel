# TouchPanel

A borderless, touch-first control panel for a small secondary touchscreen on Windows (Stream-Deck-like) that never steals focus from games or apps.

TouchPanel renders a configurable grid of touch widgets on a monitor of your choice and drives them with a real action engine and live PC telemetry. It is built as a WPF `.NET 10` desktop utility: lightweight, tray-resident, config-driven, and designed so that operating it never pulls keyboard/mouse focus away from the application you are actually using.

Latest release: [github.com/gramcko/TouchPanel/releases](https://github.com/gramcko/TouchPanel/releases).

## Features

- **Borderless fullscreen panel** on a configurable monitor (full monitor bounds, including the taskbar area).
- **Never activates / never steals focus** — `WS_EX_NOACTIVATE` + `WS_EX_TOOLWINDOW`, `ShowActivated=false`, and no `SetForegroundWindow`/`Activate` in normal mode. Not in the taskbar or Alt-Tab.
- **Touch widgets**: Button, Toggle, Label, Gauge, Bar, Sparkline, Slider, Clock, ImageButton, Image.
- **Pages**: multiple pages switched by horizontal swipe or the pager (tabs, dots, and an `n / N` counter); the active page is preserved by id.
- **Page transitions**: selectable slide, fade, slide-up, or zoom animation on page changes, with `none` to disable.
- **Per-page background**: optional solid colour and/or image behind each page's widgets.
- **Actions**: launch programs; send key combinations (e.g. `Alt+A`); media keys; volume; mute; run a hotkey.
- **Global hotkeys** that fire while a game has focus and do not steal focus.
- **Live telemetry** at ~1 Hz (CPU/GPU/RAM/disk/network, temperatures and fan speeds via LibreHardwareMonitor — optional), with a configurable polling interval.
- **Themes** (dark / light / high-contrast / midnight / nord / OLED) plus accent colour and icon packs, with per-widget appearance overrides and app-wide themed scrollbars.
- **Edit mode**: add/remove/duplicate widgets, move/resize, property and action editors with executable browse, grid snap with alignment guides, and undo/redo for widget edits.
- **System tray** lifecycle with Show/Hide Panel, Edit Mode, Settings…, Hotkeys…, About TouchPanel…, Open Configuration Folder, and Exit.
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

## First run

On the **first launch** — when `%APPDATA%\TouchPanel\panel.json` does not exist yet — TouchPanel opens a **setup window** instead of placing the panel immediately:

- **Choose the monitor** the panel should appear on from the detected monitors (the primary is preselected). Each entry shows its name, resolution, and identity summary, and the primary is marked.
- **Choose whether TouchPanel starts with Windows** (per-user, no elevation).

**Save and continue** writes the choice with the usual atomic save and applies it: the monitor identity is stored in `monitor.match` with `monitor.fallback` set to `none`, and the start-with-Windows option is applied immediately.

The wizard is shown only once. Afterwards, change the monitor from **Settings… → monitor picker** (or edit `monitor.match` in `panel.json` by hand), and change the startup option from Settings or in the configuration.

**Skip** (or closing the window) saves nothing and continues with the generated defaults — an unmatched monitor placed on the primary display — and the app starts normally.

To run the setup again, close TouchPanel (tray **Exit**) and delete or move `%APPDATA%\TouchPanel\panel.json`; the next launch shows the wizard again. Back the file up first if you want to keep the current layout.

If no monitor is detected, the monitor choice is skipped (a short message says so) but **Start with Windows** is still available and is applied as chosen.

The setup window is a normal activating window shown deliberately on first run only; the panel itself still never steals focus in normal mode.

## Usage

### Tray menu

While TouchPanel is running it shows a notification-area icon with:

- **Show/Hide Panel**
- **Edit Mode** (checkable toggle)
- **Settings…**
- **Hotkeys…**
- **About TouchPanel…**
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

### Pages

TouchPanel can host multiple pages. The configuration lists them in `pages[]` and names the active one in `activePageId`; the active page is preserved by id across reorders and reloads.

- Switch pages with a **horizontal swipe** on the panel (touch or mouse drag) or with the pager: page tabs at the top, and dots plus a `n / N` counter at the bottom. Paging does not wrap at the ends, and a swipe does not fire widget actions.
- Manage pages in **Settings… → Pages** or from the edit toolbar's **Pages…** surface: add, rename, remove, open, and drag to reorder (an accent insertion caret shows the drop position; **Move up** / **Move down** are the touch fallback).
- The last page cannot be removed, and page-structure changes are not part of edit-mode undo/redo.
- Each page can set an optional solid colour (`#RRGGBB` / `#AARRGGBB`) and/or a background image (absolute path, chosen with **Browse**) in the Pages editor. Images are decoded bounded (≤2048 px) and scaled to fill behind the widgets, which stay above and touchable. Unset falls back to the theme background; invalid values are cleared with a warning.

### Page transitions

Choose a transition in **Settings… → Appearance → Page transition**; it applies live and is stored as `window.pageTransition`.

- `none` — no animation; the new page appears instantly.
- `slide` — direction-aware horizontal slide (default): a left swipe brings the next page in from the right, a right swipe brings the previous page in from the left; tabs and Open use the forward direction (from the right).
- `fade` — crossfade between pages.
- `slideUp` — the new page rises in as the old page rises out.
- `zoom` — subtle scale and fade.

Animations last about 200–260 ms with an ease-out curve. They never play on startup, in edit mode, or when a hot reload or import rebuilds the panel. They play even when Windows animation effects are turned off — the selected mode is an explicit choice, and `none` is the opt-out. An unknown value falls back to `slide` with a validation warning.

### Edit Mode

Enter Edit Mode from the tray, by **long-pressing a widget**, by long-pressing empty panel space (a horizontal swipe cancels the hold), or via the edit toolbar. In Edit Mode you can:

- move and resize widgets (touch or mouse);
- add, remove, and duplicate widgets from the palette;
- edit the label, colours, icon, action, and sensor-bound properties;
- use grid snap and alignment guides;
- undo and redo widget edits with `Ctrl+Z` / `Ctrl+Y` (the toolbar **Undo** / **Redo** buttons do the same).

Leave Edit Mode with the **Exit Edit Mode** button or press `Esc`. Layout changes persist on exit. Tapping widgets while editing selects/moves them and does not fire their actions.

Keyboard shortcuts while editing: `Esc` exits, arrow keys nudge the selected widget, `Delete` removes it, `Ctrl+D` duplicates it, `Ctrl+G` toggles grid snap, and `Ctrl+Z` / `Ctrl+Y` undo and redo.

### Actions

Each widget binds to exactly one action:

- **Launch program** — a discrete executable path (with a **Browse** button), plus argument list and optional working directory.
- **Send key combination** — for example `Alt+A`, delivered to the foreground application.
- **Media key** — play/pause, next, previous, stop.
- **Volume** — set level or step up/down.
- **Mute** — toggle.
- **Run hotkey** — trigger a configured hotkey.

### About and updates

Tray **About TouchPanel…** opens a themed window with the app name, the running version (`1.0.2` today), a one-line description, links to the GitHub repository and releases, and a **Check for updates** button.

- The check is **manual only** — never on startup and no background polling. It queries the latest GitHub release over HTTPS with a 10 s timeout and a bounded response, then compares the tag with the running version.
- It reports **Update available: vX** (with an **Open release** link) or **You're up to date**, and shows a neutral **Couldn't check for updates** when offline, when there are no releases, or on error. It never downloads or installs anything.
- The About window is a normal activating window because it is opened deliberately; the panel itself still never activates in normal mode.

### Global hotkeys

Create global hotkeys in the **Hotkeys…** window. Each hotkey requires at least one modifier plus a key, and binds to one action. They work while a game or another application has focus and do not steal focus.

### Widgets and sensors

Gauges, Bars, and Sparklines pick a **sensor from a list**. Byte/rate sensors offer a display unit selector (Auto / B / KB / MB / GB). Sparklines can show or hide the current value. The Image widget displays a **PNG** loaded from a path chosen with **Browse**.

### Appearance

Choose a theme (dark / light / high-contrast / midnight / nord / OLED), an accent colour, and an icon pack (default / filled / outline / duotone) app-wide in **Settings… → Appearance**, with per-widget appearance overrides on top. Scrollbars are themed app-wide to match the active theme, with visible thumbs sized for touch.

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
