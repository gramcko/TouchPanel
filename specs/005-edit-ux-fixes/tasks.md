# TouchPanel — Tasks 005: Edit-UX Fixes (M5)

| Field | Value |
|---|---|
| Tasks ID | 005-edit-ux-fixes |
| Status | Draft (M5 — specification + plan + tasks) |
| Date | 2026-10-10 |
| Inputs | `specs/005-edit-ux-fixes/{spec.md, plan.md}` |
| Execution rule | After every task: `dotnet build` and `dotnet test` must succeed (full M1–M4 suite green). No `RF-072`, no dropped features, no schema bump beyond v4. |
| Scope | Five edit-UX fixes: delete affordance, sensor picker, hotkey-reference action, add-widget flow, theme-aware defaults |

Task granularity is a **vertical slice** (a coherent feature/subsystem change), not one class per task. Each task lists objective, files/directories, dependencies, implementation notes, tests, acceptance criteria, and requirement IDs. Build/test command: `C:\Program Files (x86)\dotnet\dotnet.exe` (SDK 10.0.401).

## Overview

| ID | Title | Depends on | Requirements |
|---|---|---|---|
| T-01 | Fix add-widget flow: explicit Apply + immediate host/overlay refresh + single-writer persistence | — | M5-RF-530..534, M5-RF-541 (add side), M5-RNF-551/554, AC-M5-05 |
| T-02 | Delete-from-edit affordance ("X") + "Are you sure?" confirmation | T-01 | M5-RF-501..504, M5-RNF-553/554, AC-M5-02 |
| T-03 | Sensor picker from a WPF-free catalog (Gauge/Sparkline/Bar) | T-01 | M5-RF-510..513, M5-RNF-551/552/555, AC-M5-03 |
| T-04 | Hotkey-reference action (new `hotkey` shape, resolver, picker, validation) | T-01 | M5-RF-520..524, M5-RNF-552/556, AC-M5-04 |
| T-05 | Theme-aware defaults for new widgets + editor | T-01 | M5-RF-540..544, M5-RNF-552/555, AC-M5-06 |
| T-06 | Invariants + schema-v4 guard + docs consistency (delete/focus/layering/security) | T-02..T-05 | M5-RF-504/524/533/550/551, M5-RNF-552..556, AC-M5-07 |
| T-07 | Final M5 validation + touch matrix + coverage statement | T-01..T-06 | M5-RF-550/551, M5-RNF-551..556, AC-M5-01..09 |

---

## T-01 — Fix add-widget flow

**Phase:** fix (foundation).

**Objective.** Make adding a widget from the palette appear immediately in the panel **and** persist through the single-writer store: reproduce the defect end-to-end (page host included), wire the property panel Apply to an explicit command, and make the widget collection refresh reliably without a full page rebuild.

**Files/directories.**
- `TouchPanel/ViewModels/EditModeService.cs` — confirm/fix `AddWidget` rollback and dirty tracking; expose the add as a command if needed.
- `TouchPanel/ViewModels/PageViewModel.cs` — make `Widgets` a notifying collection (`ObservableCollection<WidgetViewModelBase>` or equivalent) **or** ensure a collection-change notification fires so `ItemsControl` re-materializes.
- `TouchPanel/Views/WidgetPageHost.xaml` — binding path only if the collection change requires it.
- `TouchPanel/Views/EditOverlay.xaml(.cs)` — wire `PropertyApplyButton` to an idempotent Apply command.
- `TouchPanel.Tests/` — new page-host refresh tests + property-panel Apply test; keep `EditModeServiceTests`/`EditOverlayTests`/`PageViewModelTests` green.

**Dependencies:** none.

**Implementation notes.** Per spec §5.4: validate edit-mode + palette + factory + known type; on success append config + view model, select, open the editor, mark dirty; on view-model build failure remove the config entry (no divergence). Prefer the minimal refresh change (notifying collection) over a rebuild; do not dispose/recreate existing widget view models. Confirm/refine **H-M5-4** by reproducing at the host level first and record the confirmed cause in the task notes.

**Tests.**
- Page-host test: with the real `WidgetPageHost` + `PageViewModel` + real palette, `AddWidget` raises the host item count by one immediately; a forced factory throw leaves the config/page unchanged.
- Apply test: invoking the property-panel Apply command commits the focused field, keeps the widget present, and is idempotent.
- Persistence: `ExitAsync` after an add writes exactly one widget through a recording store.
- Regression: existing `EditModeServiceTests`, `EditOverlayTests`, `PageViewModelTests` stay green.

