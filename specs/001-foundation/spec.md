# TouchPanel — Specification 001: Foundation

| Field | Value |
|---|---|
| Spec ID | 001-foundation |
| Status | Approved by human; 15 minor reviewer QA refinements applied |
| Date | 2026-10-08 |
| Phase | 1 — Specification |
| Author | Planner agent |
| Next steps | Implementation per `plan.md` and `tasks.md` (Phase 3 planning complete; no code written yet) |

This document captures the **full product vision** as tagged requirements so nothing is silently dropped, and defines a concrete **M1 (initial foundation) boundary**.

Milestone tags used on every requirement:

- **M1** — implemented in the first milestone.
- **M2** — first expansion after M1.
- **M3** — later expansion.
- **Later** — acknowledged vision, no committed milestone.

Decision markers **D-xx** refer to section 8 (Key decisions). Each decision is an explicitly chosen sensible default that the human can override at approval.

---

## 1. Goal

TouchPanel is a personal Windows desktop utility: a **borderless, touch-first control panel** that lives on a small secondary touchscreen and provides a configurable grid/canvas of widgets (buttons, toggles, sliders, gauges, labels, graphs) that trigger shortcuts, application/command launches, media/volume control, key combinations, and display live PC telemetry.

It must feel like a polished native Windows utility: lightweight while idle, reliable as an auto-start background utility, and — critically — usable **while a game or another application owns keyboard/mouse focus**, without ever stealing that focus.

**M1 goal:** deliver a reliable, testable foundation that proves the core architecture end to end:

1. coherent startup/lifecycle (single instance, tray, logging, exception handling),
2. correct window/monitor/DPI/fullscreen behavior with no focus stealing,
3. touch operation,
4. a config-driven widget host with a minimal set of widgets,
5. a minimal but real action execution path,
6. robust JSON configuration with schema versioning/recovery,
7. optional, failure-tolerant ~1 Hz telemetry.

## 2. Scope

**In scope for the product (all milestones).** Borderless fullscreen panel on a configurable monitor; no focus stealing; touch interaction; edit mode; widget host and built-in widgets (Button, Toggle, Slider, Gauge, Label, Sparkline); action system (open program / run command, media keys, volume, mute, send-keys); global hotkeys; monitoring via LibreHardwareMonitor behind an abstraction; JSON configuration with schema versioning, migration, hot reload, import/export; themes/accent/icon packs; structured logging; tray lifecycle; start with Windows; single instance; self-contained Windows deployment. Macros/sequences, repeat-while-held, the `delay` action, HTTP actions, and shell-open of files/folders/URLs were dropped (2026-10-09) and are not in scope.

**In scope for M1.** Everything tagged `[M1]` in section 5–6. Section 7 lists the exact boundary.

**Out of scope for M1.** Everything tagged `[M2]`, `[M3]`, `[Later]` — still captured as requirements, deferred by design.

**Focus-preservation guarantee scope.** Focus preservation (RF-018) covers windowed and borderless-fullscreen applications and games. If a game takes exclusive-fullscreen ownership of a display, no overlay window can reliably be shown above it; this is a documented limitation (R-1), not an M1 goal.

The single source of truth for milestone membership is the per-requirement tag; section 7 is a summary, not a replacement.

## 3. Non-goals

- Cross-platform support or cross-platform abstractions (Windows-only for the foreseeable product).
- Web-based or browser-hosted UI; the application is a native WPF desktop utility.
- Microservices, event-bus architectures, plugin systems, scripting engines, or heavy DI frameworks.
- Cloud sync, accounts, remote configuration, or telemetry upload.
- Enterprise concerns (multi-tenant, centralized management, MSI/MSIX/installer requirements for M1).
- Panels on multiple monitors at once, or freely floating/resizable panel windows (the panel occupies a whole monitor by design).
- A full theme designer or widget store in the first milestones.
- Assuming the touchscreen is a specific fixed monitor number, or hard-coding screen coordinates.

## 4. Terminology

- **Panel** — the TouchPanel borderless window shown on the target monitor.
- **Target monitor** — the monitor TouchPanel is configured to occupy.
- **Page** — a named collection of positioned widgets; exactly one page is active per panel in M1.
- **Widget** — a config-driven UI element (button, toggle, slider, gauge, label, sparkline).
- **Action** — a typed operation executed by a widget (run command, media key, etc.).
- **Macro** — *(dropped 2026-10-09; retained for historical reference)* an ordered sequence of actions. TouchPanel uses one action per widget; macros are not implemented.
- **Edit mode** — a mode for moving/resizing/configuring widgets.
- **SensorSnapshot** — an application-level, immutable telemetry sample.
- **DIP** — device-independent pixel (WPF logical unit, 1/96 inch).
- **Last known valid configuration** — the most recent configuration that loaded, validated, and was saved successfully.

---

## 5. Functional requirements

All requirements are prefixed with a stable ID and a milestone tag. "Must" indicates a verifiable requirement.

### 5.1 Application lifecycle & startup

- **RF-001 [M1] Single instance.** At most one TouchPanel instance must run per Windows session. Starting the application again must not create a second panel or a second tray icon.
- **RF-002 [M1] Second-launch signaling.** A subsequently launched process must signal the running instance through a Windows-appropriate IPC mechanism (D-21) and then exit without creating a window. By default the running instance's response is to show the panel if hidden (without activation, and subject to the RF-017 unavailable-monitor policy) and log the event. If signaling fails, the second process must log the failure and exit without creating a panel.
- **RF-003 [M1] Start with Windows (opt-in).** The application must support enabling/disabling automatic start at user logon. The mechanism must be per-user and must not require elevation (D-4). The enabled state must be stored in configuration and toggling it must create/remove the actual Windows startup registration.
- **RF-004 [M1] Startup registration failure handling.** Failures to read, write, or remove the startup registration must be logged, surfaced via tray notification, and must never crash the application. If configuration says "start with Windows" is enabled but the registration is missing or points at a stale path, the application must log a warning and attempt to re-register the current executable path.
- **RF-005 [M1] Tray icon and menu.** While running, the application must show a system-tray icon whose M1 menu provides: **Show/Hide Panel**, **Edit Mode** (toggle), **Open Configuration Folder**, and **Exit**. (A Settings window is deferred to M2.)
- **RF-006 [M1] Window close behavior (explicitly defined).** Closing the panel window (e.g., Alt+F4 or a system close request) must **hide the panel to the tray**, not terminate the application. The first time this happens, a one-time tray notification must explain that TouchPanel keeps running and how to exit. The application must terminate only through the tray **Exit** action (or an explicit shutdown command); the "notification already shown" flag must persist across restarts.
- **RF-007 [M1] Graceful shutdown.** On exit the application must, within a ~3 second budget: stop telemetry polling, cancel in-flight application-owned work, save configuration if dirty (atomically), remove the tray icon, flush logs, and write a shutdown log entry. Shutdown must not throw user-visible errors.
- **RF-008 [M1] Structured logging content.** The application must log at minimum: startup/shutdown; monitor detection and selection outcome; configuration load/save/migration outcomes and failures; widget/action execution failures; global hotkey registration failures (once implemented); telemetry initialization/read failures; display/DPI change handling; and unexpected exceptions. Logs must include timestamp, severity, category, message, and exception data where applicable. Logs must be stored in a user-accessible application-data directory (D-5).
- **RF-009 [M1] Log rotation and retention.** Log files must be bounded by rotation (default: 5 files × 2 MB, D-16). Logging must never grow without limit, and rotation must be safe while the app runs.
- **RF-010 [M1] Logging resilience.** If the log directory is unwritable or a write fails, the application must not crash; it must degrade (best-effort alternate sink / debug output), and must not block the UI thread.
- **RF-011 [M1] Global exception handling.** The application must handle: WPF `DispatcherUnhandledException` (log, mark handled only where continuing is safe), `AppDomain.CurrentDomain.UnhandledException` (log during termination), and `TaskScheduler.UnobservedTaskException` (observe and log). No unexpected termination may occur without a log entry that includes diagnostic information.
- **RF-012 [M1] Subsystem isolation.** Failures in telemetry, hotkeys, actions, or configuration must not terminate the whole application where the failure can be isolated. The affected subsystem must be marked degraded, the failure logged, and the remaining panel functionality must continue.
- **RF-013 [M1] Tray resilience.** If Windows Explorer restarts (tray disappears), or if the initial tray registration fails (e.g., Explorer not yet ready during logon auto-start), the application must retry registration with bounded backoff, log each failure, and must never exit or crash over tray registration.

