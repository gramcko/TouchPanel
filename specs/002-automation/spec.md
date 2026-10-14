# TouchPanel — Specification 002: Automation (M2)

| Field | Value |
|---|---|
| Spec ID | 002-automation |
| Status | **Scope frozen (2026-10-09): Core Release delivered; remaining M2 features frozen backlog** |
| Date | 2026-10-09 |
| Phase | M2 — Specification |
| Author | Planner agent |
| Inputs | `specs/001-foundation/spec.md` (approved M1 + freeze), `specs/001-foundation/plan.md`, `specs/001-foundation/tasks.md`, the M1/M2 implementation in `TouchPanel/` |
| Scope | Delivered **Core Release** only: **Sparkline/graph + bounded telemetry history**, **global hotkeys**, **send-keys (key combinations)**, and **visual customization (icon/colors/font/opacity)**. All other M2 work is **frozen backlog** (`D-M2-8`, 001 `D-24`) |
| Out of scope | **Frozen backlog** (code remains in the build but is unsupported/unmaintained): RF-040, RF-043 (non-appearance), RF-044, RF-045, RF-060, RF-070, RF-071, RF-082, RF-083, RF-091..RF-093, RF-046/RF-061/RF-072/RF-084. **Dropped**: RF-042, RF-054, RF-055, RF-056, RF-087, the `delay` action, and shell-open of files/folders/URLs |

### Why "Automation"

M2 is dominated by making the running panel *do* things beyond a single local tap: **global hotkeys** that work while a game owns focus and **key-combination actions** (send-keys, e.g. Alt+A) delivered to the foreground application — plus the widgets that drive them (Slider, Sparkline) and the configuration surfaces needed to use them (full edit UI, hot reload, import/export, settings, monitor picker). "Automation" names the center of gravity; the UI/config requirements are the way that automation is composed. The name does not change any 001 milestone tag.

> **Descope note (2026-10-09).** Macros/sequences, macro cancellation, repeat-while-held, the standalone `delay` action, **HTTP actions (RF-056) and their safety constraints (RF-087)**, and **shell-open of files/folders/URLs** are **dropped from the product**, not deferred. The action model is **one action per widget** (as in M1). The M2 action set is **open program (the existing M1 run-command/launch action) plus send key combination** (send-keys, RF-057). A widget or hotkey that presses **Alt+A** in the foreground application is the canonical example.

> **Scope freeze (2026-10-09, D-M2-8 / 001 D-24).** The product is frozen to a **Core Release**. Delivered and supported: **Sparkline/graph + bounded telemetry history** (RF-041, RF-069, RNF-012), **global hotkeys** (RF-058, RF-059), **send-keys** (RF-057), and **visual customization** (RF-043 appearance only — label/icon/colors/font/opacity). **Everything else in this document is frozen backlog**: its code may remain in the build but it is unsupported and unmaintained and will not be extended. See §2, §7.3, and §14/D-M2-8.

> **M4 promotion note (2026-10-10, 001 `D-25`).** M4 ("Promote & Polish") **un-freezes and officially supports** the implemented M2 backlog: RF-040, RF-043 (non-appearance), RF-044, RF-045, RF-060, RF-070, RF-071, RF-082, RF-083, RF-091, RF-092, RF-093 (see `specs/004-promotion/spec.md`). The §7.3 status table and the per-requirement `[FROZEN — backlog 2026-10-09]` tags below now read "supported via M4". RF-046/061/084 remain supported (M3); **RF-072 stays frozen/skipped**; the dropped set stays dropped. IDs are unchanged.

---

## 1. Goal

M1 delivered a reliable, config-driven, touch-first panel that never steals focus. M2 must turn that foundation into a genuinely useful personal control surface without violating the invariants M1 proved:

1. **Automation that works globally.** Global hotkeys registered through Win32 `RegisterHotKey`, executing actions while another application or game has focus, without ever stealing that focus.
2. **Graphing and visual customization.** The Sparkline/graph widget with bounded telemetry history, plus per-widget visual customization (icon / colors / font / opacity) in edit mode. (The Slider widget, the full edit UI beyond appearance, and grid/guides are **frozen backlog**.)
3. **A model-driven action layer.** The M1 one-action-per-widget model extended with SendInput-based key-combination synthesis (send-keys); "open program" remains the existing M1 run-command/launch action. Failure feedback and redaction are unchanged.
4. **Live, bounded telemetry.** Fixed-capacity ring buffers per metric feeding the Sparkline. (Throttled backend re-initialization and a configurable polling interval are **frozen backlog**.)
5. **Configuration lifecycle (M1).** The M1 configuration store with schema versioning, migration, atomic save, backup/recovery, and validation. (Hot reload and import/export are **frozen backlog**.)
6. **Same discipline as M1.** WPF-free core, `Interop/` isolation, bounded memory, UI-thread safety, untrusted configuration treated as data, and a deterministic test suite that touches no hardware or network.

**Success for M2** means: a user can graph CPU history from bounded telemetry, send a key combination such as Alt+A from a widget or from a global hotkey while playing a full-screen game (keyboard focus never moves), and customize widget appearance (icon / colors / font / opacity) — all with no crash, no runaway memory, and no focus stealing.

## 2. Scope

**Delivered Core Release (in scope, supported):**

- **Widgets:** RF-041 Sparkline/graph; RF-043 **appearance only** — the visual-customization sub-requirements `M2-RF-435` (label/icon) and `M2-RF-436` (colors/font/opacity) and the `WidgetConfig` appearance fields (schema v3).
- **Monitoring:** RF-069 bounded history; RNF-012 bounded-history memory documentation.
- **Actions:** RF-057 send keys (key combinations). The M1 run-command/launch action ("open program") is kept (M1).
- **Input:** RF-058 global hotkeys, RF-059 registration-failure handling.

**Frozen backlog (out of scope; code remains in the build, unsupported and unmaintained):**

> **M4 promotion (2026-10-10, 001 `D-25`).** The items listed here are **no longer frozen**: M4 un-froze and now supports RF-040, RF-043 (non-appearance), RF-044, RF-045, RF-060, RF-070, RF-071, RF-082, RF-083, RF-091, RF-092, RF-093 (see `specs/004-promotion/spec.md`). RF-046/061/084 are supported (M3). Only **RF-072** remains frozen/skipped; the dropped set stays dropped. The list is retained for historical context.

- RF-040 Slider.
- RF-043 non-appearance parts: palette add/remove/duplicate (`M2-RF-430/431/432`), type-specific property fields (`M2-RF-433`), action picker + executable browse (`M2-RF-434`), and the persistence/isolation parts (`M2-RF-437..440`) insofar as they cover those.
- RF-044 grid snap + alignment aids.
- RF-045 toggle-state persistence.
- RF-060 local shortcuts.
- RF-070 throttled backend re-initialization.
- RF-071 configurable polling interval.
- RF-082 hot reload.
- RF-083 import/export.
- RF-091 page-switching UI, RF-092 settings window, RF-093 monitor picker.
- M3/Later: RF-046, RF-061, RF-072, RF-084.

**In scope as integration only:** extending existing M1 seams (`IActionExecutor`, `IConfigStore`, `IMonitoringService`, `WidgetRegistry`, `EditModeService`, `MessageWindow`, `IConfigStore` single-writer rule). No M1 behavior is redefined.

**Dropped (not deferred):** RF-042 repeat-while-held, RF-054 macros/sequences, RF-055 macro cancellation, **RF-056 HTTP action, RF-087 HTTP safety constraints**, the standalone `delay` action, and shell-open of files/folders/URLs. These are removed from the product plan (user decision, 2026-10-09); their 001 IDs are retained but marked dropped. The widget "running" state that existed only for sequence cancellation is also removed. The action set is open program (existing run-command) + send key combination.

**Focus-preservation scope.** The RF-018 guarantee is preserved for all delivered features. Global hotkeys must never activate the panel or move the foreground window. The exclusive-fullscreen limitation from 001 §2 still applies and is documented, not solved.

## 3. Non-goals

- No plugin system, event bus, mediator, DI container, scripting engine, or expression language. There is no macro/sequence model: a widget binds exactly one action.
- No cross-platform abstractions; Windows-only, x64, Windows 10 21H2+/Windows 11 (RF-090 unchanged).
- No elevation and no UAC prompt for any M2 feature (RF-086); hotkeys, history, and edit UI all work as a standard user.
- No arbitrary shell construction. "Open program" uses the existing M1 safe process-launch path (RF-049, `UseShellExecute=false`), and send-keys is a modeled typed action; configuration remains data throughout (RF-081). HTTP and shell-open of files/folders/URLs are dropped, so no new execution surface is introduced.
- No cloud sync, remote config, accounts, or telemetry upload.
- No user-configurable local-shortcut editor in M2 (RF-061 is M3); local shortcuts are the app's fixed edit-mode keys.
- No macros/sequences, no delays, and no repeat-while-held in M2 or later (dropped, not deferred).
- No undo/redo history engine in the edit UI; edit changes are saved as a whole via the existing single-writer store.
- No fuzzy "any key" send-keys or text typing in M2 beyond a single key + modifier combination per action (e.g. Alt+A).

## 4. Relationship to 001 and requirement ID rules

This document **does not renumber, restate, or change the semantics of any 001 requirement.** Where 001 gives a requirement one sentence, this document adds testable sub-requirements in the `M2-RF-###` / `M2-RNF-###` namespace. Every sub-requirement carries a **Maps to** reference to its 001 base ID.

Rules:

- Base IDs (`RF-040`…`RF-093`, `RNF-012`) keep their 001 meaning and milestone tag.
- `M2-RF-4xx` = widgets, `M2-RF-5xx` = actions, `M2-RF-58x`/`59x`/`60x` = input, `M2-RF-69x`/`70x`/`71x` = monitoring, `M2-RF-82x`/`83x` = configuration, `M2-RF-91x`/`92x`/`93x` = deferred UI. The `M2-RF-87x` (security) bucket is unassigned because RF-087 is dropped.
- `M2-RNF-12x` maps to `RNF-012`; `M2-RNF-1xx` (other) are M2 execution constraints that refine M1's RNF-002/003/004/006/010/013/014 for the new code.
- Sub-requirement numbers are **three-digit** and unique within the document. The widget bucket uses `M2-RF-40x`..`M2-RF-45x` (RF-040→40x, RF-041→41x, RF-043→43x, RF-044→44x, RF-045→45x; the `42x` block is unassigned because RF-042 is dropped); within a block, numbers are ascending.
- A base requirement's block is a **contiguous ascending range** and may cross the nominal two-digit prefix when a base has more than ten sub-requirements; e.g. RF-043 occupies `M2-RF-430..440` (crossing from `43x` into `440`). The only rule is that numbers are unique and ascending within the block; the prefix is a mnemonic, not a hard boundary.
- If a sub-requirement is dropped at approval, only it is removed; the base 001 requirement remains accounted for by its remaining sub-requirements. A base requirement is *fully* covered when all of its sub-requirements are accepted.

## 5. Functional requirements

Conventions: **Must** = verifiable. "UI thread" means the WPF dispatcher thread. "Bounded" means (a) a hard maximum exists, and (b) it is documented and tested. Timing values marked *(default)* are chosen defaults the human can override at approval.

### 5.1 Widgets

#### RF-040 — Slider widget **[FROZEN — backlog 2026-10-09]**

- **M2-RF-401 [Maps RF-040] Slider configuration.** A `slider` widget type with: numeric `minimum` and `maximum` (finite, `maximum > minimum`), numeric `step` (`> 0`, `≤ range`), `orientation` (`horizontal` *(default)* or `vertical`), initial `value` (clamped into `[minimum, maximum]` and rounded to `step`), an optional bound action, and `label`. Invalid ranges/steps are normalized by the M1 `ConfigValidator` rules with a logged warning, consistent with entry-level containment (RF-079).
- **M2-RF-402 [Maps RF-040] Touch/mouse interaction.** Dragging the slider (touch or mouse) updates the displayed value continuously. A completed interaction emits exactly one value-change action carrying the final value; continuous drag updates are **throttled** (at most one emitted action per `100 ms` *(default)* or per `step` change, whichever is less frequent). Releasing does not emit a duplicate of the last emitted value. Press feedback is immediate (< ~100 ms, per RF-030).
- **M2-RF-403 [Maps RF-040] Value → action binding.** The slider supplies its current value to the bound action where that action type defines a numeric parameter. For M2 the only supported binding is the `volume` action in `Set` mode, whose `level` is replaced by the slider value normalized from the slider range onto `0–100`. If the bound action type has no numeric target parameter, the slider loads **disabled** with a visible error indicator and a log entry (the RF-047 containment rule), never silently ignoring the value.
- **M2-RF-404 [Maps RF-040] Orientation.** `horizontal` maps the range left→right (minimum→maximum); `vertical` maps bottom→top. Layout and hit-testing are correct in both orientations at 100%–200% scaling (RNF-015).
- **M2-RF-405 [Maps RF-040, RF-031] Touch target.** The slider's interactive track and any handle meet the ≥ 40 DIP touch-target default (D-9). The slider is operable without keyboard focus (RF-025).
- **M2-RF-406 [Maps RF-040] Failure feedback.** A failed value-change action produces the ~2 s transient widget error state and structured log (RF-036/RF-050); the slider remains usable.

#### RF-041 — Sparkline/graph widget

- **M2-RF-411 [Maps RF-041] Sparkline configuration.** A `sparkline` widget type with: `sensor` (a stable application-level sensor key, as used by Gauge; unknown keys disable the widget per RF-047), `windowSeconds` (bounded, e.g., `10–3600`, default `60`), a render style (line *(default)*; optional filled area), and optional explicit `minimum`/`maximum` (auto-scaled from the data when omitted).
- **M2-RF-412 [Maps RF-041, RF-069] History consumption only.** The Sparkline renders from the monitoring layer's bounded ring buffer for its metric. It **must not** call the hardware library or any hardware-polling path. The consumer surface is the monitoring-owned history boundary fed by `ISensorSnapshotSource` (`Latest` + `SnapshotAvailable`); `MonitoringService.PublishOnce` is the producer and is never invoked by a widget. The Sparkline reads a history slice (or subscribes to `SnapshotAvailable`) only.
- **M2-RF-413 [Maps RF-041, RF-038, RF-067] UI-thread rendering and rate.** Rendering happens on the UI thread from the buffered history; it updates **at most once per snapshot** (≤ ~1 Hz by default) and performs **no** idle animation and **no** re-render when the history and scale are unchanged. Snapshot callbacks arrive on the background thread and are marshalled via the existing dispatcher hook.
- **M2-RF-414 [Maps RF-041, RNF-012] Unavailable data.** Missing/unavailable samples render as **gaps** in the line, never as zero. Auto-scaling ignores unavailable samples; an all-unavailable window shows an explicit "Unavailable" state (consistent with Gauge).
- **M2-RF-415 [Maps RF-041, RF-069] Bounded points.** The number of rendered points is bounded by the ring buffer capacity for the configured window and interval (RF-069/RF-071). The Sparkline never keeps an unbounded collection of its own.
- **M2-RF-416 [Maps RF-041] Failure isolation.** A render failure (bad data, scale math) degrades to "Unavailable" and logs; it never crashes the page or affects other widgets (RF-012/RNF-013).

#### RF-043 — Full edit UI **[PARTIAL: appearance (M2-RF-435/436) delivered; remainder FROZEN — backlog 2026-10-09]**

