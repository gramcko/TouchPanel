# TouchPanel — Implementation Plan 004: Promotion & Polish (M4)

| Field | Value |
|---|---|
| Plan ID | 004-promotion |
| Status | Draft for human approval |
| Date | 2026-10-10 |
| Inputs | `specs/004-promotion/spec.md` (reviewed **ACCEPTABLE**; 5 minor annotations applied), `specs/001-foundation/{spec,plan}.md` (incl. `D-25`), `specs/002-automation/{spec,plan,tasks}.md`, `specs/003-customization/{spec,plan,tasks}.md`, and the delivered implementation in `TouchPanel/` (schema **v4**, 1480 tests green; reviewer ACCEPTABLE for M3) |
| Scope | Promote the implemented M2 backlog (RF-040/043-non-appearance/044/045/060/070/071/082/083/091/092/093) via verification + gap-fix + supported declaration; resolve the three M3 findings; add cohesion/polish. |
| Out of scope | `RF-072` skipped; the dropped set stays dropped (RF-042/054/055/056/087, `delay`, shell-open). No M5; no schema bump beyond v4. |
| Task file | `specs/004-promotion/tasks.md` |

**M4 requirement set** (spec IDs): `M4-RF-401..402`, `M4-RF-430..433`, `M4-RF-440..441`, `M4-RF-450..451`, `M4-RF-600..601`, `M4-RF-700`, `M4-RF-710..711`, `M4-RF-820..821`, `M4-RF-830..831`, `M4-RF-910..911`, `M4-RF-920..921`, `M4-RF-930..931`, `M4-RF-950..952`, `M4-RF-960..964`, `M4-RNF-141..146`.

---

## 1. Architecture and integration with the delivered seams

M4 is a **delta on the delivered M1/M2/M3 product**. It **reuses every existing boundary** and adds no new architectural pattern (no DI container, no event bus, no plugin system, no repository/generic layering, no cross-platform shims). The WPF-free boundaries (`Config/`, `Monitoring/`, `Actions/`, `Input/`) stay WPF-free; P/Invoke stays only under `Interop/`; `Theme/` remains resource dictionaries plus thin hosts; `Services/Time/` remains WPF-free utility logic.

| M4 area | Delivered seam it extends | M4 approach |
|---|---|---|
| Promotion verification | Existing M2/M3 units: `EditModeService`, `PropertyEditorViewModel`, `ActionPickerViewModel`, `GridSnap`/`AlignmentGuides`, `ToggleStatePersistence`/`StateStore`, `HotKeyService`/local-shortcut handling, `MonitoringService`/`ThrottledFailureLogger`/`TelemetryConfig`, `ConfigReloadController`/`ReloadDecision`, `JsonConfigStore` import/export, `PageSwitcherViewModel`/`PageSwitchCoordinator`, `SettingsViewModel`/`MonitorPickerViewModel` | Verify unchanged against the M2 sub-requirements; produce the coverage table; fix only surfaced gaps. No rewrite. |
| Slider reachability | `WidgetPaletteSource`, `PropertyEditorViewModel`, `ConfigValidator` | Confirm palette entry + property-editor fields; validation reuses the shared rules. |
| Property editor coverage | `ViewModels/Edit/PropertyEditorViewModel`, `ActionPickerViewModel`, `Views/EditOverlay.xaml`, `Config/IconCatalog`/`IconPackCatalog` | Add Clock/ImageButton/Bar type-specific field editors over the existing VM/overlay pattern; built-in icons only; no new persistence. |
| Sparkline fill | `Widgets/SparklineWidget/SparklineWidgetTemplates.xaml`, `ViewModels/AppearanceForegroundConverter`, `AppearanceBrushFactory`, `Theme/ThemeManager`, `SparklineWidgetViewModel.AccentBrush` | Derive the filled-area brush from the effective accent (per-widget → global → theme) at a documented translucent alpha, using a `DynamicResource`-consistent binding so live swaps reach realized elements. |
| Clock culture | `Services/Time/ClockFormat`, `Config/Validation/ConfigValidator.ValidateClock`, `ViewModels/ClockWidgetViewModel`, `App.xaml.cs` | Introduce one WPF-free, injectable culture source; supply it to both validation and the VM. |
| `AppearanceConfig` | `Config/PanelConfig.cs` | Move the type to `Config/AppearanceConfig.cs`; behaviorless. |
| Schema/compat | `PanelConfig.CurrentSchemaVersion` (=4), `ConfigMigrationChain`, `ConfigValidator` | Keep v4; no `V4ToV5`; document session-only grid/snap. |
| Cohesion | `Services/Tray/TrayMenu.cs`, `Views/Settings/SettingsWindow.xaml`, `Views/Hotkeys/HotkeysEditorWindow.xaml`, `Views/EditOverlay.xaml`, `Views/PageSwitcher/PageSwitcherView.xaml` | Assert one surface per concern; no duplicate editors; document automatic/runtime behaviors. |
| Composition root | `App.xaml.cs` | Capture the Clock culture once; wire the property-editor fields and the Sparkline fill nothing extra; keep startup order and shutdown participation unchanged. |

