# TouchPanel — Implementation Plan 001: Foundation (M1)

| Field | Value |
|---|---|
| Plan ID | 001-foundation |
| Status | Draft for human approval (Phase 3) |
| Date | 2026-10-08 |
| Inputs | `specs/001-foundation/spec.md` (approved; 15 QA refinements applied), `specs/001-foundation/tasks.md` |
| Scope | M1 only |
| Out of scope | All M2/M3/Later work: RF-040..RF-046, RF-054..RF-061, RF-069..RF-072, RF-082..RF-084, RF-087, RF-091..RF-093, RNF-012 |

> **Descope note (2026-10-09):** the user dropped macros/sequences (RF-054), macro cancellation (RF-055), repeat-while-held (RF-042), the `delay` action, HTTP action + HTTP safety (RF-056/RF-087), and shell-open of files/folders/URLs from the product (not deferred; backlog); the action model is one action per widget. The final M2 action set is open program (existing run-command) + send key combination. These IDs are marked dropped in `spec.md` and appear in no milestone; this M1 plan is otherwise unchanged.

**M1 requirement set** (tags in the spec are authoritative; 67 functional + 14 non-functional = 81 requirements):

- RF-001..RF-039, RF-047..RF-053, RF-062..RF-068, RF-073..RF-081, RF-085, RF-086, RF-088..RF-090
- RNF-001..RNF-011, RNF-013..RNF-015

---

## 1. Architecture

### 1.1 Solution layout

```text
touch-monitor-app/
├─ TouchPanel.sln
├─ specs/001-foundation/{spec.md, plan.md, tasks.md}
├─ TouchPanel/                          net10.0-windows, UseWPF, nullable enabled
│  ├─ App.xaml / App.xaml.cs            composition root; startup/shutdown orchestration
│  ├─ MainWindow.xaml / MainWindow.xaml.cs   the panel window
│  ├─ app.manifest                      PerMonitorV2, supportedOS (Win10/11)
│  ├─ Views/                            presentation-only views (edit overlay, tray menu host if needed)
│  ├─ ViewModels/                       MainWindowViewModel, page/geometry state
│  ├─ Widgets/                          per-type View + ViewModel, WidgetRegistry, template selector
│  ├─ Actions/                          action handlers, ActionExecutor; config DTOs reused from Config/ActionConfig.cs (post-M1 correction, 2026-10-09)
│  ├─ Input/                            reserved for M2 hotkeys; no M1 code
│  ├─ Monitoring/                       SensorSnapshot, IMonitoringService, LibreHardwareMonitor adapter
│  ├─ Config/                           schema models, ConfigStore, validation, migrations, paths
│  ├─ Interop/                          all P/Invoke: monitors, window styles, tray, mutex/message window, SendInput
│  ├─ Services/                         lifecycle coordinator, single instance, startup registration, tray service, notifications, monitor matching
│  └─ Theme/                            Colors.xaml, Styles.xaml (touch-friendly defaults)
└─ TouchPanel.Tests/                    xUnit; no hardware, no drivers, temp-dir isolated
```

`Input/` exists in the layout for traceability to the approved structure but contains no M1 code (hotkeys are M2, RF-058..RF-061).

### 1.2 Responsibilities and dependency direction

- **Views → ViewModels → boundaries.** Views contain presentation only (XAML, templates, minimal code-behind for window mechanics that cannot live in a VM, e.g., `SourceInitialized`).
- **Widgets** depend on the action executor contract and `SensorSnapshot`; they are never referenced by Actions/Monitoring/Config.
- **Actions, Monitoring, Config** are WPF-free C# (no `PresentationFramework`/`PresentationCore`/`WindowsBase` types) so they are unit-testable without a UI; this is enforced by a reflection-based architecture test plus code review (RNF-010).
- **Interop** is the only place with P/Invoke (`DllImport`/`LibraryImport`). It may use WPF's `HwndSource` to host message-only windows (a deliberate, contained exception) but contains no business logic.
- **Services** orchestrate lifecycle concerns and use Interop; they are UI-framework-agnostic except `Dispatcher` marshalling helpers, which live in ViewModels/Views.
- **Theme** contains only XAML resource dictionaries.
- **Composition root:** `App.xaml.cs` constructs concrete implementations in a fixed order and wires them manually. No DI container, no service locator (RNF-010).
- **Shutdown:** one `AppLifecycle` coordinator owns an ordered list of disposable participants with a ~3 s budget (RF-007).