### 5.2 Window, monitor, DPI, and focus behavior

- **RF-014 [M1] Borderless fullscreen panel.** The panel must be a borderless window (`WindowStyle=None`, no caption/borders) that, when shown, occupies the **full bounds** of the target monitor (`rcMonitor`, including any taskbar area), not merely the work area.
- **RF-015 [M1] Configurable monitor selection.** The target monitor must be selectable through configuration. The configuration must store a monitor identity block (see RF-016) and a fallback policy, not a bare monitor index.
- **RF-016 [M1] Stable monitor identification (no index reliance).** Monitor matching must use a deterministic priority scheme based on stable identity, never monitor index alone:
  1. EDID-derived hardware identity (manufacturer + product code + serial) when available;
  2. display device path / device instance identifier;
  3. friendly name + resolution (+ position as final tiebreak).
  Matching must be implemented as testable logic over monitor descriptors. When no descriptor matches, the monitor is considered unavailable (RF-017). Ambiguous matches must be resolved deterministically and logged. (Decision D-17: at startup, all discovered monitors are logged with their identity fields so the user can pin the touchscreen in configuration. A monitor-picker UI is deferred to M2.)
- **RF-017 [M1] Unavailable target monitor policy.** If the configured monitor is unavailable at startup or disappears at runtime, the application must not crash and must not silently open on an unexpected monitor. Default policy (D-2): stay hidden, log a warning, show a tray notification, and remain running; when the configured monitor (re)appears, the panel must show on it automatically (driven by display-change events, not polling). A configuration option may enable fallback to the primary monitor, and any fallback placement must be accompanied by a notification — never silent. Manual Show requests (tray or second launch) while the target monitor is unavailable follow the same policy: with `fallback: none` the request is refused with a tray notification explaining that the monitor is unavailable; with the primary fallback enabled, the panel may be shown on the primary monitor with a notification — never silently.
- **RF-018 [M1] No focus stealing in normal mode.** In normal (non-edit) operation the panel must never activate itself and must never take keyboard/mouse focus from the foreground application (e.g., a running game). Concretely: the window must be created with `WS_EX_NOACTIVATE` and `WS_EX_TOOLWINDOW` extended styles, shown with `ShowActivated=false`, and the application must not call `SetForegroundWindow`/`Activate` when showing, hiding, or interacting with the panel. Touch/click interaction with the panel must not move keyboard focus away from the foreground application.
- **RF-019 [M1] Not in taskbar or Alt-Tab.** The panel must not appear in the taskbar (`ShowInTaskbar=false`) nor in the Alt-Tab window list (consistent with `WS_EX_TOOLWINDOW`).
- **RF-020 [M1] Always-on-top option.** Configuration must support an optional always-on-top behavior (D-3; default off). It must apply without activation; M1 applies the setting at startup, and M2 applies it live via hot reload (RF-082).
- **RF-021 [M1] DPI awareness (PerMonitorV2).** The application must declare **PerMonitorV2** DPI awareness via the application manifest and must position/size the window using per-monitor DPI-correct coordinates. Rendering must not be bitmap-stretched; layout must recompute on DPI changes.
- **RF-022 [M1] Display and DPI change handling.** The application must react to display added/removed/resolution-change/DPI-change events (e.g., via `SystemEvents.DisplaySettingsChanged` or equivalent Win32 notifications): re-validate the target monitor per RF-017, recompute window bounds per RF-014, and clamp or relayout widgets so nothing is left unreachable. No crash, no orphaned windows.
- **RF-023 [M1] Small display support.** The UI must be usable on small panels such as 1024×600 at 100% scaling and at higher scaling factors. On first run, the generated default layout must fit within the target monitor without clipping or scrolling.
- **RF-024 [M1] Show/hide through tray without activation.** Show and Hide must work from the tray menu; showing must not activate the window or change the foreground window (and is subject to RF-017 when the target monitor is unavailable).
- **RF-025 [M1] Normal-mode keyboard independence.** Normal operation must be fully operable via touch/mouse without keyboard focus. No M1 interaction may depend on the panel having keyboard focus.
- **RF-026 [M1] Edit mode enter/exit.** Edit mode must be enterable/exitable from the tray menu. Entering edit mode may intentionally activate the panel (D-6) — this is a deliberate, user-initiated act, acceptable to take focus. Exiting edit mode (tray or Esc) must save the layout if dirty and restore non-activating behavior (RF-018).
- **RF-027 [M1] Edit mode keyboard interaction.** While in edit mode: **Esc** exits edit mode; **arrow keys** nudge the selected widget by 1 DIP. No other keyboard interactions are required in M1.
- **RF-028 [M1] Panel placement policy.** The panel is monitor-fullscreen by design; it must not be freely draggable as a floating window. Its bounds are always derived from the target monitor's full bounds and recomputed on display/DPI changes.
- **RF-029 [M1] Multi-monitor coexistence.** Running TouchPanel must not disturb windows or applications on other monitors. The panel must appear only on the target monitor.

### 5.3 Touch interaction and widget system

- **RF-030 [M1] Touch interaction baseline.** All interactive widgets must respond to touch (tap/press) and to mouse equivalently. Press feedback must be visually immediate (perceived reaction under ~100 ms). No M1 widget may depend on hover or right-click for its primary function.
- **RF-031 [M1] Minimum touch target.** Built-in interactive widget templates must default to at least 40×40 DIP (D-9) so they remain usable on 1024×600 panels. Widgets may be configured larger.
- **RF-032 [M1] Config-driven widget host.** The panel must render the active page from configuration as absolutely positioned widgets (x, y, width, height in DIPs), scaled correctly per monitor DPI. The host must contain no per-widget-instance logic; widget behavior lives in reusable per-type View + ViewModel pairs.
- **RF-033 [M1] Built-in widget catalog (M1 subset).** M1 must ship: **Button**, **Toggle**, **Label**, and **Gauge**. Each must be reusable, config-driven, and registered with the host so that adding a new widget type later requires no changes to the host beyond registration (extensibility). Slider and Sparkline are M2 (RF-040, RF-041).
- **RF-034 [M1] Widget properties (M1).** Configuration must drive at least: position/size; label text; action binding (Button/Toggle); sensor + display range (Gauge); and the initial on/off state applied to a Toggle when the page loads. Colors/icon/appearance overrides are deferred to the full edit UI (M2); M1 uses theme defaults.
- **RF-035 [M1] Press semantics.** A Button must execute its action once per completed tap (release inside the control). A Toggle must flip its visual state and execute its action once per flip. Long-press/repeat-while-held behavior (RF-042) was dropped (user-descoped 2026-10-09); a completed tap/flip remains the only press semantic.
- **RF-036 [M1] Widget action failure feedback.** When an action fails, the widget must show a transient error indication (approximately 2 seconds) in addition to the structured log entry, without blocking further interaction.
- **RF-037 [M1] Pages model.** The configuration schema must represent `pages[]` and an `activePageId` from schema version 1. On load, `activePageId` is validated against `pages[]`; if missing or invalid, the first page in `pages[]` is used and the substitution is logged. M1 renders the active page; page-switching UI is deferred to M2 (RF-091, D-18).
- **RF-038 [M1] Bounded update rate for data-driven widgets.** Telemetry-driven widgets (Gauge) must update at most at the snapshot rate (≤ ~1 Hz) and must not run idle animation loops; there must be no UI churn when values are unchanged.
- **RF-039 [M1] Edit mode manipulation.** While in edit mode, widgets must be movable by drag and resizable via a resize handle, with touch or mouse. Geometry must be clamped to the page bounds and to a defined minimum size; changes must persist to configuration on edit-mode exit/save (RF-026). Adding/removing/duplicating widgets and property editing are deferred to the full edit UI (M2).
- **RF-040 [SUPPORTED via M4 — promotion 2026-10-10] Slider widget.** Touch/mouse slider with configurable range, step, orientation, and action binding (e.g., volume control); emits value-change actions with throttling. (Implemented M2; promoted by M4 per `D-25`.)
- **RF-041 [M2] Sparkline/graph widget.** A bounded-history graph widget consuming ring buffers (RF-069), with configurable metric and time window; renders on the UI thread from snapshots/history, never polling hardware directly.
- **RF-042 [DROPPED — user-descoped 2026-10-09] Repeat while held.** _Removed from the product plan (not deferred); retained only for ID stability. No repeat-while-held behavior will be implemented._ Original intent: press-and-hold on a widget repeats its action at a configurable interval until release.
- **RF-043 [M2 appearance supported; non-appearance SUPPORTED via M4 — promotion 2026-10-10] Full edit UI.** Add/remove/duplicate widgets, property editor, action picker, label/icon selection, and color/appearance editing within edit mode. (Appearance delivered M2; non-appearance implemented M2 and promoted by M4 per `D-25`.)
- **RF-044 [SUPPORTED via M4 — promotion 2026-10-10] Grid snap and alignment aids** in edit mode, in addition to M1 free positioning with bounds clamping. (Implemented M2; promoted by M4 per `D-25`.)
- **RF-045 [SUPPORTED via M4 — promotion 2026-10-10] Toggle state persistence.** Option to persist toggle states across restarts (D-15 keeps M1 runtime-only; persistence is added later if wanted). (Implemented M2; promoted by M4 per `D-25`.)
- **RF-046 [M3 — un-frozen 2026-10-10] Additional widget types.** Architecture must allow adding new widget types (beyond the six built-ins) without host changes; no plugin system is introduced — new types are code additions.

