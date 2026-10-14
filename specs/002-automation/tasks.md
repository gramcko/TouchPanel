# TouchPanel — Tasks 002: Automation (M2)

| Field | Value |
|---|---|
| Tasks ID | 002-automation |
| Status | **Scope frozen (2026-10-09): Core Release delivered; tasks for frozen features are not to be continued** |
| Date | 2026-10-09 |
| Inputs | `specs/002-automation/{spec.md, plan.md}` |
| Execution rule | After every task: `dotnet build` and `dotnet test` must succeed. No M3/Later work, no dropped-feature work (RF-042/054/055/056/087, `delay`, shell-open), and **no continuation of frozen-backlog tasks**. |
| Phases | Historical: Phase 1 = T-01..T-10 (W1–W5); Phase 2 = T-11..T-18 (W6). See the freeze status below. |

Task granularity is a **vertical slice** (a coherent feature or subsystem), not one class per task. Each task lists purpose, files, dependencies, implementation notes, tests, acceptance criteria, and requirement IDs.

> **Freeze status (2026-10-09, `D-M2-8` / 001 `D-24`).** Only the **Core Release** is delivered and supported: Sparkline/graph + bounded history, global hotkeys, send-keys, and visual customization. Tasks for frozen features are **not to be continued** (their code may remain in the build but is unsupported/unmaintained). The remaining core work is only final validation (T-18).
>
> - **Core (delivered/supported):** **T-01** (schema/migration), **T-02** (send-keys), **T-03** (history), **T-07** (Sparkline), **T-09** (global hotkeys), **T-14** *appearance portion only* (label/icon/colors/font/opacity), and **T-18** (final validation, the only remaining core work).
> - **Frozen backlog (not to be continued):** **T-04** (interval), **T-05** (backend re-init), **T-06** (Slider), **T-08** (toggle persistence), **T-10** (local shortcuts), **T-11** (hot reload), **T-12** (import/export), **T-13** (add/remove/duplicate/property editor), **T-14** *action-picker/browse portion*, **T-15** (grid/guides), **T-16** (page switching), **T-17** (settings/monitor picker).
> - **Dropped (not backlog):** RF-042/054/055/056/087, the `delay` action, and shell-open.

## Overview

| ID | Title | Phase | Depends on | Requirements |
|---|---|---|---|---|
| T-01 | Schema migration foundation: config DTOs, validation, ordered v1→v2 migration (v2→v3 added in T-14) | 1 | — | AC-M2-02, RF-075/076, MD-1 |
| T-02 | Send-keys action (config + friendly-name parser + handler) | 1 | T-01 | RF-057 |
| T-03 | Monitoring bounded history (per-metric ring buffers + tracked set) | 1 | T-01 | RF-069, RNF-012 |
| T-04 | Configurable polling interval (0.5–5 s) + live apply | 1 | T-03 | RF-071 |
| T-05 | Throttled monitoring backend re-initialization | 1 | T-03 | RF-070 |
| T-06 | Slider widget (VM + view + registry) | 1 | T-01 | RF-040 |
| T-07 | Sparkline widget (VM + view + registry) over history | 1 | T-03 | RF-041 |
| T-08 | Toggle state persistence in `state.json` | 1 | T-01 | RF-045 |
| T-09 | Global hotkeys (`RegisterHotKey` + `Input/` service + failure handling) | 1 | T-02 | RF-058, RF-059 |
| T-10 | Local shortcuts (M2 edit-mode fixed set) | 1 | T-09 | RF-060 |
| T-11 | Hot reload (pure decision + watcher + apply pipeline) | 2 | T-01..T-10 | RF-082 |
| T-12 | Import / export configuration | 2 | T-11 | RF-083 |
| T-13 | Full edit UI 1: add/remove/duplicate + property editor | 2 | T-06, T-07, T-08 | RF-043 |
| T-14 | Full edit UI 2: action picker (browse) + label/icon/colors | 2 | T-13 | RF-043 |
| T-15 | Grid snap + alignment guides | 2 | T-13 | RF-044 |
| T-16 | Page-switching UI | 2 | T-11 | RF-091 |
| T-17 | Settings window + monitor picker | 2 | T-11, T-12 | RF-092, RF-093 |
| T-18 | Manual verification matrix, performance, acceptance run | 2 | T-17 | RNF-012, AC-M2-* |

