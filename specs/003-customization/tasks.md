# TouchPanel — Tasks 003: Customization (M3)

| Field | Value |
|---|---|
| Tasks ID | 003-customization |
| Status | Draft for human approval |
| Date | 2026-10-10 |
| Inputs | `specs/003-customization/{spec.md, plan.md}` |
| Execution rule | After every task: `dotnet build` and `dotnet test` must succeed. No M4 work, no frozen-backlog work, no dropped-feature work, **no RF-072**. |
| Scope | RF-046, RF-061, RF-084 |

Task granularity is a **vertical slice** (a coherent feature/subsystem), not one class per task. Each task lists purpose, files/dirs, dependencies, implementation notes, tests, acceptance criteria, and requirement IDs.

## Overview

| ID | Title | Depends on | Requirements |
|---|---|---|---|
| T-01 | Theme-resource refactor + built-in themes + `ThemeManager` (DynamicResource, self-merge removal, swap-with-rollback) | — | M3-RF-842/845/848/849/851, M3-RNF-132/134/136/138/140, AC-M3-12 |
| T-02 | Clock widget (preset/custom-safe format, culture-pinned, boundary-scheduled, OS-event recompute) | T-01 | M3-RF-462/463, M3-RNF-131/133/135/138/140, AC-M3-03/04 |
| T-03 | ImageButton widget (built-in icon + `showLabel` + one action, non-activating) | T-01 | M3-RF-464/465, M3-RNF-133/140, AC-M3-03/04 |
| T-04 | Bar widget (single-value horizontal meter over `ISensorSnapshotSource`) | T-01 | M3-RF-466/467, M3-RNF-131/133/138/139/140, AC-M3-03/04 |
| T-05 | Widget extensibility + JSON forward-compat + palette + no-host-change test | T-02, T-03, T-04 | M3-RF-461/468/469, M3-RNF-134, AC-M3-03/13 |
| T-06 | Hotkey editor model + validation + persistence (`SettingsChange.Hotkeys`, `BuildUpdated` preserve) | — | M3-RF-611/612/613/615/619, M3-RNF-133/138, AC-M3-05 |
| T-07 | `HotkeysEditorWindow` + tray entry + live re-apply + failure semantics | T-06 | M3-RF-611/614/616/617/618/619, M3-RNF-132/137/138, AC-M3-06 |
| T-08 | Schema v4 + appearance config + migration + validation/fallback | T-01 | M3-RF-841/846/847, M3-RNF-133/135, AC-M3-02/07 |
| T-09 | Accent color + icon packs + precedence + live VM refresh | T-01, T-08 | M3-RF-843/844/845, M3-RNF-134/135, AC-M3-07 |
| T-10 | Appearance apply pipeline + selection UI | T-07, T-08, T-09 | M3-RF-848/850, M3-RNF-132/137/138, AC-M3-08 |
| T-11 | Final verification: manual matrix, real-BAML, performance, Core-Release regression | T-10 | all M3-RF/M3-RNF, AC-M3-01..13 |

---

## T-01 — Theme-resource refactor + built-in themes + `ThemeManager`

**Phase:** foundation (first).

**Objective.** Make live, app-wide theme swapping actually possible and safe: convert theme references to `DynamicResource`, remove the per-dictionary palette self-merge, split the palette into built-in themes, and add a `ThemeManager` that swaps the active theme with wholesale rollback.

**Files/directories.**
- `TouchPanel/Theme/Themes/Dark.xaml`, `Light.xaml`, `HighContrast.xaml` — one `ResourceDictionary` per built-in theme defining the same key set as `Theme/Colors.xaml` (panel background/foreground/disabled, accent, widget surface/pressed/disabled/border/error). `Dark` reproduces the current palette exactly.
- `TouchPanel/Theme/Colors.xaml` — reduced to the shared/default palette source or retired; **no `StaticResource` theme bindings remain**.
- `TouchPanel/Theme/Styles.xaml` — remove the merged `Colors.xaml`; convert `PanelForegroundBrush`, `WidgetSurfaceBrush`, `WidgetBorderBrush`, `WidgetErrorBrush`, `PanelAccentBrush`, etc. to `DynamicResource`; implement the dynamic-safe per-widget override mechanism (base element carries `DynamicResource`; override layered via presence-converter overlay/Style or VM-resolved brush).
- `TouchPanel/Widgets/*/*Templates.xaml` — convert all theme-key references to `DynamicResource`.
- `TouchPanel/Theme/ThemeManager.cs` — WPF-affine host: given a theme id (and later accent), merges the active theme dictionary at the application level (`Application.Current.Resources`), captures the previous state, and restores it wholesale on failure.
- `TouchPanel/Theme/ThemeIds.cs` — the fixed theme ids (`dark`, `light`, `highContrast`).
- `App.xaml` — merge the active theme dictionary at the app level.
- `TouchPanel.Tests/Theme/` — theme resolution tests + `ThemeManagerTests` + the real-BAML STA test.

