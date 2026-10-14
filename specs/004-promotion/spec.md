# TouchPanel — Specification 004: Promotion & Polish (M4)

| Field | Value |
|---|---|
| Spec ID | 004-promotion |
| Status | Draft for human approval (M4 — Specification) |
| Date | 2026-10-10 |
| Phase | M4 — Specification |
| Author | Planner agent |
| Inputs | `specs/001-foundation/spec.md` (M1 + Core-Release freeze, `D-24`), `specs/002-automation/spec.md` / `tasks.md` (M2, `D-M2-8`), `specs/003-customization/*` (M3), and the delivered implementation in `TouchPanel/` (schema **v4**, 1480 tests green; reviewer ACCEPTABLE for M3) |
| Scope | **Un-freeze and officially support the already-implemented backlog**, resolve the three M3 minor findings, and add the small consistency/polish needed for coherent support. `RF-072` stays skipped; dropped items stay dropped. |
| Out of scope | `RF-072` (optional elevated companion — skipped, needs UAC), and the dropped set (RF-042/054/055/056/087, the `delay` action, shell-open of files/folders/URLs). No new feature is invented. |
| Next steps | `plan.md` and `tasks.md` for M4 (not created in this phase) |

### Why "Promotion & Polish"

The 2026-10-09 freeze (`001 D-24`, `002 D-M2-8`) tagged many requirements as **frozen backlog even though their code was already implemented and tested during M2**. M3 un-froze three of them (`RF-046`, `RF-061`, `RF-084`). M4 does the same for the **remaining implemented backlog**: it promotes those features from "unsupported code in the build" to **officially supported and maintained**, verifies them against their existing M2 sub-requirements, fixes a few concrete gaps, and resolves the M3 minor findings — while keeping every requirement ID stable and changing no M1/M2/M3 semantics. The name "Promotion & Polish" names that center of gravity: promote the working backlog, polish the seams.

> **Promotion note (2026-10-10).** The human has **un-frozen and approved M4** as "Promote & Polish". The relevant requirements were marked `[FROZEN — backlog 2026-10-09]` in 001 (`D-24`) and 002 (`D-M2-8`); this document is the authoritative M4 scope for them. The Core Release and M3 features are unchanged. `RF-072` remains frozen/skipped. This promotion is recorded in 001 as **`D-25`**.

---

## 1. Goal

M1 proved the architecture and the M2 Core Release / M3 customization are delivered. During M2 a large set of features was **fully implemented and unit-tested but declared unsupported**. M4 must make those features **honestly supported** — reachable, verified, documented, and maintained — without disturbing any invariant M1/M2/M3 proved:

1. **Promote the implemented backlog.** Slider, the non-appearance full edit UI, grid snap + alignment, toggle persistence, local shortcuts, backend re-init, configurable interval, hot reload, import/export, page-switching UI, the settings window, and the monitor picker become supported. Because they are already built, M4's work is **verification + gap-fix + a "supported" declaration**, not greenfield.
2. **Resolve the M3 minor findings.** (a) the Sparkline filled area must follow the theme/accent instead of a hard-coded color; (b) Clock custom-format validation must use the same display culture the widget uses (or the divergence is documented); (c) `AppearanceConfig` moves out of `PanelConfig.cs`.
3. **Coherent support surface.** Each promoted feature is reachable from exactly one sensible surface; there are no duplicate or competing editors.
4. **Honest scope boundary.** The supported set becomes "everything implemented except `RF-072` (skipped) and the dropped set". Freeze documentation is updated accordingly.
5. **Same discipline as M1/M2/M3.** WPF-free core, `Interop/` isolation, no elevation/UAC, bounded resources, untrusted configuration treated as data, no idle work, no focus stealing, and a deterministic test suite that touches no hardware.

**Success for M4** means: every previously-frozen-but-implemented feature is verified against its M2 sub-requirements and declared supported; the three M3 findings are fixed and tested; the settings/hotkey/edit surfaces host the promoted options without duplication; the full suite (1480 + new tests) is green; and the freeze documentation reads "supported = everything implemented except `RF-072` and the dropped set".

## 2. Scope

**In scope (promoted to supported; already implemented in M2):**

- **Widgets/editing:** `RF-040` Slider (`M2-RF-401..406`); `RF-043` non-appearance full edit UI (`M2-RF-430/431/432/433/434` and the persistence/isolation parts `M2-RF-437..440`); `RF-044` grid snap + alignment (`M2-RF-441..446`); `RF-045` toggle-state persistence (`M2-RF-451..455`).
- **Input:** `RF-060` local shortcuts (`M2-RF-601..604`).
- **Monitoring:** `RF-070` throttled backend re-initialization (`M2-RF-701..704`); `RF-071` configurable polling interval (`M2-RF-711..714`).
- **Configuration:** `RF-082` hot reload (`M2-RF-821..830`); `RF-083` import/export (`M2-RF-831..835`).
- **Deferred UI:** `RF-091` page-switching UI (`M2-RF-911..915`); `RF-092` settings window (`M2-RF-921..925`); `RF-093` monitor picker (`M2-RF-931..934`).

**In scope (M3 minor findings + polish):**

- The three M3 reviewer findings (`M4-RF-950..952`).
- UI cohesion, the schema check, the promotion declaration, and the freeze-documentation update (`M4-RF-960..964`).

**Out of scope (unchanged):**

- **`RF-072` optional elevated companion — skipped** (needs UAC/elevation; cannot run unattended). Remains frozen `[Later]`.
- **Dropped (not deferred):** `RF-042` repeat-while-held, `RF-054` macros/sequences, `RF-055` macro cancellation, `RF-056` HTTP action, `RF-087` HTTP safety constraints, the `delay` action, and shell-open of files/folders/URLs. These stay dropped with IDs retained.
- `RF-046` (additional widget types), `RF-061` (hotkey-management UI), and `RF-084` (themes/accent/icon packs) are **already supported by M3** and are **not re-opened**; M4 only touches their cohesion (e.g. where appearance is hosted).

**In scope as integration only:** extending existing seams (`EditModeService`/`PropertyEditorViewModel`/`ActionPickerViewModel`/`WidgetPaletteSource`, `GridSnap`/`AlignmentGuides`, `ToggleStatePersistence`/`StateStore`, `HotKeyService`/local-shortcut handling, `MonitoringService`/backend re-init/interval, `ConfigReloadController`/`ReloadDecision`, import/export on `JsonConfigStore`, `PageSwitcherViewModel`/`PageSwitchCoordinator`, `SettingsWindow`/`SettingsViewModel`, `MonitorPickerViewModel`/`IMonitorEnumerator`, `ThemeManager`/`AppearanceForegroundConverter`, `ConfigValidator`). No M1/M2/M3 behavior is redefined.

