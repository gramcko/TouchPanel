# TouchPanel — Tasks 004: Promotion & Polish (M4)

| Field | Value |
|---|---|
| Tasks ID | 004-promotion |
| Status | Draft for human approval |
| Date | 2026-10-10 |
| Inputs | `specs/004-promotion/{spec.md, plan.md}` |
| Execution rule | After every task: `dotnet build` and `dotnet test` must succeed (full M1+M2+M3 suite green). No `RF-072`, no dropped features, no M5, no schema bump beyond v4. |
| Scope | Promote the implemented M2 backlog + resolve the 3 M3 findings + cohesion/polish |

Task granularity is a **vertical slice** (a coherent feature/subsystem), not one class per task. Each task lists objective, files/dirs, dependencies, implementation notes, tests, acceptance criteria, and requirement IDs. Verification-only tasks (T-01, T-07, T-08, T-09, T-10) change no product behavior; code tasks are T-02..T-06.

## Overview

| ID | Title | Depends on | Requirements |
|---|---|---|---|
| T-01 | Promotion verification baseline (map tests↔ACs; regression; coverage gaps) | — | M4-RF-401/430/431/440/450/600/700/710/820/830/910/920/930/960/964, M4-RNF-141 |
| T-02 | Sparkline filled area follows theme/accent (translucent, fallback) | T-01 | M4-RF-950, M4-RF-963, M4-RNF-142, AC-M4-03 |
| T-03 | Shared Clock validation/display culture source | T-01 | M4-RF-951, M4-RNF-142, AC-M4-04 |
| T-04 | Extract `AppearanceConfig` to its own file | T-01 | M4-RF-952, M4-RNF-142, AC-M4-05 |
| T-05 | Property editor extended to Clock/ImageButton/Bar + Slider reachability | T-01 | M4-RF-402/431/432, M4-RNF-142/145, AC-M4-06 |
| T-06 | UI surface-map cohesion + ownership tests + automatic-behavior docs | T-01, T-05 | M4-RF-433/601/921/960/964, M4-RNF-146, AC-M4-08 |
| T-07 | Editing/grid/toggle/local-shortcut verification | T-01 | M4-RF-401/430/431/433/440/450/451/600, AC-M4-02/07/10 |
| T-08 | Config/UI/runtime verification (reload, import/export, page switch, settings, monitor, interval, backend re-init) | T-01 | M4-RF-451/700/710/711/820/821/830/831/910/911/920/930/931, M4-RNF-143/144/145/146, AC-M4-09/10 |
| T-09 | Schema-stays-v4 + grid/snap session-only guard | T-01 | M4-RF-441/961, AC-M4-13 |
| T-10 | Final M4 validation + supported declaration + docs consistency | T-02..T-09 | M4-RF-601/962/963/964, M4-RNF-141..146, AC-M4-01..13 |

---

## T-01 — Promotion verification baseline

**Phase:** verification (first; no product code).

**Objective.** Establish, before any change, that every implemented-but-frozen feature passes its existing M2/M3 tests, and map each promoted base to its sub-requirements, test classes, and acceptance criteria. Produce the coverage table consumed by T-07/T-08 and flag any genuine gaps.

**Files/directories.**
- `specs/004-promotion/` — the coverage/verification table (added to this tasks file or the plan as an appendix).
- `TouchPanel.Tests/` — executed, not modified (modification only if a gap is real and is then scheduled into T-07/T-08).
- Reference only: `TouchPanel/ViewModels/EditModeService.cs`, `PropertyEditorViewModel.cs`, `ActionPickerViewModel.cs`, `GridSnap.cs`, `AlignmentGuides.cs`, `Search` … (read to confirm the feature is reachable).

**Dependencies:** none.

**Implementation notes.** Run `dotnet build` and `dotnet test` (`C:\Program Files (x86)\dotnet\dotnet.exe`). For each promoted base, tabulate: base → M2 sub-requirements → existing test class(es) → M4 AC. Confirm each feature is reachable from its canonical surface (`M4-RF-960`). Classify any feature with a coverage suspicion as a gap and route it to T-07/T-08 (verification) or T-05 (code) — do not silently accept it.

**Tests.** None new; execute the full suite and record the mapping. Expected: 1480 tests green.

**Acceptance criteria.** The coverage table has a row for every promoted base with a named test class and AC; no promoted feature is left unmapped; any gap is explicitly scheduled.