**Dependencies:** none.

**Implementation notes.** Prefer the pure `DynamicResource` conversion; the alternative controlled re-apply is allowed only if AC-M3-12 passes. Because `{DynamicResource}` cannot be `Binding.TargetNullValue`, use DD-M3-2's base+overlay (or `SetResourceReference`) mechanism so RF-435/436 overrides keep winning. `ThemeManager.Apply` captures the previous theme and swaps; on any exception it restores the previous theme wholesale (no mixed keys) and logs. UI-thread marshalling belongs to the composition root/caller; `ThemeManager` itself runs only on the UI thread. Accessibility: `HighContrast` uses high-contrast-appropriate values (system high-contrast colors where practical) and preserves ≥ 40 DIP targets.

**Tests.**
- Styles/templates resolve theme keys dynamically (no `StaticResource` theme binding remains) — a source/reflection guard test.
- `ThemeManager` selects a theme, is idempotent, and rolls back wholesale on an injected swap failure (no mixed keys).
- **Real-BAML (STA):** load the real `Styles.xaml` + a widget template dictionary, realize an override-less and an overridden widget, swap the theme at runtime; assert the override-less widget's brush changes, the overridden widget keeps its override while non-overridden properties follow, and an injected failure restores the previous theme (AC-M3-12).
- Layer/presentation checks: no business logic added to `Theme/` beyond the thin host.

**Acceptance criteria.** AC-M3-12 passes; a live theme switch updates already-realized widgets; per-widget overrides still win; a failed swap leaves the previous theme fully applied.

**Requirements:** M3-RF-842/845/848/849/851, M3-RNF-132/134/136/138/140.

---

## T-02 — Clock widget

**Phase:** widgets.

**Objective.** Ship a config-driven, touch-friendly Clock with safe formatting and event-driven boundary recomputation, as a plain code addition with no host changes.

**Files/directories.**
- `TouchPanel/Config/WidgetConfig.cs` — `ClockWidgetConfig` (Mode enum + `Format`), `WidgetTypes.Clock`, `ClockMode`.
- `TouchPanel/Config/Json/WidgetConfigJsonConverter.cs` — recognize `clock`.
- `TouchPanel/Config/Validation/ConfigValidator.cs` — validate `mode` (unknown → `time24`), bound `format` length (`1–64`), ignore invalid/throwing formats.
- `TouchPanel/ViewModels/ClockWidgetViewModel.cs` — culture-pinned effective format; escaped-literal-aware detection (`s`/`f`/`F` force 1 s; `t`/`tt` do **not**); boundary-scheduled `TimeProvider` timer; update-on-change only; recompute next boundary after each tick and on clock-state events.
- `TouchPanel/Services/Time/IClockStateSource.cs` + `SystemEventsClockStateSource.cs` — testable seam over `SystemEvents.TimeChanged`/`PowerModeChanged`(Resume)/`SessionSwitch`; no P/Invoke.
- `TouchPanel/Widgets/ClockWidget/ClockWidgetTemplates.xaml`, `WidgetTemplateKeys.Clock`.
- `TouchPanel/Widgets/WidgetRegistry.cs` — register `clock` (`DisplayName`, default config, `240×120`).
- `App.xaml` — merge the Clock template dictionary.

**Dependencies:** T-01 (templates authored `DynamicResource`-first).

**Implementation notes.** Display-only; no action. Format is data: length-capped, tried via a safe formatter, else the `mode` preset with a warning (never an error state). `t`/`tt` must not trigger per-second refresh. Subscribe/unsubscribe clock-state events with the widget so disposal leaves no live handlers. No idle busy loop.