### 1.1 Promotion surface map (`M4-RF-960`)

| Surface | Hosts | Does not host |
|---|---|---|
| **Settings window** (`RF-092`) | panel behavior (`alwaysOnTop`), startup (start-with-Windows), telemetry interval (`RF-071`), target-monitor picker (`RF-093`), import/export (`RF-083`), read-only diagnostics | hotkeys, appearance |
| **Hotkeys / appearance editor** (`RF-061`/`RF-084`) | global `hotkeys` list, theme/accent/icon pack | startup, monitor, interval, import/export |
| **Edit mode / overlay** (`RF-043`/`RF-044`) | widget palette add/remove/duplicate, property editor, action picker/browse, grid snap + alignment guides; Slider and all built-in types | app-level settings |
| **Page switcher** (`RF-091`) | in-panel page selection in normal mode | widget editing |
| **Tray menu** (`RF-005`) | Show/Hide, Edit Mode, Settings…, Hotkeys…, Open Config Folder, Exit | direct editors |

**No user surface required (automatic/runtime behaviors, `M4-RF-964`/M4-M1):** RF-070 backend re-init, RF-082 hot reload, RF-060 local shortcuts (fixed app-defined keys), and the runtime side of RF-045 (toggle-state restoration). "Supported" for these means active + logged/observable + tested. RF-045's only user knob is the per-Toggle `persistState` flag, reachable via the property editor.

## 2. Key design decisions

| ID | Decision | Justification |
|---|---|---|
| DD-M4-1 | **Promotion is verification-first.** W1 executes and maps the existing M2/M3 suites before any change; only surfaced gaps are fixed. | The features are already delivered; re-writing them risks regressions and violates "no new features". |
| DD-M4-2 | **Property editor extended to all built-in types** — add Clock (`mode`/`format`), ImageButton (`showLabel`/icon), and Bar (`sensor`/`minimum`/`maximum`/`showValue`) fields to `PropertyEditorViewModel`/`EditOverlay.xaml`, reusing the existing typed-field + validation pattern; ImageButton also uses the existing `ActionPickerViewModel`; icons come from the built-in `IconCatalog`/active `IconPackCatalog` only. | `M4-RF-432`/D-M4-2 reverses `M3-RF-468`/`D-M3-8` now that the editor is supported; keeps one editing path and `RF-081` (no external image paths). |
| DD-M4-3 | **Grid size and snap stay session-only**, default `gridSize=8` / snap-on; documented; **no schema field**. | `M4-RF-441`/D-M4-3 matches `M2-RF-441`'s "per-session snap toggle"; avoids a v5 bump (`M4-RF-961`/D-M4-6). |
| DD-M4-4 | **Sparkline filled area derives from the effective accent** with precedence per-widget accent override → global accent → theme accent, at a documented translucent alpha (default `0x55`, the current value). Implemented as a `DynamicResource`-fed multi-binding (Tag = `{DynamicResource PanelAccentBrush}` + VM `AccentBrush` override) through a small fill converter that applies the alpha, so realized elements follow live theme/accent swaps and the override still wins. | `M4-RF-950`/D-M4-4; preserves `RF-436` precedence and the `M3-RF-851` dynamic layer; removes the hard-coded `#553D7EFF`. |
| DD-M4-5 | **One shared, injectable Clock culture source** in `Services/Time/` (WPF-free), captured once at the composition root and supplied to **both** `ConfigValidator.ValidateClock` (via an injected `CultureInfo`/source parameter, no longer `InvariantCulture`) and `ClockWidgetViewModel`. Tests pin an explicit culture. | `M4-RF-951`/D-M4-5 eliminates the load-vs-runtime divergence while keeping validation pure and testable; the source remains WPF-free per `RNF-010`. |
| DD-M4-6 | **Schema stays v4; no migration added.** Promoted fields already exist via `v1→v2→v3→v4`. Grid/snap is not persisted. | `M4-RF-961`; avoids churn and keeps all four `RF-075` load states covered. |
| DD-M4-7 | **`AppearanceConfig` extracted** to `Config/AppearanceConfig.cs` with no behavior change (same namespace/shape; JSON round-trip and `v3→v4` unchanged). | `M4-RF-952`; pure organization, keeps `PanelConfig.cs` focused. |
| DD-M4-8 | **Cohesion asserted, not redesigned.** Add an ownership test/assertion proving the settings window does not host hotkey/appearance controls and the hotkeys editor does not host startup/monitor/interval/import-export; document automatic behaviors. | `M4-RF-921`/`M4-RF-960`/`M4-RF-964`; prevents duplicate editors without touching delivered UI. |
| DD-M4-9 | **Freeze documentation already updated in the spec phase** (`001 §5/§7.2/§7.3/§8 D-25`, `002 §2/§7.3/§14`, `003 §5.1/§13`); the M4 final task re-verifies consistency as an acceptance invariant. | `M4-RF-962`/AC-M4-13; the promotion record must not drift. |

