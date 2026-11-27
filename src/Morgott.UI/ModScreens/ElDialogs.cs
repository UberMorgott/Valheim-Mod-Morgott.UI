using Morgott.UI.Api;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using Morgott.UI.Classify;
using Morgott.UI.Ownership;
using Morgott.UI.Restyle;
using Morgott.UI.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Morgott.UI.ModScreens
{
    // Epic Loot's modal dialogs built from vanilla parts (no reference; EpicLoot 0.15.0 decompile):
    // - AugmentChoiceDialog (AugmentHelper.CreateAugmentChoiceDialog, AugmentHelper.cs:65-98; EnchantingUIController
    //   .AugmentItem reparents it under the enchanting window, EnchantingUIController.cs:1551-1568) and CraftSuccessDialog
    //   (CraftSuccessDialog.Create, CraftSuccessDialog.cs:273-298; under the enchanting window or MerchantPanel) are
    //   Instantiate(InventoryGui.m_variantDialog) + Instantiate(m_recipeIcon / m_recipeName / m_recipeDecription) into
    //   its frame. Instantiate copies the source's live state, so the dialog depended on what RuneSkin had done to the
    //   sources by then:
    //   1. Geometry: Epic Loot lays the dialog out against the vanilla prefab geometry of m_recipeIcon / m_recipeName
    //      (icon top-left, name beside it, description pushed below: AugmentHelper.cs:73-84). CraftingSkin.Detail re-places
    //      both into the crafting detail column every layout (icon centred, IconH tall, CraftingMath o.Icon), so a clone
    //      taken after any crafting layout put the icon over the description (user 2026-10-27, 0.25.11); one taken before
    //      the first crafting layout was right. The clones get the vanilla geometry back (snapshot in InventoryGui.Awake
    //      prefix, before any RuneSkin postfix places them) - the geometry Epic Loot's own offsets assume.
    //   2. RootMark: VanillaExtras marks m_variantDialog as its own root; the clone carried that mark without being a
    //      registered root, so the owning unit's pass (ForeignUi: EpicLootEnchanting / EpicLootBoard) skipped the whole
    //      dialog as a "nested root" (Restyler.ExcludedBy) and the Jotunn choice buttons (GUIManager.CreateButton,
    //      AugmentHelper.cs:88) kept Jotunn's brown plates and Epic Loot's font, truncated at 300 wide. The stale mark
    //      leaves; the dialog is part of its window's unit and gets the variant dialog's roles there (ForeignRoles.Precise).
    //   3. The choice stack (after the unit pass: the panel stack's corner caps exist then) moves above the bottom corner
    //      caps and takes the frame's inner width between its drawn side borders (ElDialogMath.InnerWidth, never under
    //      Epic Loot's 300; run 20261027-165655 truncated "(сохранить) Урон по оглушенному врагу +7,5% [3,5-10,5]" at
    //      300, run 20261027-170958 sized them to the description view, which measured narrower than 300); each label
    //      fills its button, wraps, auto-sizes to the readable floor and grows its button past two lines
    //      (ElDialogMath.Stack / ButtonHeight); the description view gives up that height (it scrolls).
    //   4. Width (user 2026-10-27 "надо их пошире сделать, но с моим интерфейсом, чтобы всё влезло"): Epic Loot's 380-unit
    //      frame is the real limit (245 px buttons at 720p), so the frame itself widens until the widest choice label
    //      fits one line at the dialog's body size (its description's 18; fixed, no auto-size) plus the drawn side borders (craft result: the tooltip's widest line), at most
    //      ElDialogMath.MaxFrameWidth and the safe area minus a margin, centred on it (ElDialogMath.FrameWidth /
    //      CentreOn); a label still wider wraps. The side borders come from the cloned panel stack's Frame layer, bound
    //      by name on the clone (PanelDecor.Frame: the clone's private reference was null -> "side insets 10/10").
    public static class ElDialogs
    {
        public const string AugmentDialog = "EpicLoot.Crafting.AugmentChoiceDialog";
        public const string SuccessDialog = "EpicLoot.Crafting.CraftSuccessDialog";
        private const string AugmentHelper = "EpicLoot.Crafting.AugmentHelper";

        internal static Harmony Harmony;
        private static bool _patched;

        private struct Geo
        {
            public bool Set;
            public Vector2 AnchorMin, AnchorMax, Pivot, Position, Size;
            public Vector3 Scale;
            public Quaternion Rotation;

            public static Geo Of(RectTransform rt) => rt
                ? new Geo { Set = true, AnchorMin = rt.anchorMin, AnchorMax = rt.anchorMax, Pivot = rt.pivot, Position = rt.anchoredPosition, Size = rt.sizeDelta, Scale = rt.localScale, Rotation = rt.localRotation }
                : default;

            public void To(RectTransform rt)
            {
                if (!Set || !rt) return;
                rt.anchorMin = AnchorMin;
                rt.anchorMax = AnchorMax;
                rt.pivot = Pivot;
                rt.sizeDelta = Size;
                rt.anchoredPosition = Position;
                rt.localScale = Scale;
                rt.localRotation = Rotation;
            }
        }

        private static Geo _icon, _name, _scroll;

        // InventoryGui.Awake prefix: the serialized prefab geometry, before any postfix (CraftingSkin) moves them.
        internal static void Snapshot(InventoryGui gui)
        {
            if (!gui) return;
            if (gui.m_recipeIcon) _icon = Geo.Of(gui.m_recipeIcon.rectTransform);
            if (gui.m_recipeName) _name = Geo.Of(gui.m_recipeName.rectTransform);
            if (gui.m_recipeListScroll) _scroll = Geo.Of((RectTransform)gui.m_recipeListScroll.transform);
        }

        // Patched once from InventoryGui.Awake (Epic Loot is loaded by then).
        internal static void EnsurePatched()
        {
            if (_patched || Harmony == null) return;
            _patched = true;
            var createPost = new HarmonyMethod(typeof(ElDialogs), nameof(Created_Postfix));
            var showPost = new HarmonyMethod(typeof(ElDialogs), nameof(Show_Postfix));
            // No Epic Loot (or one without these builders): nothing clones the variant dialog.
            var helper = Morgott.Core.TypeLookup.ByFullName(AugmentHelper);
            if (helper != null) Patch(helper, "CreateAugmentChoiceDialog", createPost, requireReturn: true);
            var augment = Morgott.Core.TypeLookup.ByFullName(AugmentDialog);
            if (augment != null) Patch(augment, "Show", showPost, requireReturn: false);
            var success = Morgott.Core.TypeLookup.ByFullName(SuccessDialog);
            if (success != null)
            {
                Patch(success, "Create", createPost, requireReturn: true);
                Patch(success, "Show", showPost, requireReturn: false);
            }
        }

        private static void Patch(Type type, string method, HarmonyMethod post, bool requireReturn)
        {
            var m = AccessTools.DeclaredMethod(type, method);
            if (m == null || (requireReturn && m.ReturnType == typeof(void)))
            {
                Plugin.Log.LogWarning($"[Morgott.UI] {type.FullName}.{method} not found: Epic Loot dialogs keep the crafting geometry of their cloned parts");
                return;
            }
            try { Harmony.Patch(m, postfix: post); }
            catch (Exception e) { Plugin.Log.LogWarning($"[Morgott.UI] {type.FullName}.{method} patch failed: {e.Message}"); }
        }

        private static void Created_Postfix(object __result)
        {
            try { Fix(__result as Component); }
            catch (Exception e) { Plugin.Log.LogError($"[Morgott.UI] Epic Loot dialog fix failed (its cloned parts keep the crafting layout): {e}"); }
        }

        private static void Show_Postfix(object __instance)
        {
            try { OnShow(__instance as Component); }
            catch (Exception e) { Plugin.Log.LogError($"[Morgott.UI] Epic Loot dialog show restyle failed: {e}"); }
        }

        private static void Fix(Component dialog)
        {
            if (!dialog) return;
            var type = dialog.GetType();
            if (ElDialogMath.IsStaleRootMark(dialog.TryGetComponent<RootMark>(out var mark), ScreenRegistry.IsRoot(dialog.transform)))
                UnityEngine.Object.DestroyImmediate(mark);
            if (Field(type, "Icon")?.GetValue(dialog) is Image icon) _icon.To(icon.rectTransform);
            if (Field(type, "MagicBG")?.GetValue(dialog) is Image bg) _icon.To(bg.rectTransform);
            if (Field(type, "NameText")?.GetValue(dialog) is TMP_Text name) _name.To(name.rectTransform);
            var frame = FrameOf(dialog);
            if (frame && Column(frame) is RectTransform scroll && scroll.TryGetComponent<ScrollRect>(out var sr) &&
                sr.verticalScrollbar && sr.verticalScrollbar.transform is RectTransform br)
            {
                _scroll.To(br);
                PinScrollbar(scroll, br);
            }
            ResetScroll(dialog);
            Modal(dialog.transform);
        }

        // Synchronous restyle + layout in the exact Show frame so async MP RPC opens and late-frame Show calls never
        // flash raw 380px Jotunn UI. No-op when the dialog's ForeignUi unit is opted out (RestyleNow returns null).
        private static void OnShow(Component dialog)
        {
            if (!dialog) return;
            if (ElDialogMath.IsStaleRootMark(dialog.TryGetComponent<RootMark>(out var mark), ScreenRegistry.IsRoot(dialog.transform)))
                UnityEngine.Object.DestroyImmediate(mark);
            var family = ForeignUi.RestyleNow(dialog.transform);
            if (!ElDialogMath.ShouldStyleOnShow(family)) return;
            Modal(dialog.transform);
            if (ElDialogMath.NeedsShowApply(family))
                Apply(dialog);
            ResetScroll(dialog);
        }

        private static RectTransform FrameOf(Component dialog)
        {
            if (!dialog) return null;
            return Field(dialog.GetType(), "Frame")?.GetValue(dialog) as RectTransform
                ?? dialog.transform.Find("Frame") as RectTransform;
        }

        private static void ResetScroll(Component dialog)
        {
            if (!dialog) return;
            var t = dialog.GetType();
            if (Field(t, "Description")?.GetValue(dialog) is TMP_Text desc && desc)
            {
                var rt = desc.rectTransform;
                if (Mathf.Abs(rt.anchoredPosition.y) > 0.01f)
                    rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, 0f);
            }
            var frame = FrameOf(dialog);
            if (frame && Column(frame) is RectTransform scroll && scroll.TryGetComponent<ScrollRect>(out var sr))
            {
                if (sr.verticalScrollbar && sr.verticalScrollbar.transform is RectTransform br) PinScrollbar(scroll, br);
                sr.verticalNormalizedPosition = 1f;
            }
        }

        // The clone keeps the variant dialog's UIGroupHandler at the template's priority. A full-screen page lifts its own
        // groups above every group (InfoPage.Focus, the enchanting page included), so the dialog over it was no longer the
        // highest group and UIGroupHandler.Update set its CanvasGroup interactable = false: choices drawn, clicks dead (user
        // 2026-11-08, augment paid, no choice possible). Modal over its window as vanilla's variant dialog is over the
        // inventory: its groups go above every other group.
        private static void Modal(Transform dialog)
        {
            var own = dialog.GetComponentsInChildren<UIGroupHandler>(true);
            if (own.Length == 0) return;
            int top = int.MinValue, min = int.MaxValue;
            foreach (var g in UnityEngine.Object.FindObjectsByType<UIGroupHandler>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (g && !g.transform.IsChildOf(dialog)) top = Mathf.Max(top, g.m_groupPriority);
            foreach (var g in own) min = Mathf.Min(min, g.m_groupPriority);
            if (top == int.MinValue || min > top) return;
            foreach (var g in own) g.m_groupPriority += top + 1 - min;
        }

        private static readonly Dictionary<Type, Dictionary<string, FieldInfo>> Fields = new Dictionary<Type, Dictionary<string, FieldInfo>>();

        private static FieldInfo Field(Type t, string name)
        {
            if (!Fields.TryGetValue(t, out var map)) Fields[t] = map = new Dictionary<string, FieldInfo>();
            if (!map.TryGetValue(name, out var f)) map[name] = f = AccessTools.Field(t, name);
            return f;
        }

        private static bool IsDialog(Type t) => t != null && (t.FullName == AugmentDialog || t.FullName == SuccessDialog);

        private static readonly List<MonoBehaviour> Behaviours = new List<MonoBehaviour>();

        // `t` is an Epic Loot dialog or lies inside one (its dialog component sits on the clone's root).
        public static bool Inside(Transform t)
        {
            for (var p = t; p; p = p.parent)
            {
                Behaviours.Clear();
                p.GetComponents(Behaviours);
                foreach (var b in Behaviours)
                    if (b && IsDialog(b.GetType()))
                    {
                        Behaviours.Clear();
                        return true;
                    }
            }
            Behaviours.Clear();
            return false;
        }

        // Roles inside an Epic Loot window (ForeignRoles.Precise): what the variant dialog's own form gave them
        // (VanillaExtras: icons kept, popup backdrops), plus the item icon and its magic background as content.
        public static void Roles(ScreenContext ctx, Transform root)
        {
            Behaviours.Clear();
            root.GetComponentsInChildren(true, Behaviours);
            for (var i = 0; i < Behaviours.Count; i++)
            {
                var d = Behaviours[i];
                if (!d || !IsDialog(d.GetType())) continue;
                VanillaExtras.VariantRoles(ctx, d.transform);
                var t = d.GetType();
                if (Field(t, "Icon")?.GetValue(d) is Image icon && icon) ctx.Role(icon, "content", "Epic Loot dialog item icon");
                if (Field(t, "MagicBG")?.GetValue(d) is Image bg && bg) ctx.Role(bg, "content", "Epic Loot dialog magic item background");
            }
            Behaviours.Clear();
        }

        // After the unit pass: augment choice buttons across the frame's inner width (labels refit on the size change).
        public static void After(Transform unit)
        {
            Behaviours.Clear();
            unit.GetComponentsInChildren(true, Behaviours);
            for (var i = 0; i < Behaviours.Count; i++)
            {
                var d = Behaviours[i];
                if (!d || !IsDialog(d.GetType())) continue;
                Apply(d);
            }
            Behaviours.Clear();
        }

        private static void Apply(Component d)
        {
            var t = d.GetType();
            if (t.FullName == AugmentDialog && Field(t, "EffectChoiceButtons")?.GetValue(d) is List<Button> buttons)
                Choices(d.transform, buttons);
            else if (t.FullName == SuccessDialog && Field(t, "Frame")?.GetValue(d) is RectTransform frame && Field(t, "Description")?.GetValue(d) is TMP_Text desc)
                Success(d.transform, frame, desc);
        }

        // Craft result: the frame widens so the item tooltip's widest line shows on one line (Epic Loot's description
        // stretches across the frame, CraftSuccessDialog.ConvertToScrollingDescription), within the screen.
        private static void Success(Transform dialog, RectTransform frame, TMP_Text desc)
        {
            if (!frame || !desc || string.IsNullOrEmpty(desc.text)) return;
            var widest = 0f;
            foreach (var line in desc.text.Split('\n'))
                if (line.Length > 0) widest = Mathf.Max(widest, desc.GetPreferredValues(line).x);
            if (widest <= 0f) return;
            FitColumn(frame, desc);
            var fr = frame.rect;
            var d = RectIn(desc.rectTransform, frame);
            var left = Mathf.Max(0f, d.xMin - fr.xMin) + desc.margin.x;
            var right = Mathf.Max(0f, fr.xMax - d.xMax) + desc.margin.z;
            var w = Widen(frame, dialog, widest, left, right);
            FitColumn(frame, desc);
            LayoutRebuilder.ForceRebuildLayoutImmediate(desc.rectTransform);
            if (Logged.Add(dialog.GetInstanceID()))
                Plugin.Log.LogInfo($"[Morgott.UI] el dialog {dialog.name}: widest description line {widest.ToString("0.#", CultureInfo.InvariantCulture)}, insets {left.ToString("0.#", CultureInfo.InvariantCulture)}/{right.ToString("0.#", CultureInfo.InvariantCulture)} -> frame {w.ToString("0.#", CultureInfo.InvariantCulture)} (scale {frame.lossyScale.x.ToString("0.###", CultureInfo.InvariantCulture)})");
        }

        // Right-pin the cloned description scrollbar cleanly inside ScrollView (CraftingSkin relocates m_recipeListScroll
        // via Geo.Place, so Epic Loot's Instantiate of it landed inward over right-aligned description text).
        private static void PinScrollbar(RectTransform scroll, RectTransform bar)
        {
            if (!scroll || !bar || bar.parent != scroll) return;
            var pad = ElDialogMath.ScrollbarPad;
            var w = ElDialogMath.ScrollbarWidth(ThemeTokens.GetFloat("size.craft.scrollbar", ElDialogMath.DefaultScrollbarWidth), scroll.rect.width);
            var aMin = new Vector2(1f, 0f);
            var aMax = Vector2.one;
            var piv = new Vector2(1f, 0.5f);
            var oMin = new Vector2(-pad - w, pad);
            var oMax = new Vector2(-pad, -pad);
            if (bar.anchorMin != aMin) bar.anchorMin = aMin;
            if (bar.anchorMax != aMax) bar.anchorMax = aMax;
            if (bar.pivot != piv) bar.pivot = piv;
            if ((bar.offsetMin - oMin).sqrMagnitude > 0.01f) bar.offsetMin = oMin;
            if ((bar.offsetMax - oMax).sqrMagnitude > 0.01f) bar.offsetMax = oMax;
            if (bar.localScale != Vector3.one) bar.localScale = Vector3.one;
            if (bar.localRotation != Quaternion.identity) bar.localRotation = Quaternion.identity;
        }

        // The description ends at its viewport's right edge, clear of the right-pinned scrollbar: the clone keeps the
        // recipe description's sizeDelta on Epic Loot's stretch anchors (ConvertToScrollingDescription), so it ran past
        // the viewport and right-aligned stat values were cut or overlapped by the scrollbar.
        private static void FitColumn(RectTransform frame, TMP_Text desc)
        {
            var rt = desc.rectTransform;
            if (!(rt.parent is RectTransform view)) return;
            var scroll = Column(frame);
            var bar = scroll && scroll.TryGetComponent<ScrollRect>(out var sr) ? sr.verticalScrollbar : null;
            var barRt = bar ? bar.transform as RectTransform : null;
            if (barRt) PinScrollbar(scroll, barRt);
            var span = RectIn(rt, view);
            // AutoHide: the bar shows once the text outgrows the view, so its place is kept either way.
            var hasBar = barRt != null;
            var barLeft = hasBar ? RectIn(barRt, view).xMin : 0f;
            var w = ElDialogMath.DescriptionWidth(span.xMin, view.rect.xMax, barLeft, hasBar, rt.localScale.x);
            if (w > 1f && Mathf.Abs(rt.rect.width - w) > 0.5f) rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w);
        }

        // The frame at the width its content needs (ElDialogMath.FrameWidth: Epic Loot's 380 up to the safe area minus
        // ElDialogMath.ScreenMargin), centred on the safe area. Returns the width.
        // The drawn frame counts: the panel stack's edges and corner caps reach past the frame rect (run 20261113-200608,
        // 760 frame at 1920x1080: right caps and value column cut by the screen edge with only the rect kept inside).
        private static float Widen(RectTransform frame, Transform dialog, float content, float left, float right)
        {
            SafeRect(frame, out var loX, out var hiX, out _, out _);
            Overhang(frame, dialog, out var overL, out var overR, out _, out _);
            var w = ElDialogMath.FrameWidth(content, left, right, hiX - loX - overL - overR);
            if (Mathf.Abs(frame.rect.width - w) > 0.5f) frame.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w);
            var fr = frame.rect;
            var dx = ElDialogMath.CentreOn(fr.xMin - overL, fr.xMax + overR, loX, hiX);
            if (Mathf.Abs(dx) > 0.5f) frame.position += frame.TransformVector(new Vector3(dx, 0f, 0f));
            return w;
        }

        private static readonly List<Graphic> DecorGraphics = new List<Graphic>();

        // How far the frame's panel stack is drawn beyond the frame rect's four sides (frame units; 0 without one).
        // The corner caps count too: they sit in the sibling RuneSkinPanelCaps, not under the stack (run 20261114-011832:
        // caps-only overhang ~27 units, the frame drawn 3-4 px past the right screen edge).
        private static void Overhang(RectTransform frame, Transform dialog, out float left, out float right, out float bottom, out float top)
        {
            left = right = bottom = top = 0f;
            var decor = Decor(frame, dialog);
            if (!decor) return;
            var fr = frame.rect;
            decor.GetComponentsInChildren(false, DecorGraphics);
            foreach (var c in decor.CornerCaps ?? Array.Empty<RectTransform>())
                if (c && c.gameObject.activeInHierarchy && c.TryGetComponent<Graphic>(out var cap)) DecorGraphics.Add(cap);
            foreach (var g in DecorGraphics)
            {
                if (!g || !g.enabled) continue;
                var r = RectIn(g.rectTransform, frame);
                left = Mathf.Max(left, fr.xMin - r.xMin);
                right = Mathf.Max(right, r.xMax - fr.xMax);
                bottom = Mathf.Max(bottom, fr.yMin - r.yMin);
                top = Mathf.Max(top, r.yMax - fr.yMax);
            }
            DecorGraphics.Clear();
        }

        // Screen.safeArea edges in the frame's local units, inset by ElDialogMath.ScreenMargin.
        private static void SafeRect(RectTransform frame, out float loX, out float hiX, out float loY, out float hiY)
        {
            var canvas = frame.GetComponentInParent<Canvas>();
            var root = canvas ? canvas.rootCanvas : null;
            var cam = root && root.renderMode != RenderMode.ScreenSpaceOverlay ? root.worldCamera : null;
            var s = Screen.safeArea;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(frame, new Vector2(s.xMin, s.yMin), cam, out var a);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(frame, new Vector2(s.xMax, s.yMax), cam, out var b);
            loX = Mathf.Min(a.x, b.x) + ElDialogMath.ScreenMargin;
            hiX = Mathf.Max(a.x, b.x) - ElDialogMath.ScreenMargin;
            loY = Mathf.Min(a.y, b.y) + ElDialogMath.ScreenMargin;
            hiY = Mathf.Max(a.y, b.y) - ElDialogMath.ScreenMargin;
        }

        // Label inset from the button's top / bottom edge (frame units).
        private const float ChoiceVPad = 3f;

        private static readonly List<RectTransform> Active = new List<RectTransform>(8);
        private static readonly Vector3[] Corners4 = new Vector3[4];

        // The choice stack: fixed stable frame width (never jumps between rolls), choices across the inner width above
        // the bottom corner caps; frame height grows within the safe area so the bottom cap band and item description
        // fit cleanly, and a scrolling description snaps to whole lines with its scrollbar pinned right of the text.
        private static void Choices(Transform dialog, List<Button> buttons)
        {
            Active.Clear();
            RectTransform frame = null;
            foreach (var b in buttons)
                if (b && b.gameObject.activeSelf && b.transform is RectTransform rt && rt.parent is RectTransform f)
                {
                    frame = frame ? frame : f;
                    if (f == frame) Active.Add(rt);
                }
            if (!frame || Active.Count == 0) return;
            var scroll = Column(frame);
            if (!scroll) return;
            var desc = DescriptionOf(dialog);
            var floor = ThemeTokens.GetFloat("size.text.fit.min", 12f);
            var body = BodySize(desc);
            SafeRect(frame, out var loX, out var hiX, out var loY, out var hiY);
            Overhang(frame, dialog, out var overL, out var overR, out var overB, out var overT);
            var stableW = ElDialogMath.StableFrameWidth(hiX - loX - overL - overR);
            if (Mathf.Abs(frame.rect.width - stableW) > 0.5f) frame.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, stableW);
            SideInsets(frame, dialog, out var left, out var right, out var art);
            // Align ScrollView horizontally with the choice buttons inside the frame borders.
            if (Mathf.Approximately(scroll.anchorMin.x, 0f) && Mathf.Approximately(scroll.anchorMax.x, 1f))
            {
                if (Mathf.Abs(scroll.offsetMin.x - left) > 0.5f) scroll.offsetMin = new Vector2(left, scroll.offsetMin.y);
                if (Mathf.Abs(scroll.offsetMax.x + right) > 0.5f) scroll.offsetMax = new Vector2(-right, scroll.offsetMax.y);
            }
            if (desc) FitColumn(frame, desc);

            var fr = frame.rect;
            var col = RectIn(scroll, frame);
            var scrollTop = Mathf.Max(0f, fr.yMax - col.yMax);
            // Above the caps the row runs between the frame art's drawn side borders (+ row pad): the whole inner width.
            var width = ElDialogMath.InnerWidth(fr.width, left, right);
            var vpad = ChoiceVPad;
            var heights = new float[Active.Count];
            for (var i = 0; i < Active.Count; i++)
                heights[i] = Label(Active[i], width, body, floor, vpad);

            var capBand = CapBand(frame, dialog);
            var probe = ElDialogMath.Stack(fr.height, scrollTop, heights, capBand, ElDialogMath.ElGap);
            var descNeed = 0f;
            float lineFirst = 0f, linePitch = 0f;
            if (desc && !string.IsNullOrEmpty(desc.text))
            {
                var toFrame = Mathf.Abs(desc.rectTransform.lossyScale.y) / Mathf.Max(Mathf.Abs(frame.lossyScale.y), 1e-6f);
                var descW = desc.rectTransform.rect.width;
                if (descW > 1f) descNeed = (desc.GetPreferredValues(desc.text, descW, 0f).y + 4f) * toFrame;
                var first = desc.GetPreferredValues("A", 10000f, 0f).y - desc.margin.w;
                var pitch = desc.GetPreferredValues("A\nA", 10000f, 0f).y - desc.margin.w - first;
                lineFirst = first * toFrame;
                linePitch = pitch * toFrame;
            }
            var targetH = ElDialogMath.FrameHeight(ElDialogMath.ElFrameHeight(Active.Count), scrollTop, probe.Top, descNeed, ElDialogMath.ElGap, 80f, hiY - loY - overB - overT);
            if (Mathf.Abs(frame.rect.height - targetH) > 0.5f) frame.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, targetH);

            fr = frame.rect;
            var dx = ElDialogMath.CentreOn(fr.xMin - overL, fr.xMax + overR, loX, hiX);
            var dy = ElDialogMath.CentreOn(fr.yMin - overB, fr.yMax + overT, loY, hiY);
            if (Mathf.Abs(dx) > 0.5f || Mathf.Abs(dy) > 0.5f)
                frame.position += frame.TransformVector(new Vector3(dx, dy, 0f));

            var centre = fr.center.x + (left - right) * 0.5f;
            var stack = ElDialogMath.Stack(fr.height, scrollTop, heights, capBand, ElDialogMath.ElGap);
            var scrollH = ElDialogMath.SnapScrollHeight(stack.ScrollHeight, descNeed, lineFirst, linePitch, 80f);
            for (var i = 0; i < Active.Count; i++)
            {
                var rt = Active[i];
                var h = heights[i];
                if (Mathf.Abs(rt.rect.width - width) > 0.5f) rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                if (Mathf.Abs(rt.rect.height - h) > 0.5f) rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
                // Pivot position in the frame's space (anchor-agnostic): column centre, the stack's bottom edge.
                var p = new Vector3(centre + (rt.pivot.x - 0.5f) * width, fr.yMin + stack.Bottoms[i] + rt.pivot.y * h, rt.localPosition.z);
                if ((rt.localPosition - p).sqrMagnitude > 0.01f) rt.localPosition = p;
            }
            if (Mathf.Abs(scroll.rect.height - scrollH) > 0.5f || Mathf.Abs(fr.yMax - RectIn(scroll, frame).yMax - scrollTop) > 0.5f)
                scroll.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, scrollTop, scrollH);
            if (desc)
            {
                FitColumn(frame, desc);
                LayoutRebuilder.ForceRebuildLayoutImmediate(desc.rectTransform);
            }
            if (Logged.Add(dialog.GetInstanceID()))
                Plugin.Log.LogInfo($"[Morgott.UI] el dialog {dialog.name}: frame {fr.width.ToString("0.#", CultureInfo.InvariantCulture)}x{fr.height.ToString("0.#", CultureInfo.InvariantCulture)} scale {frame.lossyScale.x.ToString("0.###", CultureInfo.InvariantCulture)}, body {body.ToString("0.#", CultureInfo.InvariantCulture)}, frame art {art}, scroll {scrollH.ToString("0.#", CultureInfo.InvariantCulture)} (desc need {descNeed.ToString("0.#", CultureInfo.InvariantCulture)}), side insets {left.ToString("0.#", CultureInfo.InvariantCulture)}/{right.ToString("0.#", CultureInfo.InvariantCulture)} -> choices {width.ToString("0.#", CultureInfo.InvariantCulture)}");
            Active.Clear();
        }

        private static readonly HashSet<int> Logged = new HashSet<int>();

        // Left / right inset of the row from the frame edges (frame units): the panel stack's frame art edge + its
        // 9-slice side border + size.row.cap.pad; without a stack Epic Loot's own content inset (10, its scroll view).
        private static void SideInsets(RectTransform frame, Transform dialog, out float left, out float right, out string where)
        {
            var fr = frame.rect;
            var decor = Decor(frame, dialog);
            var art = decor ? decor.Frame : null;
            where = !decor ? "none (no panel stack)" : !art ? $"{decor.transform.parent.name}: no Frame layer" : !art.enabled ? $"{decor.transform.parent.name}: Frame off" : $"{decor.transform.parent.name}/{PanelDecor.ChildName}/Frame";
            if (!art || !art.enabled)
            {
                left = right = 10f;
                return;
            }
            var pad = ThemeTokens.GetFloat("size.row.cap.pad", 6f);
            var a = RectIn(art.rectTransform, frame);
            var border = TextInset.SliceBorderIn(art, frame);
            left = Mathf.Max(0f, a.xMin + border.x - fr.xMin) + pad;
            right = Mathf.Max(0f, fr.xMax - (a.xMax - border.z)) + pad;
        }

        // Epic Loot's description scroll view (CraftSuccessDialog.ConvertToScrollingDescription under the frame).
        private static RectTransform Column(RectTransform frame)
        {
            foreach (Transform c in frame)
                if (c.TryGetComponent<ScrollRect>(out _)) return (RectTransform)c;
            return null;
        }

        // One label across its button; returns the button height it needs (Epic Loot's 40, or more for two lines at the floor).
        private static float Label(RectTransform button, float width, float body, float floor, float vpad)
        {
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (!label) return ElDialogMath.ElHeight;
            // Claim ownership before measuring margins: reverts PlateLabelFit / TextSizeGroup / TextInset so they never
            // re-enable auto-size or overwrite label.margin on subsequent frames.
            OwnFit.Claim(label);
            var hpad = LabelPad(button);
            var lr = label.rectTransform;
            if (lr.parent == button)
            {
                if (lr.anchorMin != Vector2.zero) lr.anchorMin = Vector2.zero;
                if (lr.anchorMax != Vector2.one) lr.anchorMax = Vector2.one;
                var min = new Vector2(hpad, vpad);
                var max = new Vector2(-hpad, -vpad);
                if (lr.offsetMin != min) lr.offsetMin = min;
                if (lr.offsetMax != max) lr.offsetMax = max;
            }
            // Fixed size, no auto-size (run 20261027-183350: auto-size from the label's fontSizeMax drew them at 24.7,
            // bigger than the description): the dialog's body size when the label fits one line there, else shrunk on
            // that line down to the readable floor, below it the floor and wrapped (ElDialogMath.LabelSize).
            var textW = width - 2f * hpad - label.margin.x - label.margin.z;
            if (label.enableAutoSizing) label.enableAutoSizing = false;
            if (textW <= 1f || string.IsNullOrEmpty(label.text))
            {
                if (Mathf.Abs(label.fontSize - body) > 0.01f) label.fontSize = body;
                return ElDialogMath.ElHeight;
            }
            var size = ElDialogMath.LabelSize(body, Mathf.Min(floor, body), Unwrapped(label, body), textW, out var wrapped);
            var wrap = wrapped ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            if (label.textWrappingMode != wrap) label.textWrappingMode = wrap;
            if (Mathf.Abs(label.fontSize - size) > 0.01f) label.fontSize = size;
            var need = label.GetPreferredValues(label.text, textW, 0f).y + label.margin.y + label.margin.w;
            return ElDialogMath.ButtonHeight(ElDialogMath.ElHeight, need, vpad);
        }

        // Top edge of the bottom corner caps over the frame's bottom edge, in frame units (0 without a panel stack).
        private static float CapBand(RectTransform frame, Transform dialog)
        {
            var decor = Decor(frame, dialog);
            var corners = decor ? decor.CornerCaps : null;
            if (corners == null || corners.Length < 4) return 0f;
            var band = 0f;
            for (var i = 2; i < 4; i++)
            {
                var c = corners[i];
                if (!c || !c.TryGetComponent<Image>(out var img) || !img.enabled || !img.sprite || !c.gameObject.activeInHierarchy) continue;
                band = Mathf.Max(band, RectIn(c, frame).yMax - frame.rect.yMin);
            }
            return band;
        }

        // The panel stack on the frame, an ancestor up to the dialog, or the frame's own panel art (PanelSwap child).
        private static PanelDecor Decor(RectTransform frame, Transform dialog)
        {
            for (var p = (Transform)frame; p; p = p.parent)
            {
                var decor = PanelDecor.Of(p);
                if (decor) return decor;
                if (p == dialog) break;
            }
            foreach (Transform c in frame)
            {
                var decor = PanelDecor.Of(c);
                if (decor) return decor;
            }
            // Deeper panel art under the frame (a nested backdrop): the widest stack there.
            PanelDecor best = null;
            foreach (var d in frame.GetComponentsInChildren<PanelDecor>(true))
                if (d && d.transform.parent is RectTransform h && (!best || h.rect.width > ((RectTransform)best.transform.parent).rect.width))
                    best = d;
            return best;
        }

        // The label's horizontal inset in its button: the plate's 9-slice side border + size.row.cap.pad.
        private static float LabelPad(RectTransform button)
        {
            var plate = button.TryGetComponent<Button>(out var b) ? b.targetGraphic as Image : null;
            return (plate ? TextInset.SliceBorder(plate).x : 0f) + ThemeTokens.GetFloat("size.row.cap.pad", 6f);
        }

        private static Type _augType;

        private static TMP_Text DescriptionOf(Transform dialog)
        {
            _augType = _augType ?? Morgott.Core.TypeLookup.ByFullName(AugmentDialog);
            var d = dialog && _augType != null ? dialog.GetComponent(_augType) : null;
            return d ? Field(d.GetType(), "Description")?.GetValue(d) as TMP_Text : null;
        }

        // The dialog's body text size: its description's (Epic Loot sets 18, ConvertToScrollingDescription), else 18.
        private static float BodySize(TMP_Text desc) =>
            desc && desc.fontSize > 1f ? desc.fontSize : ElDialogMath.ElBodySize;

        // The label's one-line width at size (auto-size off for the measure only).
        private static float Unwrapped(TMP_Text label, float size)
        {
            var auto = label.enableAutoSizing;
            var was = label.fontSize;
            label.enableAutoSizing = false;
            label.fontSize = size;
            var w = label.GetPreferredValues(label.text).x;
            label.fontSize = was;
            label.enableAutoSizing = auto;
            return w;
        }

        // rt's rect in space's local units.
        private static Rect RectIn(RectTransform rt, Transform space)
        {
            rt.GetWorldCorners(Corners4);
            var a = space.InverseTransformPoint(Corners4[0]);
            var b = space.InverseTransformPoint(Corners4[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }
    }

    [HarmonyPatch]
    public static class ElDialogsAdapter
    {
        // Harmony asks before patching this class (Plugin: one PatchAll(Assembly), batched by FastStartup). Needed while
        // RuneSkin moves the cloned parts (Crafting), marks the cloned dialog (InventoryExtras), or styles Epic Loot UI.
        private static bool Prepare() =>
            OwnershipRegistry.IsOwned(OwnershipRegistry.Crafting) ||
            OwnershipRegistry.IsOwned(OwnershipRegistry.InventoryExtras) ||
            OwnershipRegistry.IsOwned(OwnershipRegistry.EpicLootEnchanting) ||
            OwnershipRegistry.IsOwned(OwnershipRegistry.EpicLootBoard);

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Awake))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Awake_Prefix(InventoryGui __instance)
        {
            try
            {
                ElDialogs.Snapshot(__instance);
                ElDialogs.EnsurePatched();
            }
            catch (Exception e) { Plugin.Log.LogError($"[Morgott.UI] Epic Loot dialog hooks failed: {e}"); }
        }
    }
}
