using System;
using System.Collections.Generic;
using Morgott.UI.Api;
using HarmonyLib;
using Morgott.UI.Classify;
using Morgott.UI.Responsive;
using Morgott.UI.Restyle;
using Morgott.UI.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Morgott.UI.Ownership;
using Morgott.UI.Compat;

namespace Morgott.UI.Screens
{
    // Pause menu (Esc), family "Menu". The vanilla Menu keeps its objects, fields and logic (Menu.cs:120-403); RuneSkin
    // restyles it in place after Menu.Start (scene object, Start hides it and builds navigation, Menu.cs:120-134) and
    // re-applies on every Menu.Show (Menu.cs:221-246), which catches entries other mods add later (idempotent: marks
    // skip unchanged graphics).
    // Installed only when RuneSkin owns "Menu" (Plugin.PatchFamilies).
    [HarmonyPatch]
    public static class MenuAdapter
    {
        // Harmony asks before patching this class (Plugin: one PatchAll(Assembly), batched by FastStartup).
        private static bool Prepare() => OwnershipRegistry.IsOwned(OwnershipRegistry.Menu);

        public const string ScreenId = "Menu";

        [HarmonyPatch(typeof(Menu), nameof(Menu.Start))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Start_Postfix(Menu __instance) => Apply(__instance);

        [HarmonyPatch(typeof(Menu), nameof(Menu.Show))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Show_Postfix(Menu __instance)
        {
            Apply(__instance);
            FitPanel(__instance);
        }

        [HarmonyPatch(typeof(Menu), nameof(Menu.Hide))]
        [HarmonyPrefix]
        private static void Hide_Prefix(Menu __instance)
        {
            var es = EventSystem.current;
            if (!es || !__instance || !__instance.m_root) return;
            var sel = es.currentSelectedGameObject;
            if (sel && sel.transform.IsChildOf(__instance.m_root)) es.SetSelectedGameObject(null);
        }

        // Guard vanilla's 2-frame SelectEntry coroutine so a hide in the same frame cannot re-select a hidden button
        // (and steal focus from another screen) two frames later.
        [HarmonyPatch(typeof(Menu), nameof(Menu.SelectEntry))]
        [HarmonyPrefix]
        private static bool SelectEntry_Prefix(Menu __instance, GameObject entry, ref System.Collections.IEnumerator __result)
        {
            __result = SafeSelectEntry(__instance, entry);
            return false;
        }

        private static System.Collections.IEnumerator SafeSelectEntry(Menu menu, GameObject entry)
        {
            yield return null;
            yield return null;
            if (!menu || !menu.m_root || !menu.m_root.gameObject.activeSelf || !entry || !entry.activeInHierarchy) yield break;
            if (EventSystem.current) EventSystem.current.SetSelectedGameObject(entry);
            menu.UpdateCursor();
        }

        [HarmonyPatch(typeof(Menu), nameof(Menu.OnQuitNo))]
        [HarmonyPostfix]
        private static void OnQuitNo_Postfix(Menu __instance) => FitPanel(__instance);

        [HarmonyPatch(typeof(Menu), nameof(Menu.OnLogoutNo))]
        [HarmonyPostfix]
        private static void OnLogoutNo_Postfix(Menu __instance) => FitPanel(__instance);

        [HarmonyPatch(typeof(Menu), nameof(Menu.Update))]
        [HarmonyPrefix]
        private static void Update_Prefix(Menu __instance, out bool __state)
            => __state = __instance && __instance.m_root && __instance.m_root.gameObject.activeSelf && __instance.m_rebuildLayout;

        [HarmonyPatch(typeof(Menu), nameof(Menu.Update))]
        [HarmonyPostfix]
        private static void Update_Postfix(Menu __instance, bool __state)
        {
            if (__state && __instance && !__instance.m_rebuildLayout) FitPanel(__instance);
        }

        // Rebuild the entry column synchronously so the panel stack is sized on the very first rendered frame
        // (avoids the 2-frame tiny-box flash, especially when MemoryModule's pause GC freezes frame T+1).
        private static void FitPanel(Menu menu)
        {
            if (!menu || !menu.menuEntriesParent || !menu.menuEntriesParent.gameObject.activeInHierarchy) return;
            if (menu.lastSaveText && menu.lastSaveText.gameObject.activeSelf)
            {
                if (menu.m_lastSavedDate > DateTime.MinValue && Localization.instance != null)
                {
                    var minutes = (DateTime.Now - menu.m_lastSavedDate).Minutes;
                    menu.lastSaveText.text = Localization.instance.Localize("$menu_manualsavetime", minutes < 1 ? "<1" : minutes.ToString());
                }
                LayoutRebuilder.ForceRebuildLayoutImmediate(menu.lastSaveText.rectTransform);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(menu.menuEntriesParent);
            var panel = menu.menuEntriesParent.Find(PanelDecor.ChildName);
            if (panel && panel.TryGetComponent<PanelDecor>(out var decor)) decor.FitToChildren("size.panel.pad");
        }

        private static Transform _styledRoot;
        private static int _styledRev = -1;
        private static readonly List<Button> ButtonBuf = new List<Button>(16);

        private sealed class MenuContextProvider
        {
            private readonly Menu _menu;
            public MenuContextProvider(Menu menu) => _menu = menu;
            public ScreenContext Build() => Context(_menu);
        }

        private static void Apply(Menu menu)
        {
            if (!menu) return;
            var root = menu.transform;
            var rev = ThemeTokens.Revision;
            if (_styledRoot == root && _styledRev == rev)
            {
                ApplyWarm(menu, root, rev);
                return;
            }
            if (menu.menuEntriesParent)
            {
                // Entry states via their owning components (MenuEntryStyle), then the panel stack behind the entries.
                foreach (var button in menu.menuEntriesParent.GetComponentsInChildren<Button>(true))
                    MenuEntryStyle.Ensure(button, button.GetComponentInChildren<TMP_Text>(true));
                // No header plaque: the pause menu has no title (menuEntriesParent holds only the entry buttons,
                // Menu.cs:58), and an empty plaque reads as a missing label.
                PanelDecor.Ensure(menu.menuEntriesParent, "size.panel.pad", glass: true, plaque: false);
            }
            // Confirm dialogs: one framed box each, sized to content, on the modal band (DialogFrame).
            DialogFrame.Apply(menu.m_quitDialog);
            DialogFrame.Apply(menu.m_logoutDialog);
            DialogFrame.Apply(menu.m_cloudStorageWarning ? menu.m_cloudStorageWarning.transform : null);
            DialogFrame.Apply(menu.m_cloudStorageWarningNextSave ? menu.m_cloudStorageWarningNextSave.transform : null);
            ScreenRegistry.Register(root, new MenuContextProvider(menu).Build);
            ScreenRegistry.Restyle(root);
            // SkinScale on the dialogs' content roots, never on the canvas (GuiScaler owns scaleFactor).
            ResponsiveContext.ApplyScale(menu.m_menuDialog);
            ResponsiveContext.ApplyScale(menu.m_quitDialog);
            ResponsiveContext.ApplyScale(menu.m_logoutDialog);
            _styledRoot = root;
            _styledRev = rev;
        }

        // Warm Esc open: only ensure + restyle newly added / unmarked entry or root mod buttons.
        private static void ApplyWarm(Menu menu, Transform root, int rev)
        {
            ScreenContext ctx = null;
            if (menu.menuEntriesParent)
            {
                ButtonBuf.Clear();
                menu.menuEntriesParent.GetComponentsInChildren(true, ButtonBuf);
                for (var i = 0; i < ButtonBuf.Count; i++)
                {
                    var button = ButtonBuf[i];
                    if (!button) continue;
                    if (!button.TryGetComponent<MenuEntryStyle>(out _) && button.transition == Selectable.Transition.Animation)
                        MenuEntryStyle.Ensure(button, button.GetComponentInChildren<TMP_Text>(true));
                    var g = button.targetGraphic ? button.targetGraphic : button.GetComponentInChildren<TMP_Text>(true);
                    if (g && (!g.TryGetComponent<SkinMark>(out var m) || !m.IsOwnMark || m.ThemeRevision != rev))
                    {
                        if (ctx == null) ctx = Context(menu);
                        Restyler.RestyleSubtree(button.transform, root, ctx);
                    }
                }
                ButtonBuf.Clear();
            }
            if (menu.m_root)
            {
                for (var i = 0; i < menu.m_root.childCount; i++)
                {
                    var child = menu.m_root.GetChild(i);
                    if (!child.TryGetComponent<Button>(out var b)) continue;
                    var g = b.targetGraphic;
                    if (g && (!g.TryGetComponent<SkinMark>(out var m) || !m.IsOwnMark || m.ThemeRevision != rev))
                    {
                        if (ctx == null) ctx = Context(menu);
                        Restyler.RestyleSubtree(child, root, ctx);
                    }
                }
            }
        }

        // Classifier step (b): what the vanilla fields tell about the menu.
        private static ScreenContext Context(Menu menu)
        {
            var ctx = new ScreenContext(ScreenId, Ownership.OwnershipRegistry.Menu);
            // Controller glyph map (Menu.m_gamepadRoot, shown by HandleInputLayoutChanged Menu.cs:136-146): content art.
            ctx.Exclude(menu.m_gamepadRoot ? menu.m_gamepadRoot.transform : null, "gamepad map (Menu.m_gamepadRoot, Menu.cs:78/141-145): controller art");
            // Settings / player list / feedback are their own screens, instantiated under the menu on demand
            // (Menu.OnSettings :414, OnCurrentPlayers :428, OnButtonFeedback :582); not owned in M0.
            if (menu.m_settingsInstance) ctx.Exclude(menu.m_settingsInstance.transform, "Settings screen (Menu.OnSettings, Menu.cs:414), own family");
            if (menu.m_currentPlayersInstance) ctx.Exclude(menu.m_currentPlayersInstance.transform, "player list (Menu.OnCurrentPlayers, Menu.cs:428), own family");
            foreach (var feedback in menu.GetComponentsInChildren<Feedback>(true))
                if (feedback.transform != menu.transform) ctx.Exclude(feedback.transform, "Feedback dialog (Menu.OnButtonFeedback, Menu.cs:582), own family");

            // Entries: text-only buttons under menuEntriesParent (Menu.cs:58, laid out by LayoutRebuilder :375).
            if (menu.menuEntriesParent)
            {
                foreach (var button in menu.menuEntriesParent.GetComponentsInChildren<Button>(true))
                {
                    if (button.targetGraphic is TMP_Text label)
                        ctx.Role(label, "menu.entry", "Menu.menuEntriesParent entry label (Button.targetGraphic)");
                    foreach (var text in button.GetComponentsInChildren<TMP_Text>(true))
                        if (text != button.targetGraphic)
                            ctx.Role(text, "menu.entry", "Menu.menuEntriesParent entry label");
                }
            }
            // Legacy uGUI labels of buttons other mods put on the menu root: Epic Loot's QuickConfigBroker
            // (Common.QuickConfigBroker.EnsurePauseButton, EpicLoot.dll decompile 2026-10-24) adds "ModQuickConfigButton"
            // to Menu.m_root through Jotunn GUIManager.CreateButton - its Image is the Button.targetGraphic (button role),
            // its label a UnityEngine.UI.Text the classifier has no rule for ("unknown Menu: ... Text not handled", 0.21.1
            // menufix run 20261024-194221). The plate's label face, like every RuneSkin button label.
            foreach (var button in menu.GetComponentsInChildren<Button>(true))
                foreach (var label in button.GetComponentsInChildren<Text>(true))
                    if (label.GetComponentInParent<Button>(true) == button)
                        ctx.Role(label, "button.label", "legacy Text label of a mod button on the menu (Jotunn CreateButton)");
            // Vanilla panel images under a dialog's panel stack (DialogFrame): off, reported skipped.
            foreach (var img in menu.GetComponentsInChildren<Image>(true))
                if (img.TryGetComponent<PreWorld.PanelSwap>(out _))
                    ctx.Role(img, "skip", "vanilla dialog panel image (RuneSkin panel stack replaces it, off; DialogFrame)");
            // "Last saved" line (Menu.lastSaveText, Menu.cs:54, written in Update :366): body text.
            ctx.Role(menu.lastSaveText, "text.body", "Menu.lastSaveText");
            return ctx;
        }
    }
}
