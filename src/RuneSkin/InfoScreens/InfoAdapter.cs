using HarmonyLib;
using RuneSkin.Ownership;
using RuneSkin.Compat;

namespace RuneSkin.InfoScreens
{
    // Families "Skills" / "Trophies" hooks (installed only while owned, with Inventory): build after every other
    // InventoryGui.Awake postfix; re-lay out after vanilla filled the lists (SkillsDialog.Setup, UpdateTrophyList).
    [HarmonyPatch]
    public static class SkillsAdapter
    {
        // Harmony asks before patching this class (Plugin: one PatchAll(Assembly), batched by FastStartup).
        private static bool Prepare() => OwnershipRegistry.IsOwned(OwnershipRegistry.Inventory) && OwnershipRegistry.IsOwned(OwnershipRegistry.Skills);

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Awake))]
        [HarmonyPostfix]
        [HarmonyPriority(PatchOrder.Skills)]
        private static void Awake_Postfix(InventoryGui __instance)
        {
            try { SkillsSkin.Attach(__instance); }
            catch (System.Exception e) { Plugin.Log.LogError($"[RuneSkin] skills panel build failed (vanilla look stays): {e}"); }
        }

        [HarmonyPatch(typeof(SkillsDialog), nameof(SkillsDialog.Setup))]
        [HarmonyPostfix]
        [HarmonyPriority(PatchOrder.Trophies)]
        private static void Setup_Postfix(Player player)
        {
            try { SkillsSkin.Instance?.AfterSetup(player); }
            catch (System.Exception e) { Plugin.Log.LogError($"[RuneSkin] skills layout failed: {e}"); }
        }
    }

    // One docked info panel at a time (InfoTabs closes its own panel before another opens). Achievements keeps the
    // inventory header row (no mirror, InfoMath.HeaderTabsShown), so a header click opened Skills / Trophies / Texts on
    // top of it; vanilla's full-screen darken covers that row. The open achievements panel closes first (vanilla
    // OnCloseAchievements, InventoryGui.cs:2391-2395).
    [HarmonyPatch]
    public static class InfoSwitchAdapter
    {
        private static bool Prepare() => OwnershipRegistry.IsOwned(OwnershipRegistry.Inventory);

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnOpenSkills))]
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnOpenTexts))]
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnOpenTrophies))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void OpenOther_Prefix(InventoryGui __instance)
        {
            if (__instance.m_achievementsPanel && __instance.m_achievementsPanel.gameObject.activeSelf) __instance.OnCloseAchievements();
        }
    }

    [HarmonyPatch]
    public static class TrophiesAdapter
    {
        // Harmony asks before patching this class (Plugin: one PatchAll(Assembly), batched by FastStartup).
        private static bool Prepare() => OwnershipRegistry.IsOwned(OwnershipRegistry.Inventory) && OwnershipRegistry.IsOwned(OwnershipRegistry.Trophies);

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Awake))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Awake_Postfix(InventoryGui __instance)
        {
            try { TrophiesSkin.Attach(__instance); }
            catch (System.Exception e) { Plugin.Log.LogError($"[RuneSkin] trophies panel build failed (vanilla look stays): {e}"); }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateTrophyList))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void UpdateTrophyList_Postfix()
        {
            try { TrophiesSkin.Instance?.AfterUpdateList(); }
            catch (System.Exception e) { Plugin.Log.LogError($"[RuneSkin] trophies layout failed: {e}"); }
        }
    }
}
