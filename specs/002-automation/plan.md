# TouchPanel — Implementation Plan 002: Automation (M2)

| Field | Value |
|---|---|
| Plan ID | 002-automation |
| Status | Draft for human approval (M2 planning) |
| Date | 2026-10-09 |
| Inputs | `specs/002-automation/spec.md` (approved ACCEPTABLE), `specs/001-foundation/{spec,plan,tasks}.md`, the M1 implementation in `TouchPanel/` |
| Scope | M2 only |
| Phases | Phase 1 = workstreams W1–W5 (`D-M2-1`); Phase 2 = workstream W6 |

**M2 requirement set** (tags in `002-automation/spec.md` are authoritative):

- **Functional:** RF-040 Slider, RF-041 Sparkline, RF-043 full edit UI, RF-044 grid snap/alignment, RF-045 toggle persistence, RF-057 send-keys, RF-058 global hotkeys, RF-059 hotkey registration failure, RF-060 local shortcuts, RF-069 bounded history, RF-070 backend re-initialization, RF-071 configurable interval, RF-082 hot reload, RF-083 import/export, RF-091 page-switching UI, RF-092 settings window, RF-093 monitor picker.
- **Non-functional:** RNF-012 (bounded history) plus M2 execution constraints M2-RNF-121..128.
- **Dropped / not planned (2026-10-09):** RF-042, RF-054, RF-055, RF-056, RF-087, the `delay` action, and shell-open. No M3/Later work (RF-046, RF-061, RF-072, RF-084).

---

## 1. Architecture and integration with M1

M2 is an **extension of the M1 composition**, not a new architecture. Every M2 feature hangs off an existing seam; no event bus, plugin system, DI container, or new cross-cutting abstraction is introduced.

### 1.1 Reused M1 seams

| M2 feature | M1 seam reused | M2 addition |
|---|---|---|
| Send-keys action | `Actions/IActionHandler`, `IActionExecutor`, `Config/ActionConfig.cs` DTOs + `ActionConfigJsonConverter`, `Interop/Input/SendInputHelper`, `LogRedactor` | `SendKeysActionConfig` + `SendKeysHandler`; a pure key-name→VK mapper |
| Open program | M1 `RunCommandHandler` + `ProcessLaunchRequestFactory` + `SystemProcessLauncher` (RF-049 path) | None (no new launch mechanism); surfaced by the edit UI action picker with browse (MD-13) |
| Global/local hotkeys | `Interop/MessageWindow`/`MessageWindowNative` (hidden window + `RegisterWindowMessage`), the empty `Input/` folder, `IActionExecutor`, `IUserNotifier` | New `Interop/Input/HotKeyNative` + `Input/` hotkey service + parsing/validation |
| Ring buffers / interval / retry | `Monitoring/MonitoringService` (`PeriodicTimer`, `PublishOnce`, `Latest`, `SnapshotAvailable`), `ISensorSnapshotSource`, `ISensorSourceHealth`, `SubsystemHealth`, `ThrottledFailureLogger`, `TimeProvider` | Monitoring-owned history store; configurable interval; throttled re-init |
| Slider / Sparkline / toggle persistence | `Widgets/WidgetRegistry` + `WidgetRegistration` + `WidgetCreationContext`, `ViewModels/WidgetViewModelBase`, `ViewModels/GaugeWidgetViewModel`, `Config/WidgetConfig.cs` + `WidgetConfigJsonConverter`, `Config/StateStore` + `RuntimeState` | Two new widget types; `toggleStates` map in `state.json` |
| Full edit UI / grid | `ViewModels/EditModeService` (clamp/move/resize/dirty/save), `Views/EditOverlay`, `ViewModels/PageViewModel`, `WidgetRegistry` | Add/remove/duplicate/property/action-picker/label-icon/color editors; snap + guides |
| Hot reload / import / export | `Config/IConfigStore`/`JsonConfigStore` (single writer, atomic save, backup), `Config/Validation/ConfigValidator`, `Config/Migrations/MigrationRunner`, `ConfigPaths` | Pure reload-decision component + debounced watcher; export/import over the existing store |
| Page switching / settings / monitor picker | `PanelConfig.ActivePageId`/`Pages`, `MainWindowViewModel`, `Services/Monitors/{IMonitorEnumerator,MonitorSelectionService,MonitorDescriptor}`, `Services/Startup/IStartupRegistration`, `Services/Tray/TrayService` | UI surfaces driving the same store + apply pipeline |
| Schema | `Config/PanelConfig.CurrentSchemaVersion`, `Config/Migrations/{IConfigMigration,MigrationRunner}`, `Config/Json/ConfigJson` | Schema v3 via the ordered v1→v2→v3 migration chain |
| Lifecycle/threading | `App.xaml.cs` composition root, `AppLifecycleCoordinator`, `Dispatcher`, `SubsystemHealth`, global exception handlers | Register M2 subsystems in fixed order; register shutdown participants |

