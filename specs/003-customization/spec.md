# TouchPanel — Specification 003: Customization (M3)

| Field | Value |
|---|---|
| Spec ID | 003-customization |
| Status | Draft for human approval (M3 — Specification) |
| Date | 2026-10-10 |
| Phase | M3 — Specification |
| Author | Planner agent |
| Inputs | `specs/001-foundation/spec.md` (M1 + Core-Release freeze), `specs/001-foundation/plan.md`, `specs/002-automation/spec.md` / `tasks.md`, and the delivered M1/M2 implementation in `TouchPanel/` (schema **v3**, 1260 tests green) |
| Scope | The three `[M3]` requirements: **RF-046** (additional widget types), **RF-061** (hotkey-management UI), **RF-084** (themes, accent colors, icon packs) |
| Out of scope | Everything still frozen or dropped. **`RF-072` (optional elevated companion) is explicitly skipped** (requires UAC/elevation, which cannot be done unattended). All frozen-backlog RF-0xx and dropped RF-0xx keep their 001 status and are untouched. |
| Next steps | `plan.md` and `tasks.md` for M3 (not created in this phase) |

### Why "Customization"

The delivered Core Release is a fixed, working panel: a small set of built-in widgets, a theme hard-coded to one dark palette, and global hotkeys that can only be edited by hand-writing JSON. M3's three requirements share one theme — **letting the user shape the panel without touching code**: more built-in widget types (RF-046), a touch-friendly way to view/edit global hotkeys and re-apply them live (RF-061), and app-wide theming with an accent color and selectable icon packs (RF-084). "Customization" names that center of gravity. It changes no 001/002 milestone tag and renumbers nothing.

> **Un-freeze note (2026-10-10).** The human has **un-frozen and approved M3**. The relevant requirements were marked `[FROZEN — backlog 2026-10-09]` in 001 (`D-24`) and 002 (`D-M2-8`); this document is the authoritative M3 scope for RF-046, RF-061, and RF-084. The Core Release and all other frozen/dropped items are **unchanged**. `RF-072` remains frozen and is **not planned**.

> **M4 promotion note (2026-10-10, 001 `D-25`).** M4 ("Promote & Polish") subsequently un-froze and now supports the **other** implemented backlog (RF-040, RF-043 non-appearance, RF-044, RF-045, RF-060, RF-070, RF-071, RF-082, RF-083, RF-091..RF-093) — see `specs/004-promotion/spec.md`. The M3 features (RF-046/061/084) remain supported; **`RF-072` remains frozen/skipped** and the dropped set stays dropped. Nothing in M3 is reinterpreted or renumbered.

---

## 1. Goal

M1 proved the architecture and M2 delivered the Core Release (global hotkeys, send-keys, Sparkline/bounded history, and per-widget visual customization). M3 must make the panel **personally configurable by touch and by configuration**, without disturbing the invariants M1/M2 proved and without introducing a plugin system:

1. **Extensibility proven, not just claimed.** Add three new built-in widget types (Clock, ImageButton, Bar) as plain code additions — a View + ViewModel + one `WidgetRegistry` registration + one template each — so RF-046's promise ("adding a type requires no host changes") is demonstrated end to end.
2. **Hotkeys editable in-app.** A focused, touch-first editor lists the global hotkeys, lets the user add/remove/edit combinations and their bound actions, validates them with the existing rules, persists them through the existing single-writer store, and **re-applies them without restarting** through the existing `App.ApplyHotKeys` / `IPanelConfigApplyTarget.ApplyHotKeys` hook.
3. **App-wide theming.** A built-in theme set (Dark *(default)*, Light, High-contrast), a global accent color, and a selectable icon pack, all driven by configuration, applied live, validated with defined fallbacks, and layered so the existing per-widget appearance overrides (RF-435/436) still win.
4. **Same discipline as M1/M2.** WPF-free core logic, `Interop/` isolation, bounded memory, UI-thread safety, untrusted configuration treated as data, no elevation/UAC, no idle work, and a deterministic test suite that touches no hardware.

**Success for M3** means: a user can add a Clock, an icon Button, and a Bar to the panel; open a Hotkeys editor from the tray, change a combination, and see it take effect while a game is focused without a restart; and switch the panel from Dark to Light, set a custom accent color, and pick a different icon pack — all with per-widget overrides still respected, no crashes, and no focus stealing.

## 2. Scope

**In scope (supported and delivered by M3):**

- **RF-046 — Additional widget types.** `Clock`, `ImageButton`, and `Bar`, each a reusable, config-driven View + ViewModel + `WidgetRegistry` entry + template. No plugin system.
- **RF-061 — Hotkey-management UI.** A focused editor that views/edits the global `hotkeys` list, validates it, persists it, and re-applies it live.
- **RF-084 — Themes, accent colors, icon packs.** A built-in theme set + global accent color + selectable built-in icon packs, configured via a new schema-v4 `appearance` section, applied app-wide, layered under per-widget overrides.
- **Schema v3 → v4** via the existing ordered, additive, validated `MigrationRunner` chain (needed only for the new global `appearance` fields).

**In scope as integration only:** extending existing seams (`WidgetRegistry`, `WidgetViewModelBase`, `WidgetConfigJsonConverter`, `MigrationRunner`/`ConfigMigrationChain`, `IconCatalog`, `Theme/*`, `HotKeyService`/`HotKeyParser`, `ISettingsService`/`SettingsCoordinator`/`SettingsChange`, `PanelConfigApplier`/`IPanelConfigApplyTarget`, `TrayService`/`TrayMenuCommand`, the `App` composition root). No M1/M2 behavior is redefined.

**Out of scope (explicitly not planned):**

- **RF-072 optional elevated companion** — skipped: it needs running elevated/UAC, which cannot be done unattended. Remains frozen `[Later]`.
- Everything else frozen in 001 §7.3 / 002 §7.3 (RF-040, RF-043 non-appearance, RF-044, RF-045, RF-060, RF-070, RF-071, RF-082, RF-083, RF-091..RF-093) stays frozen and untouched. In particular **RF-092 (settings window) is not extended**: M3 introduces a **new focused editor** rather than modifying the frozen `SettingsWindow` code (see §5.2 / D-M3-2).
- Dropped items (RF-042/054/055/056/087, the `delay` action, shell-open of files/folders/URLs) stay dropped.

**Focus-preservation scope.** The RF-018 guarantee is preserved by every M3 feature. Normal-mode panel behavior is unchanged; only explicitly user-opened editor windows may activate (the D-6 analogue), and theming/hotkey re-application never call `SetForegroundWindow`/`Activate`. The exclusive-fullscreen limitation from 001 §2 still applies and is documented, not solved.

## 3. Non-goals