**Requirements:** M4-RF-401/430/431/440/450/600/700/710/820/830/910/920/930/960/964, M4-RNF-141.

---

## T-02 — Sparkline filled area follows theme/accent

**Phase:** gap fix (code).

**Objective.** Remove the hard-coded `#553D7EFF` fill and derive the Sparkline "filled" area from the effective accent (per-widget accent override → global accent → theme accent) at a documented translucent alpha, following live theme/accent swaps on realized elements.

**Files/directories.**
- `TouchPanel/Widgets/SparklineWidget/SparklineWidgetTemplates.xaml` — replace `FillBrush="{StaticResource SparklineFillBrush}"` with the dynamic accent-fill binding; keep `Tag="{DynamicResource PanelAccentBrush}"` as the theme fallback.
- `TouchPanel/ViewModels/AppearanceForegroundConverter.cs` and/or a new small fill converter (e.g. `AppearanceAccentFillConverter`) in `ViewModels/` — resolve the effective accent and apply the translucent alpha (default `0x55`).
- `TouchPanel/ViewModels/SparklineWidgetViewModel.cs` — expose/confirm the `AccentBrush` override used by the binding (already present).
- `TouchPanel.Tests/` — converter unit tests + a real-BAML test (extend `SparklineTemplateTests`/`ThemeResourceRefactorTests`).

**Dependencies:** T-01.

**Implementation notes.** Precedence must mirror the line: per-widget accent override wins; otherwise the global/theme accent (`PanelAccentBrush`, `DynamicResource`) applies and follows a live swap. The converter must not break the `M3-RF-851` dynamic layer. The alpha is a documented constant (default `0x55`). No code-behind, no animation.

**Tests.**
- Converter unit test: override present → override color at alpha; override absent → fallback accent at alpha; fallback unavailable → theme accent; never throws on invalid input.
- Real-BAML (STA): load the real `SparklineWidgetTemplates.xaml`, realize an override-less and an overridden Sparkline; swap the active theme/accent at runtime; assert the override-less fill updates, the overridden widget keeps its override, and the previous state is restored on an injected failure.
- Source guard: no hard-coded `#553D7EFF` literal remains in the template.

**Acceptance criteria.** AC-M4-03 passes; the filled area tracks theme/accent; per-widget override wins; live swap reaches realized elements.

**Requirements:** M4-RF-950, M4-RF-963, M4-RNF-142.

---

## T-03 — Shared Clock validation/display culture source

**Phase:** gap fix (code).

**Objective.** Make load-time Clock format validation use the same pinned display culture as `ClockWidgetViewModel`, eliminating the `InvariantCulture`/display-culture divergence.

**Files/directories.**
- `TouchPanel/Services/Time/` — a WPF-free culture source (e.g. `ClockCulture` holder / `IClockCultureSource`), default = the user's current culture captured once.
- `TouchPanel/Config/Validation/ConfigValidator.cs` — `ValidateClock` uses the supplied culture (`ClockFormat.TryCustomFormat(format, culture)`) instead of `CultureInfo.InvariantCulture`; thread the culture through `Validate` (overload/parameter) with a sensible default for existing callers.
- `TouchPanel/ViewModels/ClockWidgetViewModel.cs` — take the same culture source/instance (already has a pinned `_culture`); ensure the same value is used for effective-format resolution.
- `App.xaml.cs` — capture the culture once and pass it to both the validator call path and the Clock VM.
- `TouchPanel.Tests/` — `ClockFormatTests`/`ClockWidgetTests`/`ConfigValidatorTests` additions with a pinned culture.

**Dependencies:** T-01.

**Implementation notes.** Keep validation pure and WPF-free; the culture is injected, not read statically at validation time. Preserve `M3-RF-462/463` behavior (length bounds, safe trial format, escaped-literal-aware seconds detection). No change to the Clock's refresh cadence or OS-event seam.

**Tests.**
- A custom format valid in the pinned culture is accepted by **both** `ConfigValidator.ValidateClock` and `ClockWidgetViewModel`; a format invalid in that culture is rejected by **both**.
- Assert both consume the same source/instance (e.g. an injected culture that differs from the machine default and from invariant).
- Existing Clock tests (cadence, mode presets, fallback) stay green.

**Acceptance criteria.** AC-M4-04 passes; no validator/VM divergence; culture source is injectable and WPF-free.

**Requirements:** M4-RF-951, M4-RNF-142.

---

