# Item browser: Gems category (mockup, 2026-11-21, approved by the user)

Mockup: `gems-tab.png` (1920x1080, Codex image gen via `cx.ps1 -Img`, 3 passes). Implemented in Morgott.UI 0.40.20 (category, sub line, search, Elite marker) + Morgott.EpicLoot 0.5.8 (per-kill chances); the Tier chip and the item-page type line "Камень босса · Тир 3" are not built yet. Mod chip: devcommands only (user, 2026-11-21).
Facts / plan: side-session notes `gem-browser-tab-facts.md` (sections 4, 8, 9).

## What it shows
Three copies of the ITEMS panel side by side as a demo; in game they are states of ONE panel (item page replaces the list).
1. List, search `bonemass`: new category button (gem glyph, after fish, before `...`) selected; strip only `Бол 33`;
   chip `Источник: Bonemass`; caption `Камни · 33`; header `БОЛОТО · ПОСЛЕ БОССА · 33`; rows `<Gem> T3 / Камень босса · Bonemass`,
   marker `Болото`; selected row `Гнилое сердце T3`.
2. List, no search, scrolled to the end: strip 33 per stage Луг..Пеп (T1 Eikthyr .. T7 Fader), `Сев 0`, `? 45`; caption
   `Камни · 276`; tail of `ПЕПЕЛЬНЫЕ ЗЕМЛИ · ПОСЛЕ БОССА` (Fader T7 gems), then section `КАМНИ МОНСТРОВ · 45`
   (ShardUnique; rows `Камень монстра · Элитные мобы, Hildir`, marker `? Элита`).
3. Item page `Гнилое сердце T3`: `Камни · Камень босса · Тир 3`, `Epic Loot · 1013_Mythic_ShardStone`.
   ДОБЫВАЕТСЯ: `Bonemass 3,3 % за убийство`; `Набор Epic Loot: камни боссов, тир 3  1–2 шт.`;
   `Улучшение: 3 × «Гнилое сердце T2» + Железо ×5 · Слизь ×3 · Редк. Пыль ×5` (real T2->T3 resources, patch 115);
   `Обмен: 3 любых камня тира 3 → 1`. ИСПОЛЬЗУЕТСЯ: tier-up to T4 (`Стол чар · уровень 3`), item socket.
Chips: Источник / Тир / Мод (+ reset). Search by boss name = new (today search matches name/prefab id only).

## Data notes
- 276 = 231 boss gems (33 types x T1..T7) + 45 monster gems. 3,3 % = 0.9/33 + 0.1*(1-(32/33)^2) at boss L1.
- Gem-to-type mapping on the page is illustrative (1013 = pool type 1, not checked to be Rotting Heart).
- Monster gems shown under their own section with stage `?` (no derived biome yet); real stage = open point.

## References
`docs\mockups\item-browser-progression\01-weapons.png` (approved list style), `ref-ingame-panel-crop.png`,
`ref-ingame-browser.png`, `ref-style-tooltip.png`, `docs\gems\icons\_sheet.png` + per-gem icons.
Missing: `Morgott.UI\docs\mockups\2026-11-04\04*/05*.png` (folder does not exist), so the item page layout follows the
facts file section 6 description, not an image.

## Codex
Prompts: `prompt-gems-tab.txt` (full spec + style-fix block from pass 1 review), `prompt-v3-edit.txt` (edit pass on v2).
Self-score vs refs (`cx`, image review): v1 7/10, v2 7/10, v3 7/10 (target 8 not reached in 3 passes).
Remaining Codex notes (fix in code, not art): icons are redrawn - use real `_sheet` icons; row frames thinner and
highlight dimmer like 01-weapons; frame less glossy; smaller grey italic sub-line legibility; `Редк. Пыль` casing is the
real EL localized name, kept.