- **No plugin system, no widget store, no external/dynamic loading.** New widget types are compiled-in code additions. Icon packs are **built-in**; external pack files/archives are not loaded.
- **No arbitrary external image files** for `ImageButton` (untrusted config must not point the app at arbitrary filesystem paths); it uses the built-in icon catalog / active icon pack only.
- **No full theme designer and no per-widget theming beyond the delivered RF-435/436 overrides.** A theme is a built-in named palette; the user cannot author new palettes in M3.
- **No arbitrary format-string injection.** Clock formatting is a bounded, validated preset + optional length-capped custom format tried through a safe formatter; configuration stays data (RF-081).
- **No new execution surface.** ImageButton executes through the existing `IActionExecutor`; themes/icons/clock formats are inert data.
- **No elevation/UAC** (RF-086). Themes, hotkey editing, and new widgets all work as a standard user.
- **No architectural additions** (event bus, mediator, DI container, repository layering). `ThemeManager`/`IconPackCatalog` are small concrete helpers/hosts.
- **No restart-required changes.** Theme, accent, icon pack, and hotkey edits apply live.
- **No motor/hardware work in new widgets.** Clock is UI-only; Bar consumes the existing snapshot boundary like Gauge; neither adds a polling path.
- **No page/layout/Focus-session changes** beyond what M1/M2 already deliver.

## 4. Relationship to 001/002 and requirement ID rules

This document **does not renumber, restate, or change the semantics of any 001/002 requirement.** It adds testable sub-requirements in the `M3-RF-###` / `M3-RNF-###` namespace, each carrying a **Maps to** reference to its 001 base ID.

Rules (mirroring 002 §4):

- Base IDs (RF-046, RF-061, RF-084; RNF-xxx) keep their 001 meaning and milestone tag.
- `M3-RF-46x` = RF-046 widgets; `M3-RF-61x` = RF-061 hotkey management; `M3-RF-84x` = RF-084 theming. Numbers are three-digit and unique within the document.
- A base requirement's block is a contiguous ascending range and may cross the nominal two-digit prefix when a base has more than ten sub-requirements (002 §4); e.g. RF-084's block is `M3-RF-841..851`.
- `M3-RNF-13x` are M3 execution constraints that refine M1's RNF-002/003/004/006/008/010/013/014 for the new code.
- A base requirement is *fully* covered when all of its sub-requirements are accepted. No `[M1]`/`[M2]` requirement is retagged or moved.

## 5. Functional requirements

Conventions: **Must** = verifiable. "UI thread" = the WPF dispatcher thread. "Bounded" means a hard maximum exists and is documented/tested. Values marked *(default)* are chosen defaults the human can override at approval. All configuration is untrusted input (RF-081): every string is validated against an allowlist or parsed as inert data, never executed.

### 5.1 Additional widget types (RF-046)