### 5.4 Action system

- **RF-047 [M1] Action model.** Each widget binds to **exactly one action**; this one-action-per-widget model is the product model. Sequences/macros are **not planned** (RF-054 was dropped, user-descoped 2026-10-09). Actions are typed with explicit parameters, validated at configuration load. An unknown/invalid action type must disable that widget with a visible error indication and a log entry, without affecting other widgets.
- **RF-048 [M1] Action catalog (M1).** M1 must implement: **Run command / launch application** (explicit executable path, argument list, optional working directory; a.k.a. **"open program"**), **Media key** (play/pause, next, previous, stop), **Volume control** (set absolute level, step up/down), and **Mute toggle**. Send-keys is M2 (RF-057). HTTP (RF-056), the `delay` action type, macros, and shell-open are dropped (2026-10-09).
- **RF-049 [M1] Safe process launching.** Action configuration is untrusted input. Process launching must default to `UseShellExecute=false`, must pass executable and each argument as discrete values (no shell string concatenation or parsing by the app), and must not wrap commands in `cmd.exe`/PowerShell implicitly (D-7). Launching documents/URLs via shell (the deferred opt-in variant of this requirement) is **dropped (2026-10-09); backlog / not planned**.
- **RF-050 [M1] Asynchronous dispatch and failure containment.** Action execution must never block the WPF UI thread. Failures (missing executable, access denied, process-start exceptions, input-synthesis failure) must be caught, logged with action type and widget ID, surfaced per RF-036, and must not crash the application.
- **RF-051 [M1] Action logging and redaction.** Action start/failure must be logged with structured context. Parameters must not be logged when they could be sensitive; a redaction rule applies to all action logging (RF-085).
- **RF-052 [M1] Process lifetime policy.** Processes launched by actions are external and are not tracked, waited on, or terminated by TouchPanel in M1 (D-20). No blocking wait/exit-code handling exists in M1.
- **RF-053 [M1] WPF independence of actions.** Action implementations must have no dependency on WPF types; they are invoked through an application-level executor boundary, keeping them testable without a UI (RNF-010).
- **RF-054 [DROPPED — user-descoped 2026-10-09] Macros / sequences.** _Removed from the product plan (not deferred); retained only for ID stability. One widget executes exactly one action (RF-047)._ Original intent: ordered execution of actions with per-step delays and a failure policy.
- **RF-055 [DROPPED — user-descoped 2026-10-09] Macro cancellation.** _Removed with macros/sequences (RF-054); retained only for ID stability._
- **RF-056 [DROPPED — user-descoped 2026-10-09] HTTP action.** _Dropped from the product (not deferred); backlog / not planned; retained only for ID stability._ Original intent: configurable HTTP method/URL/headers/body with bounded timeout, response-size limit, and redirect cap.
- **RF-057 [M2] Send keys / key synthesis.** A widget or global hotkey sends one configurable key + modifier combination (e.g., **Alt+A**) via `SendInput`, delivered to the **foreground** application (focus implications explicit). Media keys (M1) remain the safe default. This covers the user's key-combination need in place of the dropped macro model.

### 5.5 Input and hotkeys

- **RF-058 [M2] Global hotkeys.** The application must support global hotkeys that trigger actions while a game or another application has focus, using `RegisterHotKey` via the `Interop/` layer (D-8) or an equivalent maintained mechanism. Hotkey registration must be separated from widget/business logic.
- **RF-059 [M2] Global hotkey registration failure.** Registration failures (e.g., combination already taken) must be logged with the combination, must not crash the app, must not retry in a loop, must allow the remaining registrations to succeed, and must be surfaced to the user (aggregated tray notification).
- **RF-060 [SUPPORTED via M4 — promotion 2026-10-10] Local shortcuts.** Shortcuts that operate only when appropriate (e.g., panel or edit mode active) must be supported and kept separate from global registration. (Implemented M2; promoted by M4 per `D-25`.)
- **RF-061 [M3 — un-frozen 2026-10-10] Hotkey management.** Configuration UI for editing hotkeys and re-applying them without restart (ties into hot reload, RF-082).

### 5.6 Monitoring (hardware telemetry)

- **RF-062 [M1] Monitoring abstraction.** The application must expose an application-level monitoring service that produces immutable `SensorSnapshot` values. The LibreHardwareMonitor dependency must be confined to an adapter inside `Monitoring/`; LibreHardwareMonitor types must not leak into ViewModels or any other layer (verified by project references/code review).
- **RF-063 [M1] Snapshot content.** `SensorSnapshot` must be capable of carrying: CPU total usage, per-core CPU usage, GPU usage, VRAM used/total, RAM used/total/percentage, disk activity/throughput, network up/down activity, temperatures (e.g., CPU package, GPU), and fan speeds where available. Fields that are not available must be explicitly marked unavailable (e.g., null), never silently fabricated as zero.
- **RF-064 [M1] Polling at ~1 Hz.** Telemetry must be polled at approximately 1 Hz (default; configurable later per RF-071), on a background thread/timer, never on the UI thread. Polling continues while the panel is hidden (D-10) for simplicity and immediate data freshness.
- **RF-065 [M1] Failure tolerance.** Hardware-library initialization failures, missing sensors, and read exceptions must produce snapshots with unavailable values for the affected fields, must log at a throttled rate (not once per poll), and must never crash the application or affect non-monitoring features.
- **RF-066 [M1] Lifecycle, cancellation, disposal.** Monitoring must start/stop with application lifecycle, support cancellation, dispose the hardware library cleanly, and leave no background threads alive after shutdown.
- **RF-067 [M1] UI-thread safety.** Snapshot delivery to ViewModels must be marshalled to the UI thread; no sensor or library calls may occur on the UI thread; a slow or failed poll must not stall the UI.
- **RF-068 [M1] Elevation separation.** Core TouchPanel functionality must require no elevation and must never trigger a UAC prompt. Sensors that require administrator rights and/or kernel drivers (LibreHardwareMonitor limitation) must simply be unavailable when the app is not elevated, clearly shown as unavailable — never an error state for the whole app (D-11).
- **RF-069 [M2] Bounded history.** Graph/consumption history must use fixed-capacity ring buffers (per metric), never unbounded collections. Capacity is derived from the configured time window at the polling rate.
- **RF-070 [SUPPORTED via M4 — promotion 2026-10-10] Backend re-initialization.** Optional periodic retry/re-initialization of a failed monitoring backend (e.g., driver installed later), throttled and logged. (Implemented M2; promoted by M4 per `D-25`.)
- **RF-071 [SUPPORTED via M4 — promotion 2026-10-10] Configurable polling interval.** Polling interval configurable within sane bounds (e.g., 0.5–5 s); 1 Hz remains the default. (Implemented M2; promoted by M4 per `D-25`.)
- **RF-072 [FROZEN — backlog 2026-10-09] Optional elevated companion.** A separate, explicitly opted-in mechanism to obtain sensors that require elevation/drivers. Only considered if a real need remains after M1/M2; not part of any committed milestone.