---

## T-01 — Schema migration foundation: config DTOs, validation, ordered v1→v2 migration

**Phase:** 1 (W1)

**Objective.** Add the M2 schema fields additively and teach the existing migration/validation/JSON machinery about them, starting the ordered chain. T-01 delivers the **v1→v2** step; **T-14** later adds **v2→v3** (appearance overrides) and bumps `PanelConfig.CurrentSchemaVersion` to the delivered **3**. Every later task gets a green baseline with valid config.

**Files/directories.**
- `TouchPanel/Config/PanelConfig.cs` — `CurrentSchemaVersion = 2` at this step (T-14 later bumps it to the delivered **3**); add `TelemetryConfig` (`pollIntervalSeconds`) and `Hotkeys` list.
- `TouchPanel/Config/WidgetConfig.cs` — add `SliderWidgetConfig`, `SparklineWidgetConfig`; add `ToggleWidgetConfig.PersistState`; new `WidgetTypes` constants.
- `TouchPanel/Config/ActionConfig.cs` — add `SendKeysActionConfig`, `ActionTypes.SendKeys`.
- `TouchPanel/Config/Json/WidgetConfigJsonConverter.cs`, `ActionConfigJsonConverter.cs` — discriminate the new types.
- `TouchPanel/Config/Migrations/` — new `V1ToV2Migration : IConfigMigration` (additive: set `schemaVersion=2`, add `telemetry.pollIntervalSeconds=1.0`, ensure toggle `persistState=false`, no v1 field removed). **T-14** adds the next step `V2ToV3Migration` (appearance overrides) to complete the chain.
- `TouchPanel/Config/Validation/ConfigValidator.cs` — bounds for interval, slider range/step, sparkline window, send-keys field presence, toggle persistence pass-through; keep two-tier containment and unknown-type preservation.
- `TouchPanel/Config/DefaultConfigGenerator.cs` — schema defaults (no new widgets by default).

**Dependencies:** none.

**Implementation notes.** Migration is additive and ordered through the existing `MigrationRunner`; v1 files must still load (migrate → validate → back up → save). `PanelConfig.CurrentSchemaVersion` must not be persisted into the old-file probe path incorrectly; reuse `JsonConfigStore`'s four-state handling. Unknown widget/action types stay preserved as raw JSON. Do **not** add HTTP/delay/sequence fields.

**Tests.**
- Fixture: a v1 `panel.json` migrates through the chain to the **current schema (v3)**, validates, and preserves the original as `panel.backup.json` (T-01 covers v1→v2; T-14 covers v2→v3).
- Fixture: a failing migration follows the invalid path and leaves the source intact.
- Round-trips for `sendKeys`/slider/sparkline DTOs; interval clamp (`0.5–5 s`, default `1 s`); unknown type preserved; four RF-075 states still pass.

**Acceptance criteria.** `dotnet test` covers AC-M2-02; a real v1 file loads and is migrated/saved atomically; no v1 data loss.

**Requirements:** M2-RF-825, RF-075, RF-076, M2-RF-711, M2-RF-401/411 DTOs, M2-RF-451 field, M2-RF-581 config shape, MD-1.

---

## T-02 — Send-keys action

**Phase:** 1 (W2)

**Objective.** Add the single M2 action type: send one key+modifier combination to the foreground application, validated and failure-contained via the existing executor.

**Files/directories.**
- `TouchPanel/Actions/SendKeyCombination.cs` (pure) — friendly-name→VK mapping and validation.
- `TouchPanel/Actions/Handlers/SendKeysHandler.cs` — `IActionHandler` for `sendKeys`; press modifiers → press key → release key → **always release modifiers** in `finally`.
- `TouchPanel/Interop/Input/SendInputHelper.cs` — reuse (existing); no new P/Invoke.
- `TouchPanel/Actions/IKeyInput.cs` — existing seam; used for a fake in tests.
- `TouchPanel/App.xaml.cs` — register `SendKeysHandler` in `CreateActionExecutor`.

