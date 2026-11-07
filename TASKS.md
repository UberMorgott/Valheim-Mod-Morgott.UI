# Morgott.UI tasks (was RuneSkin)

Open items only. Workspace-wide steps (Phase 6.x, item browser IB.*): `E:\DEV\Valheim\docs\mod-structure-TASKS.md`.
Finished work: History below (one line per milestone); details in `git log` / tags. The full pre-cleanup backlog
(per-milestone notes, run ids, text floor path list) is `git show 20f294d:TASKS.md`.

## Open

### Item icons

- [ ] Needs regen without yellow cast: 217 restyled icons reverted to their original sprite (2026-11-08,
      fix/revert-yellow-icons) because the restyle tinted them yellow: wood / plank family, honey, bee, berries,
      mushrooms + every override with Lab `b*` up >= 12 into the yellow hue band vs its original. List:
      `docs/icons-yellow-reverted.txt` (also `pack_exclude.txt` in Morgott.Patcher `tools\iconstyle`). Fix the warm
      shift in iconstyle (median `b*` +4.5 over all 1566 compared icons), re-stylize these, drop them from the exclude
      list, pack.

### Perf / leaks

- [ ] Remaining allocations: CharacterPreview.LateUpdate (camera render window), InventorySkin / CraftingSkin LateUpdate
      0.9-3.6 KB/frame, HUD idle (HudCluster / BarView; PlayHud.cs:228, BarView.cs:410). Real per-method numbers need an
      exact per-thread counter; none works in the 6000.0.75f1 player (Profiler.GetMonoUsedSizeLong counts every thread /
      mod, harness gates the excess over the time-share expectation since w28).
- [ ] uifix-lag: frame time flat (7.01 -> 7.56 ms) but objects under InventoryGui grew 1187 -> 2449 over 3 tab walks
      (run 20261103-005216); breakdown logged by tools 88f5633 on the next run.

### Text / fonts (own track, per-screen in-game iterations)

- [ ] Text below 16 px at 1080p (`<prefix>-text-min`, gated on m5 only; other screens report `otherTracks`). Plan (Codex
      chat fb40c2e3, 2026-10-26): no blanket ReadableSweep on in-world roots; floor only roles whose container wraps or
      grows; buttons / tabs / dropdowns / placeholders / fixed cells get per-screen container sizing; crafting res_name
      excluded until the socket fits 2 lines at 16 px; text lists cached at open / restyle (ForeignUi dirty pass),
      re-measured on scale / language / text change from the original size. Offenders as of runs 20261023-132956 ..
      20261024-213803 (root: count, smallest px, shots): TabContent (Epic Loot enchanting) 29, 14, m7; IngameGui 21, 14,
      info/m6c/m7; ConnectionPanel 18, 13.9, m6c; HUD 13, 10.9, info/m1/m4-m6c; ItemList 7, 14, m6c/m7; Crafting 6, 10.5;
      hudroot 5, 10.9; root 5, 7.2; EnchantingUI(Clone) 4, 10, m7; Player 3, 8; ListView 2, 14, info; Panel 2, 14.1, m7;
      Players 2, 8, m6c; one each: AchContainer 9.8, AvailableBountiesPanel 14, BuildHud res_name 15.8, Cost 14.1,
      Description 14, EnemyHud 14, HotKeyBar 12, Inventory_screen 15.3, LoadingGUI 15, RecipeList 14.2, TreasureMap 13.
- [ ] Shrink-to-fit floor (0.28.4, `<prefix>-shrink-min` gated everywhere): TextInset / crafting Label + FitName /
      item browser / ammo name stop at the rendered 16 px and ellipsize. Exceptions with a shrink below it, by layout:
      plate labels (PlateLabelFit / Restyler.Fit, size.text.fit.min 13: at 16 the pre-world buttons truncated) and crafting
      recipe rows (`CraftingSkin` row name min 12: mockup column width, 20 names per list ellipsized at 16; run
      20261107-021204). Both need container sizing (wider column / two-line plates) before they can take the floor.
- [ ] Faces outside the two-font theme (`<prefix>-two-fonts`): `HUD/hudroot/HotKeyBar/HotKeyElement(Clone)/binding`
      Valheim-AveriaSansLibre (m1, m4-m7); `IngameGui/EnemyHud/HudRoot/HudBase(Clone)/Alerted` Valheim-AveriaSerifLibre
      (m6; EnemyHudSkin.AlertFace applied, awaits a ui-runeskin-m4 run).

### Pre-existing harness FAILs (not re-run since; no current code touches them)

- [ ] m4-bar-caps-1920x1080 / -2560x1440 problems=[bars=Stamina,Health] layout=strip-0.16 (runs 20261026-102357, -171856).
- [ ] m4-maphint-inset-1920x1080 / -2560x1440 "no large-map UITooltip with text" (run 20261026-171856).
- [ ] overdraw-map 1920x1080 + 2560x1440 (runs 20261023-161531, 20261026-102357, -171856).
- [ ] m5-text-min settings stepper value 14.9 px (CommonSettings/InputLayout/GUIStepper/Value/Text; run 20261026-171058,
      PASS in 20261026-171856).
- [ ] grid-cells-chest-large-1280x720: container cells past the right edge at the cell floor (runs 20261024-134656 ..
      -175034, layout A).

### Windows / layout

- [ ] corner-clear-<res> / -info-*: InventoryElement "binding" key label inside RuneSkinInventory_Center's bottom-right
      cap reach (dx 39 / dy 8 / C 43.5 at 1080p).