## T-04 — Extract `AppearanceConfig` to its own file

**Phase:** gap fix (code, mechanical).

**Objective.** Move the `AppearanceConfig` type out of `Config/PanelConfig.cs` into `Config/AppearanceConfig.cs` with no behavior change.

**Files/directories.**
- `TouchPanel/Config/PanelConfig.cs` — remove the `AppearanceConfig` declaration.
- `TouchPanel/Config/AppearanceConfig.cs` — new file holding the moved type (same namespace `TouchPanel.Config`, same public shape and XML docs; keep `ThemeIds`/`IconPackIds` defaults).
- `TouchPanel.Tests/` — `AppearanceConfigTests`, `MigrationV4Tests`, `ConfigJson*` remain green (no changes expected).

**Dependencies:** T-01.

**Implementation notes.** Pure organization; no member renames, no default changes, no serializer attribute changes. Ensure `PanelConfig.Appearance` still resolves. This is the M3 finding (c).

**Tests.**
- `ConfigJson` round-trip of `appearance` unchanged.
- `v3→v4` migration and missing/invalid appearance fallbacks unchanged.
- Build proves the type resolves from the new file.

**Acceptance criteria.** AC-M4-05 passes; `AppearanceConfig` is declared in `Config/AppearanceConfig.cs` and nowhere else; all pre-existing tests green.

**Requirements:** M4-RF-952, M4-RNF-142.

---

## T-05 — Property editor extended to Clock, ImageButton, and Bar (+ Slider reachability)

**Phase:** gap fix (code).

**Objective.** Reverse the M3 `M3-RF-468`/`D-M3-8` JSON-only boundary: make the type-specific fields of Clock, ImageButton, and Bar editable in the existing property editor, and confirm Slider's type-specific fields are reachable there.

**Files/directories.**
- `TouchPanel/ViewModels/Edit/PropertyEditorViewModel.cs` — add field projections/commands and `Has*` flags for Clock (`mode`, `format`), ImageButton (`showLabel`, icon), Bar (`sensor`, `minimum`, `maximum`, `showValue`); wire them into the existing `Read*`/apply `switch`; ImageButton gains an `ActionPickerViewModel` like Button/Toggle.
- `TouchPanel/Views/EditOverlay.xaml` — add the new type-specific editor rows, visibility-gated by the `Has*` flags, touch-first (≥ 40 DIP), matching existing sections.
- `TouchPanel/ViewModels/Edit/ActionPickerViewModel.cs` — confirm ImageButton action editing works through it (no new picker).
- `TouchPanel/Config/IconCatalog.cs` / `IconPackCatalog.cs` — ImageButton icon selection restricted to built-in icons/active pack.
- `TouchPanel.Tests/` — `PropertyEditorTests`/`ActionPickerTests` additions.

**Dependencies:** T-01.

**Implementation notes.** Reuse the existing typed-field + validation pattern; every input is validated with the **same rules the `ConfigValidator` uses** (invalid blocked/clamped with visible feedback, never written to the in-memory config). Do **not** add a parallel validator. ImageButton never accepts an external image path (`RF-081`); if no icon is set it keeps the current neutral default. No new persistence path (dirty-tracked via `EditModeService`). Keep `EditModeService`/host untouched (proves the promotion, not a redesign).

**Tests.**
- Clock `mode`/`format`, ImageButton `showLabel`/icon, Bar `sensor`/`minimum`/`maximum`/`showValue` are settable, validated, and round-trip through JSON.
- Invalid values are blocked/clamped; an unknown Bar sensor disables with a reason; ImageButton with no/invalid action stays disabled.
- ImageButton icon restricted to the built-in catalog/active pack (hostile/id path rejected).
- Slider `minimum`/`maximum`/`step`/`orientation`/initial value remain editable; palette lists Slider and all built-in types.

**Acceptance criteria.** AC-M4-06 passes (and the Slider reachability part of AC-M4-07); no host changes required; `LayerPurityTests` green.

**Requirements:** M4-RF-402/431/432, M4-RNF-142/145.

---

## T-06 — UI surface-map cohesion + ownership tests + automatic-behavior documentation

**Phase:** cohesion (tests + minimal docs).

**Objective.** Prove there are no duplicate/competing editors: the settings window hosts behavior/startup/interval/monitor/import-export/diagnostics; the hotkeys/appearance editor hosts hotkeys + appearance; the edit overlay hosts widget editing; the page switcher hosts page selection. Document that automatic/runtime behaviors require no user surface.

