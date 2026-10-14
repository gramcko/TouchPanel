# TouchPanel — Specification 005: Edit-UX Fixes (M5)

| Field | Value |
|---|---|
| Spec ID | 005-edit-ux-fixes |
| Status | Draft (M5 — specification + plan + tasks) |
| Date | 2026-10-10 |
| Phase | M5 — small "edit-UX fixes" milestone |
| Author | Planner agent |
| Inputs | `specs/001-foundation/spec.md` (RF-018/RF-026/RF-032/RF-034/RF-039/RF-043/RF-047/RF-058/RF-079/RF-080/RF-084, `D-24`/`D-25`), `specs/002-automation/*` (M2 sub-requirements), `specs/003-customization/*` (M3), `specs/004-promotion/*` (M4), and the delivered implementation in `TouchPanel/` (schema **v4**, 1514 tests green) |
| Scope | Five concrete human-requested edit-UX fixes (delete affordance, sensor picker, button-from-hotkey, add-widget bug, theme-aware defaults). No new product surface; no schema bump beyond v4. |
| Out of scope | `RF-072` (skipped), the dropped set (RF-042/054/055/056/087, `delay`, shell-open), any macro/sequence work, plugin/DI/event-bus, elevation. |
| Next steps | `plan.md` and `tasks.md` in this directory |

> **M5 framing.** M1–M4 are accepted (Core Release + M3 + M4; schema **v4**, **1514 tests green**). M5 is a **small, bug-and-friction milestone**: it makes five already-scoped capabilities behave correctly and predictably in the shipped edit UI. It **reuses every existing seam** and renumbers/retags nothing. All base IDs (`RF-0xx`, `RNF-xxx`) keep their 001 meaning; M5 adds `M5-RF-###` / `M5-RNF-###` sub-requirements with explicit `[Maps RF-0xx]` back-references.

---

## 1. Goal

The delivered edit UI works for its M4 acceptance matrix, but five concrete friction points were reported by the human while using it for real. M5 must fix each with a **testable requirement** and a **vertical implementation slice**:

1. **Delete a widget by touch from edit mode** — a visible "X" on the selected widget, a confirmation, and deletion through the existing remove/persistence path.
2. **Pick a sensor from a list** — replace free-text sensor entry (Gauge/Sparkline/Bar) with a list of available sensor keys, including per-core CPU and fans when available, with a sensible fallback.
3. **A button that runs a created hotkey** — let a Button bind an action that references a configured global hotkey's action, without duplicating or insecurely re-expressing it.
4. **Fix the add-widget "Apply" defect** — adding via the palette must appear immediately **and** persist through the single-writer store.
5. **Theme-aware defaults** — new widgets and the editor must get sensible default color/font-size/opacity values per active theme instead of blanks or hard-coded values.

**Success for M5** means each fix is observable on the touchscreen, covered by deterministic tests (no hardware, no WPF beyond the existing real-BAML pattern), the suite stays green, the schema stays **v4**, and no M1–M4 invariant regresses.

## 2. Scope

**In scope (five fixes):**

- **Delete affordance + confirmation** (`M5-RF-501..504`): an X on the selected widget, an "Are you sure?" confirm surface, cancel semantics, touch target, and reuse of `EditModeService.RemoveSelectedWidget` + the single-writer save.
- **Sensor picker** (`M5-RF-510..513`): a WPF-free sensor-catalog seam over the existing `SensorKeys` / `SensorSnapshot` / `SensorSelector` model; a picker in the property editor for Gauge/Sparkline/Bar; fallback and unknown-value preservation.
- **Hotkey-reference action** (`M5-RF-520..524`): a new additive `hotkey` action shape that references a configured `HotkeyConfig.Id`; runtime execution resolves and runs that hotkey's bound action through the existing `IActionExecutor`; the action picker offers it and lists the user's created hotkeys; validation disables a widget whose reference is missing.
- **Add-widget flow** (`M5-RF-530..534`): reproduce the defect end-to-end (including the page host, not just the service), wire the property panel Apply, and make the added widget appear immediately and persist atomically.
- **Theme-aware defaults** (`M5-RF-540..544`): a WPF-free per-theme appearance-default table; seeding new widgets at creation; theme-aware editor initial/placeholder values; precedence preserved.

**In scope as integration only:** extending existing seams (`EditModeService`, `PropertyEditorViewModel`, `ActionPickerViewModel`, `EditOverlay.xaml(.cs)`, `WidgetPageHost.xaml`, `PageViewModel`, `WidgetRegistry`/`WidgetRegistration`, `ConfigValidator`, `ActionConfig`/`ActionTypes` + a handler, `Config/HotkeyConfig`, `PanelConfig.Hotkeys`, `SensorKeys`/`SensorSnapshot`, `ThemeIds`, `WidgetAppearance`, `IConfigStore`/`JsonConfigStore`, `App.xaml.cs`). No M1–M4 behavior is redefined.

