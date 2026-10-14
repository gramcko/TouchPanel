# TouchPanel — Tasks 001: Foundation (M1)

| Field | Value |
|---|---|
| Tasks ID | 001-foundation |
| Status | Draft for human approval (Phase 3) |
| Date | 2026-10-08 |
| Inputs | `specs/001-foundation/spec.md` (approved), `specs/001-foundation/plan.md` |
| Execution rule | After every task: `dotnet build` and `dotnet test` must succeed. No M2/M3/Later work is allowed in any task. |

Task granularity is a **vertical slice** (a coherent feature or subsystem), not one class per task. Each task lists its purpose, files, dependencies, implementation notes, tests, acceptance criteria, and requirement IDs.

## Overview

| ID | Title | Depends on | Requirements |
|---|---|---|---|
| T-01 | Solution skeleton, manifest, theme resources, green baseline | — | RNF-006, RNF-010, RNF-011, RF-021 (manifest), RF-086/089/090 (baseline) |
| T-02 | Logging, global exception handling, subsystem health | T-01 | RF-008..RF-012, RF-051/RF-085 (redaction helper), RNF-007, RNF-013 (contract) |
| T-03 | Single instance, message window, lifecycle/shutdown coordinator | T-02 | RF-001, RF-002, RF-007 (partial), RNF-005 (partial) |
| T-04 | Configuration store: schema v1, validation, migration, atomic save, recovery | T-03 | RF-015, RF-020, RF-023 (defaults), RF-034, RF-037, RF-047 (validation), RF-073..RF-081, RNF-006, RNF-008 |
| T-05 | Tray icon, notifications, close-to-tray support, Explorer resilience | T-04 | RF-005, RF-007 (tray), RF-013, RF-024 (menu), RF-077 (notification path) |
| T-06 | Start-with-Windows registration and self-heal | T-04 | RF-003, RF-004, RF-086 |
| T-07 | Monitor enumeration, stable identity matching, missing-monitor policy | T-04 | RF-015, RF-016, RF-017 (decision), RF-022 (detection) |
| T-08 | Panel window: borderless, no-activate, placement, DPI, display changes, close-to-tray | T-05, T-07 | RF-006, RF-014, RF-017 (apply), RF-018..RF-024, RF-028, RF-029, RNF-004, RNF-005, RNF-008, RNF-015 |
| T-09 | Action system: safe launch, media/volume/mute, executor, failure contract | T-02 | RF-047 (runtime), RF-048..RF-053, RF-081 (execution side), RF-085, RNF-014 |
| T-10 | Widget host: Button, Toggle, Label, touch/theme polish, error feedback | T-08, T-09 | RF-023 (rendering), RF-025, RF-030..RF-037, RF-050 (surface), RNF-009 |
| T-11 | Telemetry service (LibreHardwareMonitor adapter) and Gauge widget | T-10 | RF-033 (Gauge), RF-038, RF-062..RF-068, RNF-003, RNF-004, RNF-013 |
| T-12 | Edit mode: move/resize, keyboard, persistence, tray toggle | T-10 | RF-005 (Edit Mode item), RF-026, RF-027, RF-039 |
| T-13 | Deployment validation: single-file publish and native-asset handling | T-12 | RF-088, RF-089, RF-090 |
| T-14 | Manual verification matrix, performance, and acceptance run | T-13 | RNF-001, RNF-002, RNF-005 (final), all ACs |

---

## T-01 — Solution skeleton and green baseline

**Purpose:** Create the solution and project structure so every later task starts from a building, tested baseline.

**Files/directories:**
- `TouchPanel.sln`
- `TouchPanel/TouchPanel.csproj` — `net10.0-windows`, `UseWPF=true`, nullable + implicit usings enabled, assembly name TouchPanel
- `TouchPanel/app.manifest` — PerMonitorV2 DPI awareness, supported OS declarations (Win10/11)
- `TouchPanel/App.xaml`, `App.xaml.cs` — minimal startup (placeholder window)
- `TouchPanel/MainWindow.xaml(.cs)` — placeholder for now; becomes the panel in T-08
- `TouchPanel/Theme/Colors.xaml`, `Theme/Styles.xaml` — scaffold merged into `App.xaml`
- `TouchPanel.Tests/TouchPanel.Tests.csproj` — xUnit, references the app project

**Dependencies:** none.

