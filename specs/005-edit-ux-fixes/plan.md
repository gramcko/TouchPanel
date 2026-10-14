# TouchPanel — Implementation Plan 005: Edit-UX Fixes (M5)

| Field | Value |
|---|---|
| Plan ID | 005-edit-ux-fixes |
| Status | Draft (M5 — specification + plan + tasks) |
| Date | 2026-10-10 |
| Inputs | `specs/005-edit-ux-fixes/spec.md`, `specs/001-foundation/*` (RF-018/026/032/034/039/043/047/058/079/080/084), `specs/002-automation/*`, `specs/003-customization/*`, `specs/004-promotion/*`, delivered implementation (schema **v4**, 1514 tests green) |
| Scope | Five edit-UX fixes: delete affordance, sensor picker, hotkey-reference action, add-widget flow, theme-aware defaults |
| Out of scope | `RF-072`, dropped set, macros, plugin/DI/event-bus, elevation, schema bump beyond v4 |
| Task file | `specs/005-edit-ux-fixes/tasks.md` |

**M5 requirement set** (spec IDs): `M5-RF-501..504`, `M5-RF-510..513`, `M5-RF-520..524`, `M5-RF-530..534`, `M5-RF-540..544`, `M5-RF-550..551`, `M5-RNF-551..556`.

---

## 1. Architecture and integration with the delivered seams

M5 is a **delta on the delivered M1–M4 product**. It adds no architectural pattern (no DI container, no event bus, no plugin system, no repository/generic layering, no cross-platform shims). It extends existing boundaries and keeps `Config/`, `Monitoring/`, `Actions/`, and `Input/` WPF-free; P/Invoke stays under `Interop/`.

| M5 area | Delivered seam it extends | M5 approach |
|---|---|---|
| Delete affordance | `Views/EditOverlay.xaml(.cs)`, `ViewModels/EditModeService.cs` (`RemoveSelectedWidget`, `CanModifySelection`, `SelectedWidget`) | Add a touch-first X in the overlay's widget item template (visible only for the selected editable widget) and a small inline confirmation state on the service; confirm routes to the existing `RemoveSelectedWidget` + dirty/save path. |
| Sensor picker | `Monitoring/SensorKeys`, `Monitoring/SensorSnapshot`, `Monitoring/SensorSelector`, `ViewModels/Edit/PropertyEditorViewModel`, `Views/EditOverlay.xaml` | New WPF-free `ISensorCatalog` seam (`Monitoring/` or `Config/`) over the existing key model; the editor's sensor field becomes a list (`SensorOptions`/`SelectedSensor`); unknown keys preserved. |
| Hotkey-reference action | `Config/ActionConfig.cs` (`ActionTypes`, `ActionConfig` subclasses), `Actions/ActionExecutor`/`IActionHandler`, `Config/Validation/ConfigValidator`, `ViewModels/Edit/ActionPickerViewModel`, `Config/PanelConfig.Hotkeys` | Additive `hotkey` action subtype + a resolver seam + a handler; the validator cross-checks references; the picker offers "Hotkey" + the configured hotkeys. |
| Add-widget flow | `ViewModels/EditModeService.AddWidget`, `ViewModels/PageViewModel`, `Views/WidgetPageHost.xaml`, `Views/EditOverlay.xaml(.cs)` | Make the widget collection notify (`ObservableCollection` or explicit refresh), wire the property-panel Apply command, and add host-level coverage so an added widget is visible immediately and persists through the single writer. |
| Theme-aware defaults | `Theme/ThemeIds`, `Config/WidgetAppearance`, `Widgets/WidgetRegistry`/`WidgetRegistration`, `ViewModels/Edit/PropertyEditorViewModel`, `Views/EditOverlay.xaml` | New WPF-free per-theme appearance-default table; `AddWidget` seeds new widgets from the active theme; the editor shows theme-aware initial/placeholder values and chrome reads theme resources. |
| Composition root | `App.xaml.cs` | Wire the sensor-catalog and hotkey-resolver seams into `EditModeService`; capture the active theme id for seeding; keep startup order and shutdown participation unchanged. |

## 2. Key design decisions