### 1.3 Boundaries (the only interfaces M1 introduces)

| Interface | Purpose | Implementations (M1) |
|---|---|---|
| `IMonitorEnumerator` | Enumerate monitors with stable identity + bounds | Win32 implementation in `Interop/`; fake in tests |
| `IMonitoringService` | Publish `SensorSnapshot` on a background cadence; start/stop/dispose | `MonitoringService` + internal LHM adapter; fake source in tests |
| `IConfigStore` | Load/save config with load-state result; injected paths | `JsonConfigStore`; temp-dir tests |
| `IActionExecutor` | Execute one action, return `ActionExecutionResult` | `ActionExecutor` with per-type handlers |
| `IProcessLauncher` | Start a validated process request | `SystemProcessLauncher` (wraps `Process.Start`) |
| `IStartupRegistration` | Read/write/remove the per-user startup entry | HKCU Run implementation; fake in tests |
| `IUserNotifier` | Non-blocking user-visible notifications (tray balloon) | `TrayNotifier`; log-only fake in tests |

Anything else stays concrete. `TimeProvider` from the BCL is used for throttling/error-state timing instead of a custom clock interface.

### 1.4 Deliberate exclusions (no over-engineering)

No event bus/mediator, no plugin system, no DI container, no repository/generic layering, no cross-platform shims, no mocking-heavy patterns. Widget dispatch is a small dictionary; action dispatch is a small dictionary. New widget/action types are code additions (RF-046, M3).

## 2. Key design decisions

