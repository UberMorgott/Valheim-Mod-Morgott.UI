using HarmonyLib;
using Morgott.UI.Ownership;
using UnityEngine;

namespace Morgott.UI.ItemBrowser
{
    // ITEMS tab wiring (docs\item-browser-design.md section 1, as VNEI InventoryGuiPatch): built after the crafting skin,
    // Craft / Upgrade presses switch back, SetupCrafting / UpdateCraftingPanel re-apply the up state after vanilla.
    [HarmonyPatch]
    internal static class BrowserPatches
    {
        private static bool Prepare() => OwnershipRegistry.IsOwned(OwnershipRegistry.Inventory) && OwnershipRegistry.IsOwned(OwnershipRegistry.Crafting);

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Awake))]
        [HarmonyPostfix]
        [HarmonyPriority(Morgott.UI.PatchOrder.Crafting - 1)] // after CraftingSkin.Attach (tab row styled first)
        private static void Awake_Postfix(InventoryGui __instance)
        {
            try { BrowserPanel.Attach(__instance); }
            catch (System.Exception e) { Plugin.Log.LogError($"[Morgott.UI] item browser tab failed (no ITEMS tab): {e}"); }
        }

        // Index built behind the loading screen, not on the first ITEMS open (~280 ms hitch): Game.FixedUpdate runs
        // UpdateRespawn while m_requestRespawn holds (Game.cs:684-697, 728-742) and Hud.UpdateBlackScreen keeps the
        // loading screen up until the player exists (Hud.cs:573). Once per respawn wait; changes before the fade are caught
        // by UpdateBlackScreen_Prefix, later ones still rebuild on open.
        private static bool _waiting;

        [HarmonyPatch(typeof(Game), nameof(Game.UpdateRespawn))]
        [HarmonyPrefix]
        private static void UpdateRespawn_Prefix(Game __instance)
        {
            var waiting = __instance.WaitingForRespawn();
            if (waiting && !_waiting)
                try { Browser.EnsureReady(); }
                catch (System.Exception e) { Plugin.Log.LogWarning($"[Morgott.UI] item browser prebuild failed (builds on open): {e.Message}"); }
            _waiting = waiting;
        }

        // Registrations keep landing after the respawn wait (spawn-time module passes, config sync); the last fully black
        // frame re-checks the fingerprint so the first ITEMS open finds a current index. Vanilla fades out from the first
        // frame its cover condition fails (Hud.UpdateBlackScreen :573/:620-626); no-op unless something changed.
        [HarmonyPatch(typeof(Hud), nameof(Hud.UpdateBlackScreen))]
        [HarmonyPrefix]
        private static void UpdateBlackScreen_Prefix(Hud __instance, Player player)
        {
            var screen = __instance.m_loadingScreen;
            if (!screen || !screen.gameObject.activeSelf || screen.alpha < 1f) return;
            if (player == null || player.IsDead() || player.IsTeleporting() || Game.instance.IsShuttingDown() || player.IsSleeping()) return;
            try { Browser.EnsureReady(); }
            catch (System.Exception e) { Plugin.Log.LogWarning($"[Morgott.UI] item browser re-check failed (builds on open): {e.Message}"); }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnTabCraftPressed))]
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnTabUpgradePressed))]
        [HarmonyPrefix]
        private static void TabPressed_Prefix()
        {
            if (BrowserPanel.Instance) BrowserPanel.Instance.Close();
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.SetupCrafting))]
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateCraftingPanel))]
        [HarmonyPostfix]
        [HarmonyAfter("org.bepinex.plugins.jewelcrafting")]
        private static void Crafting_Postfix()
        {
            if (BrowserPanel.Up) BrowserPanel.Instance.Enforce();
        }

        // Hover key hint under inventory grid tooltips, added to the shown text: UITooltip.UpdateTextElements (on hover
        // start and on a changed Set) writes Localize(m_text) into the tooltip's "Text". Not in m_text: Epic Loot's
        // CreateItemTooltip prefix replaces the vanilla text builder (TooltipPatches.InventoryGrid_CreateItemTooltip_Patch,
        // ItemTooltip.GetInventoryText), and changing m_text after Set would re-set the tooltip every frame.
        [HarmonyPatch(typeof(UITooltip), nameof(UITooltip.UpdateTextElements))]
        [HarmonyPostfix]
        private static void TooltipText_Postfix(UITooltip __instance)
        {
            if (Browser.RecipeKey == null || !UITooltip.m_tooltip || !__instance.GetComponentInParent<InventoryElement>()) return;
            var t = Utils.FindChild(UITooltip.m_tooltip.transform, "Text");
            if (t && t.TryGetComponent<TMPro.TMP_Text>(out var text) && text.text.Length > 0)
                text.text += KeyHint(Browser.RecipeKey.Value, Browser.UsedInKey.Value);
        }

        // Latin key names from the live config (memory key-hints-english), words through the browser's own tokens.
        internal static string KeyHint(BepInEx.Configuration.KeyboardShortcut recipe, BepInEx.Configuration.KeyboardShortcut used)
        {
            var parts = new System.Collections.Generic.List<string>(2);
            if (recipe.MainKey != KeyCode.None) parts.Add($"[{recipe}] {Browser.Localize("$ib_obtained_from")}");
            if (used.MainKey != KeyCode.None) parts.Add($"[{used}] {Browser.Localize("$ib_used_in")}");
            return parts.Count == 0 ? "" : $"\n\n<color={GameScreens.ActiveEffectsPage.Hex("color.text.muted", new Color(0.62f, 0.58f, 0.5f))}>{string.Join("   ", parts)}</color>";
        }

        // Text typed into the browser's search must not close the inventory (InventoryGui.Update :551: "Use" / "Inventory").
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Update))]
        [HarmonyPrefix]
        private static void Update_Prefix()
        {
            if (!BrowserPanel.Typing) return;
            ZInput.ResetButtonStatus("Inventory");
            ZInput.ResetButtonStatus("Use");
        }
    }
}
