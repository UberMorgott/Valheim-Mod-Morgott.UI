# Step 4.5 invariants (MorgottTweaks icon / UI glue -> Morgott.UI)

Moved from MorgottTweaks 03f7d99: `Modules/IconRestyle.cs` -> `Icons/IconRestyle.cs`, `Modules/ModStatusIcons.cs` +
`ModStatusIconMap.cs` -> `Icons/`, `Modules/CurrencyPocketTooltip.cs` -> `Compat/`, `Assets/ItemIcons/*` (atlas, 14 pages)
+ `Assets/StatusIcons.png` + `Assets/icons/status_icons.json` -> `src/Morgott.UI/Assets/`, test `ModStatusIconChecks` ->
`ModStatusIconMapTests`.

Stay in MorgottTweaks (universal; Morgott.UI is client-only, absent on the dedicated server):

- `Core/IconText.cs`: status-effect icon texts of auras / boss gems (`AurasEffect.cs:156`, `BossGems.cs:490,557`,
  `BossGemDownsides.cs:312`).
- `Core/IconTint.cs`: recoloured icons of items registered by MT (`EarlyMagicModule.cs:136`, `HarvestMagicModule.cs:85`,
  `HarvestWeaponsModule.cs:121`, `LifeStaffModule.cs:160`); Morgott.Summons has its own copy (`NecroSetModule.cs:137`).
- `Modules/GlyphCache.cs`: used only by `DeathMessagesModule.cs:102` (Survival domain, universal).
- `EmbeddedIconAtlas` (`AuraIcons.cs`): aura icons (Morgott.Summons has its own copy); Morgott.UI loads its one status
  atlas itself (`ModStatusIcons.Load`).
- `RuneSkinHud.cs`, `RuneSkinOwnership.cs` (brief), `tools/iconstyle` (atlas builder, now packs into this repo's
  `Assets/ItemIcons`).

Kept:

- Atlas format v2 (`mt-item-icons`, manifest validation, BC7 + gzip, page prefetch on worker threads, lazy page load,
  drop of unused prefetched pages after `OnItemsRegistered`), swap rules (`m_icons[0]` copy, variants kept, first original
  kept in `Originals`, ZNetScene copies), Priority.Last postfixes on `ObjectDB.Awake` / `CopyOtherDB`, the shipped files in
  `<plugin>\icons\`. Registration still before the main menu's ObjectDB and before Toadstool's `OnItemsRegistered`
  handler (MT FejdStartup), so the sapling still copies the restyled icon.
- Status icons: same 4 statuses / 3 cells, Warfare `OnEnable` postfixes, Epic Loot prototype postfix + `EL_Paralyze`
  into ObjectDB on `OnItemsRegistered`, only icon-less effects filled; registered from `Plugin.Start` (late plugins).
- CurrencyPocket transpiler (2 literals, warning on mismatch), from `Plugin.Start`.
- Config: same sections and keys (`[IconRestyle] Enabled`, `[Compat] ModStatusIcons`, `[Compat] CurrencyPocketTooltip`,
  defaults true, restart-applied); map `docs/cfgport-4.5.map` (live cfg: 0 port, 3 default).
- Translation texts (English + Russian).

Intended changes:

- Keys `$mt_cp_coin_drop[_desc]` -> `$ui_cp_coin_drop[_desc]`; sprite / texture names `MT_ItemIcon_*` / `MT_ItemIcons_N` /
  `MT_StatusIcon_*` -> `UI_ItemIcon_*` / `UI_ItemIcons_N` / `UI_StatusIcon_*` (only the harness reads them: patch
  `handoff-4.5-tools.patch`).
- Icons register before the theme loads and stay on when the theme is invalid (Harmony instance created before it);
  IconRestyle disabled / empty atlas / no GPU = nothing patched or subscribed (was: subscribed, no-op).
- `InternalsVisibleTo Morgott.DevTools.IconCapture` (reads `IconRestyle.Originals`).
- Pack: `plugins\Morgott.UI\icons\` (from `bin\Release\net472\icons`) must be staged with the DLL; `plugins\MorgottTweaks\icons\`
  removed from Z:, VPS, game, dedicated server.