### 5.7 Configuration and persistence

- **RF-073 [M1] Location and files.** The main configuration must live at `%APPDATA%\TouchPanel\panel.json` (D-12). Non-user-facing runtime flags (e.g., "close notification shown") must be stored separately in a small state file (`state.json`) so the user-editable configuration stays clean; a corrupt/missing state file must be non-fatal and regenerated.
- **RF-074 [M1] Schema version.** The configuration must contain an explicit integer `schemaVersion` (current: `1`) near the root. The design must allow future migrations (RF-076). JSON example (illustrative; the exact schema is finalized in the implementation plan):
  ```json
  {
    "schemaVersion": 1,
    "activePageId": "main",
    "monitor": { "match": { "hardwareId": "...", "devicePath": "...", "friendlyName": "..." }, "fallback": "none" },
    "window": { "alwaysOnTop": false },
    "startup": { "startWithWindows": false },
    "pages": [ { "id": "main", "widgets": [ { "id": "w1", "type": "button", "x": 16, "y": 16, "width": 120, "height": 120, "label": "Media", "action": { "type": "mediaKey", "key": "playPause" } } ] } ]
  }
  ```
  Telemetry polling is fixed at ~1 Hz in M1; a configurable interval arrives with RF-071 (M2) through a schema migration (D-19).
- **RF-075 [M1] Distinguishable configuration states.** On load the application must distinguish and handle four states:
  1. **Current valid** — schema version equals the supported version and validation passes: load normally.
  2. **Older supported** — schema version is older than supported: migrate (RF-076), validate the result, back up the original, and save the migrated file.
  3. **Invalid/corrupt** — unparsable JSON or structurally invalid: do not overwrite; preserve the bad file (e.g., `panel.corrupt-<timestamp>.json`), recover from the last known valid backup (RF-078) or generated defaults, log, and notify the user.
  4. **Unsupported future** — schema version newer than supported: do not overwrite and do not attempt to interpret; log, notify the user, and run from the last known valid backup or defaults.
- **RF-076 [M1] Migration framework.** Ordered, testable migration steps from older schema versions to current must exist as a framework (even though v1 is the first schema and no real migration is required yet). Migration failure must follow the invalid/corrupt path (RF-075.3); the source file must never be destructively overwritten before a migrated configuration has been validated and saved successfully.
- **RF-077 [M1] Atomic save.** Saves must be atomic: write to a temporary file in the same directory, flush, then replace via atomic file replacement (`File.Replace`/`MoveFileEx` semantics). A crash or power loss must never leave a partially written `panel.json`. Save failures (disk full, permissions, file locked) must be logged, surfaced via tray notification, keep in-memory state intact, and be retried on the next save/shutdown.
- **RF-078 [M1] Backup and recovery.** A last known valid backup (`panel.backup.json`) must be maintained (previous successfully validated version) and used for recovery. Corrupt or future-version sources must be preserved for diagnosis, never silently deleted or overwritten.
- **RF-079 [M1] Validation tiers.** Configuration is untrusted input (RF-081). Handling is two-tier (D-13): file-level problems (JSON syntax, wrong root types, missing/invalid `schemaVersion`) trigger the recovery path (RF-075); per-widget/per-entry problems are contained with a logged warning while the rest of the file loads: unknown widget types are preserved and rendered as a disabled placeholder with their raw JSON kept (never dropped; post-M1 QA refinement, 2026-10-09); a widget with an invalid action binding remains visible but disabled with an error indicator (RF-047); out-of-bounds geometry is clamped into the page; values have sane numeric bounds.
- **RF-080 [M1] Save triggers, single writer, no polling.** Configuration is read at startup and written only on defined triggers (edit-mode layout changes, preference changes, shutdown when dirty). M1 must not poll or watch the filesystem; a single writer owns the file.
- **RF-081 [M1] Untrusted configuration.** All configuration strings/values must be validated and treated as data, never code. No configuration value may cause execution beyond explicitly modeled action types executed through the action layer.
- **RF-082 [SUPPORTED via M4 — promotion 2026-10-10] Hot reload.** External configuration changes must be detected (e.g., `FileSystemWatcher`, debounced) and applied or rejected as a whole: validate first, then apply; on any failure keep the currently running configuration active, log, and notify. A failed reload must never leave the app in a partially applied or broken state.
- **RF-083 [SUPPORTED via M4 — promotion 2026-10-10] Import/export.** User-triggered export/import of configuration with validation before applying an imported file; the current configuration is backed up first. (Implemented M2; promoted by M4 per `D-25`.)
- **RF-084 [M3 — un-frozen 2026-10-10] Themes, accent colors, icon packs.** Themed resources driven by configuration, including accent color and icon-pack selection. New configuration fields arrive through migrations (D-19).

### 5.8 Security and reliability

- **RF-085 [M1] No secrets in logs.** Secrets (tokens, credentials, sensitive action parameters) must never be written to logs. Log entries for actions/configuration must redact or omit sensitive values. M1 stores no secrets; with HTTP (RF-056) dropped, M2 introduces no secret-bearing configuration fields, so no secret persistence is expected. A redaction unit test is required (section 11, AC-01).
- **RF-086 [M1] No elevation, no UAC.** Core operation (startup, panel, widgets, actions, tray, configuration, monitoring) must work as a standard user and must never prompt for elevation (D-11).
- **RF-087 [DROPPED — user-descoped 2026-10-09] HTTP safety constraints.** _Dropped with HTTP (RF-056); backlog / not planned; retained only for ID stability._

### 5.9 Deployment

- **RF-088 [M1] Publish command works.** Windows deployment must work through `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true` and produce a runnable executable. (Exact publishing strategy may be revisited later; single-file self-contained is the M1 expectation.)
- **RF-089 [M1] Portable, no installer.** M1 requires no installer; the application runs from any user-writable location, is self-contained (no runtime prerequisite besides the OS), and performs all writes under the user profile.
- **RF-090 [M1] Supported OS.** Windows 10 21H2+ and Windows 11, x64 (D-14). The application is Windows-only; no cross-platform abstractions are introduced.

### 5.10 Deferred user-interface features (M2)

- **RF-091 [SUPPORTED via M4 — promotion 2026-10-10] Page-switching UI.** An in-panel touch affordance for switching the active page, persisting `activePageId` (RF-037) with the same validation and fallback rules. (Implemented M2; promoted by M4 per `D-25`.)
- **RF-092 [SUPPORTED via M4 — promotion 2026-10-10] Settings window.** A tray-accessible settings window for editing configuration options (monitor selection, window preferences, startup, telemetry interval) with validation and atomic save through the M1 configuration store. (Implemented M2; promoted by M4 per `D-25`.)
- **RF-093 [SUPPORTED via M4 — promotion 2026-10-10] Monitor picker.** Discovered monitors are listed with their stable identity fields so the user can select the target monitor without hand-editing JSON (RF-016, D-17). (Implemented M2; promoted by M4 per `D-25`.)