### 1.2 Dependency direction (unchanged from M1)

- **Views → ViewModels → boundaries.** New M2 views (edit property panel, action picker, settings, monitor picker, page tabs, alignment guides) carry presentation only.
- **`Actions/`, `Monitoring/`, `Config/`, `Input/` are WPF-free** (no `PresentationFramework`/`PresentationCore`/`WindowsBase`). P/Invoke is confined to `Interop/`. Enforced by the existing `TouchPanel.Tests/Architecture/LayerPurityTests`, extended to `Input/` and the new `Monitoring/`/`Config/` types.
- **Interop** hosts the single deliberate WPF exception (the `HwndSource`-hosted `MessageWindow`) already used by M1; M2's hotkey window reuses that pattern.
- **Composition root only**, no container: `App.xaml.cs` constructs concrete types in order and wires them manually.

### 1.3 Interfaces: only at true boundaries

M2 adds exactly **three** boundaries, each with a fake for tests:

| Interface | Purpose | Implementations |
|---|---|---|
| `ISensorHistory` (or equivalent read-only history boundary) | Read fixed-capacity per-metric history; never exposes polling | Monitoring implementation; fake in tests |
| `IHotKeyRegistration` | Register/unregister a hotkey and surface failure, isolated from Win32 | Win32 implementation in `Interop/`; fake seam in tests |
| `IKeyInput` (exists in M1 as `Actions/IKeyInput`) | Synthesize a key combination without real input in tests | Existing `SendInputHelper` wrapper; fake in tests |

Everything else stays concrete. The reload logic is a **pure class** (not an interface) so it is trivially unit-tested; the OS `FileSystemWatcher` is a thin adapter.

### 1.4 Deliberate exclusions

No event bus/mediator/plugin/DI/repository patterns; no generic layering; no new external dependencies (BCL only). Widget/action dispatch stays a small dictionary; new types are code additions.

---

## 2. Key design decisions