**Implementation notes:** create the folder layout from plan section 1.1 as code arrives (no placeholder classes); verify `dotnet --list-sdks` shows .NET 10 before building (PATH refresh may be required — SDK is installed at `C:\Program Files (x86)\dotnet`).

**Tests:** one smoke test proving the test project runs; baseline reference check.

**Acceptance criteria:** `dotnet build` and `dotnet test` succeed; launching the app opens an empty placeholder window without errors; the generated manifest contains PerMonitorV2.

**Requirements:** RNF-006 (test infrastructure), RNF-010/RNF-011 (structure, dependencies), RF-021 (manifest groundwork), RF-086, RF-089, RF-090 (baseline).

## T-02 — Logging, global exception handling, subsystem health

**Purpose:** Make the application diagnosable from the start: structured rotating logs, global handlers, and a degraded-subsystem contract for isolation.

**Files/directories:**
- `TouchPanel/Services/AppPaths.cs` — `%LOCALAPPDATA%\TouchPanel\logs` and `%APPDATA%\TouchPanel` (injectable root for tests)
- `TouchPanel/Services/Logging/` — Serilog configuration (rolling file, 5 × 2 MB), startup/shutdown entries, `LogRedactor`
- `TouchPanel/Services/Health/SubsystemHealth.cs` — register/mark degraded/restore + log
- `TouchPanel/App.xaml.cs` — initialize logging first; wire `DispatcherUnhandledException`, `AppDomain.CurrentDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`

**Dependencies:** T-01.

**Implementation notes:** log directory failure falls back to `Debug` output and never blocks the UI (RF-010); redaction helper is the single place sensitive values are scrubbed (used by actions in T-09).

**Tests:** `LogRedactor` cases (tokens/passwords/keys never appear); `SubsystemHealth` transitions; log initialization/write against a temp path; rotation settings asserted via configured options.

**Acceptance criteria:** real log file contains startup and shutdown entries; a simulated dispatcher exception is logged before being handled; unwritable log path does not crash or stall startup.

**Requirements:** RF-008, RF-009, RF-010, RF-011, RF-012 (contract), RF-051/RF-085 (redaction helper), RNF-007, RNF-013 (contract).

## T-03 — Single instance, message window, lifecycle/shutdown coordinator

**Purpose:** Guarantee one running instance per session, define second-launch signaling, and provide the ordered shutdown path every subsystem plugs into.

**Files/directories:**
- `TouchPanel/Interop/MessageWindow.cs` — message-only window (class registration, WndProc, `TaskbarCreated` hook reused by T-05)
- `TouchPanel/Services/SingleInstance/SingleInstanceService.cs` — `Local\TouchPanel.SingleInstance` mutex, `RegisterWindowMessage("TouchPanel.ShowPanel.v1")`, `FindWindow` + `PostMessage` signaling
- `TouchPanel/Services/AppLifecycle/AppLifecycleCoordinator.cs` — ordered shutdown participants with ~3 s budget
- `TouchPanel/App.xaml.cs` — startup sequence and shutdown wiring

**Dependencies:** T-02.

**Implementation notes:** the second process logs and exits without creating any window; the primary raises a "show panel requested" event (the window subscribes in T-08); signaling failure is logged and non-fatal.

**Tests:** coordinator order/timeout with fake participants; single-instance decision logic with a fake mutex/signal seam.

**Acceptance criteria:** unit tests green; a second launch while running exits without a panel (verified manually in T-14/MV-09); shutdown coordinator completes within budget with all participants invoked.

**Requirements:** RF-001, RF-002, RF-007 (partial), RNF-005 (partial).

## T-04 — Configuration store

**Purpose:** Implement the complete M1 configuration subsystem: schema v1, default generation, four load states, migration framework, validation tiers, atomic save, backup/recovery, separate runtime state.

**Files/directories:**
- `TouchPanel/Config/` — `PanelConfig` + section DTOs (`monitor`, `window`, `startup`, `pages`, widgets, actions), `ActivePageId`, `ConfigPaths`, `ConfigLoadResult/ConfigState`
- `TouchPanel/Config/Json/` — `System.Text.Json` setup, custom `type`-discriminated converters for widgets/actions
- `TouchPanel/Config/Validation/` — `ConfigValidator` (pure, two-tier per RF-079)
- `TouchPanel/Config/Migrations/` — `IConfigMigration`, `MigrationRunner` (ordered, validated)
- `TouchPanel/Config/StateStore.cs` — `state.json` (non-fatal if missing/corrupt)
- `TouchPanel/Config/JsonConfigStore.cs` — load/save, atomic write (temp + `File.Replace`), `panel.backup.json` rotation, corrupt preservation
- Default config generator (fits 1024×600, includes Label + a few Buttons + a Gauge placeholder widget)