## 3. Affected modules / directories

| Workstream | Primary locations | Tasks |
|---|---|---|
| Verification sweep (no code) | all `TouchPanel.Tests/` (mapping, not modification unless a gap is found) | T-01, T-07, T-08 |
| Sparkline fill | `TouchPanel/Widgets/SparklineWidget/SparklineWidgetTemplates.xaml`, `ViewModels/AppearanceForegroundConverter.cs` (+ new fill converter), `ViewModels/AppearanceBrushFactory.cs`, `ViewModels/SparklineWidgetViewModel.cs`, `TouchPanel.Tests/` (real-BAML) | T-02 |
| Clock culture | `TouchPanel/Services/Time/` (culture source), `Config/Validation/ConfigValidator.cs`, `ViewModels/ClockWidgetViewModel.cs`, `App.xaml.cs`, `TouchPanel.Tests/` | T-03 |
| `AppearanceConfig` extract | `TouchPanel/Config/PanelConfig.cs` → `Config/AppearanceConfig.cs` | T-04 |
| Property editor coverage | `TouchPanel/ViewModels/Edit/PropertyEditorViewModel.cs`, `Views/EditOverlay.xaml`, `ViewModels/Edit/ActionPickerViewModel.cs` (ImageButton), `Config/IconCatalog.cs`/`IconPackCatalog.cs`, `TouchPanel.Tests/` | T-05 |
| Surface-map cohesion | `Views/Settings/SettingsWindow.xaml`, `Views/Hotkeys/HotkeysEditorWindow.xaml`, `Views/EditOverlay.xaml`, `Views/PageSwitcher/PageSwitcherView.xaml`, `TouchPanel.Tests/` (host/ownership tests) | T-06 |
| Runtime promoted-behavior verification | `Monitoring/`, `Config/` reload/import-export, `Input/` local shortcuts, `ToggleStatePersistence`/`StateStore`, `TouchPanel.Tests/` | T-07, T-08 |
| Schema/grid guard | `Config/PanelConfig.cs`, `Config/Migrations/`, `ViewModels/Edit/EditModeService.cs`, `TouchPanel.Tests/` | T-09 |
| Final validation | all (within M4 scope only) | T-10 |

## 4. Dependency order and rationale