| ID | Decision | Justification (personal utility, no over-engineering) |
|---|---|---|
| DD-1 | **Logging:** Serilog with the rolling file sink (5 files × 2 MB, `%LOCALAPPDATA%\TouchPanel\logs`), compact text with structured properties; initialized first in `App`. A tiny redaction helper scrubs sensitive action parameters. On sink failure, fall back to `Debug`/trace output and continue (RF-010). | Mature, thread-safe rolling; one dependency instead of a hand-written file sink with rotation bugs. Redaction stays ours because it is domain-specific. |
| DD-2 | **Tray:** native `Shell_NotifyIcon` behind `Interop/TrayIcon` (message-only window listens for `WM_TRAYICON`, `TaskbarCreated`, menu commands); balloon notifications via `NIF_INFO`; context menu is a WPF `ContextMenu`. Bounded retry/backoff when registration fails (RF-013). **Post-M1 clarification (2026-10-09):** the context-menu host calls `SetForegroundWindow` on the hidden tray message window immediately before opening the WPF menu so the popup receives the user's dismissing click; the tray window is a hidden `WS_EX_NOACTIVATE` tool window, so this occurs only on the user-initiated tray click and never activates the panel (RF-018/RF-024 remain satisfied). | Zero third-party dependencies; we already need a message-only window for single-instance; `TaskbarCreated` re-registration is natural. A tray library is rejected by dependency discipline. |
| DD-3 | **Single instance (D-21):** session-scoped named mutex (`Local\TouchPanel.SingleInstance`); a hidden message-only window registers a `RegisterWindowMessage("TouchPanel.ShowPanel.v1")` message; the second process finds the window (`FindWindow`) and `PostMessage`s it, then exits; failures are logged and the second process exits silently (RF-002). | Standard Win32, no IPC framework, no files/pipes to clean up. |
| DD-4 | **Monitor identity (RF-016):** `QueryDisplayConfig` for target device path + monitor friendly name + source GDI name, combined with `EnumDisplayMonitors`/`GetMonitorInfo` (bounds, work area, primary) and `EnumDisplayDevices` (monitor device IDs, device string). Output is a `MonitorDescriptor` record; a pure `MonitorMatcher` applies the priority chain (device path / EDID-derived hardware ID → monitor device ID → friendly name + resolution + position) with deterministic tiebreaks and human-readable reasons for logs. No WMI/SetupAPI in M1; serial-based matching is best-effort from the device path when present. | Native, fast, dependency-free; keeps all Win32 in `Interop/`; matching logic is pure and unit-testable. WMI adds weight for marginal gain at M1 scale. |
| DD-5 | **Window behavior (RF-014/018/019/028):** WPF window with `WindowStyle=None`, `ResizeMode=NoResize`, `ShowInTaskbar=false`, `ShowActivated=false`; in `SourceInitialized` add `WS_EX_NOACTIVATE \| WS_EX_TOOLWINDOW`; placement via `SetWindowPos` using **physical pixel** bounds from `GetMonitorInfo` (SWP_NOACTIVATE, no Z-order change); show/hide via `ShowWindow(SW_SHOWNOACTIVATE/SW_HIDE)`; topmost toggle via `SetWindowPos(HWND_TOPMOST/HWND_NOTOPMOST, SWP_NOACTIVATE)`; never `Activate`/`SetForegroundWindow` in normal mode. PerMonitorV2 manifest. | One well-tested path for placement; avoids WPF DIU/pixel ambiguity (R-4). |
| DD-6 | **Widget host (RF-032/033):** `ItemsControl` with a `Canvas` `ItemsPanel`; item containers bind `Canvas.Left/Top` from widget VM geometry (DIPs); `WidgetTemplateSelector` + `WidgetRegistry` map config `type` → (VM factory, template key). Adding a type is one registration (M3 readiness). | Minimal WPF machinery; no framework, no plugin loading. |
| DD-7 | **Edit mode (RF-026/027/039):** `EditModeService` flag in `MainWindowViewModel`; an overlay adds per-widget drag/resize `Thumb`s (touch promotes to mouse, so one code path); VMs clamp geometry to page bounds and minimum size; Esc/arrow keys handled on the activated window; changes marked dirty and saved on exit through `IConfigStore`. Entering edit mode calls `Activate()` (D-6); exiting re-arms non-activating show behavior. | Uses built-in WPF `Thumb` semantics; no custom gesture engine. |
| DD-8 | **Actions (RF-047..053):** `ActionExecutor` resolves an `IActionHandler` by config `type`; M1 handlers: run command (delegates to `IProcessLauncher`), media key, volume step/set, mute — media keys, volume step, and mute via `SendInput` with `VK_MEDIA_*`/`VK_VOLUME_*` keys in `Interop/`; **volume set (absolute level)** uses the Core Audio `IAudioEndpointVolume.SetMasterVolumeLevelScalar` API under `Interop/Audio` (COM interop, no NuGet) because key presses advance by a device-defined increment and cannot reach an exact configured level (`VolumeHandler` stays unit-testable behind the `IAudioController` seam). **Post-M1 amendment (2026-10-09):** accepted deviation from the original "all media/volume via SendInput" wording; requirement IDs/semantics unchanged. `ProcessStartInfo { UseShellExecute=false, ArgumentList }` built by a pure `ProcessLaunchRequestFactory` (unit-tested against hostile strings). Handlers never wait for processes (D-20, RF-052); failures return `ActionExecutionResult` and are logged redacted (RF-051). | One input mechanism for all M1 input actions; pure argument construction is the security-critical, testable part. |
| DD-9 | **Configuration (RF-073..081):** `System.Text.Json` with a custom converter keyed on the `type` discriminator for widgets/actions (explicit polymorphism, no magic). `JsonConfigStore.Load` returns `ConfigLoadResult(State, Config, Diagnostics)` with the four spec states; `ConfigValidator` is pure and two-tier (RF-079; unknown widget/action types are preserved with their raw JSON and rendered as a disabled placeholder, never dropped — post-M1 correction 2026-10-09); `MigrationRunner` applies ordered `IConfigMigration` steps (v1 baseline; older versions exercised by test fixtures only); atomic save = temp file + flush + `File.Replace` (fallback `File.Move` when no destination); `panel.backup.json` rotates before overwrite; corrupt sources are preserved as `panel.corrupt-<timestamp>.json`; `state.json` is separate and non-fatal; all paths injected via `ConfigPaths`. | BCL JSON keeps dependencies minimal; explicit converter is debuggable; atomic/backup rules are the core of the user's data-safety requirement. |
| DD-10 | **Telemetry (RF-062..068):** `MonitoringService` runs a `PeriodicTimer` loop on a background task at 1 Hz with a `CancellationToken`; the LHM adapter (only file referencing LibreHardwareMonitorLib) lazily opens `Computer` on the background thread, translates sensors into an immutable `SensorSnapshot` with per-field availability, catches all read/open exceptions, and throttles failure logging via `TimeProvider`. Snapshots are published through a callback; ViewModels marshal to the UI thread. Disposal stops the loop and closes the library. | Keeps LHM types out of the app (RF-062/070) and the UI thread free (RF-067); bounded, simple loop. |
| DD-11 | **Startup registration (RF-003/004):** `IStartupRegistration` implemented with `Microsoft.Win32.Registry` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value name `TouchPanel`, command = quoted current exe path; read/compare for self-heal; failures log + notify. M1 toggles it via `panel.json` (`startup.startWithWindows`); a UI toggle arrives with the M2 settings window (RF-092). | Per-user, no elevation, trivially removable (D-4). |
| DD-12 | **Publish (RF-088, D-22):** gate is `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`; LHM driver assets are validated early (T-11 dev smoke + T-13 publish test); fallbacks: `IncludeNativeLibrariesForSelfExtract` or non-single-file self-contained build. | Packaging details must not block M1 completion; behavior stays self-contained. |
| DD-13 | **Theme (RNF-009, RF-031):** `Theme/Colors.xaml` + `Theme/Styles.xaml` with neutral dark defaults; widget templates enforce min 40×40 DIP, pressed/disabled/error visuals, no hover-only affordances. | Central styling, no theme engine in M1 (RF-084 is M3). |
| DD-14 | **Time:** `TimeProvider` (BCL) injected where timing matters (failure-log throttle, widget error-state timeout, retry backoff). Tests use a small manual fake; no custom `IClock`. | Modern BCL, avoids a bespoke interface. |