**Dependencies:** T-03.

**Implementation notes:** schema v1 has no telemetry-interval field (fixed 1 Hz; RF-071 is M2 via migration); config is read at startup and written only on defined triggers; no file watchers or polling; all paths injected for tests.

**Tests:** the full config test inventory from plan section 6.2 (states, migration fixtures, atomic write incl. simulated interruption, backup/recovery, corrupt preservation, two-tier validation incl. disabled-widget for bad action binding, `activePageId` fallback, defaults fit, hostile values, state.json resilience).

**Acceptance criteria:** all config unit tests green; `dotnet test` proves every RF-075 state; no partially written file in simulated failures.

**Requirements:** RF-015, RF-020, RF-023 (defaults), RF-034, RF-037, RF-047 (validation), RF-073..RF-081, RNF-006, RNF-008.

## T-05 — Tray icon, notifications, Explorer resilience

**Purpose:** Provide the tray presence, menu, and notification channel that the rest of the app (and the user) depends on for lifecycle management.

**Files/directories:**
- `TouchPanel/Interop/TrayIcon.cs` — `Shell_NotifyIcon` wrapper (add/modify/delete, `NIF_INFO` balloons, `WM_TRAYICON`, `TaskbarCreated`)
- `TouchPanel/Services/Tray/TrayService.cs` — icon lifecycle, menu (Show/Hide Panel, Open Configuration Folder, Exit; Edit Mode item added in T-12), bounded retry/backoff for registration
- `TouchPanel/Services/Notifications/IUserNotifier.cs` + `TrayNotifier.cs` (log-only fallback used by earlier tasks until this task lands)

**Dependencies:** T-04.

**Implementation notes:** registration failures retry with backoff and are logged (RF-013); balloons are non-activating; "Open Configuration Folder" opens the resolved `%APPDATA%\TouchPanel` directory; Exit invokes the T-03 shutdown coordinator.

**Tests:** retry/backoff policy with fake clock; menu command mapping with fakes; notification-once flag behavior; notifier failures never throw.

**Acceptance criteria:** unit tests green; manual smoke (deferred to MV-07): icon appears, menu opens, Exit terminates cleanly; Explorer restart re-registers the icon (shape check in T-14).

**Requirements:** RF-005 (menu except Edit Mode), RF-007 (tray removal), RF-013, RF-024 (menu path), RF-077 (notification path).

## T-06 — Start-with-Windows registration and self-heal

**Purpose:** Implement opt-in auto-start with the per-user, no-elevation mechanism and graceful failure handling.

**Files/directories:**
- `TouchPanel/Services/Startup/IStartupRegistration.cs`, `RegistryStartupRegistration.cs` — HKCU `Software\Microsoft\Windows\CurrentVersion\Run`, value `TouchPanel`
- `TouchPanel/App.xaml.cs` — apply `startup.startWithWindows` at startup; self-heal stale/missing entries

**Dependencies:** T-04.

**Implementation notes:** command is the quoted current executable path; read/compare/update; failures log + tray notification once T-05 exists (before that, log-only via `IUserNotifier`); M1 toggles the setting by editing `panel.json` (UI toggle is M2/RF-092).

**Tests:** fake registry seam: create/remove/read, stale-path detection, failure paths, never-throw guarantee.

**Acceptance criteria:** unit tests green; manual (MV-08): toggling the config value adds/removes the Run entry; moved executable self-heals with a log entry.

**Requirements:** RF-003, RF-004, RF-086.

## T-07 — Monitor enumeration, identity matching, missing-monitor policy

**Purpose:** Resolve the configured target monitor robustly (no index reliance) and define what happens when it is unavailable.

**Files/directories:**
- `TouchPanel/Interop/Monitors/Win32MonitorEnumerator.cs` — `QueryDisplayConfig` (target device path, friendly name, source GDI name), `EnumDisplayMonitors`/`GetMonitorInfo` (bounds, work area, primary), `EnumDisplayDevices` (monitor device IDs)
- `TouchPanel/Services/Monitors/MonitorDescriptor.cs`, `MonitorMatcher.cs` — pure matching per RF-016 priority with deterministic tiebreaks and reason strings
- `TouchPanel/Services/Monitors/MonitorSelectionService.cs` — combines enumerator + matcher + config block, returns resolved monitor or unavailable with reason