---

## 6. Non-functional requirements

- **RNF-001 [M1] Idle CPU.** With ~1 Hz telemetry running, average CPU usage must stay below ~2% of one logical core over a 5-minute idle measurement (after a 60 s warm-up) on the developer machine, measured in both states — panel visible and panel hidden (D-10) — verified via Task Manager or PerformanceCounter. No busy loops and no timers faster than needed.
- **RNF-002 [M1] Memory behavior.** Steady-state working set target ≤ 300 MB; an 8-hour soak must show no unbounded growth (target: < 10 MB/hour after warm-up). Verified manually in M1.
- **RNF-003 [M1] Rate discipline.** Telemetry ≤ 1 poll/second by default; data-driven UI updates at most once per snapshot; no filesystem polling; no unnecessary background work while idle.
- **RNF-004 [M1] UI-thread discipline.** No synchronous blocking calls (file I/O, process wait, hardware library) on the WPF UI thread; long operations are async with cancellation; the UI must not freeze perceptibly during configuration load/save or action dispatch.
- **RNF-005 [M1] Startup time.** Tray icon and panel (when the target monitor is available) must appear within ~3 s of launch on the developer machine; telemetry initialization must not delay window/tray creation.
- **RNF-006 [M1] Testability.** Core logic — configuration load/save/migration/validation, monitor matching, action parameter building, snapshot aggregation, widget view models — must be unit-testable without hardware, drivers, LibreHardwareMonitor, or elevation. `dotnet test` is the acceptance gate. Tests must not touch the real user profile (temp dirs / injected paths).
- **RNF-007 [M1] Logging performance.** Logging must be bounded and must not block the UI thread; rotation limits total disk usage (~10 MB default).
- **RNF-008 [M1] No unnecessary background work.** M1 has no filesystem watchers; display changes are handled via OS events; no idle animations; no rendering when values are unchanged.
- **RNF-009 [M1] Touch ergonomics.** Default interactive targets ≥ 40×40 DIP; no hover-dependent behavior; usable at 100% scaling on a 1024×600 panel.
- **RNF-010 [M1] Layering.** Win32 interop isolated under `Interop/`; `Actions/`, `Monitoring/`, and `Config/` must not reference WPF; interfaces only at meaningful boundaries (monitoring, configuration store, action execution, hotkeys, startup registration, process launching where testability matters). No event bus, plugin system, or heavy DI — a composition root only. Project layout constraint: `App.xaml(.cs)`, `MainWindow.xaml(.cs)`, and the folders `Views/`, `ViewModels/`, `Widgets/`, `Actions/`, `Input/` (reserved for M2 hotkeys), `Monitoring/`, `Config/`, `Interop/`, `Services/`, `Theme/`; tests live in `TouchPanel.Tests/`.
- **RNF-011 [M1] Dependency discipline.** Minimal third-party dependencies (LibreHardwareMonitor, one logging approach, one test framework); each additional dependency must be justified.
- **RNF-012 [M2] Bounded history.** Ring buffers are fixed-capacity per metric (e.g., 3600 samples = 1 h at 1 Hz); memory overhead documented.
- **RNF-013 [M1] Graceful degradation.** Any non-core subsystem failure leaves the application usable and diagnosable; core behavior (window, tray, configuration, widgets, M1 actions) is never taken down by telemetry, hotkey, or action failures.
- **RNF-014 [M1] Security posture.** RF-049 (safe launching), RF-081 (untrusted input), RF-085 (no secrets in logs), and RF-086 (no elevation) are non-negotiable M1 defaults.
- **RNF-015 [M1] Display correctness.** Correct layout and hit-testing at 100%–200% scaling and at 1024×600; no bitmap stretching (PerMonitorV2).

## 7. Milestone scoping

### 7.1 M1 boundary

M1 follows the user's stated priority order — (1) startup/lifecycle, (2) window/monitor/DPI/fullscreen, (3) touch, (4) widget architecture, (5) action architecture, (6) configuration/persistence, (7) telemetry — and stops before customization and polish (8, 9), which are deferred. M1 deliberately proves each layer with a thin but real vertical slice:

- **App shell:** .NET 10 WPF, MVVM, single instance, tray lifecycle, startup registration toggle.
- **Reliability:** structured rotating logs, global exception handling, subsystem isolation, graceful shutdown.
- **Window:** borderless fullscreen on a robustly identified monitor, `WS_EX_NOACTIVATE`/`WS_EX_TOOLWINDOW`, `ShowActivated=false`, optional always-on-top, PerMonitorV2, 1024×600 support, display hot-plug behavior, graceful handling of a missing monitor.
- **Editing:** minimal edit mode — enter/exit via tray, move/resize with bounds clamp, Esc/arrow-key support, persistence on exit.
- **Widgets:** config-driven host plus Button, Toggle, Label, Gauge.
- **Actions:** Run command/launch app (safe argument handling), media keys, volume, mute; async execution; failure feedback and logging.
- **Configuration:** `panel.json` with `schemaVersion: 1`, four distinguishable load states, migration framework, atomic writes, backup/recovery, two-tier validation, generated defaults.
- **Telemetry:** application-level `SensorSnapshot` at ~1 Hz off the UI thread, LibreHardwareMonitor isolated and optional, failure-tolerant, no elevation requirement.
- **Deployment:** `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true` produces a working portable executable.

### 7.2 Delivered Core Release and milestone mapping (2026-10-09 freeze) — *(historical)*

> **Historical snapshot.** This subsection records the 2026-10-09 freeze as it stood then. The subsequent un-freeze/promotion (M3 for RF-046/061/084; M4 for the rest) is recorded in **§7.3 and `D-25`** and supersedes the "Frozen backlog" column below for the promoted IDs.

The product is **frozen** (`D-24`) to a **Core Release** = the M1 core plus exactly four M2 features: **Sparkline/graph + bounded telemetry history**, **global hotkeys**, **send-keys (key combinations)**, and **visual customization (icon / colors / font / opacity)**. Everything else is **frozen backlog**: its code may exist in the build but is unsupported and will not be extended. No M3/Later work is planned.

| Area | M1 (delivered) | Core Release (M2, delivered) | Frozen backlog (2026-10-09) |
|---|---|---|---|
| Lifecycle & startup | RF-001..RF-013 | — | — |
| Window / monitor / DPI / focus | RF-014..RF-029 | — | — |
| Touch & widgets | RF-030..RF-039 | RF-041, RF-043 (appearance only) | RF-040, RF-043 (add/remove/property/action-picker), RF-044, RF-045, RF-046 |
| Actions | RF-047..RF-053 | RF-057 | — |
| Input & hotkeys | — | RF-058, RF-059 | RF-060, RF-061 |
| Monitoring | RF-062..RF-068 | RF-069 | RF-070, RF-071, RF-072 |
| Configuration | RF-073..RF-081 | — | RF-082, RF-083, RF-084 |
| Security | RF-085, RF-086 | — | — |
| Deployment | RF-088..RF-090 | — | — |
| Deferred UI features | — | — | RF-091..RF-093 |
| Non-functional | RNF-001..RNF-011, RNF-013..RNF-015 | RNF-012 | — |

**Dropped (user-descoped 2026-10-09; retained for ID stability, not planned):** RF-042 repeat-while-held, RF-054 macros/sequences, RF-055 macro cancellation, RF-056 HTTP action, RF-087 HTTP safety constraints, the `delay` action type, and shell-open of files/folders/URLs (RF-049 variant). The action model is one action per widget (RF-047); the delivered action set is **open program (existing run-command) + send key combination** (send-keys, RF-057), with global hotkeys (RF-058/RF-059).