**Acceptance criteria.** AC-M5-05 passes; no full page reload/flicker; no subscription leak; single-writer persistence honoured.

**Requirements:** M5-RF-530/531/532/533/534, M5-RF-541 (add side), M5-RNF-551, M5-RNF-554.

---

## T-02 — Delete affordance + confirmation

**Phase:** fix (edit UX).

**Objective.** Add a touch-first "X" to the selected widget in edit mode and an "Are you sure?" confirmation, with cancel semantics, reusing the existing remove + single-writer persistence.

**Files/directories.**
- `TouchPanel/Views/EditOverlay.xaml(.cs)` — X button in the widget item template (top-right, ≥ 40 DIP, `Focusable=False`, visible only for the selected editable widget); inline confirmation surface with Delete/Cancel.
- `TouchPanel/ViewModels/EditModeService.cs` — confirmation state (`IsDeleteConfirmationVisible`, `RequestRemoveSelectedWidget`, `ConfirmRemove`, `CancelRemove`) over the existing `RemoveSelectedWidget`/`CanModifySelection`.
- `TouchPanel.Tests/Views/EditOverlayTests.cs` — real-BAML assertions for the X and confirm affordances.
- `TouchPanel.Tests/ViewModels/EditModeServiceTests.cs` — confirm/cancel state machine + persistence.

**Dependencies:** T-01.

**Implementation notes.** No `MessageBox`/new window (no focus steal, RF-018). Confirming routes to `RemoveSelectedWidget` (dirty + save on exit). Cancel changes nothing. The X never fires the bound action (edit mode owns taps, `M2-RF-439`). Handle "confirmation open then selection changes" by cancelling if the target is no longer selected/editable.

**Tests.**
- State machine: request opens; cancel keeps widget + selection and leaves `IsDirty` unchanged; confirm removes once from both the live page and the `PageConfig` and marks dirty.
- Persistence: `ExitAsync` after a confirmed delete writes the removal through a recording store.
- Real-BAML: X present for a selected editable widget (≥ 40 DIP, visible without hover), absent outside selection/edit mode; confirm surface present on X.
- Tap safety: triggering the X does not execute the widget's action.

**Acceptance criteria.** AC-M5-02 passes; no focus steal; no action fired while editing.

**Requirements:** M5-RF-501/502/503/504, M5-RNF-553, M5-RNF-554.

---

## T-03 — Sensor picker from a WPF-free catalog

**Phase:** fix (edit UX).

**Objective.** Replace free-text sensor entry for Gauge/Sparkline/Bar with a selectable list of available sensor keys, including per-core CPU and fans when available, with a fixed fallback.

**Files/directories.**
- `TouchPanel/Monitoring/` (or `TouchPanel/Config/`) — new `ISensorCatalog` + a default implementation over `SensorKeys`/`SensorSnapshot` (aggregate keys + `cpu.core.<n>`/`fan.<n>` from the snapshot; fixed fallback when no snapshot). A `SensorOption` record (key, display name) in the WPF-free layer.
- `TouchPanel/ViewModels/Edit/PropertyEditorViewModel.cs` — `SensorOptions`/`SelectedSensor`; keep `SensorText` semantics for unknown keys.
- `TouchPanel/Views/EditOverlay.xaml` — replace the sensor `TextBox` with a `ComboBox` gated by `HasSensor`.
- `TouchPanel/App.xaml.cs` — wire the catalog into `EditModeService`/`PropertyEditorViewModel`.
- `TouchPanel.Tests/` — catalog + property-editor tests.

**Dependencies:** T-01.

**Implementation notes.** The catalog is WPF-free and testable with a fake snapshot. The list must include the currently configured sensor even if unknown (never silently clear). Selecting a key writes the string with the existing validation. Keep the field a string key in JSON. Live snapshot refresh is optional; if added, never move the user's selection and stay bounded.

**Tests.**
- Catalog: with a fake snapshot, per-core/fan keys appear; with no/degraded snapshot, the fixed aggregate list is returned and is non-empty.
- Editor: `SensorOptions` non-empty; selecting a known key writes it; an unknown configured key is preserved and displayed; Gauge/Sparkline/Bar expose the picker, other types do not.
- JSON: the sensor remains a string key (round-trip unchanged).

**Acceptance criteria.** AC-M5-03 passes; no hardware required; layer-purity green.

**Requirements:** M5-RF-510/511/512/513, M5-RNF-551, M5-RNF-552, M5-RNF-555.