**Focus-preservation scope.** The `RF-018` guarantee is preserved by every promoted feature. Normal-mode panel behavior is unchanged; the settings window and the hotkeys/appearance editor are ordinary activating windows (deliberate user actions, the `D-6` analogue); all other promoted features never call `SetForegroundWindow`/`Activate`. The exclusive-fullscreen limitation from 001 §2 still applies and is documented, not solved.

## 3. Non-goals

- **No new product features.** M4 promotes, verifies, gap-fixes, and documents; it does not invent requirements.
- **No renumbering or milestone retagging** of any `RF-0xx`/`RNF-xxx`. Only the freeze/support tag changes.
- **No schema bump beyond v4.** The promoted features' fields already exist in v4 through the `v1→v2→v3→v4` chain; grid size stays a documented session-only default (no new field).
- **No plugin system, event bus, mediator, DI container, or repository layering.**
- **No elevation/UAC** (`RF-086`); `RF-072` remains skipped.
- **No new execution surface.** Promoted features reuse existing modelled data and the existing safe-launch path (`RF-049`); configuration remains untrusted (`RF-081`).
- **No UI-automation framework** and no WPF-presentation tests beyond the existing real-BAML pattern (M3 `AC-M3-12`).
- **No redesign of the edit/settings surfaces**; only minimal cohesion wiring.
- **No reopening of M3's design boundaries** except where this spec explicitly reverses one (the `M3-RF-468`/`D-M3-8` JSON-only property editor — see `M4-RF-432`/`D-M4-2`).

## 4. Relationship to 001/002/003 and requirement ID rules

This document **does not renumber, restate, or change the semantics of any 001/002/003 requirement.** It adds testable sub-requirements in the `M4-RF-###` / `M4-RNF-###` namespace, each carrying a **Maps to** reference to its base/001 ID. For the promoted features, the **normative behavior** is the existing M2 sub-requirement set; M4 adds the verification, gap-fix, and support declarations on top.

Rules (mirroring 002 §4 / 003 §4):

- Base IDs (`RF-040`…`RF-093`, `RNF-xxx`) keep their 001 meaning and milestone tag.
- `M4-RF-40x` = RF-040, `M4-RF-43x` = RF-043, `M4-RF-44x` = RF-044, `M4-RF-45x` = RF-045, `M4-RF-60x` = RF-060, `M4-RF-70x` = RF-070, `M4-RF-71x` = RF-071, `M4-RF-82x` = RF-082, `M4-RF-83x` = RF-083, `M4-RF-91x`/`92x`/`93x` = deferred UI. Numbers are three-digit and unique within the document.
- `M4-RF-95x` = the M3 minor findings; `M4-RF-96x` = cohesion/schema/promotion cross-cutting requirements.
- `M4-RNF-14x` are M4 execution constraints that refine M1's RNF-002/003/004/006/008/010/012/013/014 for the promotion work.
- A base requirement is *fully covered* when all of its sub-requirements are accepted. No `[M1]`/`[M2]`/`[M3]` requirement is retagged or moved.

## 5. Functional requirements

Conventions: **Must** = verifiable. "Implemented (M2)" means the code and its unit tests already exist in the build; M4 does not rewrite it, it verifies and declares it. "Gap-fix" means a concrete change M4 must make. "UI thread" = the WPF dispatcher thread. Values marked *(default)* are chosen defaults the human can override at approval. All configuration is untrusted input (`RF-081`).

### 5.1 Widgets and editing

#### RF-040 — Slider widget

- **M4-RF-401 [Maps RF-040] Supported declaration & verification.** The Slider is **implemented (M2)** (`M2-RF-401..406`; `SliderWidgetTests`, `SliderTemplateTests`, `PropertyEditorTests`). M4 requires it be verified, unchanged, against those sub-requirements and declared **supported/maintained**: range/step/orientation/initial-value normalization; continuous drag with throttled value-change emission (at most one action per `100 ms` *(default)* or per `step`, whichever is less frequent); exactly one action per completed interaction; the `volume Set` range→`0–100` mapping; a non-numeric/unbound action disables the widget with an error indicator; horizontal/vertical orientation correct at 100%–200% scaling; ≥ 40 DIP touch target; and the ~2 s transient failure state.
- **M4-RF-402 [Maps RF-040, RF-043] Palette and property-editor reachability.** The Slider must appear in the edit-mode palette and its type-specific fields (`minimum`/`maximum`/`step`/`orientation`/initial `value`) must be editable from the property editor, with the **configured initial value** (not the live dragged value, per `D-M2-6`) being what is validated and persisted. This makes the promoted Slider coherent with the promoted edit UI.

#### RF-043 — Full edit UI (non-appearance)

- **M4-RF-430 [Maps RF-043] Palette add/remove/duplicate + unknown-type preservation — verification.** The add/remove/duplicate operations are **implemented (M2)** (`M2-RF-430/431/432`; `EditModeServiceTests`, `EditOverlayTests`, `PropertyEditorTests`). M4 requires verification, unchanged: built-in types only in the palette; add creates a generated unique id, a deterministic default geometry, and a first-free-position placement (with the documented 16 DIP cascade fallback); remove deletes immediately and on save; duplicate clones the full config under a new id with a fixed 16 DIP offset and selects the clone; unknown widget types are preserved with their raw JSON, rendered as a disabled placeholder, removable, and never offered for add (`RF-079`). All are dirty-tracked.
- **M4-RF-431 [Maps RF-043] Property editor + action picker/browse — verification.** The property editor and action picker are **implemented (M2)** (`M2-RF-433/434`; `PropertyEditorTests`, `ActionPickerTests`). M4 requires verification, unchanged: property inputs validated with the same rules as the config validator (invalid blocked/clamped with visible feedback, never written to the in-memory config); the action picker offers the M1 types plus `sendKeys`; the run-command/launch ("open program") action exposes an executable **file browse** while arguments and working directory stay discrete editable fields; an invalid/unknown selection leaves the widget disabled with a visible error indicator and a log entry (`RF-047`), never dropping it.
- **M4-RF-432 [Maps RF-043, M3-RF-468, D-M3-8] Gap-fix — property editor covers all built-in widget types.** Now that the property editor is supported, the `M3-RF-468`/`D-M3-8` **JSON-only** limitation for the M3 types is reversed: the property editor must expose the type-specific fields of **Clock** (`mode`, `format`), **ImageButton** (`showLabel`, `icon`), and **Bar** (`sensor`, `minimum`, `maximum`, `showValue`) in addition to the M1/M2 types. Fields are validated by the same `ConfigValidator` rules (invalid values clamped/blocked with visible feedback, never persisted as invalid); the ImageButton icon is chosen from the **built-in** icon catalog / active icon pack only (`RF-081`); no external image path is accepted. JSON round-trips and unknown-type preservation are unchanged. *(Chosen default per `D-M4-2`; the alternative — keep JSON-only and document it — is overridable.)*
- **M4-RF-433 [Maps RF-043, M2-RF-437..440] Persistence, touch, isolation — verification.** The persistence/isolation parts (`M2-RF-437..440`) are **implemented (M2)**. M4 requires verification, unchanged: all edit operations mark the layout dirty and persist **only** through the existing `IConfigStore` single-writer path on edit-mode exit/save (or shutdown); no file watcher and no partial write (`RF-077`); a failed save keeps in-memory state and notifies; all edit interactions are touch-operable (no physical keyboard required); a tap in edit mode selects/moves and **never** fires the bound action (`M2-RF-439`); a failure building the palette/property editor/icon catalog degrades that affordance (logged, disabled) without taking the panel or edit mode down.

