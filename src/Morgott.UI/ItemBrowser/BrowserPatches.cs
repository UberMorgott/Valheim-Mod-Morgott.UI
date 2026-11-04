using HarmonyLib;
using Morgott.UI.Compat;
using Morgott.UI.Ownership;
using UnityEngine;

namespace Morgott.UI.ItemBrowser
{
    // ITEMS tab wiring (docs\item-browser-design.md section 1, as VNEI InventoryGuiPatch): built after the crafting skin,
    // Craft / Upgrade presses switch back, SetupCrafting / UpdateCraftingPanel re-apply the up state after vanilla and VNEI.
    [HarmonyPatch]
    internal static class BrowserPatches
    {
        private static bool Prepare() => OwnershipRegistry.IsOwned(OwnershipRegistry.Inventory) && OwnershipRegistry.IsOwned(OwnershipRegistry.Crafting);

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Awake))]
        [HarmonyPostfix]
        [HarmonyPriority(Morgott.UI.PatchOrder.Crafting - 1)] // after CraftingSkin.Attach (tab row styled first)
        [HarmonyAfter(ModCompat.VneiGuid)]
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
        [HarmonyAfter(ModCompat.VneiGuid, "org.bepinex.plugins.jewelcrafting")]
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

    // VNEI coexistence until IB.5 (Codex 2026-11-04): while our tab exists VNEI's tab is hidden and its UI entry paths
    // (tab activation, Open UI / View Recipe hotkeys -> BaseUI.SetVisibility(true)) stay shut; its index keeps working for
    // the old-vs-new coverage comparison. Browser.MuteVnei = false restores VNEI for the old harness check.
    internal static class Vnei
    {
        private static bool Muted => Browser.MuteVnei && BrowserPanel.Instance;

        public static void Hide(InventoryGui gui)
        {
            var tab = gui.m_tabUpgrade ? gui.m_tabUpgrade.transform.parent.Find("VNEI") : null;
            if (tab && tab.gameObject.activeSelf == Muted) tab.gameObject.SetActive(!Muted);
        }

        // Patched lazily at the tab's creation (InventoryGui.Awake): VNEI's assembly may load after ours (FadePark too).
        private static bool _tried;

        public static void Patch()
        {
            if (_tried || FadePark.Harmony == null) return;
            _tried = true;
            var handler = Morgott.Core.TypeLookup.ByFullName("VNEI.UI.MainVneiHandler");
            var ui = Morgott.Core.TypeLookup.ByFullName(ModCompat.VneiBaseUi);
            if (handler == null || ui == null) return; // no VNEI
            try
            {
                FadePark.Harmony.Patch(AccessTools.Method(handler, "SetTabActive"), prefix: new HarmonyMethod(typeof(Vnei), nameof(SetTabActive_Prefix)));
                FadePark.Harmony.Patch(AccessTools.Method(ui, "SetVisibility", new[] { typeof(bool) }), prefix: new HarmonyMethod(typeof(Vnei), nameof(SetVisibility_Prefix)));
            }
            catch (System.Exception e) { Plugin.Log.LogWarning($"[Morgott.UI] VNEI mute failed (its tab may open beside ITEMS): {e.Message}"); }
        }

        // Old mode (MuteVnei off): VNEI's tab may open; ours closes first so the two never share the body.
        private static bool SetTabActive_Prefix()
        {
            if (Muted) return false;
            if (BrowserPanel.Up) BrowserPanel.Instance.Close();
            return true;
        }

        private static void SetVisibility_Prefix(ref bool visible)
        {
            if (Muted) visible = false;
        }
    }
}
