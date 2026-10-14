# TouchPanel — Implementation Plan 003: Customization (M3)

| Field | Value |
|---|---|
| Plan ID | 003-customization |
| Status | Draft for human approval |
| Date | 2026-10-10 |
| Inputs | `specs/003-customization/spec.md` (reviewed ACCEPTABLE), `specs/001-foundation/{spec,plan}.md`, `specs/002-automation/{spec,plan,tasks}.md`, the delivered M1/M2 implementation in `TouchPanel/` (schema **v3**, 1260 tests green) |
| Scope | RF-046 (additional widget types), RF-061 (hotkey-management UI), RF-084 (themes/accent/icon packs) |
| Out of scope | **RF-072 skipped** (elevation/UAC). All frozen-backlog (RF-040, RF-043 non-appearance, RF-044, RF-045, RF-060, RF-070, RF-071, RF-082, RF-083, RF-091..RF-093, RF-092 settings window) and dropped items stay frozen/dropped and untouched. No M4 work. |
| Task file | `specs/003-customization/tasks.md` |

**M3 requirement set** (spec IDs): `M3-RF-461..469` (RF-046), `M3-RF-611..619` (RF-061), `M3-RF-841..851` (RF-084), `M3-RNF-131..140`.

---

## 1. Architecture and integration with the delivered seams

M3 is a **delta on the delivered Core Release**. It reuses every existing boundary and adds no new architectural pattern (no DI container, no event bus, no plugin system, no repository/generic layering, no cross-platform shims). The Core Release's WPF-free boundaries (`Config/`, `Monitoring/`, `Actions/`, `Input/`) stay WPF-free; P/Invoke stays only under `Interop/`; `Theme/` remains resource dictionaries plus thin hosts.