- **M3-RF-461 [Maps RF-046] Extension contract.** A new built-in widget type must be addable by exactly: (a) a `WidgetConfig` subclass with an override of `Type` returning a new `WidgetTypes` constant; (b) a `WidgetViewModelBase` subclass; (c) one `WidgetRegistry.Register` call in `WidgetRegistry.CreateDefault` (with `DisplayName`, `createDefaultConfig`, `DefaultWidth`, `DefaultHeight` so it is addable from the edit-mode palette); (d) one `DataTemplate` in a new per-type `ResourceDictionary` merged into `App.xaml`; (e) one branch in `WidgetConfigJsonConverter` recognizing the discriminator. **No change to the widget host, `PageViewModel`, `WidgetTemplateSelector`, or `EditModeService` is permitted** to support a new type (this is the RF-046 proof).
- **M3-RF-462 [Maps RF-046] Clock widget configuration.** A `clock` widget type with a `mode` field drawn from a fixed enum — `time24` *(default)*, `time12`, `dateShort`, `dateLong`, `dateTime` — and an optional `format` string (length `1–64` characters). When `format` is present it is tried through a safe formatter (see M3-RF-463) and, on failure or over-length, is **ignored** with a logged warning and the `mode` preset is used (never propagated as an error state). **The display culture is pinned** to the user's current culture captured at widget construction (or the invariant culture is used) so the effective format and the seconds/sub-second detection are deterministic; tests pin the culture explicitly. No action binding; `clock` is display-only and is not offered an action picker.
- **M3-RF-463 [Maps RF-046] Clock behavior.** The Clock displays local time/date. It updates **only when the displayed text changes**, driven by a single `TimeProvider`-based timer. The refresh cadence is determined by **escaped-literal-aware** inspection of the effective format string: runs of `\`-escaped literals (and quoted literals) are ignored when detecting `s`/`f`/`F`/`t` specifiers, so only real seconds (`s`) or sub-second (`f`/`F`) designators force a **1-second** refresh. The `t`/`tt` (AM/PM) designators **do not** force per-second refresh — a 12-hour format without a seconds field still refreshes only on the minute boundary. Only formats containing a real sub-second specifier *may* tick below 1 s, and are capped at 1 s in M3 (finer than 1 s is out of scope). The **next boundary is recomputed after every tick** and on **OS time/state events** (event-driven, no polling): system clock/time-zone change and resume-from-sleep/session changes, subscribed via the platform equivalents of `Microsoft.Win32.SystemEvents.TimeChanged`, `SystemEvents.PowerModeChanged` (Resume/Suspend), and `SystemEvents.SessionSwitch`, owned/released with the widget (or a single app-level source feeding the Clock). This keeps the Clock from drifting or stalling across sleep/time-zone changes. It runs **no idle busy loop and no animation**, performs no hardware polling, and is fully operable with no keyboard focus. A timer or format failure degrades to a static (last-valid) label and logs; it never crashes the page (RF-012).
- **M3-RF-464 [Maps RF-046] ImageButton configuration.** An `imageButton` widget type that renders the widget's configured built-in `icon` (base `WidgetConfig.Icon`, resolved through the **active icon pack**, RF-084) prominently, optionally with `label` text (a `showLabel` boolean, default `true`), and binds to **exactly one** action (the existing one-action-per-widget model, RF-047). If no `icon` is configured the widget renders its `label` (or a neutral built-in default) rather than failing. Arbitrary external image paths are out of scope and must not be read (RF-081).
- **M3-RF-465 [Maps RF-046] ImageButton behavior.** A completed tap (release inside the control) executes the bound action **exactly once**, with immediate press feedback (< ~100 ms, RF-030), the ≥ 40×40 DIP touch-target default (RF-031/D-9), no hover/right-click dependency (RF-025), and the shared transient failure state + structured log on failure (RF-036/RF-050). A missing/invalid action leaves the widget visible but disabled with an error indicator (RF-047), including inside edit mode (where actions never fire, M2-RF-439).
- **M3-RF-466 [Maps RF-046] Bar configuration.** A `bar` widget type — a single-value horizontal meter — with `sensor` (a stable application-level sensor key, as used by Gauge), numeric `minimum` *(default `0`)* and `maximum` *(default `100`)* with `maximum > minimum`, an optional `label`, and a `showValue` boolean *(default `true`)*. Vertical orientation and multiple segments are out of scope. It has no action binding.
- **M3-RF-467 [Maps RF-046] Bar behavior.** The Bar consumes the **existing public** monitoring snapshot boundary (`ISensorSnapshotSource`/`SensorSnapshot`) exactly like Gauge and **never** polls hardware, calls `PublishOnce`, or reads history. It maps the sensor value onto `[minimum, maximum]`, clamps out-of-range values, and renders the filled fraction plus the numeric value when `showValue`. Unavailable/unknown sensors render as an explicit "Unavailable" state (never as zero, per RF-063/RF-065); an unknown sensor key disables the widget with a reason (RF-047). Updates occur **at most once per snapshot** (≤ ~1 Hz) and only when the value or availability changes — no idle animation, no UI-thread blocking (RF-038/M3-RNF-131).
- **M3-RF-468 [Maps RF-046] Registry, palette, template, converter integration.** All three types are registered in `WidgetRegistry.CreateDefault`, appear in the edit-mode palette (built-in types only), can be added with a deterministic default geometry (Clock `240×120`, ImageButton `120×120`, Bar `240×64`) via the existing `IWidgetPaletteSource`, resolve their templates through `WidgetTemplateKeys`, and round-trip through `WidgetConfigJsonConverter`. `EditModeService` and the property editor operate on them through the existing base `WidgetConfig` behavior (geometry, label, appearance overrides, enablement) without type switches. **Type-specific fields are JSON-only in M3:** the new types' type-specific properties — Clock `mode`/`format`, ImageButton `showLabel`, Bar `sensor`/`minimum`/`maximum`/`showValue` — are edited by hand-editing `panel.json`; the **frozen** property editor (RF-043 non-appearance / M2-RF-433) is **not extended** in M3. The palette adds a new widget with the type's registered default config, which is then refined in JSON. Geometry/label/appearance/enablement of the new types remain editable through the existing base behavior only. *(Superseded by `M4-RF-432`/`D-M4-2`: once M4 promotes the property editor, the new types' type-specific fields become editable in the UI.)*
- **M3-RF-469 [Maps RF-046, RF-079] New-type validation and forward compatibility.** New-type-specific fields are normalized by the M1 `ConfigValidator` two-tier rules: invalid `clock.mode`/`bar` ranges fall back to defaults with a logged warning; `imageButton` with no valid action is disabled (not dropped). A new widget type is an additive `type` value, not a new schema structure, so **two distinct forward-compatibility cases** apply: **(a) a same-schema-version build that merely lacks the type** treats the entry as unknown — preserved with its raw JSON and rendered as a disabled placeholder (RF-079), never dropped; **(b) a build whose `CurrentSchemaVersion` is lower than the file's** does **not** interpret the file at all and follows the whole-file future-schema path (RF-075.4) — the file is left untouched and the app runs from backup/defaults with a notification. Neither case requires a schema step per widget type.

### 5.2 Hotkey-management UI (RF-061)

**Surface choice (D-M3-2).** M3 introduces a **new, focused, touch-first `HotkeysEditorWindow`** reachable from a new tray menu command (`Hotkeys…`, added to `TrayMenuCommand`/`TrayService`). It does **not** extend the frozen `SettingsWindow` (RF-092 stays frozen; extending its code would violate the M2 freeze). The editor's underlying view model is WPF-free and reusable (it could later be embedded in a settings surface), but M3 does not un-freeze RF-092.

- **M3-RF-611 [Maps RF-061] Editor surface and entry.** A tray-accessible editor window lists the effective global hotkeys (combination + bound action + a human-readable summary). Opening it is a deliberate user action; it is an ordinary activating window (the D-6 analogue, exactly like the M2 `SettingsWindow`, M2-RF-924) and opening/closing it must not change the panel's normal no-activate behavior (RF-018). If the hotkey service failed to initialize (RF-012/M2-RF-582), the editor still opens and shows the configured list with a clear "hotkeys unavailable in this session" note; it never crashes.
- **M3-RF-612 [Maps RF-061] Editor operations.** The editor supports viewing, **adding** (with a generated unique `id`), **editing** (modifier set + key + bound action), and **removing** hotkey entries. The bound action is chosen/edited through the existing action catalog (`ActionPickerViewModel` semantics: the M1 actions + `sendKeys`), reusing the same typed-parameter editing. All operations are operable by touch; the editor does not require a physical keyboard (a modifier toggle set + a key selector drawn from the fixed friendly-key catalog, so no global keystroke capture is required).
- **M3-RF-613 [Maps RF-061, M2-RF-581/594] Validation reusing `HotKeyParser`.** Every entry is validated with the same rules as runtime registration and the same shared catalog: **at least one modifier** (Ctrl/Alt/Shift/Win, D-M2-4), a **known key name**, a **known modifier set**, and **exactly one bound action**. Invalid entries are blocked inline with the reason and are never written to the configuration. **Duplicate combinations** (same normalized modifier set + key, case-insensitive, configuration order) are detected and flagged inline before save (the first-wins semantics of M2-RF-593). The editor does not introduce a second, divergent validation implementation — it calls `HotKeyParser`/`SendKeyCombination` so the editor and the registrar cannot drift.
- **M3-RF-614 [Maps RF-061] Apply without restart.** Saving the edited list persists it and **re-applies it live** through the existing re-registration hook (`App.ApplyHotKeys` / `IPanelConfigApplyTarget.ApplyHotKeys`, M2-RF-595), which unregisters the previous set and registers the new one on the **UI thread** (M2-RF-582). No application restart is required. Per-entry registration outcomes reuse M2 semantics: a refused combination is skipped, the rest continue, and exactly **one aggregated tray notification** reports the failures (no retry loop, RF-059/M2-RF-591/592).
- **M3-RF-615 [Maps RF-061, RF-080] Persistence and single writer.** Hotkey edits persist **only** through the existing single-writer path (`ISettingsService`/`SettingsCoordinator` with a new **optional `SettingsChange.Hotkeys` property** — `SettingsChange` is an immutable **record with optional properties**, each `null` meaning "leave unchanged" — plus `IConfigStore.SaveAsync`). `SettingsCoordinator.BuildUpdated` **must preserve every section it is not explicitly changing**: a hotkey edit must not reset `appearance` and an appearance edit must not reset `hotkeys` (nor window/startup/telemetry/monitor/pages). The in-memory running configuration is never mutated before the save succeeds; a failed save keeps the dirty edits in the editor, notifies, and does not partially apply (M2-RF-922). No file watcher is added and no polling occurs (RF-080).
- **M3-RF-616 [Maps RF-061, RF-018, D-6] Activation and no focus-steal regression.** The editor is the only M3 window that may activate, and only because the user opened it. Editing hotkeys, saving, and re-registering must never call `SetForegroundWindow`/`Activate`, must never change the panel's `WS_EX_NOACTIVATE` normal behavior, and must never take focus from a running game by themselves. Saving a hotkey change while another application is focused must not move the foreground window.
- **M3-RF-617 [Maps RF-061, RF-012] Failure isolation.** A failure building/showing the editor, reading the hotkey list, or applying the change is logged and isolated: the panel, tray, and hotkeys keep working (RNF-013). A failure **before** the re-apply runs — e.g. a `SettingsCoordinator` save failure — leaves the running configuration and the **currently registered** hotkeys intact (the apply hook is never reached). Once a successful save has triggered the re-apply, the previous set is first unregistered (M2-RF-595); a subsequent total registration failure may therefore leave no registrations, which is surfaced per M3-RF-618 (log + one aggregated notification) and never crashes (see F-M3-15).
- **M3-RF-618 [Maps RF-061, RF-059] Registration failure surfacing.** After a live re-apply, refused combinations are surfaced exactly as at startup: logged with the full combination and one aggregated notification (M2-RF-591/592). The editor reflects which entries are currently registered versus refused where practical, but this reflection is presentation-only and must not change registration semantics.
- **M3-RF-619 [Maps RF-061, RNF-004] Touch/keyboard/UI-thread discipline.** The editor is touch-first (≥ 40 DIP controls, no hover-only affordances), validation is synchronous and cheap, and persistence is async off the UI thread. Registration/unregistration and hidden-window handling remain marshalled to the UI thread (M2-RF-582); the editor never blocks the message pump.

### 5.3 Themes, accent colors, icon packs (RF-084)

- **M3-RF-841 [Maps RF-084, RF-074/D-19] Appearance configuration shape.** A top-level `appearance` section (schema v4):
  ```json
  {
    "schemaVersion": 4,
    "appearance": {
      "theme": "dark",
      "accentColor": null,
      "iconPack": "default"
    }
  }
  ```
  `theme` is a built-in theme id; `accentColor` is `#RRGGBB`/`#AARRGGBB` or `null` ("use the theme's accent"); `iconPack` is a built-in icon-pack id. All three are optional and default to `dark` / `null` / `default`. The fields are plain data; the model never interprets them.