#### RF-044 — Grid snap and alignment aids

- **M4-RF-440 [Maps RF-044] Supported declaration & verification.** Grid snap, alignment guides, and the snap-aware nudge are **implemented (M2)** (`M2-RF-441..446`; `GridSnapTests`, `AlignmentGuidesTests`, `EditSnapTests`). M4 requires verification, unchanged: snapping applies to drag-move and resize before page-clamp + 40 DIP minimum; a widget smaller than a cell snaps its origin only; arrow-key nudge moves by `gridSize` with snap on and by 1 DIP with snap off; alignment guides appear within a fixed 4 DIP threshold and snap-to-alignment when enabled; guides render only during the drag and are not persisted; free (unsnapped) positioning remains available via an explicit toggle and/or modifier; grid/alignment math is pure and unit-tested with hostile/non-finite input normalized.
- **M4-RF-441 [Maps RF-044] Documented session-only grid/snap boundary.** `gridSize` (bounded `2–64` DIP, default `8`) and the snap toggle are **session-only runtime state** (not part of `panel.json`), matching `M2-RF-441`'s "per-session snap toggle". M4 requires this be **documented as intentional** and that no schema field is added; a value set during a session is honoured until restart and otherwise defaults to `8`/snap-on, never persisted and never a load error. *(Chosen default per `D-M4-3`; persisting grid size would require a v5 bump and is overridable.)*

#### RF-045 — Toggle-state persistence

- **M4-RF-450 [Maps RF-045] Supported declaration & verification.** Toggle persistence is **implemented (M2)** (`M2-RF-451..455`; `ToggleStateTests`, `StateStoreTests`). M4 requires verification, unchanged: a per-Toggle `persistState` flag (default `false`, preserving `D-15`); when `true` the on/off value is restored after restart; persisted states live in the **runtime-state store** (`state.json`) under a `toggleStates` object keyed `"<pageId>/<widgetId>"`, **never** in `panel.json`; writes are atomic and debounced, failed writes are non-fatal and retried; stale keys are ignored/pruned; an absent/empty/shape-mismatched map is non-fatal and regenerated; flipping a persisted toggle never marks config dirty and never triggers a reload.
- **M4-RF-451 [Maps RF-045, RF-082] No reload-loop coupling — verification.** Flipping a persisted toggle writes only `state.json`; the reload watcher is filtered to `panel.json` and app-originated writes are suppressed, so no reload attempt occurs (`M2-RF-455/821/823`).

### 5.2 Input

#### RF-060 — Local shortcuts

- **M4-RF-600 [Maps RF-060] Supported declaration & verification.** Local shortcuts are **implemented (M2)** (`M2-RF-601..604`; `LocalShortcutsTests`, `EditModeServiceTests`). M4 requires verification, unchanged: local shortcuts are handled **separately** from global `RegisterHotKey` registrations and never fire when the panel does not have focus / edit mode is not active; the fixed edit-mode set (Esc exit, arrow nudge, remove selected, duplicate selected, toggle snap) works; normal-mode operation remains fully usable without keyboard focus and does not depend on local shortcuts; outside their context they are inert and never change the no-activate behavior.
- **M4-RF-601 [Maps RF-060, RF-061] Fixed-set boundary.** Local shortcuts remain an **app-defined fixed set**, not user-configurable; `RF-061` governs only **global** hotkeys. M4 requires this boundary be documented (no local-shortcut editor is introduced).

### 5.3 Monitoring

#### RF-070 — Backend re-initialization

- **M4-RF-700 [Maps RF-070] Supported declaration & verification.** Throttled backend re-init is **implemented (M2)** (`M2-RF-701..704`; `BackendReinitTests`, `ThrottledFailureLoggerTests`). M4 requires verification, unchanged: when the LibreHardwareMonitor adapter failed/degraded, the app may retry initialization throttled (at most once per `5 min` *(default)*), logged at a throttled rate; a successful retry re-marks the `monitoring` subsystem restored and resumes snapshots/history; retry is bounded, never blocks the loop or UI thread, never requests elevation, and degrades gracefully if it cannot recover; failure remains non-fatal.

#### RF-071 — Configurable polling interval

- **M4-RF-710 [Maps RF-071] Supported declaration & verification.** A configurable interval is **implemented (M2)** (`M2-RF-711..714`; `IntervalTests`, `SettingsViewModelTests`). M4 requires verification, unchanged: interval bounded to `0.5–5 s` (default `1 s`) with out-of-range values clamped and warned; changing it on hot reload restarts/adjusts the loop without leaving two loops running and recomputes ring capacity; the effective polling rate equals the configured interval and data-driven UI updates remain at most once per snapshot; interval changes are applied on the background path or marshalled safely.
- **M4-RF-711 [Maps RF-071, RF-092] Settings-window hosting.** The interval must be editable from the settings window (hosted under Telemetry) and applied live. *(Verified surface per `M4-RF-960`/`D-M4-7`.)*

### 5.4 Configuration

#### RF-082 — Hot reload

- **M4-RF-820 [Maps RF-082] Supported declaration & verification.** Hot reload is **implemented (M2)** (`M2-RF-821..830`; `ConfigReloadControllerTests`, `ConfigReloadPipelineTests`, `AppWriteSuppressionTests`, `ReloadDecisionTests`). M4 requires verification, unchanged: a debounced `FileSystemWatcher` filtered to the effective configuration file **`panel.json` only** (no polling; temp/backup/state/recovery/corrupt writes generate no reload event); validate-then-apply as a whole via an atomic in-memory swap (any parse/migration/validation failure keeps the running configuration, logs, notifies, and applies nothing — `F-21`); all app-originated writes (config store **and** state store) are suppressed so the app never self-reloads; an older-but-supported schema migrates in memory and validates before applying; a future schema is not interpreted and the file is not written; an invalid external edit keeps the running configuration and never overwrites the user's file; watcher failures degrade gracefully and are re-established; notification spam is throttled; the watcher/debounce are disposed on shutdown.
- **M4-RF-821 [Maps RF-082, RNF-006] Pure reload-decision component — verification.** The event→decision semantics (`ignore` / `debounce-and-wait` / `reload-from-file`) must remain a **pure, injectable component** (`M2-RF-830`) testable without the file system; the OS watcher and store are thin adapters. M4 requires this separation be verified by `ReloadDecisionTests`.

