using System;
using System.Collections.Generic;
using Morgott.Core;
using Morgott.UI.Classify;
using Morgott.UI.InfoScreens;
using Morgott.UI.InventoryScreen;
using Morgott.UI.Restyle;
using Morgott.UI.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Morgott.UI.GameScreens
{
    // Hildir's boss contract board (approved mockup docs\mockups\2026-11-24\hildir-bounty-board.png), the right zone of
    // her trader page (TraderPage drives it; data = Core IHildirContracts, host Morgott.EpicLoot; locked rows = Core
    // IUniqueBosses whose unlock key is unset). Well + knot plate header, tabs Доступные / Активные n / Выполненные, the
    // row list left, the detail card right with Взять заказ / Получить награду / Отказаться. Skin primitives of the
    // Haldor page: rows / tabs = craft.row + craft.row.selected (its store rows), buttons = button roles. Laid out in
    // 1080p px (the root's local unit = one page unit); provider reads gated to once a second (HildirRefreshGate) + one forced read per action result.
    // Driver-facing names (harness): MorgottHildirBoard, HildirTab0..2, HildirRow:<prefab>, HildirAccept / HildirClaim /
    // HildirAbandon (interactable = enabled).
    internal sealed class HildirBoard
    {
        public const string RootName = "MorgottHildirBoard";
        private const string HildirName = "$npc_hildir";
        private const float Refresh = 1f;
        private const float Pad = 16f, HeadH = 52f, TabH = 48f, RowH = 92f, RowGap = 8f, ButtonH = 86f, ButtonText = 24f;
        private static readonly string[] TabTokens = { "$ui_hildir_tab_open", "$ui_hildir_tab_active", "$ui_hildir_tab_done" };
        private static readonly Func<string, bool> KeySet = k => ZoneSystem.instance && ZoneSystem.instance.GetGlobalKey(k);

        private sealed class Tab
        {
            public Button Button;
            public Image Sel;
            public Image[] Edge;
            public TMP_Text Label;
        }

        private sealed class RowView
        {
            public RectTransform Rt;
            public CanvasGroup Group;
            public Image Sel, Socket, Icon, Coin, Token, Lock;
            public Image[] ChipEdge;
            public TMP_Text Name, Biome, Coins, Gold, Chip;
            public RectTransform ChipPlate;
            public HildirRow Data;
        }

        private sealed class InfoRow
        {
            public RectTransform Rt;
            public Image Glyph;
            public Image[] Edge;
            public TMP_Text Text;
        }

        private readonly RectTransform _parent;
        private RectTransform _root, _card;
        private Image _plate, _rule, _dSocket, _dIcon, _rCoin, _rToken;
        private TMP_Text _title, _empty, _dName, _dSub, _dDesc, _rGold, _rSep;
        private readonly Tab[] _tabs = new Tab[HildirBoardMath.TabCount];
        private readonly List<RowView> _rows = new List<RowView>(4);
        private readonly InfoRow[] _info = new InfoRow[4]; // Где / Условие / Награда / Трофей
        private Button _accept, _claim, _abandon;
        private readonly List<HildirRow>[] _data = { new List<HildirRow>(), new List<HildirRow>(), new List<HildirRow>() };
        private readonly string[] _selected = new string[HildirBoardMath.TabCount];
        private readonly Dictionary<string, (Sprite Icon, string Name)> _trophies = new Dictionary<string, (Sprite, string)>();
        private Sprite _coinIcon, _tokenIcon;
        private IHildirContracts _c;
        private int _tab;
        private readonly HildirRefreshGate _gate = new HildirRefreshGate(Refresh);
        private bool _busy, _view = true; // _busy: an accept awaits its callback (no second one meanwhile)
        private Vector2 _size;

        public HildirBoard(RectTransform parent) => _parent = parent;

        // Hildir's store with the contract host present.
        public static bool For(StoreGui gui, out IHildirContracts contracts)
        {
            contracts = null;
            return gui && gui.m_trader && gui.m_trader.m_name == HildirName && Services.TryGet(out contracts) && contracts != null;
        }

        public void Hide()
        {
            if (_root && _root.gameObject.activeSelf) _root.gameObject.SetActive(false);
            _busy = false; // a callback lost with the page must not lock Accept
        }

        // Each frame while the page shows Hildir: the root on `right` (canvas box) at the page unit u; data / view rebuilt
        // only when due.
        public void Layout(RectTransform canvas, Box right, float u, IHildirContracts contracts)
        {
            if (!_parent || !canvas || right.IsEmpty || u <= 0f) return;
            _c = contracts;
            if (!_root) Build();
            if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);
            var pu = Mathf.Abs(_parent.lossyScale.x / Mathf.Max(1e-6f, canvas.lossyScale.x));
            Geo.SetScale(_root, u / Mathf.Max(1e-6f, pu));
            Geo.Place(canvas, _root, right);
            var size = _root.rect.size;
            if ((size - _size).sqrMagnitude > 0.25f)
            {
                _size = size;
                _view = true;
            }
            if (_gate.Take(Time.unscaledTime))
            {
                Load();
                _view = true;
            }
            if (!_view) return;
            _view = false;
            Arrange(canvas, u);
        }

        private void Load()
        {
            var bosses = Services.TryGet<IUniqueBosses>(out var ub) ? ub?.Bosses : null;
            HildirBoardMath.Partition(_c?.Available(), _c?.Active(), bosses, KeySet, _data);
        }

        private HildirRow Selected()
        {
            var rows = _data[_tab];
            var i = HildirBoardMath.Select(rows, _selected[_tab]);
            return i >= 0 ? rows[i] : null;
        }

        // ---- build (once)

        private void Build()
        {
            _root = new GameObject(RootName, typeof(RectTransform)).GetComponent<RectTransform>();
            _root.SetParent(_parent, false);
            _root.SetAsLastSibling();
            _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(0f, 1f);
            _plate = InfoArt.OwnImage(_root, "Plate", "decor.plaque", "hildir board header plate (mockup 2026-11-24)");
            _plate.preserveAspect = false;
            _title = Txt(_plate.transform, "Label", "text.header", TextAlignmentOptions.Center, 30f);
            _title.fontStyle = FontStyles.UpperCase;
            _title.text = L("$ui_hildir_board");
            for (var i = 0; i < _tabs.Length; i++)
            {
                var n = i;
                var b = Btn(_root, "HildirTab" + i, "craft.row", () => { _tab = n; _view = true; });
                var t = new Tab { Button = b, Sel = Img(b.transform, "Sel", "craft.row.selected") };
                t.Label = Txt(b.transform, "Label", "text.body", TextAlignmentOptions.Center, 24f);
                t.Label.fontStyle = FontStyles.UpperCase;
                t.Edge = Edge(b.transform);
                _tabs[i] = t;
            }
            _rule = Img(_root, "TabRule", "ib.rule"); // mockup: a divider between the tabs and the list
            _rule.preserveAspect = false;
            _empty = Txt(_root, "Empty", "text.muted", TextAlignmentOptions.Center, 22f);
            _empty.textWrappingMode = TextWrappingModes.Normal;
            _card = Img(_root, "Card", "well").rectTransform;
            _dSocket = Img(_card, "Socket", "socket.round");
            _dIcon = Icon(_dSocket.transform, "Icon");
            _dName = Txt(_card, "Name", "text.ember", TextAlignmentOptions.MidlineLeft, 42f);
            _dName.fontStyle = FontStyles.Bold;
            _dSub = Txt(_card, "Subtitle", "text.muted", TextAlignmentOptions.MidlineLeft, 24f);
            _dDesc = Txt(_card, "Description", "text.body", TextAlignmentOptions.TopLeft, 20f);
            _dDesc.textWrappingMode = TextWrappingModes.Normal;
            _dDesc.overflowMode = TextOverflowModes.Truncate;
            string[] glyphs = { "glyph.board.treasure", "toggle.check", "content", "ib.cat.trophies" };
            for (var i = 0; i < _info.Length; i++)
            {
                var plate = Img(_card, "Info" + i, "craft.row");
                var r = new InfoRow { Rt = plate.rectTransform, Glyph = glyphs[i] == "content" ? Icon(plate.transform, "Glyph") : Img(plate.transform, "Glyph", glyphs[i]) };
                r.Text = Txt(plate.transform, "Text", "text.body", TextAlignmentOptions.MidlineLeft, 20f);
                r.Text.enableAutoSizing = true;
                r.Text.fontSizeMin = 14f;
                r.Text.fontSizeMax = 20f;
                r.Edge = Edge(plate.transform);
                _info[i] = r;
            }
            var reward = _info[2].Rt;
            _rSep = Txt(reward, "Sep", "text.muted", TextAlignmentOptions.Center, 22f);
            _rSep.text = "|";
            _rToken = Icon(reward, "Token");
            _rGold = Txt(reward, "Gold", "text.body", TextAlignmentOptions.MidlineLeft, 20f);
            _rCoin = _info[2].Glyph;
            _accept = Btn(_card, "HildirAccept", "button.primary", Accept, "$ui_hildir_accept");
            _claim = Btn(_card, "HildirClaim", "button", Claim, "$ui_hildir_claim");
            _abandon = Btn(_card, "HildirAbandon", "button", Abandon, "$ui_hildir_abandon");
        }

        private RowView MakeRow()
        {
            var back = Img(_root, "HildirRow", "craft.row");
            back.raycastTarget = true;
            var rv = new RowView { Rt = back.rectTransform, Group = back.gameObject.AddComponent<CanvasGroup>() };
            var b = back.gameObject.AddComponent<Button>();
            b.targetGraphic = back;
            b.navigation = new Navigation { mode = Navigation.Mode.Automatic }; // gamepad walks rows / tabs / actions spatially (InfoTabs, vanilla store prefab default)
            b.onClick.AddListener(() =>
            {
                if (rv.Data == null) return;
                _selected[_tab] = rv.Data.Prefab;
                _view = true;
            });
            Restyle(back, "craft.row");
            rv.Sel = Img(back.transform, "Sel", "craft.row.selected");
            rv.Socket = Img(back.transform, "Socket", "socket.round");
            Rect(rv.Socket.rectTransform, 8f, 8f, 76f, 76f);
            rv.Icon = Icon(rv.Socket.transform, "Icon");
            Rect(rv.Icon.rectTransform, 10f, 10f, 56f, 56f);
            // Mockup row hierarchy (Codex review): bold target names, larger amounts / chips.
            rv.Name = Txt(back.transform, "Name", "text.ember", TextAlignmentOptions.MidlineLeft, 30f);
            rv.Name.fontStyle = FontStyles.Bold;
            rv.Biome = Txt(back.transform, "Biome", "text.muted", TextAlignmentOptions.MidlineLeft, 20f);
            rv.Coin = Icon(back.transform, "Coin");
            rv.Coins = Txt(back.transform, "Coins", "text.body", TextAlignmentOptions.MidlineRight, 26f);
            rv.Token = Icon(back.transform, "Token");
            rv.Gold = Txt(back.transform, "Gold", "text.body", TextAlignmentOptions.MidlineRight, 26f);
            rv.Lock = Img(back.transform, "Lock", "glyph.ench.lock");
            var chip = Img(back.transform, "Chip", "craft.row");
            rv.ChipPlate = chip.rectTransform;
            rv.Chip = Txt(chip.transform, "Label", "text.body", TextAlignmentOptions.Center, 20f);
            rv.Chip.fontStyle = FontStyles.UpperCase;
            rv.ChipEdge = Edge(chip.transform);
            _rows.Add(rv);
            return rv;
        }

        // ---- layout + fill (on data / size / selection change)

        private void Arrange(RectTransform canvas, float u)
        {
            var w = _size.x;
            var h = _size.y;
            var head = new Box(Pad, Pad, w - 2f * Pad, HeadH);
            Rect(_plate.rectTransform, head);
            InfoPage.Ends(canvas, _plate, Mathf.Min(head.H * 2.6f, head.W * 0.22f) * u);
            Rect(_title.rectTransform, new Box(0f, 0f, head.W, head.H));
            // Tabs: three equal plates under the header (mockup: inset from the well's sides).
            var ty = head.Bottom + 12f;
            var tx = Pad + 32f;
            var tw = (w - 2f * tx - 2f * 24f) / 3f;
            for (var i = 0; i < _tabs.Length; i++)
            {
                var t = _tabs[i];
                Rect((RectTransform)t.Button.transform, new Box(tx + i * (tw + 24f), ty, tw, TabH));
                Rect(t.Label.rectTransform, new Box(8f, 0f, tw - 16f, TabH));
                Rect(t.Sel.rectTransform, new Box(0f, 0f, tw, TabH));
                var label = L(TabTokens[i]);
                if (i == 1 && _data[1].Count > 0) label += "  " + _data[1].Count;
                Set(t.Label, label);
                On(t.Sel, i == _tab);
                Edge(t.Edge, tw, TabH, i != _tab); // the selected tab keeps its glow instead
            }
            // Mockup: a rule ~20 px under the tabs, the body ~20 px under it.
            var bw = w - 2f * Pad;
            Rect(_rule.rectTransform, new Box(Pad, ty + TabH + 18f, bw, EdgeW));
            Tint(_rule, EdgeColor);
            // Body: row list left (mockup share 680 / 1290), detail card right.
            var by = ty + TabH + 38f;
            var lw = Mathf.Round(bw * 0.527f);
            var list = new Box(Pad, by, lw, Mathf.Max(0f, h - Pad - by));
            var card = new Box(list.Right + 12f, by, Mathf.Max(0f, w - Pad - list.Right - 12f), list.H);
            var rows = _data[_tab];
            var sel = Selected();
            if (sel != null) _selected[_tab] = sel.Prefab;
            // shortcut: no scrolling, the catalog holds 3 bosses (6 rows fit); add a scroll view if it grows past ~6.
            while (_rows.Count < rows.Count) MakeRow();
            for (var i = 0; i < _rows.Count; i++)
            {
                var rv = _rows[i];
                var on = i < rows.Count && list.Y + i * (RowH + RowGap) + RowH <= list.Bottom + 0.5f;
                On(rv.Rt, on);
                rv.Data = on ? rows[i] : null;
                if (on) FillRow(rv, new Box(list.X, list.Y + i * (RowH + RowGap), list.W, RowH), rows[i], rows[i] == sel);
            }
            On(_empty, rows.Count == 0);
            if (rows.Count == 0)
            {
                Rect(_empty.rectTransform, new Box(list.X + 24f, list.Y, list.W - 48f, Mathf.Min(list.H, 3f * RowH)));
                Set(_empty, L("$ui_hildir_empty_" + _tab));
            }
            Rect(_card, card);
            FillCard(card.W, card.H, sel);
        }

        private void FillRow(RowView rv, Box box, HildirRow r, bool selected)
        {
            var name = "HildirRow:" + r.Prefab;
            if (rv.Rt.name != name) rv.Rt.name = name;
            Rect(rv.Rt, box);
            Rect(rv.Sel.rectTransform, new Box(0f, 0f, box.W, box.H));
            On(rv.Sel, selected);
            var locked = r.Chip == HildirChip.Locked;
            var alpha = locked ? 0.55f : 1f;
            if (!Mathf.Approximately(rv.Group.alpha, alpha)) rv.Group.alpha = alpha;
            Portrait(rv.Icon, r.Prefab);
            Tint(rv.Socket, BossRim);
            Tint(rv.Biome, Secondary);
            // Right to left: chip, (lock), token amount, coin amount.
            var chipW = 130f;
            var x = box.W - 10f - chipW;
            Rect(rv.ChipPlate, new Box(x, (box.H - 36f) / 2f, chipW, 36f));
            Rect(rv.Chip.rectTransform, new Box(4f, 0f, chipW - 8f, 36f));
            Edge(rv.ChipEdge, chipW, 36f, true);
            Set(rv.Chip, L(HildirBoardMath.ChipToken(r.Chip)));
            var accent = r.Chip == HildirChip.Taken || r.Chip == HildirChip.Done;
            var chipColor = accent ? ThemeTokens.GetColor("color.text.accent", Color.cyan) : ThemeTokens.GetColor("color.text.body", Color.white);
            if (rv.Chip.color != chipColor) rv.Chip.color = chipColor;
            On(rv.Lock, locked);
            if (locked)
            {
                x -= 8f + 28f;
                Rect(rv.Lock.rectTransform, new Box(x, (box.H - 28f) / 2f, 28f, 28f));
            }
            foreach (var g in new Component[] { rv.Coin, rv.Coins, rv.Token, rv.Gold }) On(g, !locked);
            if (!locked)
            {
                x = Amount(rv.Token, rv.Gold, Token(), r.Gold.ToString(), "00", x - 14f, box.H);
                x = Amount(rv.Coin, rv.Coins, Coin(), r.Coins.ToString(), "0000", x - 14f, box.H);
            }
            var nx = 96f;
            var nw = Mathf.Max(0f, x - 8f - nx);
            Set(rv.Name, Name(r));
            Set(rv.Biome, L(HildirBoardMath.BiomeToken(r.Biome)));
            var nh = LineH(rv.Name, 40f);
            Rect(rv.Name.rectTransform, new Box(nx, 6f, nw, nh));
            Rect(rv.Biome.rectTransform, new Box(nx, 6f + nh, nw, Mathf.Max(24f, box.H - 12f - nh)));
        }

        // One line's box at the text's own size: a box shorter than the line drops it under Ellipsis (the 42 px card name
        // in a 50 px box vanished, Codex review 8/10).
        private static float LineH(TMP_Text t, float min) => Mathf.Max(min, Mathf.Ceil(t.GetPreferredValues(t.text).y));

        // An icon + right-aligned amount ending at `right`, the column as wide as `widest` (icons line up across rows: "1200"
        // vs "270"); the new right edge (its left).
        private static float Amount(Image icon, TMP_Text text, Sprite sprite, string value, string widest, float right, float rowH)
        {
            Set(text, value);
            var tw = Mathf.Ceil(Mathf.Max(text.GetPreferredValues(widest).x, text.GetPreferredValues(value).x)) + 2f;
            var th = LineH(text, 36f);
            Rect(text.rectTransform, new Box(right - tw, (rowH - th) / 2f, tw, th));
            var ix = right - tw - 4f - 30f;
            Rect(icon.rectTransform, new Box(ix, (rowH - 30f) / 2f, 30f, 30f));
            SetSprite(icon, sprite);
            return ix;
        }

        private void FillCard(float w, float h, HildirRow r)
        {
            var has = r != null;
            foreach (Transform c in _card) On(c, has || c == _accept.transform || c == _claim.transform || c == _abandon.transform);
            Rect(_dSocket.rectTransform, new Box(16f, 16f, 140f, 140f));
            Rect(_dIcon.rectTransform, new Box(18f, 18f, 104f, 104f));
            Tint(_dSocket, BossRim);
            Tint(_dSub, Secondary);
            var locked = has && r.Chip == HildirChip.Locked;
            if (has)
            {
                Portrait(_dIcon, r.Prefab);
                Set(_dName, Name(r));
                var key = HildirBoardMath.TextKey(r.Prefab);
                Set(_dSub, Text("$ui_hildir_sub_" + key, "$ui_hildir_sub_generic"));
                Set(_dDesc, Text("$ui_hildir_desc_" + key, "$ui_hildir_desc_generic"));
                // Mockup: large name, subtitle, description stacked right of the portrait.
                var tx = 172f;
                var tw = Mathf.Max(0f, w - tx - 16f);
                var nh = LineH(_dName, 50f);
                var sh = LineH(_dSub, 32f);
                Rect(_dName.rectTransform, new Box(tx, 10f, tw, nh));
                Rect(_dSub.rectTransform, new Box(tx, 10f + nh, tw, sh));
                var dy = 14f + nh + sh;
                Rect(_dDesc.rectTransform, new Box(tx, dy, tw, Mathf.Max(0f, 230f - dy)));
                var biome = L(HildirBoardMath.BiomeToken(r.Biome));
                Set(_info[0].Text, L("$ui_hildir_where") + ": " + (biome.Length > 0 ? biome + ", " : "") + L("$ui_hildir_where_pin"));
                Set(_info[1].Text, L("$ui_hildir_cond") + ": " + L("$ui_hildir_cond_chest"));
                var lockGlyph = ThemeTokens.Role(locked ? "glyph.ench.lock" : "toggle.check");
                if (lockGlyph != null && lockGlyph.Sprite && _info[1].Glyph.sprite != lockGlyph.Sprite) _info[1].Glyph.sprite = lockGlyph.Sprite;
                var trophy = Trophy(r.Prefab);
                Set(_info[3].Text, HildirBoardMath.LabelLine(L("$ui_hildir_trophy"), trophy.Name ?? Name(r)));
            }
            // Info rows (the reward row hidden for a locked boss).
            // Mockup rhythm: the info block lower, the buttons raised toward it (run 20261124-113939: ~150 px gap vs ~54).
            var y = 242f;
            for (var i = 0; i < _info.Length; i++)
            {
                var ir = _info[i];
                var on = has && !(i == 2 && locked);
                On(ir.Rt, on);
                if (!on) continue;
                var rw = w - 24f;
                Rect(ir.Rt, new Box(12f, y, rw, 48f));
                Edge(ir.Edge, rw, 48f, true);
                Rect(ir.Glyph.rectTransform, new Box(14f, 9f, 30f, 30f));
                if (i == 2) Reward(r, rw);
                else Rect(ir.Text.rectTransform, new Box(58f, 0f, rw - 70f, 48f));
                y += 56f;
            }
            // Buttons along the card bottom (mockup: 86 px plates, two-line labels where one line does not fit).
            var bw = (w - 24f - 2f * 10f) / 3f;
            var by = h - 28f - ButtonH;
            HildirBoardMath.Buttons(r, out var accept, out var claim, out var abandon);
            Place(_accept, new Box(12f, by, bw, ButtonH), accept && !_busy, "$ui_hildir_accept");
            Place(_claim, new Box(12f + bw + 10f, by, bw, ButtonH), claim, "$ui_hildir_claim");
            Place(_abandon, new Box(12f + 2f * (bw + 10f), by, bw, ButtonH), abandon, "$ui_hildir_abandon");
        }

        // "Награда: <coins>  |  <token> <gold>" with the item icons (coin icon = the row glyph).
        private void Reward(HildirRow r, float rw)
        {
            SetSprite(_rCoin, Coin());
            var t = _info[2].Text;
            var s = L("$ui_hildir_reward") + ":  " + r.Coins;
            Set(t, s);
            var x = 58f;
            var w = Mathf.Ceil(t.GetPreferredValues(s).x) + 2f;
            Rect(t.rectTransform, new Box(x, 0f, w, 48f));
            x += w + 12f;
            Rect(_rSep.rectTransform, new Box(x, 0f, 16f, 48f));
            x += 16f + 12f;
            Rect(_rToken.rectTransform, new Box(x, 9f, 30f, 30f));
            SetSprite(_rToken, Token());
            x += 34f;
            var g = r.Gold.ToString();
            Set(_rGold, g);
            Rect(_rGold.rectTransform, new Box(x, 0f, Mathf.Max(0f, rw - x - 8f), 48f));
        }

        private static void Place(Button b, Box box, bool enabled, string token)
        {
            Rect((RectTransform)b.transform, box);
            if (b.interactable != enabled) b.interactable = enabled;
            if (b.transform.Find("Label") is RectTransform l && l.TryGetComponent<TMP_Text>(out var t))
            {
                // The plate caps' inset as the plate label fit computes it (button.label fitInset, share of the width).
                var inset = LabelFit.Inset(ThemeTokens.Role("button.label")?.FitInset ?? 40f, box.W, ThemeTokens.GetFloat("share.text.fit.inset", 0.12f));
                Rect(l, new Box(inset, 0f, Mathf.Max(0f, box.W - 2f * inset), box.H));
                var c = ThemeTokens.GetColor(enabled ? "color.text.body" : "color.text.muted", enabled ? Color.white : Color.gray);
                if (t.color != c) t.color = c;
                // Mockup: a two-word label on two lines at the full size (HildirBoardMath.ButtonLabel). Measured at the current
                // size and scaled (widths are linear in the size): no size flip, no mesh rebuild while nothing changed.
                var s = L(token);
                var k = ButtonText / Mathf.Max(1f, t.fontSize);
                var shown = HildirBoardMath.ButtonLabel(s, (i, j) => k * t.GetPreferredValues(s.Substring(i, j - i), float.PositiveInfinity, float.PositiveInfinity).x,
                    Mathf.Max(0f, box.W - 2f * inset), ButtonText, out var size);
                Set(t, shown);
                if (Mathf.Abs(t.fontSize - size) > 0.05f) t.fontSize = size;
            }
        }

        // ---- actions

        private void Accept()
        {
            var r = Selected();
            if (_busy || _c == null || r?.Id == null || r.Chip != HildirChip.Open) return;
            _busy = true;
            _view = true;
            _c.Accept(r.Id, ok =>
            {
                if (!ok && MessageHud.instance) MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, L("$ui_hildir_accept_failed"));
                _busy = false;
                _view = true;
                _gate.Force(); // the one immediate read this action owes
            });
        }

        private void Claim()
        {
            var r = Selected();
            if (_c == null || r?.Id == null || r.Chip != HildirChip.Done) return;
            _c.Claim(r.Id);
            _gate.Force();
        }

        private void Abandon()
        {
            var r = Selected();
            if (_c == null || r?.Id == null || r.Chip != HildirChip.Taken) return;
            var id = r.Id;
            var c = _c;
            UnifiedPopup.Push(new YesNoPopup(Name(r), "$ui_hildir_abandon_confirm", () =>
            {
                UnifiedPopup.Pop();
                c.Abandon(id);
                _gate.Force();
            }, UnifiedPopup.Pop));
        }

        // ---- game data

        private static string L(string token) => string.IsNullOrEmpty(token) ? "" : Localization.instance != null ? Localization.instance.Localize(token) : token;

        // The boss's own LOC text, else the generic one.
        private static string Text(string token, string fallback)
        {
            var s = L(token);
            return HildirBoardMath.Missing(s) ? L(fallback) : s;
        }

        private static string Name(HildirRow r)
        {
            var n = string.IsNullOrEmpty(r.NameToken) ? null : L(r.NameToken);
            return string.IsNullOrEmpty(n) || HildirBoardMath.Missing(n) ? r.Prefab : n;
        }

        // The boss's trophy (its CharacterDrop drop of ItemType.Trophy): icon + localized name; cached per prefab.
        private (Sprite Icon, string Name) Trophy(string prefab)
        {
            if (prefab == null) return default;
            if (_trophies.TryGetValue(prefab, out var t)) return t;
            var go = ZNetScene.instance ? ZNetScene.instance.GetPrefab(prefab) : null;
            if (!go) return default; // not cached: the scene may not be up yet
            if (go.TryGetComponent<CharacterDrop>(out var drops))
                foreach (var d in drops.m_drops)
                {
                    var shared = d.m_prefab && d.m_prefab.TryGetComponent<ItemDrop>(out var item) ? item.m_itemData.m_shared : null;
                    if (shared == null || shared.m_itemType != ItemDrop.ItemData.ItemType.Trophy || shared.m_icons.Length == 0) continue;
                    t = (shared.m_icons[0], L(shared.m_name));
                    break;
                }
            _trophies[prefab] = t;
            return t;
        }

        // Trophy icon, else the trophies category glyph (vanilla-art fallback already on the item browser).
        private void Portrait(Image icon, string prefab)
        {
            var s = Trophy(prefab).Icon;
            if (!s) s = ThemeTokens.Role("ib.cat.trophies")?.Sprite;
            SetSprite(icon, s);
        }

        private Sprite Coin() => _coinIcon ? _coinIcon : _coinIcon = ItemIcon("Coins");
        private Sprite Token() => _tokenIcon ? _tokenIcon : _tokenIcon = ItemIcon("GoldBountyToken");

        private static Sprite ItemIcon(string item)
        {
            var go = ObjectDB.instance ? ObjectDB.instance.GetItemPrefab(item) : null;
            return go && go.TryGetComponent<ItemDrop>(out var d) ? d.m_itemData.GetIcon() : null;
        }

        // ---- widget helpers (InfoArt / BrowserPanel conventions)

        private static Image Img(Transform parent, string name, string role) => InfoArt.OwnImage(parent, name, role, "hildir board " + name);

        private static Image Icon(Transform parent, string name)
        {
            var image = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
            image.rectTransform.SetParent(parent, false);
            image.raycastTarget = false;
            image.preserveAspect = true;
            DollCell.Mark(image, "content", "hildir board item icon");
            return image;
        }

        private static TMP_Text Txt(Transform parent, string name, string role, TextAlignmentOptions align, float size)
        {
            var t = InfoArt.RowText(parent, name, role, align);
            OwnFit.Claim(t);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.enableAutoSizing = false;
            t.fontSize = size;
            return t;
        }

        private Button Btn(Transform parent, string name, string role, Action click, string label = null)
        {
            var image = Img(parent, name, role);
            image.preserveAspect = false;
            image.raycastTarget = true;
            var b = image.gameObject.AddComponent<Button>();
            b.targetGraphic = image;
            b.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            b.onClick.AddListener(() => click());
            // Styled after the Button exists: the role's sprite states go on its targetGraphic (BrowserPanel.Btn).
            Restyle(image, role);
            if (label != null)
            {
                // text.body = button.label's face + colour without its plate fit (PlateLabelFit / TextSizeGroup would turn
                // auto-size back on): Place owns the cap inset, line break and size.
                var t = Txt(image.transform, "Label", "text.body", TextAlignmentOptions.Center, ButtonText);
                t.fontStyle = FontStyles.UpperCase;
                t.text = L(label);
            }
            return b;
        }

        private static void Restyle(Image image, string role)
        {
            if (image.GetComponent<SkinMark>() is SkinMark m) m.Outcome = Restyler.Apply(image, m, ThemeTokens.Role(role)) ? Outcome.Styled : Outcome.Degraded;
        }

        // Colour tokens on top of the roles (mockup 2026-11-24, Codex review 7.5/10): amber rim on the boss portrait rings,
        // brighter secondary text than text.muted, teal-grey plate edges.
        private const string BossRim = "color.text.active", Secondary = "color.stats.sub", EdgeColor = "color.ib.rule";
        private const float EdgeW = 2f;

        private static void Tint(Graphic g, string token)
        {
            var c = ThemeTokens.GetColor(token, g.color);
            if (g.color != c) g.color = c;
        }

        // A thin outline around a plate (four ib.rule strips): unselected tabs, state chips and info rows stay readable on
        // the dark well.
        private static Image[] Edge(Transform plate)
        {
            var e = new Image[4];
            for (var i = 0; i < e.Length; i++)
            {
                e[i] = Img(plate, "Edge" + i, "ib.rule");
                e[i].preserveAspect = false;
            }
            return e;
        }

        private static void Edge(Image[] e, float w, float h, bool on)
        {
            foreach (var i in e)
            {
                On(i, on);
                if (on) Tint(i, EdgeColor);
            }
            if (!on) return;
            Rect(e[0].rectTransform, 0f, 0f, w, EdgeW);
            Rect(e[1].rectTransform, 0f, h - EdgeW, w, EdgeW);
            Rect(e[2].rectTransform, 0f, 0f, EdgeW, h);
            Rect(e[3].rectTransform, w - EdgeW, 0f, EdgeW, h);
        }

        private static void Rect(RectTransform rt, Box b) => InfoArt.Rect(rt, b.X, b.Y, b.W, b.H, 0f);
        private static void Rect(RectTransform rt, float x, float y, float w, float h) => InfoArt.Rect(rt, x, y, w, h, 0f);

        private static void Set(TMP_Text t, string s)
        {
            if (t.text != s) t.text = s;
        }

        private static void SetSprite(Image i, Sprite s)
        {
            if (i.sprite != s) i.sprite = s;
            if (i.enabled != (bool)s) i.enabled = s;
        }

        private static void On(Component c, bool on)
        {
            if (c && c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
        }
    }
}