- [ ] winmove-inventory-chest-2560x1440-handle: FAIL since the dock header was top-aligned (raycast at the header centre
      lands on dropButton, run 20261026-184538); find what blocks the band.
- [ ] m2-layout chest header title truncated ('Сундук из че…' at the 12 px floor): the corner keep-out narrows the dock
      header plate; needs a title room rule (smaller slot shift or two lines).
- [ ] Craft row field-align / button-skew (m2 / m3 / info): the amount field is ActionRow-scaled (k = 0.4 W / need = 0.82 at
      craft W ~ 300; main W ~ 328 -> k 0.90, field 48.9 -> gap 19 > 12); the CRAFT glyph clamps at b.X + 0.3 H, so the
      label group stays off-centre. Menu changelog button-skew at 1280x720 (gaps 120/135) also still FAILs (run
      20261029-105944).

### HUD strip / belt

- [ ] Joint in-game belt run with the Morgott.Power Belt (belt shipped here in 0.25.33, inert without it), the Morgott.EpicLoot
      belt patch and pack cfg `Enable Quick Slots = false`: belt + item loss (save/relog, death, shrink, quick-row items back
      to the grid). No run recorded here yet.
- [ ] Epic Loot belt with PotionSlots 1 equipped did not raise PotionSlotCount.Max (3 -> 3): provider side (now
      Morgott.Power); the strip follows the API. Re-check with the belt run.
- [ ] Art polish (Codex in-game fidelity 6.5): halo / caption / frame tint; hotbar cells read double-framed (StripCell rarity
      rim / glow inside the art well); empty 4th potion slot shows no key label (EAQS has 3 quick cells, capacity 4 from the
      provider); f1p1 / f2p3 end medallions a few px off the mockup rows 1-2; glyph_ship_sail_half Codex 7.
- [ ] Craft shift label flicker: not reproduced with simulated AltPlace Press() (40/40 frames); real-key path (Input System
      events) live only. Stepper > 1 not exercised (test character max craftable = 1).

### Live report 2026-11-02 (not reproduced, need user detail)

- [ ] Bug 3: flicker with fragments of other UI - uifix-tab-frames 0 blinks over 3 tab walks (run 20261103-005216).
- [ ] Bug 5: mouse stays captured after closing a window - Tab / Skills+Esc / Esc: cursor Locked, player turns (run
      20261103-005216). Which window, closed how?

### Epic Loot 0.15.4+ (sockets dock window)

- [ ] Gamepad group focus indicator for dock windows (selected_frame hidden on every panel, sockets included).
- [ ] Gamepad right-stick tooltip scroll: TooltipPlacer disables EL ScrollWheelHandler (which also runs GamepadScroll).
- [ ] Sockets header glyph: no socket art in the bundle (header shows the title only).
- [ ] Harness 97u-socket-use reads EpicLoot.ShardStones.SocketsUI.OpenEquipment / m_container: dead on 0.15.4.

### Other

- [ ] Typed theme regions for own-mod objects `PA_Trash` / `PA_CraftSearch` (Morgott.Patcher) and `CR_Frame`
      (Morgott.Creatures) through Morgott.Core `ITheme` (today found by object name: `Morgott.Core.Contracts`,
      `src\Morgott.UI\Compat\ModCompat.cs`).
- [ ] Jotunn ModCompatibility window never shot (appears only on a version mismatch).
- [ ] Art provenance: the Codex style references `D:\tmp\cx-augaskin\mockup-1.png` / `work\ref\*` are gone; what mockup-1 was
      made from cannot be recorded. Bundle contents are Codex image gen + OFL fonts (no Auga sprite / font / shader / code).

## History

- M0-M2 (2026-10-21): theme tokens + bundle, classifier, journaled restyler, ownership, diagnostics; Menu / Tooltip / HUD
  cluster / inventory layout A; harness shots ui-runeskin-m0..m2.
- M3-M5 (2026-10-22, 0.5.0-0.6.x): crafting column, skills, trophies, minimap, enemy HUD, messages, damage text,
  pre-world screens.
- M6-M7 (2026-10-22, 0.7.0-0.11.0): in-game vanilla screens, mod panels, generic ForeignUi fallback restyler (family ModUi).
- 0.9.x-0.10.x: rune face preset, minimap fix, hand mirrors removed, global layout rules (batch G), cell zones.
- AugaSkin retired (2026-10-23): features ported (batch P), removed from the pack; skin standalone.
- 0.12.0-0.14.1: live 3D character preview, enemy bars E1-E4, perf track B1-B5 (layered theme, ModCompat table).
- 0.15.x-0.19.0 (Track H): bottom HUD strip, two-font theme (PT Sans + Valheim-Norsebold), hold prompt, centre gauges.
- 0.23.x-0.24.0 (2026-10-26): centre wings, summon party frame, windows-v2 (one RuneWindowHeader per window), integrated
  action header (plinth).
- 0.25.x (2026-10-28/29): Sprite Atlas V2 packer, first-Tab cold open 214 -> 51 ms (hidden first open behind the
  loading screen), tooltip clearing, rp-relog census fixes, tab_* redraw (pick C glass rune, Codex 8.3), belt row
  (0.25.33), Epic Loot 0.15.4-0.15.6 sockets dock (0.25.37).
- 0.26.0-0.27.3 (2026-11-04): rename to Morgott.UI (Core `ITheme`, `$ui_*` keys, `morgott.valheim.ui`, RuneSkin.API deleted with
  its unused paths, Almanac support removed), icon restyle / mod status icons / CurrencyPocket tooltip moved in from
  MorgottTweaks, item browser ITEMS tab (IB.2), VNEI coexistence dropped (VNEI retired, 0.27.2).