---

## T-04 — Hotkey-reference action

**Phase:** fix/feature (actions).

**Objective.** Let a Button (and other action-bearing widgets) bind an action that references a configured global hotkey and runs **that hotkey's action** through the existing executor, without duplicating or insecurely re-expressing it.

**Files/directories.**
- `TouchPanel/Config/ActionConfig.cs` — `ActionTypes.Hotkey = "hotkey"` + `RunHotkeyActionConfig { string HotkeyId }`.
- `TouchPanel/Config/Json/ActionConfigJsonConverter.cs` — register the new discriminator (if the converter is a switch).
- `TouchPanel/Actions/` — `IHotkeyActionResolver` seam + `RunHotkeyHandler : IActionHandler` that resolves the referenced `HotkeyConfig.Action` and dispatches it through `IActionExecutor` (no chaining).
- `TouchPanel/Config/Validation/ConfigValidator.cs` — cross-check each widget's `hotkey` reference against `config.Hotkeys`; disable with a reason when missing/blank; first-wins for duplicate ids.
- `TouchPanel/ViewModels/Edit/ActionPickerViewModel.cs` — add the "Hotkey" option + a list of the current hotkeys; empty-list state.
- `TouchPanel/Views/EditOverlay.xaml` — Hotkey section (selector + hotkey list).
- `TouchPanel/App.xaml.cs` — wire the resolver over `PanelConfig.Hotkeys` into the executor/picker.
- `TouchPanel.Tests/` — JSON round-trip, resolver, validator, executor, picker tests.

**Dependencies:** T-01.

**Implementation notes.** Per spec §5.3/DD-M5-3: the action carries only the hotkey id; execution resolves the live hotkey action (so edits/removals apply without touching the widget); the handler reuses `IActionExecutor`; a `hotkey` action referring to another `hotkey` action fails gracefully (no recursion). **No schema bump** (`M5-RF-524`): the discriminator is additive and the unknown-action seam preserves it on older builds.

**Tests.**
- JSON: a `hotkey` action round-trips in v4; schema stays 4.
- Resolver/handler: resolves by id; first-wins duplicates; missing/blank → disabled reason; no chaining; the resolved action is dispatched through a recording `IActionExecutor` and nothing else.
- Validator: a widget referencing a non-existent hotkey is disabled with a reason and not dropped.
- Picker: "Hotkey" appears in the catalog; the hotkey list reflects `config.Hotkeys`; empty list shows a clear state.

**Acceptance criteria.** AC-M5-04 passes; no new execution surface; schema stays v4.

**Requirements:** M5-RF-520/521/522/523/524, M5-RNF-552, M5-RNF-556.

---

## T-05 — Theme-aware defaults for new widgets and the editor

**Phase:** fix (appearance).

**Objective.** Give new widgets and the editor sensible per-theme default color/font-size/opacity values instead of blanks or hard-coded ones, preserving the per-widget-override → global-accent → theme precedence.

**Files/directories.**
- `TouchPanel/Theme/` — new WPF-free `ThemeAppearanceDefaults` keyed by `ThemeIds` (foreground/background/accent/font-size/opacity; dark reproduces the delivered palette).
- `TouchPanel/Config/WidgetAppearance.cs` — reuse bounds/normalization (no behavior change beyond any needed helper).
- `TouchPanel/Widgets/WidgetRegistry.cs` / `WidgetRegistration` — feed the active theme's defaults into `createDefaultConfig` or an add-time seeding step.
- `TouchPanel/ViewModels/EditModeService.cs` — seed new widgets from the active theme (per Q-M5-1: font-size + opacity, plus accent where used).
- `TouchPanel/ViewModels/Edit/PropertyEditorViewModel.cs` — theme-aware initial/placeholder values for null overrides; no hard-coded source of truth.
- `TouchPanel/Views/EditOverlay.xaml` — editor chrome consumes theme resources (`DynamicResource`); remove hard-coded color/font as the source of truth.
- `TouchPanel/App.xaml.cs` — capture the active theme id for seeding.
- `TouchPanel.Tests/` — defaults table, seeding, editor display, precedence tests.

**Dependencies:** T-01.

**Implementation notes.** Per spec §5.5/DD-M5-5 and Q-M5-1: seed **font-size and opacity** (theme-independent) plus **accent** where the widget uses it; keep **foreground/background inherited** so a later theme switch keeps working; the editor shows per-theme color defaults for null fields. Seeded values must be normalized/validated. Pre-existing widgets with null appearance must **not** be rewritten on load. A live theme switch updates inherited fields (in-progress explicit edits are preserved).