| ID | Decision | Justification |
|---|---|---|
| MD-1 | **Schema v3 via the ordered v1→v2→v3 migration chain.** `PanelConfig.CurrentSchemaVersion` becomes `3`. Two ordered, additive `IConfigMigration` steps (registered in `MigrationRunner`): **v1→v2** adds the `sendKeys` action discriminator, `telemetry.pollIntervalSeconds` (default `1.0`), the Slider/Sparkline widget config shapes, the Toggle `persistState` flag, and the `hotkeys[]` list; **v2→v3** adds per-widget **appearance overrides** (`icon`, `foreground`, `background`, `accent`, `fontSize`, `opacity`) introduced by the full edit UI (T-14). Migration is additive and never destructive; older (v1 and v2) files still load and are migrated/saved through the existing RF-075/RF-076 path (validate → back up → save). **Dated note (2026-10-09):** the third schema version is an accepted post-planning change — T-14's appearance overrides required new persisted fields; requirement IDs and semantics are unchanged, only `CurrentSchemaVersion` and the chain grew. Overridable. | M1 froze v1 and built the migration framework precisely for this; additive migration preserves user data and keeps the single-writer/atomic-save guarantees. Using a second ordered step keeps the appearance fields isolated from the engine/config fields. |
| MD-2 | **Send-keys action.** `SendKeysActionConfig { key: string, modifiers: string[] }`. A pure `SendKeyCombination` parser maps friendly names (`A–Z`, `0–9`, `F1–F24`, `Tab/Enter/Escape/Space/Left/Right/Up/Down/Home/End/PageUp/PageDown/Insert/Delete/Backspace`) to VKs and validates ≥1 known key and known modifiers. `SendKeysHandler` presses modifiers, presses the key, releases the key, and **always releases modifiers in a `finally`** (no stuck modifiers). Delivery targets the **foreground** window via the existing `SendInputHelper`; TouchPanel never activates. Invalid/unknown/modifier-only combos disable the widget at validation (RF-047). | Reuses the M1 input path and executor boundary with no new P/Invoke surface; the pure mapper is the testable, security-relevant part. |
| MD-3 | **Global hotkeys.** A new `Interop/Input/HotKeyNative` wraps `RegisterHotKey`/`UnregisterHotKey`. `Input/HotKeyService` parses/validates combos, registers each on a hidden **UI-thread** window (reusing `MessageWindow`, created non-message-only so it can pump `WM_HOTKEY`), maps `WM_HOTKEY` ids to bindings, and dispatches the single bound action through `IActionExecutor` (fire-and-forget). **At least one modifier is required** (D-M2-4). Registration is marshalled to the UI thread (thread affinity). A failed registration is logged and skipped; the rest continue; **one** aggregated tray notification is shown; no retry loop. Global config `hotkeys[]`; local edit-time shortcuts are a separate fixed set. | Native, dependency-free, and exactly the mechanism 001 D-8 selected; keeps focus preservation because the window is hidden and never activated. |
| MD-4 | **Local shortcuts.** The M2 fixed edit-mode set (remove, duplicate, toggle snap) joins the M1 Esc/arrow keys as key bindings/handlers on the activated panel only; they are app-defined (no editor) and inert outside edit mode. | RF-060 separation; avoids a second global registration surface. |
| MD-5 | **Monitoring ring buffers.** `Monitoring/` owns a `SensorHistoryStore` holding one fixed-capacity `RingBuffer<T>` per tracked metric. Capacity = `clamp(ceil(windowSeconds / intervalSeconds), min, 3600)`. Buffers are created lazily for the metric keys referenced by the current configuration (Sparklines + Gauge); unreferenced keys are released on reload/page change. Writes are O(1) and allocation-free; reads return a consistent copy under a lock so the UI thread never sees torn data. Unavailable samples are stored as a null marker (gaps), never zero. History is populated from the same snapshots `MonitoringService.PublishOnce` already produces. | Bounded memory, simple, testable; satisfies RF-069/RNF-012 without a second polling path. |
| MD-6 | **Configurable interval.** `MonitoringService` takes the interval from config (clamped `0.5–5 s`, default `1 s`) instead of the fixed `DefaultInterval`. A live change (hot reload/apply) restarts the loop with the new interval (single loop, no duplication) and recomputes ring capacity (preserving what fits, oldest-dropped). Interval mutation stays on the monitoring/background path. | RF-071 with no two-loop hazard and no UI-thread work. |
| MD-7 | **Throttled backend re-initialization.** When `ISensorSourceHealth` reports a backend failure, the monitoring loop attempts a re-open no more than once per `5 min` (via `TimeProvider`), logs at a throttled rate, and marks `monitoring` restored on success using the existing `SubsystemHealth`. Never elevates. | RF-070; reuses M1 failure-throttling and health primitives. |
| MD-8 | **Toggle persistence.** Add `ToggleWidgetConfig.persistState` (default `false`, preserving D-15). Extend `RuntimeState` with `toggleStates: Dictionary<string, bool>` keyed `"<pageId>/<widgetId>"`, persisted by the existing `StateStore` (atomic, non-fatal) — **never** in `panel.json`. Writes are debounced/batched and go through hot-reload self-write suppression. Unknown/stale keys are ignored and pruned; absent/shape-mismatched maps are non-fatal. | Keeps user config clean and prevents reload storms (RF-045, M2-RF-452..455). |
| MD-9 | **Hot reload.** A pure `ReloadDecision` class (event + last-written-content hash + in-flight-write flag → `Ignore` / `Debounce` / `Reload`) captures all semantics; a thin `ConfigReloadController` wraps a `FileSystemWatcher` with `Filter = "panel.json"` and `NotifyFilter = LastWrite|FileName|Size|CreationTime`, **debounced** (`400 ms`). App-originated writes (config store **and** state store) are suppressed around every save and by content-hash comparison. On `Reload`, the file is parsed → migrated → validated as a **whole**; only a fully valid candidate is swapped atomically via the shared apply pipeline; otherwise the running config stays and the user is notified (throttled). Hot reload never writes the watched file. The watcher and debounce timer are shutdown participants. | Satisfies RF-080 (no polling, single writer) and RF-082 (validate-then-apply, no partial apply); the pure component makes semantics deterministic to test. |
| MD-10 | **Shared apply pipeline.** One `PanelConfigApplier` (owned by the composition root) applies a validated config to the live app on the UI thread: window options (`alwaysOnTop`), active page widgets (dispose old page VMs, build new), monitor target, telemetry interval, tracked-metric set, hotkey re-registration. Used by hot reload, import, settings, page switching, and monitor-picker changes. Disposing old page VMs releases their subscriptions. | Avoids duplicated apply logic and guarantees one disposal/relink path (M2-RF-824). |
| MD-11 | **Import/export.** Export serializes the current `PanelConfig` (with `schemaVersion`) to a chosen path; no secret fields exist in M2, so no redaction is required, and content is never logged. Import reads → migrates older supported → validates fully, takes a **pre-import backup** (timestamped copy and/or `panel.backup.json`), then swaps + persists through the existing store and applies via MD-10; any failure changes nothing. File I/O is off the UI thread; a file picker is used from the settings window. | RF-083 with the M1 data-safety guarantees. |
| MD-12 | **Full edit UI on the existing edit mode.** `EditModeService` gains page-config mutation (add/remove/duplicate with generated ids and deterministic placement), a property-editor model, and snap/align hooks. New `Views/Edit*` panels are touch-first and dialog-light; all changes flow through the existing dirty-tracking + single-writer save. In edit mode widget taps never execute actions (M1 invariant retained). Unknown widget types stay as disabled placeholders and are not offered for add. | Reuses M1 editing, clamping, and atomic persistence; keeps the host free of per-instance logic. |
| MD-13 | **Action picker incl. open-program browse.** The picker is driven by the registered action catalog (M1 types + `sendKeys`). For run-command/launch ("open program") it offers an executable **file browse** and keeps discrete `arguments`/`workingDirectory` fields; the M1 `ProcessLaunchRequestFactory` (no shell, discrete args) is unchanged. Committing an invalid action disables the widget with a visible indicator. | Makes the common "open program" case configurable by touch with no new execution surface (M2-RNF-128). |
| MD-14 | **Grid snap + alignment aids.** A pure `GridSnap`/`AlignmentGuides` helper computes snapped coordinates and guide lines; snapping composes with the existing page clamp + 40 DIP minimum. Arrow nudge uses `gridSize` when snap is on, else `1 DIP`. A snap toggle and free-position override keep M1 free positioning available. | Pure math is unit-testable; reuses `EditModeService.Clamp*`. |
| MD-15 | **Settings + monitor picker + page switching.** A tray-accessible Settings window (activating, D-6 analogy) edits `alwaysOnTop`, start-with-Windows (reusing `IStartupRegistration`), telemetry interval, and the target monitor (via the picker built on `IMonitorEnumerator`/`MonitorDescriptor`), plus import/export and read-only diagnostics. A touch page selector switches `activePageId` (validated, persisted) without activation. All changes save atomically and apply live via MD-10. | RF-091/092/093; reuses M1 monitor identity/startup/store. |
| MD-16 | **Threading and lifecycle.** Hotkey register/unregister, window (re)creation, and UI-state mutation are UI-thread-marshalled; history reads are marshalled by widgets; file I/O/HTTP-free work is async. Every M2 subsystem (watcher, hotkeys, history, page VMs, reload debounce) is registered with `AppLifecycleCoordinator` and released within the ~3 s budget. | Preserves M1 RNF-004/RF-007 guarantees. |