### 7.3 Frozen backlog (2026-10-09) → promoted by M4 (2026-10-10)

The following requirements were frozen on 2026-10-09: their implementation already existed in the build but was **unsupported and unmaintained**. IDs are retained; nothing is renumbered.

- **Widgets/editing:** RF-040 Slider; RF-043 full edit UI **except** appearance (i.e. palette add/remove/duplicate `M2-RF-430/431/432`, type-specific property fields `M2-RF-433`, action picker + executable browse `M2-RF-434`, and the persistence/isolation parts `M2-RF-437..440` insofar as they cover those); RF-044 grid snap + alignment aids; RF-045 toggle-state persistence.
- **Input:** RF-060 local shortcuts.
- **Monitoring:** RF-070 throttled backend re-initialization; RF-071 configurable polling interval.
- **Configuration:** RF-082 hot reload; RF-083 import/export.
- **Deferred UI:** RF-091 page-switching UI; RF-092 settings window; RF-093 monitor picker.
- **M3/Later:** RF-046 additional widget types; RF-061 hotkey-management UI; RF-072 optional elevated monitoring companion; RF-084 themes/accent/icon packs (beyond the per-widget visual overrides delivered in the Core Release).
  - **Un-freeze note (2026-10-10):** the human approved **M3**, scoping **RF-046, RF-061, and RF-084** (see `specs/003-customization/spec.md`). **RF-072 remains frozen/skipped** (elevation/UAC).
- **Already dropped (not backlog):** RF-042, RF-054, RF-055, RF-056, RF-087, the `delay` action, and shell-open.

> **M4 promotion note (2026-10-10, `D-25`).** The human approved **M4 ("Promote & Polish")**, which un-freezes and officially supports the already-implemented backlog listed above (see `specs/004-promotion/spec.md`). Those requirements are now **supported and maintained**; the sections above tag them `[SUPPORTED via M4 — promotion 2026-10-10]`. The supported set is now **everything implemented except `RF-072` (skipped) and the dropped set**. `RF-046/061/084` remain supported (M3). `RF-072` remains frozen/skipped. IDs are unchanged.

In the requirement sections above, previously frozen items now carry `[SUPPORTED via M4 — promotion 2026-10-10]`; `RF-072` retains its frozen tag; dropped items retain their dropped tags.

## 8. Key decisions (defaults; the human can override any of these at approval)

| ID | Decision (chosen default) | Rationale |
|---|---|---|
| D-1 | M1 includes a **minimal edit mode**: enter/exit, move, resize, bounds clamp, persist (RF-039); no add/remove/property UI. | Proves touch layout + persistence end to end; otherwise layout would only be editable by hand-writing JSON. |
| D-2 | Missing target monitor → **stay hidden**, log, tray notification, auto-show when it returns; optional `fallback: primary` never silent. | Satisfies "handle gracefully, no crash, no silent unexpected placement". |
| D-3 | `alwaysOnTop` **default false**. | Safer coexistence with games/other windows; trivially enabled in config. |
| D-4 | Start with Windows via **HKCU `...\CurrentVersion\Run`** value (per-user, no elevation). | Simple, discoverable, removable; no scheduled-task complexity. |
| D-5 | Logs at `%LOCALAPPDATA%\TouchPanel\logs`; config at `%APPDATA%\TouchPanel\panel.json`; runtime flags in `state.json` beside config. | User-accessible; config path per user's stated expectation; machine-local logs don't roam. |
| D-6 | Edit mode **may activate** the panel (deliberate user action); normal mode never activates. | Editing needs keyboard; normal operation must never steal focus. |
| D-7 | Process launching is `UseShellExecute=false` only, explicit exe + discrete args; **shell-open of documents/URLs is dropped (2026-10-09), not planned** (backlog). | Security: no hidden shell invocation, no injection surface; the opt-in shell path is no longer on any milestone. |
| D-8 | Global hotkeys via `RegisterHotKey` in `Interop/` (M2). | Native, no dependency, well-suited to a personal utility. |
| D-9 | Minimum default interactive target **40×40 DIP**. | Usability on 1024×600; larger targets remain configurable. |
| D-10 | Telemetry continues at ~1 Hz while the panel is hidden. | Simplicity and immediate freshness; one LHM update/s is cheap. Revisit if idle CPU suffers. |
| D-11 | **No UAC, ever.** Sensors needing admin/drivers are simply unavailable and shown as such; optional elevated companion is Later (RF-072). | Core utility must work as a standard user. |
| D-12 | Exact paths: `%APPDATA%\TouchPanel\panel.json` and `%APPDATA%\TouchPanel\state.json`; corrupt state file is regenerated silently (logged). | Keeps user-facing config clean of runtime flags. |
| D-13 | Validation is **two-tier**: file-level errors → recovery path; entry-level errors → skip/clamp with warning. | One bad widget must not kill the whole panel, but structural garbage must not be trusted. |
| D-14 | Supported OS: **Windows 10 21H2+ and Windows 11, x64**. | Matches .NET 10 and touch hardware expectations; Windows-only product. |
| D-15 | Toggle widget state is **runtime-only** in M1 (resets on restart). | No hidden persistence semantics; can be added later (RF-045). |
| D-16 | Log rotation: **5 files × 2 MB**. | Bounded disk usage (~10 MB) for a long-running utility. |
| D-17 | First run targets the **primary monitor**; all discovered monitors are logged with identity fields so the user can pin the touchscreen; monitor-picker UI (RF-093) is **supported via M4** (was frozen under D-24; see D-25). | Predictable, visible first run; no risky guessing; robust pinning remains config-driven. |
| D-18 | Pages exist in schema v1 (RF-037); switching UI deferred to M2. | Avoids schema churn while keeping M1 UI simple. |
| D-19 | Schema v1 contains only M1 features; hotkeys/themes fields arrive via future migrations. | No dead/ambiguous fields in the initial schema; exercises the migration path when needed. |
| D-20 | External processes launched by actions are not tracked or killed on shutdown. | Killing user-launched apps would be wrong; shutdown does not terminate external processes. |
| D-21 | Single instance via a named mutex (per session) plus a registered window message / hidden message window for signaling; behavior fixed by RF-001/RF-002, exact mechanism may be adjusted in the plan. | Standard, dependency-free Windows approach. |
| D-22 | The M1 publish gate is a working single-file, self-contained `win-x64` build; if LibreHardwareMonitor native assets conflict with single-file packaging, fall back to `IncludeNativeLibrariesForSelfExtract` or a non-single-file self-contained layout (still self-contained). | Keeps RF-088 verifiable without blocking on a packaging detail the user explicitly deferred; R-5 tracks the risk. |
| D-23 | **Product descope (2026-10-09):** macros/sequences (RF-054), macro cancellation (RF-055), repeat-while-held (RF-042), the standalone `delay` action, **HTTP action (RF-056) and HTTP safety constraints (RF-087)**, and **shell-open of files/folders/URLs** are **dropped from the product plan (not deferred); backlog / not planned**. The action model is one action per widget (RF-047); **the final M2 action set is open program (existing run-command) + send key combination** (send-keys, RF-057), with global/local hotkeys (RF-058..RF-060). | The macro/HTTP/shell-open surfaces were speculative complexity the user does not need; a single key-combination action plus the existing safe program-launch covers the real requirement far more simply. IDs are retained for stability but marked dropped. |
| D-24 | **Product scope freeze (2026-10-09):** the product is frozen to a **Core Release** = the M1 core plus **Sparkline/graph + bounded telemetry history** (RF-041, RF-069, RNF-012), **global hotkeys** (RF-058/RF-059), **send-keys** (RF-057), and **visual customization** (RF-043 appearance only: label/icon/colors/font/opacity; the schema-v3 appearance fields). Everything else is **frozen backlog**: its code may remain in the build but is **unsupported and unmaintained** and will not be extended — RF-040 Slider, RF-043 non-appearance parts, RF-044, RF-045, RF-060, RF-070, RF-071, RF-082, RF-083, RF-091..RF-093, and M3/Later RF-046/RF-061/RF-072/RF-084. Already-dropped items (RF-042/054/055/056/087, `delay`, shell-open) stay dropped. No M3/Later work is planned. | Freezes scope for a stable, supportable core release; already-built backlog code is kept in the build without any maintenance or delivery obligation. IDs are retained and not renumbered. |
| D-25 | **M4 promotion (2026-10-10):** M4 ("Promote & Polish") **un-freezes and officially supports** the implemented backlog — RF-040, RF-043 (non-appearance), RF-044, RF-045, RF-060, RF-070, RF-071, RF-082, RF-083, RF-091, RF-092, RF-093 — via verification against their existing M2 sub-requirements plus the gap fixes and cohesion/polish in `specs/004-promotion/spec.md`. The **supported set is now everything implemented except `RF-072` (skipped) and the dropped set** (RF-042/054/055/056/087, `delay`, shell-open). RF-046/061/084 remain supported (M3). No new feature is invented; no ID is renumbered or retagged; schema stays **v4**. Supersedes the frozen status of D-24 for the promoted IDs only. | The M2 code for those features was fully implemented and tested; M4 makes their support honest (verification + gap-fix + declaration) without reopening M1/M2/M3 semantics. IDs remain stable. |