#### RF-083 — Import / export

- **M4-RF-830 [Maps RF-083] Supported declaration & verification.** Import/export is **implemented (M2)** (`M2-RF-831..835`; `ImportExportTests`, `SettingsViewModelTests`). M4 requires verification, unchanged: export writes the JSON configuration (including `schemaVersion`) to a user-chosen location; import fully validates the chosen file (parse → migrate older supported → validate) **before** applying, backs up the current configuration first, changes nothing on failure, and on success replaces the effective configuration, persists atomically, and triggers the same apply pipeline as a hot reload; imported content is untrusted (`RF-081`); only file paths and result summaries are logged, never configuration content (`RF-085`); file I/O runs off the UI thread with failures reported without crashing or partial writes.
- **M4-RF-831 [Maps RF-083, RF-092] Settings-window hosting.** Import/export must be reachable from the settings window (Configuration section) with a file picker. *(Verified surface per `M4-RF-960`/`D-M4-7`.)*

### 5.5 Deferred user-interface features

#### RF-091 — Page-switching UI

- **M4-RF-910 [Maps RF-091] Supported declaration & verification.** Page switching is **implemented (M2)** (`M2-RF-911..915`; `PageSwitcherViewModelTests`, `PageSwitcherViewTests`, `PageSwitchCoordinatorTests`, `PageSwitchIntegrationTests`). M4 requires verification, unchanged: a touch-friendly in-panel affordance switches the active page in normal mode; switching updates `activePageId` with the M1 validation/fallback rules and persists it atomically through the store; an invalid `activePageId` falls back to the first page and logs; switching works without activating the panel or changing the foreground window; the outgoing page's widget view models (and subscriptions) are disposed and the incoming page built, with rebuild failure leaving the panel without widgets but stable; the page-switch save is a self-save that must not trigger a hot reload.
- **M4-RF-911 [Maps RF-091, RF-018] Normal-mode reachability and no focus steal.** The affordance must be reachable in normal (non-edit) mode and must not require keyboard focus; switching must not move the foreground window.

#### RF-092 — Settings window

- **M4-RF-920 [Maps RF-092] Supported declaration & verification.** The settings window is **implemented (M2)** (`M2-RF-921..925`; `SettingsWindowTests`, `SettingsViewModelTests`, `SettingsCoordinatorTests`). M4 requires verification, unchanged: a tray-accessible, ordinary activating window (the `D-6` analogue) edits `alwaysOnTop`, start-with-Windows, the telemetry interval (`RF-071`), and the target monitor (via `RF-093`), plus import/export (`RF-083`) and read-only diagnostics (config path, current target identity, schema version); inputs are validated with the config-validator rules and saved atomically through the existing store; valid changes apply live where possible without restart (start-with-Windows reuses `RF-003/RF-004`); opening/closing never changes the panel's no-activate behavior; failures building/showing the window are isolated and logged.
- **M4-RF-921 [Maps RF-092, RF-061, M3-RF-850] Cohesion — no duplicate editors.** The settings window must **not** edit global hotkeys or appearance; those remain in the dedicated hotkeys/appearance editor (`RF-061`/`RF-084`). Conversely, the hotkeys editor must not duplicate the settings window's controls. Each promoted concern is hosted by exactly one surface (`M4-RF-960`).

#### RF-093 — Monitor picker

- **M4-RF-930 [Maps RF-093] Supported declaration & verification.** The monitor picker is **implemented (M2)** (`M2-RF-931..934`; `MonitorPickerViewModelTests`, `MonitorSelectionServiceTests`). M4 requires verification, unchanged: it lists all discovered monitors with their stable identity fields (EDID hardware id when available, device path, friendly name), resolution, position, and primary flag via the existing `IMonitorEnumerator`/`MonitorDescriptor`; selecting a monitor writes the identity block into `monitor.match` and applies placement without restart or hand-editing JSON; the current target is highlighted and an unavailable target is shown clearly so the user can reselect; the fallback policy (none/primary) can be chosen with the M1 warning semantics; matching uses the same deterministic priority/tiebreak semantics as `MonitorMatcher`, and a monitor with no stable fields warns before saving.
- **M4-RF-931 [Maps RF-093, RF-092] Settings-window hosting.** The monitor picker must be hosted (embedded) in the settings window as its single surface. *(Verified surface per `M4-RF-960`/`D-M4-7`.)*

### 5.6 M3 minor findings (resolved in M4)