---

## 3. Affected modules/directories per workstream

```text
TouchPanel/
├─ Config/
│  ├─ PanelConfig.cs / PageConfig.cs / WidgetConfig.cs / ActionConfig.cs   schema v3 DTOs (older fields preserved)
│  ├─ Json/{Action,Widget}ConfigJsonConverter.cs                           new sendKeys + slider/sparkline discriminators
│  ├─ Migrations/                                                          ordered v1→v2 and v2→v3 IConfigMigrations
│  ├─ Validation/ConfigValidator.cs                                        new bounds + toggle/slider/sparkline/hotkey validation
│  ├─ RuntimeState.cs / StateStore.cs                                      toggleStates map
│  └─ (new) ImportExport/ConfigImportExport.cs                             export/import + pre-import backup
├─ Actions/
│  ├─ Handlers/SendKeysHandler.cs                                          new
│  ├─ SendKeyCombination.cs (pure)                                         friendly-name→VK mapping + validation
│  └─ IKeyInput.cs                                                         existing seam reused
├─ Monitoring/
│  ├─ SensorHistoryStore.cs + RingBuffer.cs                                bounded per-metric history
│  ├─ MonitoringService.cs                                                 configurable interval + history feed
│  └─ BackendReinitializer.cs                                              throttled retry
├─ Input/                                                                  (reserved empty in M1; filled in M2)
│  ├─ HotKeyService.cs, HotKeyBinding.cs, HotKeyParser.cs                  global hotkeys + local shortcut set
├─ Interop/
│  └─ Input/HotKeyNative.cs                                                RegisterHotKey/UnregisterHotKey
├─ ViewModels/
│  ├─ SliderWidgetViewModel.cs, SparklineWidgetViewModel.cs                new widget VMs
│  ├─ EditModeService.cs                                                   add/remove/duplicate/property/snap
│  ├─ (new) Edit/PropertyEditorViewModel.cs, ActionPickerViewModel.cs      edit UI state
│  ├─ (new) Edit/GridSnap.cs, AlignmentGuides.cs                           pure geometry
│  └─ (new) Settings/SettingsViewModel.cs, MonitorPickerViewModel.cs, PageSwitcherViewModel.cs
├─ Widgets/
│  ├─ SliderWidget/, SparklineWidget/                                       templates + registration
│  └─ WidgetRegistry.cs                                                     register the two new types
├─ Views/
│  ├─ EditOverlay.xaml(.cs)                                                 extend for editors + guides
│  └─ (new) Edit/, Settings/, MonitorPicker/, PageSwitcher/                 XAML + minimal code-behind
├─ Services/
│  ├─ Config/ConfigReloadController.cs + ReloadDecision.cs (pure)           hot reload
│  └─ AppLifecycle/                                                         register new shutdown participants
└─ App.xaml.cs                                                             composition root wiring + apply pipeline

TouchPanel.Tests/
├─ Config/          MigrationChainTests, ToggleStateTests, ImportExportTests, ReloadDecisionTests
├─ Monitoring/      SensorHistoryStoreTests, IntervalTests, BackendReinitTests
├─ Actions/         SendKeyCombinationTests, SendKeysHandlerTests
├─ Input/           HotKeyParserTests, HotKeyServiceTests
├─ ViewModels/      SliderWidgetTests, SparklineWidgetTests, EditOperationsTests, GridSnapTests
└─ Architecture/    LayerPurityTests (extend to Input/ + new types)
```

