# Morgott.UI (was RuneSkin)

Valheim UI skin (BepInEx): restyles the live vanilla uGUI in place from a data-driven theme (carved stone frames,
rune glow, dark glass). No prefab replacements, no transpilers; unknown UI is left untouched and reported.

## Layout

- `src/Morgott.UI` - the plugin (`morgott.valheim.ui`), implements Morgott.Core `ITheme` (other mods call it typed
  through `Morgott.Core.Theme.Available`):
  `Theme/` (ThemeTokens, JSON schema, bundle loader), `Classify/` (Classifier), `Restyle/` (Restyler, journal marks),
  `Ownership/` (frozen screen-family ownership), `Diagnostics/`, `Responsive/`, `Api/` (ITheme backend),
  `Translations/` (Jotunn localization JSON, `$ui_*`), `Screens/` (per-screen adapters), `Icons/` (item icon restyle
  from `Assets/ItemIcons`, icons for other mods' icon-less statuses from `Assets/StatusIcons.png`).
- `build/ref/Morgott.Core.dll` - pinned Core reference (`valbuild ref add`).
- `unity/` - Unity 6000.0.75f1 project that builds the asset bundles; `art/` - sources.
- `docs/theme-contract.md` - asset names the theme expects.

## Build

```powershell
dotnet build Morgott.UI.sln -c Release            # or -p:ValheimDir=<game dir>, -p:CoreRef=<local Morgott.Core.dll>
```

Deploy `src\Morgott.UI\bin\Release\net472\Morgott.UI.dll`, its `icons\` folder (item icon atlas) and the bundles to `BepInEx\plugins\Morgott.UI\`.
Needs Morgott.Core and Jotunn.

## Config (`BepInEx\config\morgott.valheim.ui.cfg`)

- `[Ownership] <Family>` - families Morgott.UI styles; frozen at startup.
- `[Diagnostics] RequireAssets` - take a family only when its art loaded.
- `[Layout] SkinScale` - 0.85-1.15 on top of the game's UI scale.
- `[IconRestyle] Enabled` - one icon style for every item the shipped atlas lists (atlas built by Morgott.Patcher
  `tools/iconstyle`, renders from Morgott.DevTools.IconCapture); independent of the theme.
- `[Compat] ModStatusIcons` - icons for Warfare bleeding / impale and Epic Loot paralyze (they ship none);
  `[Compat] CurrencyPocketTooltip` - CurrencyPocket's English "Coin Drop" tooltip translated.

Console: `ui_diag [screen]` (dump to `BepInEx\Morgott.UI\diag`), `ui_reload` (theme override
`BepInEx\config\Morgott.UI\theme.json`).
