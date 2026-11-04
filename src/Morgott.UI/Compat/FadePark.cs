using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Morgott.UI.Compat
{
    // HIDDEN subtrees kept outside the inventory's fade group. Every CanvasGroup alpha write of the open / close fade
    // (InventorySkin.Fade, CanvasGroup on the InventoryGui object) broadcasts OnCanvasGroupChanged to every component
    // under InventoryGui, inactive ones included: rp-open-hitch run 20261027-220134 measured 0.421 ms per write for 2585
    // objects, EAQS's unused-cell pool (Player/EaqsHiddenSlotRoot) 169 of them. Unity 6000.0.75 batchmode probe (874-object
    // inactive subtree under a faded group of ~800 active objects): 0.38 ms per write inside, 0.16 ms parked in an
    // inactive holder outside the group. Hidden, they draw nothing and receive nothing, so they wait in one inactive
    // holder beside InventoryGui (same canvas, outside the group) and go back before they are shown.
    // - EAQS: EnsureHiddenRoot creates its pool once (inactive, kept in a static field, recreated only when destroyed) and
    //   ParkHiddenCell / UpdateInventorySlots move cells in and out of that object wherever it is: the pool itself parks.
    public static class FadePark
    {
        internal static Harmony Harmony;
        private const string HolderName = "RuneSkinFadePark";

        // InventorySkin.Park (the screen faded out): EAQS's pool of unused cells, once per pool (it stays parked; EAQS
        // only moves cells in and out of it).
        public static void ParkEaqsPool(InventoryGui gui)
        {
            if (!gui || !gui.m_player) return;
            var pool = gui.m_player.Find(ModCompat.EaqsHiddenRoot);
            if (!pool || FadeParkMath.Decide(pool.gameObject.activeSelf, true, true) != FadeParkMove.Park) return;
            var holder = Holder(gui);
            if (holder) pool.SetParent(holder, false);
        }

        // ---- vanilla dialogs -----------------------------------------------------------------------------------------
        // InventoryGui's own on-demand dialogs (root/Achievements, SplitDialog, Skills, Trophies, Texts: ~270 objects,
        // inactive until opened; rp-open-hitch run 20261027-234026: fade group 1514 objects, one alpha write
        // 0.327 ms) wait in the same holder while hidden. Vanilla keeps them by field (InventoryGui.m_*), never by path, and
        // shows each through one call: InventoryGui.ShowSplitDialog -> SplitDialog.SetActive(true) (InventoryGui.cs:2264),
        // SkillsDialog.Setup (SkillsDialog.cs:114), TextsDialog.Setup (TextsDialog.cs:65), InventoryGui.OnOpenTrophies
        // (:2352), OnOpenAchievements (:2370). A prefix on each moves the
        // dialog home (parent + sibling index: draw order) before it activates; InventorySkin also homes any parked dialog
        // that someone else activated (Sweep, every open frame). Parked again with the screen (InventorySkin.Park).
        // VariantDialog (26 objects) stays: Epic Loot clones m_variantDialog for its augment / craft-result dialogs
        // (ElDialogs), and a clone placed beside a parked source would land in the inactive holder.
        private static bool _vanillaTried;
        // Parked dialog -> home parent + sibling index. Keyed by the dialog itself, not a component on it: a clone of a
        // dialog (Instantiate copies components) must never inherit a home and be moved into the vanilla root.
        private static readonly Dictionary<Transform, (Transform Parent, int Index)> Homes = new Dictionary<Transform, (Transform, int)>();

        private static InventoryGui _dialogsOf;
        private static Transform[] _dialogs;

        // Cached per InventoryGui (Sweep runs every open frame: no allocation).
        private static Transform[] VanillaDialogs(InventoryGui gui)
        {
            if (_dialogsOf != gui || _dialogs == null)
            {
                _dialogsOf = gui;
                _dialogs = Collect(gui);
            }
            return _dialogs;
        }

        private static Transform[] Collect(InventoryGui gui) => new[]
        {
            gui.m_splitDialog ? gui.m_splitDialog.transform : null, gui.m_skillsDialog ? gui.m_skillsDialog.transform : null,
            gui.m_textsDialog ? gui.m_textsDialog.transform : null, gui.m_trophiesPanel ? gui.m_trophiesPanel.transform : null,
            gui.m_achievementsPanel ? gui.m_achievementsPanel.transform : null,
        };

        // InventorySkin.Park (the screen faded out): every hidden vanilla dialog out of the fade group.
        public static void ParkVanilla(InventoryGui gui)
        {
            if (!gui) return;
            EnsureVanillaPatched();
            foreach (var t in VanillaDialogs(gui))
            {
                if (!t) continue;
                var parent = Homes.TryGetValue(t, out var h) && h.Parent ? h.Parent : t.parent;
                if (!parent || FadeParkMath.Decide(t.gameObject.activeSelf, t.parent == parent, parent.IsChildOf(gui.transform)) != FadeParkMove.Park) continue;
                var holder = Holder(gui);
                if (!holder) return;
                if (Homes.Count > 32) Homes.Clear(); // a previous GUI's dead keys (logout); live ones are re-added here
                Homes[t] = (parent, t.GetSiblingIndex());
                t.SetParent(holder, false);
            }
        }

        // Inventory open: a parked dialog activated by a path without a prefix (another mod's SetActive) goes home.
        public static void SweepVanilla(InventoryGui gui)
        {
            if (!gui) return;
            foreach (var t in VanillaDialogs(gui))
                if (t && t.gameObject.activeSelf) Home(t);
        }

        private static void Home(Transform t)
        {
            if (!t || !Homes.TryGetValue(t, out var home) || !home.Parent || t.parent == home.Parent) return;
            t.SetParent(home.Parent, false);
            if (home.Index >= 0 && home.Index < home.Parent.childCount) t.SetSiblingIndex(home.Index);
        }

        private static void EnsureVanillaPatched()
        {
            if (_vanillaTried || Harmony == null) return;
            _vanillaTried = true;
            var prefix = new HarmonyMethod(typeof(FadePark), nameof(DialogShow_Prefix)) { priority = Priority.First };
            var guiPrefix = new HarmonyMethod(typeof(FadePark), nameof(GuiShow_Prefix)) { priority = Priority.First };
            var targets = new (MethodBase m, HarmonyMethod p)[]
            {
                (AccessTools.Method(typeof(SplitDialog), nameof(SplitDialog.SetActive), new[] { typeof(bool) }), prefix),
                (AccessTools.Method(typeof(SkillsDialog), nameof(SkillsDialog.Setup)), prefix),
                (AccessTools.Method(typeof(TextsDialog), nameof(TextsDialog.Setup)), prefix),

                (AccessTools.Method(typeof(InventoryGui), nameof(InventoryGui.OnOpenTrophies)), guiPrefix),
                (AccessTools.Method(typeof(InventoryGui), nameof(InventoryGui.OnOpenAchievements)), guiPrefix),
            };
            foreach (var (m, p) in targets)
            {
                if (m == null) continue;
                try { Harmony.Patch(m, prefix: p); }
                catch (Exception e) { Plugin.Log.LogWarning($"[Morgott.UI] fade park: {m.DeclaringType?.Name}.{m.Name} patch failed (homed one frame late): {e.Message}"); }
            }
        }

        // SplitDialog.SetActive(false) passes too: homing a hidden dialog is harmless (parked again with the screen).
        private static void DialogShow_Prefix(Component __instance)
        {
            try { Home(__instance ? __instance.transform : null); }
            catch (Exception e) { Plugin.Log.LogWarning($"[Morgott.UI] fade park home: {e.Message}"); }
        }

        private static void GuiShow_Prefix(InventoryGui __instance, MethodBase __originalMethod)
        {
            try
            {
                if (!__instance) return;
                Home(__originalMethod.Name == nameof(InventoryGui.OnOpenTrophies)
                    ? (__instance.m_trophiesPanel ? __instance.m_trophiesPanel.transform : null)
                    : (__instance.m_achievementsPanel ? __instance.m_achievementsPanel.transform : null));
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[Morgott.UI] fade park home: {e.Message}"); }
        }

        // One inactive holder per InventoryGui, its sibling: same canvas and lifetime (destroyed with the GUI on logout),
        // outside the fade group.
        private static Transform Holder(InventoryGui gui)
        {
            var parent = gui ? gui.transform.parent : null;
            if (!parent) return null;
            var found = parent.Find(HolderName);
            if (found) return found;
            var go = new GameObject(HolderName, typeof(RectTransform));
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            return go.transform;
        }
    }
}
