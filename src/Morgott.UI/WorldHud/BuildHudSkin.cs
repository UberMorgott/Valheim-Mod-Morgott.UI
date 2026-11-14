using Morgott.UI.Api;
using System;
using Morgott.UI.Classify;
using Morgott.UI.Ownership;
using Morgott.UI.PreWorld;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Morgott.UI.WorldHud
{
    // Family "BuildHud" (mockup m6-build-a, art\README.md "M6 sprites"): the build menu (BuildUi, hudroot/BuildUIV2:
    // mode tabs, tag list, search, piece grid, favourites dropdown) and Hud.m_buildHud (SelectedInfo strip, hovered
    // piece author window) restyled through the role pass, as the M5 forms: vanilla panels -> panel stack
    // (PreWorldKit.Panels), mode tabs = tab plates, tag rows = craft rows (selected = m_toggledOnObject), piece buttons
    // = slot_cell_recess sockets (icons content), search = field_search, keyboard hint plates = plate_key, requirement
    // cells = socket_req. Hud.m_pieceSelectionWindow (the pre-V2 menu, deactivated by Hud.Awake :382) is excluded.
    public static class BuildHudSkin
    {
        public const string ScreenId = "BuildHud";

        public static void Attach(Hud hud)
        {
            if (hud.m_buildUi) Register(hud.m_buildUi.transform, ScreenId);
            if (hud.m_buildHud)
            {
                StripWell(hud.m_buildHud.transform.Find("SelectedInfo"));
                DescriptionUnderName(hud.m_buildSelection, hud.m_pieceDescription);
                Register(hud.m_buildHud.transform, ScreenId + ".Selected");
            }
            // Hovered piece author window (crossplay author name / platform id; kit popup): its own root unless it lives
            // under the selected-piece strip (then that registration covers it).
            var author = hud.m_hoveredPieceAuthorWindow ? hud.m_hoveredPieceAuthorWindow.transform : null;
            if (author && !(hud.m_buildHud && author.IsChildOf(hud.m_buildHud.transform))) Register(author, ScreenId + ".Author");
            Plugin.Log.LogInfo("[Morgott.UI] build HUD attached");
        }

        private static void Register(Transform root, string id)
        {
            PreWorldKit.Panels(root, "size.panel.pad");
            ScreenRegistry.Register(root, () => Context(root, id));
            ScreenRegistry.Restyle(root);
        }

        // Selected-piece strip (mockup m6-build-a: frame_inset well): vanilla draws its backgrounds only in some states
        // (Bkg2 objects off with the menu open, run 20261022-101946), so the strip gets its own well behind everything.
        private const string WellName = "RuneSkin_StripWell";

        private static void StripWell(Transform info)
        {
            if (!(info is RectTransform rt) || rt.Find(WellName)) return;
            var go = new GameObject(WellName, typeof(RectTransform), typeof(Image));
            var wr = (RectTransform)go.transform;
            wr.SetParent(rt, false);
            wr.SetAsFirstSibling();
            wr.anchorMin = Vector2.zero;
            wr.anchorMax = Vector2.one;
            var pad = Theme.ThemeTokens.GetFloat("size.build.strip.pad", 10f);
            wr.offsetMin = new Vector2(-pad, -pad);
            wr.offsetMax = new Vector2(pad, pad);
            go.GetComponent<Image>().raycastTarget = false;
        }

        // Hud.SetupPieceInfo (Hud.cs:1526) fills these two; the prefab rects abut (PlayHudMath.DescriptionTop), run
        // 20261108-051942: name line box bottom == description line box top (324 px at 1080). Rect fix, set once.
        private static void DescriptionUnderName(TMP_Text name, TMP_Text desc)
        {
            if (!name || !desc || name.transform.parent != desc.transform.parent) return;
            var n = name.rectTransform;
            var d = desc.rectTransform;
            if (n.anchorMin.y != 1f || n.anchorMax.y != 1f || d.anchorMax.y != 1f) return; // prefab changed: top-anchored math no longer holds
            var top = PlayHudMath.DescriptionTop(n.anchoredPosition.y, n.rect.height, n.pivot.y, d.offsetMax.y, Theme.ThemeTokens.GetFloat("size.build.desc.gap", 6f));
            d.offsetMax = new Vector2(d.offsetMax.x, top);
        }

        // Strip fitted to what Hud.SetupPieceInfo (Hud.cs:1511) shows (user 2026-11-14: the repair tool, icon + name only,
        // sat in the full 410 x 163 prefab box). Prefab SelectedInfo (pivot bottom-centre): selected_piece (icon x -168,
        // name, description, snapping hint x +168) over the requirement row (cells from x -196). Per call: the drawn
        // content box (icon / snapping hint rects, text bounds, active requirement cells) is moved so it is centred on x 0
        // with its bottom margin above y 0, and Bkg2 (vanilla 50 % black) + the well hug it. Bottom stays at y 0 for every
        // piece, so StripClear's flush placement (drawn bottom) does not jump when the content changes.
        private static RectTransform _piece, _req, _bkg, _wellRt;
        private static Vector2 _pieceHome, _reqHome, _shift;

        public static void FitStrip(Hud hud)
        {
            var info = hud.m_buildHud ? hud.m_buildHud.transform.Find("SelectedInfo") as RectTransform : null;
            if (!info) return;
            if (!_piece || _piece.parent != info)
            {
                _piece = info.Find("selected_piece") as RectTransform;
                _req = info.Find("requirements") as RectTransform;
                _bkg = info.Find("Bkg2") as RectTransform;
                _wellRt = info.Find(WellName) as RectTransform;
                if (!_piece || !_req) { _piece = null; return; }
                _pieceHome = _piece.anchoredPosition;
                _reqHome = _req.anchoredPosition;
                _shift = Vector2.zero;
            }
            // Vanilla never disables the description; an empty one would still count as drawn (StripClear, harness).
            var desc = hud.m_pieceDescription;
            if (desc) desc.enabled = !string.IsNullOrEmpty(desc.text);
            float l = float.MaxValue, r = float.MinValue, b = float.MaxValue, t = float.MinValue;
            void Point(Transform from, Vector2 p)
            {
                var q = info.InverseTransformPoint(from.TransformPoint(p)) - (Vector3)_shift; // home (unshifted) coords
                l = Mathf.Min(l, q.x); r = Mathf.Max(r, q.x); b = Mathf.Min(b, q.y); t = Mathf.Max(t, q.y);
            }
            void Add(Transform from, Vector2 min, Vector2 max) { Point(from, min); Point(from, max); }
            void AddRect(RectTransform rt) { var rr = rt.rect; Add(rt, rr.min, rr.max); }
            void AddText(TMP_Text x)
            {
                if (!x || !x.enabled || string.IsNullOrEmpty(x.text)) return;
                var tb = x.textBounds; // last mesh: one frame behind a text change, SetupPieceInfo runs every frame
                if (tb.size.x > 0f) Add(x.transform, tb.min, tb.max);
            }
            if (hud.m_buildIcon && hud.m_buildIcon.enabled) AddRect(hud.m_buildIcon.rectTransform);
            if (hud.m_snappingIcon && hud.m_snappingIcon.enabled) AddRect(hud.m_snappingIcon.rectTransform);
            AddText(hud.m_buildSelection);
            AddText(desc);
            foreach (var go in hud.m_requirementItems)
                if (go && go.activeSelf) AddRect((RectTransform)go.transform);
            if (l == float.MaxValue) return;
            var inner = Theme.ThemeTokens.GetFloat("size.build.strip.inner", 6f);
            var pad = Theme.ThemeTokens.GetFloat("size.build.strip.pad", 10f);
            PlayHudMath.StripFitShift(l, r, b, inner + pad, out var dx, out var dy);
            _shift = new Vector2(dx, dy);
            Set(_piece, _pieceHome + _shift);
            Set(_req, _reqHome + _shift);
            var box = Rect.MinMaxRect(l + _shift.x, b + _shift.y, r + _shift.x, t + _shift.y);
            var full = info.rect;
            Hug(_bkg, box, full, inner);
            Hug(_wellRt, box, full, inner + pad);
        }

        private static void Set(RectTransform rt, Vector2 pos)
        {
            if ((rt.anchoredPosition - pos).sqrMagnitude > 0.0001f) rt.anchoredPosition = pos;
        }

        // Stretched backdrop (anchors 0..1) -> content box + margin, in the parent's rect space.
        private static void Hug(RectTransform rt, Rect box, Rect full, float margin)
        {
            if (!rt) return;
            var min = new Vector2(box.xMin - margin - full.xMin, box.yMin - margin - full.yMin);
            var max = new Vector2(box.xMax + margin - full.xMax, box.yMax + margin - full.yMax);
            if ((rt.offsetMin - min).sqrMagnitude > 0.0001f) rt.offsetMin = min;
            if ((rt.offsetMax - max).sqrMagnitude > 0.0001f) rt.offsetMax = max;
        }

        public static void Templates(BuildUi ui)
        {
            if (ui.m_pieceButtonPrefab) RegisterTemplate(ui.m_pieceButtonPrefab.transform);
            if (ui.m_tagButtonPrefab) RegisterTemplate(ui.m_tagButtonPrefab.transform);
        }

        private static void RegisterTemplate(Transform template)
        {
            if (ScreenRegistry.ContextOf(template) != null) return;
            ScreenRegistry.Register(template, () => Context(template, ScreenId + "." + template.name.Replace("(Clone)", "")));
            ScreenRegistry.Restyle(template);
        }

        public static void Refresh(BuildUi ui) => ScreenRegistry.Restyle(ui.transform);

        private static ScreenContext Context(Transform root, string id)
        {
            var ctx = new ScreenContext(id, OwnershipRegistry.BuildHud);
            var hud = Hud.instance;
            if (hud && hud.m_pieceSelectionWindow && hud.m_pieceSelectionWindow.transform.IsChildOf(root))
                ctx.Exclude(hud.m_pieceSelectionWindow.transform, "Hud.m_pieceSelectionWindow: pre-V2 build menu, deactivated by Hud.Awake");
            foreach (var h in root.GetComponentsInChildren<TabHandler>(true))
                foreach (var tab in h.m_tabs)
                    if (tab != null && tab.m_button && tab.m_button.targetGraphic) ctx.Role(tab.m_button.targetGraphic, "tab", "BuildUi mode tab (TabHandler)");
            foreach (var tag in root.GetComponentsInChildren<BuildUiTagButton>(true))
            {
                if (tag.TryGetComponent<Selectable>(out var sel) && sel.targetGraphic) ctx.Role(sel.targetGraphic, "craft.row", "build tag row");
                else if (tag.TryGetComponent<Image>(out var row)) ctx.Role(row, "craft.row", "build tag row");
                if (tag.m_toggledOnObject && tag.m_toggledOnObject.TryGetComponent<Image>(out var on)) ctx.Role(on, "craft.row.selected", "build tag selected (m_toggledOnObject)");
                foreach (var t in tag.GetComponentsInChildren<TMP_Text>(true)) ctx.Role(t, "text.body", "build tag label");
                foreach (var img in tag.GetComponentsInChildren<Image>(true))
                    if (!ctx.AdapterRoles.ContainsKey(img) && img.sprite) ctx.Role(img, "content", $"build tag icon '{img.sprite.name}'");
            }
            foreach (var piece in root.GetComponentsInChildren<BuildUiPieceButton>(true))
            {
                if (piece.m_button && piece.m_button.targetGraphic) ctx.Role(piece.m_button.targetGraphic, "slot.recess", "build piece socket");
                else if (piece.TryGetComponent<Image>(out var back)) ctx.Role(back, "slot.recess", "build piece socket");
                foreach (var img in piece.GetComponentsInChildren<Image>(true))
                    if (!ctx.AdapterRoles.ContainsKey(img)) ctx.Role(img, "content", "build piece icon / marker (vanilla)");
            }
            foreach (var img in root.GetComponentsInChildren<Image>(true))
            {
                var sprite = img.sprite ? img.sprite.name : null;
                if (img.GetComponent<Restyle.SkinMark>() is Restyle.SkinMark m && !string.IsNullOrEmpty(m.OriginalSprite)) sprite = m.OriginalSprite;
                // Keyboard hint plates (InputHelp/MK hints Left / Right, SearchBarHint/Mouse): TextBackdrop boxes.
                if (img.name == WellName) ctx.Role(img, "well", "selected-piece strip well (frame_inset)");
                else if ((sprite == "TextBackdrop" || sprite == "key_base") && Under(img.transform, root, "hint")) ctx.Role(img, "key.plate", "keyboard hint plate");
                // Selected-piece strip: requirement cells (res_bkg) and the strip panel.
                else if (img.name.StartsWith("res_bkg", StringComparison.Ordinal)) ctx.Role(img, "craft.socket", "build requirement cell");
                else if (img.name == "res_icon" || img.name == "piece_icon" || img.name == "snappingHint" || img.name == "player_portrait") ctx.Role(img, "content", "piece / resource icon");
            }
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (ctx.AdapterRoles.ContainsKey(t)) continue;
                if (t.name == "piece_name") ctx.Role(t, "text.display", "selected piece name (display serif)");
                else if (t.name == "res_amount") ctx.Role(t, "skill.level", "requirement amount");
                else if (t.name == ComfortBadge.ValueName) ctx.Role(t, "content", "piece comfort value (ComfortBadge, fixed size)");
            }
            PreWorldKit.Common(ctx, root);
            return ctx;
        }

        private static bool Under(Transform t, Transform root, string fragment)
        {
            for (var p = t; p && p != root; p = p.parent)
                if (p.name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
    }
}
