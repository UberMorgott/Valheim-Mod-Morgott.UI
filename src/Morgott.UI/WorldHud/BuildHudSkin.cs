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
        // Strip = ONE row of what Hud.SetupPieceInfo (Hud.cs:1511) shows (user 2026-11-14 "ещё уже надо. в 1 строку"; earlier:
        // the description box covered the screen): [piece icon][piece name][requirement cells][snapping hint], each element
        // centred on one midline, the row centred on x 0, its bottom margin above y 0 (SelectedInfo pivot bottom-centre),
        // so StripClear's flush placement (drawn bottom) never jumps. The description (Hud.cs:1527) is not drawn: one row
        // cannot hold it. The icon is scaled to the cell height. The requirement row moves as one block (`requirements`
        // keeps its own cell spacing). Each call moves elements by (target - current drawn box): idempotent per frame.
        // Bkg2 (vanilla 50 % black) + the well hug the row.
        private static RectTransform _req, _bkg, _wellRt;
        private static Transform _info;

        public static void FitStrip(Hud hud)
        {
            var info = hud.m_buildHud ? hud.m_buildHud.transform.Find("SelectedInfo") as RectTransform : null;
            if (!info || hud.m_requirementItems == null || hud.m_requirementItems.Length == 0 || !hud.m_requirementItems[0]) return;
            if (_info != info)
            {
                _req = info.Find("requirements") as RectTransform;
                _bkg = info.Find("Bkg2") as RectTransform;
                _wellRt = info.Find(WellName) as RectTransform;
                if (!_req) return;
                _info = info;
            }
            if (hud.m_pieceDescription && hud.m_pieceDescription.enabled) hud.m_pieceDescription.enabled = false;
            var cellH = ((RectTransform)hud.m_requirementItems[0].transform).rect.height;
            var icon = hud.m_buildIcon && hud.m_buildIcon.enabled ? hud.m_buildIcon.rectTransform : null;
            if (icon && icon.rect.height > 0f)
            {
                var s = cellH / icon.rect.height;
                if (Mathf.Abs(icon.localScale.x - s) > 0.001f) icon.localScale = new Vector3(s, s, 1f);
            }

            // Drawn box of each element in SelectedInfo space.
            var moves = new System.Collections.Generic.List<RectTransform>(4);
            var boxes = new System.Collections.Generic.List<Rect>(4);
            Rect Box(Transform from, Vector2 min, Vector2 max)
            {
                Vector2 a = info.InverseTransformPoint(from.TransformPoint(min)), c = info.InverseTransformPoint(from.TransformPoint(max));
                return Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y), Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y));
            }
            Rect Union(Rect p, Rect q) => Rect.MinMaxRect(Mathf.Min(p.xMin, q.xMin), Mathf.Min(p.yMin, q.yMin), Mathf.Max(p.xMax, q.xMax), Mathf.Max(p.yMax, q.yMax));
            bool TextBox(TMP_Text x, out Rect r)
            {
                r = default;
                if (!x || !x.enabled || !x.gameObject.activeInHierarchy || string.IsNullOrEmpty(x.text)) return false;
                var tb = x.textBounds; // last mesh: one frame behind a text change, SetupPieceInfo runs every frame
                if (!(tb.size.x > 0f)) return false;
                r = Box(x.transform, tb.min, tb.max);
                return true;
            }
            void Element(RectTransform move, Rect box) { moves.Add(move); boxes.Add(box); }

            if (icon) Element(icon, Box(icon, icon.rect.min, icon.rect.max));
            if (TextBox(hud.m_buildSelection, out var nameBox)) Element(hud.m_buildSelection.rectTransform, nameBox);
            var cells = false;
            var row = default(Rect);
            foreach (var go in hud.m_requirementItems)
            {
                if (!go || !go.activeSelf) continue;
                var rt = (RectTransform)go.transform;
                var cell = Box(rt, rt.rect.min, rt.rect.max);
                foreach (var t in go.GetComponentsInChildren<TMP_Text>(false))
                    if (TextBox(t, out var tr)) cell = Union(cell, tr); // res_name / res_amount drawn past the cell count
                row = cells ? Union(row, cell) : cell;
                cells = true;
            }
            if (cells) Element(_req, row);
            var snap = hud.m_snappingIcon && hud.m_snappingIcon.enabled ? hud.m_snappingIcon.rectTransform : null;
            if (snap) Element(snap, Box(snap, snap.rect.min, snap.rect.max));
            if (moves.Count == 0) return;

            var inner = Theme.ThemeTokens.GetFloat("size.build.strip.inner", 6f);
            var pad = Theme.ThemeTokens.GetFloat("size.build.strip.pad", 10f);
            var gap = Theme.ThemeTokens.GetFloat("size.build.strip.gap", 8f);
            var widths = new float[moves.Count];
            var rowH = 0f;
            for (var i = 0; i < moves.Count; i++) { widths[i] = boxes[i].width; rowH = Mathf.Max(rowH, boxes[i].height); }
            var lefts = PlayHudMath.StripRow(widths, gap);
            var mid = inner + pad + rowH * 0.5f;
            for (var i = 0; i < moves.Count; i++)
            {
                var d = new Vector2(lefts[i] - boxes[i].xMin, mid - boxes[i].center.y);
                if (d.sqrMagnitude <= 0.0001f) continue;
                var parent = moves[i].parent;
                moves[i].anchoredPosition += (Vector2)parent.InverseTransformVector(info.TransformVector(d));
            }
            var box = Rect.MinMaxRect(lefts[0], inner + pad, lefts[moves.Count - 1] + widths[moves.Count - 1], inner + pad + rowH);
            var full = info.rect;
            Hug(_bkg, box, full, inner);
            Hug(_wellRt, box, full, inner + pad);
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