---

## 4. Dependency order and rationale

```text
Phase 1 (W1–W5)
T-01 schema migration + DTOs/validation (v1→v2; v2→v3 lands with T-14)
   ├─► T-02 send-keys action (config + parser + handler)
   │       └─► T-09 global hotkeys (RegisterHotKey + Input/HotKeyService) ─► T-10 local shortcuts
   ├─► T-03 monitoring history (ring buffers + tracked set)
   │       └─► T-07 Sparkline widget
   ├─► T-04 configurable interval (+ live apply)
   ├─► T-05 throttled backend re-initialization
   ├─► T-06 Slider widget
   └─► T-08 toggle persistence (state.json toggleStates)

Phase 2 (W6)
T-11 hot reload (pure decision + watcher + apply pipeline)
   ├─► T-12 import/export
   ├─► T-13 full edit UI: add/remove/duplicate + property editor (needs T-06/T-07)
   ├─► T-14 full edit UI: action picker (browse) + label/icon/colors
   ├─► T-15 grid snap + alignment guides (needs T-13)
   ├─► T-16 page-switching UI
   └─► T-17 settings window + monitor picker (needs T-11/T-12)
T-18 manual verification matrix + performance/soak
```

Rationale highlights:

- **T-01 first** so every later task lands on a green build/test baseline and so config/validation for the new types exists before consumers.
- **T-02 before T-09** so hotkeys can bind the new action shape; T-09 before T-10 so local shortcuts share the parsing service.
- **T-03 before T-04/T-05/T-07**: history is the monitoring core; interval and retry refine the loop; Sparkline consumes history.
- **T-06/T-07 depend on T-01** (config/registry) and T-07 additionally on T-03.
- **T-08 depends on T-01** (Toggle field) and on the state-store suppression contract from T-11 in spirit; it ships in Phase 1 and T-11 wires suppression.
- **T-11 before T-12/T-17**: both reuse the validate-then-apply pipeline and single-writer suppression.
- **T-13 before T-14/T-15**: editors build on page-config mutation.
- **T-18 last**: executes the manual matrix and performance/soak checks over the finished M2.