| ID | Decision | Justification |
|---|---|---|
| DD-M5-1 | **In-overlay delete affordance + inline confirmation.** The X lives inside the existing `EditOverlay` widget item template (top-right, ≥ 40 DIP, `Focusable=False`); confirming opens a small in-overlay confirm surface (not a `MessageBox`/new window) that routes to `EditModeService.RemoveSelectedWidget`. | `M5-RF-501..504`; keeps `RF-018` (no new activating window), reuses the existing removal + single writer, and stays touch-first/testable. |
| DD-M5-2 | **WPF-free sensor catalog with a fixed fallback list.** `ISensorCatalog.GetSensorOptions()` returns aggregate keys plus per-core/fan keys discovered from the current snapshot; when no snapshot exists it returns the fixed aggregate keys; the editor never writes an unknown key implicitly. | `M5-RF-510..513`; keeps the sensor model in the WPF-free boundary, works with no hardware, and never loses a hand-edited key. |
| DD-M5-3 | **Additive `hotkey` action that references a `HotkeyConfig.Id`.** New `ActionTypes.Hotkey` + `RunHotkeyActionConfig { HotkeyId }` + `RunHotkeyHandler` resolving the referenced action at execution time via an injectable `IHotkeyActionResolver`; the validator cross-checks references against `config.Hotkeys`; no chaining. | `M5-RF-520..524`; the hotkey definition stays the single source of truth, execution reuses `ActionExecutor`, no duplicate/insecure re-expression, and the additive discriminator needs no schema bump. |
| DD-M5-4 | **Fix the add flow at the binding boundary.** Bind the property panel Apply to an explicit idempotent command; make the page/overlay widget collection notifying (or explicitly refresh the host) so `WidgetPageHost` re-materializes on add; keep `AddWidget` rollback semantics. | `M5-RF-530..534`; addresses the host-refresh gap the existing tests miss and makes "add then Apply" observably succeed. |
| DD-M5-5 | **Theme defaults are a WPF-free table, seeded at creation, displayed in the editor.** `ThemeAppearanceDefaults.For(themeId)` returns in-bounds foreground/background/accent/font-size/opacity; `AddWidget` seeds font-size + opacity (+ accent where used) per Q-M5-1; the editor shows theme defaults for null overrides and its chrome uses theme resources. | `M5-RF-540..544`; satisfies "no blanks/hard-coded values" without pinning foreground/background so live theme switching keeps working. |
| DD-M5-6 | **Schema stays v4; no migration.** Delete/sensor/theme add no field; the `hotkey` action is an additive discriminator preserved by the RF-079 unknown-action seam on older builds. | `M5-RF-524/550`; avoids churn and keeps all four `RF-075` load states covered. |
| DD-M5-7 | **Single writer and no new execution surface are non-negotiable.** All M5 persistence uses `IConfigStore`; the hotkey action executes only the referenced, already-validated action through `IActionExecutor`; no elevation. | `M5-RNF-554/556`, `M5-RF-503/533`; preserves M1–M4 invariants. |

## 3. Affected modules / directories

| Workstream | Primary locations | Tasks |
|---|---|---|
| Delete affordance | `TouchPanel/Views/EditOverlay.xaml(.cs)`, `TouchPanel/ViewModels/EditModeService.cs`, `TouchPanel.Tests/Views/EditOverlayTests.cs`, `TouchPanel.Tests/ViewModels/EditModeServiceTests.cs` | T-02 |
| Sensor picker | `TouchPanel/Monitoring/` (new catalog), `TouchPanel/ViewModels/Edit/PropertyEditorViewModel.cs`, `TouchPanel/Views/EditOverlay.xaml`, `TouchPanel.Tests/` | T-03 |
| Hotkey-reference action | `TouchPanel/Config/ActionConfig.cs`, `TouchPanel/Actions/` (new handler + resolver seam), `TouchPanel/Config/Validation/ConfigValidator.cs`, `TouchPanel/ViewModels/Edit/ActionPickerViewModel.cs`, `TouchPanel/Views/EditOverlay.xaml`, `TouchPanel.Tests/` | T-04 |
| Add-widget flow | `TouchPanel/ViewModels/EditModeService.cs`, `TouchPanel/ViewModels/PageViewModel.cs`, `TouchPanel/Views/WidgetPageHost.xaml`, `TouchPanel/Views/EditOverlay.xaml(.cs)`, `TouchPanel.Tests/` | T-01 |
| Theme-aware defaults | `TouchPanel/Theme/` (new defaults table), `TouchPanel/Config/WidgetAppearance.cs`, `TouchPanel/Widgets/WidgetRegistry.cs`, `TouchPanel/ViewModels/Edit/PropertyEditorViewModel.cs`, `TouchPanel/Views/EditOverlay.xaml`, `TouchPanel.Tests/` | T-05 |
| Composition wiring | `TouchPanel/App.xaml.cs` | T-01/T-03/T-04/T-05 |
| Invariants/schema/docs + final validation | `specs/005-edit-ux-fixes/`, `TouchPanel.Tests/Config/SchemaV4GuardTests.cs`, `TouchPanel.Tests/Architecture/LayerPurityTests.cs`, all tests | T-06, T-07 |

