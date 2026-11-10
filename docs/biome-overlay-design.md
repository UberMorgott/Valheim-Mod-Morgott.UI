# Biome overlay ("Подсветка биомов") — design note

Status: implemented in Morgott.UI 0.29.39 (Core 0.8.0 `Contracts.PortalPinType`, Morgott.Patcher 0.5.5 portal pin type). Approach: own RawImage (Jotunn MinimapManager overlays draw on the small map too and replace the map colour). Mockup: `docs/mockups/2026-11-10/biome-overlay.png` (rev 3).

User decisions (2026-11-10): overlay fills explored areas only; large map only (no HUD minimap overlay); legend becomes a filter list (checkbox per pin/marker type + per layer); map screen always full screen (windowed/restored state removed); maximize/restore button removed.

## Overlap check

- ValheimPlus `[Map]` (cfg line 1738): shareMapProgression, exploreRadius, syncPins, displayCartsAndBoats, preventPlayerFromTurningOffPublicPosition. No biome overlay.
- Pack plugins (BepInEx\plugins, 2026-11-10): no biome-overlay mod. Only Jotunn references `m_mapTexture` (its MinimapManager overlay API, not used by any pack mod for biomes).

## Game wiring (decompile 1.0.21, `Minimap.cs`)

- `GenerateWorldMap` (1950): per pixel `WorldGenerator.GetBiome(wx, wy)` at `wx = (j - size/2) * m_pixelSize + m_pixelSize/2`; colour from `GetPixelColor` (2092). Cached per world (`cacheMinimapBiome`, 578).
- `m_mapTexture` is already a biome-colour texture, but Mountain, DeepNorth and Ocean are all white there, so biome ids cannot be decoded from it. We sample `GetBiome` ourselves on the same grid.
- Large map: `RawImage m_mapImageLarge` with material `m_mapLargeShader` (`_MainTex`, `_MaskTex`, `_HeightTex`, `_FogTex`); pan/zoom via `m_mapImageLarge.uvRect` (1270).
- Fog: `m_fogTexture` R = own unexplored, G = shared unexplored (`Reset` 598, `ResetSharedMapData` 612). `Explore(x,y)` (1857) and `ExploreOthers(x,y)` (1914) clear one pixel; bits are in `m_explored` / `m_exploredOthers`; `SetMapData` (2158) loads them.

## Approach

- Biome id grid: `byte[m_textureSize^2]` from `GetBiome` on the vanilla grid (same size, same pixel centres). Built once per world seed on a background thread, cached at `BepInEx\cache\Morgott.UI\biomes-<seed>-<size>.bin` (rule: caches inside game folder). Thread-safety of `WorldGenerator.GetBiome` off the main thread must be verified first; fallback = time-sliced coroutine on the main thread.
- Overlay texture: RGBA32, same size, `filterMode = Point` or Bilinear (pick in lab). Pixel = biome fill colour at alpha `FillAlpha`; a pixel whose 4-neighbour has another biome id = outline colour at alpha `OutlineAlpha`. Ocean = transparent. One edge-detect pass over the id grid, no geometry.
- Render: child `RawImage` of the map image's parent, same rect as `m_mapImageLarge`, `raycastTarget = false`, sibling index right after the map image so pins (`m_pinRootLarge`) and the player marker stay on top; `uvRect` copied from `m_mapImageLarge` every LateUpdate while the large map is open. No custom shader.
- Colours: vanilla palette (`m_meadowsColor` etc.), except where it collides on the overlay: Mountain white, DeepNorth pale ice blue, Mistlands vanilla dark grey lifted to grey-violet so it reads on dark terrain. Values in the theme JSON (`Theme/default-theme.json`).
- Outline thickness = one texture pixel (`m_pixelSize` world metres); at high zoom it looks blocky — lab decides Point vs Bilinear, or a 2x upsampled overlay if needed.

## Fog of war (proposal)

- Overlay shows only explored pixels: alpha = 0 where both `m_explored` and `m_exploredOthers` are false.
- Postfix on `Minimap.Explore` / `ExploreOthers` when they return true → set that overlay pixel alpha; one `Apply()` per frame if dirty. Rebuild alpha from the bit arrays after `SetMapData`, `Reset`, `ResetSharedMapData`.
- No information leak: unexplored biome borders are never shown.

## Perf

- Grid: `m_textureSize^2` `GetBiome` calls once per world (vanilla does the same work plus height/mask in `GenerateWorldMap`); then cache hit = file read.
- Runtime: zero cost while the large map is closed (overlay inactive); open = one extra UI quad + texture, no per-frame CPU beyond uvRect copy. Explore postfix = one `SetPixel` per newly explored pixel.

## Jotunn alternative (check before implementing)

- Jotunn.dll ships `MinimapManager` map overlays (`MapOverlayBase`, `GetMapOverlay`/`RemoveMapOverlay`, `MinimapOverlayPanel` with per-overlay toggles, fog handling `DrawFogFilter`/`IgnoreFog`). Morgott.UI already skins `MinimapOverlayPanel` (LargeMap.cs:20). Before writing the RawImage layer, query Jotunn docs (Context7 `/websites/valheim-modding_github_io_jotunn`): if a Jotunn overlay can respect fog and stay large-map-only, use it (project rule: prefer Jotunn manager). Not yet verified.