- **M4-RF-950 [Maps RF-041/M2-RF-411, RF-084/M3-RF-843/M3-RF-851] Sparkline filled area follows theme/accent.** *Gap-fix.* The Sparkline "filled" style currently uses a hard-coded `#553D7EFF` (`SparklineWidgetTemplates.xaml`, `SparklineFillBrush`). It must instead derive from the **effective accent** with the same precedence as the line (`RF-436` per-widget accent override wins → else global accent (`M3-RF-843`) → else the active theme's accent), applied at a documented translucency (the current ~`0x55` alpha is the default). The fill must follow a **live** theme/accent swap on already-realized elements (consistent with the `M3-RF-851` `DynamicResource` conversion) and must never be a fixed literal. A theme/accent that is unavailable falls back to the theme accent fill without crashing. *(Chosen default per `D-M4-4`; documenting the fixed color as intentional instead is overridable.)*
- **M4-RF-951 [Maps RF-046/M3-RF-462/M3-RF-463] Clock validation/display culture alignment.** *Gap-fix.* `ConfigValidator.ValidateClock` validates a custom format with `CultureInfo.InvariantCulture` while `ClockWidgetViewModel` resolves it with the pinned **display culture**. A format whose validity is culture-dependent could therefore be accepted at load and rejected at runtime (or vice versa). M4 must introduce a **single canonical Clock culture source** (default: the user's current culture captured once — the same value the view model pins per `M3-RF-462`) used by **both** load-time validation and runtime resolution, so validator and widget agree; tests pin the culture explicitly. **The source is WPF-free and lives in `Services/Time/` (e.g. `ClockCulture`/`IClockCultureSource`); it is captured once at the composition root (`App.xaml.cs`) and supplied to `ConfigValidator.ValidateClock` and `ClockWidgetViewModel` alike (the validator takes it as an injected parameter/dependency rather than reading `CultureInfo.InvariantCulture`), and it is injectable so tests pin a specific culture.** *(Chosen default per `D-M4-5`; alternatively documenting the invariant-culture validation as intentional is overridable, provided the runtime/validation divergence is eliminated.)*
- **M4-RF-952 [Maps RNF-010] `AppearanceConfig` extracted to its own file.** *Cosmetic gap-fix.* `AppearanceConfig` is currently declared inside `Config/PanelConfig.cs`. It must move to its own file (`Config/AppearanceConfig.cs`) with **no behavior change** (same namespace, same public shape, unchanged JSON round-trip/migration). This is a pure organization fix that keeps `PanelConfig.cs` focused.

### 5.7 Cohesion, schema, promotion, and invariants

- **M4-RF-960 [Maps RF-043/083/091/092/093, RF-061/M3-RF-850] Canonical UI surface map.** Each promoted concern is reachable from exactly one surface, and no concern is duplicated across surfaces:

  | Surface | Hosts | Does not host |
  |---|---|---|
  | **Settings window** (`RF-092`) | panel behavior (`alwaysOnTop`), startup (start-with-Windows), telemetry interval (`RF-071`), target-monitor picker (`RF-093`), import/export (`RF-083`), read-only diagnostics | hotkeys, appearance |
  | **Hotkeys / appearance editor** (`RF-061`/`RF-084`) | global `hotkeys` list, theme/accent/icon pack | startup, monitor, interval, import/export |
  | **Edit mode / overlay** (`RF-043`/`RF-044`) | widget palette add/remove/duplicate, property editor, action picker/browse, grid snap + alignment guides; Slider and all built-in types | app-level settings |
  | **Page switcher** (`RF-091`) | in-panel page selection in normal mode | widget editing |
  | **Tray menu** (`RF-005`) | Show/Hide, Edit Mode, Settings…, Hotkeys…, Open Config Folder, Exit | direct editors |

  If a promoted feature is already reachable from one of these surfaces (e.g. interval and monitor picker from settings; hotkeys/appearance from the editor), M4 must **not** add a second editor for it.
- **M4-RF-961 [Maps RF-074/RF-076, D-M2-7, M3-RF-846] No schema bump beyond v4.** The promoted features' persisted fields already exist in schema **v4** via the ordered additive chain (`v1→v2` slider/`persistState`/interval/hotkeys fields; `v2→v3` appearance; `v3→v4` global appearance). M4 must **not** raise `PanelConfig.CurrentSchemaVersion`; no `V4ToV5` migration is added. The only documented schema-adjacent boundary is the session-only grid/snap state (`M4-RF-441`), which is not persisted. `PanelConfig.CurrentSchemaVersion` stays `4`, and all four `RF-075` load states remain covered.
- **M4-RF-962 [Maps 001 §7.3 / D-24, 002 §7.3 / D-M2-8] Promotion declaration and freeze-documentation update.** M4 declares the un-frozen requirements **supported and maintained** (see §2) and updates the freeze documentation accordingly (see §8): the 001 §7.3 "Frozen backlog", the 001 inline `[FROZEN — backlog 2026-10-09]` tags, the 002 §7.3 status table, and the 003 frozen-remainder notes now read "**supported via M4**" for these IDs. Requirement IDs, sub-requirement sets, and semantics are unchanged. The only requirements still outside support are **`RF-072` (skipped)** and the **dropped set**. A dated decision (`001 D-25`) records the promotion.
- **M4-RF-963 [Maps RF-018, RNF-010, RF-086, RF-069/RNF-012, RF-081/RNF-014] Invariant preservation.** No promoted feature may regress the established invariants: normal mode never activates the panel or takes foreground focus; `Actions/`, `Monitoring/`, `Config/`, and `Input/` remain WPF-free with P/Invoke confined to `Interop/`; no feature prompts for elevation (`RF-086`); history and edit state stay bounded (fixed-capacity rings, no unbounded collections); configuration remains untrusted data with no new execution surface; and the M3 `DynamicResource`/rollback theming is not regressed by `M4-RF-950`.
- **M4-RF-964 [Maps RF-005/RF-092/RF-008] Reachability and documentation.** Every promoted **user-configurable** feature must be reachable from at least one surface (per `M4-RF-960`) and listed as **supported** (decision/spec documentation plus, where a surface exists, reflected in the settings diagnostics or equivalent). "In the build but undocumented/unreachable" is not sufficient for a supported declaration. The promoted behaviors that are **automatic/runtime** — `RF-070` backend re-initialization, `RF-082` hot reload, `RF-060` local shortcuts (fixed app-defined edit-mode keys), and the runtime side of `RF-045` (toggle-state restoration) — **require no user surface**: for these, "supported" means the behavior is active, logged/observable, and covered by tests, not that a UI control exists. The `RF-060` fixed key set is documented (no local-shortcut editor) and `RF-045`'s only user-facing knob is the per-Toggle `persistState` flag already reachable via the property editor.

## 6. Non-functional requirements

- **M4-RNF-141 [Maps RNF-006] Testability.** Every promoted feature is verified by its **existing** M2 unit/integration tests, which must remain green; the three M3 findings and the cohesion boundary gain new tests. Testability remains without WPF (except the real-BAML Sparkline-fill test), hardware, drivers, elevation, or network; tests use temp directories and injected paths/fakes. `dotnet test` is the acceptance gate.
- **M4-RNF-142 [Maps RNF-010] Layering.** The promotion changes no layer boundary: `Actions/`, `Monitoring/`, `Config/`, and `Input/` stay WPF-free; P/Invoke stays in `Interop/`; `Theme/` remains resources plus thin hosts. The `M4-RF-952` move keeps `AppearanceConfig` in the WPF-free `Config/` layer. The layer-purity architecture test remains green and is unchanged unless a moved/added file requires extending its namespace list.
- **M4-RNF-143 [Maps RNF-003, RNF-008] No new idle work.** Promotion adds no polling, watcher, animation, or background loop. Hot reload stays watcher-based and debounced; the Clock updates only on a text-changing boundary; the interval honors the configured rate. Idle CPU with all promoted features present stays within RNF-001.
- **M4-RNF-144 [Maps RNF-002, RNF-012] Bounded resources.** Promoted features add no unbounded collection: history rings remain fixed-capacity, edit state is bounded, and the `state.json` toggle map is pruned. The 8-hour soak holds the RNF-002 targets.
- **M4-RNF-145 [Maps RNF-014, RF-081, RF-086] Security posture.** No promoted feature introduces a new execution surface or elevation. The property-editor extension (`M4-RF-432`) validates all fields against the config validator, uses only built-in icons/packs, and never accepts external image paths; import/export treat imported content as untrusted; no secret/log policy is weakened.
- **M4-RNF-146 [Maps RF-018] No focus-steal regression.** Only the user-opened settings window and hotkeys/appearance editor may activate (the `D-6` analogue). Page switching, hot reload, interval changes, toggle persistence, backend re-init, monitor selection, and import/export never activate the panel or change the foreground window.

## 7. Milestone scoping

M4 is a **single milestone** with no sub-milestone labels. It is organized as four workstreams; because most requirements are already implemented, W1 is verification-only and W2 holds the only real code changes.

- **W1 — Promotion verification (no code).** Execute the existing M2/M3 test suites and map each promoted base (`RF-040/043/044/045/060/070/071/082/083/091/092/093`) to its sub-requirements and its surface (`M4-RF-401/430/440/450/600/700/710/820/830/910/920/930`). Produce the coverage/verification table and confirm nothing has regressed.
- **W2 — Gap fixes (code).** `M4-RF-432` (property editor covers Clock/ImageButton/Bar), `M4-RF-950` (Sparkline fill follows accent), `M4-RF-951` (Clock single culture source), `M4-RF-952` (extract `AppearanceConfig`). Delivered on the existing green baseline.
- **W3 — Cohesion, schema, and documentation.** `M4-RF-441` (document session-only grid/snap), `M4-RF-921/960` (surface map, no duplicate editors), `M4-RF-961` (v4 unchanged), `M4-RF-962` (promotion declaration + 001/002/003 freeze-documentation update, `D-25`), `M4-RF-964` (reachability/documentation).
- **W4 — Acceptance.** Run the full suite (1480 + new tests), the `A`/`M` acceptance criteria, the focus matrix, and the performance/soak checks; hand off to the reviewer.

`RF-072` is **skipped** and appears in no task. No dropped item is revived and no un-implemented requirement is promoted.

## 8. Freeze-documentation update (normative for M4)

M4 requires these documentation updates (IDs stable, no renumbering):

- **001 §7.3 "Frozen backlog (2026-10-09)"** gains an M4 promotion note: the listed requirements — `RF-040`, `RF-043` (non-appearance), `RF-044`, `RF-045`, `RF-060`, `RF-070`, `RF-071`, `RF-082`, `RF-083`, `RF-091`, `RF-092`, `RF-093` — are **supported via M4 (2026-10-10)**. `RF-072` remains frozen/skipped; the dropped set stays dropped.
- **001 §5 inline tags** for those requirements change from `[FROZEN — backlog 2026-10-09]` to `[SUPPORTED via M4 — promotion 2026-10-10]` (base IDs unchanged); `RF-072` keeps its frozen tag.
- **001 §8** gains **`D-25`**: the M4 promotion decision (supported = everything implemented except `RF-072` and the dropped set), mirroring this spec.
- **002 §7.3 status table** rows for `RF-040/043(non-appearance)/044/045/060/070/071/082/083/091/092/093` change from "Frozen backlog" to "**Supported via M4 (2026-10-10)**"; the `M3/Later` row reflects that `RF-046/061/084` are supported (M3) and only `RF-072` remains frozen; dropped rows unchanged. The 002 header/§2/§14 freeze banners gain a one-line M4-promotion note referencing `001 D-25`.
- **003** header/§2/§12 frozen-remainder notes gain a one-line note that the previously-frozen items are promoted by M4 (`001 D-25`), except `RF-072` and the dropped set; `RF-046/061/084` remain supported.

## 9. Acceptance criteria

Each criterion is automated (A), manual (M), or both (A+M).

- **AC-M4-01 (A) Build & tests.** The solution builds with the .NET 10 SDK; `dotnet test` passes with the new M4 tests; the full M1+M2+M3 suite (**1480 tests**) stays green. (`M4-RNF-141`)
- **AC-M4-02 (A) Promotion verification matrix.** Each promoted base maps to its M2 sub-requirements and its surface, and the referenced M2 test classes remain green: Slider (`SliderWidgetTests`), edit UI (`EditModeServiceTests`/`EditOverlayTests`/`PropertyEditorTests`/`ActionPickerTests`), grid/alignment (`GridSnapTests`/`AlignmentGuidesTests`/`EditSnapTests`), toggle persistence (`ToggleStateTests`/`StateStoreTests`), local shortcuts (`LocalShortcutsTests`), backend re-init (`BackendReinitTests`), interval (`IntervalTests`), hot reload (`ConfigReload*`/`ReloadDecisionTests`/`AppWriteSuppressionTests`), import/export (`ImportExportTests`), page switching (`PageSwitcher*`), settings (`SettingsWindowTests`/`SettingsViewModelTests`), monitor picker (`MonitorPickerViewModelTests`). (`M4-RF-401/430/431/433/440/450/451/600/700/710/820/821/830/910/920/930`)
- **AC-M4-03 (A) Sparkline fill follows accent (real BAML).** A **real-BAML** test loads the actual `SparklineWidgetTemplates.xaml`, realizes the Sparkline, and asserts: (i) an override-less filled area uses the **global/theme accent** at the documented translucency and **changes** on a live theme/accent swap; (ii) a per-widget accent override wins over the global/theme accent; (iii) an unavailable accent falls back to the theme accent fill without throwing. No hard-coded fill literal remains. (`M4-RF-950`, `D-M4-4`, `M3-RF-851`)
- **AC-M4-04 (A) Clock culture alignment.** With the culture pinned in the test, a custom format valid in that culture is accepted by **both** load-time validation and the Clock view model, and a format invalid in that culture is rejected by **both**; the validator and the view model resolve through the **same** culture source (asserted directly), eliminating the `InvariantCulture`/display-culture divergence. (`M4-RF-951`, `D-M4-5`)
- **AC-M4-05 (A) `AppearanceConfig` extraction.** `AppearanceConfig` is declared in `Config/AppearanceConfig.cs` (not inside `PanelConfig.cs`); the public shape, namespace, JSON round-trip, and `v3→v4` migration tests are unchanged and green. (`M4-RF-952`, `M4-RNF-142`)
- **AC-M4-06 (A) Property editor covers all built-in types.** The property editor exposes and validates Clock (`mode`/`format`), ImageButton (`showLabel`/`icon`), and Bar (`sensor`/`minimum`/`maximum`/`showValue`) type-specific fields, in addition to the M1/M2 types; invalid values are clamped/blocked, ImageButton uses only built-in icons, and JSON round-trips are unchanged. (`M4-RF-432`, `D-M4-2`)
- **AC-M4-07 (M) Edit UI, Slider, and grid on touch.** On the real touchscreen the user adds/removes/duplicates widgets, edits properties/actions (including the executable browse), edits all type-specific fields, drags the Slider to control volume, and uses grid snap + alignment guides — all by touch; changes survive restart; actions never fire while editing. (`M4-RF-402/430/431/432/433/440`)
- **AC-M4-08 (M) Settings and editor cohesion.** The settings window edits start-with-Windows, interval, monitor (embedded picker), import/export, and always-on-top and applies live; the hotkeys/appearance editor edits hotkeys and theme/accent/icon pack; **neither duplicates the other**; opening/closing either never changes the panel's no-activate behavior. (`M4-RF-920/921/930/931/960/961`)
- **AC-M4-09 (M) Hot reload / import-export / page switching live.** Externally editing valid `panel.json` applies live (widgets/topmost/interval/hotkeys) without restart and never triggers a self-reload; editing invalid JSON keeps the panel running and notifies; export writes valid JSON with `schemaVersion` and import validates, backs up, and applies; switching pages works in normal mode without focus steal and persists `activePageId` with fallback. (`M4-RF-820/821/830/831/910/911`)
- **AC-M4-10 (M) Monitoring promotion live.** Changing the interval applies live without duplicate loops; a simulated backend failure recovers via throttled re-init and re-marks `monitoring` restored; a persisted Toggle restores after restart while a non-persisting Toggle resets; a toggle flip writes only `state.json` and causes no reload. (`M4-RF-700/710/711/450/451`)
- **AC-M4-11 (M) Focus & performance/soak.** The `RF-018` focus matrix passes with all promoted features present: showing/hiding the panel, tapping widgets, switching pages, changing the interval, hot-reloading, and import/export never interrupt foreground typing; only the user-opened settings/editor windows activate. Idle CPU stays within RNF-001 and an 8-hour soak holds RNF-002. (`M4-RNF-143/144/146`)
- **AC-M4-12 (A) Layering & security regression.** `Actions/`, `Monitoring/`, `Config/`, and `Input/` remain WPF-free; P/Invoke stays in `Interop/`; no promoted feature prompts for elevation; hostile configuration (property fields, imported files, grid values, colors, clock formats) is contained, never executed or trusted; no new execution surface. (`M4-RNF-142/145`, `M4-RF-963`)
- **AC-M4-13 (A) Freeze-documentation consistency.** The 001 §5/§7.3 tags, 001 `D-25`, and the 002/003 notes mark the promoted requirements **supported via M4** with IDs unchanged; the only requirements outside support are `RF-072` (skipped) and the dropped set; no requirement is renumbered or retagged. (`M4-RF-962`, `M4-RF-964`)

## 10. Failure cases and required behavior

| ID | Scenario | Required behavior | Refs |
|---|---|---|---|
| F-M4-01 | Grid size / snap set in a session, then restart | Session-only (documented): resets to `gridSize=8`/snap-on; no config write, no load error | M4-RF-441 |
| F-M4-02 | Clock custom format valid in one culture but not another | Validator and view model use the **same** culture source and agree; no runtime/validation divergence | M4-RF-951 |
| F-M4-03 | Active accent unavailable when rendering a filled Sparkline | Fall back to the theme accent fill at the documented translucency; no crash; follows later accent/theme swaps | M4-RF-950 |
| F-M4-04 | Property-editor value invalid for a new/old widget type | Clamp/block with visible feedback; invalid value never written to the in-memory config; widget disabled with reason where applicable (`RF-047`) | M4-RF-432, RF-079 |
| F-M4-05 | A previously-frozen feature regresses during promotion | Treated as a defect; the referenced M2/M3 test fails the gate; not shipped | AC-M4-01/02 |
| F-M4-06 | Promotion tag/documentation left inconsistent | Acceptance fails (`AC-M4-13`); IDs/semantics must remain stable | M4-RF-962 |
| F-M4-07 | `AppearanceConfig` move breaks JSON/migration | Round-trip/`v3→v4` tests must stay green; the move is behaviorless | M4-RF-952 |
| F-M4-08 | Import/hot-reload/property edit with hostile or malformed data | Contained per existing `RF-079`/`RF-081` (skip/clamp/disable; keep running config); no new execution surface | M4-RF-830, M4-RNF-145 |

Behavioral failure cases for the promoted features themselves remain governed by the existing M1 `F-01..F-22` and M2 `F-M2-01..F-M2-27`; M4 adds no new failure semantics for them.

## 11. Test strategy

Aligned with M1 §11 / M2 §10 / M3 §11: unit tests are the gate, hardware/network/elevation are excluded, temp directories isolate the profile, and WPF-free layer purity is enforced. No UI-automation framework is introduced.

### 11.1 Unit tests (no hardware, no drivers, no elevation, no network)

- **Promotion verification:** run and keep green the existing M2/M3 suites for every promoted base (the classes listed in `AC-M4-02`). No new tests are required merely to "re-prove" already-covered behavior, but coverage gaps surfaced by the verification table must be closed.
- **Gap fixes:** Sparkline accent-fill precedence/fallback (plus one real-BAML realization test per `AC-M4-03`); Clock validator/view-model shared culture source (`AC-M4-04`); property-editor type-specific fields for Clock/ImageButton/Bar with validator-rules clamping (`AC-M4-06`); `AppearanceConfig` extraction with unchanged round-trip/migration (`AC-M4-05`).
- **Cohesion:** a settings/editor ownership test (or a source-level assertion) that the settings window does not expose hotkey/appearance editing and the hotkeys editor does not expose startup/monitor/interval/import-export (`M4-RF-921`/`960`).
- **Architecture:** layer-purity test extended if needed for any moved/added file; P/Invoke confined to `Interop/`.

### 11.2 Integration tests (still no real hardware/network)

- `JsonConfigStore` against a temp directory: schema stays **v4** (`M4-RF-961`); import/export and reload self-suppression remain correct after any `AppearanceConfig` move.
- `SettingsCoordinator` with a fake store/applier: settings and hotkey/appearance edits each persist through the single writer without clobbering the other section.

### 11.3 Manual verification matrix (executed in the M4 acceptance run)

- **MV-M4-01 Edit UI / Slider / grid:** touch add/remove/duplicate/property/action-picker/browse/type-specific fields/slider/grid/guides; restart persistence; no action fires while editing.
- **MV-M4-02 Settings & editor cohesion:** settings (startup/interval/monitor/import-export/always-on-top) and hotkeys/appearance editor, no duplication; live apply; activation behavior.
- **MV-M4-03 Hot reload / import-export / page switching:** external valid/invalid edits; export/import; page switching with fallback.
- **MV-M4-04 Monitoring:** interval live change; backend re-init recovery; toggle persistence across restart.
- **MV-M4-05 Sparkline / Clock polish:** filled Sparkline follows theme/accent live; clock format accepted/rejected consistently.
- **MV-M4-06 Focus matrix & performance/soak:** `RF-018` procedure with all promoted features present; idle CPU within RNF-001; 8-hour soak within RNF-002.
- **MV-M4-07 Documentation:** freeze tags/`D-25` read consistently; supported set = everything implemented except `RF-072` and the dropped set.

### 11.4 Gates

- After every M4 task: `dotnet build` and `dotnet test` succeed; the full M1+M2+M3 suite stays green.
- Manual MV-M4 checks pass or produce a documented, human-accepted limitation.
- A separate reviewer agent validates the implementation against this spec; reviewers do not modify code.

## 12. Integration with the existing architecture

| M4 area | Existing seam it extends | Required approach |
|---|---|---|
| Promotion verification | Existing M2/M3 test suites and VMs/services | Verify unchanged; produce the coverage table; no rewrite. |
| Slider reachability | `WidgetPaletteSource`, `PropertyEditorViewModel`, `ConfigValidator` | Confirm palette + property-editor fields; validate with the shared rules. |
| Property editor coverage | `PropertyEditorViewModel`/`ActionPickerViewModel`, `WidgetConfig` subclasses, `IconCatalog`/`IconPackCatalog` | Add Clock/ImageButton/Bar field editors; built-in icons only; no new persistence. |
| Sparkline fill | `SparklineWidgetTemplates.xaml`, `AppearanceForegroundConverter`, `ThemeManager` | Derive the fill from the effective accent (override→global→theme) at a documented alpha, `DynamicResource`-consistent. |
| Clock culture | `ConfigValidator.ValidateClock`, `ClockWidgetViewModel`, `ClockFormat` | One shared culture source for validation and resolution. |
| `AppearanceConfig` | `Config/PanelConfig.cs` | Move to `Config/AppearanceConfig.cs`; behaviorless. |
| Toggle persistence | `ToggleStatePersistence`, `StateStore`, `RuntimeState` | Verify only; no change. |
| Monitoring interval/re-init | `MonitoringService`, `TelemetryConfig`, `ThrottledFailureLogger` | Verify only; no change. |
| Hot reload / import-export | `ConfigReloadController`, `ReloadDecision`, `JsonConfigStore`, `ConfigValidator` | Verify only; no change. |
| Page switching / settings / monitor picker | `PageSwitcherViewModel`/`PageSwitchCoordinator`, `SettingsWindow`/`SettingsViewModel`, `MonitorPickerViewModel` | Verify only; enforce the surface map (`M4-RF-960`). |
| Composition root | `App.xaml.cs` | No new wiring beyond the property-editor fields and the Sparkline/culture fixes; keep startup order and shutdown participation. |

**Threats to M1/M2/M3 invariants and how M4 protects them:**

- **Focus preservation (`RF-018`):** only the user-opened settings/editor windows activate; all other promoted features never activate the panel.
- **Single writer / no polling (`RF-080`):** promotion adds no watcher or polling; hot reload and state writes keep their suppression.
- **UI-thread discipline (`RNF-004`):** theme/accent swap, Sparkline refills, interval changes, and saves are marshalled/async as before.
- **Layer purity (`RNF-010`):** no boundary changes; `AppearanceConfig` stays in `Config/`.
- **Untrusted configuration (`RF-081`) / no new execution surface:** property fields and imports are validated; built-in icons only; no elevation.
- **Bounded history (`RF-069`/`RNF-012`):** untouched by promotion.

## 13. Resolved decisions and open questions

| ID | Decision (overridable) | Where applied |
|---|---|---|
| D-M4-1 | **M4 scope = promote the implemented backlog** (`RF-040/043/044/045/060/070/071/082/083/091/092/093`) via verification + gap-fix + a supported declaration; keep IDs stable; do not invent features. `RF-072` skipped; dropped set stays dropped. | Header, §2, §7 |
| D-M4-2 | **Property editor covers all built-in types** (Clock/ImageButton/Bar type-specific fields), reversing `M3-RF-468`/`D-M3-8`'s JSON-only limitation now that the editor is supported. Alternative: keep JSON-only and document it. | M4-RF-432, AC-M4-06 |
| D-M4-3 | **Grid size and snap stay session-only** (`gridSize=8`, snap-on default), documented as intentional; no schema field/bump. Alternative: persist them (would require v5). | M4-RF-441, F-M4-01 |
| D-M4-4 | **Sparkline filled area derives from the effective accent** (override→global→theme) at the documented `~0x55` translucency and follows live theme/accent swaps. Alternative: document the fixed color as intentional. | M4-RF-950, AC-M4-03 |
| D-M4-5 | **One shared Clock culture source** (default: user's current culture captured once) for both load-time validation and runtime resolution. Alternative: document invariant-culture validation — only if the divergence is eliminated. | M4-RF-951, AC-M4-04 |
| D-M4-6 | **Schema stays v4**; no `V4ToV5`. Promoted fields already exist via `v1→v2→v3→v4`. | M4-RF-961 |
| D-M4-7 | **UI cohesion:** Settings hosts behavior/startup/interval/monitor/import-export/diagnostics; the hotkeys editor hosts hotkeys + appearance; the edit overlay hosts widget editing; the page switcher hosts page selection. No duplicate editors. | M4-RF-960/921/931 |
| D-M4-8 | **Freeze documentation updated** (001 §5/§7.3 + `D-25`, 002 §7.3, 003 notes): supported = everything implemented except `RF-072` and the dropped set; IDs unchanged. | M4-RF-962, §8 |

**Open questions:** none blocking. The overridable defaults are `D-M4-2` (extend the property editor), `D-M4-3` (session-only grid), `D-M4-4` (accent-derived fill), and `D-M4-5` (shared Clock culture).

## 14. Traceability — base 001 → M4 sub-requirements

| 001 base | M4 sub-requirements | M2/M3 normative set |
|---|---|---|
| RF-040 Slider | M4-RF-401..402 | M2-RF-401..406 |
| RF-043 full edit UI (non-appearance) | M4-RF-430..433 | M2-RF-430..434, 437..440 |
| RF-044 grid/alignment | M4-RF-440..441 | M2-RF-441..446 |
| RF-045 toggle persistence | M4-RF-450..451 | M2-RF-451..455 |
| RF-060 local shortcuts | M4-RF-600..601 | M2-RF-601..604 |
| RF-070 backend re-init | M4-RF-700 | M2-RF-701..704 |
| RF-071 polling interval | M4-RF-710..711 | M2-RF-711..714 |
| RF-082 hot reload | M4-RF-820..821 | M2-RF-821..830 |
| RF-083 import/export | M4-RF-830..831 | M2-RF-831..835 |
| RF-091 page-switching UI | M4-RF-910..911 | M2-RF-911..915 |
| RF-092 settings window | M4-RF-920..921 | M2-RF-921..925 |
| RF-093 monitor picker | M4-RF-930..931 | M2-RF-931..934 |
| RF-041/RF-084 (Sparkline/accent) | M4-RF-950 | M2-RF-411, M3-RF-843/851 |
| RF-046 (Clock culture, property editor) | M4-RF-951, M4-RF-432 | M3-RF-462/463/468 |
| RNF-010 (organization) | M4-RF-952 | — |
| Cross-cutting (cohesion/schema/promotion/invariants) | M4-RF-960..964 | — |
| RNF refinements | M4-RNF-141..146 | — |
| RF-072 | **Skipped — no sub-requirements** | — |

**Coverage check:** every un-frozen base requirement (`RF-040/043/044/045/060/070/071/082/083/091/092/093`) maps to at least one M4 sub-requirement. The three M3 findings are covered by `M4-RF-950/951/952`. **`RF-072` is intentionally skipped.** The dropped set has no sub-requirements. `RF-046/061/084` remain supported (M3) and are not re-opened. No existing 001/002/003 ID is renumbered or retagged.