**Tests.**
- `ThemeAppearanceDefaults`: values exist for dark/light/highContrast, are within `WidgetAppearance` bounds, and dark reproduces the delivered palette.
- Seeding: a widget added under each theme receives the expected seeded values; invalid theme-default data never yields an invalid config.
- Editor: a null override displays the theme default (not blank/hard-coded); editor chrome styles come from theme resources; precedence override → global accent → theme; clearing an override restores live inheritance.
- Regression: M4 appearance/theme tests (incl. real-BAML) stay green.

**Acceptance criteria.** AC-M5-06 passes; precedence preserved; no layer-purity regression.

**Requirements:** M5-RF-540/541/542/543/544, M5-RNF-552, M5-RNF-555.

---

## T-06 — Invariants, schema-v4 guard, and docs consistency

**Phase:** guard (tests + docs).

**Objective.** Lock the M5 non-negotiables and keep the documentation consistent: schema stays v4, focus/security/layering invariants hold, and the M5 fixes are recorded.

**Files/directories.**
- `TouchPanel.Tests/Config/SchemaV4GuardTests.cs` — assert `CurrentSchemaVersion == 4`, the migration chain ends at v4, no `V4ToV5`, and a `hotkey`-action file round-trips in v4.
- `TouchPanel.Tests/Architecture/LayerPurityTests.cs` — keep/extend for the new sensor-catalog and theme-default files (WPF-free; P/Invoke only in `Interop/`).
- `TouchPanel.Tests/` — focus/security assertions: no M5 affordance calls `Activate`/`SetForegroundWindow`; the hotkey action executes only through the executor.
- `specs/005-edit-ux-fixes/spec.md`/`plan.md` — already record the decisions; this task confirms them against the built code.

**Dependencies:** T-02, T-03, T-04, T-05.

**Implementation notes.** Prefer assertions over `*.g.cs` internals. This is a guard, not a redesign; if a duplicate/competing affordance is found, fix it minimally or document it.

**Tests.**
- Schema guard: v4 current; chain terminates at v4; no `V4ToV5`; `hotkey` action round-trips.
- Layering: `Actions/`/`Monitoring/`/`Config/`/`Input/` WPF-free; `ThemeAppearanceDefaults` and the sensor catalog WPF-free; P/Invoke confined to `Interop/`.
- Focus/security: no M5 feature activates the panel or prompts for elevation; hotkey action has no second execution path.
- Single writer: add/delete saves do not trigger a reload (self-write suppression) and only `IConfigStore` writes `panel.json`.

**Acceptance criteria.** AC-M5-07 passes; invariants documented.

**Requirements:** M5-RF-504/524/533/550/551, M5-RNF-552/553/554/555/556.

---

## T-07 — Final M5 validation + touch matrix + coverage

**Phase:** acceptance (final).

**Objective.** Run the full suite and the manual matrix with all five fixes present, confirm no M1–M4 invariant regressions, and record the M5 coverage.

**Files/directories.**
- All `TouchPanel.Tests/` (full suite).
- `specs/005-edit-ux-fixes/tasks.md` — final coverage statement.

**Dependencies:** T-01..T-06.

**Implementation notes.** Execute AC-M5-01..09. Run MV-M5-01..06 on the touchscreen. Confirm the expected add flow (spec `M5-RF-530`) and the delete/confirm flow. Verify schema stays v4 and no dropped/frozen requirement was revived.

**Tests.** Full suite green (1514 + new M5 tests). Manual MV-M5 checks pass or a documented, human-accepted limitation is recorded.

**Acceptance criteria.** AC-M5-01..09 pass; coverage statement complete; reviewer accepts without modifying code.

**Requirements:** M5-RF-550/551, M5-RNF-551..556.

---

## Coverage statement

All **25** `M5-RF-###` and **6** `M5-RNF-###` requirements are mapped to at least one task (see `plan.md` §8 and the overview). The five fixes are: T-01 add-widget flow, T-02 delete affordance, T-03 sensor picker, T-04 hotkey-reference action, T-05 theme-aware defaults; T-06 guards invariants and the v4 schema; T-07 is the final validation + touch matrix + coverage. **`RF-072` is excluded**, no dropped feature is implemented, and the schema stays **v4** (the `hotkey` action is an additive discriminator preserved by the existing unknown-action seam). No new execution surface, no elevation, no focus steal.