- **M3-RF-842 [Maps RF-084] Built-in theme set.** M3 ships exactly three built-in themes: **`dark` (default)**, **`light`**, and **`highContrast`**. Each is a `ResourceDictionary` defining the **same key set** currently in `Theme/Colors.xaml` (`PanelBackground*`, `PanelForeground*`, `PanelDisabledForeground*`, `PanelAccent*`, `WidgetSurface*`, `WidgetPressed*`, `WidgetDisabled*`, `WidgetBorder*`, `WidgetError*`). `Theme/Styles.xaml` and the widget templates consume those keys; a new theme may only change values, never introduce required new keys that templates must know about. The `dark` theme reproduces the current palette exactly (so the default is a no-op).
- **M3-RF-843 [Maps RF-084, M2-RF-436] Accent color.** `appearance.accentColor` sets a **global** accent used wherever the theme accent is used (pressed borders, toggle on-layer, slider fill/handle, badges as applicable). It layers **under** the per-widget `accent` override (RF-436): a widget `accent` wins when set; otherwise the global accent; otherwise the theme accent. The color is validated with the existing `WidgetAppearance` color rules (`#RRGGBB`/`#AARRGGBB`); invalid/blank → `null` (theme accent) with a logged warning.
- **M3-RF-844 [Maps RF-084, M2-RF-435] Icon packs.** A **built-in** `IconPack` set with **at least two** packs: `default` (the current `IconCatalog.BuiltIn` catalog of **42** entries, unchanged) and at least one alternate implementer-chosen pack (e.g. a "minimal"/"filled" variant) whose glyphs also resolve in the bundled icon font (`Segoe MDL2 Assets`). Packs share a **single stable icon-id namespace**: a pack maps a subset of ids to alternate glyphs, and an id **absent** from the active pack falls back to the `default` pack glyph — ids are never cleared by the active pack. Per-widget `icon` ids therefore remain pack-independent and no per-widget migration is needed. External/loaded packs are out of scope.
- **M3-RF-845 [Maps RF-084, M2-RF-435/436] Application scope and precedence.** Applying appearance affects **all** panels/widgets app-wide (not the config file alone): the active theme dictionary is swapped at the application level (M3-RF-851) and the accent/icon-pack resolution updated for the whole application. Precedence is explicit and testable: **global theme → global accent → per-widget overrides (RF-435/436)**, where per-widget values win per property and `null` means "inherit". Applying appearance must not change widget geometry, page membership, actions, or monitoring, and must not activate the panel.
- **M3-RF-846 [Maps RF-084, RF-076/D-19] Schema v3 → v4 migration.** A new ordered, additive `V3ToV4Migration : IConfigMigration` (From `3`, To `4`) is added to `ConfigMigrationChain.Create()`, and `PanelConfig.CurrentSchemaVersion` becomes `4`. The step only documents the additive `appearance` section and raises the version: a v3 file deserializes directly into a valid v4 model (`appearance` defaults are the documented values), and **no v3 field is removed or renamed**. Older v1/v2/v3 files still load through the ordered chain, are validated after each step, backed up, and saved atomically (RF-075.2/RF-076/RF-078). A failing step follows the invalid/corrupt path and never destructively overwrites the source. **New widget-type discriminators (RF-046) require no schema step** (M3-RF-469).
- **M3-RF-847 [Maps RF-084, RF-079] Validation and fallback.** Appearance values are entry-level, contained per RF-079: an unknown `theme` → `dark` with a logged warning; an unknown `iconPack` → `default` with a warning; an invalid/blank `accentColor` → `null` with a warning; a missing `appearance` section → all defaults. None of these is a file-level error, so an otherwise-valid file must still load and run. Values are case-insensitive where ids are concerned and normalized on validation.
- **M3-RF-848 [Maps RF-084, M2-RF-824] Live apply pipeline integration.** Appearance changes apply live through the shared apply pipeline: `PanelConfigApplier` gains an isolated `appearance` step and `IPanelConfigApplyTarget` gains `ApplyAppearance(AppearanceConfig)`, implemented by the composition root. Enabling this reuses the existing `SettingsCoordinator` path (via a new **optional `SettingsChange.Appearance` property**) so a saved appearance change is published and applied without restart. **Swap-with-rollback:** the apply step captures the currently active theme/pack/accent and, if loading or swapping the new theme resource dictionary or refreshing the live widgets fails, **restores the previous theme wholesale** so the UI is never left half-applied (no mix of old and new palette keys); the failure is logged and isolated, and the panel keeps working (RF-012).
- **M3-RF-849 [Maps RF-084, RF-086] High contrast and accessibility.** The `highContrast` theme uses high-contrast-appropriate values (mapping to Windows system high-contrast colors where practical) and must remain readable with ≥ 40 DIP touch targets and without hover-only affordances. No theme requires elevation. (Honoring the live OS high-contrast switch automatically is *not* required in M3; the built-in `highContrast` theme satisfies RF-084.)
- **M3-RF-850 [Maps RF-084] Selection UI.** A touch-friendly selection surface in the new M3 editor window (an "Appearance" section) lets the user pick the theme, set/clear the accent color, and pick the icon pack, with the same validation/fallback and live-apply semantics as configuration. The confirmation/rejection is immediate; an invalid entry is blocked inline or falls back with visible feedback. This section is presentation over the same `ISettingsService` path and adds no new persistence mechanism.
- **M3-RF-851 [Maps RF-084] Theme-resource conversion required for live swap (`DynamicResource` + self-merge removal).** Live theme swapping is **not achievable** with the delivered architecture (theme brushes are almost entirely `StaticResource`, and every widget template dictionary self-merges `Theme/Styles.xaml`, which in turn merges the dark `Theme/Colors.xaml`, so an app-level theme dictionary cannot win for already-realized elements). M3 **must** therefore convert the theming layer before live apply can be claimed:
  - All theme-referencing bindings in `Theme/Styles.xaml` **and** every per-type widget template dictionary (`Widgets/*/*Templates.xaml`) that reference theme keys (including control-template `Background`/`BorderBrush`/`Foreground`/`Fill` and the disabled/pressed triggers) must use **`DynamicResource`** so a runtime swap of the active theme dictionary updates **realized** elements without a rebuild.
  - The **per-dictionary `Theme/Colors.xaml` self-merge in `Theme/Styles.xaml` (and any other dictionary that self-merges the palette) must be removed/neutralized**, and `Theme/Styles.xaml` must not hard-merge a concrete theme. The active theme dictionary is merged at the **application level** (`App.xaml` / `ThemeManager`) so it is the single source for the palette keys, and a runtime swap replaces it there.
  - **Per-widget appearance overrides (RF-435/436) must keep working after the conversion without `Binding.TargetNullValue` holding a `DynamicResource`** — WPF does not allow `{DynamicResource}` as a `Binding.TargetNullValue`, so the delivered `Background="{Binding BackgroundBrush, TargetNullValue={StaticResource WidgetSurfaceBrush}}"` pattern cannot be mechanically preserved. The required mechanism is concrete and dynamic-safe:
    - **(a) Element base + layered override (preferred):** the template element carries the theme brush directly via `DynamicResource` (e.g. `Background="{DynamicResource WidgetSurfaceBrush}"`), and the per-widget override is layered on top through a `Style`/`DataTrigger`/overlay driven by a presence converter (e.g. an overlay `Border` whose `Background` binds to the VM override brush and whose `Visibility`/`Opacity` is driven by a `HasBackgroundOverride`-style converter). Same for foreground/accent.
    - **(b) VM-resolved brush (alternative):** the view model (or a small code-behind helper) resolves the effective brush and applies it with `SetResourceReference`/resource lookup, so a theme swap re-resolves the fallback while a present override still wins.
    - Either way the **precedence is preserved**: override present → override wins; override absent → the `DynamicResource` theme/accent value applies and follows live theme swaps. The chosen mechanism must be covered by AC-M3-12 with an **overridden** widget.
  - An alternative controlled re-apply/rebuild of realized elements is permissible **only** if a real-BAML test (AC-M3-12) proves a realized widget updates on a live theme change; the `DynamicResource` conversion is the preferred approach.
  - Ordering note: this conversion is a prerequisite of the appearance apply step (M3-RF-848) and of the live-swap acceptance criteria; it may be done as part of the W1 foundation before any theme is applied.