**Out of scope (unchanged):**

- `RF-072` (skipped) and the dropped set stay dropped.
- No new widget types, no macro/sequence/HTTP, no new execution surface, no elevation.
- No schema bump beyond v4 (see `M5-RF-524`, `M5-RF-550`).
- No plugin system, event bus, DI container, or repository layering.

**Focus-preservation scope.** `RF-018` is preserved: entering edit mode is still the only deliberate activation (`D-6`); the X and the confirmation are **in-overlay, in-panel** affordances (no new activating window), and normal-mode interaction never activates the panel.

## 3. Non-goals

- **No new product requirements.** M5 fixes and refines already-scoped behavior (RF-043/RF-034/RF-084/RF-047/RF-032).
- **No renumbering or retagging** of any `RF-0xx`/`RNF-xxx`.
- **No schema bump beyond v4.** The delete/sensor/theme fixes add no field; the hotkey-reference action is an additive discriminator that the existing unknown-action seam preserves on older builds.
- **No redesign** of the edit/settings/hotkey surfaces beyond the five fixes.
- **No new persistence mechanism.** Everything routes through the existing single-writer `IConfigStore`.
- **No WPF-presentation tests** beyond the existing real-BAML instantiation pattern; no UI-automation framework.

## 4. Relationship to 001–004 and requirement-ID rules

This document **does not renumber or change any 001–004 requirement.** It adds testable sub-requirements in the `M5-RF-###` / `M5-RNF-###` namespace, each with a `[Maps RF-0xx]` back-reference.

- `M5-RF-50x` = delete affordance (`RF-043`/`RF-026`/`RF-018`).
- `M5-RF-51x` = sensor picker (`RF-034`/`RF-043`/`RF-079`).
- `M5-RF-52x` = hotkey-reference action (`RF-047`/`RF-058`/`RF-049`/`RF-079`/`RF-074`).
- `M5-RF-53x` = add-widget flow (`RF-043`/`RF-032`/`RF-039`/`RF-077`/`RF-080`).
- `M5-RF-54x` = theme-aware defaults (`RF-084`/`RF-043`(RF-436)/`RF-046`/`RF-034`).
- `M5-RF-550/551` = cross-cutting schema/docs/invariant requirements.
- `M5-RNF-55x` refine M1's RNFs for this milestone.

A base requirement is *fully covered* when all its sub-requirements are accepted. No base requirement is retagged or moved.

## 5. Functional requirements

Conventions: **Must** = verifiable. **(default)** values are chosen defaults the human can override. "UI thread" = the WPF dispatcher thread. API type names are the delivered ones (`EditModeService`, `PropertyEditorViewModel`, `ActionPickerViewModel`, …).

### 5.1 Delete a widget from edit mode (RF-043)