**Dependencies:** T-04.

**Implementation notes:** all discovered monitors are logged once with identity fields (D-17); no polling — availability is (re)evaluated on demand and on display-change events (wired in T-08); no WMI/SetupAPI in M1; serial-based matching is best-effort from the device path when present.

**Tests:** matcher priority chain, missing EDID/device fields, duplicate models with deterministic tiebreak, no-match, reason strings; selection service with fake enumerator.

**Acceptance criteria:** all matcher tests green; selection logs the chosen monitor and why; unavailable produces a policy object T-08 can act on.

**Requirements:** RF-015, RF-016, RF-017 (decision logic), RF-022 (detection).

## T-08 — Panel window

**Purpose:** Deliver the window core: borderless fullscreen panel on the target monitor with no focus stealing, correct DPI/placement, display-change reactions, and close-to-tray.

**Files/directories:**
- `TouchPanel/MainWindow.xaml(.cs)` — `WindowStyle=None`, `ResizeMode=NoResize`, `ShowInTaskbar=false`, `ShowActivated=false`; `SourceInitialized` applies `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`
- `TouchPanel/Interop/WindowStyles.cs` — `SetWindowLongPtr`/`SetWindowPos`/`ShowWindow` helpers
- `TouchPanel/Services/Display/DisplayChangeWatcher.cs` — `SystemEvents.DisplaySettingsChanged` (and `WM_DPICHANGED` handling)
- `TouchPanel/ViewModels/MainWindowViewModel.cs` — show/hide state, topmost flag, monitor availability, edit-mode flag placeholder (used in T-12)
- `App` wiring: show/hide requests from tray (T-05) and second launch (T-03)

**Dependencies:** T-05, T-07.

**Implementation notes:** placement via `SetWindowPos` in physical pixels with `SWP_NOACTIVATE`; show via `ShowWindow(SW_SHOWNOACTIVATE)`; never `Activate`/`SetForegroundWindow` in normal mode; always-on-top toggled with `HWND_TOPMOST/NOTOPMOST` + `SWP_NOACTIVATE`; close event hides to tray with the one-time notification (state flag from T-04), app stays alive; when the target monitor is unavailable, apply RF-017/D-2 (stay hidden, notify, auto-show on return; fallback `primary` only with notification); manual Show while unavailable follows the same policy.

**Tests:** policy decision table for unavailable monitor + show requests (pure); topmost flag mapping; one-time notification flag; auto-show logic on monitor return (with fakes).

**Acceptance criteria:** manual (MV-02, MV-04, and focus checks in MV-01): full-monitor borderless coverage, absent from taskbar/Alt-Tab, no activation on show/interaction, survive monitor unplug/replug, live DPI change relayout, close hides to tray.

**Requirements:** RF-006, RF-014, RF-017 (apply), RF-018..RF-024, RF-028, RF-029, RNF-004, RNF-005, RNF-008, RNF-015.

## T-09 — Action system

**Purpose:** Implement the M1 action execution path end to end with safe process launching, media/volume/mute input actions, and a failure contract widgets can surface.

**Files/directories:**
- `TouchPanel/Config/ActionConfig.cs` — action config DTOs (run command, media key, volume, mute), defined once in T-04 and reused by `Actions/` (post-M1 correction, 2026-10-09)
- `TouchPanel/Actions/Handlers/` — `RunCommandHandler`, `MediaKeyHandler`, `VolumeHandler`, `MuteHandler` (media keys, volume step, mute via `SendInput` with `VK_MEDIA_*`/`VK_VOLUME_*`; volume set uses Core Audio `IAudioEndpointVolume.SetMasterVolumeLevelScalar` under `Interop/Audio` — DD-8 post-M1 correction, 2026-10-09)
- `TouchPanel/Actions/ActionExecutor.cs` + `IActionExecutor`, `ActionExecutionResult`
- `TouchPanel/Actions/ProcessLaunchRequestFactory.cs` (pure) + `Services/Processes/SystemProcessLauncher.cs` (`IProcessLauncher`)
- `TouchPanel/Interop/Input/SendInputHelper.cs`