**Dependencies:** T-01.

**Implementation notes.** One simultaneous combination; no sequence/delay/repeat. Unknown key/modifier, modifier-only, or malformed combos disable the widget at validation (RF-047). A bare key is valid for a widget; hotkeys require a modifier (enforced in T-09). Delivery targets the foreground window; never activate the panel. Redaction-safe logs.

**Tests.**
- Mapping for letters/digits/F1–F24/named keys; unknown key/modifier rejected.
- Modifier down-before/key/release-after ordering; **modifiers released on simulated target-key failure** (no stuck modifiers).
- Handler returns a failed `ActionExecutionResult` on synthesis failure/rejection; success path uses the fake `IKeyInput`.
- Executor dispatch for `sendKeys`; `ActionExecutionResult` no-throw contract unchanged.

**Acceptance criteria.** All AC-M2-11 unit cases pass; a configured `sendKeys` reaches the executor with modifiers always released.

**Requirements:** RF-057, M2-RF-571..576, RF-047, RF-050, MD-2.

---

## T-03 — Monitoring bounded history

**Phase:** 1 (W4)

**Objective.** Give the monitoring layer a fixed-capacity per-metric history that widgets read without polling hardware.

**Files/directories.**
- `TouchPanel/Monitoring/RingBuffer.cs` (pure) — fixed-capacity, O(1)/allocation-free append, consistent-copy read.
- `TouchPanel/Monitoring/SensorHistoryStore.cs` — per-metric buffers + tracked-key set (add/release), capacity math, history slice reads; a read-only boundary type.
- `TouchPanel/Monitoring/MonitoringService.cs` — feed every produced snapshot into the history store.
- `TouchPanel/App.xaml.cs` — construct/register the store; shutdown participant.

**Dependencies:** T-01.

**Implementation notes.** Capacity = `clamp(ceil(windowSeconds / intervalSeconds), min, 3600)`. Buffers are created lazily for keys referenced by the current config (Sparklines + Gauge); unreferenced keys are released on reload/page change. Unavailable samples stored as a null marker (gaps), never zero. Writes on the polling thread; reads under a lock returning a copy so the UI thread never sees torn data. No second polling path.

**Tests.**
- Capacity math + clamp; append wraps at capacity; concurrent read returns a consistent slice; gaps preserved; tracked-set add/release; disposal clears buffers.

**Acceptance criteria.** AC-M2-03 unit cases pass; no unbounded collection; memory bound computable.

**Requirements:** RF-069, RNF-012, M2-RF-691..697, M2-RNF-121, M2-RF-692, MD-5.

---

## T-04 — Configurable polling interval

**Phase:** 1 (W4)

**Objective.** Drive the polling loop from `telemetry.pollIntervalSeconds` (0.5–5 s) and apply changes live without leaking loops.

