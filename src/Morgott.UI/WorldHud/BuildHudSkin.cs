using Morgott.Core;
using Morgott.UI.Api;
using System;
using Morgott.UI.Classify;
using Morgott.UI.InfoScreens;
using Morgott.UI.Ownership;
using Morgott.UI.PreWorld;
using Morgott.UI.Theme;
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
            if (hud.m_buildUi)
            {
                var buildRoot = hud.m_buildUi.transform;
                Register(buildRoot, ScreenId);
                _styledBuildUi = buildRoot;
                _styledBuildRev = ThemeTokens.Revision;
            }
            if (hud.m_buildHud)
            {
                var info = hud.m_buildHud.transform.Find("SelectedInfo");
                StripWell(info);
                EnsureCreator(info as RectTransform);
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
            ScreenRegistry.Register(root, new ContextProvider(root, id).Get);
            ScreenRegistry.Restyle(root);
        }

        private sealed class ContextProvider
        {
            private readonly Transform _root;
            private readonly string _id;
            public ContextProvider(Transform root, string id) { _root = root; _id = id; }
            public ScreenContext Get() => Context(_root, _id ?? (ScreenId + "." + _root.name.Replace("(Clone)", "")));
        }

        // Selected-piece strip (mockup m6-build-a: frame_inset well): vanilla draws its backgrounds only in some states
        // (Bkg2 objects off with the menu open, run 20261022-101946), so the strip gets its own well behind everything.
        private const string WellName = "RuneSkin_StripWell";
        public const string RepairCreatorName = "RuneSkin_RepairCreator";

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

        private static TMP_Text EnsureCreator(RectTransform info)
        {
            if (!info) return null;
            var t = InfoArt.RowText(info, RepairCreatorName, "text.body", TextAlignmentOptions.MidlineLeft);
            t.raycastTarget = false;
            t.richText = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.enableAutoSizing = false;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            StyleCreator(t);
            if (!t.gameObject.activeSelf && string.IsNullOrEmpty(t.text)) t.gameObject.SetActive(false);
            return t;
        }

        private static void StyleCreator(TMP_Text t)
        {
            if (!t) return;
            t.richText = false;
            var gold = ThemeTokens.GetColor("color.text.active", new Color(0.91f, 0.71f, 0.29f));
            var sub = ThemeTokens.GetColor("color.stats.sub", new Color(0.66f, 0.70f, 0.72f));
            t.color = Color.Lerp(gold, sub, 0.35f);
            t.fontSize = MessageSkin.HudReadable(ThemeTokens.GetFloat("size.build.creator.font", 18f));
        }

        // Hud.SetupPieceInfo (Hud.cs:1526) fills these two; the prefab rects abut (PlayHudMath.DescriptionTop), run
        // 20261108-051942: name line box bottom == description line box top (324 px at 1080). Rect fix, set once.
        // Strip = ONE row of what Hud.SetupPieceInfo (Hud.cs:1511) shows (user 2026-11-14 "ещё уже надо. в 1 строку"; earlier:
        // the description box covered the screen): [piece icon][piece name][requirement cells][snapping hint][repair creator], each element
        // centred on one midline, the row centred on x 0, its bottom margin above y 0 (SelectedInfo pivot bottom-centre),
        // so StripClear's flush placement (drawn bottom) never jumps. The description (Hud.cs:1527) is not drawn: one row
        // cannot hold it. The icon is scaled to the cell height. The requirement row moves as one block (`requirements`
        // keeps its own cell spacing). Each call moves elements by (target - current drawn box): idempotent per frame.
        // Bkg2 (vanilla 50 % black) + the well hug the row.
        private static GameObject _buildHud;
        private static RectTransform _info, _req, _bkg, _wellRt;
        private static TMP_Text _creator;
        private static GameObject[] _reqItemsSource;
        private static TMP_Text[][] _reqTexts;
        private static readonly RectTransform[] _stripMoves = new RectTransform[5];
        private static readonly Rect[] _stripBoxes = new Rect[5];
        private static readonly float[] _stripWidths = new float[5];
        private static readonly float[] _stripLefts = new float[5];

        private static Rect StripBox(Transform info, Transform from, Vector2 min, Vector2 max)
        {
            Vector2 a = info.InverseTransformPoint(from.TransformPoint(min)), c = info.InverseTransformPoint(from.TransformPoint(max));
            return Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y), Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y));
        }

        private static Rect StripUnion(Rect p, Rect q) =>
            Rect.MinMaxRect(Mathf.Min(p.xMin, q.xMin), Mathf.Min(p.yMin, q.yMin), Mathf.Max(p.xMax, q.xMax), Mathf.Max(p.yMax, q.yMax));

        private static bool StripTextBox(Transform info, TMP_Text x, out Rect r)
        {
            r = default;
            if (!x || !x.enabled || !x.gameObject.activeInHierarchy || string.IsNullOrEmpty(x.text)) return false;
            var tb = x.textBounds; // last mesh: one frame behind a text change, SetupPieceInfo runs every frame
            if (!(tb.size.x > 0f)) return false;
            r = StripBox(info, x.transform, tb.min, tb.max);
            return true;
        }

        public static void FitStrip(Hud hud)
        {
            if (!hud.m_buildHud || hud.m_requirementItems == null || hud.m_requirementItems.Length == 0 || !hud.m_requirementItems[0]) return;
            if (_buildHud != hud.m_buildHud || !_info)
            {
                _buildHud = hud.m_buildHud;
                _info = _buildHud.transform.Find("SelectedInfo") as RectTransform;
                if (!_info) return;
                _req = _info.Find("requirements") as RectTransform;
                _bkg = _info.Find("Bkg2") as RectTransform;
                _wellRt = _info.Find(WellName) as RectTransform;
                _creator = EnsureCreator(_info);
                _reqItemsSource = null;
                _reqTexts = null;
            }
            var info = _info;
            if (!_req) return;
            if (hud.m_pieceDescription && hud.m_pieceDescription.enabled) hud.m_pieceDescription.enabled = false;
            var cellH = ((RectTransform)hud.m_requirementItems[0].transform).rect.height;
            UpdateRepairCreator(hud, info, cellH);
            var icon = hud.m_buildIcon && hud.m_buildIcon.enabled ? hud.m_buildIcon.rectTransform : null;
            if (icon && icon.rect.height > 0f)
            {
                var s = cellH / icon.rect.height;
                if (Mathf.Abs(icon.localScale.x - s) > 0.001f) icon.localScale = new Vector3(s, s, 1f);
            }

            // Drawn box of each element in SelectedInfo space.
            var count = 0;
            if (icon) { _stripMoves[count] = icon; _stripBoxes[count++] = StripBox(info, icon, icon.rect.min, icon.rect.max); }
            if (StripTextBox(info, hud.m_buildSelection, out var nameBox)) { _stripMoves[count] = hud.m_buildSelection.rectTransform; _stripBoxes[count++] = nameBox; }

            var items = hud.m_requirementItems;
            if (_reqItemsSource != items || _reqTexts == null || _reqTexts.Length != items.Length)
            {
                _reqItemsSource = items;
                _reqTexts = new TMP_Text[items.Length][];
                for (var i = 0; i < items.Length; i++)
                    if (items[i]) _reqTexts[i] = items[i].GetComponentsInChildren<TMP_Text>(true);
            }
            var cells = false;
            var row = default(Rect);
            for (var i = 0; i < items.Length; i++)
            {
                var go = items[i];
                if (!go || !go.activeSelf) continue;
                var rt = (RectTransform)go.transform;
                var cell = StripBox(info, rt, rt.rect.min, rt.rect.max);
                var texts = _reqTexts[i] ?? (_reqTexts[i] = go.GetComponentsInChildren<TMP_Text>(true));
                for (var j = 0; j < texts.Length; j++)
                    if (StripTextBox(info, texts[j], out var tr)) cell = StripUnion(cell, tr); // res_name / res_amount drawn past the cell count
                row = cells ? StripUnion(row, cell) : cell;
                cells = true;
            }
            if (cells) { _stripMoves[count] = _req; _stripBoxes[count++] = row; }
            var snap = hud.m_snappingIcon && hud.m_snappingIcon.enabled ? hud.m_snappingIcon.rectTransform : null;
            if (snap) { _stripMoves[count] = snap; _stripBoxes[count++] = StripBox(info, snap, snap.rect.min, snap.rect.max); }
            if (StripTextBox(info, _creator, out var creatorBox)) { _stripMoves[count] = _creator.rectTransform; _stripBoxes[count++] = creatorBox; }
            if (count == 0) return;

            var inner = Theme.ThemeTokens.GetFloat("size.build.strip.inner", 6f);
            var pad = Theme.ThemeTokens.GetFloat("size.build.strip.pad", 10f);
            var gap = Theme.ThemeTokens.GetFloat("size.build.strip.gap", 8f);
            var rowH = 0f;
            var x = 0f;
            var gapClamped = Mathf.Max(0f, gap);
            for (var i = 0; i < count; i++)
            {
                var w = _stripBoxes[i].width;
                _stripWidths[i] = w;
                rowH = Mathf.Max(rowH, _stripBoxes[i].height);
                _stripLefts[i] = x;
                x += Mathf.Max(0f, w) + (i + 1 < count ? gapClamped : 0f);
            }
            var half = x * 0.5f;
            for (var i = 0; i < count; i++) _stripLefts[i] -= half;
            var mid = inner + pad + rowH * 0.5f;
            for (var i = 0; i < count; i++)
            {
                var d = new Vector2(_stripLefts[i] - _stripBoxes[i].xMin, mid - _stripBoxes[i].center.y);
                if (d.sqrMagnitude <= 0.0001f) continue;
                var move = _stripMoves[i];
                var parent = move.parent;
                move.anchoredPosition += (Vector2)parent.InverseTransformVector(info.TransformVector(d));
            }
            var box = Rect.MinMaxRect(_stripLefts[0], inner + pad, _stripLefts[count - 1] + _stripWidths[count - 1], inner + pad + rowH);
            var full = info.rect;
            Hug(_bkg, box, full, inner);
            Hug(_wellRt, box, full, inner + pad);
        }

        private static void UpdateRepairCreator(Hud hud, RectTransform info, float cellH)
        {
            var player = Player.m_localPlayer;
            var menuOpen = Hud.IsPieceSelectionVisible() || (hud && hud.m_buildUi && hud.m_buildUi.gameObject.activeSelf);
            var inRepair = player && player.InRepairMode();
            var piece = !menuOpen && inRepair ? player.GetHoveringPiece() : null;
            var creatorId = piece ? piece.GetCreator() : 0L;
            if (!PlayHudMath.ShouldShowRepairCreator(menuOpen, inRepair, creatorId))
            {
                if (_creator && _creator.gameObject.activeSelf) _creator.gameObject.SetActive(false);
                return;
            }
            if (!_creator) _creator = EnsureCreator(info);
            if (!_creator) return;
            var serviceName = Services.TryGet<IPlayerNames>(out var names) ? names.NameOf(creatorId) : null;
            var history = ZNet.World?.m_playerHistory;
            var unknown = Localization.instance != null ? Localization.instance.Localize("$build_piece_author_unknown") : "Unknown";
            var resolved = PlayHudMath.ResolveCreatorName(
                creatorId,
                serviceName,
                player.GetPlayerID(),
                player.GetPlayerName(),
                FindLivePlayerName,
                history?.Count ?? 0,
                ReadWorldHistory,
                piece.GetCreatorPlatformUserIdIndex(),
                unknown,
                FilterCreatorName);
            var line = PlayHudMath.FormatRepairCreator(Words.Get("$ui_repair_built_by"), resolved);
            StyleCreator(_creator);
            var turnedOn = !_creator.gameObject.activeSelf;
            if (turnedOn) _creator.gameObject.SetActive(true);
            if (turnedOn || !string.Equals(_creator.text, line, StringComparison.Ordinal))
            {
                _creator.text = line;
                var pref = _creator.GetPreferredValues(line, float.PositiveInfinity, float.PositiveInfinity);
                _creator.rectTransform.sizeDelta = new Vector2(Mathf.Min(520f, Mathf.Ceil(pref.x) + 4f), Mathf.Max(cellH, Mathf.Ceil(pref.y) + 2f));
                _creator.ForceMeshUpdate();
            }
        }

        private static string FindLivePlayerName(long creatorId)
        {
            var players = Player.s_players;
            if (players == null) return null;
            for (var i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p && p.GetPlayerID() == creatorId) return p.GetPlayerName();
            }
            return null;
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

        private static (string, string) ReadWorldHistory(int i)
        {
            var entry = ZNet.World.m_playerHistory[i];
            return (entry.m_serverAssignedDisplayName, entry.m_displayName);
        }

        private static string FilterCreatorName(string name, long id) =>
            CensorShittyWords.FilterUGC(name, UGCType.CharacterName, id);

        private static void RegisterTemplate(Transform template)
        {
            if (ScreenRegistry.ContextOf(template) != null) return;
            ScreenRegistry.Register(template, new ContextProvider(template, null).Get);
            ScreenRegistry.Restyle(template);
        }

        private static Transform _styledBuildUi;
        private static int _styledBuildRev = -1;

        public static void Refresh(BuildUi ui)
        {
            if (!ui) return;
            var root = ui.transform;
            var rev = ThemeTokens.Revision;
            if (_styledBuildUi != root || _styledBuildRev != rev)
            {
                if (ScreenRegistry.Restyle(root))
                {
                    _styledBuildUi = root;
                    _styledBuildRev = rev;
                }
                return;
            }
            // Warm open: only restyle newly pooled / mod-added piece or tag button subtrees.
            if (ui.m_specialPieceButton) RefreshPiece(ui.m_specialPieceButton, root, rev);
            var pieces = ui.m_pieceButtons;
            if (pieces != null)
                for (var i = 0; i < pieces.Count; i++) RefreshPiece(pieces[i], root, rev);
            var tagList = ui.m_tagListScrollContent;
            if (tagList)
            {
                for (var i = 0; i < tagList.childCount; i++)
                {
                    var child = tagList.GetChild(i);
                    if (child.TryGetComponent<BuildUiTagButton>(out var tb)) RefreshTag(tb, root, rev);
                }
            }
        }

        private static void RefreshPiece(BuildUiPieceButton piece, Transform root, int rev)
        {
            if (!piece) return;
            var g = piece.m_button && piece.m_button.targetGraphic ? piece.m_button.targetGraphic : piece.GetComponent<Image>();
            if (IsStyledMark(g, rev)) return;
            Restyle.Restyler.RestyleSubtree(piece.transform, root, Context(piece.transform, ScreenId));
        }

        private static void RefreshTag(BuildUiTagButton tag, Transform root, int rev)
        {
            if (!tag) return;
            var g = tag.TryGetComponent<Selectable>(out var sel) && sel.targetGraphic ? sel.targetGraphic : tag.GetComponent<Image>();
            if (IsStyledMark(g, rev)) return;
            Restyle.Restyler.RestyleSubtree(tag.transform, root, Context(tag.transform, ScreenId));
        }

        private static bool IsStyledMark(Graphic g, int rev) =>
            g && g.TryGetComponent<Restyle.SkinMark>(out var m) && m.IsOwnMark && m.ThemeRevision == rev;

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
                else if (t.name == RepairCreatorName) ctx.Role(t, "text.body", "repair hovered piece creator (SelectedInfo)");
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