```text
T-01 promotion verification baseline (no code; map tests↔ACs, regression)
      │
      ├─► T-02 Sparkline fill follows accent ─────────────┐
      ├─► T-03 shared Clock culture source ───────────────┤
      ├─► T-04 AppearanceConfig extraction ───────────────┤ (independent code gap-fixes)
      └─► T-05 property editor coverage (Clock/ImageButton/Bar + Slider reachability)
                                                           │
      T-06 surface-map cohesion + ownership tests ─────────┤
      T-07 editing/grid/toggle/local-shortcut verification ┤
      T-08 config/UI/runtime verification (reload, import/export, page switch, settings, monitor, interval, backend re-init)
      T-09 schema-v4 + grid session-only guard ────────────┤
                                                           ▼
                              T-10 final M4 validation + supported declaration
```

- **T-01 first** so the existing suite is mapped to accepted behaviors before any code changes; it also surfaces coverage gaps that feed T-07/T-08.
- **T-02/T-03/T-04/T-05** are the only code changes and are mutually independent (Sparkline presentation; Clock validation/VM; a mechanical file move; property-editor fields). They land on the green baseline.
- **T-06..T-09** are verification/cohesion/guard tasks that depend only on T-01 (and T-05 for edit-UI cohesion).
- **T-10** runs the full suite, the manual matrix, performance/soak, invariant checks, and the documentation/supported declaration.

Each task ends with `dotnet build` and `dotnet test` green; manual checks are performed when the task lands or are explicitly deferred to T-10.

## 5. Verification approach for the verification-only promoted features

Because M4 promotes already-implemented work, the primary evidence is the **existing** test classes mapped to the M4 sub-requirements and their M2 normative sets. T-01 produces this table; T-07/T-08 execute and (only if a gap is found) close it.

| Promoted base | Existing test evidence | Additional M4 testing needed |
|---|---|---|
| RF-040 Slider (`M4-RF-401/402`) | `SliderWidgetTests`, `SliderTemplateTests`, `PropertyEditorTests` | Property-editor Slider reachability asserted via T-05; otherwise none. |
| RF-043 edit UI (`M4-RF-430/431/433`) | `EditModeServiceTests`, `EditOverlayTests`, `PropertyEditorTests`, `ActionPickerTests`, `GridSnapTests` | Cohesion/ownership (T-06); new-type fields (T-05). |
| RF-043 property editor coverage (`M4-RF-432`) | (new) — currently JSON-only for M3 types | New tests in T-05 (Clock/ImageButton/Bar fields, validation, icon allowlist). |
| RF-044 grid/alignment (`M4-RF-440/441`) | `GridSnapTests`, `AlignmentGuidesTests`, `EditSnapTests` | Session-only guard/documentation (T-09). |
| RF-045 toggle persistence (`M4-RF-450/451`) | `ToggleStateTests`, `StateStoreTests` | No reload-loop assertion already in `AppWriteSuppressionTests`; T-07 confirms. |
| RF-060 local shortcuts (`M4-RF-600/601`) | `LocalShortcutsTests`, `EditModeServiceTests` | Fixed-set/no-editor documentation (T-06/T-10). |
| RF-070 backend re-init (`M4-RF-700`) | `BackendReinitTests`, `ThrottledFailureLoggerTests` | T-08 executes. |
| RF-071 interval (`M4-RF-710/711`) | `IntervalTests`, `SettingsViewModelTests` | T-08 confirms settings hosting. |
| RF-082 hot reload (`M4-RF-820/821`) | `ConfigReloadControllerTests`, `ConfigReloadPipelineTests`, `AppWriteSuppressionTests`, `ReloadDecisionTests` | T-08 executes. |
| RF-083 import/export (`M4-RF-830/831`) | `ImportExportTests`, `SettingsViewModelTests` | T-08 confirms settings hosting. |
| RF-091 page switching (`M4-RF-910/911`) | `PageSwitcherViewModelTests`, `PageSwitcherViewTests`, `PageSwitchCoordinatorTests`, `PageSwitchIntegrationTests` | T-08 executes. |
| RF-092 settings (`M4-RF-920/921`) | `SettingsWindowTests`, `SettingsViewModelTests`, `SettingsCoordinatorTests` | Ownership (T-06). |
| RF-093 monitor picker (`M4-RF-930/931`) | `MonitorPickerViewModelTests`, `MonitorSelectionServiceTests` | T-08 confirms settings hosting. |