## 3. Affected modules/directories per workstream

| Workstream | Primary locations | Tasks |
|---|---|---|
| Solution skeleton, manifest, theme resources | `TouchPanel.sln`, `TouchPanel/*`, `TouchPanel/Theme/`, `TouchPanel.Tests/*` | T-01 |
| Logging, exception handling, subsystem health | `TouchPanel/Services/Logging/`, `App.xaml.cs`, `Services/Health/` | T-02 |
| Single instance, message window, lifecycle/shutdown | `TouchPanel/Services/SingleInstance/`, `Interop/MessageWindow`, `Services/AppLifecycle`, `App.xaml.cs` | T-03 |
| Configuration store, schema v1, validation, migration, recovery | `TouchPanel/Config/`, `Services/Notifications` (contract) | T-04 |
| Tray icon, menu, notifications, resilience | `TouchPanel/Interop/TrayIcon`, `Services/Tray/`, `Services/Notifications/` | T-05 |
| Start with Windows | `TouchPanel/Services/Startup/` | T-06 |
| Monitor enumeration, identity, matching, missing-monitor policy | `TouchPanel/Interop/Monitors`, `Services/Monitors/` | T-07 |
| Panel window behavior, placement, DPI/display changes | `TouchPanel/MainWindow.*`, `Interop/WindowStyles`, `Services/Display/` | T-08 |
| Action system (safe launch, media/volume/mute, executor) | `TouchPanel/Actions/`, `Interop/Input`, `Services/Processes/` | T-09 |
| Widget host, Button/Toggle/Label, touch/theme polish | `TouchPanel/Widgets/`, `ViewModels/`, `Views/`, `Theme/` | T-10 |
| Telemetry service and Gauge widget | `TouchPanel/Monitoring/`, `Widgets/Gauge`, `Services/Telemetry/` | T-11 |
| Edit mode (move/resize/keyboard/persist) | `TouchPanel/Widgets/Edit/`, `ViewModels/`, `Views/EditOverlay` | T-12 |
| Deployment validation | `TouchPanel/TouchPanel.csproj` (publish props), publish scripts not required | T-13 |
| Manual verification & acceptance run | all (no new production code expected; fixes if checks fail) | T-14 |

## 4. Dependency order and rationale

```text
T-01 skeleton ─► T-02 logging ─► T-03 single instance/lifecycle ─► T-04 config
   ─► T-05 tray ─► T-06 startup registration ─► T-07 monitor identity ─► T-08 panel window
   ─► T-09 actions ─► T-10 widgets ─► T-11 telemetry + Gauge ─► T-12 edit mode
   ─► T-13 publish ─► T-14 manual verification
```