## 9. M1 acceptance criteria

Each criterion is verifiable by automated tests (A), manual check (M), or both.

- **AC-01 (A) Build & tests.** The solution builds with the .NET 10 SDK; `dotnet test` passes. Tests cover: configuration load states and migration, monitor matching priority, action parameter building (incl. hostile strings), log redaction of sensitive parameters (RF-085), snapshot aggregation with a fake sensor source, and widget view models. (RNF-006)
- **AC-02 (M) Startup & tray.** As a standard user, the app starts with no UAC prompt; the tray icon appears within ~3 s; the log contains startup entries. (RF-005, RF-086, RNF-005)
- **AC-03 (M) Panel placement.** With the configured monitor connected, the panel opens borderless, covering the monitor's full bounds; it is absent from the taskbar and from Alt-Tab. (RF-014, RF-019, RF-028)
- **AC-04 (M) Focus preservation.** With another app (e.g., Notepad, or a windowed game) focused and being typed into, launching TouchPanel and showing/hiding the panel from the tray never changes the foreground window; typing continues uninterrupted; touching/clicking panel widgets does not move keyboard focus. (RF-018, RF-024)
- **AC-05 (M) Missing monitor.** Pointing config at a disconnected monitor's identity produces: no panel on any other monitor, a warning log, a tray notification, and a running app; reconnecting the monitor makes the panel appear automatically on it. (RF-016, RF-017, RF-022)
- **AC-06 (A+M) Monitor identity stability.** Unit tests cover the priority order, ambiguous-match tiebreak, and no-match case; manually, reordering/renumbering displays in Windows does not change which physical monitor is selected. (RF-016)
- **AC-07 (M) Always-on-top.** With `alwaysOnTop: true` the panel stays above normal windows and still never activates; with `false` it can be covered by other windows. (RF-018, RF-020)
- **AC-08 (M) DPI & small display.** At 100%, 150% (or 125%) and 200% scaling, layout is crisp (no bitmap stretching) and hit-testing matches visuals; changing the target monitor's scaling while the app is running re-lays out correctly without restart; the default layout fits a 1024×600 monitor at 100% and remains usable at elevated scaling. (RF-021, RF-022, RF-023, RNF-015)
- **AC-09 (M) Display hot-plug.** Disconnecting the target monitor does not crash the app and behaves per RF-017; reconnecting restores the panel on it. (RF-017, RF-022)
- **AC-10 (M) Touch operation.** Tapping a Button triggers its action; tapping a Toggle flips state and triggers once; the Gauge shows live values; all of this works without keyboard focus, with immediate visual feedback, and with interactive targets that meet the ≥ 40×40 DIP default. (RF-030, RF-031, RF-033, RF-035)
- **AC-11 (M+A) Actions.** A configured Run-command action launches the target executable with arguments and working directory respected (A: ProcessStartInfo construction unit tests; M: real launch); a bad path produces a log entry, a transient widget error state, and a stable app; media-key, volume, and mute actions audibly work in a real player. (RF-036, RF-048, RF-049, RF-050)
- **AC-12 (A+M) Configuration first run & save.** First run creates `panel.json` with `schemaVersion: 1` and a default page that fits the target monitor; edit-mode layout changes persist across restart; no partially written file is ever observed (atomic write tests). (RF-073, RF-074, RF-077)
- **AC-13 (A+M) Corrupt-config recovery.** A deliberately corrupted file is preserved as `panel.corrupt-<timestamp>.json`; the app starts from the backup/defaults with a notification; the last known valid configuration is not destroyed. (RF-075, RF-078)
- **AC-14 (A+M) Future schema.** A file with `schemaVersion: 99` is not overwritten or interpreted; the app logs, notifies, and runs from backup/defaults. (RF-075)
- **AC-15 (A) Migration.** Test fixtures: an older supported version migrates, validates, saves, and preserves the original; a failing migration follows the invalid path and leaves the source file intact. (RF-076)
- **AC-16 (M) Telemetry unavailable.** With the hardware library unable to initialize (absent driver / simulated failure), the app continues; Gauge widgets show values as unavailable; failures are logged at a throttled rate; no crash. (RF-065, RF-068)
- **AC-17 (M) Telemetry cadence & idle cost.** Polling measured at ~1 Hz; idle CPU measured with the panel visible and with the panel hidden stays within RNF-001; Gauge updates never exceed the snapshot rate and do not re-render unchanged values. (RF-038, RF-064, RNF-001, RNF-003)
- **AC-18 (M) Single instance.** Starting the app while it is already running produces no second panel/tray icon; the second process exits; the running instance's panel is shown if it was hidden. (RF-001, RF-002)
- **AC-19 (M) Close & exit lifecycle.** Closing the panel window hides it with the one-time notification (shown exactly once across restarts); tray Exit shuts down cleanly: monitoring disposed, config saved if dirty, tray removed, shutdown logged, process exits within the budget. (RF-006, RF-007)
- **AC-20 (M) Startup registration.** Toggling "start with Windows" creates/removes the per-user registration; a simulated failure is logged and notified without crashing. (RF-003, RF-004)
- **AC-21 (A+M) Exception handling.** An injected fatal path on the dispatcher is logged before termination; an isolated subsystem failure (e.g., telemetry thread) leaves the app running and is visible in logs; log rotation/retention is enforced, and an unwritable log directory degrades without crashing. (RF-008, RF-009, RF-010, RF-011, RF-012)
- **AC-22 (M) Deployment.** The stated `dotnet publish` command produces a runnable single-file executable; a smoke test (tray + panel on the configured monitor) passes. (RF-088, RF-089)
- **AC-23 (M) Soak.** An 8-hour continuous run completes without crash and without unbounded memory growth per RNF-002. (RNF-002)
- **AC-24 (M) Edit mode.** The tray toggles edit mode; entering may activate the panel (D-6); drag moves and the resize handle resizes with bounds and minimum-size clamping; Esc exits; arrow keys nudge by 1 DIP; exiting restores non-activating behavior and persists changes. (RF-026, RF-027, RF-039)
- **AC-25 (M) Multi-monitor coexistence.** The panel appears only on the target monitor, and windows/interaction on other monitors are unaffected. (RF-029)
- **AC-26 (M) Tray resilience.** Killing and restarting Explorer re-registers the tray icon; starting the app while Explorer is unavailable retries registration with logging and neither crashes nor exits. (RF-013)

---

## 10. Failure cases and required behavior

