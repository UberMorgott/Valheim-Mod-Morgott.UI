# Step 3.U1 / 3.U1b invariants (RuneSkin -> Morgott.UI)

Kept:
- Every screen family, its config key (section `[Ownership]`, same names, same order) and the restart-only freeze; family
  dependencies (Crafting / Skills / Trophies / CharacterStats need Inventory, MenuCompendium needs Menu).
- Theme API behaviour for the four consumers, now typed through Morgott.Core `ITheme`: MT HUD pools / capacities, MT
  sort panel actions + lock overlays + glyph sprites, Hamingja hold prompt (`hold.render`), Vaettir boss-bar theme
  sprites / floats, AdventureBackpacks `panel.dock`; provider faults disabled after 3 throws.
- Translations (176 keys, English + Russian) with the same texts; keys renamed `$runeskin_*` -> `$ui_*`.
- Theme files, bundle `runeskin` (unchanged bytes), GameObject names `RuneSkin*` (harness and coverage key on them).
- Shader sources and property contracts (Morgott.Core IVfx doc).

Intended changes:
- Assembly / namespace / GUID / cfg `Morgott.UI`, `morgott.valheim.ui`, `morgott.valheim.ui.cfg` (cfgport map
  `docs/cfgport-3.U1.map`: pack cfg had only defaults); dirs `BepInEx\Morgott.UI\diag`, `config\Morgott.UI\theme.json`,
  `cache\Morgott.UI`; console `ui_diag`, `ui_reload`; log tag `[Morgott.UI]`.
- RuneSkin.API deleted. API-only paths nobody used removed: registered doll slots, docked panels, status effect
  descriptors, roots / Bind / Invalidate, action hotkeys + disabled reasons.
- Localization through Jotunn LocalizationManager (Core `Translations.Load`), no own Localization patch.
- Shaders renamed `Morgott/Shell`, `Morgott/Telegraph`, `Morgott/UI Boss FX`, `Morgott/UI Bar` in the new
  `morgott_vfx` bundle (unity `BuildVfx`), served by `IVfx` (registered in Awake even when the theme is invalid; not
  headless). The UI bundle build no longer imports shaders (the shipped `runeskin` still carries the old copies, unused).
- Almanac support removed (Almanac left the pack, user 2026-11-04).
- `CharacterStats` ownership switch is advanced in ConfigurationManager like the others.