- **T-01 first** so every later task lands on a green `dotnet build`/`dotnet test` baseline; the manifest fixes PerMonitorV2 from day one (RF-021).
- **T-02 before anything long-running** because every subsystem logs failures and the global exception handlers must exist before real work starts (RF-008..011, RNF-007).
- **T-03 before feature work** so there is exactly one process lifecycle and a shutdown coordinator to plug subsystems into (RF-001/002/007).
- **T-04 config early** because the window, widgets, and monitor selection are config-driven, and load/save data safety is the foundation of the user's trust (RF-073..081). It also creates `state.json`, which the tray needs for the one-time close notification.
- **T-05 tray before the window** provides notifications (used by config/monitor failures) and the Exit path; the panel window later adds close-to-tray (RF-006, RF-024).
- **T-06 is small and isolated** (HKCU Run); placed here to keep startup behavior complete before the window exists, so auto-start testing is meaningful early.
- **T-07 before T-08** because the window cannot be placed without a resolved target monitor; the pure matcher lands first so the risky logic is tested before Win32 placement.
- **T-08 window** delivers the focus/placement core; it is the highest-risk WPF/Win32 work and gets its own slice (RF-014..029).
- **T-09 actions before T-10 widgets** because Button/Toggle widgets bind actions; the executor boundary is implemented first so widgets never use stubs (RF-047..053).
- **T-10 widgets** proves touch, layout, binding, and error feedback end to end (RF-030..036).
- **T-11 telemetry + Gauge** is the first data-driven vertical slice and validates threading, disposal, failure tolerance, and the 1 Hz cadence against the real UI (RF-062..068).
- **T-12 edit mode** requires widgets and config persistence to exist (RF-026/027/039).
- **T-13 publish** validates packaging after all production assets exist (RF-088/089).
- **T-14** executes the manual verification matrix and performance/soak checks (RNF-001..005 etc.).

Each task ends with `dotnet build` and `dotnet test` green; manual checks listed in a task are performed when the task lands or are explicitly deferred to T-14 (noted per task).

## 5. Risks and mitigations

| ID | Risk | Mitigation / task anchor |
|---|---|---|
| R-1 | Focus stealing is subtle (exclusive-fullscreen games, topmost, anti-cheat overlays) | Window behavior centralized in `Interop/` + T-08; MV-01 focus matrix in T-14; exclusive-fullscreen documented limitation (spec section 2) |
| R-2 | LHM sensor availability depends on elevation/drivers/OS | Adapter failure-tolerant by design (T-11); UI shows unavailable; MV-10 checks with and without admin |
| R-3 | Monitor identity ambiguity (virtual displays, missing EDID, duplicates) | Pure `MonitorMatcher` with deterministic tiebreaks + reason logging (T-07); unit-tested; user-overridable config |
| R-4 | WPF + PerMonitorV2 physical-placement pitfalls | All placement via Win32 `SetWindowPos` in physical pixels (DD-5); MV-04 live DPI change check (T-14) |
| R-5 | Single-file publish vs LHM native driver assets | Early smoke check in T-11, full publish test in T-13; D-22 fallbacks (`IncludeNativeLibrariesForSelfExtract` / non-single-file) |
| R-6 | .NET 10 SDK availability on the implementation shell | Resolved: 10.0.401 installed at `C:\Program Files (x86)\dotnet`; T-01 verifies `dotnet --list-sdks` works in the implementation shell (PATH refresh may be needed) |
| R-7 | Tray icon hidden in Windows 11 overflow | Documented; MV-07 checklist; optional hotkey path arrives with M2 hotkeys |
| R-8 | Topmost panel vs exclusive-fullscreen content on the same monitor | `alwaysOnTop` default off (D-3); MV-01/MV-03 cover it |
| R-9 | Touch hardware/latency variance | MV-05 on the real touchscreen; standard WPF touch→mouse promotion used by `Thumb`s and buttons |

## 6. Testing strategy

### 6.1 Test project structure

```text
TouchPanel.Tests/
├─ Config/        ConfigStoreTests, ValidationTests, MigrationTests, AtomicWriteTests, BackupRecoveryTests
├─ Monitoring/    SnapshotAggregationTests, CadenceTests, FailureThrottleTests, DisposalTests
├─ Actions/       LaunchRequestTests, ExecutorTests, MediaVolumeMappingTests, RedactionTests
├─ Monitors/      MonitorMatcherTests (priority, ambiguous, no-match)
├─ ViewModels/    ToggleWidgetTests, GaugeWidgetTests, ErrorStateTests (fake TimeProvider)
└─ Architecture/  LayerPurityTests (Actions/Monitoring/Config contain no WPF types)
```

Tests use temp directories and injected `ConfigPaths`; they never touch the real user profile, hardware, drivers, LibreHardwareMonitor, or elevation (RNF-006). WPF-free testability is enforced by `LayerPurityTests` (reflection over members of the three namespaces) with code review as the secondary check.