---

## 5. Phase boundary (explicit)

- **Phase 1 — W1–W5 (T-01..T-10):** schema migration (ordered v1→v2→v3; Phase 1 delivers v1→v2, Phase 2/T-14 adds v2→v3); send-keys action; global/local hotkeys; monitoring ring buffers + configurable interval + throttled backend retry; Slider, Sparkline, toggle persistence. Fully usable via hand-edited JSON; "open program" is the existing M1 run-command action. Highest-risk work (focus preservation, key synthesis) is validated before the UI.
- **Phase 2 — W6 (T-11..T-18):** hot reload; import/export; page-switching UI; full edit UI + grid/guides + action picker/browse; settings window; monitor picker; final manual/acceptance run.

Both phases are part of the single M2 milestone (`D-M2-1`); no `M2a`/`M2b` labels and no 001 retagging.

---

## 6. Risks and mitigations

| ID | Risk | Mitigation / task anchor |
|---|---|---|
| MR-1 | `RegisterHotKey` collides with OS/game shortcuts | RF-059 handling (skip + aggregate, no retry) in T-09; MV-M2 focus matrix in T-18 |
| MR-2 | Hot-reload feedback loop (app saves → watcher fires) | Pure `ReloadDecision` + content-hash + suppression of config **and** state writes (MD-9); T-11 temp-dir test |
| MR-3 | `sendKeys` stuck modifier on partial failure | Modifiers always released in a `finally` (MD-2); T-02 unit test; T-18 live check |
| MR-4 | Unbounded memory from history or edit UI | Fixed-capacity rings + lazy tracked set + documented memory (MD-5); T-03/T-18 |
| MR-5 | Schema migration chain (v1→v2→v3) breaks existing configs | Ordered additive migration; fixtures for both steps; source never destructively overwritten (RF-076); T-01 (v1→v2) and T-14 (v2→v3) |
| MR-6 | Edit UI complexity bloating the small app | Touch-first, dialog-light, no undo engine, reuse M1 edit mode/store (MD-12); T-13/T-14 |
| MR-7 | `FileSystemWatcher` buffer overflow under heavy editing | Debounce + overflow handling + re-establish (M2-RF-827); T-11 |
| MR-8 | Sparkline history races the polling thread | Lock-protected consistent reads (MD-5); T-03/T-07 deterministic tests |
| MR-9 | Focus stealing introduced by hotkeys/settings | Hotkey window hidden + never activated; UI-thread marshalling; settings is a deliberate activating window (D-6 analogy); T-09/T-17/T-18 |
| MR-10 | Live interval change leaves two loops or loses snapshots | Single-loop restart with capacity recompute (MD-6); T-04 test |
| MR-11 | Page switch/settings/hot-reload writes trigger self-reload | All app writes suppressed (MD-9); covered in T-11/T-16/T-17 tests |
| MR-12 | Toggle-state writes bloat or corrupt `state.json` | Non-fatal shape handling + pruning + debounce (MD-8); T-08 |