## 4. Dependency order and rationale

```text
T-01 Add-widget flow fix (foundation: add/commit/refresh path)
      │
      ├─► T-02 Delete affordance + confirmation (reuses remove path)
      ├─► T-03 Sensor picker (property editor field)
      ├─► T-04 Hotkey-reference action (config/actions/validator/picker)
      └─► T-05 Theme-aware defaults (registry + editor + overlay)
                         │
      T-06 Invariants + schema-v4 guard + docs ──┐
                         ▼
      T-07 Final M5 validation + touch matrix + coverage
```

- **T-01 first** because it fixes the add/commit/refresh path that T-05 (seeding on add) and T-02 (X on the newly selected widget) build upon, and because the page-host refresh fix is the highest-risk change.
- **T-02..T-05** are otherwise independent vertical slices; because T-02/T-03/T-05 all touch `EditOverlay.xaml` and `PropertyEditorViewModel`, they are landed in the order above to keep merges/conflicts small.
- **T-06** locks the schema/invariants and records the docs; **T-07** runs the full suite + manual matrix + coverage.

Each task ends with `dotnet build` and `dotnet test` green; manual checks land with the task or are explicitly deferred to T-07.

## 5. Testing strategy

Aligned with M1–M4: unit tests are the gate; no hardware, drivers, elevation, or network; temp directories isolate the profile; WPF-free purity is enforced; the only WPF-coupled tests use the existing real-BAML/STA pattern (no shown window).

- **Unit:** `SensorCatalog` (aggregate + fake-snapshot per-core/fan + fallback); `PropertyEditorViewModel` sensor-list and theme-aware appearance display; `ActionConfigJson` `hotkey` round-trip + resolver (first-wins, missing → disabled reason, no chaining) + an `ActionExecutor` recording test; page-host add refresh + Apply command + rollback; `ThemeAppearanceDefaults` (three themes, in-bounds, dark == delivered palette) + seeding per theme + precedence/clear-to-inherit; delete-confirmation state machine and persistence.
- **Real-BAML/STA:** `EditOverlayTests` extended for the X and confirm affordances (present, ≥ 40 DIP, selection-scoped) and for the sensor list; no shown window, no desktop state.
- **Integration:** `JsonConfigStore` temp-dir v4 round-trip with a `hotkey` action; app-originated add/delete saves cause no reload (self-write suppression); schema stays v4.
- **Manual matrix (T-07):** MV-M5-01..06 from spec §10.3.
- **Gates:** after every task `dotnet build` + `dotnet test` green; T-07 passes every AC-M5-01..09 or records a documented, human-accepted limitation; a separate reviewer validates without modifying code.

## 6. Migration and compatibility

- **Schema:** `PanelConfig.CurrentSchemaVersion` **stays 4**; the `ConfigMigrationChain` remains `[V1ToV2, V2ToV3, V3ToV4]`; **no** `V4ToV5` (`M5-RF-524/550`, DD-M5-6). The `hotkey` action is an additive discriminator handled by the delivered unknown-action seam on older builds.
- **Widget config:** no new widget field; sensor remains a string key; appearance fields unchanged (theme defaults are seeded values in existing fields).
- **Runtime:** standard user, no UAC; self-contained `win-x64` behavior unchanged; no new watcher/polling.

## 7. Risks and mitigations