## 6. Non-functional requirements

- **M3-RNF-131 [Maps RNF-003, RNF-008] No idle work.** Appearance applies once per change with no background loops. The Clock updates only on a text-changing boundary (≤ 1/minute normally, ≤ 1/second only with seconds); the Bar updates at most once per snapshot. Idle CPU with the panel visible and hidden must stay within RNF-001.
- **M3-RNF-132 [Maps RNF-004] UI-thread discipline.** Theme-dictionary swapping, live widget icon/appearance refresh, the Clock timer callback, and hotkey re-registration are marshalled to the UI thread; file I/O and saves run off the UI thread; no synchronous blocking work is added to the UI thread.
- **M3-RNF-133 [Maps RNF-006] Testability.** Theme/accent/icon-pack resolution and fallback, the v3→v4 migration, the Clock format/behavior, the Bar value mapping, the ImageButton action semantics, and the hotkey-editor view model are unit-testable **without** WPF, hardware, drivers, elevation, or network. `dotnet test` remains the acceptance gate; tests use temp directories and injected paths.
- **M3-RNF-134 [Maps RNF-010] Layering.** New `Config/` (appearance DTOs, validation, migration), `Input/` (hotkey editor logic) and any `Theme` helper logic stay **WPF-free** except the XAML resource dictionaries and the thin view layer. P/Invoke stays in `Interop/`; the layer-purity architecture test is extended to the new namespaces. `Theme/` remains presentation resources plus thin hosts.
- **M3-RNF-135 [Maps RNF-014, RF-081] Untrusted-configuration security.** Theme/pack ids are matched against fixed allowlists; accent colors are parsed (not executed); the Clock custom format is length-capped and tried through a safe formatter; only built-in icon packs and the built-in icon catalog are used; no external image/archive/plugin is loaded; nothing in M3 adds a new execution surface (ImageButton uses the existing action executor).
- **M3-RNF-136 [Maps RNF-002] Memory/perf.** Theme dictionaries and icon packs are bounded fixed resources; applying appearance allocates only transiently; the new widgets add no unbounded collections. The 8-hour soak targets from RNF-002 hold.
- **M3-RNF-137 [Maps RF-018] No focus-steal regression.** Only the user-opened M3 editor may activate (D-6 analogue); theme/accent/pack/hotkey application never activates the panel or changes the foreground.
- **M3-RNF-138 [Maps RNF-013] Isolation.** Appearance, icon-pack, Clock, Bar, and hotkey-editor failures degrade locally and are logged; core panel/tray/config keeps working.
- **M3-RNF-139 [Maps RNF-012/RF-069] Bounded history preserved.** Bar consumes snapshots only and does not alter the bounded-history store's capacity, tracked set, or threading model.
- **M3-RNF-140 [Maps RNF-015] Display correctness.** The new widgets and themes render crisply and hit-test correctly at 100%–200% scaling and on a 1024×600 panel (no bitmap stretching); theme swaps trigger no layout corruption.

## 7. Schema and migration (v3 → v4)

| Step | From → To | Additive change | Guard |
|---|---|---|---|
| `V3ToV4Migration` | 3 → 4 | Introduce the global `appearance` section (`theme`, `accentColor`, `iconPack`) with documented defaults; raise `schemaVersion`. | Ordered, additive, idempotent; no v3 field removed/renamed; no per-widget rewrite. |

- `PanelConfig.CurrentSchemaVersion` becomes **4**; `ConfigMigrationChain.Create()` returns `[V1ToV2, V2ToV3, V3ToV4]`.
- `MigrationRunner` semantics are unchanged: each step runs in order, the result is validated (`ConfigValidator.Validate`) before the store persists it, and a failure follows the invalid/corrupt path (RF-075.3/RF-076) leaving the source intact (RF-078).
- **New widget types need no schema step** (M3-RF-469): they are additive `type` discriminator values within the existing `widgets[]` structure. A **same-version** build that lacks the type renders it as an unknown-type placeholder with its raw JSON preserved; a **lower-schema-version** build reading a v4 file takes the whole-file future-schema path (RF-075.4) and leaves the file untouched.
- All four RF-075 load states remain covered, and v1/v2/v3 files keep loading.