**Files/directories.**
- `TouchPanel/Monitoring/MonitoringService.cs` — accept a configurable interval; expose a safe restart/update path; recompute history capacity.
- `TouchPanel/Config/Validation/ConfigValidator.cs` — clamp bounds (from T-01 if not already).
- `TouchPanel/Services/Config/` — interval apply hook (used by T-11's pipeline; a direct path here is acceptable).

**Dependencies:** T-03.

**Implementation notes.** One loop only; a live change restarts it (or rebases the timer) and recomputes capacity preserving what fits (oldest-dropped). Interval mutation stays on the background monitoring path; never mutates UI directly. Default `1 s` preserves M1 behavior.

**Tests.**
- Effective cadence matches the configured interval (fake `TimeProvider`).
- Live change: no duplicate loop, capacity recomputed, latest snapshot retained within race tolerance.
- Clamp `0.5–5 s` with warning.

**Acceptance criteria.** AC-M2-04 passes; idle rate discipline preserved.

**Requirements:** RF-071, M2-RF-711..714, M2-RF-695, M2-RNF-003, MD-6.

---

## T-05 — Throttled monitoring backend re-initialization

**Phase:** 1 (W4)

**Objective.** Retry a failed monitoring backend on a bounded throttle and recover health, without busy loops or elevation.

**Files/directories.**
- `TouchPanel/Monitoring/BackendReinitializer.cs` (or logic in `MonitoringService`) — throttle via `TimeProvider`, log throttled.
- `TouchPanel/Monitoring/MonitoringService.cs` — consult `ISensorSourceHealth`, mark `SubsystemHealth` degraded/restored.

**Dependencies:** T-03.

**Implementation notes.** At most one re-open attempt per `5 min` (default); no tight loop; runs on the background path; never requests elevation; a failed backend leaves monitoring degraded while the loop keeps publishing unavailable snapshots.

**Tests.**
- Throttle gating with fake `TimeProvider`; a successful retry restores health and resumes snapshots; repeated failures log throttled; no loop starvation.

**Acceptance criteria.** AC-M2-03/M2-RF-701 behavior verified; app remains usable.

**Requirements:** RF-070, M2-RF-701..704, M2-RNF-126, MD-7.

---

## T-06 — Slider widget

**Phase:** 1 (W5)

**Objective.** Ship a config-driven, touch-first Slider that drives a numeric action parameter with throttled emission.

**Files/directories.**
- `TouchPanel/ViewModels/SliderWidgetViewModel.cs` — parse range/step/orientation/value; value→`volume Set` mapping; throttled emission; unsupported-action disable; transient error state.
- `TouchPanel/Widgets/SliderWidget/SliderWidgetTemplates.xaml` (+ `WidgetTemplateKeys`, `WidgetRegistration`).
- `TouchPanel/Widgets/WidgetRegistry.cs` — register `slider`.
- `TouchPanel/Theme/Styles.xaml` — slider track/handle ≥ 40 DIP, no hover-only affordance.

**Dependencies:** T-01.

**Implementation notes.** Horizontal maps min→max left→right; vertical bottom→top. Drag updates continuously; emits at most one action per `100 ms` or per step change; release does not duplicate. Only the `volume Set` numeric binding is supported in M2; any other bound action type disables the slider with an indicator. Works without keyboard focus.

**Tests.**
- Range/step/orientation/value validation + hostile input normalization.
- Value→`level` mapping onto `0–100`; throttle counts; unsupported action disables; failure shows transient error.

**Acceptance criteria.** AC-M2-15 (slider) passes; AC-M2-16 verified manually in T-18.

**Requirements:** RF-040, M2-RF-401..406, RF-025, MD-12 (template/registry).

---

## T-07 — Sparkline widget

**Phase:** 1 (W5)

**Objective.** Ship a bounded-history Sparkline that renders monitoring history on the UI thread without polling.

**Files/directories.**
- `TouchPanel/ViewModels/SparklineWidgetViewModel.cs` — read history slice / subscribe to `SnapshotAvailable`; map to points with gaps; auto/manual scale; update-on-change only.
- `TouchPanel/Widgets/SparklineWidget/SparklineWidgetTemplates.xaml`.
- `TouchPanel/Widgets/WidgetRegistry.cs` — register `sparkline`.

**Dependencies:** T-03.

**Implementation notes.** Unknown sensor key disables the widget (RF-047). Render at most once per snapshot; no idle animation; no re-render when history/scale unchanged. Unavailable samples render as gaps; all-unavailable shows "Unavailable". Bounded points by ring capacity. Never calls hardware or `PublishOnce`.

**Tests.**
- History→points mapping incl. gaps; auto vs manual scale; all-unavailable state; update-on-change only; bounded point count; unknown key disables; render failure degrades.

**Acceptance criteria.** AC-M2-15 (sparkline) passes; AC-M2-16 verified manually in T-18.

**Requirements:** RF-041, M2-RF-411..416, M2-RF-412 (seam), RNF-012, MD-5.

---

## T-08 — Toggle state persistence

**Phase:** 1 (W5)

**Objective.** Optionally persist Toggle on/off state across restarts via `state.json`, never `panel.json`.

**Files/directories.**
- `TouchPanel/Config/RuntimeState.cs` — add `ToggleStates` map.
- `TouchPanel/Config/StateStore.cs` — serialize/deserialize the map; non-fatal on absent/shape mismatch.
- `TouchPanel/ViewModels/ToggleWidgetViewModel.cs` — read initial state for `persistState` toggles; write on flip (debounced/batched).
- `TouchPanel/App.xaml.cs` — wire the persisted-state lookup into `WidgetCreationContext`.

**Dependencies:** T-01.

**Implementation notes.** Key `"<pageId>/<widgetId>"`; default `persistState=false` (D-15 preserved). Stale keys ignored/pruned; a flip must not mark config dirty, rewrite `panel.json`, or trigger a reload. Writes flow through the same app-write suppression contract that T-11 will formalize.

**Tests.**
- Restore/reset behavior; atomic writes; absent/shape-mismatched map non-fatal + regenerated; stale-key pruning; flip does not touch `panel.json`.

**Acceptance criteria.** AC-M2-23 unit/positive cases pass.

**Requirements:** RF-045, M2-RF-451..455, RF-073, MD-8.

---

## T-09 — Global hotkeys

**Phase:** 1 (W3)

**Objective.** Register global hotkeys that fire actions while another app/game has focus, without stealing focus, and handle failures gracefully.

**Files/directories.**
- `TouchPanel/Interop/Input/HotKeyNative.cs` — `RegisterHotKey`/`UnregisterHotKey` P/Invoke.
- `TouchPanel/Input/HotKeyParser.cs` (pure) — combo parse/validate (≥1 modifier, known key/modifiers).
- `TouchPanel/Input/HotKeyBinding.cs`, `HotKeyService.cs` — resolve `hotkeys[]`, register on a hidden UI-thread window (reuse `MessageWindow`), map `WM_HOTKEY`, dispatch via `IActionExecutor`, aggregate failures + one notification, no retry.
- `TouchPanel/Input/IHotKeyRegistration.cs` — test seam.
- `TouchPanel/App.xaml.cs` — construct/register; shutdown participant; re-register hook for T-11.

**Dependencies:** T-02.

**Implementation notes.** Registration/unregistration and hidden-window (re)creation are always marshalled to the **UI thread**. Each hotkey binds exactly one action. Modifier-less entries are invalid (D-M2-4). A failed registration is logged with the combination, skipped; the rest continue; one aggregated tray notification; no retry loop. Handling never activates the panel or changes the foreground. Re-registration unregisters all then re-applies (used on reload/shutdown).

**Tests.**
- Parser: parse/duplicate/≥1-modifier/unknown/invalid skipping.
- Registration-result handling with fake `IHotKeyRegistration`: skip failed, continue, one aggregate, no retry.
- UI-thread marshalling asserted; re-registration leaves no leaked registrations.

**Acceptance criteria.** AC-M2-13 passes; AC-M2-14 verified manually in T-18.

**Requirements:** RF-058, RF-059, M2-RF-581..586, M2-RF-591..595, D-M2-4, MD-3.

---

## T-10 — Local shortcuts

**Phase:** 1 (W3)

**Objective.** Add the M2 fixed edit-mode local shortcuts, separate from global registration.

**Files/directories.**
- `TouchPanel/Views/EditOverlay.xaml(.cs)` / `MainWindow.xaml(.cs)` — key handling for the fixed set.
- `TouchPanel/ViewModels/EditModeService.cs` — commands for remove/duplicate/toggle-snap (used by T-13/T-15 too).
- `TouchPanel/Input/` — keep local handling distinct from `HotKeyService`.

**Dependencies:** T-09.

**Implementation notes.** Fixed app-defined set (Esc, arrows from M1 + remove, duplicate, toggle snap); active only in edit mode / when the panel has keyboard focus; inert in normal mode; no focus-side effects. Not user-configurable (RF-061 is M3).

**Tests.**
- Context gating: shortcuts fire only in edit mode; no-op in normal mode; no activation change.

**Acceptance criteria.** AC-M2-18 (shortcut portion) verified; behavior asserted.

**Requirements:** RF-060, M2-RF-601..604, MD-4.

---

## T-11 — Hot reload

**Phase:** 2 (W6)

**Objective.** Apply external `panel.json` edits live with validate-then-apply, whole-file semantics, and no self-triggered loops.

**Files/directories.**
- `TouchPanel/Services/Config/ReloadDecision.cs` (pure) — event + last-written-content hash + in-flight-write flag → `Ignore`/`Debounce`/`Reload`.
- `TouchPanel/Services/Config/ConfigReloadController.cs` — debounced `FileSystemWatcher` (`Filter="panel.json"`, limited `NotifyFilter`), self-write suppression for config **and** state stores, read → migrate → validate → swap.
- `TouchPanel/Services/Config/PanelConfigApplier.cs` — shared apply pipeline (window options, active page rebuild, monitor target, telemetry interval, tracked metrics, hotkey re-registration), UI-thread-marshalled.
- `TouchPanel/Config/JsonConfigStore.cs` / `StateStore.cs` — self-write window hooks.
- `TouchPanel/App.xaml.cs` — construct/register; shutdown participant; route import/settings through `PanelConfigApplier`.

**Dependencies:** T-01..T-10.

**Implementation notes.** Filter the watcher to `panel.json` only so `state.json`/temp/backup/recovery writes emit no event; additionally suppress all app-originated writes via a self-write window + content-hash comparison. On reload, only a fully valid candidate is applied atomically; invalid/future keeps the running config and notifies (throttled); the watched file is never written. Watcher failures degrade gracefully; the watcher/debounce timer are disposed on shutdown.

**Tests.**
- Pure `ReloadDecision`: ignore/debounce/reload, content-unchanged no-op, self-write suppression (config + state), invalid keeps running config, future schema not interpreted.
- Temp-dir integration: watcher filtered to `panel.json`; `state.json`/temp/backup writes emit no reload.

**Acceptance criteria.** AC-M2-19 and AC-M2-20 pass.

**Requirements:** RF-082, M2-RF-821..830, RF-080, M2-RNF-122, MD-9, MD-10.

---

## T-12 — Import / export configuration

**Phase:** 2 (W6)

**Objective.** User-triggered export/import with validation and a pre-import backup.

**Files/directories.**
- `TouchPanel/Config/ImportExport/ConfigImportExport.cs` — export current `PanelConfig` (incl. `schemaVersion`); import read → migrate → validate → pre-import backup → swap.
- `TouchPanel/App.xaml.cs` — apply imported config through `PanelConfigApplier`.

**Dependencies:** T-11.

**Implementation notes.** M2 has no secret fields; never log content (paths/summaries only). Imported content is untrusted (RF-081). I/O off the UI thread; failures change nothing. File picker supplied by the settings window (T-17) or a default app-data path.

**Tests.**
- Export includes `schemaVersion`; import validate + migrate + pre-import backup; failed import leaves config intact; path-only logging.
- Temp-dir round-trip: export → import reproduces the effective config.

**Acceptance criteria.** AC-M2-21 passes.

**Requirements:** RF-083, M2-RF-831..835, MD-11.

---

## T-13 — Full edit UI 1: add/remove/duplicate + property editor

**Phase:** 2 (W6)

**Objective.** Extend edit mode with widget lifecycle operations and a touch property editor.

**Files/directories.**
- `TouchPanel/ViewModels/EditModeService.cs` — page-config mutation: add (deterministic first-free placement, generated id), remove, duplicate (new id, 16 DIP offset); property edits; dirty tracking.
- `TouchPanel/ViewModels/Edit/PropertyEditorViewModel.cs` — position/size/label + type-specific property model.
- `TouchPanel/Views/EditOverlay.xaml(.cs)` (or new `Views/Edit/` panels) — palette, selection, property panel; touch-first.
- `TouchPanel/Widgets/WidgetRegistry.cs` — expose the built-in type list for the palette.

**Dependencies:** T-06, T-07, T-08.

**Implementation notes.** Add/remove/duplicate spans the six built-in types; unknown types remain disabled placeholders and are not offered for add. Property values are validated with the same rules as `ConfigValidator`; invalid input is blocked/clamped and never mutates the in-memory config. Actions must not fire in edit mode. All changes persist only through the existing single-writer store on exit/save.

**Tests.**
- Add placement/id generation; remove/duplicate (offset, new id); property validation/blocking; dirty tracking; save-through-fake-store; no action execution in edit mode; unknown type not addable.

**Acceptance criteria.** AC-M2-17 (lifecycle/property portion) passes; AC-M2-18 verified in T-18.

**Requirements:** RF-043, M2-RF-430..434 (partial), M2-RF-437..440, MD-12.

---

## T-14 — Full edit UI 2: action picker (browse) + label/icon/colors

**Phase:** 2 (W6)

**Objective.** Complete the property editor with action selection (including open-program browse), label/icon, and appearance overrides.

**Files/directories.**
- `TouchPanel/ViewModels/Edit/ActionPickerViewModel.cs` — catalog of M1 types + `sendKeys`; typed parameter editors; **executable file browse** for run-command ("open program").
- `TouchPanel/Views/Edit/` — action picker, label/icon picker (built-in catalog), color/font/opacity editors.
- `TouchPanel/Widgets/` — built-in icon catalog resource (1–64 entries).
- `TouchPanel/Config/WidgetConfig.cs` — appearance override fields (`icon`, `foreground`, `background`, `accent`, `fontSize`, `opacity`).
- `TouchPanel/Config/Migrations/` — new `V2ToV3Migration : IConfigMigration` (additive: set `schemaVersion=3`, default the new appearance fields to theme defaults / null; no v2 field removed) completing the ordered chain.
- `TouchPanel/Config/PanelConfig.cs` — bump `CurrentSchemaVersion` to the delivered **3**.

**Dependencies:** T-13.

**Implementation notes.** Committing an invalid action disables the widget with an indicator. Browse selects an `.exe` path; arguments/working directory stay discrete fields; the M1 safe-launch path is unchanged. Label ≤ 128 chars; icon from the bundled catalog only; colors `#RRGGBB`/`#AARRGGBB`, font `8–96 DIP`, opacity `0.0–1.0`; invalid values fall back with a warning. The appearance fields are persisted via the `v2→v3` step so older (v1/v2) files still load.

**Tests.**
- Action catalog selection + `sendKeys` parameter editing; browse path model; invalid action disables; label/icon bounds; color/font/opacity parse + fallback.
- `v2→v3` migration fixture: a v2 file gains defaulted appearance fields, validates, and saves; appearance-override DTO round-trip.

**Acceptance criteria.** AC-M2-17 (action/label/icon/colors portion) passes.

**Requirements:** RF-043, M2-RF-434..436, M2-RF-434 (browse), M2-RNF-128, MD-13.

---

## T-15 — Grid snap + alignment guides

**Phase:** 2 (W6)

**Objective.** Add configurable grid snapping and alignment aids to edit mode without losing free positioning.

**Files/directories.**
- `TouchPanel/ViewModels/Edit/GridSnap.cs` (pure), `AlignmentGuides.cs` (pure) — snapping + guide computation.
- `TouchPanel/ViewModels/EditModeService.cs` — compose snap with existing clamp/min-size; nudge uses `gridSize` when on.
- `TouchPanel/Views/EditOverlay` — guide rendering during drag; snap toggle + free-position override.

**Dependencies:** T-13.

**Implementation notes.** `gridSize` `2–64 DIP` (default `8`); snap on by default; snap then clamp then ≥40 DIP. Guides within a `4 DIP` threshold to other widgets/page edges/center; snap-to-alignment when enabled; guides render only during drag and are not persisted.

**Tests.**
- Grid rounding; threshold edge cases; clamp-composition order; hostile/non-finite input; nudge with snap on/off; free override bypasses snap.

**Acceptance criteria.** AC-M2-17 (grid portion) passes; AC-M2-18 verified in T-18.

**Requirements:** RF-044, M2-RF-441..446, MD-14.

---

## T-16 — Page-switching UI

**Phase:** 2 (W6)

**Objective.** Add a touch affordance to switch the active page, persisted and non-activating.

**Files/directories.**
- `TouchPanel/ViewModels/PageSwitcherViewModel.cs` — page list, current page, switch command.
- `TouchPanel/Views/PageSwitcher/` — in-panel selector UI.
- `TouchPanel/ViewModels/PageViewModel.cs` / `App.xaml.cs` — rebuild page on switch (dispose old VMs/subscriptions) and persist `activePageId`.

**Dependencies:** T-11.

**Implementation notes.** Switch without activation or foreground change; persist `activePageId` atomically; invalid id falls back to first page + log; rebuild failure leaves the panel stable; the switch save is a self-save that must not trigger a reload.

**Tests.**
- Switch selects/persists; invalid id fallback; rebuild disposes old subscriptions; self-save does not trigger reload.

**Acceptance criteria.** AC-M2-22 (page-switch portion) verified.

**Requirements:** RF-091, M2-RF-911..915, RF-037, MD-15.

---

## T-17 — Settings window + monitor picker

**Phase:** 2 (W6)

**Objective.** Provide a tray-accessible settings window and a monitor picker that configure and apply live.

**Files/directories.**
- `TouchPanel/ViewModels/Settings/SettingsViewModel.cs` — `alwaysOnTop`, start-with-Windows (reuse `IStartupRegistration`), telemetry interval, import/export buttons, read-only diagnostics.
- `TouchPanel/ViewModels/Settings/MonitorPickerViewModel.cs` — discovered monitors via `IMonitorEnumerator`/`MonitorDescriptor`, current/unavailable target, fallback policy, selection → identity block.
- `TouchPanel/Views/Settings/`, `Views/MonitorPicker/` — XAML + minimal code-behind.
- `TouchPanel/Services/Tray/TrayService.cs` / `App.xaml.cs` — add a Settings menu item; wire apply via `PanelConfigApplier`.

**Dependencies:** T-11, T-12.

**Implementation notes.** Settings is a deliberate activating window (D-6 analogy) and must not change the panel's no-activate behavior. Valid changes save atomically and apply live (topmost, interval, monitor target); invalid input is blocked inline; start-with-Windows reuses M1 registration; hotkey editing is not present (RF-061/M3). Monitor selection writes the identity block and re-places without restart.

**Tests.**
- Settings validation/apply intent; start-with-Windows toggle mapping; picker identity mapping + current/unavailable display; selection persistence.

**Acceptance criteria.** AC-M2-22 (settings + monitor picker portions) verified.

**Requirements:** RF-092, RF-093, M2-RF-921..925, M2-RF-931..934, RF-003/004, MD-15.

---

## T-18 — Manual verification matrix, performance, acceptance run

**Phase:** 2 (W6)

**Objective.** Execute the M2 manual matrix (plan §7.3) and performance/soak checks against the finished build; fix defects within M2 scope.

**Files/directories.**
- No planned production changes; fixes as found; results recorded (e.g., `specs/002-automation/verification-notes.md` if the human wants a record).

**Dependencies:** T-17.

**Implementation notes.** Run MV-M2-01..08 (focus/hotkeys, send-keys, slider/sparkline, toggle persistence, edit UI, hot reload, import/export/settings/monitor picker/page switch, performance/soak). Measure RNF-001 idle CPU (panel visible/hidden with hotkeys + history), RNF-002 8 h soak, and the documented history memory bound. Record any accepted limitation (e.g., exclusive-fullscreen).

**Tests.** MV checklist execution; final `dotnet build`/`dotnet test` green; performance/soak captures.

**Acceptance criteria.** Every AC-M2-01..AC-M2-25 has a pass result or a documented, human-accepted limitation.

**Requirements:** RNF-012, M2-RNF-121/122/127, RNF-001/RNF-002, and final verification of all M2 requirements in plan §9.

---

## Execution notes

- Tasks run in order; dependencies are the minimum. Phase 1 (T-01..T-10) and Phase 2 (T-11..T-18) are both part of the single M2 milestone (D-M2-1).
- `dotnet build` and `dotnet test` must stay green after every task; a task is complete only when its tests and listed manual checks (or explicitly deferred ones) are done.
- No dropped-feature work (RF-042/054/055/056/087, `delay`, shell-open) and no M3/Later work (RF-046, RF-061, RF-072, RF-084) may be partially implemented "for later".
- The reviewer agent validates against the spec without modifying code; remediation is a new task appended in a later planning pass.