---

## 7. Testing strategy

Aligned with M1 (plan §6): `dotnet test` is the acceptance gate; tests use temp directories and injected paths; no hardware, drivers, elevation, or real network; WPF-free layer purity is enforced.

### 7.1 Unit tests (fakes for real-world seams)

- **Config/migration (T-01, T-14):** v1→v2→v3 migration chain fixtures (v1→v2 in T-01, v2→v3 in T-14); v1 and v2 still load through the chain; new `sendKeys`/slider/sparkline DTO round-trips; appearance-override round-trips; interval clamp `0.5–5 s`; unknown-type preservation; four RF-075 states unchanged.
- **Send-keys (T-02):** friendly-name→VK mapping (letters/digits/F-keys/named keys), modifier ordering, always-release on failure, invalid/modifier-only/unknown disabling, bare-key-valid-for-widget, failure result mapping (input behind `IKeyInput` fake).
- **Monitoring (T-03/T-04/T-05):** ring capacity math/clamp, O(1)/allocation-free append, concurrent read consistency, gap storage, tracked-set add/release, capacity recompute on interval change, single-loop on live interval change, throttled retry with fake `TimeProvider`, health transitions.
- **Widgets (T-06/T-07/T-08):** Slider validation/value→`volume Set` mapping/throttled emission/unsupported-action disable; Sparkline history→points with gaps, auto/manual scale, no-poll, update-on-change; Toggle persistence read/write/reconcile/prune against a fake state store.
- **Input (T-09/T-10):** hotkey parsing, duplicate detection, ≥1-modifier rule, invalid-entry skipping, registration-result aggregation (fake `IHotKeyRegistration`), no-retry, re-registration on reload, UI-thread marshalling assertion; local-shortcut context gating.
- **Hot reload (T-11):** pure `ReloadDecision` (ignore/debounce/reload), self-write suppression for config + state writes, content-unchanged no-op, validate-before-apply, invalid/future-schema keeps running config, no-partial-apply.
- **Import/export (T-12):** export includes `schemaVersion`; import validate+migrate+pre-import backup; failed import leaves config intact; paths-only logging.
- **Edit operations (T-13/T-14/T-15):** add/remove/duplicate id generation + placement, property/action/color/label-icon editing, executable browse model, grid/alignment math (rounding, threshold edge cases, clamp composition, hostile input), dirty tracking, save-through-fake-store, no action execution in edit mode, unknown-type not offered for add.
- **UI VMs (T-16/T-17):** page-switch persistence/fallback; settings validation + live-apply intent; monitor-picker identity mapping and selection.
- **Architecture:** `LayerPurityTests` extended to `Input/` and new `Monitoring/`/`Config/`/`Actions/` types; P/Invoke confined to `Interop/`.

### 7.2 Integration tests (no real hardware/network)

- `MonitoringService` + `SensorHistoryStore` with a fake sensor source: history population, live interval change, throttled retry.
- `JsonConfigStore` temp-dir: reload self-suppression (simulated external vs app write), import/export round-trip.
- `ActionExecutor` with fake handlers + one harmless real process launch (M1 pattern) + one `sendKeys` against a fake input seam.

### 7.3 Manual verification matrix (executed in T-18)

| ID | Procedure | Expected | ACs |
|---|---|---|---|
| MV-M2-01 | Global hotkey bound to `sendKeys` while Notepad and a windowed/borderless game is focused | Combination reaches the focused app; typing/foreground uninterrupted; no activation | AC-M2-12, AC-M2-14 |
| MV-M2-02 | Widget `sendKeys` (e.g. Alt+A) into a focused app; media keys | Combination typed; media keys unaffected; no focus change | AC-M2-12 |
| MV-M2-03 | Slider drag controls system volume; Sparkline shows live history | Smooth volume control; history scrolls within the window and stays bounded | AC-M2-16 |
| MV-M2-04 | Toggle `persistState` across restart; non-persisting resets | State restored/reset; no `panel.json` change; no reload attempt | AC-M2-23 |
| MV-M2-05 | Touch-only edit: add/remove/duplicate, property/action/label/icon/color, grid snap + guides | All touch-operable; changes survive restart; `open program` browsable | AC-M2-17, AC-M2-18 |
| MV-M2-06 | External `panel.json` edit valid/invalid; app's own saves | Valid applies live; invalid keeps running + notifies; no reload loop | AC-M2-19, AC-M2-20 |
| MV-M2-07 | Import/export; settings window; monitor picker; page switch | Pre-import backup; settings apply live; picker writes identity; page switch persists without focus steal | AC-M2-21, AC-M2-22 |
| MV-M2-08 | Idle CPU with hotkeys+history (visible/hidden); 8 h soak | Within RNF-001/RNF-002; history memory matches the documented bound | AC-M2-24 |