| ID | Scenario | Required behavior | Refs |
|---|---|---|---|
| F-01 | Target monitor absent at startup | No crash; no placement on an unexpected monitor; warning log; tray notification; stay running; auto-show when it appears (or notify on configured fallback) | RF-017 |
| F-02 | Target monitor removed at runtime | Panel hidden/removed from the vanished monitor; log; notification; recover per F-01 | RF-017, RF-022 |
| F-03 | Display added/removed, resolution or DPI changed | Recompute bounds and layout; clamp widgets; no crash, no orphaned windows | RF-022 |
| F-04 | `panel.json` unparsable/structurally invalid | Preserve as `panel.corrupt-<timestamp>.json`; recover from backup/defaults; notify; never overwrite the source with garbage | RF-075, RF-078 |
| F-05 | `schemaVersion` newer than supported | Do not overwrite/interpret; log; notify; run from backup/defaults | RF-075 |
| F-06 | Migration failure | Follow corrupt path; source file untouched; backup preserved | RF-076, RF-075 |
| F-07 | Save failure (locked file, disk full, permissions) | No partial file; keep in-memory state; log; notify; retry next save/shutdown | RF-077 |
| F-08 | Save failure during shutdown | Log; attempt an emergency copy (e.g., `panel.recovery.json`); never block shutdown indefinitely | RF-077, RF-007 |
| F-09 | `state.json` missing/corrupt | Non-fatal; regenerate; log at low severity | RF-073 |
| F-10 | LibreHardwareMonitor init failure | Snapshots with unavailable values; throttled log; app fully usable | RF-065, RF-068 |
| F-11 | Sensor read error / NaN / sensor missing | Affected fields unavailable; throttled log; no UI crash | RF-065 |
| F-12 | Monitoring background failure/crash | Subsystem marked degraded and restarted or disabled; app continues | RF-012, RF-065 |
| F-13 | Action target missing/access denied | Widget error state (RF-036); structured log; app stable | RF-050, RF-036 |
| F-14 | Input synthesis (media/volume) fails | Widget error state; log; app stable | RF-050 |
| F-15 | Global hotkey already taken (M2) | Log combination; skip; continue other registrations; aggregated notification; no retry loop | RF-059 |
| F-16 | Startup registration read/write/remove failure | Log; notify; no crash; attempt self-heal on next start | RF-004 |
| F-17 | Second-instance signaling fails | Second process logs and exits without creating a panel | RF-002 |
| F-18 | Explorer restart or initial tray registration failure (Explorer not ready at logon auto-start) | Re-register with bounded retry/backoff; log each failure; never crash or exit over tray registration | RF-013 |
| F-19 | Log directory unwritable / write failure | Degrade to alternate sink; never crash; never block UI | RF-010 |
| F-20 | Unexpected exception (dispatcher/background) | Logged via global handlers; continue when safe; never silent death | RF-011 |
| F-21 | Invalid config reload (M2) | Reject whole reload; keep running configuration; log; notify; no partial apply | RF-082 |
| F-22 | HTTP action failure/timeout | **Dropped (2026-10-09):** RF-056/RF-087 are backlog / not planned, so no HTTP failure behavior applies | RF-056, RF-087 (dropped) |

## 11. Test strategy

**Environment prerequisite.** A .NET 10 SDK must be installed on the development machine (it was not detected during spec creation; see risk R-6). All tests run on Windows x64.

**Unit tests (no hardware, no drivers, no elevation):**

- Configuration: parse/validation of the four load states, migration success/failure fixtures, atomic-write behavior (including simulated failure between temp write and replace), backup rotation, corrupt-file preservation, two-tier entry validation (skip/clamp), default generation, hostile/untrusted values.
- Monitor matching: priority order, missing EDID fields, duplicate descriptors, deterministic tiebreak, no-match.
- Actions: `ProcessStartInfo` construction from configuration (discrete args, spaces/quotes, no shell wrapping), invalid/malicious parameters, unknown action types, and log redaction of sensitive parameters (RF-085).
- Monitoring: snapshot aggregation from a fake sensor source (per-core bounds, unavailable values), polling cadence with a fake clock, throttled failure logging.
- Widget view models: toggle transitions, gauge mapping/clamping/unavailable display, transient error state timing (fake clock).
- Single-instance/startup-registration logic behind interfaces (fake registry/IPC where practical).

**Integration tests (still no real hardware):** configuration store against temp directories (including concurrent save simulation), monitoring service with fake source and cancellation, action executor against a harmless local process.

**Manual verification matrix (WPF/Win32 behaviors that cannot be reliably automated in M1):** focus-stealing procedure (AC-04), placement/borderless/toolwindow (AC-03), monitor replug/reorder (AC-05, AC-06, AC-09), DPI/scaling and 1024×600 (AC-08), touch on real hardware (AC-10), always-on-top (AC-07), tray/startup/single instance/close behavior (AC-02, AC-18..AC-20), telemetry with and without drivers (AC-16, AC-17), publish smoke test (AC-22), 8-hour soak (AC-23). Manual procedures will be recorded in the plan/tasks phase.

**Gate.** After each implementation task, `dotnet build` and `dotnet test` must succeed; manual checks are performed for the UI/interop acceptance criteria before milestone completion. M1 does not introduce UI automation frameworks; testability is concentrated in the core logic via interfaces at real boundaries.

## 12. Risks

| ID | Risk | Mitigation / note |
|---|---|---|
| R-1 | Focus stealing is subtle: exclusive-fullscreen games, topmost interactions, anti-cheat overlays may behave differently despite `WS_EX_NOACTIVATE` | Manual test matrix across windowed and fullscreen-borderless games; documented known limitations; behavior centralized in `Interop/` for targeted fixes |
| R-2 | LibreHardwareMonitor sensor availability depends on elevation/drivers/OS | Everything optional by design (RF-065, RF-068); UI shows unavailable; optional elevated companion only reconsidered later |
| R-3 | Monitor identity can be ambiguous (virtual displays, missing EDID, duplicated models) | Multi-field priority matching (RF-016), deterministic tiebreaks, logged decisions, user-overridable config |
| R-4 | WPF + PerMonitorV2 physical-placement pitfalls (DIU vs pixels) | Window placement via Win32 `SetWindowPos` in physical coordinates behind an `Interop/` helper; explicit manual DPI tests |
| R-5 | Single-file publish vs LibreHardwareMonitor native driver assets (extraction paths) | Test the publish command early in M1; fallback per D-22 (`IncludeNativeLibrariesForSelfExtract` or non-single-file self-contained) if extraction conflicts appear |
| R-6 | .NET 10 SDK availability on the implementation shell | Resolved: .NET 10.0.401 SDK is installed (`C:\Program Files (x86)\dotnet`); verify `dotnet --list-sdks` works in the implementation shell before T-01 (PATH may need an environment refresh) |
| R-7 | Tray icon may land in Windows 11's hidden-icons overflow | Document; users can pin it; future optional hotkey to open the panel once hotkeys exist (M2) |
| R-8 | Topmost panel may fight exclusive-fullscreen content on the same monitor | `alwaysOnTop` default off (D-3); documented; per-monitor test |
| R-9 | Touch hardware/latency variants across devices | Validate on the actual target touchscreen; touch paths use standard WPF touch/mouse promotion |

## 13. Open questions

**None blocking.** The specification is fully determined by the original request together with the decisions in section 8. The most product-visible overridable defaults are:

- **D-1** — M1 includes a minimal edit mode (move/resize/persist).
- **D-2** — Missing monitor default: stay hidden with notification; `fallback: primary` opt-in, never silent.
- **D-3** — `alwaysOnTop` defaults to off.
- **D-6** — Entering edit mode may activate the panel; normal mode never activates.
- **D-15** — Toggle widget state is runtime-only in M1.
- **D-17** — First run targets the primary monitor; monitor-picker UI deferred to M2.

If any default should differ, it can be changed at approval; otherwise the spec stands as written.