## Map screen layout changes

- Always full screen: the map window is permanently in today's maximized geometry (safe area). Remove `MapWindow.cs` maximize button (`BuildMaxButton`, `ToggleMaximize`, flag `WindowMove.Map "map#max"`) and the restored/windowed rect path; window is not movable. HUD small minimap is already hidden by vanilla in Large mode (`SetMapMode`); nothing else draws a small map on the map screen.
- Legend = one scrollable filter list: sections МЕТКИ (pin types), ИГРОКИ (players), СЛОИ (layers: "Подсветка биомов" + any Jotunn overlays). Every row = icon socket + label + checkbox in one column. "Все"/"Мои" stays under МЕТКИ.

## Pin types in the filter list

Vanilla `Minimap.PinType` (Minimap.cs:24): Icon0..Icon4, Death, Bed, Shout, None, Boss, Player, RandomEvent, Ping, EventArea, Hildir1..3, Memorial. Filter = `m_visibleIconTypes[(int)type]` (checked for every pin in `UpdatePins`, :1622; vanilla never persists it, resets to all-true in Awake :554).

| Row (ru) | Types | Notes |
| --- | --- | --- |
| Огонь | Icon0 | |
| Дом | Icon1 | |
| Молот | Icon2 | |
| Точка | Icon3 | |
| Пещера | Icon4 | caves only (portals have their own row since Patcher 0.5.4) |
| Смерть | Death | |
| Босс | Boss | |
| Кровать | Bed | |
| Событие | RandomEvent + EventArea | |
| Хильдир | Hildir1..3 | |
| Мемориал | Memorial | |
| Крик | Shout | |
| Сигнал | Ping | vanilla has no Ping filter button but `UpdatePins` honours the array, so it is filterable |
| Награды / Сокровища | EpicLoot BountyPinType / TreasureMapPinType | mod types; EpicLoot owns the filter (`AdventurePinFilter`, toggles itself); row clicks its button as today |

- Not rows: None (never drawn), Player (handled by ИГРОКИ section).
- Other pack mods adding pins: only EpicLoot and Morgott.Patcher (string scan of BepInEx\plugins for `AddPin`, plus ValheimPlus cart/boat pins when `displayCartsAndBoats` on — currently off).

## Config (Jotunn/ConfigurationManager, all features default ON)

- `[Map] BiomeOverlay = true`; `[Map] BiomeOverlayFillAlpha = 0.25`; `[Map] BiomeOverlayOutlineAlpha = 0.60` (Codex compare of run 1: calmer borders).
- Pin filters: `[Map Filters] <Row> = true` per vanilla row above (`Fire, Home, Hammer, Dot, Cave, Death, Boss, Bed, Event, Hildir, Memorial, Shout, Ping`). Applied to `m_visibleIconTypes` on map open (Minimap.Start postfix); checkbox click writes the entry, and a SettingChanged handler writes the array, so ConfigurationManager and the list stay in sync.
- EpicLoot rows: not persisted by us (EpicLoot owns that state; persisting would need an EpicLoot adapter — skip).
- Players: per-player visibility is session data (names change per server) — not persisted.
- Layers: each Jotunn overlay keeps Jotunn's own state.
- Removed: `map#max` window flag (no maximize).

## User answers (2026-11-10)

- Portals: own filter row "Портал" (`[Map Filters] Portal`), pin type `Contracts.PortalPinType` = 100 (Patcher grows `m_visibleIconTypes`).
- Biome colour key: yes, two columns under "Подсветка биомов" in СЛОИ.
- "Все" / "Мои" buttons of the mockup: not built (the shared-data toggle was removed on 2026-11-08, map data is always shared).

## Run-2 follow-ups (coordinator 2026-11-10, Codex compare 7.5/10)

- Fog tint: unexplored pixels of the overlay carry `color.map.fog` (#374E66, alpha 0.65) over the vanilla beige clouds, toward the mockup grey (`[Map] FogTint`, default on). No new fog art.
- Borders: zoomed in (a texel wider than `size.map.biome.line.switch` = 2 screen px) the overlay shows the fill-only texture and `BorderLines` (UI quads per merged border run of a texel window around the view, max `count.map.biome.lines.max` = 12000, clipped to the map image) draws `size.map.biome.line` = 1.5 px lines; zoomed out the texture's one-texel outline is already that thin.
- Section titles: `font.map.header` = the game's Valheim-AveriaSerifLibre, capitals (third face, map list only; the two-faces rule of 2026-10-22 otherwise stands).
- Icons (Codex image gen, post-processed by crop + 128 px area resample only): `Assets\MapLayersIcon.png` (СЛОИ row), `Assets\MapBossIcon.png` (red boss skull: Boss pin sprite in `Minimap.m_icons`, existing boss pins, the vanilla Boss filter button).
- Players: 4 rows visible (`count.map.players.max`), more scroll with the wheel over the ИГРОКИ block.