**Dependencies:** T-02.

**Implementation notes:** `ProcessStartInfo` with `UseShellExecute=false`, explicit executable, discrete `ArgumentList`; no shell wrapping, no URL/document launching (D-7); handlers never wait for processes (D-20); unknown/invalid action types fail validation at load (T-04) and fail fast at execution; failures are logged with redaction (T-02 helper) and returned as results; no WPF types in `Actions/` (RF-053).

**Tests:** `ProcessLaunchRequestFactory` against hostile strings/spaces/quoting; executor dispatch and result mapping with fake handlers/launcher; media/volume VK mapping; redaction of parameters; unknown type behavior.

**Acceptance criteria:** all action unit tests green; a configured run-command launches a real executable with args/workdir; a bad path returns a failure result and leaves the app stable (manual, MV-10).

**Requirements:** RF-047 (runtime), RF-048..RF-053, RF-081 (execution side), RF-085, RNF-014.

## T-10 — Widget host, Button, Toggle, Label

**Purpose:** Render a config-driven page of widgets with touch-first interaction, action binding, and visible failure feedback.

**Files/directories:**
- `TouchPanel/ViewModels/PageViewModel.cs`, `WidgetViewModelBase.cs`
- `TouchPanel/Widgets/` — `WidgetRegistry`, `WidgetTemplateSelector`, `ButtonWidget/`, `ToggleWidget/`, `LabelWidget/` (View + ViewModel each)
- `TouchPanel/Views/` — page host (ItemsControl + Canvas), DataTemplates
- `TouchPanel/Theme/Styles.xaml` — touch styles: min 40×40 DIP targets, pressed/disabled/error visuals, no hover-only affordances

**Dependencies:** T-08, T-09.

**Implementation notes:** absolute geometry from config in DIPs; selector/registry maps `type` → VM + template; Button fires once per completed tap; Toggle flips state exactly once and executes its action (initial state from config, runtime flips not persisted — D-15); action failures produce a ~2 s transient error state (driven by `TimeProvider`); widgets with invalid action bindings load disabled with an error indicator (RF-047/RF-079); no per-instance code-behind.

**Tests:** toggle state transitions + no persistence; action invocation count per tap/flip; error-state timing with fake `TimeProvider`; disabled-widget rendering state; registry/template selection for known and unknown types; min-size defaults.

**Acceptance criteria:** for the default page on the target monitor: widgets render at configured positions, respond to touch and mouse, show feedback, execute bound actions, and surface failures per RF-036 (manual MV-05 plus unit tests).

**Requirements:** RF-023 (rendering), RF-025, RF-030..RF-037, RF-050 (surface), RNF-009.

## T-11 — Telemetry service and Gauge widget

**Purpose:** Provide failure-tolerant ~1 Hz application-level telemetry and the first data-driven widget, proving the background/UI boundary.

**Files/directories:**
- `TouchPanel/Monitoring/SensorSnapshot.cs`, `IMonitoringService.cs`, `MonitoringService.cs` (PeriodicTimer loop, cancellation, disposal, failure throttling via `TimeProvider`)
- `TouchPanel/Monitoring/LibreHardwareMonitorAdapter.cs` — the only file referencing LibreHardwareMonitorLib; maps sensors to snapshot fields with explicit availability
- `TouchPanel/Widgets/GaugeWidget/` — View + ViewModel consuming snapshots via dispatcher marshalling
- `TouchPanel/App.xaml.cs` — start/stop monitoring with lifecycle

**Dependencies:** T-10.

**Implementation notes:** poll ~1 Hz regardless of panel visibility (D-10); never touch the UI thread from the adapter; all open/read errors produce unavailable values and throttled logs; no elevation requests ever (RF-068); Gauge updates only when values change and never faster than the snapshot rate; ring buffers/history are M2 (RF-069) and must not be implemented here.

**Tests:** snapshot aggregation from fake sources (per-core bounds, unavailable/NaN fields), cadence with `TimeProvider`, throttled failure logging, cancellation/disposal leaves no live timers, Gauge mapping/clamping/unavailable display, update-on-change only; layer-purity test extended to `Monitoring/`.

**Acceptance criteria:** with LHM unable to initialize, the app runs, the Gauge shows values as unavailable, and logs are throttled; with working sensors, the Gauge updates about once per second; no UI stalls (manual MV-10/MV-13 plus unit tests); a development smoke verifies the LHM assembly works before publish (R-5).