## 8. Milestone scoping

M3 is a single milestone with three independent workstreams that share only the schema bump:

- **W1 — Schema/appearance foundation (RF-084).** `AppearanceConfig`, validation/fallback, `V3ToV4Migration`, `CurrentSchemaVersion = 4`, **the `DynamicResource` theme-resource conversion and self-merge removal (M3-RF-851, a prerequisite of live swap)**, theme ResourceDictionaries, `ThemeManager`, `IconPackCatalog`, apply-pipeline wiring. Delivered first so every later task lands on a green baseline with a valid v4 config.
- **W2 — Widgets (RF-046).** Clock, ImageButton, Bar (config + VM + View + registry + templates + palette). Depends on W1 only for `IconPackCatalog` (ImageButton) and the v4 baseline.
- **W3 — Hotkey-management UI (RF-061).** `HotkeysEditorWindow` + WPF-free view model, `SettingsChange.Hotkeys`, tray entry, live re-apply. Depends on W1's `SettingsChange`/apply-path extension but is otherwise independent of W2.
- **W4 — Appearance selection UI (RF-084 UI, M3-RF-850), integrated into the same M3 editor window**, plus final verification.

`RF-072` is **skipped** and appears in no task. No frozen-backlog or dropped feature is partially implemented "for later".

## 9. Acceptance criteria

Each criterion is automated (A), manual (M), or both (A+M).

- **AC-M3-01 (A) Build & tests.** The solution builds with the .NET 10 SDK; `dotnet test` passes with the new M3 tests; the full M1+M2 suite (1260 tests) stays green. (M3-RNF-133)
- **AC-M3-02 (A) Schema migration to v4.** A v3 configuration migrates to **v4** through the ordered, additive chain and validates/saves; v1/v2 files still migrate through the whole chain; a failing migration follows the invalid path and leaves the source intact. New widget-type discriminators are shown to need **no** schema step. (M3-RF-846/469, RF-075/076)
- **AC-M3-03 (A) New widget view models and validation.** Clock (`mode` presets + bounded custom-format fallback), ImageButton (`icon`/`showLabel`/single-action semantics + disabled-when-invalid), and Bar (`minimum`/`maximum` normalization, value→fraction mapping, clamp, "Unavailable", unknown-sensor disable, no-poll, update-on-change-only) are unit-tested; the registry exposes all three with deterministic defaults; the JSON converter round-trips them. (M3-RF-462..469)
- **AC-M3-04 (M) New widgets live.** On the real touchscreen a Clock shows correct local time/date and ticks at the expected cadence (no seconds field → minute updates); an ImageButton executes its action exactly once per tap with feedback; a Bar tracks its sensor and shows "Unavailable" when the sensor is absent. Widgets are added from the palette (registered defaults) and their type-specific fields are configured **in JSON** (M3-RF-468/AC-M3-13). No keyboard focus is required. (M3-RF-463/465/467, RF-030/025)
- **AC-M3-05 (A) Hotkey editor logic.** Listing, add (generated unique id), edit, and remove; validation via `HotKeyParser` (≥ 1 modifier, known key/modifier, single action), inline blocking of invalid entries, duplicate detection (first-wins order), and save-through the single-writer store with `SettingsChange.Hotkeys` are unit-tested at the view-model level; the re-apply hook is invoked with the validated list. (M3-RF-611..615/619)
- **AC-M3-06 (M) Hotkey editor live.** Opening the editor from the tray and changing a combination re-registers it **without restart**; the new hotkey fires while another application (Notepad / a windowed or borderless game) is focused; a deliberately conflicting combination is skipped and reported in one aggregated notification; editing/saving never changes the foreground window. (M3-RF-614/616/618, RF-058/059/RF-018)
- **AC-M3-07 (A) Appearance resolution and precedence.** Theme/accent/icon-pack resolution, per-property precedence (global theme < global accent < per-widget override), and fallbacks (unknown theme → dark, unknown pack → default, invalid accent → null, missing section → defaults) are unit-tested; icon-id fallback to the default pack is tested; the layer-purity test covers the new logic. (M3-RF-841..847/850)
- **AC-M3-08 (M) Appearance live.** Switching Dark ↔ Light ↔ High-contrast, setting/clearing a custom accent color, and switching the icon pack all apply app-wide **without restart**; a widget with its own color/icon override still wins; the saved values survive a restart; applying appearance never activates the panel. (M3-RF-843/844/845/848/850)
- **AC-M3-09 (A) Layering & security.** New `Config`/`Input` logic is WPF-free; P/Invoke stays in `Interop/`; hostile theme/pack ids, accent strings, clock formats, and widget geometry are contained (fallback/disable), never executed; no external image, pack, or plugin is loaded; no new execution surface; no elevation. (M3-RNF-134/135, RF-081/RF-086)
- **AC-M3-10 (M) Focus & performance.** The RF-018 focus matrix still passes with M3 features present: showing/hiding the panel, tapping new widgets, applying themes, and re-registering hotkeys never interrupt foreground typing; only the user-opened editor activates. Idle CPU stays within RNF-001 and an 8-hour soak holds RNF-002. (M3-RNF-131/136/137, RF-018, RNF-001/002)
- **AC-M3-11 (M) Core-Release regression.** The delivered Core Release (Sparkline/bounded history, global hotkeys, send-keys, per-widget appearance) and all frozen-backlog code in the build remain functional and untouched; the frozen features' behavior is not silently changed by M3. (001 D-24, 002 D-M2-8)
- **AC-M3-12 (A) Live theme swap on a realized widget (real BAML).** A **real-BAML** test (loading the actual `Theme/Styles.xaml` + a widget template dictionary, not a mock) creates **two realized** widgets (measured/rendered, e.g. via an STA test host): one with **no** per-widget override and one **overridden** widget whose per-widget appearance override is set. It swaps the active theme dictionary at runtime exactly as `ThemeManager` does and asserts: (i) the override-less realized element's resolved theme brush **changed** to the new theme (proving the `DynamicResource` conversion, M3-RF-851, works for already-realized elements, not only the panel background); (ii) the **overridden** widget still shows its override (precedence preserved) while its non-overridden properties follow the theme; and (iii) the swap is **atomic/rollback-safe** — an injected swap failure leaves the previous theme fully in effect with no mixed palette keys (M3-RF-848). (M3-RF-842/845/848/851)
- **AC-M3-13 (A) New type-specific fields are JSON-only.** Tests/checks confirm the frozen property editor is unchanged and the new types' type-specific fields (Clock `mode`/`format`, ImageButton `showLabel`, Bar `sensor`/`minimum`/`maximum`/`showValue`) round-trip through JSON; palette add uses registered defaults; geometry/label/appearance/enablement remain editable through existing base behavior only. (M3-RF-468)