| M3 area | Delivered seam it extends | M3 approach |
|---|---|---|
| Theme live-swap foundation | `Theme/Colors.xaml`, `Theme/Styles.xaml`, per-type `Widgets/*/*Templates.xaml`, `App.xaml` merged dictionaries | Convert theme references from `StaticResource` to `DynamicResource`, remove the per-dictionary `Colors.xaml` self-merge, split the palette into `Theme/Themes/{Dark,Light,HighContrast}.xaml`, merge the active theme at the application level, and add a small `ThemeManager` for swap-with-wholesale-rollback (M3-RF-851/842/845/848). |
| New widget types | `WidgetConfig`/`WidgetTypes`, `WidgetConfigJsonConverter`, `WidgetRegistry`/`WidgetRegistration`, `WidgetViewModelBase`, `WidgetTemplateKeys`, `IWidgetPaletteSource`, `App.xaml` | One DTO + one VM + one registration + one template dictionary + one converter branch per type. **No host/`PageViewModel`/`WidgetTemplateSelector`/`EditModeService` change** (M3-RF-461). |
| Bar telemetry | public `ISensorSnapshotSource`/`SensorSnapshot` (Gauge's consumer surface) | Consume snapshots like Gauge; never poll, never touch `ISensorHistory`/`PublishOnce`. |
| Clock timing/state | `TimeProvider`; platform managed events (`Microsoft.Win32.SystemEvents.TimeChanged`/`PowerModeChanged`/`SessionSwitch`) behind a testable seam | One boundary-scheduled timer, update-on-change only; an injected clock-state source owns the OS subscriptions and raises events; no polling. |
| Hotkey editor | `Input/HotKeyParser`, `Input/HotKeyService`, `IPanelConfigApplyTarget.ApplyHotKeys`, `App.ApplyHotKeys`, `ViewModels/Edit/ActionPickerViewModel`, `Services/Tray/TrayMenuCommand`/`TrayService` | New focused `HotkeysEditorWindow` + WPF-free editor model; reuse the parser and the existing re-registration hook; add a tray entry. |
| Hotkey persistence | `Services/Config/SettingsChange` (record w/ optional props), `SettingsCoordinator`, `ISettingsService`, `Config/IConfigStore` | Add an optional `SettingsChange.Hotkeys` property; `SettingsCoordinator.BuildUpdated` must preserve every unchanged section; persist through the single writer; apply live. |
| Appearance config | `Config/PanelConfig`, `Config/Validation/ConfigValidator`, `Config/Migrations/MigrationRunner`/`ConfigMigrationChain`, `Config/DefaultConfigGenerator` | Add the `appearance` section, bump `CurrentSchemaVersion` to **4**, add the additive `V3ToV4Migration`; validate/fallback per RF-079. |
| Icon packs | `Config/IconCatalog` (stable id namespace), `ViewModels/WidgetViewModelBase.ApplyAppearance` | Add `IconPackCatalog` (built-in packs, missing id → `default` glyph); refresh live VMs' `IconGlyph` on pack change. |
| Apply pipeline | `Services/Config/PanelConfigApplier`, `IPanelConfigApplyTarget` | Add an isolated `appearance` step + `ApplyAppearance(AppearanceConfig)`; compose with the existing `SettingsCoordinator` path. |
| Composition root | `App.xaml.cs` | Wire the theme manager, icon-pack source, editor window, tray command, and appearance apply step; keep startup order and shutdown participation unchanged. |

## 2. Key design decisions

| ID | Decision | Justification |
|---|---|---|
| DD-M3-1 | **Theme-resource refactor is mandatory and lands first.** Convert `Styles.xaml` + every widget template to `DynamicResource`; remove the per-dictionary `Colors.xaml` self-merge; merge the active theme dictionary at the app level. | Without it a runtime theme swap cannot reach already-realized elements (reviewer F-1); it is the prerequisite for every appearance requirement. |
| DD-M3-2 | **Dynamic-safe per-widget override fallback.** Because WPF forbids `{DynamicResource}` as `Binding.TargetNullValue`, each widget template carries the theme brush via `DynamicResource` on the base element and layers the override through a presence-converter-driven overlay/Style (or the VM resolves the effective brush via `SetResourceReference`). Precedence stays: override present → override; absent → dynamic theme value. | Keeps RF-435/436 overrides working under live themes without an invalid markup pattern (reviewer nit 4). |
| DD-M3-3 | **`ThemeManager` swap-with-wholesale-rollback.** Applying a theme captures the active theme/pack/accent; on any failure it restores the previous theme wholesale (no half-applied palette) and logs/isolates. | Guarantees "never half-themed" (M3-RF-848/F-M3-16). |
| DD-M3-4 | **Appearance precedence:** global theme < global accent < per-widget override; `null` = inherit. Theme = built-in id (`dark` default/`light`/`highContrast`); unknown → `dark` + warning. Accent `#RRGGBB`/`#AARRGGBB`; invalid/blank → theme accent. | Matches the spec (§5.3) and keeps the delivered M2 override semantics intact. |
| DD-M3-5 | **Icon packs** are built-in catalogs sharing one stable icon-id namespace; a missing id falls back to the `default` glyph; ids are never cleared. No external/loaded packs. | M3-RF-844; keeps `WidgetConfig.Icon` pack-independent so no per-widget migration is needed. |
| DD-M3-6 | **Three new widget types, no host changes:** `Clock` (preset `mode` + bounded/culture-pinned custom `format`, escaped-literal-aware seconds detection, boundary-scheduled, OS-event boundary recompute), `ImageButton` (built-in icon + `showLabel` + exactly one action; non-activating), `Bar` (single-value horizontal meter over `ISensorSnapshotSource`). Type-specific fields are **JSON-only** in M3. | M3-RF-461..469; proves the RF-046 extension contract without a plugin system. |
| DD-M3-7 | **Clock OS-event seam.** A testable `IClockStateSource` (default adapter over `SystemEvents.TimeChanged`/`PowerModeChanged`/`SessionSwitch`) raises recompute events; the VM takes the seam so tests use a fake. | Event-driven boundary recompute, no polling (M3-RF-463/nit 5). |
| DD-M3-8 | **`HotkeysEditorWindow`** (new tray entry) rather than extending the frozen `SettingsWindow`. Reuses `HotKeyParser` (≥1 modifier, known key/modifiers, one action), first-wins duplicate detection, the existing re-apply hook, and one aggregated failure notification. | M3-RF-611..619; preserves the RF-092 freeze. |
| DD-M3-9 | **Narrowed re-apply failure semantics (F-2).** The re-apply unregisters the previous set first (M2-RF-595); a post-save total failure may leave no registrations → log + one aggregated notification, no retry. A failure *before* re-apply (save failure) leaves current registrations intact. | Consistent with delivered M2 behavior; no fictional rollback. |
| DD-M3-10 | **`SettingsChange` is a record with optional properties;** `BuildUpdated` preserves every section it is not explicitly changing (hotkey edit ≠ appearance reset and vice versa). | F-3; avoids subtle cross-section data loss. |
| DD-M3-11 | **Schema v4** via additive `V3ToV4Migration` (adds `appearance`; defaults `dark`/`null`/`default`), ordered in `ConfigMigrationChain`. New widget-type discriminators need **no** schema step. | M3-RF-846/469; older v1/v2/v3 files keep loading. |
| DD-M3-12 | **UI-thread + activation rules.** Theme swap, VM refresh, Clock callbacks, and hotkey re-registration are marshalled to the UI thread. Only the user-opened editor window may activate (D-6 analogue); appearance/hotkey apply never activate the panel. | RNF-004/RF-018/M3-RNF-132/137. |
| DD-M3-13 | **Error isolation everywhere.** Theme/pack/clock/bar/hotkey-editor failures degrade locally, log, and leave the panel/tray/config working; no M3 feature takes down the app or prompts for elevation. | M3-RNF-138/135, RF-012/RF-086. |

## 3. Affected modules / directories

| Workstream | Primary locations | Tasks |
|---|---|---|
| Theme-resource refactor, built-in themes, `ThemeManager`, per-widget dynamic fallback, real-BAML test | `TouchPanel/Theme/` (`Styles.xaml`, `Colors.xaml`, `Themes/*`), `Widgets/*/*Templates.xaml`, `App.xaml`, `TouchPanel.Tests/Theme/` | T-01 |
| Clock widget + clock-state seam | `TouchPanel/Config/WidgetConfig.cs`, `Config/Json/WidgetConfigJsonConverter.cs`, `Config/Validation/ConfigValidator.cs`, `ViewModels/ClockWidgetViewModel.cs`, `Widgets/ClockWidget/`, `Services/Time/` (clock-state adapter), `Widgets/WidgetRegistry.cs`, `App.xaml` | T-02 |
| ImageButton widget | same config/converter/validator/registry files, `ViewModels/ImageButtonWidgetViewModel.cs`, `Widgets/ImageButtonWidget/` | T-03 |
| Bar widget | same config/converter/validator/registry files, `ViewModels/BarWidgetViewModel.cs`, `Widgets/BarWidget/` | T-04 |
| Widget extensibility + JSON forward-compat + palette + no-host-change test | `Widgets/WidgetRegistry.cs`, `Config/Json/WidgetConfigJsonConverter.cs`, `ViewModels/Edit/` (palette), architecture tests | T-05 |
| Hotkey editor model + persistence | `Input/HotkeyEditorModel.cs`, `Services/Config/SettingsCoordinator.cs` (`SettingsChange.Hotkeys`, `BuildUpdated`), `TouchPanel.Tests/Input/` | T-06 |
| Hotkey editor window + tray + live re-apply | `ViewModels/Hotkeys/`, `Views/Hotkeys/`, `Services/Tray/`, `App.xaml.cs` | T-07 |
| Schema v4 + appearance config/migration/validation | `Config/PanelConfig.cs`, `Config/Migrations/V3ToV4Migration.cs`, `Config/Migrations/ConfigMigrationChain.cs`, `Config/Validation/ConfigValidator.cs`, `Config/DefaultConfigGenerator.cs` | T-08 |
| Accent + icon packs + precedence | `Config/IconCatalog.cs` → `Config/IconPackCatalog.cs`, `Theme/ThemeManager.cs`, `ViewModels/WidgetViewModelBase.cs`, `App.xaml.cs` | T-09 |
| Appearance apply pipeline + selection UI | `Services/Config/PanelConfigApplier.cs`, `Services/Config/SettingsCoordinator.cs` (`SettingsChange.Appearance`), `App.xaml.cs`, `Views/Hotkeys/` (Appearance section) | T-10 |
| Final verification | all (fixes within M3 scope only) | T-11 |

## 4. Dependency order and rationale

```text
T-01 theme-resource refactor + themes + ThemeManager ─────────────┐
      (DynamicResource conversion, self-merge removal, rollback)  │
   ├─► T-02 Clock ─┐                                              │
   ├─► T-03 ImageButton ─┼─► T-05 widget extensibility/JSON/palette│
   └─► T-04 Bar ───┘                                              │
   T-06 hotkey editor model/persistence ─► T-07 editor window+tray+re-apply
   T-08 schema v4 + appearance config/migration ─► T-09 accent+icon packs ─► T-10 appearance apply pipeline + selection UI
                                                                  │
   all ──────────────────────────────────────────────────────────► T-11 final verification
```

- **T-01 first** because live theming is impossible before the `DynamicResource` conversion and self-merge removal (reviewer F-1); it underpins every appearance task.
- **T-02/T-03/T-04 (widgets)** follow the refactor so their new templates are authored with `DynamicResource` from day one (no rework). They are independent of each other and of the hotkey/schema streams.
- **T-05** is the cross-cutting proof of the RF-046 extension contract and JSON forward-compatibility across all three new types.
- **T-06 → T-07 (hotkey editor)** is largely independent of the widget stream; it depends only on the existing hotkey seams and adds the `SettingsChange.Hotkeys` threading + `BuildUpdated` preserve rule.
- **T-08 → T-09 → T-10 (schema v4 + appearance apply)** lands late per the agreed ordering: the config model/migration first, then accent/icon-pack resolution, then the apply-pipeline step and the selection UI that tie theme + accent + pack together live.
- **T-11** runs the automated suite, the manual matrix, real-BAML live-theme checks, performance/soak, and the Core-Release regression.

Each task ends with `dotnet build` and `dotnet test` green; manual checks are performed when the task lands or are explicitly deferred to T-11.

## 5. Risks and mitigations

| ID | Risk | Mitigation / task anchor |
|---|---|---|
| RM3-1 | A theme swap fails or leaves mixed keys | `ThemeManager` swap-with-wholesale-rollback (DD-M3-3); injected-failure test + real-BAML rollback assertion (T-01, AC-M3-12). |
| RM3-2 | `{DynamicResource}` cannot be a `Binding.TargetNullValue`, breaking per-widget overrides after conversion | Dynamic-safe base+overlay/VM-resolved mechanism (DD-M3-2); AC-M3-12 includes an overridden widget (T-01). |
| RM3-3 | An icon id is missing from the active pack | Stable-id fallback to the `default` pack glyph; id never cleared (DD-M3-5; T-09, F-M3-05). |
| RM3-4 | Clock custom format injection / bad format / timezone-sleep drift | Length-capped, culture-pinned, trial-format with fallback; escaped-literal-aware detection; OS-event boundary recompute (DD-M3-7; T-02). |
| RM3-5 | Hotkey re-apply conflicts / lost registrations | Reuse M2 skip+aggregate+no-retry; narrowed F-M3-15 semantics (DD-M3-9; T-06/T-07). |
| RM3-6 | `SettingsChange` edit resets an unrelated section | `BuildUpdated` preserve-every-unchanged-section rule + tests (DD-M3-10; T-06/T-10). |
| RM3-7 | Schema v4 migration breaks existing configs | Ordered additive `V3ToV4Migration`, validate-after-step, backup, source never overwritten (T-08, AC-M3-02). |
| RM3-8 | UI-thread violation or accidental activation | All UI-affine work marshalled; only the user-opened editor activates (DD-M3-12; T-01/T-07/T-10). |
| RM3-9 | WPF-free layering eroded by new logic | Editor model + appearance/theme resolution logic stay WPF-free; layer-purity test extended (T-05/T-06/T-09). |
| RM3-10 | Real-BAML test flakiness/STA requirements | Isolated STA test host with deterministic resource loading (T-01). |

## 6. Testing strategy

Aligned with M1 §11 / M2 §10: unit tests are the gate; hardware/network/elevation are excluded; temp directories isolate the profile; WPF-free layer purity is enforced. No UI-automation framework is added.

- **Unit tests (no hardware/drivers/elevation/network):** theme/accent/icon-pack resolution + fallbacks + precedence; `ThemeManager` swap/rollback; v3→v4 (and v1/v2→v4) migration fixtures with failing-step/intact-source cases; `AppearanceConfig` round-trip; Clock mode/format (escaped-literal aware, `t`/`tt` does not force per-second) + cadence decision (fake `TimeProvider`) + boundary recompute on fake clock-state events + update-on-change; Bar mapping/clamp/unavailable/unknown-sensor/no-poll/update-on-change; ImageButton single-action/disabled-when-invalid; hotkey editor list/add/edit/remove + `HotKeyParser` validation + duplicate first-wins + `SettingsChange.Hotkeys` save-through-fake-store + `BuildUpdated` preserve-every-section; registry/palette; architecture (layer purity extended to new `Config`/`Input` logic, P/Invoke confined to `Interop/`).
- **Real-BAML live-theme test:** an STA test loading the actual `Theme/Styles.xaml` + a widget template dictionary, realizing an override-less and an overridden widget, swapping the active theme dictionary at runtime, and asserting the realized brushes update/retain per precedence plus wholesale rollback on injected failure (AC-M3-12).
- **Integration tests (no real hardware):** `JsonConfigStore` temp-dir migrate/validate/backup and appearance-section write; `SettingsCoordinator` with a fake store/applier for hotkey and appearance edits, including section preservation; `HotKeyService` re-registration against `FakeHotKeyRegistration`.
- **Manual verification matrix (T-11):** MV-M3-01 widgets, MV-M3-02 hotkeys, MV-M3-03 appearance, MV-M3-04 focus matrix, MV-M3-05 performance/soak, MV-M3-06 Core-Release regression.
- **Gates:** after every task `dotnet build` + `dotnet test` green; T-11 passes every AC-M3-01..13 or records a documented, human-accepted limitation; a separate reviewer validates without modifying code.

## 7. Migration and compatibility

- **Schema:** `PanelConfig.CurrentSchemaVersion` **3 → 4** via additive `V3ToV4Migration` in the ordered `ConfigMigrationChain`; `MigrationRunner` remains the generic engine and validates the migrated result before persistence. Older v1/v2/v3 files still load; a failing step follows the invalid/corrupt path and never overwrites the source.
- **Widget types:** new discriminators are additive values, not schema structure. A same-version build lacking a type preserves it as an unknown-type placeholder (RF-079); a lower-version build reading a v4 file takes the whole-file future-schema path (RF-075.4).
- **Persistence:** the `appearance` and `hotkeys` edits go through the existing single writer; no file watcher/polling is added.
- **Runtime:** standard user, no UAC (RF-086); self-contained `win-x64` behavior unchanged.

## 8. Traceability — M3 requirement → task

| Requirement | Task(s) |
|---|---|
| M3-RF-461, M3-RF-468, M3-RF-469 | T-05 (with T-02/T-03/T-04) |
| M3-RF-462, M3-RF-463 | T-02 |
| M3-RF-464, M3-RF-465 | T-03 |
| M3-RF-466, M3-RF-467 | T-04 |
| M3-RF-611, M3-RF-612, M3-RF-613 | T-06 (surface in T-07) |
| M3-RF-614 | T-07 |
| M3-RF-615 | T-06 |
| M3-RF-616, M3-RF-617, M3-RF-618 | T-07 |
| M3-RF-619 | T-06, T-07 |
| M3-RF-841, M3-RF-846, M3-RF-847 | T-08 |
| M3-RF-842, M3-RF-845, M3-RF-851 | T-01 |
| M3-RF-843, M3-RF-844 | T-09 (with T-01) |
| M3-RF-848 | T-01 (swap/rollback) + T-10 (pipeline) |
| M3-RF-849 | T-01 (theme values) |
| M3-RF-850 | T-10 |
| M3-RNF-131, M3-RNF-139 | T-02, T-04 |
| M3-RNF-132 | T-01, T-07, T-10 |
| M3-RNF-133 | T-01..T-10 (per-task tests) |
| M3-RNF-134 | T-01, T-05, T-06, T-09 |
| M3-RNF-135 | T-02, T-08, T-09 |
| M3-RNF-136 | T-01 |
| M3-RNF-137 | T-07, T-10 |
| M3-RNF-138 | T-01, T-02, T-04, T-06, T-07, T-10 |
| M3-RNF-140 | T-01, T-02, T-03, T-04 |
| **Coverage check** | 28 `M3-RF` + 10 `M3-RNF` all mapped; **RF-072 excluded**; no frozen/dropped requirement implemented. |