**Files/directories.**
- `TouchPanel.Tests/` — ownership tests (e.g. `SettingsWindowTests`/`HotkeysEditorWindowTests` additions or a new `SurfaceOwnershipTests`) asserting the settings window has no hotkey/appearance controls and the hotkeys editor has no startup/monitor/interval/import-export controls (via XAML/`FindName`/logical-tree assertions or a source check).
- `specs/004-promotion/spec.md`/`plan.md` — the surface map is already recorded; this task confirms it against the built views and records the automatic-behavior list (RF-060/070/082 + the runtime side of RF-045).
- Reference: `Views/Settings/SettingsWindow.xaml`, `Views/Hotkeys/HotkeysEditorWindow.xaml`, `Views/EditOverlay.xaml`, `Views/PageSwitcher/PageSwitcherView.xaml`, `Services/Tray/TrayMenu.cs`.

**Dependencies:** T-01, T-05.

**Implementation notes.** Prefer assertions over view-model/**.g.cs** internals so they stay stable. This is not a redesign; if an unexpected duplicate surface is found, remove/disable it minimally or document it, within M4 scope.

**Tests.**
- Settings window exposes no `hotkeys`/`appearance` editing; hotkeys editor exposes no startup/monitor/interval/import-export.
- Tray menu contains exactly Show/Hide, Edit Mode, Settings…, Hotkeys…, Open Config Folder, Exit.
- Edit-overlay actions do not fire while editing (existing `M2-RF-439`, re-asserted).

**Acceptance criteria.** AC-M4-08 passes; one surface per concern; automatic behaviors documented.

**Requirements:** M4-RF-433/601/921/960/964, M4-RNF-146.

---

## T-07 — Editing / grid / toggle / local-shortcut verification

**Phase:** verification (no product change unless T-01 surfaced a gap).

**Objective.** Verify the editing, grid/alignment, toggle-persistence, and local-shortcut promoted features against their M2 sub-requirements.

**Files/directories.**
- `TouchPanel.Tests/` — execute `EditModeServiceTests`, `EditOverlayTests`, `PropertyEditorTests`, `ActionPickerTests`, `GridSnapTests`, `AlignmentGuidesTests`, `EditSnapTests`, `ToggleStateTests`, `StateStoreTests`, `LocalShortcutsTests`; add tests only for a surfaced gap.

**Dependencies:** T-01.

**Implementation notes.** Confirm: palette add/remove/duplicate + unknown-type preservation; property editor + action picker/browse; persistence/single-writer/touch/isolation; grid/alignment composition and snap-aware nudge; toggle `persistState` shape (`state.json` `toggleStates` keyed `<pageId>/<widgetId>`) and no reload loop; fixed edit-mode local shortcuts separated from global registration and inert outside context. Grid/snap session-only is asserted in T-09.

**Tests.** The classes above; plus, if needed, a targeted test closing any T-01 gap. Assert toggle-flip writes only `state.json` and triggers no reload (`AppWriteSuppressionTests`).

**Acceptance criteria.** AC-M4-02 (editing/grid/toggle/shortcut portion), AC-M4-07 (edit UI/Slider/grid on touch, manual), AC-M4-10 (toggle persistence portion) hold.

**Requirements:** M4-RF-401/430/431/433/440/450/451/600.

---

## T-08 — Configuration/UI/runtime promoted-behavior verification

**Phase:** verification (no product change unless T-01 surfaced a gap).

**Objective.** Verify hot reload, import/export, page switching, settings window, monitor picker, configurable interval, and backend re-init against their M2 sub-requirements.

**Files/directories.**
- `TouchPanel.Tests/` — execute `ConfigReloadControllerTests`, `ConfigReloadPipelineTests`, `ConfigReloadPipelineIntegrationTests`, `AppWriteSuppressionTests`, `ReloadDecisionTests`, `ImportExportTests`, `PageSwitcherViewModelTests`, `PageSwitcherViewTests`, `PageSwitchCoordinatorTests`, `PageSwitchIntegrationTests`, `SettingsWindowTests`, `SettingsViewModelTests`, `SettingsCoordinatorTests`, `MonitorPickerViewModelTests`, `MonitorSelectionServiceTests`, `IntervalTests`, `BackendReinitTests`, `ThrottledFailureLoggerTests`; add tests only for a surfaced gap.

**Dependencies:** T-01.

**Implementation notes.** Confirm: watcher filtered to `panel.json` + self-write suppression + validate-then-apply + future-schema untouched + no polling; export includes `schemaVersion`, import validate+backup+apply, paths/summaries only; page-switch persistence/fallback/rebuild/isolation and self-save-no-reload; settings scope + atomic save + live apply + activation; monitor listing/selection/current-unavailable/deterministic matching; interval bounds/clamp/live-apply/capacity recompute; backend re-init throttled recovery + subsystem restore.

**Tests.** The classes above; plus, if needed, a targeted test closing any T-01 gap.

**Acceptance criteria.** AC-M4-09 and AC-M4-10 pass; no idle work introduced (`M4-RNF-143`); resource bounds unchanged (`M4-RNF-144`); no focus steal (`M4-RNF-146`).

**Requirements:** M4-RF-451/700/710/711/820/821/830/831/910/911/920/930/931, M4-RNF-143/144/145/146.

---

## T-09 — Schema-stays-v4 + grid/snap session-only guard

**Phase:** guard (tests + documentation).

**Objective.** Lock the M4 schema decision: `PanelConfig.CurrentSchemaVersion` stays **4** with no `V4ToV5`, and grid size/snap remain session-only (no schema field).

**Files/directories.**
- `TouchPanel.Tests/` — e.g. `MigrationTests`/`DefaultConfigGeneratorTests` additions: assert `CurrentSchemaVersion == 4`, `ConfigMigrationChain.Create()` ends at `To == 4`, and no `V4ToV5` type exists; assert the serialized `panel.json` has no grid-size field.
- `TouchPanel/ViewModels/Edit/EditModeService.cs` — reference only (grid/snap are session-only); no change expected.

**Dependencies:** T-01.

**Implementation notes.** The M4 spec annotations (002 `M2-RF-441` note, `M4-RF-441`, `D-M4-3`) document the deliberate non-persistence; this task enforces it.

**Tests.**
- Schema guard: current version 4; chain terminates at v4; no `V4ToV5`.
- Grid/snap default (`8`/snap-on) after a simulated restart; not persisted.
- All four `RF-075` load states remain covered (existing tests).

**Acceptance criteria.** AC-M4-13 (schema/grid portion) passes.

**Requirements:** M4-RF-441/961.

---

## T-10 — Final M4 validation + supported declaration + documentation consistency

**Phase:** acceptance (final).

**Objective.** Run the full suite and manual matrix with every promoted feature present, confirm no invariant regressions, and verify the freeze documentation/supported declaration is consistent.

**Files/directories.**
- All `TouchPanel.Tests/` (full suite).
- `specs/001-foundation/spec.md`, `specs/002-automation/spec.md`, `specs/003-customization/spec.md` — re-verify tags/`D-25` consistency (no drift); no functional edits unless a discrepancy is found.

**Dependencies:** T-02..T-09.

**Implementation notes.** Execute AC-M4-01..13. Run the manual matrix: MV-M4-01 edit UI/Slider/grid; MV-M4-02 settings/editor cohesion; MV-M4-03 hot reload/import-export/page switching; MV-M4-04 monitoring (interval/backend re-init/toggle persistence); MV-M4-05 Sparkline/Clock polish; MV-M4-06 focus matrix + idle CPU/soak; MV-M4-07 documentation. Record the supported-feature declaration: supported = everything implemented except `RF-072` (skipped) and the dropped set.

**Tests.** Full suite green (1480 + new M4 tests). Manual MV-M4 checks pass or a documented, human-accepted limitation is recorded.

**Acceptance criteria.** AC-M4-01..13 pass; the freeze docs read consistently; the reviewer accepts the M4 implementation against the spec without modifying code.

**Requirements:** M4-RF-601/962/963/964, M4-RNF-141..146.

---

## Coverage statement

All **32** `M4-RF-###` and **6** `M4-RNF-###` requirements are mapped to at least one task (see plan §9 and the overview table). The four code gap-fixes are T-02 (Sparkline fill), T-03 (Clock culture), T-04 (`AppearanceConfig` extraction), and T-05 (property editor coverage); the M3-minor resolution tasks are T-02/T-03/T-04; the promoted-feature verification/cohesion tasks are T-01/T-06/T-07/T-08/T-09; and T-10 is the final validation + supported declaration. **`RF-072` is excluded**, no dropped feature is implemented, and the schema stays **v4**. No M5.