### 6.2 Automated tests (summary)

- **Config:** four load states, migration success/failure fixtures, atomic write incl. simulated failure between temp write and replace, backup rotation and recovery, corrupt preservation, two-tier validation (unknown-type disabled placeholder with raw JSON kept, clamp, disabled-widget), `activePageId` fallback, defaults generation, hostile values, state.json resilience.
- **Monitors:** priority chain, missing EDID/device-path fields, duplicates and deterministic tiebreak, no-match result, reason strings.
- **Actions:** `ProcessStartInfo` construction (discrete args, quoting, spaces, hostile/injection strings), `UseShellExecute=false` invariant, unknown action types, result mapping, redaction of sensitive parameters.
- **Monitoring:** snapshot aggregation from fake sources (per-core bounds, unavailable fields, NaN), 1 Hz cadence with `TimeProvider`, failure-log throttling, cancellation/disposal.
- **ViewModels:** toggle transitions and no persistence, Gauge mapping/clamping/unavailable display, update only on changed values, transient error state timing.
- **Architecture:** layer purity test (RF-053, RF-062, RNF-010).

### 6.3 Integration tests (no real hardware)

- `JsonConfigStore` against temp directories, including concurrent-save simulation and read-only directory failures.
- `MonitoringService` with a fake sensor source and cancellation.
- `ProcessLauncher` against a harmless explicit executable invoked directly (e.g., `where.exe`), verifying no shell wrapping.

### 6.4 Manual verification matrix (executed in T-14; procedures recorded here)

| ID | Procedure | Expected result | ACs / requirements |
|---|---|---|---|
| MV-01 | Focus matrix: focus Notepad and type; launch app; Show via tray; tap widgets; repeat with a windowed/borderless game (and optional exclusive-fullscreen, documented only) | Foreground/typing never interrupted by launch, show, or touch; only edit mode may take focus | AC-04, RF-018, R-1 |
| MV-02 | Placement: inspect panel over the target monitor (including taskbar area); check taskbar and Alt-Tab; touch all four corners | Full monitor coverage, borderless, absent from taskbar/Alt-Tab, corners reachable | AC-03, RF-014/019/028 |
| MV-03 | Always-on-top: `alwaysOnTop: true` vs `false` with a normal window on the target monitor | Topmost stays above; false is coverable; never activates | AC-07, RF-020 |
| MV-04 | DPI: 100%, 125/150%, 200%; live scaling change while running; 1024×600 default layout | Crisp layout, correct hit-testing, correct relayout on live change, default layout fits | AC-08, RF-021/022/023, RNF-015 |
| MV-05 | Touch on the real touchscreen: Button, Toggle, Gauge, feedback, target sizes | Actions trigger exactly once per tap; feedback visible; ≥ 40 DIP targets; no keyboard needed | AC-10, RF-030/031/033/035 |
| MV-06 | Edit mode: enter from tray, drag/resize (incl. out-of-bounds attempts), Esc, arrow nudge, restart | Clamped/limited, Esc exits, layout persists, normal mode non-activating afterwards | AC-24, RF-026/027/039 |
| MV-07 | Tray/lifecycle: close window, one-time notification (restart to confirm shown-once), tray Exit, kill/restart Explorer, start app while Explorer stopped | Hides to tray; notification exactly once; clean exit; icon re-registers with retry logging | AC-02/19/26, RF-006/007/013/024 |
| MV-08 | Startup: enable in config, check HKCU Run; disable; move exe and start | Value created/removed correctly; stale path self-heals with log | AC-20, RF-003/004 |
| MV-09 | Single instance: launch second copy while running (panel hidden and visible) | No second panel/tray; second exits; panel shown if hidden | AC-18, RF-001/002 |
| MV-10 | Actions: real command launch (args/workdir), bad path, media keys, volume/mute in a player; telemetry with and without admin | Works or errors visibly+logged; no crash; unavailable sensors show as unavailable | AC-11/16, RF-047..050, RF-065/068 |
| MV-11 | Config: first run files; layout persistence; corrupt file, future version; backup recovery | States behave per RF-075; last known good never destroyed; notifications shown | AC-12..15, RF-073..078 |
| MV-12 | Publish: run the command in D-22; launch from a clean folder | Runnable self-contained output; tray + panel smoke passes; fallback applied if native assets conflict | AC-22, RF-088/089 |
| MV-13 | Performance: idle CPU visible/hidden (5 min after warm-up), startup-to-tray timing, 8 h soak | Within RNF-001/002/005; no unbounded growth | AC-17/23, RNF-001/002/005 |

