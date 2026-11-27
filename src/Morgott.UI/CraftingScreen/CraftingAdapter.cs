using HarmonyLib;
using Morgott.UI.Ownership;
using Morgott.UI.Compat;

namespace Morgott.UI.CraftingScreen
{
    // Family "Crafting" hooks (installed only while RuneSkin owns Crafting and Inventory). No transpilers, no skipped
    // originals: InventoryGui.Awake builds the skin after every other Awake postfix (CraftingFilter, Epic Loot,
    // RecipeChains add their objects there); InventoryGui.Update postfix after them; SetupRequirementList prefix
    // only grows the requirement cell array so vanilla's paging (InventoryGui.cs:1665) never pages.
    [HarmonyPatch]
    public static class CraftingAdapter
    {
        // Harmony asks before patching this class (Plugin: one PatchAll(Assembly), batched by FastStartup).
        private static bool Prepare() => OwnershipRegistry.IsOwned(OwnershipRegistry.Inventory) && OwnershipRegistry.IsOwned(OwnershipRegistry.Crafting);

        private const string ValheimPlus = ModCompat.ValheimPlusGuid;

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Awake))]
        [HarmonyPostfix]
        [HarmonyPriority(PatchOrder.Crafting)]
        private static void Awake_Postfix(InventoryGui __instance)
        {
            try { CraftingSkin.Attach(__instance); }
            catch (System.Exception e) { Plugin.Log.LogError($"[Morgott.UI] crafting skin build failed (vanilla look stays): {e}"); }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Update))]
        [HarmonyPostfix]
        [HarmonyPriority(PatchOrder.Crafting)]
        [HarmonyAfter(ValheimPlus)]
        private static void Update_Postfix()
        {
            var skin = CraftingSkin.Instance;
            if (!skin) return;
            try { skin.Frame(); }
            catch (System.Exception e) { Fault(e); }
        }

        // Every list rebuild (InventoryGui.UpdateRecipeList, :1158): new rows, or rows a pool kept (MorgottTweaks
        // [RecipeListReuse]) whose CanCraft may have changed - the same row count and first row do not mean the same rows.
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateRecipeList))]
        [HarmonyPostfix]
        private static void UpdateRecipeList_Postfix()
        {
            var skin = CraftingSkin.Instance;
            if (skin) skin.RowsRebuilt();
        }

        // An open sort dropdown list takes the gamepad (its items are EventSystem-selected): vanilla's DPad recipe walk
        // (InventoryGui.UpdateRecipeGamepadInput :1396-1409, crafting group active) must not move the selection under it.
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateRecipeGamepadInput))]
        [HarmonyPrefix]
        private static bool UpdateRecipeGamepadInput_Prefix() => !(CraftingSkin.Instance && CraftingSkin.Instance.SortListOpen);

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.SetupRequirementList))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void SetupRequirementList_Prefix(InventoryGui __instance)
        {
            var skin = CraftingSkin.Instance;
            var recipe = __instance.m_selectedRecipe.Recipe;
            if (!skin || !recipe || recipe.m_resources == null) return;
            try { skin.EnsureRequirementCells(recipe.m_resources.Length); }
            catch (System.Exception e) { Fault(e); }
        }

        // Which requirement each cell shows (InventoryGui.SetupRequirement, :1744; ValheimPlus replaces its body, postfixes
        // still run): the cell stock (CraftingSkin.SocketState) reads it.
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.SetupRequirement))]
        // After Morgott.Patcher's postfix (RecipeChains writes its producer lines into the cell tooltip): RequirementTip
        // puts the full item tooltip in front of them.
        [HarmonyPostfix]
        [HarmonyAfter(Morgott.Core.Contracts.PatcherGuid)]
        private static void SetupRequirement_Postfix(UnityEngine.Transform elementRoot, Piece.Requirement req, bool craft, int quality, int craftMultiplier, bool __result)
        {
            var skin = CraftingSkin.Instance;
            if (!skin || !craft || !elementRoot || req == null || !req.m_resItem) return;
            try
            {
                skin.Requirement(elementRoot, req, quality, craftMultiplier);
                if (__result) skin.RequirementTip(elementRoot, req);
            }
            catch (System.Exception e) { Fault(e); }
        }

        // Vanilla clears only m_text (InventoryGui.HideRequirement); the topic RequirementTip set goes with it.
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.HideRequirement))]
        [HarmonyPostfix]
        private static void HideRequirement_Postfix(UnityEngine.Transform elementRoot)
        {
            var skin = CraftingSkin.Instance;
            if (skin && elementRoot) skin.HideTip(elementRoot);
        }

        // Any inventory changed (the player's, or a chest CraftFromChest / the station network counts): chain verdicts stale.
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.Changed))]
        [HarmonyPostfix]
        private static void InventoryChanged()
        {
            var skin = CraftingSkin.Instance;
            if (skin) skin.StockChanged();
        }

        // Craft amount stepper: arms vanilla's multi-craft (m_multiCraftAmount + m_touchMultiCrafting) right before the two
        // vanilla readers, UpdateRecipe (InventoryGui.cs:1498, label :1572-1575, requirements :1556) and OnCraftPressed (:2059).
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateRecipe))]
        [HarmonyPrefix]
        private static void UpdateRecipe_Prefix() => Arm();

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnCraftPressed))]
        [HarmonyPrefix]
        private static void OnCraftPressed_Prefix(bool __runOriginal)
        {
            Arm();
            if (__runOriginal) Queue(s => s.PressPrefix());
        }

        // Craft queue (CraftingSkin.PressPrefix): N single vanilla crafts instead of one batch. A press another mod took
        // over (a prefix returned false, e.g. Morgott.Patcher RecipeChains crafting a chain) is not ours to queue.
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnCraftPressed))]
        [HarmonyPostfix]
        private static void OnCraftPressed_Postfix(bool __runOriginal)
        {
            if (__runOriginal) Queue(s => s.PressPostfix());
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.DoCrafting))]
        [HarmonyPrefix]
        private static void DoCrafting_Prefix(Player player)
        {
            var skin = CraftingSkin.Instance;
            if (!skin) return;
            try { skin.CraftPrefix(player); }
            catch (System.Exception e) { Fault(e); }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.DoCrafting))]
        [HarmonyPostfix]
        private static void DoCrafting_Postfix(Player player)
        {
            var skin = CraftingSkin.Instance;
            if (!skin) return;
            try { skin.CraftPostfix(player); }
            catch (System.Exception e) { Fault(e); }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateRecipe))]
        [HarmonyPostfix]
        private static void UpdateRecipe_Postfix() => Queue(s => s.QueueTick());

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
        [HarmonyPostfix]
        private static void Hide_Postfix() => Queue(s => s.QueueClosed());

        private static void Queue(System.Action<CraftingSkin> step)
        {
            var skin = CraftingSkin.Instance;
            if (!skin) return;
            try { step(skin); }
            catch (System.Exception e) { Fault(e); }
        }

        private static void Arm()
        {
            var skin = CraftingSkin.Instance;
            if (!skin) return;
            try { skin.Arm(); }
            catch (System.Exception e) { Fault(e); }
        }

        private static int _faults;

        private static void Fault(System.Exception e)
        {
            if (_faults++ < 5) Plugin.Log.LogError($"[Morgott.UI] crafting frame failed: {e}");
        }
    }
}