**Requirements:** RF-033 (Gauge), RF-038, RF-062..RF-068, RNF-003, RNF-004, RNF-013.

## T-12 — Edit mode

**Purpose:** Let the user move and resize widgets by touch/mouse and persist the layout, completing the M1 customization loop.

**Files/directories:**
- `TouchPanel/ViewModels/EditModeService.cs` (state, dirty tracking)
- `TouchPanel/Views/EditOverlay` — per-widget drag/resize `Thumb`s (shared touch/mouse path)
- `TouchPanel/MainWindow.xaml(.cs)` — Esc exit, arrow-key nudge, activation handling for edit mode (D-6)
- `TouchPanel/Services/Tray/TrayService.cs` — add the Edit Mode toggle menu item (RF-005 completion)
- `TouchPanel/Config/JsonConfigStore.cs` — save geometry on edit exit through the existing store

**Dependencies:** T-10.

**Implementation notes:** geometry clamped to page bounds and a defined minimum size; entering edit mode may activate the panel (deliberate, D-6) and exiting restores non-activating show behavior; save only when dirty, atomically via T-04; add/remove/property editing must not be implemented (M2/RF-043).

**Tests:** clamping and min-size math; dirty tracking; save-on-exit invocation (fake store); Esc/arrow key commands; non-activating state restored (VM-level flag).

**Acceptance criteria:** manual MV-06: enter/exit from tray, drag/resize within limits, Esc exits, arrows nudge 1 DIP, layout survives restart, normal mode remains non-activating; unit tests green.

**Requirements:** RF-005 (Edit Mode item), RF-026, RF-027, RF-039.

## T-13 — Deployment validation

**Purpose:** Prove the M1 publish story works, including LibreHardwareMonitor native assets, with the D-22 fallback path.

**Files/directories:**
- `TouchPanel/TouchPanel.csproj` — publish properties if needed (single-file, self-extract, trimming off)
- Publish output validated from a clean folder

**Dependencies:** T-12.

**Implementation notes:** run `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`; launch from a clean directory and smoke test (tray + panel on the configured monitor + telemetry best-effort); if native assets conflict, apply D-22 fallback (`IncludeNativeLibrariesForSelfExtract=true`, or non-single-file self-contained) and document the chosen strategy; no installer (RF-089); no elevation required (RF-086).

**Tests:** no new unit tests; publish smoke is a scripted/manual check with results recorded.

**Acceptance criteria:** the documented command produces a runnable self-contained artifact whose smoke test passes on a machine without the SDK; any fallback is documented.

**Requirements:** RF-088, RF-089, RF-090.

## T-14 — Manual verification matrix, performance, and acceptance run

**Purpose:** Execute the plan's manual verification matrix (MV-01..MV-13) against the finished M1 and record pass/fail with evidence; fix defects within M1 scope.

**Files/directories:** no planned production changes; fixes as needed; results recorded (e.g., `specs/001-foundation/verification-notes.md` may be added during execution if the human wants a record).

**Dependencies:** T-13.

**Implementation notes:** execute every MV procedure (plan section 6.4): focus matrix incl. windowed/borderless game, placement/Alt-Tab, always-on-top, DPI incl. live change and 200%, touch, edit mode, tray/close/Explorer-restart, startup registration, single instance, actions, config corruption/future version, publish smoke, idle CPU visible/hidden, 8 h soak. Measure RNF-001/002/005; record results and any accepted limitation (e.g., exclusive-fullscreen).

**Tests:** MV checklist execution; `dotnet build`/`dotnet test` final green run; soak and performance captures.

**Acceptance criteria:** every AC-01..AC-26 in the spec has a pass result or a documented, human-accepted limitation; no M1 requirement remains unverified; the reviewer agent can validate the implementation against the spec afterward.

**Requirements:** RNF-001, RNF-002, RNF-005 (final), and final verification of all M1 requirements listed in plan section 8.

---

## Execution notes

- Tasks are executed in order; dependencies listed are the minimum, ordering is sequential for a single developer.
- `dotnet build` and `dotnet test` must stay green after every task; a task is not complete until its tests and listed manual checks (or explicitly deferred ones) are done.
- No M2/M3/Later requirement may be partially implemented "for later"; the spec's tags forbid it.
- The reviewer agent validates implementation against the spec without modifying code; remediation is a new task appended in a later planning pass.