### 6.5 Gates

- After every task: `dotnet build` and `dotnet test` succeed.
- T-14: all MV checks pass or produce documented, human-accepted limitations (e.g., exclusive-fullscreen).
- Reviewer (separate agent) validates implementation against the spec afterward; no code changes by the reviewer.

## 7. Migration and compatibility concerns

- **Schema v1 is frozen** by this plan: `system.text.json` DTOs plus the spec's example define the contract. Any change after M1 ships requires a new `schemaVersion` and an ordered migration step (RF-075/076); the migration runner is pure, ordered, idempotent, and validated after each step.
- **No historical versions exist in the wild** at M1; migration success/failure behavior is proven with test fixtures (e.g., a synthetic v0).
- **Backup/recovery chain:** successful save → validate → temp+replace → rotate `panel.backup.json`; load failures preserve the source and recover from backup/defaults; the last known valid configuration is never overwritten by garbage (RF-075/077/078).
- **Forward compatibility:** unsupported future versions are left untouched and the app runs from backup/defaults with a notification.
- **Packaging compatibility:** self-contained `win-x64`; no installer; all writes under the user profile; startup registration (when enabled) self-heals a moved executable.
- **Runtime compatibility:** Windows 10 21H2+/Windows 11 x64; no elevation; restricted sensors simply unavailable (RF-068).

## 8. Traceability — M1 requirements → tasks

Every M1 requirement (spec tags) maps to at least one task. M2/M3/Later requirements intentionally appear in no task.

| Requirement(s) | Task(s) |
|---|---|
| RF-001..RF-002 | T-03 |
| RF-003..RF-004 | T-06 |
| RF-005 | T-05, T-12 |
| RF-006 | T-08 |
| RF-007 | T-03, T-04, T-05, T-11 |
| RF-008..RF-011 | T-02 |
| RF-012 | T-02, T-11 |
| RF-013 | T-05 |
| RF-014 | T-08 |
| RF-015 | T-04, T-07 |
| RF-016 | T-07 |
| RF-017 | T-07, T-08 |
| RF-018..RF-019 | T-08 |
| RF-020 | T-04, T-08 |
| RF-021 | T-01, T-08 |
| RF-022 | T-07, T-08 |
| RF-023 | T-04, T-08, T-10 |
| RF-024 | T-05, T-08 |
| RF-025 | T-10 |
| RF-026..RF-027 | T-12 |
| RF-028..RF-029 | T-08 |
| RF-030..RF-032 | T-10 |
| RF-033 | T-10, T-11 |
| RF-034 | T-04, T-10 |
| RF-035..RF-036 | T-10 |
| RF-037 | T-04, T-10 |
| RF-038 | T-11 |
| RF-039 | T-12 |
| RF-047 | T-04, T-09 |
| RF-048..RF-049 | T-09 |
| RF-050 | T-09, T-10 |
| RF-051 | T-02, T-09 |
| RF-052..RF-053 | T-09 |
| RF-062..RF-068 | T-11 |
| RF-073..RF-076 | T-04 |
| RF-077 | T-04, T-05 |
| RF-078..RF-080 | T-04 |
| RF-081 | T-04, T-09 |
| RF-085 | T-02, T-09 |
| RF-086 | T-01, T-06, T-09 |
| RF-088 | T-13 |
| RF-089..RF-090 | T-01, T-13 |
| RNF-001 | T-11 (design), T-14 (measurement) |
| RNF-002 | T-14 |
| RNF-003 | T-11 |
| RNF-004 | T-08, T-09, T-11 |
| RNF-005 | T-03, T-08 |
| RNF-006 | T-01 (infrastructure; tests in every task) |
| RNF-007 | T-02 |
| RNF-008 | T-04, T-08 |
| RNF-009 | T-10 |
| RNF-010..RNF-011 | T-01 |
| RNF-013 | T-02, T-11 |
| RNF-014 | T-09 |
| RNF-015 | T-08 |

**Coverage check:** 67 tagged M1 functional requirements + 14 tagged M1 non-functional requirements, all mapped. No M2/M3/Later requirement is referenced by any task.