| ID | Risk | Mitigation / task anchor |
|---|---|---|
| RM5-1 | The page-host refresh fix regresses live drag/resize/selection or leaks subscriptions | Minimal change (notifying collection or explicit refresh); reuse existing disposal; host-level add test + existing `EditOverlayTests`/`EditModeServiceTests` stay green (T-01). |
| RM5-2 | The add defect root cause differs from H-M5-4 | T-01 reproduces end-to-end at the host level **before** fixing and records the confirmed cause; the expected add flow is covered either way (spec `M5-RF-530`). |
| RM5-3 | Sensor list is empty or stale with no hardware | Fixed aggregate fallback is guaranteed; fake-snapshot and degraded-snapshot tests (T-03, `M5-RF-512`). |
| RM5-4 | A removed hotkey orphans a widget reference | Validator cross-check disables the widget with a reason (non-destructive); resolver returns a clear failure; tests cover missing/duplicate ids (T-04, `M5-RF-523`). |
| RM5-5 | Hotkey action becomes a second execution surface or allows chaining | Single resolver + existing `IActionExecutor`; explicit no-chaining rule; layering/security tests (T-04/T-06, `M5-RNF-556`). |
| RM5-6 | Seeded theme colors break live theme switching | Per Q-M5-1, seed only font-size/opacity (+ accent where used), keep foreground/background inherited; precedence/clear-to-inherit tests (T-05, `M5-RF-543`). |
| RM5-7 | Delete confirmation steals focus or fires the bound action | In-overlay confirm with `Focusable=False`; reuse edit-mode tap suppression; focus/tap tests (T-02, `M5-RF-504`). |
| RM5-8 | Schema accidentally bumped | Guard test asserts v4 and no `V4ToV5` (T-06, `M5-RF-524/550`). |

## 8. Traceability — M5 requirement → task

| Requirement | Task(s) |
|---|---|
| M5-RF-501 (X affordance) | T-02 |
| M5-RF-502 (confirmation + cancel) | T-02 |
| M5-RF-503 (remove + single writer) | T-02 |
| M5-RF-504 (touch/focus/keyboard parity) | T-02, T-06 |
| M5-RF-510 (sensor catalog seam) | T-03 |
| M5-RF-511 (picker replaces free text) | T-03 |
| M5-RF-512 (fallback + unknown preservation) | T-03 |
| M5-RF-513 (Gauge/Sparkline/Bar only) | T-03 |
| M5-RF-520 (hotkey action shape) | T-04 |
| M5-RF-521 (resolve + execute via executor) | T-04 |
| M5-RF-522 (picker offers Hotkey + list) | T-04 |
| M5-RF-523 (missing/duplicate/no-chaining) | T-04 |
| M5-RF-524 (schema stays v4) | T-04, T-06 |
| M5-RF-530 (expected add flow) | T-01 |
| M5-RF-531 (Apply is a command) | T-01 |
| M5-RF-532 (host/overlay immediate refresh) | T-01 |
| M5-RF-533 (single writer + failure isolation) | T-01, T-06 |
| M5-RF-534 (add diagnostics) | T-01 |
| M5-RF-540 (per-theme defaults table) | T-05 |
| M5-RF-541 (seed new widgets on add) | T-01, T-05 |
| M5-RF-542 (theme-aware editor, no blanks) | T-05 |
| M5-RF-543 (precedence/clear-to-inherit) | T-05 |
| M5-RF-544 (all built-in types incl. M3) | T-05 |
| M5-RF-550 (schema/docs) | T-06, T-07 |
| M5-RF-551 (docs/invariants) | T-06, T-07 |
| M5-RNF-551 (testability) | T-01..T-07 |
| M5-RNF-552 (layering) | T-03, T-04, T-05, T-06 |
| M5-RNF-553 (no focus steal/elevation) | T-02, T-06, T-07 |
| M5-RNF-554 (single writer/no reload) | T-01, T-02, T-06 |
| M5-RNF-555 (no idle work/bounded) | T-03, T-05, T-07 |
| M5-RNF-556 (security posture) | T-04, T-06, T-07 |
| **Coverage check** | All 25 `M5-RF` + 6 `M5-RNF` mapped; `RF-072` and the dropped set excluded; no schema bump beyond v4. |