### 7.4 Gates

- After every task: `dotnet build` and `dotnet test` succeed; the full M1+M2 suite stays green.
- T-18: all MV-M2 checks pass or produce a documented, human-accepted limitation (e.g., exclusive-fullscreen).
- A separate reviewer validates against the spec afterward; reviewers do not modify code.

---

## 8. Migration and compatibility

- **Schema v1 and v2 are preserved.** The current schema is **v3**; the v1→v2→v3 chain is additive and each step is validated in order. A v1 or v2 file migrates in memory and is saved via the existing atomic path (validate → rotate `panel.backup.json` → temp + replace). No older field is renamed or removed. The per-widget `action` stays a single action.
- **Forward compatibility:** a newer/future schema is not interpreted and is never overwritten (RF-075.4), on startup or reload.
- **Hot-load safety:** reload/import never write the watched file; app writes are suppressed from the watcher; `state.json` (toggle state) lives outside `panel.json`.
- **Memory/format:** `state.json` gains `toggleStates`; absent/shape-mismatched maps are non-fatal and regenerated.
- **Packaging:** unchanged from M1 — self-contained `win-x64`; no new dependency; no installer; all writes under the user profile; no elevation.

---

## 9. Traceability — M2 requirement → task

| Requirement(s) | Task(s) |
|---|---|
| RF-040 Slider (M2-RF-401..406) | T-06 |
| RF-041 Sparkline (M2-RF-411..416) | T-03, T-07 |
| RF-043 full edit UI (M2-RF-430..440) | T-13, T-14 |
| RF-044 grid/alignment (M2-RF-441..446) | T-15 |
| RF-045 toggle persistence (M2-RF-451..455) | T-08 |
| RF-057 send keys (M2-RF-571..576) | T-02 |
| RF-058 global hotkeys (M2-RF-581..586) | T-09 |
| RF-059 hotkey failure (M2-RF-591..595) | T-09 |
| RF-060 local shortcuts (M2-RF-601..604) | T-10 |
| RF-069 bounded history (M2-RF-691..697) | T-03 |
| RF-070 backend re-init (M2-RF-701..704) | T-05 |
| RF-071 polling interval (M2-RF-711..714) | T-04 |
| RF-082 hot reload (M2-RF-821..830) | T-11 |
| RF-083 import/export (M2-RF-831..835) | T-12 |
| RF-091 page-switching UI (M2-RF-911..915) | T-16 |
| RF-092 settings window (M2-RF-921..925) | T-17 |
| RF-093 monitor picker (M2-RF-931..934) | T-17 |
| RNF-012 bounded history (M2-RNF-121) | T-03, T-18 |
| M2-RNF-122/123 (no polling; UI-thread) | T-04, T-09, T-11, T-16, T-17 |
| M2-RNF-124 (testability) | every task; seams in T-02/T-03/T-09/T-11 |
| M2-RNF-125 (layering) | T-01 (test), extended in T-02/T-03/T-09/T-11/T-12 |
| M2-RNF-126 (isolation) | T-05, T-09, T-11, T-17 |
| M2-RNF-127 (memory) | T-03, T-18 |
| M2-RNF-128 (no new execution surface) | T-02, T-13/T-14 (open program unchanged) |

**Coverage check:** every surviving M2 base 001 requirement and RNF-012 maps to at least one task; every M2-RNF-121..128 is anchored. Dropped requirements (RF-042/054/055/056/087) and the `delay` action/shell-open appear in no task. No M3/Later work (RF-046, RF-061, RF-072, RF-084) is implemented.