- **M2-RF-430 [Maps RF-043] Unknown-type preservation.** **[FROZEN — backlog 2026-10-09]** Unknown widget types remain visible as disabled placeholders with their raw JSON preserved (RF-079) and are not offered for add (the palette lists built-in types only). They can be removed.
- **M2-RF-431 [Maps RF-043] Add widget.** **[FROZEN — backlog 2026-10-09]** In edit mode, a widget palette offers the six built-in types. Adding creates a config entry with a generated unique `id` within the page, a default geometry, and default type properties. Default geometry is an implementer-chosen default: size `160×120 DIP` (or the type's registered default when larger), placed at the **first free position** scanning the page top-left→bottom-right on the grid (M2-RF-441) where the widget fits and does not overlap an existing widget. If no position fits, place it at the page origin offset by a fixed `16 DIP` cascade from the last-added widget; if that too is out of bounds, place it at the origin. The new widget appears immediately and is selected.
- **M2-RF-432 [Maps RF-043] Remove and duplicate.** **[FROZEN — backlog 2026-10-09]** The selected widget can be removed (it disappears immediately and is removed from the page config on save). Duplicate clones the selected widget's full config under a new unique `id`, offset by a fixed `16 DIP` delta, and selects the clone. Remove/duplicate are dirty-tracked.
- **M2-RF-433 [Maps RF-043, RF-034] Property editor.** **[FROZEN — backlog 2026-10-09]** A touch-operable property editor edits the selected widget's position/size (in DIPs), `label`, and its type-specific properties: Button/Toggle action binding; Toggle initial state; Slider min/max/step/orientation/**initial value**; Gauge sensor/range; Sparkline sensor/window/style. The Slider `value` edited here is the **configured initial value** (M2-RF-401); the live value produced by dragging is transient runtime UI state and is never persisted (D-M2-6, §14). Inputs are validated with the **same rules the config validator uses**; invalid values are blocked or clamped with visible feedback, and never written to the in-memory config.
- **M2-RF-434 [Maps RF-043, RF-047] Action picker (incl. open-program browse).** **[FROZEN — backlog 2026-10-09]** The picker chooses from all action types available in the build — the M1 types (run command / launch program, media key, volume, mute; **"open program" is the run-command/launch action**) plus the M2 `sendKeys` type (M2-RF-571) — and edits typed parameters. For the run-command/launch ("open program") action, the picker provides an executable **file browse** selection so the user can point at an `.exe` without hand-editing the path; arguments and working directory remain editable as discrete fields. The selected action is validated on commit; an invalid/unknown selection leaves the widget disabled with a visible error indicator and a log entry (RF-047), never dropping the widget.
- **M2-RF-435 [Maps RF-043] Label and icon.** The editor edits label text (bounded: `0–128` characters) and selects an icon from a **built-in** icon catalog shipped with the application. The catalog contains an implementer-chosen **1–64** entries (a single flat set); external icon packs are M3/RF-084 and must not be introduced.
- **M2-RF-436 [Maps RF-043, RF-034] Colors and appearance.** Per-widget appearance overrides layered over theme defaults (RF-084 full theming is M3): background, foreground, and accent colors accepted as `#RRGGBB` or `#AARRGGBB`; optional font size in `8–96 DIP` and opacity in `0.0–1.0` (implementer-chosen bounds within these). Invalid or out-of-range values fall back to the theme/default bound with a logged warning. Overrides are stored in configuration.
- **M2-RF-437 [Maps RF-043, RF-080] Persistence and single writer.** **[FROZEN — backlog 2026-10-09 insofar as it covers the frozen edit operations; appearance edits persist through the same store]** Add/remove/duplicate/property/action/label/icon/color changes mark the layout dirty and are persisted **only** through the existing `IConfigStore` single-writer path on edit-mode exit/save (or shutdown), exactly as M1 does for geometry. No file watcher, no partial write (RF-077). A failed save keeps the in-memory state and notifies (RF-077).
- **M2-RF-438 [Maps RF-043, RF-025, D-6] Touch-first editing.** **[FROZEN — backlog 2026-10-09 insofar as it covers the frozen edit operations]** All M2 edit interactions are operable by touch. The edit UI may rely on the deliberate activation allowed by D-6 while edit mode is active, but must not require a physical keyboard. Dialog-less, non-blocking interaction is preferred so a mis-tap does not lose the whole edit session.
- **M2-RF-439 [Maps RF-043, RF-035] Actions do not fire while editing.** **[FROZEN — backlog 2026-10-09]** In edit mode a tap on a widget selects/moves it; the widget's bound action **must not** execute. Exiting edit mode restores normal action behavior.
- **M2-RF-440 [Maps RF-043, RF-012] Edit UI failure isolation.** **[FROZEN — backlog 2026-10-09]** A failure while building the palette, property editor, or icon catalog degrades the affected affordance (logged, disabled) without taking the panel or edit mode down.

#### RF-044 — Grid snap and alignment aids **[FROZEN — backlog 2026-10-09]**

- **M2-RF-441 [Maps RF-044] Grid configuration.** A `gridSize` (bounded `2–64 DIP`, default `8`) and a per-session snap toggle in edit mode (default on). Snapping applies to drag-move and resize; dragging pins positions/sizes to the nearest grid multiple while preserving page clamping and the 40 DIP minimum (RF-039). *(M4 note: `gridSize` persistence was **deliberately not taken** — grid size and the snap toggle are **session-only** per `001 D-25`/`M4-RF-441`/`D-M4-3`; no schema field is added and `panel.json` stays v4.)*
- **M2-RF-442 [Maps RF-044, RF-039] Snap + clamp composition.** Snapping is applied first, then page clamping and the 40 DIP minimum, so a snapped value is always finite, within bounds, and ≥ minimum. A widget smaller than a grid cell snaps its origin only.
- **M2-RF-443 [Maps RF-044, RF-027] Keyboard nudge under snap.** With snap **on**, arrow-key nudge moves by `gridSize` DIP; with snap **off**, by `1 DIP` (preserving the M1 RF-027 default). Nudge uses the same clamp pipeline.
- **M2-RF-444 [Maps RF-044] Alignment aids.** While dragging, alignment guides appear when the moving widget's edges or center align (within a fixed `4 DIP` threshold) with another widget's edges/center or with the page center/edges. When snap-to-alignment is enabled, the widget snaps to the aligned coordinate. Guides are rendered only during the drag and are not persisted.
- **M2-RF-445 [Maps RF-044] Free positioning override.** Free (unsnapped) positioning remains available via an explicit toggle (touch) and/or a modifier key, so snap is never a lock-in (the M1 free positioning of RF-039 is preserved as an option).
- **M2-RF-446 [Maps RF-044] Deterministic math.** Grid and alignment math is pure and unit-tested (grid rounding, threshold edge cases, composition with clamp and minimum size, hostile/non-finite inputs normalized).

#### RF-045 — Toggle state persistence **[FROZEN — backlog 2026-10-09]**

- **M2-RF-451 [Maps RF-045, D-15] Per-toggle opt-in.** A Toggle widget config flag `persistState` (boolean, default `false`, preserving D-15). When `true`, the toggle's on/off value is restored after a restart; when `false`, it resets to `initialOn` as in M1.
- **M2-RF-452 [Maps RF-045, RF-073] Persistence location and shape.** Persisted toggle states are stored in the existing **runtime-state store** (`state.json`, M1 `RuntimeState`/`StateStore`, RF-073) under a dedicated `toggleStates` object mapping a composite key `"<pageId>/<widgetId>"` to a boolean (`{ "toggleStates": { "main/w1": true } }`) — **never** in `panel.json`. This keeps user configuration clean (D-12), avoids writing user-editable config on every flip, and prevents hot-reload storms (M2-RF-823). An absent, empty, or shape-mismatched `toggleStates` map is **non-fatal**: it is treated as "no persisted states" and regenerated on the next write.
- **M2-RF-453 [Maps RF-045] Write behavior.** Toggle state is written atomically with the existing state-store semantics, debounced/batched (e.g., at most one write per few seconds and on shutdown). A failed write is logged, non-fatal, and retried on the next change or shutdown. Writes go through the same app-originated-write suppression as config saves (M2-RF-823).
- **M2-RF-454 [Maps RF-045] Reconciliation.** On load, persisted states whose `pageId`/`widgetId` no longer exist are ignored (and pruned on the next write). A missing, corrupt, or shape-mismatched state file is non-fatal and regenerated (RF-073). Persistence applies only to Toggle widgets; no other widget state is persisted.
- **M2-RF-455 [Maps RF-045] Independence from config saves and reload.** Flipping a persisted toggle must not mark the configuration dirty, must not rewrite `panel.json`, and must not trigger a reload attempt. Because the hot-reload watcher is filtered to the effective configuration file only (`panel.json`, M2-RF-821) and app-originated writes are suppressed (M2-RF-823), writing `state.json` can never cause a reload.

### 5.2 Action system

#### RF-057 — Send keys / key synthesis

- **M2-RF-571 [Maps RF-057] Configuration and combination syntax.** A `sendKeys` action sends one **key combination** to the foreground application. Configuration is a required `key` plus a `modifiers` set drawn from `Ctrl`/`Alt`/`Shift`/`Win`. `key` is a friendly name from a fixed mapping (letters `A–Z`, digits `0–9`, function keys `F1–F24`, and a documented set of named keys such as `Tab`, `Enter`, `Escape`, `Space`, `Left`, `Right`, `Up`, `Down`, `Home`, `End`, `PageUp`, `PageDown`, `Insert`, `Delete`, `Backspace`); it resolves to a Win32 virtual-key code. Example: `modifiers: ["Alt"], key: "A"` presses **Alt+A**. There is exactly one simultaneous combination per action; there is **no** sequence, delay, or repeat. Text typing and "any key" are out of scope.
- **M2-RF-572 [Maps RF-057, RF-050] SendInput implementation.** Key down/up events are synthesized through the existing `Interop/Input/SendInputHelper` (Win32 `SendInput`). Modifiers are pressed down before the target key and released after, and are **always released** even if the target-key event fails (no stuck modifiers).
- **M2-RF-573 [Maps RF-057, RF-018, RF-058] Focus semantics.** Keys are delivered to the **foreground** application; TouchPanel never activates itself or changes the foreground to send keys (RF-018 preserved). If no foreground window can accept input, the action fails with a result and log; no crash. A `sendKeys` action bound to a **global hotkey** (RF-058) therefore reaches whatever application currently has focus — which is the intended use case for a widget or hotkey that sends a combination like Alt+A while a game is focused.
- **M2-RF-574 [Maps RF-057] Safe default.** Media keys (M1 RF-048) remain the safe, recommended default. `sendKeys` is an explicit opt-in; the spec documents that it targets whatever has focus, which is a deliberate user choice.
- **M2-RF-575 [Maps RF-057, RF-047] Validation.** Friendly key/modifier names are validated against the fixed mapping; an unknown key name or unknown modifier disables the widget with a reason and a log entry (RF-047). A **modifier-only** combination (empty/absent `key`) and a **malformed** combination are invalid and disable the widget. A bare key with no modifiers is valid for **widget** `sendKeys` (a widget action is user-initiated on the panel); only **global hotkey** bindings require a modifier (D-M2-4).
- **M2-RF-576 [Maps RF-057, RF-050] Failure feedback.** Input-synthesis failure or a rejected call returns a failed result, shows the transient widget error, and logs (RF-036/RF-050). The M1 single-action `ActionExecutionResult` contract is unchanged (RF-050/RF-053).

### 5.3 Input and hotkeys

#### RF-058 — Global hotkeys

- **M2-RF-581 [Maps RF-058, D-M2-4] Configuration.** A top-level `hotkeys` list, each entry with a generated/unique `id`, a `modifiers` set, a `key`, and exactly one bound action identical in shape to a widget's binding (the M1 one-action-per-widget model). Hotkeys are global (system-wide) by definition. Each hotkey **must include at least one modifier** (Ctrl/Alt/Shift/Win) to avoid hijacking bare keys from the foreground application (D-M2-4, §14); a modifier-less entry is invalid (M2-RF-594).
- **M2-RF-582 [Maps RF-058, D-8] Registration mechanism and thread affinity.** Registration uses Win32 `RegisterHotKey` in `Interop/` and a hidden window owned by the UI thread that pumps `WM_HOTKEY`. The hidden window may reuse or follow the M1 `MessageWindow`/`MessageWindowNative` pattern; `RegisterHotKey` does **not** require a visible window. All P/Invoke stays under `Interop/`. Because `RegisterHotKey`/`UnregisterHotKey` and the message window are **thread-affine**, registration, unregistration, and hidden-window creation/recreation are **always marshalled to the UI thread** (never called from a background thread).
- **M2-RF-583 [Maps RF-058, RF-050] Dispatch.** A received `WM_HOTKEY` maps to its configured binding and dispatches the single bound action asynchronously through the existing `IActionExecutor` boundary. Message handling must not block the message pump; dispatch is fire-and-forget with contained failure handling.
- **M2-RF-584 [Maps RF-058, RNF-010] Separation.** Hotkey registration, parsing, and message handling live in `Input/` and `Interop/`; they have no dependency on widgets or view models. Global hotkey actions reuse the executor so action implementations are not duplicated.
- **M2-RF-585 [Maps RF-058, RF-018] No focus stealing.** Handling a hotkey never activates the panel, never calls `SetForegroundWindow`/`Activate`, and never changes the foreground window. The panel remains `WS_EX_NOACTIVATE` in normal mode (RF-018).
- **M2-RF-586 [Maps RF-058] Global reach.** Hotkeys must fire while another application (including a windowed or borderless-fullscreen game) has focus. This is the core M2 input goal. Exclusive-fullscreen limitations from 001 §2 still apply. A hotkey bound to a `sendKeys` action (RF-057) is the canonical way to press a combination such as Alt+A in the focused application.

#### RF-059 — Global hotkey registration failure

- **M2-RF-591 [Maps RF-059] Per-hotkey failure.** A failed registration (e.g., combination already taken) is logged with the full combination (modifiers + key); that hotkey is skipped; registration of the remaining hotkeys **continues**. There is **no** retry loop.
- **M2-RF-592 [Maps RF-059] Aggregated notification.** After attempting all registrations, the app shows **one** aggregated tray notification listing the failed combinations (or a count plus details in the log). Never one balloon per failure.
- **M2-RF-593 [Maps RF-059] Duplicates.** Duplicate combinations within the configuration are detected; the first is registered, later duplicates are skipped and logged as duplicates (deterministic order = configuration order). This is distinct from an OS-level "already taken" failure.
- **M2-RF-594 [Maps RF-059, RF-047, RF-079] Invalid entries.** A hotkey with an unknown key/modifier, no modifier (D-M2-4), or a missing binding is invalid: it is skipped (not registered) and logged, and does not prevent other hotkeys. Invalid entries do not disable unrelated widgets.
- **M2-RF-595 [Maps RF-058, RF-082] Re-registration.** On hot reload or shutdown, all registered hotkeys are unregistered and re-registered from the effective configuration: unregister with `UnregisterHotKey`, release the hidden window, and re-apply. Unregistration/re-registration and any hidden-window (re)creation are marshalled to the UI thread (M2-RF-582). No leaked registrations across reloads.

#### RF-060 — Local shortcuts **[FROZEN — backlog 2026-10-09]**

- **M2-RF-601 [Maps RF-060] Separation.** Local shortcuts (active only when appropriate) are registered/handled separately from global `RegisterHotKey` registrations. They must not use global registration and must not fire when the panel does not have focus / edit mode is not active.
- **M2-RF-602 [Maps RF-060, RF-025] Scope.** Local shortcuts are only active in the contexts where they make sense (edit mode and, where defined, when the panel window has keyboard focus). Normal-mode operation must remain fully usable without keyboard focus and must not depend on local shortcuts (RF-025).
- **M2-RF-603 [Maps RF-060, RF-027] M2 fixed set.** M2 defines a small fixed set of edit-mode local shortcuts layered on the M1 ones (Esc exit, arrow nudge): remove selected widget, duplicate selected widget, and toggle snap. These are app-defined, not user-configurable (the hotkey-management UI is M3/RF-061).
- **M2-RF-604 [Maps RF-060, RF-018] No focus side effects.** Defining or activating local shortcuts must not change the normal-mode no-activate behavior; outside their context they are inert.

### 5.4 Monitoring

#### RF-069 — Bounded history

- **M2-RF-691 [Maps RF-069, RNF-012] Ring buffers.** The monitoring layer owns one fixed-capacity ring buffer per tracked metric. Capacity = `ceil(windowSeconds / intervalSeconds)` clamped to a documented maximum (e.g., `3600` samples) and a minimum. The buffers are never unbounded collections.
- **M2-RF-692 [Maps RF-069, RF-067] Thread safety.** Buffers are written by the polling thread and read by the UI thread. Reads return a consistent slice/copy (lock-protected ring or immutable snapshot); readers never observe torn or partially written samples. Ring writes are O(1) and allocation-free per sample (amortized).
- **M2-RF-693 [Maps RF-069] Tracked metric set.** Buffers are created lazily for the metric keys actually referenced by the current configuration (Sparkline widgets, and the Gauge's current sensor where useful). On config reload/page change, keys no longer referenced are released. Per-core keys participate in the same lazy scheme, so unreferenced per-core buffers cost nothing.
- **M2-RF-694 [Maps RF-069, RNF-012] Memory documentation.** The per-metric and worst-case memory is documented (capacity × tracked metrics × sample size + ring overhead) and stated in the acceptance criteria. The tracking set is bounded.
- **M2-RF-695 [Maps RF-069, RF-071] Interval coupling.** When the polling interval changes (RF-071), capacity is recomputed for the same window. A capacity change preserves as much existing history as still fits, oldest-dropped.
- **M2-RF-696 [Maps RF-069, RF-063] Unavailable samples.** Unavailable readings are stored as an explicit "unavailable" marker (not zero) and rendered as gaps (M2-RF-414).
- **M2-RF-697 [Maps RF-069, RF-066] Disposal.** History buffers are cleared/released with monitoring disposal and on shutdown; no lingering references keep samples alive.

#### RF-070 — Backend re-initialization **[FROZEN — backlog 2026-10-09]**

- **M2-RF-701 [Maps RF-070, RF-065] Throttled retry.** When the monitoring backend (LibreHardwareMonitor adapter) has failed to initialize or is degraded, the app may retry initialization periodically, throttled (e.g., at most once per `5 min` *(default)*), logged at a throttled rate. No busy retry, no tight loop.
- **M2-RF-702 [Maps RF-070, RF-012] Recovery.** A successful retry re-marks the `monitoring` subsystem restored (using the existing `SubsystemHealth`) and resumes normal snapshots; history buffers continue to be fed.
- **M2-RF-703 [Maps RF-070] Bounded and optional.** Retry is bounded in frequency and never blocks the loop or the UI thread; it must not request elevation (RF-086) and must degrade gracefully if the backend cannot recover.
- **M2-RF-704 [Maps RF-070, RF-074/RF-082] Config/visibility.** Retry cadence is an internal default (not necessarily user-facing in M2); behavior is logged and observable. Failure remains non-fatal and never takes down other subsystems (RF-012/RNF-013).

#### RF-071 — Configurable polling interval **[FROZEN — backlog 2026-10-09]**

- **M2-RF-711 [Maps RF-071] Configuration.** A configurable telemetry interval within `0.5–5 s` (default `1 s`). Values outside the range are clamped with a warning (entry-level containment, RF-079). The field arrives through schema migration (M2-RF-825).
- **M2-RF-712 [Maps RF-071, RF-082] Live application.** Changing the interval on hot reload restarts/adjusts the polling loop with the new interval and recomputes ring capacity (M2-RF-695). Applying the new interval must not leave two loops running and must not lose the latest snapshot beyond a race tolerance.
- **M2-RF-713 [Maps RF-071, RNF-003] Rate discipline.** The effective polling rate equals the configured interval; data-driven UI updates remain at most once per snapshot. No faster-than-configured polling is introduced.
- **M2-RF-714 [Maps RF-071, RF-067] Threading.** Interval changes are applied on the background monitoring path (or marshalled safely), never by directly mutating UI state from a non-UI thread.

### 5.5 Configuration

#### RF-082 — Hot reload **[FROZEN — backlog 2026-10-09]**

- **M2-RF-821 [Maps RF-082, RNF-008] Detection scoped to the effective configuration file.** External changes are detected with a `FileSystemWatcher` on the config directory whose `Filter` is the effective configuration file name **only** (`panel.json`) and whose `NotifyFilter` is limited to `LastWrite | FileName | Size | CreationTime` (no attribute/security events). The config directory also holds `panel.backup.json`, `state.json`, `panel.recovery.json`, `panel.corrupt-*.json`, and the `panel.json.tmp` used by atomic save (RF-077); because the watcher is filtered to `panel.json`, writes to the temp/backup/state/recovery/corrupt files generate **no** reload event. Events are **debounced** (e.g., `400 ms` *(default)*) and coalesced into one reload attempt. No filesystem polling (RF-080/RNF-008). Hot reload is therefore scoped to `panel.json` alone.
- **M2-RF-822 [Maps RF-082, F-21] Validate-then-apply as a whole.** A reload reads the new file, migrates if needed, and validates it fully into a **candidate** configuration. Only a fully valid candidate is applied, by an atomic in-memory swap. On any parse/migration/validation failure the **currently running configuration stays active**, the failure is logged and notified, and nothing is partially applied (F-21).
- **M2-RF-823 [Maps RF-082, RF-080] Single-writer reconciliation for all app writes (no polling).** The app's own writes must not trigger a self-reload. In addition to the file filter (M2-RF-821), the reload controller suppresses **all application-originated writes** — the config store (`IConfigStore.SaveAsync`, including the temp + atomic-replace of `panel.json`) **and** the runtime-state store (`state.json` toggle persistence, M2-RF-453) — by (a) marking a self-write window around every app write, and (b) ignoring events whose resulting file content is byte-identical (or hash-equal) to the last content the app wrote. This prevents reload storms and preserves the single-writer rule without adding polling, and remains correct under rapid user edits and app saves.
- **M2-RF-824 [Maps RF-082] Apply scope.** An accepted reload applies, on the UI thread where UI state is involved: window options (including live `alwaysOnTop`), the active page's widgets (dispose old page VMs, build new), monitor target (re-evaluate per RF-016/017), telemetry interval (RF-712), tracked metric set (M2-RF-693), and hotkey registrations (M2-RF-595, marshalled to the UI thread per M2-RF-582). Old pages/hotkeys/buffers are disposed/released so nothing leaks.
- **M2-RF-825 [Maps RF-082, RF-074/RF-076] Schema migration on reload.** A reload of an older-but-supported schema migrates in memory and validates the result before applying. A newer/future schema is **not** interpreted (RF-075.4): the running configuration stays, the external file is left untouched, and the user is notified. Hot reload **never writes** to the watched file.
- **M2-RF-826 [Maps RF-082, RF-075] Invalid external file.** A corrupt/invalid external edit keeps the running configuration, logs the specific diagnostics, and notifies (throttled). The user's file is never overwritten or preserved-copied by the reload path (preservation remains a startup-load concern, RF-075/RF-078).
- **M2-RF-827 [Maps RF-082, RF-012] Watcher resilience.** Watcher creation/read failures (directory unavailable, access denied, buffer overflow) degrade gracefully: the app continues with the running configuration, logs, and (where possible) re-establishes the watcher. No crash, no exit (RF-012/RNF-013).
- **M2-RF-828 [Maps RF-082, RF-007] Shutdown.** The watcher and any pending debounce timer are disposed on shutdown within the budget; no reload work runs after shutdown begins.
- **M2-RF-829 [Maps RF-082] Notification throttling.** Reload failure notifications are aggregated/throttled so a repeatedly-edited invalid file does not spam balloons (RF-010 spirit).
- **M2-RF-830 [Maps RF-082, RNF-006] Pure reload-decision component.** The reload semantics (given a normalized filesystem event plus the app's last-written content and in-flight-write state, decide: **ignore**, **debounce-and-wait**, or **reload-from-file**) are implemented as a **pure, injectable decision component** (event → decision, no ambient I/O). The OS `FileSystemWatcher` and the store are thin adapters feeding it. This lets hot-reload semantics — self-write suppression, coalescing, content-unchanged no-op, invalid-file rejection — be unit-tested **deterministically without touching the file system**; the real watcher is exercised only in integration/manual tests.

#### RF-083 — Import / export **[FROZEN — backlog 2026-10-09]**

- **M2-RF-831 [Maps RF-083] Export.** Export writes the JSON configuration to a user-chosen location, including the current `schemaVersion`. M2 has no secret-bearing configuration fields (HTTP is dropped), so no secret redaction is required; the general M1 RF-085 rule (never log sensitive content) still applies, and export never logs configuration content.
- **M2-RF-832 [Maps RF-083, RF-075/RF-076] Import validation and backup.** Import validates the chosen file fully (parse → migrate older supported → validate) **before** applying anything. The current configuration is backed up first (a timestamped copy and/or the existing `panel.backup.json` mechanism). A failed import changes nothing and reports the diagnostics.
- **M2-RF-833 [Maps RF-083] Apply and persist.** A successful import replaces the effective configuration and persists atomically through the existing store (RF-077), then triggers the same apply pipeline as a hot reload (M2-RF-824). Imported content is untrusted and subject to RF-081.
- **M2-RF-834 [Maps RF-083, RF-085] No content logging.** Import/export log only file paths and result summaries, never configuration content (RF-085).
- **M2-RF-835 [Maps RF-083, RNF-004] UI path.** Import/export file I/O runs off the UI thread; a file picker is used where an interactive surface exists (settings window, RF-092). Failures (canceled dialog, unreadable file, unwritable target) are reported without crashing and without partial writes.

### 5.6 Deferred UI features

#### RF-091 — Page-switching UI **[FROZEN — backlog 2026-10-09]**

- **M2-RF-911 [Maps RF-091, RF-037] In-panel affordance.** A touch-friendly affordance switches the active page among configured pages (e.g., a page selector/tab strip; the exact visual is an implementation choice). It is reachable in normal mode.
- **M2-RF-912 [Maps RF-091, RF-037] Persistence and fallback.** Switching updates `activePageId` with the M1 validation/fallback rules and persists it atomically through the store. A page id that becomes invalid falls back to the first page and logs (RF-037).
- **M2-RF-913 [Maps RF-091, RF-018] No focus stealing.** Page switching works without activating the panel or changing the foreground window (RF-018/RF-025).
- **M2-RF-914 [Maps RF-091, RF-012] Rebuild isolation.** Switching disposes the outgoing page's widget view models (including their subscriptions) and builds the incoming page. A rebuild failure leaves the panel without widgets but stable (mirrors M1 `AttachActivePage`).
- **M2-RF-915 [Maps RF-091, RF-082] No reload loop.** The page-switch save is a self-save and must not trigger a hot reload (M2-RF-823).

#### RF-092 — Settings window **[FROZEN — backlog 2026-10-09]**

- **M2-RF-921 [Maps RF-092] Scope.** A tray-accessible settings window edits configuration options available in M2: window behavior (`alwaysOnTop`), start-with-Windows, telemetry interval, and target-monitor selection (via RF-093), plus import/export (RF-083) and read-only diagnostics (config path, current monitor identity, schema version). Hotkey editing is **not** in this window (RF-061 is M3).
- **M2-RF-922 [Maps RF-092, RF-081/RF-077] Validation and atomic save.** Settings inputs are validated with the same rules as the config validator; valid changes are saved atomically through the existing store. Invalid input is blocked/flagged inline; a save failure is surfaced and does not partially apply.
- **M2-RF-923 [Maps RF-092, RF-082] Live application.** Where possible, accepted settings apply live through the shared apply pipeline (window topmost, telemetry interval, monitor target) without a restart; start-with-Windows toggling reuses M1 registration (RF-003/RF-004). A restart is never required for these options.
- **M2-RF-924 [Maps RF-092, D-6/RF-018] Activation.** The settings window is an ordinary activating window (deliberate user action, analogous to edit mode D-6). Opening/closing it must not change the panel's normal no-activate behavior.
- **M2-RF-925 [Maps RF-092, RF-012] Failure isolation.** A failure building or showing the settings window is logged and isolated; the panel and tray keep working.

#### RF-093 — Monitor picker **[FROZEN — backlog 2026-10-09]**

- **M2-RF-931 [Maps RF-093, RF-016] Discovery listing.** The picker lists all discovered monitors with their stable identity fields (EDID hardware id when available, device path, friendly name), resolution, position, and primary flag, using the existing `IMonitorEnumerator`/`MonitorDescriptor`.
- **M2-RF-932 [Maps RF-093, RF-016/RF-015] Selection persistence.** Selecting a monitor writes the corresponding identity block (`hardwareId`/`devicePath`/`friendlyName`) into `monitor.match` and applies placement (potentially via the apply pipeline) without a restart. No hand-editing of JSON is required.
- **M2-RF-933 [Maps RF-093, RF-017] Current and unavailable targets.** The current target is highlighted; if the configured target is unavailable, the picker shows that clearly so the user can reselect. Fallback policy can also be chosen (none/primary) with the M1 warning semantics (never silent).
- **M2-RF-934 [Maps RF-093, RF-016] Deterministic matching.** The stored identity uses the same deterministic priority/tiebreak semantics as `MonitorMatcher`; a monitor with no stable fields warns before saving.

### 5.7 Security

> RF-087 (HTTP safety constraints), and therefore the whole `M2-RF-87x` security block, is **dropped with HTTP (2026-10-09)** and has no sub-requirements. General untrusted-configuration handling remains governed by M1 RF-081 and the M2 action requirements (M2-RF-434, M2-RF-571/575); no new execution surface is introduced by M2 (open program is the existing safe-launch path, and send-keys is modeled data).

## 6. Non-functional requirements

- **M2-RNF-121 [Maps RNF-012] Bounded-history memory documented.** The per-metric capacity and worst-case memory for ring buffers are documented in the spec/README and verified in acceptance (M2-RF-691/M2-RF-694). Runtime memory stays within the M1 RNF-002 target.
- **M2-RNF-122 [Maps RNF-003, RNF-008] No polling; debounced reload.** Hot reload uses a debounced `FileSystemWatcher` (no polling). Idle CPU with hotkeys registered, history enabled, and no sparkline update must stay within RNF-001.
- **M2-RNF-123 [Maps RNF-004] UI-thread discipline.** File I/O, hotkey message dispatch, history reads, and reload apply never block the WPF UI thread; long operations are async with cancellation; UI state mutation is marshalled.
- **M2-RNF-124 [Maps RNF-006] Testability.** M2 core logic is unit-testable **without** hardware, drivers, elevation, or real network: send-keys mapping (no real input behind a fake input seam), hotkey parsing/registration-result handling (fake registration seam), ring buffers (deterministic), slider/sparkline view models (fake snapshot/history source), edit operations (fake store), **hot-reload decision (pure injectable event→decision component per M2-RF-830; the OS watcher is an integration concern only)**, and import/export (temp dir). `dotnet test` remains the acceptance gate.
- **M2-RNF-125 [Maps RNF-010] Layering.** `Actions/` (send-keys; the existing safe process-launch handler is unchanged), `Monitoring/` (rings, interval, retry), `Config/` (hot reload, import/export), and `Input/` (hotkeys, local shortcuts) remain WPF-free; P/Invoke stays in `Interop/`; the layer-purity architecture test is extended to the new namespaces. The `Interop/` message-window exception for hotkeys mirrors M1's contained exception.
- **M2-RNF-126 [Maps RNF-010, RNF-013] Isolation.** Hotkey, reload, history, and edit-UI failures are isolated; the panel/tray/config core keeps working (RF-012). No M2 feature may introduce an elevation prompt.
- **M2-RNF-127 [Maps RNF-002] Memory.** Ring buffers + history + edit UI additions must not introduce unbounded growth; the 8-hour soak (AC-M2-24) must hold the M1 memory targets.
- **M2-RNF-128 [Maps RNF-014, RF-081] No new execution surface.** M2 adds no shell-open of files/folders/URLs and no HTTP. "Open program" uses the existing M1 safe-launch path (RF-049); send-keys and hotkey combinations are validated data executed only through the action layer. Configuration remains untrusted (RF-081).

## 7. Milestone scoping and workstreams

The M2 set is large — roughly the size of M1 — and touches three largely independent subsystems (actions/input, monitoring/history, and configuration/UI). The requirements above are complete for M2.

> **Scope freeze (2026-10-09, D-M2-8 / 001 D-24).** Only the **Core Release** is delivered and supported: **Sparkline/graph + bounded history** (RF-041, RF-069, RNF-012), **global hotkeys** (RF-058, RF-059), **send-keys** (RF-057), and **visual customization** (RF-043 appearance only). The workstream/phase text below is retained as the historical implementation ordering; the parts that produced frozen features (RF-040/044/045, RF-060, RF-070/071, RF-082/083, RF-091/092/093, and the RF-043 non-appearance parts) are **not continued**. See §7.3 for the delivered-vs-frozen mapping.

**Approved delivery boundary (D-M2-1, §14).** M2 stays a **single milestone** (no M2a/M2b milestones and no sub-milestone requirement labels), delivered in **two execution phases** (historical):

- **Phase 1 — Actions, input, telemetry, widgets (workstreams W1–W5).** Schema migration (current schema **v3**, delivered via the ordered `v1→v2→v3` chain; Phase 1 delivers `v1→v2` with the `sendKeys` action discriminator, telemetry interval, and ring-buffer/toggle fields; Phase 2/T-14 adds `v2→v3` appearance overrides); the send-keys action; global/local hotkeys; monitoring ring buffers + configurable interval + throttled backend retry; Slider, Sparkline, toggle persistence. The per-widget action model remains **one action per widget**; "open program" is the existing M1 run-command action.
- **Phase 2 — Configuration & UI surface (workstream W6).** Hot reload, import/export, page-switching UI, full edit UI + grid/guides (including the **action picker with executable browse** for "open program"), settings window, monitor picker. Deliverable: all M2 features configurable by touch and live-reloadable.

The requirement set and acceptance criteria in this spec are identical under either phase; only the delivery boundary and ordering change. The ordering below is how the workstreams should be implemented.

### 7.1 Workstreams and dependency order

```text
Phase 1 — Actions, input, telemetry, widgets
W1 Schema migration + validation (current schema v3 via the ordered v1→v2→v3 chain:
       v1→v2 adds the sendKeys action discriminator, telemetry interval, ring-buffer/toggle
       fields; v2→v3 adds appearance overrides in Phase 2/T-14; single action per widget preserved)
        │
        ├─► W2 Actions: send-keys (SendInput, e.g. Alt+A); open program = existing M1 run-command
        │        │
        │        └─► W3 Input: global hotkeys (RegisterHotKey), failure aggregation, local shortcuts
        │
        ├─► W4 Monitoring: ring buffers, tracked-metric set, live interval, throttled retry
        │
        └─► W5 Widgets: Slider (value→action), Sparkline (history), toggle persistence
                  │
Phase 2 — Configuration & UI surface
                  └─► W6 Configuration/UI surface: hot reload (self-write suppression),
                                                  import/export (validate+backup), page switching,
                                                  full edit UI + grid/guides + action picker/browse,
                                                  settings window, monitor picker
```

- **W1 first** because every later stream needs the new schema fields and the ordered migration chain (RF-076) so `panel.json` stays the single source of truth and older (v1/v2) files keep loading.
- **W2** defines the new single-action type (`sendKeys`) and its validation; hotkeys (W3) reuse the same action-binding shape. "Open program" needs no new code — it is the M1 run-command handler, surfaced in the W6 edit UI.
- **W3** provides global/local hotkeys and may bind either an M1 action (incl. run-command "open program") or a W2 `sendKeys` action.
- **W4 independent of W2/W3**; it can proceed after W1. Sparkline (W5) depends on W4.
- **W5** depends on W1 (widget config), and Sparkline depends on W4.
- **W6 is the Phase 2 boundary** and depends on W1–W5 because the edit UI/action picker/settings must expose the new types, and hot reload must re-apply them.

### 7.2 Phase boundary and rationale

M2 is a **single milestone** (approved: D-M2-1). It is delivered in two execution phases so each phase is independently reviewable, testable, and shippable, and the highest-risk work (focus preservation, key-synthesis) is validated before the UI that exposes it:

- **Phase 1 (W1–W5).** Schema migration (ordered `v1→v2→v3`; Phase 1 delivers `v1→v2`, Phase 2/T-14 adds `v2→v3`); send-keys action; global/local hotkeys; ring buffers + configurable interval + throttled retry; Slider, Sparkline, toggle persistence. ("Open program" is available via hand-edited JSON using the existing M1 run-command action.)
- **Phase 2 (W6).** Hot reload; import/export; page-switching UI; full edit UI + grid/guides + action picker/browse; settings window; monitor picker.

Phase 1 is fully functional via hand-edited JSON (consistent with M1's JSON-first workflow) and exercises every `[M2]` functional requirement except the W6 UI surfaces (RF-043 full edit UI beyond M1 geometry, RF-044 grid/guides, RF-082, RF-083, RF-091, RF-092, RF-093) which Phase 2 delivers. The requirement set and acceptance criteria are unchanged by this ordering; there are **no** `M2a`/`M2b` requirement labels and no 001 retagging.

### 7.3 Delivered Core Release vs frozen backlog (2026-10-09)

| 001 base | Sub-requirements | Status |
|---|---|---|
| RF-041 Sparkline | M2-RF-411..416 | **Core (delivered/supported)** |
| RF-069 bounded history | M2-RF-691..697 | **Core** |
| RNF-012 bounded history | M2-RNF-121 (with M2-RNF-122..128 constraints) | **Core** |
| RF-057 send-keys | M2-RF-571..576 | **Core** |
| RF-058 global hotkeys | M2-RF-581..586 | **Core** |
| RF-059 hotkey failure | M2-RF-591..595 | **Core** |
| RF-043 appearance | M2-RF-435 (label/icon), M2-RF-436 (colors/font/opacity) + schema-v3 appearance fields | **Core** |
| RF-040 Slider | M2-RF-401..406 | **Supported via M4 (2026-10-10)** |
| RF-043 non-appearance | M2-RF-430, 431, 432, 433, 434, and 437..440 (insofar as they cover those) | **Supported via M4 (2026-10-10)** |
| RF-044 grid/alignment | M2-RF-441..446 | **Supported via M4 (2026-10-10)** |
| RF-045 toggle persistence | M2-RF-451..455 | **Supported via M4 (2026-10-10)** |
| RF-060 local shortcuts | M2-RF-601..604 | **Supported via M4 (2026-10-10)** |
| RF-070 backend re-init | M2-RF-701..704 | **Supported via M4 (2026-10-10)** |
| RF-071 polling interval | M2-RF-711..714 | **Supported via M4 (2026-10-10)** |
| RF-082 hot reload | M2-RF-821..830 | **Supported via M4 (2026-10-10)** |
| RF-083 import/export | M2-RF-831..835 | **Supported via M4 (2026-10-10)** |
| RF-091 page switching | M2-RF-911..915 | **Supported via M4 (2026-10-10)** |
| RF-092 settings window | M2-RF-921..925 | **Supported via M4 (2026-10-10)** |
| RF-093 monitor picker | M2-RF-931..934 | **Supported via M4 (2026-10-10)** |
| M3 RF-046/061/084 | — | **Supported via M3 (2026-10-10)** |
| RF-072 | — | **Frozen/skipped** |
| RF-042/054/055/056/087, `delay`, shell-open | — | **Dropped (not backlog)** |

The M4 column reflects `specs/004-promotion/spec.md` / `001 D-25`: the promoted features are now **supported and maintained**, verified against the M2 sub-requirements listed. Requirement IDs are unchanged.

## 8. Acceptance criteria

Each criterion is automated (A), manual (M), or both. "MV" refers to the M2 manual verification matrix to be defined in the M2 plan.

- **AC-M2-01 (A) Build & tests.** The solution builds with the .NET 10 SDK; `dotnet test` passes with the new M2 unit tests. The M1 suite stays green. (M2-RNF-124)
- **AC-M2-02 (A) Schema migration to current (v3).** A v1 (and a v2) configuration migrates to the **current schema v3** through the ordered, additive `v1→v2→v3` chain (`v1→v2` adds the `sendKeys` action discriminator, the telemetry interval, and the ring-buffer/toggle fields; `v2→v3` adds per-widget appearance overrides; the per-widget `action` remains a **single** action; no data loss), validates, and saves; a failing migration follows the invalid path and leaves the source intact. All four RF-075 load states remain covered. (M2-RF-825, RF-075/RF-076, D-M2-7)
- **AC-M2-03 (A) Ring buffers.** Capacity = `ceil(window/interval)`, clamped; O(1)/allocation-free appends; concurrent write/read returns consistent slices; unavailable samples stored as gaps; capacity recomputed on interval change; unreferenced metrics released. (M2-RF-691..697, M2-RNF-121)
- **AC-M2-04 (A) Interval.** Interval validated/clamped to `0.5–5 s`, default `1 s`; live change restarts the loop without duplication and recomputes capacity. (M2-RF-711..714)
- **AC-M2-05 (A) Single-action model unchanged.** A widget and a hotkey each bind exactly one action; the M1 `ActionExecutionResult` no-throw contract, failure feedback (RF-036), and redaction (RF-051) are unchanged; no sequence/delay/repeat configuration field exists. (RF-047, RF-050, RF-053)
- **AC-M2-11 (A) Send-keys.** Combination syntax (`modifiers` + `key`) parsed; friendly-name→VK mapping for letters/digits/F-keys/named keys; modifiers pressed before and released after the target key and **always released** on failure (no stuck modifiers); unknown key/modifier, modifier-only, and malformed combinations are invalid and disable the widget; a bare key without modifiers is valid for a **widget** `sendKeys` but a **global hotkey** requires a modifier (D-M2-4); failure returns a result. (M2-RF-571..576)
- **AC-M2-12 (M) Send-keys live.** A `sendKeys` action sends a combination such as **Alt+A** into a focused application (e.g., Notepad) from a widget and from a global hotkey; media keys remain functional; no focus change by TouchPanel. (M2-RF-573, M2-RF-586)
- **AC-M2-13 (A) Hotkey logic.** Parsing, duplicate detection, invalid-entry skipping (including **modifier-less entries**, D-M2-4), and registration-failure aggregation (skip failed, continue others, one notification, no retry loop) are unit-tested with a fake registration seam; registration/unregistration is asserted to run on the UI thread. (M2-RF-581..595)
- **AC-M2-14 (M) Hotkey live + focus.** A global hotkey fires its action while Notepad and while a windowed/borderless game has focus; the foreground window and typing are uninterrupted; a deliberately conflicting combination is skipped and aggregated. (M2-RF-586, M2-RF-585, RF-018)
- **AC-M2-15 (A) Slider + Sparkline view models.** Slider range/step/orientation/value validation and the `volume Set` value mapping; throttled emission; unsupported action disables the slider; Sparkline maps history to points with gaps, auto/manual scale, no polling, update-on-change only. (M2-RF-401..416)
- **AC-M2-16 (M) Slider + Sparkline live.** Slider drag controls system volume smoothly; Sparkline shows live history that scrolls within the window and never grows unbounded. (M2-RF-403, M2-RF-412)
- **AC-M2-17 (A) Edit operations.** Add/remove/duplicate (concrete default geometry, fixed 16 DIP duplicate delta), property edits, action picker (**including the executable browse for "open program"/run-command**, M2-RF-434), label/icon (`0–128` chars, 1–64 bundled icons), color/font-size/opacity bounds, are unit-tested at the view-model level; invalid input is clamped/blocked; changes are dirty-tracked and persisted only through the store; actions never fire in edit mode. (M2-RF-430..440, M2-RF-441..446)
- **AC-M2-18 (M) Edit UI on touch.** In edit mode the user adds/removes/duplicates widgets, edits properties/actions/labels/icons/colors, and uses grid snap + alignment guides entirely by touch; Esc/nudge/shortcuts work; changes survive restart. (M2-RF-437, M2-RF-444, RF-039)
- **AC-M2-19 (A) Hot reload decision.** The pure event→decision component (M2-RF-830) is unit-tested with no file system for: validation-before-apply; invalid external file keeps the running config with no partial apply; self-write suppression for **config-store and state-store** writes; content-unchanged no-op; future schema not interpreted and not written. The watcher is additionally exercised in a temp-dir integration test confirming it is filtered to `panel.json` and that writing `state.json`/temp/backup files produces no reload event. (M2-RF-821..830)
- **AC-M2-20 (M) Hot reload live.** Externally editing valid JSON applies live (widgets/topmost/interval/hotkeys) without restart; editing invalid JSON keeps the panel running and notifies; the app's own saves (edit mode, page switch, settings) never trigger a reload. (M2-RF-823)
- **AC-M2-21 (A+M) Import/export.** Export writes valid JSON including `schemaVersion`; import validates fully and backs up before applying; a failed import changes nothing; older-schema import migrates; logs contain paths/summaries only, never content. (M2-RF-831..835)
- **AC-M2-22 (M) Deferred UI.** Page switching persists `activePageId` without focus stealing and without a reload loop; the settings window edits window/startup/interval/monitor and applies live; the monitor picker lists stable identities, highlights the current target, and writes a selection without restart. (M2-RF-911..915, M2-RF-921..925, M2-RF-931..934)
- **AC-M2-23 (A+M) Toggle persistence.** A `persistState` toggle restores after restart; a non-persisting toggle resets; the persisted shape is the `toggleStates` map keyed `"<pageId>/<widgetId>"`; an absent/shape-mismatched map is non-fatal and regenerated; state files are written atomically. **Positive test:** a toggle flip writes `state.json` (no `panel.json` change) and the reload controller makes **no reload attempt** (watcher filtered to `panel.json`, M2-RF-821/823/455). (M2-RF-451..455)
- **AC-M2-24 (M) Performance & soak.** Idle CPU with hotkeys + history + idle UI stays within RNF-001; no filesystem polling; memory stays within RNF-002 over an 8-hour soak; history memory matches the documented bound. (M2-RNF-121/122/127, RNF-001/RNF-002)
- **AC-M2-25 (A+M) Layering & security.** The layer-purity test covers `Actions/`, `Monitoring/`, `Config/`, and `Input/` (WPF-free); P/Invoke is confined to `Interop/`; no M2 feature prompts for elevation; hostile configuration (executable paths/arguments, key combinations, hotkey combinations, colors, geometry) is contained, never executed or trusted; and M2 adds no shell-open or HTTP surface. (M2-RNF-125/126/128, RF-081)

## 9. Failure cases and required behavior

| ID | Scenario | Required behavior | Refs |
|---|---|---|---|
| F-M2-01 | Global hotkey combination already taken | Log combination, skip, continue others, one aggregated notification, no retry loop | RF-059, M2-RF-591/592 |
| F-M2-02 | Invalid hotkey entry (unknown key/binding) | Skip + log; other hotkeys and widgets unaffected | M2-RF-594 |
| F-M2-03 | Hotkey action fails | Contained failure, redacted log, no focus change, app stable | M2-RF-583, RF-050 |
| F-M2-10 | SendInput rejected / no foreground window | Failed result, widget error, log; no crash; no stuck modifiers | M2-RF-572/576 |
| F-M2-11 | Monitoring backend failed | Unavailable snapshots; throttled retry; subsystem degraded/restored; no crash | RF-065/070, M2-RF-701/702 |
| F-M2-12 | Config interval out of bounds | Clamp to `0.5–5 s` with warning; effective rate matches | M2-RF-711 |
| F-M2-13 | External config edit is invalid/corrupt | Keep running config; no partial apply; log+notify; never write the watched file | RF-082/F-21, M2-RF-822/826 |
| F-M2-14 | External config edit is a newer schema | Do not interpret or write; keep running config; notify | RF-075.4, M2-RF-825 |
| F-M2-15 | App saves config while watcher active | Self-write suppressed; no reload loop; single-writer preserved | RF-080/082, M2-RF-823 |
| F-M2-16 | File watcher unavailable/overflow | Stay running with current config; log; attempt re-establish | RF-012, M2-RF-827 |
| F-M2-17 | Import invalid / file unreadable | Abort with diagnostics; current config intact; pre-import backup preserved | M2-RF-832 |
| F-M2-18 | Import/export must not log configuration content | Log only file paths and result summaries; never log config content (M1 RF-085 still applies) | RF-083/085, M2-RF-834/835 |
| F-M2-19 | Toggle-state file corrupt/missing | Non-fatal; regenerate; ignore stale keys | RF-073, M2-RF-454 |
| F-M2-20 | Edit-UI save fails | Keep in-memory edits; keep dirty flag; notify; retry on next save/shutdown | RF-077, M2-RF-437 |
| F-M2-21 | Page switch with invalid `activePageId` | Fall back to first page with log; persist the resolved id | RF-037, M2-RF-912 |
| F-M2-22 | Sparkline sensor unavailable/unknown | Gaps / "Unavailable"; unknown key disables the widget per RF-047 | M2-RF-414/411 |
| F-M2-23 | Toggle flip / app-originated write lands in the config directory (`state.json`, temp, backup, recovery) | No reload attempt: watcher filtered to `panel.json` only and all app writes suppressed | M2-RF-455/821/823, AC-M2-23 |
| F-M2-25 | Hotkey configured without a modifier | Treat as invalid: skip + log; continue other hotkeys | D-M2-4, M2-RF-581/594 |
| F-M2-26 | Persisted `toggleStates` map absent or shape-mismatched | Non-fatal: treat as no persisted states; regenerate on next write | M2-RF-452/454 |
| F-M2-27 | `sendKeys` combination malformed / modifier-only / unknown key | Invalid: disable the widget or skip the hotkey with a logged reason; no input synthesized | M2-RF-571/575, RF-047 |

## 10. Test strategy

Aligned with M1 (plan §6): unit tests are the gate, hardware/network are excluded, temp directories isolate the profile, and WPF-free layer purity is enforced.

### 10.1 Unit tests (no hardware, no drivers, no elevation, no real network)

- **Actions:** single-action dispatch through `ActionExecutor`; `sendKeys` combination parsing/mapping and failure mapping; the existing run-command ("open program") launch request construction (discrete args, hostile strings, `UseShellExecute=false`); result mapping and redaction; no sequence/delay/repeat fields exist.
- **Send-keys:** friendly-name→VK mapping, modifier ordering, always-release, invalid-key validation, failure mapping (input synthesis behind an interface so no real input occurs).
- **Hotkeys:** combination parsing, duplicate detection, invalid-entry skipping, registration-result handling (failed → skip + aggregate), re-registration on reload (fake registration seam).
- **Monitoring history:** capacity math, O(1)/allocation-free appends, concurrent read consistency, gap storage, capacity recompute on interval change, tracked-metric set add/release.
- **Widget view models:** Slider validation/value mapping/throttling/unsupported-action disable; Sparkline history→points mapping, auto/manual scale, gaps, no-poll, update-on-change; Toggle persistence read/write/reconcile against a fake state store.
- **Edit operations:** add/remove/duplicate id generation, property/action/color/label-icon editing, grid/alignment math, clamp composition, dirty tracking, save-through-fake-store, no action execution in edit mode.
- **Hot reload:** temp-dir watcher/debounce decision, validate-before-apply, invalid keeps running config, self-write suppression (simulate app save vs external change), no polling assertion.
- **Import/export:** export writes valid JSON including `schemaVersion`; import validates and migrates and takes a pre-import backup; a failed import leaves the config intact; only paths/summaries are logged.
- **Config:** schema `v1→v2→v3` migration chain, interval bounds, new action/widget DTO round-trips, appearance-override round-trips, unknown-type preservation.
- **Architecture:** layer-purity test extended to `Actions/`, `Monitoring/`, `Config/`, `Input/` (no WPF types); P/Invoke confined to `Interop/`.

### 10.2 Integration tests (still no real hardware/network)

- `MonitoringService` with a fake sensor source: history population, live interval change, throttled backend retry with fake `TimeProvider`.
- `JsonConfigStore` with a temp directory for reload self-suppression and import/export round-trips.
- `ActionExecutor` with fake handlers plus one harmless real process launch (as M1 does) and one `sendKeys` action against a fake input seam.

### 10.3 Manual verification matrix (defined fully in the M2 plan; shape here)

- **MV-M2 focus matrix:** a global hotkey bound to `sendKeys` (e.g. Alt+A) while Notepad and while a windowed/borderless game is focused; verify the combination reaches the foreground app, foreground/typing uninterrupted, no activation.
- **MV-M2 slider/sparkline:** touch drag controls volume; sparkline renders live history within the window.
- **MV-M2 edit UI:** touch-only add/remove/duplicate/edit/color/icon/snap/guides; restart persists.
- **MV-M2 hot reload:** external edit applies live; invalid edit keeps running; app's own saves do not reload.
- **MV-M2 import/export/settings/monitor picker:** UI flows, pre-import backup, only paths/summaries logged.
- **MV-M2 performance:** idle CPU with hotkeys+history (RNF-001); 8-hour soak memory (RNF-002/RNF-127); documented history memory.

### 10.4 Gates

- After every M2 task: `dotnet build` and `dotnet test` succeed; the full M1+ M2 suite stays green.
- Manual MV-M2 checks pass or produce a documented, human-accepted limitation (e.g., exclusive-fullscreen).
- A separate reviewer agent validates the implementation against this spec; reviewers do not modify code.

## 11. Integration with the M1 architecture

| M2 area | M1 seam it extends | Required approach |
|---|---|---|
| Send-keys | `Interop/Input/SendInputHelper`, `Actions/Handlers` | Reuse `SendInputHelper`; add a `SendKeysHandler`; key-name mapping in `Actions/` (pure). |
| Open program | M1 `RunCommandHandler`/`ProcessLaunchRequestFactory`/`SystemProcessLauncher` (unchanged) | No new launch mechanism. The M2 edit UI's action picker surfaces the existing run-command action with an executable **browse** selection; the safe `UseShellExecute=false` discrete-argument path (RF-049) is untouched. |
| Global hotkeys | `Interop/MessageWindow`/`MessageWindowNative`, `Input/` (reserved empty in M1) | `Input/` gains a hotkey service using `RegisterHotKey` via a new `Interop/` wrapper; a hidden **UI-thread** window receives `WM_HOTKEY`; register/unregister and window (re)creation are always UI-thread-marshalled (M2-RF-582/595); dispatch through `IActionExecutor`. |
| Ring buffers / interval / retry | `Monitoring/MonitoringService` (`PeriodicTimer`, `Latest`, `SnapshotAvailable`), `ISensorSnapshotSource` | Add a monitoring-owned history store; make the interval configurable; add throttled backend retry; keep LHM confined to the adapter. |
| Slider / Sparkline / toggle persistence | `WidgetRegistry`, `WidgetViewModelBase`, `GaugeWidgetViewModel`, `StateStore` | Register two new widget types in `WidgetRegistry.CreateDefault`; Sparkline consumes history via a monitoring boundary; toggle state uses `StateStore` (`state.json`), never `panel.json`. |
| Full edit UI / grid | `EditModeService`, `Views/EditOverlay`, `PageViewModel` | Extend edit mode with add/remove/duplicate/property/action-picker (incl. executable browse for "open program" per M2-RF-434)/label/icon/color editors and snap/guides; persist through the existing `IConfigStore` single-writer path. |
| Hot reload | `IConfigStore`/`JsonConfigStore` (single writer, no polling) + composition root apply pipeline | Add a debounced `FileSystemWatcher` **filtered to `panel.json` only** (M2-RF-821), a pure injectable event→decision component (M2-RF-830), and self-write suppression covering config store **and** state store writes (M2-RF-823); validate-then-apply as a whole; reuse the existing load/validate/migrate code. Never add polling (RF-080). |
| Import/export | `JsonConfigStore`, `ConfigValidator`, `MigrationRunner` | Validate-before-apply and pre-import backup on the existing store; log only paths/summaries (M2-RF-834/835). |
| Page switching / settings / monitor picker | `PanelConfig.ActivePageId`/`Pages`, `MainWindowViewModel.ApplyWindowOptions`, `MonitorSelectionService`, `TrayService` | UI surfaces drive the same config store and apply pipeline; settings/monitor picker reuse `IMonitorEnumerator` and `IStartupRegistration`. |
| Schema | `PanelConfig.CurrentSchemaVersion` (=3), `MigrationRunner`, `ConfigJson` converters | Bump to the **current schema v3** via the ordered, additive chain: `v1→v2` adds the `sendKeys` action discriminator, the telemetry interval, and the ring-buffer/toggle fields; `v2→v3` adds per-widget appearance overrides. The per-widget `action` stays a **single** action; older (v1/v2) files still load. Keep unknown-type preservation. |

**Threats to the M1 invariants and how M2 protects them:**

- **Focus preservation (RF-018):** hotkeys use `RegisterHotKey` and a hidden window, never activation; send-keys sends to the foreground; the settings window is the only new activating surface and is explicitly user-initiated (D-6 analogy). No M2 code path calls `SetForegroundWindow`/`Activate` except the existing edit-mode/tray behavior.
- **Single writer / no polling (RF-080):** hot reload is watcher-based (not polling), **filtered to `panel.json` only** so writes to `state.json`/temp/backup/recovery/corrupt files cannot trigger a reload (M2-RF-821), and suppresses **all** app-originated writes (config store and state store, M2-RF-823); toggle state and other runtime state go to `state.json`, never `panel.json`.
- **UI-thread discipline (RNF-004):** file I/O and history reads are async/marshalled; hotkey dispatch is non-blocking; `RegisterHotKey`/`UnregisterHotKey` and the message window are thread-affine and always marshalled to the UI thread (M2-RF-582/595).
- **Layer purity (RNF-010):** new `Actions/`/`Monitoring/`/`Config/`/`Input/` code stays WPF-free; P/Invoke stays in `Interop/`.
- **Untrusted configuration (RF-081):** preserved and applied to send-keys and hotkey combinations; no new execution surface (HTTP and shell-open dropped, M2-RNF-128).

## 12. Risks

| ID | Risk | Mitigation |
|---|---|---|
| RM-1 | `RegisterHotKey` combinations collide with OS/game shortcuts | RF-059 handling (skip + aggregate, no retry); user reconfigures; error visible. |
| RM-2 | Hot-reload feedback loop (app saves → watcher fires → reload) | Self-write suppression + content/hash comparison (M2-RF-823); temp-dir unit test. |
| RM-3 | Unbounded memory from history or edit UI | Fixed capacity per metric, lazy tracked set, documented memory, soak test (M2-RNF-121/127). |
| RM-4 | `sendKeys` leaving a stuck modifier if synthesis fails midway | Modifiers are always released even on target-key failure (M2-RF-572); explicit stuck-modifier unit test. |
| RM-5 | Schema migration chain (v1→v2→v3) breaks existing configs | Ordered additive migration mirroring the M1 framework; fixtures for both steps; source never destructively overwritten before validation (RF-076); T-01 (v1→v2), T-14 (v2→v3). |
| RM-6 | Edit UI complexity bloating the small app | Touch-first, dialog-light, no undo engine, reuse existing edit mode/store; keep per-widget templates thin. |
| RM-7 | `FileSystemWatcher` buffer overflow under heavy editing | Debounce + overflow handling + re-establish (M2-RF-827); no polling fallback that violates RF-080. |
| RM-8 | Sparkline reading history races the polling thread | Lock-protected ring with consistent reads (M2-RF-692); deterministic tests. |

## 13. Traceability — base 001 → M2 sub-requirements

| 001 base | M2 sub-requirements |
|---|---|
| RF-040 Slider | M2-RF-401..406 |
| RF-041 Sparkline | M2-RF-411..416 |
| RF-043 full edit UI | M2-RF-430..440 |
| RF-044 grid/alignment | M2-RF-441..446 |
| RF-045 toggle persistence | M2-RF-451..455 |
| RF-057 send keys | M2-RF-571..576 |
| RF-058 global hotkeys | M2-RF-581..586 |
| RF-059 hotkey failure | M2-RF-591..595 |
| RF-060 local shortcuts | M2-RF-601..604 |
| RF-069 bounded history | M2-RF-691..697 |
| RF-070 backend re-init | M2-RF-701..704 |
| RF-071 polling interval | M2-RF-711..714 |
| RF-082 hot reload | M2-RF-821..830 |
| RF-083 import/export | M2-RF-831..835 |
| RF-091 page-switching UI | M2-RF-911..915 |
| RF-092 settings window | M2-RF-921..925 |
| RF-093 monitor picker | M2-RF-931..934 |
| RNF-012 bounded history | M2-RNF-121 (plus M2-RNF-122..128 execution constraints) |

**Coverage check:** every `[M2]` requirement from 001 plus `RNF-012` maps to at least one M2 sub-requirement. RF-042, RF-054, RF-055, RF-056, and RF-087 are **dropped (2026-10-09)** and intentionally have no sub-requirements. No `[M3]`/`[Later]` requirement (RF-046, RF-061, RF-072, RF-084) is implemented. The M2 action set is open program (existing M1 run-command) + send key combination.

## 14. Resolved decisions (approved)

The previous open questions were answered by the human; the answers are applied throughout this document. D-M2-4 remains an overridable default. D-M2-2/D-M2-3 (macro cancellation affordances) are **removed** with the macro descope, and no HTTP-specific decision remains. There are no open questions.

| ID | Decision (approved / overridable) | Where applied |
|---|---|---|
| D-M2-1 | **One M2 milestone, two execution phases.** M2 is not split into M2a/M2b milestones. Phase 1 = W1–W5 (actions/input/telemetry/widgets); Phase 2 = W6 (configuration & UI surface). No sub-milestone requirement labels; `[M2]` tags and all IDs unchanged. | §7 (intro, §7.1 diagram/order, §7.2) |
| D-M2-4 | **Global hotkeys require at least one modifier.** A global hotkey must include at least one of Ctrl/Alt/Shift/Win; modifier-less combinations are invalid (skipped + logged), avoiding hijacking bare keys from the foreground application. Overridable. | M2-RF-581/594, AC-M2-13 |
| D-M2-5 | **Product descope (user-descoped, 2026-10-09), not deferred:** macros/sequences (RF-054), macro cancellation (RF-055), repeat-while-held (RF-042), the `delay` action, **HTTP action (RF-056) and HTTP safety (RF-087)**, and shell-open of files/folders/URLs. The action model is one action per widget; **the final M2 action set is open program (existing M1 run-command/launch) + send key combination** (`sendKeys`, RF-057), with global/local hotkeys (RF-058..RF-060). The widget "running" state is removed. 001 RF-042/RF-054/RF-055/RF-056/RF-087 are marked dropped. | §2, §3, §5.2 (RF-057), §5.7, §7, §11, §13, and `specs/001-foundation/spec.md` |
| D-M2-6 | **A slider's live value is transient UI state; the only persisted slider value is its configured `initial value`.** Dragging updates a runtime value used solely for the value→action binding (M2-RF-403) and is never written to configuration. `SliderWidgetConfig.Value` is the **initial value** (clamped/snapped by `ConfigValidator`, `config.widget.slider.value`), and the edit UI edits that initial value (M2-RF-401/M2-RF-433); editing it does not capture the current runtime position. Overridable. | M2-RF-401/403/433, §5.1 |
| D-M2-7 | **Current schema is v3 (accepted post-planning change, 2026-10-09).** The M2 build sets `PanelConfig.CurrentSchemaVersion = 3` and migrates through the ordered, additive, validated chain `v1→v2` (M2 engine/config fields: `sendKeys`, telemetry interval, ring/toggle fields) then `v2→v3` (per-widget appearance overrides: `icon`/`foreground`/`background`/`accent`/`fontSize`/`opacity`). Older (v1/v2) files still load through the chain. Requirement IDs and semantics are unchanged; only the schema version and chain grew. Overridable. | AC-M2-02, M2-RF-825, §5.5/§5.6, §11 (Schema), and `specs/002-automation/plan.md` MD-1 |
| D-M2-8 | **Product scope freeze (2026-10-09): delivered Core Release = Sparkline/graph + bounded history, global hotkeys, send-keys, and visual customization only.** Everything else in this document is **frozen backlog** (RF-040, RF-043 non-appearance, RF-044, RF-045, RF-060, RF-070, RF-071, RF-082, RF-083, RF-091..RF-093, RF-046/061/072/084): its code may remain in the build but is unsupported/unmaintained and will not be extended. Dropped items (RF-042/054/055/056/087, `delay`, shell-open) stay dropped. No M3/Later work. Mirrors 001 `D-24`. Overridable. **Superseded in part by 001 `D-25` (M4 promotion, 2026-10-10)** for RF-040/043(non-appearance)/044/045/060/070/071/082/083/091/092/093 only; RF-072 and the dropped set remain outside support. | Header, §1, §2, §7 (banner + §7.3), §14, and `specs/001-foundation/spec.md` §7.2/§7.3/D-24/D-25 |