## 10. Failure cases and required behavior

| ID | Scenario | Required behavior | Refs |
|---|---|---|---|
| F-M3-01 | `appearance.theme` unknown | Load with `dark` + logged warning; app runs normally | M3-RF-847 |
| F-M3-02 | `appearance.iconPack` unknown | Load with `default` + logged warning; app runs normally | M3-RF-847 |
| F-M3-03 | `appearance.accentColor` invalid/blank | Treat as `null` (theme accent) + warning | M3-RF-843/847 |
| F-M3-04 | Missing/absent `appearance` section | Apply all defaults (`dark`/`null`/`default`) | M3-RF-841/847 |
| F-M3-05 | Component/icon id absent from the active pack | Fall back to the `default` pack glyph; never clear the id | M3-RF-844 |
| F-M3-06 | `clock.format` invalid, over-length, or throwing | Ignore it, use the `mode` preset + warning; no error badge | M3-RF-462 |
| F-M3-07 | Clock timer/format failure | Freeze at last-valid text, log, isolate; page stable | M3-RF-463/RF-012 |
| F-M3-08 | Bar sensor missing/unavailable/NaN | Show "Unavailable" (never zero), throttled log; app stable | M3-RF-467/RF-065 |
| F-M3-09 | Bar invalid range (`maximum ≤ minimum`) | Normalize to defaults with a warning; widget stays usable | M3-RF-466/469 |
| F-M3-10 | ImageButton with no/invalid action | Widget visible but disabled with an error indicator (not dropped) | M3-RF-464/465/RF-047 |
| F-M3-11a | A **lower-schema-version** build reads a v4 file | Whole-file future-schema path (RF-075.4): do not interpret or overwrite; notify; run from backup/defaults | M3-RF-469/RF-075.4 |
| F-M3-11b | A **same-schema-version** build lacks a new widget type present in the file | Unknown-type placeholder, raw JSON preserved (RF-079); no schema step needed | M3-RF-469/RF-079 |
| F-M3-12 | v3→v4 migration step fails | Invalid/corrupt path; source file left intact; recover from backup/defaults | M3-RF-846/RF-075/076 |
| F-M3-13 | Hotkey editor save fails | Keep dirty edits in the editor; notify; running config and current registrations unchanged | M3-RF-615/617 |
| F-M3-14 | Live hotkey re-apply: combination already taken | Log full combination, skip, continue others, one aggregated notification, no retry | M3-RF-614/618/RF-059 |
| F-M3-15 | Live hotkey re-apply fails **after** a successful save (or the hotkey service is unavailable) | The re-apply unregisters the previous set first (M2-RF-595), so the previous registrations **may be lost**; log + exactly one aggregated notification, no retry; panel/tray keep working. (A failure *before* the re-apply — e.g. a save failure — instead leaves the current registrations intact.) | M3-RF-614/617/618/RF-059 |
| F-M3-16 | Theme resource swap fails or a key is missing | **Restore the previous theme wholesale** (no half-applied theme), log, isolate; panel remains usable | M3-RF-848/RF-012 |
| F-M3-17 | Appearance apply step fails during pipeline | Isolated step failure; other steps continue; current look retained | M3-RF-848/M2-RF-824 |

## 11. Test strategy

Aligned with M1 §11 / M2 §10: unit tests are the gate, hardware/network/elevation are excluded, temp directories isolate the profile, and WPF-free layer purity is enforced. No UI-automation framework is introduced.

### 11.1 Unit tests (no hardware, no drivers, no elevation, no network)

- **Config/migration:** `v3→v4` (and `v1→v4`, `v2→v4`) fixtures; failing-step handling leaves the source intact; four RF-075 states; `AppearanceConfig` round-trip; unknown theme/pack/color fallbacks; missing-section defaults; new widget-type DTO round-trips; unknown-type preservation.
- **Theme/appearance:** theme resolution + fallbacks; accent precedence (theme < global < per-widget); icon-pack id fallback to `default`; `IconPackCatalog` resolution; `ThemeManager` selection as a pure/idempotent operation where feasible; **swap rollback** (an injected swap failure restores the previous theme with no mixed keys).
- **Real-BAML live theme:** an STA test that loads the real `Theme/Styles.xaml` and a real widget template dictionary, realizes a widget, swaps the active theme dictionary at runtime, and asserts the realized element's resolved theme brush changed (AC-M3-12). This is a WPF-presentation test, not a fake-resource test.
- **Widget view models:** Clock mode/format validation + cadence decision (fake `TimeProvider`) + update-on-change; Bar mapping/clamp/unavailable/unknown-sensor/no-poll/update-on-change; ImageButton single-action semantics + disabled-when-invalid.
- **Registry/palette:** all three types registered with deterministic defaults; template keys resolve; the palette lists built-in types only.
- **Hotkey editor:** list/add/edit/remove; validation via `HotKeyParser`; duplicate/invalid blocking; id generation; save-through a fake store with `SettingsChange.Hotkeys`; re-apply invoked with the validated list.
- **Architecture:** layer-purity test extended to the new `Config`/`Input` logic (no WPF types); P/Invoke confined to `Interop/`.

### 11.2 Integration tests (still no real hardware)

- `JsonConfigStore` against a temp directory: v3→v4 migrate/validate/backup; write of the new `appearance` section; no read-modify write surprises.
- `SettingsCoordinator` with a fake store/applier: a hotkey edit and an appearance edit persist and publish through the shared apply pipeline; a failed save changes nothing; **`BuildUpdated` preserves every unchanged section** (a hotkey edit leaves `appearance` intact and an appearance edit leaves `hotkeys` intact).

### 11.3 Manual verification matrix (executed in the M3 acceptance run)

- **MV-M3-01 Widgets:** place Clock/ImageButton/Bar by touch; verify clock cadence, action execution, bar tracking + "Unavailable"; no keyboard needed.
- **MV-M3-02 Hotkeys:** open the editor from the tray; add/edit/remove; conflicts and invalid entries; live re-apply without restart; the edited hotkey fires while a game/Notepad is focused; foreground unchanged.
- **MV-M3-03 Appearance:** switch themes/accent/icon pack live; per-widget overrides still win; values persist across restart.
- **MV-M3-04 Focus matrix:** RF-018 procedure with all M3 features present; only the user-opened editor activates.
- **MV-M3-05 Performance/soak:** idle CPU with themes + new widgets within RNF-001; 8-hour soak within RNF-002.
- **MV-M3-06 Regression:** Core-Release features and frozen-backlog code still behave as before.

### 11.4 Gates

- After every M3 task: `dotnet build` and `dotnet test` succeed; the full M1+M2+M3 suite stays green.
- Manual MV-M3 checks pass or produce a documented, human-accepted limitation.
- A separate reviewer agent validates the implementation against this spec; reviewers do not modify code.

## 12. Integration with the existing architecture

