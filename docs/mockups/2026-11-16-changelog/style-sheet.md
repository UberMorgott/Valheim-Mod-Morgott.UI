# Changelog window mockup: style sheet

Sources: `Morgott.UI\art\STYLE.md`, `src\Morgott.UI\Theme\default-theme.json`,
refs `docs\mockups\tabs-ref\tabs-c-glass-rune.png` (frames/tabs), `docs\mockups\2026-11-08\tooltip-b.png` (typography).
All sizes in 1920x1080 reference px.

## Window

- ~1100x760, centered over the blurred main menu; scrim #04070A @ 0.80 outside the window.
- Frame rail: dark slate carved stone #2a343b, 11-14 px thick, grooves #05080a / #5c6d78, light from top-left.
- Corners: inset stone block #222b31 (~60 px reach) with knotwork + one glowing cyan rune #4fe0e6.
- Interior: near-black glass #121d23 -> #0b1419 (top->bottom), alpha 0.90; faint inner cyan line #2395b7 1 px.
- Close "x": small square stone plaque top-right, silver glyph, cyan on hover.

## Tabs (variant C glass + rune)

- Plaque height 44, font 21, gap 8 (size.startgame.tab.*).
- Selected: label #e8b64a gold, cyan rune underline #4fe0e6 flowing into the panel, small rune glyph left.
- Normal: label #e3e6e8, no underline, sits slightly darker.

## Text

- Title: serif caps (Norsebold display), #d9dce0, ~30 px, letter-spaced.
- Body: PT Sans regular, #e3e6e8, 17-18 px; secondary/meta #8a9096; disabled #6f767a.
- Date headers: serif, #d9dce0, ~20 px, thin rule #3d8290 after it.
- Mod name: PT Sans bold #f0c419 (item-title gold); version #8ab4f0.

## Type tags / chips (accent colors)

| Type | Hex |
| --- | --- |
| Новое | #9fd66b |
| Исправления | #e0413a |
| Изменения | #4fe0e6 |
| Баланс | #e8b64a |
| Производительность | #8ab4f0 |
| Моды | #8e7bb8 |

- Chip: dark glass pill, 1-2 px rim in its accent, label #e3e6e8 + count in accent; active chips filled ~25% accent.
- Line tag: small rounded rect, accent text on accent @ 0.18 fill.

## Inputs, scrollbar

- Fields/dropdown: inset glass #0b1419, 1 px rim #5c6d78, height ~34; focus rim #4fe0e6.
- Scrollbar: thin carved stone track (~8 px), handle slate with cyan rune glint; hover cyan.
- Footer: thin rule, "Показано 128 из 2 341" body text #8a9096 with numbers #e3e6e8.

Glow only on runes, selection rims, the inner panel line.
