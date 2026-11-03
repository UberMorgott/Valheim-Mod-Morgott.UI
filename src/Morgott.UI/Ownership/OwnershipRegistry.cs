using System.Collections.Generic;
using Morgott.UI.Theme;

namespace Morgott.UI.Ownership
{
    // Screen families are the ownership unit: each family is styled by exactly one skin. Decided once in Awake (other
    // mods may ask ITheme.IsOwned from their Start) and never changed in the session, so a screen is never handed back
    // half-mutated. A requested family is owned only when its theme assets passed the preflight (otherwise the vanilla
    // look stays), unless RequireAssets is off (dev runs before the art bundle exists). Family list + docs: Plugin.Families.
    public static class OwnershipRegistry
    {
        public const string Menu = "Menu", Tooltips = "Tooltips", Hud = "Hud", Inventory = "Inventory", Crafting = "Crafting";
        public const string Skills = "Skills", Trophies = "Trophies", Minimap = "Minimap", EnemyHud = "EnemyHud";
        public const string MessageHud = "MessageHud", DamageText = "DamageText", MainMenu = "MainMenu", Settings = "Settings";
        public const string Connection = "Connection", BuildHud = "BuildHud", Crosshair = "Crosshair", ActionHud = "ActionHud";
        public const string ShipHud = "ShipHud", KeyHints = "KeyHints", LoadScreen = "LoadScreen", Chat = "Chat", Popups = "Popups";
        public const string TextDialogs = "TextDialogs", Compendium = "Compendium", SplitDialog = "SplitDialog", Store = "Store";
        public const string Barber = "Barber", DevPanels = "DevPanels", Feedback = "Feedback", ModUi = "ModUi";
        public const string EpicLootEnchanting = "EpicLootEnchanting", EpicLootBoard = "EpicLootBoard", EpicLootTemper = "EpicLootTemper";
        public const string ModHud = "ModHud", InventoryExtras = "InventoryExtras", Radial = "Radial", MenuCompendium = "MenuCompendium";
        public const string SummonFrames = "SummonFrames", PlayerShields = "PlayerShields", Cursor = "Cursor", CharacterStats = "CharacterStats";

        // Every family of this build in config order (set by Freeze; the autotest driver reads it by name).
        public static string[] Known = new string[0];

        private static readonly HashSet<string> Owned = new HashSet<string>();
        public static bool Frozen { get; private set; }

        public static void Freeze(IDictionary<string, bool> requested, bool requireAssets)
        {
            if (Frozen) return;
            Frozen = true;
            Known = new List<string>(requested.Keys).ToArray();
            foreach (var pair in requested)
            {
                var family = pair.Key;
                if (!pair.Value)
                {
                    Plugin.Log.LogInfo($"[Morgott.UI] ownership {family}: not requested (config)");
                    continue;
                }
                var missing = new List<string>();
                var ready = ThemeTokens.FamilyReady(family, missing);
                if (!ready && requireAssets)
                {
                    Plugin.Log.LogWarning($"[Morgott.UI] ownership {family}: NOT taken, assets missing: {string.Join(", ", missing)}");
                    continue;
                }
                Owned.Add(family);
                Plugin.Log.LogInfo(ready
                    ? $"[Morgott.UI] ownership {family}: owned"
                    : $"[Morgott.UI] ownership {family}: owned without assets (RequireAssets=false), missing: {string.Join(", ", missing)}");
            }
        }

        public static bool IsOwned(string family) => Owned.Contains(family);
    }
}