| M3 area | Existing seam it extends | Required approach |
|---|---|---|
| New widget types | `WidgetConfig`/`WidgetTypes`, `WidgetConfigJsonConverter`, `WidgetRegistry`/`WidgetRegistration`, `WidgetViewModelBase`, `WidgetTemplateKeys`, per-type template dictionaries in `App.xaml` | One DTO, one VM, one registration, one template, one converter branch per type. **No host/`PageViewModel`/`WidgetTemplateSelector`/`EditModeService` change** (M3-RF-461). |
| Bar monitoring | public `ISensorSnapshotSource`/`SensorSnapshot`, Gauge consumption pattern | Consume snapshots like Gauge; never poll or touch history. |
| Clock timing | `TimeProvider` | One boundary-scheduled timer; update-on-change only. |
| Hotkey editor | `HotKeyParser`/`SendKeyCombination`, `HotKeyService`/`App.ApplyHotKeys`, `IPanelConfigApplyTarget.ApplyHotKeys`, `ActionPickerViewModel`, `TrayMenuCommand`/`TrayService` | New focused editor + WPF-free view model; reuse the parser and the existing re-registration hook; add a tray entry. |
| Hotkey persistence | `SettingsChange`/`SettingsCoordinator`/`ISettingsService`/`IConfigStore` | Add an optional **`SettingsChange.Hotkeys` property** (record with optional properties); persist through the single writer; `BuildUpdated` preserves all unchanged sections; apply live. |
| Themes/accent | `Theme/Colors.xaml`/`Styles.xaml`, widget template dictionaries, `DynamicResource` | **Required refactor (M3-RF-851):** convert theme references in `Styles.xaml` and every widget template to `DynamicResource`, remove the per-dictionary `Colors.xaml` self-merge, and merge the active theme dictionary at the application level; split into per-theme dictionaries with the same key set; swap at runtime with wholesale rollback on failure; accent applied as a global layer under per-widget overrides. |
| Icon packs | `IconCatalog` (stable id namespace), `WidgetViewModelBase.ApplyAppearance` | Add `IconPackCatalog` with packs falling back to `default`; refresh live VMs' `IconGlyph` on pack change. |
| Schema | `PanelConfig.CurrentSchemaVersion` (=3), `ConfigMigrationChain`, `MigrationRunner`, `ConfigValidator` | Bump to 4; add `V3ToV4Migration`; validate/fallback appearance; keep unknown-type preservation. |
| Apply pipeline | `PanelConfigApplier`/`IPanelConfigApplyTarget` | Add an isolated `appearance` step + `ApplyAppearance`; compose with the existing `SettingsCoordinator` path. |
| Composition root | `App.xaml.cs` (`ApplyHotKeys`, `CreateDefault` registry, settings window hosting, tray commands) | Wire the new editor, `ThemeManager`, `IconPackCatalog`, and the appearance apply step; keep startup order and shutdown participation unchanged. |

**Threats to M1/M2 invariants and how M3 protects them:**

- **Focus preservation (RF-018):** only the user-opened M3 editor activates (D-6 analogue); appearance/hotkey application never activates the panel or changes the foreground.
- **Single writer / no polling (RF-080):** hotkey and appearance edits persist only through the existing store on defined triggers; no file watcher, no polling added.
- **UI-thread discipline (RNF-004):** theme swap, VM refresh, Clock callbacks, and hotkey re-registration are marshalled to the UI thread; saves are async.
- **Layer purity (RNF-010):** new Config/Input logic stays WPF-free; P/Invoke stays in `Interop/`; `Theme/` remains resources + thin hosts.
- **Untrusted configuration (RF-081) / no new execution surface:** allowlists and safe parsers for theme/pack/color/format; ImageButton uses the existing action executor; no external files/packs loaded.
- **Bounded history (RF-069/RNF-012):** Bar consumes snapshots only; the history store is untouched.

**Frozen and delivered work is not touched:** the delivered Core Release features keep working (AC-M3-11); frozen-backlog code remains in the build unsupported/unmaintained and is not extended (RF-092's `SettingsWindow` in particular is not modified — M3 adds a new editor instead); dropped items stay dropped; RF-072 is skipped.

## 13. Resolved decisions and open questions

| ID | Decision (overridable) | Where applied |
|---|---|---|
| D-M3-1 | **M3 scope:** exactly RF-046 + RF-061 + RF-084; **RF-072 skipped** (elevation/UAC, cannot run unattended). | Header, §2, §8 |
| D-M3-2 | **New focused `HotkeysEditorWindow`** (tray entry) rather than extending the frozen `SettingsWindow` (RF-092 stays frozen). Its view model is reusable and WPF-free. | §5.2, §12 |
| D-M3-3 | **New widgets:** `Clock` (display-only), `ImageButton` (built-in icon + single action), `Bar` (single-value horizontal meter over snapshots). Icon-based, no external image files. | §5.1 |
| D-M3-4 | **Themes:** built-in `dark` (default)/`light`/`highContrast`; global `accentColor`; built-in `iconPack` set with stable-id fallback to `default`. Precedence global theme < global accent < per-widget overrides. | §5.3 |
| D-M3-5 | **Schema v4** via additive `V3ToV4Migration` (adds the global `appearance` section). New widget types need no schema step (additive discriminator values). | §7, M3-RF-469/846 |
| D-M3-6 | **Appearance selection UI** lives in the same new M3 editor window as an "Appearance" section, over the same `ISettingsService` path. | M3-RF-850 |
| D-M3-7 | **Live theme swap requires a resource-layer refactor.** Theme references convert to `DynamicResource`, per-dictionary `Colors.xaml` self-merges are removed, and the active theme dictionary is merged at the application level (M3-RF-851); the apply step uses wholesale swap-with-rollback so the UI is never half-themed. | §5.3, M3-RF-842/845/848/851, AC-M3-12 |
| D-M3-8 | **New-type-specific fields are JSON-only in M3** — the frozen property editor (M2-RF-433) is not extended. **Superseded by `M4-RF-432`/`D-M4-2`** (M4 promotes the property editor and makes the new types' type-specific fields editable in the UI). | M3-RF-468, AC-M3-13 |

**Open questions:** none blocking. The product-visible defaults (D-M3-1..8, and the specific third widget/two-pack names in D-M3-3/4) are the human-overridable choices.

## 14. Traceability — base 001 → M3 sub-requirements

| 001 base | M3 sub-requirements |
|---|---|
| RF-046 additional widget types | M3-RF-461..469 |
| RF-061 hotkey management | M3-RF-611..619 |
| RF-084 themes/accent/icon packs | M3-RF-841..851 |
| RNF-002/003/004/006/008/010/012/013/014/015 (refinements) | M3-RNF-131..140 |
| RF-076/D-19 schema migration | M3-RF-846 |
| RF-072 | **Skipped** — no sub-requirements |

**Coverage check:** every `[M3]` requirement (RF-046, RF-061, RF-084) maps to at least one M3 sub-requirement. **RF-072 is intentionally skipped.** No `RF-092`/`RF-093` or other frozen-backlog requirement is implemented; no dropped requirement is revived. Existing 001/002 IDs are unchanged.
