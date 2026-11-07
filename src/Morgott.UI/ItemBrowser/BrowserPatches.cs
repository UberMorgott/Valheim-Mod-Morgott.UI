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

        // Hover key hint under inventory grid tooltips only: InventoryGrid.CreateItemTooltip (InventoryGrid.cs:409-412) sets
        // the tooltip from item.GetTooltip(); appending inside GetTooltip keeps UITooltip.Set's equal-text early-out.
        private static bool _gridTip;

        [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.CreateItemTooltip))]
        [HarmonyPrefix]
        private static void GridTip_Prefix() => _gridTip = true;

        [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.CreateItemTooltip))]
        [HarmonyFinalizer]
        private static void GridTip_Finalizer() => _gridTip = false;

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip), typeof(int))]
        [HarmonyPostfix]
        private static void GetTooltip_Postfix(ref string __result)
        {
            if (_gridTip && Browser.RecipeKey != null) __result += KeyHint(Browser.RecipeKey.Value, Browser.UsedInKey.Value);
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