**Invariant guards** (`M4-RF-963`, `M4-RNF-142/145`): the existing `LayerPurityTests` remains green; T-03/T-04/T-05 must not add WPF types to `Services/Time/`, `Config/`, or the editor VM. No elevation, no new execution surface.

## 6. Testing strategy

Aligned with M1 §11 / M2 §10 / M3 §11: unit tests are the gate; hardware/network/elevation are excluded; temp directories isolate the profile; WPF-free layer purity is enforced. No UI-automation framework is added.

- **Unit tests (no hardware/drivers/elevation/network):**
  - **Gap fixes:** Sparkline fill precedence/fallback/alpha via a converter unit test plus one **real-BAML** realization test (load the real `SparklineWidgetTemplates.xaml`, assert override-less fill uses global/theme accent, an overridden widget's fill uses its override, and a live accent/theme swap updates a realized element) — `M4-RF-950`, AC-M4-03.
  - Clock validator/VM shared-culture test with a pinned culture: a format valid in the culture is accepted by both; an invalid one rejected by both; asserted through the same injected source — `M4-RF-951`, AC-M4-04.
  - Property-editor fields for Clock/ImageButton/Bar: set/validate/clamp through the shared `ConfigValidator` rules; ImageButton icon restricted to the built-in catalog; JSON round-trip unchanged — `M4-RF-432`, AC-M4-06.
  - `AppearanceConfig` extraction: unchanged namespace/shape; `AppearanceConfigTests`/`MigrationV4Tests`/`ConfigJson*` remain green — `M4-RF-952`, AC-M4-05.
  - Cohesion/ownership: a test (reflection/source assertion over the settings/editor views) proving no duplicate editors — `M4-RF-921/960`, AC-M4-08.
  - Schema/grid guard: `PanelConfig.CurrentSchemaVersion == 4`, `ConfigMigrationChain` ends at v4 with no `V4ToV5`; grid/snap state is not persisted — `M4-RF-961`, `M4-RF-441`, AC-M4-13.
- **Integration tests (no real hardware):** `JsonConfigStore` temp-dir import/export and reload self-suppression remain correct after the `AppearanceConfig` move; `SettingsCoordinator` section-preservation (hotkey edit ≠ appearance reset and vice versa) remains green.
- **Manual verification matrix (T-10):** MV-M4-01 edit UI/Slider/grid; MV-M4-02 settings/editor cohesion; MV-M4-03 hot reload/import-export/page switching; MV-M4-04 monitoring (interval/backend re-init/toggle persistence); MV-M4-05 Sparkline/Clock polish; MV-M4-06 focus matrix + performance/soak; MV-M4-07 documentation consistency.
- **Gates:** after every task `dotnet build` + `dotnet test` green; T-10 passes every AC-M4-01..13 or records a documented, human-accepted limitation; a separate reviewer validates without modifying code.

## 7. Migration and compatibility

- **Schema:** `PanelConfig.CurrentSchemaVersion` **stays 4**; the `ConfigMigrationChain` remains `[V1ToV2, V2ToV3, V3ToV4]`; **no** `V4ToV5` is added (`M4-RF-961`/DD-M4-6). Promoted fields already exist. All four `RF-075` load states remain covered.
- **Grid/snap:** session-only (`M4-RF-441`/DD-M4-3); no schema field; restart resets to `8`/snap-on.
- **Widget types:** the M3 discriminators are unchanged; the only M4 change is that their type-specific fields become editable via the property editor (`M4-RF-432`), with JSON round-trips untouched.
- **Runtime:** standard user, no UAC (`RF-086`); self-contained `win-x64` behavior unchanged; no new watcher/polling.

## 8. Risks and mitigations

| ID | Risk | Mitigation / task anchor |
|---|---|---|
| RM4-1 | A "verification-only" feature regresses or a coverage gap is missed | T-01 maps every promoted base → tests → ACs and runs the full suite first; gaps feed T-07/T-08 (DD-M4-1). |
| RM4-2 | Sparkline fill binding does not follow a live theme swap on realized elements | `DynamicResource`-fed multi-binding + real-BAML test (DD-M4-4; T-02, AC-M4-03). |
| RM4-3 | Per-widget accent override stops winning after the fill change | Precedence asserted in the real-BAML test (override → global → theme) (DD-M4-4; T-02). |
| RM4-4 | Clock validator/VM culture source is captured at different times | Capture once at the composition root and supply the same instance to both; tests pin explicitly (DD-M4-5; T-03, AC-M4-04). |
| RM4-5 | `AppearanceConfig` move breaks serialization/migration | Behaviorless move; round-trip + `v3→v4` + JSON tests stay green (DD-M4-7; T-04, AC-M4-05). |
| RM4-6 | Property-editor extension accepts an external image path | ImageButton icons restricted to the built-in catalog/active pack; validation test (DD-M4-2; T-05, `RF-081`). |
| RM4-7 | Duplicate/competing editors sneak in | Ownership assertion + surface map (DD-M4-8; T-06, AC-M4-08). |
| RM4-8 | Schema accidentally bumped or grid persisted | Guard test asserting v4 and non-persistence (T-09, AC-M4-13). |
| RM4-9 | Freeze documentation drifts from the code | T-10 re-verifies tags/`D-25` consistency (DD-M4-9; AC-M4-13). |
| RM4-10 | Layer purity eroded by the new culture/editor code | Culture source stays in WPF-free `Services/Time/`; `LayerPurityTests` remains green (T-03/T-05, `M4-RNF-142`). |

## 9. Traceability — M4 requirement → task

| Requirement | Task(s) |
|---|---|
| M4-RF-401 (Slider supported/verification) | T-01, T-07 |
| M4-RF-402 (Slider palette + property editor) | T-05 |
| M4-RF-430 (palette add/remove/duplicate + unknown-type) | T-01, T-07 |
| M4-RF-431 (property editor + action picker/browse verification) | T-01, T-05, T-07 |
| M4-RF-432 (property editor covers Clock/ImageButton/Bar) | T-05 |
| M4-RF-433 (persistence/touch/isolation) | T-06, T-07 |
| M4-RF-440 (grid/alignment verification) | T-07 |
| M4-RF-441 (grid/snap session-only) | T-09 |
| M4-RF-450 (toggle persistence) | T-07 |
| M4-RF-451 (no reload loop) | T-07, T-08 |
| M4-RF-600 (local shortcuts) | T-07 |
| M4-RF-601 (fixed-set boundary) | T-06, T-10 |
| M4-RF-700 (backend re-init) | T-08 |
| M4-RF-710 (interval) | T-08 |
| M4-RF-711 (interval settings hosting) | T-08 |
| M4-RF-820 (hot reload) | T-08 |
| M4-RF-821 (pure reload decision) | T-08 |
| M4-RF-830 (import/export) | T-08 |
| M4-RF-831 (import/export settings hosting) | T-08 |
| M4-RF-910 (page switching) | T-08 |
| M4-RF-911 (page switch no focus steal) | T-08 |
| M4-RF-920 (settings window) | T-08 |
| M4-RF-921 (cohesion — no duplicate editors) | T-06 |
| M4-RF-930 (monitor picker) | T-08 |
| M4-RF-931 (monitor picker settings hosting) | T-08 |
| M4-RF-950 (Sparkline fill follows accent) | T-02 |
| M4-RF-951 (shared Clock culture source) | T-03 |
| M4-RF-952 (AppearanceConfig extraction) | T-04 |
| M4-RF-960 (canonical surface map) | T-06 |
| M4-RF-961 (schema stays v4) | T-09 |
| M4-RF-962 (promotion declaration/freeze docs) | T-10 |
| M4-RF-963 (invariant preservation) | T-02, T-03, T-05, T-06, T-10 |
| M4-RF-964 (reachability/documentation; automatic behaviors) | T-06, T-10 |
| M4-RNF-141 (testability) | T-01..T-10 (per-task tests) |
| M4-RNF-142 (layering) | T-03, T-04, T-05 |
| M4-RNF-143 (no new idle work) | T-08, T-10 |
| M4-RNF-144 (bounded resources) | T-08, T-10 |
| M4-RNF-145 (security posture) | T-05, T-08, T-10 |
| M4-RNF-146 (no focus-steal regression) | T-06, T-08, T-10 |
| **Coverage check** | All 32 `M4-RF` + 6 `M4-RNF` mapped; **RF-072 excluded**; no dropped/frozen requirement implemented; no schema bump beyond v4. |
