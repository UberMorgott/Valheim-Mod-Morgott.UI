using Morgott.Core;
using Morgott.UI.Api;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Morgott.UI.Diagnostics;
using Morgott.UI.Ownership;
using Morgott.UI.Responsive;
using Morgott.UI.Screens;
using Morgott.UI.Theme;

namespace Morgott.UI
{
    // Morgott.UI (was RuneSkin): clean-room UI skin that restyles live vanilla uGUI from theme tokens (no prefab swaps, no
    // transpilers). Awake: config -> theme + asset bundle -> ownership frozen -> ITheme registered -> Harmony patches of owned families.
    [BepInPlugin(Contracts.UiGuid, "Morgott.UI", Version)]
    [BepInDependency(Contracts.CoreGuid)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [BepInProcess("valheim.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Version = "0.29.7";

        public static ManualLogSource Log { get; private set; }
        public static ConfigEntry<float> SkinScale { get; private set; }
        public static ConfigEntry<float> PanelFade { get; private set; }
        public static ConfigEntry<float> NumberOutline { get; private set; }
        public static ConfigEntry<bool> CharacterPreview { get; private set; }
        public static ConfigEntry<bool> MovableWindows { get; private set; }
        public static ConfigEntry<bool> ResetLayout { get; private set; }
        private static ThemeProvider _theme;
        public static ConfigEntry<float> HudScale { get; private set; }
        public static ConfigEntry<float> MinimapScale { get; private set; }
        public static ConfigEntry<bool> UltrawideBox { get; private set; }
        public static ConfigEntry<float> StripWidth { get; private set; }
        public static ConfigEntry<int> StatusPerRow { get; private set; }
        public static ConfigEntry<int> StatusRows { get; private set; }
        public static ConfigEntry<float> StatusSize { get; private set; }
        public static ConfigEntry<string> BuffSide { get; private set; }
        public static ConfigEntry<bool> CenterGauges { get; private set; }
        public static ConfigEntry<float> GaugeIdleDelay { get; private set; }
        public static ConfigEntry<float> GaugeOpacity { get; private set; }
        public static ConfigEntry<bool> RageInRow { get; private set; }
        public static ConfigEntry<KeyboardShortcut> StatusListKey { get; private set; }
        public static ConfigEntry<string> StatusListButton { get; private set; }
        public static ConfigEntry<KeyboardShortcut> StatsKey { get; private set; }
        public static ConfigEntry<float> StatsRefresh { get; private set; }
        public static ConfigEntry<float> StatsReferenceHit { get; private set; }

        private static ConfigEntry<bool> _requireAssets;
        private static ConfigEntry<string> _modUiOptOut;
        public static ConfigEntry<bool> WatchTheme { get; private set; }
        private static ConfigEntry<bool> _verboseLog;
        // Development diagnostics (skin counts per screen, mod units, HUD keep-out moves): off in play.
        public static bool Verbose => _verboseLog != null && _verboseLog.Value;
        private int _ownOrder;

        // Ownership switches (family, what Morgott.UI does): restart-only by design (families freeze at startup), advanced
        // in ConfigurationManager, listed in this order.
        private static readonly (string Family, string Does)[] Families =
        {
            ("Menu", "styles the pause menu."),
            ("Tooltips", "styles tooltips."),
            ("Hud", "draws the player HUD (bars, food, status effects, hotbar placement)."),
            ("Inventory", "lays out the inventory screen (layout A: doll, grid, chest / backpack dock, crafting placement)."),
            ("Crafting", "styles and lays out the crafting column (station header, tabs, recipe list, detail, requirements). Needs Inventory."),
            ("Skills", "styles and lays out the skills dialog (needs Inventory)."),
            ("Trophies", "styles and lays out the trophies panel (needs Inventory)."),
            ("Minimap", "draws the small minimap as a round rune ring (compass runes, wind, day phase, biome plaque) and restyles the large map screen."),
            ("EnemyHud", "restyles enemy health bars, star levels and the boss bar (Vaettir rarity frames keep their tiers)."),
            ("MessageHud", "restyles centre messages, top-left notifications, unlock messages and the biome-discovered banner."),
            ("DamageText", "restyles floating damage numbers (font, outline, colour per hit type)."),
            ("MainMenu", "styles the pre-world screens (main menu, character select / create, world and server select, dialogs, loading)."),
            ("Settings", "styles the Settings window (category list, sliders, checkboxes, dropdowns, key bindings)."),
            ("Connection", "styles the server password and connecting dialogs."),
            ("BuildHud", "styles the build menu, the selected-piece strip and the hovered-piece author window."),
            ("Crosshair", "styles the crosshair, hover text, piece health bar and the stealth eye."),
            ("ActionHud", "styles the action progress and stagger bars, the event / raid line and the mount panel."),
            ("ShipHud", "draws the ship HUD as a speed ladder with a rudder socket (wind stays on the minimap ring)."),
            ("KeyHints", "styles the key hints (key plates and labels; gamepad glyphs stay)."),
            ("LoadScreen", "styles the black screens over the world (respawn tip banner + rune spinner, sleep, teleport ring)."),
            ("Chat", "styles the chat window, in-world chat texts and NPC / raven speech bubbles."),
            ("Popups", "styles the small popups (achievement unlock, join code, session player list, closed captions)."),
            ("TextDialogs", "styles the text input (signs, portals, pins, names) and the text viewer (runestones, intro saga, raven)."),
            ("Compendium", "styles the compendium / lore dialog."),
            ("SplitDialog", "styles the split stack dialog."),
            ("Store", "styles the trader store (Haldor, Hildir, Bog Witch; Epic Loot's own trader panels excluded)."),
            ("Barber", "styles the barber / mirror."),
            ("DevPanels", "styles the F5 console and the F2 connect panel."),
            ("Feedback", "styles the feedback dialog."),
            ("ModUi", "restyles every window no other family owns (other mods' canvases and panels, Jotunn windows, IMGUI fonts, other mods' main menu lines): theme panels, controls and fonts, the mod's own layout kept."),
            ("EpicLootEnchanting", "styles Epic Loot's enchanting table window and its dialogs."),
            ("EpicLootBoard", "styles Epic Loot's adventure board at Haldor (secret stash, gamble, treasure maps, bounties)."),
            ("EpicLootTemper", "styles Epic Loot's tempering panel at Hildir."),
            ("ModHud", "styles HUD parts other mods add (Epic Loot ability bar and debug text, level texts)."),
            ("InventoryExtras", "styles the achievements panel, the variant dialog and the dragged item."),
            ("Radial", "styles the radial menus (texts; wheel art and icons stay)."),
            ("MenuCompendium", "adds a Compendium entry under Settings in the pause menu (needs Menu)."),
            ("SummonFrames", "shows a frame per summon next to the hotbar (icon, health with the summon's energy shield, respawn / stay / off state)."),
            ("PlayerShields", "shows other players' energy shield and absorb shield as rails under their overhead health bar (MorgottTweaks publishes them)."),
            ("Cursor", "sets its rune mouse cursor."),
            ("CharacterStats", "adds the character stats window (5th button of the inventory info row, or Hud.StatsKey): pools and regeneration per second, resistances, movement and weight, defense, food, rest, combat modifiers, MorgottTweaks shield and slots (needs Inventory)."),
        };

        // A family that lives inside another one's screen stays with the other skin without it.
        private static readonly Dictionary<string, string> Needs = new Dictionary<string, string>
        {
            [OwnershipRegistry.Crafting] = OwnershipRegistry.Inventory,
            [OwnershipRegistry.Skills] = OwnershipRegistry.Inventory,
            [OwnershipRegistry.Trophies] = OwnershipRegistry.Inventory,
            [OwnershipRegistry.CharacterStats] = OwnershipRegistry.Inventory,
            [OwnershipRegistry.MenuCompendium] = OwnershipRegistry.Menu,
        };

        private ConfigDescription Advanced(string description) =>
            new ConfigDescription(description, null, new ConfigurationManagerAttributes { IsAdvanced = true, Order = 1000 - _ownOrder++ });
        private Harmony _harmony;

        private long _initStart;

        private void Awake()
        {
            _initStart = Stopwatch.GetTimestamp();
            Log = Logger;
            if (CoreApi.ApiMajor != 1)
            {
                Log.LogError($"[Morgott.UI] inactive: Morgott.Core API {CoreApi.ApiMajor}, needs 1");
                return;
            }
            Translations.Load(typeof(Plugin).Assembly, "Morgott.UI.Translations.");
            // Shared VFX (Core IVfx) do not depend on the theme: registered even when the skin stays inactive.
            if (!CoreApi.Headless) Services.Register<IVfx>(VfxProvider.Instance = new VfxProvider(System.IO.Path.GetDirectoryName(Info.Location)));
            // Icons and compat text do not depend on the theme either (MorgottTweaks moved them here, TASKS 4.5).
            _harmony = new Harmony(Contracts.UiGuid);
            Icons.IconRestyle.Register(Config, _harmony);
            var wanted = new Dictionary<string, bool>();
            foreach (var (family, does) in Families)
            {
                wanted[family] = Config.Bind("Ownership", family, true, Advanced("Morgott.UI " + does + " Read once at startup (restart to change).")).Value;
                if (family == OwnershipRegistry.ModUi)
                    _modUiOptOut = Config.Bind("Ownership", "ModUiOptOut", "Morgott.Creatures,CR_WorldBossBar", Advanced(
                        "Comma-separated object or plugin assembly names the fallback restyler (ModUi) never touches, with everything below them (default: Morgott.Creatures' boss / raid / target bars keep their own design, styled by the EnemyHud track). Read once at startup."));
            }
            HudScale = Config.Bind("Hud", "Scale", 1f, new ConfigDescription(
                "Scale of the bottom strip (its cells, frames and gaps; the inventory grid shares the strip's cell), the minimap ring and the HUD margins on top of the game's UI scale. Fonts never go below the readable floor.",
                new AcceptableValueRange<float>(PlayerHud.HudMath.HudScaleMin, PlayerHud.HudMath.HudScaleMax)));
            MinimapScale = Config.Bind("Hud", "MinimapScale", 1f, new ConfigDescription(
                "Minimap ring size on top of the theme size (size.minimap.ring) and Hud.Scale; its height cap (share.minimap.ring.h) scales with it. Applied live.",
                new AcceptableValueRange<float>(0.6f, 1.6f)));
            UltrawideBox = Config.Bind("Hud", "UltrawideBox", true,
                "On 21:9 and wider screens the HUD corners are those of a centred 16:9 box (off = true screen corners).");
            StripWidth = Config.Bind("HudStrip", "Span", 1f, new ConfigDescription(
                "Width of the bottom HUD strip (key Span: the old Width was only a cap) as a share of the screen (or of the centred 16:9 box on ultrawide); only the straight wing rails stretch. Its height follows the slot size: hotbar cells = inventory grid cells (theme size.slot x Layout.SkinScale x the game's UI scale).", new AcceptableValueRange<float>(0.5f, 1f)));
            StatusPerRow = Config.Bind("HudStrip", "StatusPerRow", 10, new ConfigDescription(
                "Status effect icons per row above the HUD strip (buffs on one side, debuffs on the other); a full row wraps upward.", new AcceptableValueRange<int>(1, 30)));
            // new key (was StatusSize 0.6 = the whole tile): the icon alone now, the name + time lines come on top of it
            StatusSize = Config.Bind("HudStrip", "StatusIconSize", 0.75f, new ConfigDescription(
                "Status effect icon side as a share of the hotbar slot; the effect name and remaining time are written under each icon.", new AcceptableValueRange<float>(0.4f, 1f)));
            StatusRows = Config.Bind("HudStrip", "StatusRows", 2, new ConfigDescription(
                "Rows of status icons per side before the rest folds into \"+N\" (F7 lists all).", new AcceptableValueRange<int>(1, 6)));
            BuffSide = Config.Bind("HudStrip", "BuffSide", "Left", new ConfigDescription(
                "Side of the HUD strip the buffs grow from (debuffs take the other side).", new AcceptableValueList<string>("Left", "Right")));
            CenterGauges = Config.Bind("HudCenter", "Enabled", true,
                "Rage (adrenaline) ring around the crosshair and the player's stagger column beside it, shown while they change (the vanilla stagger bar and adrenaline bar are hidden).");
            GaugeIdleDelay = Config.Bind("HudCenter", "IdleDelay", 1.5f, new ConfigDescription(
                "Seconds a centre gauge stays after its value stopped changing before it fades out (a full rage ring stays while full).", new AcceptableValueRange<float>(0.2f, 10f)));
            GaugeOpacity = Config.Bind("HudCenter", "Opacity", 0.85f, new ConfigDescription(
                "Opacity of the centre gauges while shown.", new AcceptableValueRange<float>(0.2f, 1f)));
            RageInRow = Config.Bind("HudCenter", "RageInBottomRow", false,
                "Also show rage (adrenaline) as a bar in the strip's resource row next to stamina / eitr while it is above 0.");
            StatusListKey = Config.Bind("Hud", "StatusListKey", new KeyboardShortcut(UnityEngine.KeyCode.F7),
                "Opens / closes the full list of status effects when the strip shows a +N cell.");
            StatusListButton = Config.Bind("Hud", "StatusListGamepadButton", "JoyDPadDown",
                "Gamepad: this ZInput button while JoyAltKeys is held opens / closes the full status list. Empty = none.");
            StatsKey = Config.Bind("Hud", "StatsKey", new KeyboardShortcut(UnityEngine.KeyCode.None),
                "Opens / closes the character stats window (with the inventory). None = only the inventory info button.");
            StatsRefresh = Config.Bind("Hud", "StatsRefresh", 0.25f, new ConfigDescription(
                "Seconds between value updates of the open character stats window.", new AcceptableValueRange<float>(0.05f, 2f)));
            StatsReferenceHit = Config.Bind("Hud", "StatsReferenceHit", 100f, new ConfigDescription(
                "Character stats resistances: the share of a hit of this size, per damage type, that Epic Loot, resistances and armor remove (the game's own damage steps run on it).",
                new AcceptableValueRange<float>(1f, 1000f)));
            WorldHud.EnemyHudConfig.Bind(Config);
            PlayerHud.BarFxConfig.Bind(Config);
            // [Diagnostics]: development switches, not looks (perf audit B4: were mixed into [Ownership]).
            _requireAssets = Config.Bind("Diagnostics", "RequireAssets", true, new ConfigDescription(
                "Take a family only when its theme assets loaded (otherwise the other skin keeps it). Off = take it anyway (development before the art bundle exists). Read once at startup.",
                null, new ConfigurationManagerAttributes { IsAdvanced = true, Order = 2 }));
            _verboseLog = Config.Bind("Diagnostics", "VerboseLog", false, new ConfigDescription(
                "Write development diagnostics to the log: skin counts per screen (diag), mod panel units, HUD strip keep-out moves. Warnings (unknown graphics, theme issues) are always written.",
                null, new ConfigurationManagerAttributes { IsAdvanced = true, Order = 0 }));
            WatchTheme = Config.Bind("Diagnostics", "WatchTheme", true, new ConfigDescription(
                "Reload the theme when the override BepInEx\\config\\Morgott.UI\\theme.json is written (0.5 s after the last change; same as the ui_reload console command). The override lists only the entries it changes (layered over the shipped theme; // comments allowed). Read once at startup.",
                null, new ConfigurationManagerAttributes { IsAdvanced = true, Order = 1 }));
            SkinScale = Config.Bind("Layout", "SkinScale", 1f, new ConfigDescription(
                "Extra scale of Morgott.UI panels on top of the game's UI scale.",
                new AcceptableValueRange<float>(ResponsiveContext.MinSkinScale, ResponsiveContext.MaxSkinScale)));
            NumberOutline = Config.Bind("Hud", "NumberOutline", 0.18f, new ConfigDescription(
                "Dark outline width (TMP SDF units) of every HUD number (bar values, slot keys / counts / timers, status timers); with a soft dark shadow, so white digits read on white. Applies on the next start or theme reload.",
                new AcceptableValueRange<float>(0f, 0.4f)));
            PanelFade = Config.Bind("Layout", "PanelFade", 0.15f, new ConfigDescription(
                "Seconds the inventory screen fades in / out in place (open and close; 0 = instant). Replaces the vanilla slide, which moved the Morgott.UI panels off their layout.",
                new AcceptableValueRange<float>(0f, 1f)));
            CharacterPreview = Config.Bind("Layout", "CharacterPreview", true,
                "Live 3D preview of your character in the inventory (off = carved silhouette). Applies on the next game start.");
            MovableWindows = Config.Bind("Layout", "MovableWindows", true,
                "Windows can be dragged by their title band (inventory group, crafting, stats, skills, trophies, compendium, map legend, mod panels); a double-click on the band puts that window back. Off = every window at its default dock.");
            ResetLayout = Config.Bind("Layout", "ResetLayout", false, new ConfigDescription(
                "Reset layout: every moved window back to its default dock (all resolutions; positions file BepInEx\\cache\\Morgott.UI\\window-positions.txt). Set to true (or press the button) to reset; it turns itself off.",
                null, new ConfigurationManagerAttributes { CustomDrawer = DrawResetLayout, HideDefaultButton = true }));
            MovableWindows.SettingChanged += (_, __) => Restyle.WindowMove.Load(); // owners re-lay out (offsets on / off)
            ResetLayout.SettingChanged += (_, __) => ConsumeResetLayout();
            ConsumeResetLayout();

            if (!ThemeTokens.Load())
            {
                Log.LogError("[Morgott.UI] inactive: no valid theme");
                OwnershipRegistry.Freeze(new Dictionary<string, bool>(), true);
                return;
            }

            var requested = new Dictionary<string, bool>();
            foreach (var (family, _) in Families)
                requested[family] = wanted[family] && (!Needs.TryGetValue(family, out var host) || wanted[host]);
            OwnershipRegistry.Freeze(requested, _requireAssets.Value);

            if (!CoreApi.Headless) Services.Register<ITheme>(_theme = new ThemeProvider());

            gameObject.AddComponent<ResponsiveContext>();
            if (WatchTheme.Value) ThemeWatcher.Attach(gameObject);

            var patchStart = Stopwatch.GetTimestamp();
            // M7: precise mod panels + the fallback restyler for every UI no family owns, Jotunn windows, IMGUI fonts
            // (their own runtime hooks), set up before the class patches that read their state.
            Screens.TooltipAdapter.Harmony = _harmony;
            ModScreens.ElDialogs.Harmony = _harmony;
            Compat.FadePark.Harmony = _harmony;
            Compat.EpicLootShardSlots.Harmony = _harmony;
            ModScreens.ForeignUi.Init(gameObject, _harmony, _modUiOptOut.Value);
            ModScreens.JotunnHook.Init(_harmony);
            ModScreens.ImguiFonts.Init(gameObject, _harmony);
            // One PatchAll(Assembly) (perf audit B3): every patch class gates itself on its family's ownership (static
            // Prepare), so FastStartup's HarmonyBatcher builds each patched game method once (22 PatchAll(Type) calls
            // rebuilt InventoryGui.Awake 6x, Hud.Awake 2x, InventoryGui.Update 2x). Ties between RuneSkin's own
            // postfixes on one method are explicit (PatchOrder): class order in the assembly is not a contract.
            // WorldHud.PlayHudAdapter has no Prepare: each of its hooks gates per family (PlayHud.Owned).
            ItemBrowser.Browser.Bind(Config);
            if (OwnershipRegistry.IsOwned(OwnershipRegistry.Crafting)) Jotunn.Utils.ModQuery.Enable(); // item browser: prefab -> mod
            _harmony.PatchAll(typeof(Plugin).Assembly);
            if (OwnershipRegistry.IsOwned(OwnershipRegistry.Cursor)) CursorSkin.Apply();
            var ms = (Stopwatch.GetTimestamp() - patchStart) * 1000.0 / Stopwatch.Frequency;
            Log.LogInfo($"[Morgott.UI] {Version} ready; init {(patchStart - _initStart) * 1000.0 / Stopwatch.Frequency:0} ms theme + ownership, {ms:0} ms patches");
        }

        // After every plugin's Awake: Warfare, Epic Loot and CurrencyPocket may load after us.
        private void Start()
        {
            if (_harmony == null) return; // inactive (Core API mismatch)
            Icons.ModStatusIcons.Register(Config, _harmony);
            Compat.CurrencyPocketTooltip.Register(Config, _harmony);
            Compat.BuildCameraText.Register(Config, _harmony);
        }

        private void OnDestroy()
        {
            if (_theme != null) Services.Unregister<ITheme>(_theme);
            if (VfxProvider.Instance != null) Services.Unregister<IVfx>(VfxProvider.Instance);
            _harmony?.UnpatchSelf();
        }

        // Re-reads the theme (override file) and re-applies every live root. Ownership never changes at runtime.
        public static void ReloadTheme()
        {
            if (!ThemeTokens.Load()) return;
            ScreenRegistry.RestyleAll();
            if (OwnershipRegistry.IsOwned(OwnershipRegistry.Cursor)) CursorSkin.Apply();
        }

        // Coverage scan for tools (AutotestDriver calls it by reflection): visible graphics styled / explained / unstyled,
        // writes BepInEx\RuneSkin\diag\coverage-<state>.txt; first line = summary.
        public static string[] Coverage(string state) => Diagnostics.Coverage.Scan(string.IsNullOrEmpty(state) ? "now" : state);

        // Diagnostics entry for tools (AutotestDriver calls it by reflection): writes BepInEx\RuneSkin\diag\<screen>.txt.
        // Layout.ResetLayout: true (cfg edit, ConfigurationManager toggle or its button) resets every window position,
        // then the entry turns itself back off.
        private static void ConsumeResetLayout()
        {
            if (ResetLayout == null || !ResetLayout.Value) return;
            Restyle.WindowMove.ResetAll();
            ResetLayout.Value = false;
        }

        // ConfigurationManager custom drawer (IMGUI, its own window): one button.
        private static void DrawResetLayout(ConfigEntryBase entry)
        {
            if (UnityEngine.GUILayout.Button("Reset layout (all windows)", UnityEngine.GUILayout.ExpandWidth(true))) Restyle.WindowMove.ResetAll();
        }

        public static string[] DiagDump(string screen)
        {
            PlayerHud.HudCluster.Instance?.Record();
            MapScreen.MinimapSkin.Instance?.Record();
            WorldHud.EnemyHudSkin.Record();
            WorldHud.MessageSkin.Instance?.Record();
            WorldHud.DamageTextSkin.Record();
            WorldHud.PlayHud.Record();
            return SkinDiagnostics.Dump(string.IsNullOrEmpty(screen) ? null : screen).ToArray();
        }
    }

    // Order of RuneSkin's own postfixes that share one game method (HarmonyX: higher priority first, ties by
    // registration). Priority.Last = 0; the old sequential PatchAll(Type) order, kept: InventoryGui.Awake / Update
    // inventory -> crafting -> skills -> trophies -> game screens -> extras; Hud.Awake HUD cluster ->
    // play HUD (WorldHud.PlayHudAdapter, Priority.Last) -> game screens -> extras; FejdStartup.Start main menu -> mod lines.
    internal static class PatchOrder
    {
        public const int Hud = Priority.Last + 1;
        public const int HudGameScreens = Priority.Last - 1, HudExtras = Priority.Last - 2;
        public const int Crafting = Priority.Last - 1, Skills = Priority.Last - 2, Trophies = Priority.Last - 3,
            InventoryGameScreens = Priority.Last - 5, InventoryExtras = Priority.Last - 6, CharacterStats = Priority.Last - 7;
        public const int MainMenuMods = Priority.Last - 1;
    }

    // Console: ui_diag [screen] (dump), ui_reload (theme). Registered with the vanilla terminal.
    [HarmonyPatch]
    internal static class ConsoleCommands
    {
        [HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
        [HarmonyPostfix]
        private static void InitTerminal_Postfix()
        {
            _ = new Terminal.ConsoleCommand("ui_diag", "[screen] Morgott.UI diagnostics dump to BepInEx\\Morgott.UI\\diag", args =>
            {
                var paths = Plugin.DiagDump(args.Length > 1 ? args[1] : null);
                args.Context?.AddString($"Morgott.UI: {paths.Length} dump(s) in {SkinDiagnostics.DumpDir}");
            });
            _ = new Terminal.ConsoleCommand("ui_reload", "reload the Morgott.UI theme (override BepInEx\\config\\Morgott.UI\\theme.json)", args =>
            {
                try
                {
                    Plugin.ReloadTheme();
                    args.Context?.AddString($"Morgott.UI: theme '{ThemeTokens.ThemeName}' rev {ThemeTokens.Revision} from {ThemeTokens.Source}");
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"[Morgott.UI] reload failed: {e}");
                }
            });
        }
    }
}