**Tests.**
- Format validation + fallback; effective-format culture pinning.
- Escaped-literal-aware seconds detection including `t`/`tt` not forcing per-second and quoted/`\` literals ignored.
- Cadence decision (fake `TimeProvider`): minute boundary normally, 1 s with seconds, capped at 1 s.
- Boundary recompute on a fake clock-state event; update-on-change only; timer/format failure degrades without crash.
- Registry/palette default config.

**Acceptance criteria.** AC-M3-03 (Clock) and AC-M3-04 (Clock) pass; no host change required.

**Requirements:** M3-RF-462/463, M3-RNF-131/133/135/138/140.

---

## T-03 — ImageButton widget

**Phase:** widgets.

**Objective.** Ship a config-driven icon button that executes exactly one action, as a plain code addition with no host changes.

**Files/directories.**
- `TouchPanel/Config/WidgetConfig.cs` — `ImageButtonWidgetConfig` (`ShowLabel`, default `true`), `WidgetTypes.ImageButton`.
- `TouchPanel/Config/Json/WidgetConfigJsonConverter.cs` — recognize `imageButton`.
- `TouchPanel/Config/Validation/ConfigValidator.cs` — disabled-when-invalid-action (RF-047); no external image path.
- `TouchPanel/ViewModels/ImageButtonWidgetViewModel.cs` — uses base `Icon` (resolved via the icon catalog; icon-pack wiring lands in T-09), label, single-action activation with transient error state; non-activating.
- `TouchPanel/Widgets/ImageButtonWidget/ImageButtonWidgetTemplates.xaml`, `WidgetTemplateKeys.ImageButton`.
- `TouchPanel/Widgets/WidgetRegistry.cs` — register `imageButton` (`120×120`, default `MuteActionConfig`).
- `App.xaml` — merge the template dictionary.

**Dependencies:** T-01.

**Implementation notes.** Tap = release inside the control, exactly one action, immediate feedback, ≥ 40×40 DIP, no hover/right-click dependency. Missing/invalid action → visible-but-disabled with an indicator (incl. edit mode, where actions do not fire). Renders label/default when no icon is set. External image files are not read.

**Tests.**
- Tap executes the bound action exactly once; failure shows the transient error state + log.
- Disabled-when-invalid-action; no action in edit mode.
- `showLabel`/icon projection; template/registry default config.

**Acceptance criteria.** AC-M3-03 (ImageButton) and AC-M3-04 (ImageButton) pass; no host change required.

**Requirements:** M3-RF-464/465, M3-RNF-133/140.

---

## T-04 — Bar widget

**Phase:** widgets.

**Objective.** Ship a config-driven single-value horizontal meter that consumes the existing snapshot boundary, as a plain code addition with no host changes.

**Files/directories.**
- `TouchPanel/Config/WidgetConfig.cs` — `BarWidgetConfig` (`Sensor`, `Minimum` default `0`, `Maximum` default `100`, `Label`, `ShowValue` default `true`), `WidgetTypes.Bar`.
- `TouchPanel/Config/Json/WidgetConfigJsonConverter.cs` — recognize `bar`.
- `TouchPanel/Config/Validation/ConfigValidator.cs` — normalize invalid ranges to defaults; unknown sensor → disabled with reason.
- `TouchPanel/ViewModels/BarWidgetViewModel.cs` — consumes `ISensorSnapshotSource`/`SensorSnapshot` like Gauge; value→fraction mapping, clamp, "Unavailable", update-on-change only.
- `TouchPanel/Widgets/BarWidget/BarWidgetTemplates.xaml`, `WidgetTemplateKeys.Bar`.
- `TouchPanel/Widgets/WidgetRegistry.cs` — register `bar` (`240×64`).
- `App.xaml` — merge the template dictionary.

**Dependencies:** T-01.

**Implementation notes.** Never polls hardware, never calls `PublishOnce`, never reads history. Snapshot callbacks marshalled to the UI thread; updates at most once per snapshot. Unavailable/NaN stored/rendered as "Unavailable", never zero. Disposal unsubscribes from the snapshot source.

**Tests.**
- Mapping/clamp/normalization; "Unavailable" for missing/NaN; unknown-sensor disable.
- No-poll / no-history assertions; update-on-change only (fake snapshot source); disposal leaves no live handlers.
- Registry/palette default config.

**Acceptance criteria.** AC-M3-03 (Bar) and AC-M3-04 (Bar) pass; no host change required.

**Requirements:** M3-RF-466/467, M3-RNF-131/133/138/139/140.

---

## T-05 — Widget extensibility, JSON forward-compat, palette, no-host-change proof

**Phase:** widgets (cross-cutting).

**Objective.** Prove the RF-046 extension contract end to end and lock in forward-compatible JSON behavior for the new types.

**Files/directories.**
- `TouchPanel/Widgets/WidgetRegistry.cs`, `WidgetRegistration.cs` — confirm the catalog exposes all new types with deterministic defaults and no per-type host logic.
- `TouchPanel/Config/Json/WidgetConfigJsonConverter.cs`, `Config/Validation/ConfigValidator.cs` — round-trip + unknown-type preservation.
- `TouchPanel/ViewModels/Edit/IWidgetPaletteSource` implementation — new types in the palette (built-in only).
- `TouchPanel.Tests/Widgets/`, `TouchPanel.Tests/Architecture/` — registry/palette/converter/forward-compat + a no-host-change architecture test.

**Dependencies:** T-02, T-03, T-04.

**Implementation notes.** No change to `PageViewModel`, `WidgetTemplateSelector`, or `EditModeService` may be required to host the new types. Type-specific fields are **JSON-only** in M3 (property editor not extended). Forward compatibility: a **same-version** build lacking a type preserves it as an unknown-type placeholder (RF-079); a **lower-version** build reading a v4 file takes the whole-file future-schema path (RF-075.4).

**Tests.**
- All three types registered with deterministic default config/geometry; palette lists built-in types only.
- DTO round-trips for Clock/ImageButton/Bar; unknown-type placeholder keeps raw JSON.
- Forward-compat: same-version-missing-type placeholder; lower-version future-schema path leaves the file untouched.
- Architecture: hosting the new types required no change to the host/selector/`EditModeService` (source/reflection guard).

**Acceptance criteria.** AC-M3-03 (integration) and AC-M3-13 pass; no host code changed.

**Requirements:** M3-RF-461/468/469, M3-RNF-134.

---

## T-06 — Hotkey editor model + validation + persistence

**Phase:** hotkey editor.

**Objective.** Provide the WPF-free editing model (list/add/edit/remove) with shared validation and single-writer persistence, and establish the `BuildUpdated` preserve-every-section rule.

**Files/directories.**
- `TouchPanel/Input/HotkeyEditorModel.cs` — WPF-free operations over `HotkeyConfig`; generated unique ids; modifier toggle set + fixed key catalog; delegates validation to `HotKeyParser`/`SendKeyCombination`.
- `TouchPanel/Services/Config/SettingsCoordinator.cs` — add the optional `SettingsChange.Hotkeys` property; `BuildUpdated` must preserve **every** section it is not changing (hotkey edit ≠ appearance reset and vice versa).
- `TouchPanel.Tests/Input/`, `TouchPanel.Tests/Config/` — model + coordinator tests.

**Dependencies:** none (existing seams only).

**Implementation notes.** Validation reuses the runtime parser: ≥ 1 modifier, known key/modifier, exactly one action; invalid entries blocked inline and never written; duplicate combinations flagged inline with first-wins (configuration order). Persist only through `ISettingsService`/`SettingsCoordinator`/`IConfigStore`; the running config is not mutated before a successful save. No file watcher/polling.

**Tests.**
- List/add/edit/remove + unique id generation; validation via `HotKeyParser`; duplicate/invalid blocking.
- Save-through a fake store with `SettingsChange.Hotkeys`; a failed save changes nothing.
- `BuildUpdated` preserves every unchanged section (hotkey edit leaves `appearance`/window/telemetry/monitor/pages intact).

**Acceptance criteria.** AC-M3-05 passes.

**Requirements:** M3-RF-611/612/613/615/619, M3-RNF-133/138.

---

## T-07 — `HotkeysEditorWindow` + tray + live re-apply

**Phase:** hotkey editor.

**Objective.** Ship the focused, touch-first hotkey editor and wire it to the tray and to live re-registration without restart.

**Files/directories.**
- `TouchPanel/ViewModels/Hotkeys/HotkeysEditorViewModel.cs` — thin VM over `HotkeyEditorModel`; binds list/add/edit/remove + action picker (`ActionPickerViewModel`) + status.
- `TouchPanel/Views/Hotkeys/HotkeysEditorWindow.xaml(.cs)` — touch-first, activating editor (D-6 analogue).
- `TouchPanel/Services/Tray/TrayMenuCommand.cs`, `TrayMenu.cs`, `Services/Tray/TrayService.cs` — add the `Hotkeys…` tray entry.
- `TouchPanel/App.xaml.cs` — open the editor; after a save, call `App.ApplyHotKeys` (`IPanelConfigApplyTarget.ApplyHotKeys`) to unregister/re-register on the UI thread.
- `TouchPanel.Tests/Views/`, `TouchPanel.Tests/ViewModels/` — editor wiring + re-apply + failure semantics.

**Dependencies:** T-06.

**Implementation notes.** Opening the editor is a deliberate user action; opening/closing must not alter the panel's no-activate behavior, and applying/saving must never call `SetForegroundWindow`/`Activate`. Re-apply uses the existing hook (unregister previous set → register new). Narrowed F-M3-15: a post-save total failure may leave no registrations → log + exactly one aggregated notification, no retry; a pre-re-apply (save) failure leaves registrations intact. If the hotkey service failed to initialize, the editor still opens with an "unavailable this session" note.

**Tests.**
- Editor VM commands drive the model; save triggers the re-apply hook with the validated list (fake apply target).
- Registration failure aggregation + no-retry (fake registration seam); no activation on save/re-apply.
- Service-unavailable path opens the editor without crashing.

**Acceptance criteria.** AC-M3-06 passes; MV-M3-02 verified in T-11.

**Requirements:** M3-RF-611/614/616/617/618/619, M3-RNF-132/137/138.

---

## T-08 — Schema v4 + appearance config + migration + validation

**Phase:** schema/appearance.

**Objective.** Add the global `appearance` section additively and migrate older files through the ordered chain, with validated fallbacks.

**Files/directories.**
- `TouchPanel/Config/PanelConfig.cs` — `AppearanceConfig` (`Theme` default `dark`, `AccentColor` default `null`, `IconPack` default `default`) + `Appearance` section; `CurrentSchemaVersion = 4`.
- `TouchPanel/Config/Migrations/V3ToV4Migration.cs` — additive step (From `3`, To `4`).
- `TouchPanel/Config/Migrations/ConfigMigrationChain.cs` — append `V3ToV4Migration`.
- `TouchPanel/Config/Validation/ConfigValidator.cs` — entry-level fallbacks (unknown theme → `dark`, unknown iconPack → `default`, invalid accent → `null`, missing section → defaults) with warnings.
- `TouchPanel/Config/DefaultConfigGenerator.cs` — default `appearance`.
- `TouchPanel.Tests/Config/` — migration fixtures (v1→v4, v2→v4, v3→v4, failing step) + round-trip + fallback.

**Dependencies:** T-01 (theme ids).

**Implementation notes.** The step only documents the additive section and raises the version; no v3 field is removed/renamed. `MigrationRunner` validates after the step before persistence; a failing step follows the invalid/corrupt path and never overwrites the source. New widget-type discriminators require no schema step (T-05).

**Tests.**
- v3→v4 (and v1/v2→v4) migrate/validate/save, original backed up; failing step leaves source intact; four RF-075 states.
- `AppearanceConfig` round-trip; unknown theme/pack/color fallbacks; missing-section defaults.

**Acceptance criteria.** AC-M3-02 and AC-M3-07 (resolution/fallback) pass.

**Requirements:** M3-RF-841/846/847, M3-RNF-133/135.

---

## T-09 — Accent color + icon packs + precedence + live VM refresh

**Phase:** schema/appearance.

**Objective.** Resolve the global accent and a selectable built-in icon pack, apply them app-wide with correct precedence, and refresh live widgets on change.

**Files/directories.**
- `TouchPanel/Config/IconPackCatalog.cs` — built-in packs (`default` = current `IconCatalog.BuiltIn` 42 entries + ≥1 alternate) sharing one stable id namespace; missing id → `default` glyph.
- `TouchPanel/Config/IconCatalog.cs` — retained or folded; stable ids preserved.
- `TouchPanel/Theme/ThemeManager.cs` — apply the global accent under per-widget overrides (theme default if unset).
- `TouchPanel/ViewModels/WidgetViewModelBase.cs` — resolve `IconGlyph` through the active pack; refresh on pack change.
- `TouchPanel/App.xaml.cs` — an active appearance/pack source the VMs read.
- `TouchPanel.Tests/Config/IconPackTests.cs`, `TouchPanel.Tests/Theme/`. 

**Dependencies:** T-01, T-08.

**Implementation notes.** Precedence is global theme < global accent < per-widget override; `null` = inherit. Icon-pack switching updates all existing widget VMs' `IconGlyph` without a rebuild, on the UI thread. Built-in packs only; no external loading. Invalid/unknown pack → `default` with a warning.

**Tests.**
- Accent precedence (theme < global < per-widget) + invalid-accent fallback.
- Icon-pack resolution + missing-id fallback to `default` + stable ids; live `IconGlyph` refresh on pack change.
- No external file/pack loading.

**Acceptance criteria.** AC-M3-07 (accent/icon precedence) passes.

**Requirements:** M3-RF-843/844/845, M3-RNF-134/135.

---

## T-10 — Appearance apply pipeline + selection UI

**Phase:** schema/appearance.

**Objective.** Apply appearance changes live through the shared pipeline with wholesale rollback, and expose a touch selection surface for theme/accent/icon pack.

**Files/directories.**
- `TouchPanel/Services/Config/SettingsCoordinator.cs` — add the optional `SettingsChange.Appearance` property; `BuildUpdated` preserves every unchanged section (including `hotkeys`).
- `TouchPanel/Services/Config/PanelConfigApplier.cs` — add the isolated `appearance` step.
- `TouchPanel/Services/Config/` (`IPanelConfigApplyTarget`) — add `ApplyAppearance(AppearanceConfig)`.
- `TouchPanel/App.xaml.cs` — implement `ApplyAppearance` (marshalled to the UI thread; uses `ThemeManager` swap-with-rollback).
- `TouchPanel/Views/Hotkeys/HotkeysEditorWindow.xaml` — "Appearance" section (theme picker, accent set/clear, icon-pack picker) over the same `ISettingsService` path.
- `TouchPanel.Tests/Config/`, `TouchPanel.Tests/Views/` — apply pipeline + selection UI tests.

**Dependencies:** T-07 (editor window), T-08, T-09.

**Implementation notes.** A saved appearance change is published and applied without restart. The apply step captures the previous theme/pack/accent and restores it wholesale on failure (no half-applied theme), logs, and isolates. Applying appearance must not change geometry/pages/actions/monitoring and must not activate the panel. Invalid input is blocked inline or falls back with visible feedback; no new persistence mechanism is added.

**Tests.**
- `SettingsChange.Appearance` persists and applies; `BuildUpdated` preserves hotkeys (and vice versa) + other sections; failed save changes nothing.
- Apply step rollback on injected theme failure; isolated step failure does not stop other steps.
- Selection UI commands map to validated changes; invalid entry blocked/falls back.

**Acceptance criteria.** AC-M3-08 passes; MV-M3-03 verified in T-11.

**Requirements:** M3-RF-848/850, M3-RNF-132/137/138.

---

## T-11 — Final verification

**Phase:** verification.

**Objective.** Run the full automated suite, the M3 manual matrix, the real-BAML live-theme check, performance/soak, and the Core-Release regression; fix defects within M3 scope only.

**Files/directories.**
- No planned production changes; fixes as found; results recorded (e.g. `specs/003-customization/verification-notes.md` if the human wants a record).

**Dependencies:** T-10.

**Implementation notes.** Execute MV-M3-01..06 (widgets, hotkeys, appearance, focus matrix, performance/soak, Core-Release regression). Measure idle CPU (RNF-001) with themes + new widgets and the 8-hour soak (RNF-002). Confirm no elevation prompt, no new execution surface, and that frozen-backlog/dropped code is untouched. Do **not** implement RF-072 or any M4 work.

**Tests.** Manual matrix execution; final `dotnet build`/`dotnet test` green; performance/soak captures.

**Acceptance criteria.** Every AC-M3-01..AC-M3-13 has a pass result or a documented, human-accepted limitation.

**Requirements:** all M3-RF/M3-RNF, AC-M3-01..13.

---

## Execution notes

- Tasks run in order; dependencies are the minimum. T-01 must land first (it is the prerequisite for all appearance work).
- `dotnet build` and `dotnet test` must stay green after every task; a task is complete only when its tests and listed manual checks (or explicitly deferred ones) are done.
- No frozen-backlog work, no dropped-feature work, and **no RF-072 / M4 work** may be partially implemented "for later".
- The reviewer agent validates against the spec without modifying code; remediation is a new task appended in a later planning pass.
