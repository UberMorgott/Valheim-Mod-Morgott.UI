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
