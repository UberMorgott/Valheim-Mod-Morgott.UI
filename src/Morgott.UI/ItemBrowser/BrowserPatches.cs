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