- **M5-RF-501 [Maps RF-043/RF-026/RF-018] X affordance on the selected widget.** While edit mode is active and a widget is selected, an "X" delete affordance must be shown **in the top-right corner of that widget** in the edit overlay. It must be visible without hover (touch-first), rendered above the move surface so it is tappable, and shown only for the selected widget (or only for editable, on-page widgets). It must not appear in normal mode and must not appear for a widget that cannot be edited (unknown type). The X is an in-panel overlay element — **no new window**.
- **M5-RF-502 [Maps RF-043] "Are you sure?" confirmation with cancel.** Tapping the X must **not** delete immediately. It must show a touch-friendly confirmation surface asking "Are you sure?" with a confirm (Delete) and a cancel (Cancel) affordance. **Cancel** must dismiss the surface, keep the widget, keep the selection, and change no configuration/dirty state. Confirming must remove the widget.
- **M5-RF-503 [Maps RF-043/RF-077/RF-080] Deletion reuses the existing remove + single writer.** A confirmed delete must go through the existing `EditModeService` remove path (mark dirty, remove from the live `PageViewModel` and the matching `PageConfig`, dispose the widget view model) and persist **only** through the existing `IConfigStore.SaveAsync` on the next edit-mode exit/save. No new persistence path and no direct file write. A failed save keeps the in-memory state and is reported (RF-077), not silently dropped.
- **M5-RF-504 [Maps RF-018/RF-025] Touch target, focus, and keyboard parity.** The X and both confirmation buttons must each meet the ≥ 40 DIP touch target. The affordances must be `Focusable="False"` (or otherwise not pull keyboard focus away from the panel's edit-mode key handling) so the existing Escape/Delete local shortcuts still work and normal-mode no-activate behavior is unchanged. Pressing the X must never fire the widget's bound action (edit mode owns taps, `M2-RF-439`).

**Edge cases.** X on an already-removed widget (no-op); confirmation open then selection changes (confirmation closes or re-targets the originally selected widget — default: the confirmation targets the widget it was opened for and is cancelled if that widget is no longer selected/editable); delete of the last widget (allowed; empty page is valid); delete while a save is in-flight (serialized by the store's gate).

### 5.2 Sensor selection list (RF-034)

- **M5-RF-510 [Maps RF-034/RF-063] Sensor catalog seam (WPF-free).** A WPF-free sensor-catalog source must expose the selectable sensor keys for the *current* machine, built over the existing `SensorKeys` model and, when a monitoring snapshot is available, the dynamically discovered per-core CPU (`cpu.core.<n>`) and fan (`fan.<n>`) keys (from `SensorSnapshot.CpuCorePercents` length and `FanSpeedsRpm` length). Each entry carries the stable key and a human-readable display name. The seam lives in the monitoring/config layer, not in a view.
- **M5-RF-511 [Maps RF-043/RF-034] Picker replaces free-text entry.** For Gauge, Sparkline, and Bar, the property editor must offer the sensor as a **selectable list** (`ItemsSource` bound to `M5-RF-510`, display name + key) instead of a free-text box. Selecting an entry writes the key with the existing validation rules. The picker must be touch-first (≥ 40 DIP) and must not require typing.
- **M5-RF-512 [Maps RF-079/RF-034] Fallback and unknown-value preservation.** When monitoring is unavailable or no snapshot has arrived, the list must fall back to the fixed aggregate keys (`cpu.total`, `ram.percent`, `gpu.usage`, `gpu.vram.percent`, `disk.activity`, `net.up`, `net.down`, `temp.cpu`, `temp.gpu`, and the other documented aggregate keys) rather than being empty. If the widget's configured sensor is **not** in the available list (hand-edited, hardware removed, or a family key with no current members), the picker must still show and preserve that key (e.g. as an extra "current" entry) and never silently clear it. Selecting a key the snapshot does not know at runtime keeps the existing RF-047 behavior (widget disabled with a reason) but is never dropped.
- **M5-RF-513 [Maps RF-034/M5-RF-511] Applies to Gauge, Sparkline, and Bar.** The picker must appear exactly for the widgets whose `HasSensor` is true (currently Gauge, Sparkline, Bar) and must not change the sensor field shape (still a string key in `panel.json`).

**Edge cases.** No hardware backend (fallback list); more cores/fans than shown at editor creation (list is rebuilt when the editor is created; a live-snapshot refresh is optional and must never move the user's selection); a very long core/fan list (bounded/scrollable; no unbounded growth — capped by the snapshot, which is already fixed-size per poll).

### 5.3 Button action that runs a created hotkey (RF-047/RF-058)

- **M5-RF-520 [Maps RF-047/RF-058] Hotkey-reference action shape.** A new **additive** action discriminator (`"hotkey"`) and a matching `ActionConfig` subtype carrying the referenced **hotkey id** must be introduced. The referenced hotkey is an entry in `PanelConfig.Hotkeys` (by `HotkeyConfig.Id`). The action does **not** embed or copy the hotkey's key/combination or its bound action; it references the hotkey so the single source of truth stays the hotkey definition. The action model stays plain data (a string id).
- **M5-RF-521 [Maps RF-047/RF-049] Execution reuses the hotkey's bound action through the executor.** When the widget runs, the action must **resolve the referenced hotkey's `Action` at execution time** and execute **that** action through the existing `IActionExecutor` (the same safe execution path the hotkey itself and every widget action use). Consequences: (i) editing the hotkey's action applies to the button without touching the widget; (ii) removing the hotkey makes the button's reference fail; (iii) no new execution surface, no elevation, and no shell string is introduced. A hotkey-reference action must **not** recursively run another hotkey reference (no chaining): if the resolved hotkey's action is itself a hotkey reference, the execution fails gracefully. The reference resolves through a narrow, injectable seam (a resolver over the live hotkeys) so it is unit-testable without the OS.
- **M5-RF-522 [Maps RF-043] Action picker offers "Hotkey" and lists created hotkeys.** The edit-mode action picker must add a **"Hotkey"** entry alongside the existing types (None, Run command, Send keys, Media key, Volume, Mute) and, when selected, show a touch-first list of the user's currently configured hotkeys (by id + human-readable combination when parseable, falling back to the id) to choose from. Choosing one writes the reference. An empty hotkey list must show a clear "no hotkeys configured yet" state, not a blank. This must work for the action-bearing built-in widgets (Button/Toggle/ImageButton).
- **M5-RF-523 [Maps RF-079/RF-058] Missing/removed/duplicate-id behavior.** Validation must cross-check each widget's hotkey-reference against the effective `config.Hotkeys`: a reference to an id that does not exist (including one removed later) must **disable the widget with a visible reason and a log entry** (RF-047), never drop it and never crash. Duplicate hotkey ids: the hotkey subsystem's existing first-wins semantics apply; the reference resolves to the **first** hotkey with that id (documented, deterministic). A blank reference id is invalid and disables the widget.
- **M5-RF-524 [Maps RF-074/RF-076/RF-079] Schema stays v4 (additive discriminator).** The new action type must **not** raise `PanelConfig.CurrentSchemaVersion`; no `V4ToV5` migration is added. A v4 file containing a `hotkey` action must round-trip through the canonical JSON unchanged on a build that understands it, and must be **preserved as an unknown action** (RF-079 seam) on a build that does not. This keeps all four `RF-075` load states intact.

**Edge cases.** Hotkey exists but its own action is invalid/missing (the referenced execution fails; the widget itself is only disabled when the *reference* is invalid, not when the target's action is invalid — the target's invalidity is handled when the hotkey is applied); hotkey with no modifiers (already rejected by the hotkey subsystem); reference to a hotkey whose action is `sendKeys`/`runCommand` (allowed; executed through the executor).

### 5.4 Add-widget flow bug (RF-043)

- **M5-RF-530 [Maps RF-043/RF-032] Normative expected add flow.** Adding a widget from the edit-mode palette must be one deterministic flow:

  1. In edit mode, tapping a palette item invokes a single **add command** carrying the widget type.
  2. The command validates: edit mode active, palette + widget factory present, and the type is one of the built-in palette types (unknown types are never addable, `M2-RF-430`). On any failed precondition it returns **without side effects** and surfaces a visible/diagnostic reason.
  3. On success it (a) generates a page-unique id, (b) appends a new `WidgetConfig` (type defaults + geometry + theme-aware appearance per `M5-RF-541`) to the active `PageConfig`, (c) materializes the widget view model and appends it to the live `PageViewModel` **with a collection-change notification**, (d) selects the new widget and opens its property editor, and (e) marks the layout dirty.
  4. The page host (`WidgetPageHost`) and the edit overlay must show the new widget **immediately** (same dispatcher tick), not only after a reload.
  5. The widget persists **only** through the existing single-writer `IConfigStore.SaveAsync` on the next edit-mode exit/save (or an explicit Apply); no file watcher is triggered by the app's own save.
  6. If building the view model fails, the config entry is removed so page and config never diverge (RF-012/RF-013 containment); edit mode and the panel stay usable.

- **M5-RF-531 [Maps RF-043] Apply is an explicit commit.** The property panel's **Apply** affordance must be wired to an explicit command (not merely a focusable no-op that forces `LostFocus`). Applying must (i) commit the focused field, (ii) leave the newly added widget selected and present, and (iii) be safe to invoke repeatedly (idempotent). After the palette add, Apply must never appear to "lose" the widget.
- **M5-RF-532 [Maps RF-032/RF-039] Immediate host + overlay refresh (root-cause fix).** The binding path that feeds the page host and the overlay must reliably re-materialize when the widget collection changes. The fix must make the added widget visible in the **page host** (the delivered tests only assert the overlay), by using a notifying collection and/or an explicit refresh, without rebuilding the whole page (no full reload, no flicker of existing widgets, no subscription leak).
- **M5-RF-533 [Maps RF-077/RF-080] Single-writer persistence and failure isolation.** The add must persist through `IConfigStore` only. A failed or canceled save keeps the in-memory added widget and marks the layout dirty for retry (RF-077), logs, and notifies; it must never partially write or leave config/page divergent.
- **M5-RF-534 [Maps RF-079] Diagnostics.** A rejected add (not in edit mode, no factory/palette, unknown type, view-model build failure) must be logged with the type and reason and must not throw.

**Root-cause hypothesis (H-M5-4).** The delivered add flow splits across two disconnected paths: (a) the palette button calls `EditModeService.AddWidget(type)` (which mutates the `PanelConfig` and the `PageViewModel`), and (b) the property-panel **Apply** button (`PropertyApplyButton` in `EditOverlay.xaml`) has **no command/Click handler** — it is only `Focusable="True"` to force a `LostFocus` field commit. Two consequences: (1) "hitting Apply" performs no add/commit, so if the add was rejected silently (edit-mode/factory/type gate) the user sees nothing; and (2) the page host refresh relies on `PropertyChanged(nameof(Widgets))` over `PageViewModel.Widgets`, exposed as a **non-notifying `ReadOnlyCollection<T>`** — WPF may not re-materialize `ItemsControl.ItemsSource` when the bound collection instance is unchanged, so an in-memory add can be invisible in the panel. The existing tests cover `EditModeService` and `EditOverlay` but **never the page host**, so the end-to-end defect is uncovered. The fix must reproduce at the host level, wire Apply, and make the collection notify. *(The implementing task confirms or refines this hypothesis before fixing.)*

### 5.5 Theme-aware defaults for new widgets and the editor (RF-084)

- **M5-RF-540 [Maps RF-084/RF-043(RF-436)] Per-theme appearance-default model (WPF-free).** A WPF-free table keyed by the canonical theme ids (`dark`, `light`, `highContrast`, `ThemeIds`) must define sensible default appearance values per theme: a foreground color, a background color, an accent color (all `#RRGGBB`/`#AARRGGBB`), a font size (within `WidgetAppearance` bounds), and an opacity (within bounds). The dark defaults must reproduce the delivered dark palette so the default is a no-op; light/high-contrast provide readable, touch-legible values (high contrast favors strong contrast and larger type). The table is data, never code, and is validated with the existing `WidgetAppearance` rules.
- **M5-RF-541 [Maps RF-043/RF-046] New widgets are seeded from the active theme.** When a widget is added (`M5-RF-530`), its new `WidgetConfig` must receive the active theme's default appearance values (per `M5-RF-540`) for the fields that apply to it, so a freshly added widget is **not blank and not hard-coded**: it starts with a sensible color/font-size/opacity. The type's registered `createDefaultConfig` still supplies type-specific defaults; theme defaults fill/override the appearance fields. The seeded values must be normalized/validated (invalid theme-default data must never produce an invalid config).
- **M5-RF-542 [Maps RF-043] Theme-aware editor values, no blanks.** The property editor's appearance section must present theme-aware values: when a widget's override is `null` (inherit), the editor shows the **active theme's default** for that field as the initial/placeholder value rather than a blank or a hard-coded constant (e.g. the current hard-coded `FontSize=13/15` in XAML must not be the source of truth). The user can accept the theme default (leave it inherited) or set an explicit override. The editor itself (labels, inputs, panel chrome) must read its colors/font sizes from the active theme resources (`DynamicResource`), so switching theme updates the editor live.
- **M5-RF-543 [Maps RF-084/RF-436] Interaction with theme keys, global accent, and per-widget overrides.** Precedence is unchanged and explicit: **per-widget override → global accent (`appearance.accentColor`) → active theme default**. A theme switch after a widget was created must still restyle any field the user left **inherited** (null); a field the user explicitly set (or that creation seeded) is a per-widget override and wins until the user clears it. "Clear to theme default" must restore live theme inheritance. Theme defaults must be applied consistently with the M3 `DynamicResource` layer (no hard-coded fill/font literals left as the source of truth).
- **M5-RF-544 [Maps RF-046/RF-084] Coverage across built-in types.** Theme-aware defaults and the editor presentation must apply to **all** built-in widget types, including the M3 types (Clock, ImageButton, Bar), with type-specific fields unaffected (e.g. Clock format/mode, Bar range, ImageButton icon). Unknown types (raw JSON) still get no editor and no seeding.

**Edge cases.** Unknown/absent active theme (fall back to `dark` defaults); widget created while monitoring/appearance is degraded (defaults still valid); a pre-existing widget (M4 file) with null appearance must **not** be rewritten on load (inheritance preserved) — only newly added widgets are seeded, and the editor only *displays* theme defaults for null fields; a live theme switch while the editor is open must update the displayed inherited values (or leave the user's in-progress edit untouched — default: in-progress explicit edits are preserved, inherited fields refresh).

## 6. Non-functional requirements

- **M5-RNF-551 [Maps RNF-006] Testability.** Every M5 behavior is covered by deterministic unit/integration tests (no hardware, drivers, elevation, network, or OS hotkey registration); the sensor catalog and hotkey-reference resolver use injected seams; the delete/confirm and add flows are testable through view models and the existing real-BAML instantiation pattern; `dotnet test` is the gate. Existing suites stay green.
- **M5-RNF-552 [Maps RNF-010] Layering and interop isolation.** `Actions/`, `Monitoring/`, `Config/`, and `Input/` stay WPF-free; P/Invoke stays only under `Interop/`; the theme-default table and sensor catalog are WPF-free; only `Views/`/`ViewModels/` presentation code references WPF. `LayerPurityTests` stays green.
- **M5-RNF-553 [Maps RF-018/RF-086] No focus steal and no elevation.** The X, the confirmation, the sensor picker, and the theme defaults never activate the panel or change the foreground window. Only the deliberate edit-mode entry (`D-6`) may activate. No M5 feature prompts for elevation/UAC.
- **M5-RNF-554 [Maps RF-080/RF-077/RF-082] Single writer, no reload loop.** All M5 persistence routes through the existing `IConfigStore` single writer (and `StateStore` where applicable); the app's own writes are suppressed so no hot reload is triggered; failed saves are non-fatal and retryable.
- **M5-RNF-555 [Maps RNF-003/RNF-002] No new idle work / bounded resources.** M5 adds no polling, watcher, or background loop. Any live refresh (sensor list on editor open, theme-default display) is event/creation-driven and bounded; the sensor list is capped by the already-fixed-size snapshot. Idle CPU stays within RNF-001 and memory within RNF-002.
- **M5-RNF-556 [Maps RNF-014/RF-081] Security posture.** The hotkey-reference action introduces no new execution surface: it executes only the referenced hotkey's already-validated action through the existing executor; references are plain data; no shell string, no elevation, no external resource. Configuration remains untrusted (RF-081).

## 7. Milestone scoping

M5 is a single milestone organized as five fix workstreams plus a guard/verification pass:

- **W1 — Delete affordance + confirmation** (`M5-RF-501..504`).
- **W2 — Sensor picker** (`M5-RF-510..513`).
- **W3 — Hotkey-reference action** (`M5-RF-520..524`).
- **W4 — Add-widget flow fix** (`M5-RF-530..534`).
- **W5 — Theme-aware defaults** (`M5-RF-540..544`).
- **W6 — Invariants, schema-v4 guard, docs, final verification** (`M5-RF-550/551`, `M5-RNF-551..556`).

## 8. Acceptance criteria

Automated (A), manual (M), or both.

- **AC-M5-01 (A) Build & tests.** The solution builds with the .NET 10 SDK; `dotnet test` passes with the new M5 tests; the full M1–M4 suite (1514) stays green. (`M5-RNF-551`)
- **AC-M5-02 (A) Delete affordance + confirm.** For a selected editable widget, the X affordance is present (top-right, ≥ 40 DIP, visible without hover, only in edit mode/selection); tapping it opens the confirmation; **Cancel** keeps the widget and the selection and changes no config/dirty state; **Delete** removes it from the live page and the `PageConfig` and marks dirty; `ExitAsync`/`SaveAsync` persists the removal through `IConfigStore`; edit-mode taps through the X never execute the bound action. Existing BAML/template tests are extended to assert the X and confirmation elements exist and are touch-sized. (`M5-RF-501..504`)
- **AC-M5-03 (A) Sensor picker.** For Gauge/Sparkline/Bar the sensor field is a list bound to the sensor catalog; with a fake snapshot the list includes per-core (`cpu.core.<n>`) and fan (`fan.<n>`) keys; with no snapshot it falls back to the fixed aggregate keys and is non-empty; an unknown configured sensor is preserved (not cleared) and selecting a known one writes it and passes validation; the sensor field remains a string key in JSON. (`M5-RF-510..513`)
- **AC-M5-04 (A) Hotkey-reference action.** A `hotkey` action serializes/deserializes in v4; the picker offers "Hotkey" and lists configured hotkeys; executing a button with a `hotkey` action resolves the referenced hotkey's action and dispatches it through a recording `IActionExecutor` (no OS call); editing the hotkey's action changes what the button runs; a now-missing reference (validator cross-check against `config.Hotkeys`) disables the widget with a visible reason and never drops it; a reference to a hotkey whose action is itself a `hotkey` action fails gracefully (no chaining); schema stays v4. (`M5-RF-520..524`)
- **AC-M5-05 (A) Add-widget end-to-end.** Starting from edit mode with the real registry/palette and an empty page: invoking the palette add appends the config + view model, the **page-host** `ItemsControl` item count increases immediately (not just the overlay), the new widget is selected with its property editor, the layout is dirty, and `ExitAsync` persists exactly one widget through the store; the Apply affordance invokes a real command and leaves the widget present; a build failure rolls back the config entry. (`M5-RF-530..534`)
- **AC-M5-06 (A) Theme-aware defaults.** The per-theme table exists for `dark`/`light`/`highContrast` with in-bounds values (dark reproduces the delivered palette); a widget added under each theme receives that theme's seeded appearance values; the editor shows the theme default for a null override rather than a blank/hard-coded constant and the editor chrome uses theme resources; precedence is override → global accent → theme; clearing an override restores live theme inheritance. (`M5-RF-540..544`)
- **AC-M5-07 (A) Schema & invariant guard.** `PanelConfig.CurrentSchemaVersion == 4`; no `V4ToV5`; the `hotkey` action round-trips in v4; `Actions/`, `Monitoring/`, `Config/`, `Input/` remain WPF-free; P/Invoke stays in `Interop/`; the focus/security invariants hold. (`M5-RF-524/550`, `M5-RNF-552/553/556`)
- **AC-M5-08 (M) Touch matrix.** On the real touchscreen: select a widget and delete it via the X + confirm (and cancel once); pick a sensor for a Gauge/Sparkline/Bar from the list (including a per-core and a fan key if present); add a Button that runs a created hotkey and verify it runs the hotkey's action; add a widget via the palette and confirm it appears and persists through restart; switch theme and confirm new widgets and the editor pick up sensible defaults. (`M5-RF-501..544`)
- **AC-M5-09 (M) Focus & no elevation.** With another app focused and being typed into, editing (including opening the X confirmation, the sensor list, and the hotkey picker) never interrupts foreground typing; only the deliberate edit-mode entry activates; no UAC prompt appears. (`M5-RNF-553`)

## 9. Failure cases and required behavior

| ID | Scenario | Required behavior | Refs |
|---|---|---|---|
| F-M5-01 | X confirmation open, user taps Cancel | Dismiss, keep widget + selection, no dirty/config change | M5-RF-502 |
| F-M5-02 | X tapped for a widget that is no longer selected/editable | Confirmation closes/cancels; nothing deleted | M5-RF-501/502 |
| F-M5-03 | No monitoring snapshot / backend degraded | Sensor list falls back to aggregate keys and is never empty | M5-RF-512 |
| F-M5-04 | Configured sensor absent from the catalog | Preserve and display it; never silently clear; RF-047 disable semantics at runtime | M5-RF-512 |
| F-M5-05 | Widget references a removed/duplicate hotkey id | Auto-disable with visible reason + log; resolve first id for duplicates; never drop | M5-RF-523 |
| F-M5-06 | Referenced hotkey's action is itself a hotkey reference | Fail gracefully (no chaining); no crash, no new execution surface | M5-RF-521 |
| F-M5-07 | Add rejected (not edit mode / no factory / unknown type / build failure) | No side effects, visible/diagnostic reason, no throw; config/page never diverge | M5-RF-530/534 |
| F-M5-08 | Save after add/delete fails or is canceled | Keep in-memory state, stay dirty, log + notify, retry; no partial write | M5-RF-503/533 |
| F-M5-09 | Unknown theme id / degraded appearance | Fall back to `dark` defaults; never produce an invalid config | M5-RF-540/543 |
| F-M5-10 | Pre-existing v4 widget with null appearance | Stay inherited (not rewritten); editor only displays theme defaults | M5-RF-542/543 |

Behavioral failure cases for the underlying features remain governed by the existing M1 `F-01..F-22` / M2 / M3 / M4 sets; M5 adds no new failure semantics beyond the table above.

## 10. Test strategy

Aligned with M1 §11 / M2 §10 / M3 §11 / M4 §11. Unit tests are the gate; hardware/network/elevation are excluded; temp directories isolate the profile; WPF-free layer purity is enforced; the only WPF-coupled tests use the existing real-BAML/STA instantiation pattern (never a shown window).

### 10.1 Unit tests (no hardware, no drivers, no elevation, no network)

- **Delete:** a `DeleteConfirmationViewModel`/service-state test (open → cancel keeps, confirm removes once, no action fired); an extended `EditOverlayTests`/real-BAML test asserting the X and confirm elements exist, are ≥ 40 DIP, and are visible only for the selected editable widget; a persistence test that the confirmed removal is written by `ExitAsync` through a recording store.
- **Sensor picker:** a `SensorCatalog` test (fixed aggregate keys always present; per-core/fan keys appear from a fake `SensorSnapshot`; empty/degraded snapshot still yields the fallback list); a `PropertyEditorTests` addition that `SensorOptions`/`SelectedSensor` set/validate/clamp and preserve an unknown configured key.
- **Hotkey action:** `ActionConfigJson` round-trip for the `hotkey` discriminator; a resolver test over fake hotkeys (resolves by id, first-wins duplicates, missing → disabled reason, no chaining); an `ActionExecutor` integration test with a `hotkey` handler and a recording executor that the resolved action is dispatched and nothing else executes; `ActionPickerTests` that the "Hotkey" option appears and lists the configured hotkeys with the empty-list fallback.
- **Add-widget:** `EditModeServiceTests` remains green; a new **page-host** test (real `WidgetPageHost`/`PageViewModel`) proves the bindings re-materialize on add (item count +1) and roll back on a forced factory failure; a property-panel Apply-command test proves no-op-safety and that the widget remains.
- **Theme defaults:** a `ThemeAppearanceDefaults` test (all three themes, in-bounds values, dark == delivered palette); a `WidgetRegistry`/`EditModeService` test that a widget added under each theme receives the seeded values; a `PropertyEditorAppearanceTests` addition that a null override displays the theme default and the editor chrome consumes theme resources; a precedence test (override → global accent → theme) and a clear-to-inherit test.

### 10.2 Integration tests (still no real hardware/network)

- `JsonConfigStore` against a temp directory: a v4 file with a `hotkey` action round-trips; schema stays v4; app-originated add/delete saves do not trigger a reload (self-write suppression).
- `SettingsCoordinator`/`PanelConfigApplier` unaffected: hotkey edits re-apply silently and never disturb widget references.

### 10.3 Manual verification matrix (executed in the M5 acceptance run)

- **MV-M5-01 Delete:** select → X → Cancel (kept) → X → Delete (removed, persisted after restart); no action fires while editing.
- **MV-M5-02 Sensor:** pick sensors for Gauge/Sparkline/Bar including a per-core and a fan key; verify fallback with monitoring unavailable.
- **MV-M5-03 Hotkey button:** add a Button, choose a created hotkey, verify the tap runs exactly the hotkey's action; remove the hotkey and verify the widget disables with a reason.
- **MV-M5-04 Add:** palette add appears immediately in the panel (not just the overlay) and survives restart.
- **MV-M5-05 Theme defaults:** add widgets under dark/light/highContrast and verify sensible defaults and live inheritance after clearing.
- **MV-M5-06 Focus/performance:** `RF-018` focus matrix with all M5 features present; idle CPU within RNF-001; no elevation.

### 10.4 Gates

- After every M5 task: `dotnet build` and `dotnet test` succeed; the full M1–M4 suite stays green.
- Manual MV-M5 checks pass or produce a documented, human-accepted limitation.
- A separate reviewer validates the implementation against this spec; reviewers do not modify code.

## 11. Integration with the existing architecture

| M5 area | Existing seam it extends | Required approach |
|---|---|---|
| Delete affordance + confirm | `EditOverlay.xaml(.cs)`, `EditModeService.RemoveSelectedWidget`, `PageViewModel.Remove` | Add an in-overlay X + inline confirm state on the service; reuse the existing remove + single writer; no new window. |
| Sensor picker | `SensorKeys`, `SensorSnapshot`, `SensorSelector`, `PropertyEditorViewModel`, `EditOverlay.xaml` | New WPF-free sensor-catalog seam; bind the editor's sensor field to a list; preserve unknown keys. |
| Hotkey-reference action | `ActionConfig`/`ActionTypes`, a new `IActionHandler`, `ActionExecutor`, `ConfigValidator`, `ActionPickerViewModel`, `PanelConfig.Hotkeys` | Additive `hotkey` discriminator + resolver seam; validator cross-check; picker lists hotkeys; no schema bump. |
| Add-widget flow | `EditModeService.AddWidget`, `PageViewModel`/`WidgetPageHost`, `EditOverlay.xaml(.cs)` | Make the collection notify, wire Apply, prove the host refreshes, keep the single-writer save. |
| Theme-aware defaults | `ThemeIds`, `WidgetAppearance`, `WidgetRegistry`/`WidgetRegistration`, `PropertyEditorViewModel`, `EditOverlay.xaml` | WPF-free per-theme table; seed new widgets at creation; theme-aware editor values; precedence unchanged. |
| Composition root | `App.xaml.cs` | Wire the sensor-catalog and hotkey-resolver seams into the edit service; capture the active theme for seeding; keep startup order/shutdown participation. |

**Threats to M1–M4 invariants and how M5 protects them:**

- **Focus preservation (`RF-018`):** all new affordances are in-panel; no new window; normal mode untouched.
- **Single writer / no polling (`RF-080`):** only `IConfigStore`/`StateStore` writes; no new watcher.
- **UI-thread discipline (`RNF-004`):** overlay/editor mutations stay on the dispatcher; the store call is awaited.
- **Layer purity (`RNF-010`):** the sensor catalog and theme-default table are WPF-free; P/Invoke untouched.
- **Untrusted configuration (`RF-081`) / no new execution surface:** the hotkey action executes only the referenced, already-validated action through the executor.
- **Bounded history (`RF-069`/`RNF-012`):** untouched.

## 12. Open questions (with recommended defaults)

| ID | Question | Recommended default |
|---|---|---|
| Q-M5-1 | When seeding theme defaults into a **new** widget, should color fields be seeded (pinning the widget to the creation theme) or only font-size/opacity, with colors shown as editor placeholders and left inherited? | Seed **font-size and opacity** (theme-independent and safe) plus **accent** where the widget uses it; leave **foreground/background inherited** so a later theme switch keeps working; show all per-theme color defaults as editor initial/placeholder values. This satisfies "no blanks/hard-coded values" while preserving live theming. |
| Q-M5-2 | When a hotkey referenced by a button is removed, should removal be blocked, or the widget auto-disabled? | **Auto-disable the widget via validation** with a visible reason (non-destructive), never block the user from removing the hotkey. |

Both have safe defaults and are **not blocking**; M5 proceeds with the recommendations above.
