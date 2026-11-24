using System.Collections.Generic;
using Morgott.Core;
using Morgott.UI.InfoScreens;
using Morgott.UI.InventoryScreen;
using Morgott.UI.ModScreens;
using Morgott.UI.Restyle;
using Morgott.UI.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Morgott.UI.GameScreens
{
    // Traders as a full-screen page (user 2026-11-07, approved mockup 2026-11-07/04-trader-fullscreen-el): the vanilla
    // store window (StoreGui.m_rootPanel "Store") is the page (InfoPage: opaque, HUD gated, single, Esc cap =
    // StoreGui.Hide; no inventory tabs / ITEMS key), title = the trader's name. Epic Loot never replaces the store: its
    // panels sit beside it under the StoreGui (StoreGui_Patch.Show_Finalizer) and keep all their wiring; the page only
    // re-places widgets (PagePart):
    // - left (~30 %): the vanilla stock list stretched to the column, Buy + the vanilla Sell (sells the first valuable,
    //   StoreGui.SellItem) under it; Bog Witch / traders without a mod panel: that column centred;
    // - right: Haldor's Epic Loot MerchantPanel sections in wells (frame_inset) with knot plate headers (mockup
    //   2026-11-16 trader-ui-mockup-v2): SecretStash across the top (2-column rows), Gamble | TreasureMap | Bounties
    //   below; widgets at the page unit, rows / texts sized in px; its Currencies + Sundial in the top bar; Hildir's
    //   TemperPanel fitted whole; Hildir without one + Core IHildirContracts: her contract board (HildirBoard, mockup
    //   2026-11-24 hildir-bounty-board) in the right zone;
    // - the coin purse (vanilla "coins") in the top bar, hidden where the mod panel shows its own currencies.
    public sealed class TraderPage : MonoBehaviour
    {
        private const string Gate = "tradepage";
        private static readonly string[] Sections = { "SecretStash", "Gamble", "TreasureMap", "Bounties" };

        private StoreGui _gui;
        private RectTransform _store, _canvas;
        private InfoPage _page;
        private RuneWindowHeader _header;
        private PagePart _list, _buy, _sell, _coins;
        private List<PagePart> _coinKids = new List<PagePart>();
        private RectTransform _topic;
        private readonly List<GameObject> _gated = new List<GameObject>();
        private readonly PagePart.Pane[] _wells = new PagePart.Pane[4];
        private PagePart.Pane _storeWell, _barCap;
        private readonly Image[] _plates = new Image[5]; // section header knot plates (mockup v2), the last = the store's
        private TMP_Text _goods;
        private string _goodsText;
        private HildirBoard _hildir; // Hildir's contract board in the right zone (her TemperPanel is gone, Morgott.EpicLoot 0.5.19)

        public static void Ensure(StoreGui s)
        {
            if (!s || !s.m_rootPanel || !(s.m_rootPanel.transform is RectTransform store) || store.GetComponent<TraderPage>()) return;
            var p = store.gameObject.AddComponent<TraderPage>();
            p._gui = s;
            p._store = store;
            var canvas = store.GetComponentInParent<Canvas>(true);
            p._canvas = canvas ? (RectTransform)canvas.rootCanvas.transform : null;
            p._page = InfoPage.Ensure(store, store, null, () => s.Hide());
        }

        private void OnDisable()
        {
            foreach (var go in _gated)
                if (go) RenderGate.Set(go, Gate, false);
            _gated.Clear();
            _hildir?.Hide();
        }

        private void Measure()
        {
            _list = PagePart.Of(_canvas, Child("ItemList"));
            _buy = PagePart.Of(_canvas, _gui.m_buyButton ? (RectTransform)_gui.m_buyButton.transform : null);
            _sell = PagePart.Of(_canvas, Child("SellPanel"));
            _coins = PagePart.Of(_canvas, Child("coins"));
            _coinKids = PagePart.Children(_canvas, Child("coins"));
            // The vanilla "topic" plate text: the page title shows the trader's name instead.
            _topic = Child("topic");
            for (var i = 0; i < _wells.Length; i++)
                _wells[i] = PagePart.Pane.Make(_store, "RuneSkinTradeWell" + i, "trader page board section well (mockup 04)");
            _storeWell = PagePart.Pane.Make(_store, "RuneSkinTradeStoreWell", "trader page store column well (mockup 04)");
            _barCap = PagePart.Pane.Make(_store, "RuneSkinTradeBarCap", "trader page top-bar currency cap (mockup 04)");
            // Over the wells (later siblings), under the board (a later sibling of the store).
            for (var i = 0; i < _plates.Length; i++)
            {
                _plates[i] = InfoArt.OwnImage(_store, "RuneSkinTradePlate" + i, "decor.plaque", "trader page section header plate (mockup v2)");
                _plates[i].preserveAspect = false;
            }
            _goods = InfoArt.RowText(_plates[Sections.Length].transform, "Label", "text.header", TextAlignmentOptions.Center);
            _goodsText = Localization.instance != null ? Localization.instance.Localize("$ui_trader_goods") : "Goods";
            // The stock list's content from the top of its (now taller) viewport: vanilla centred it in a fixed-height one
            // (run 20261107-145553: rows began ~240 px down).
            if (_gui.m_listRoot is RectTransform lr)
            {
                lr.anchorMin = new Vector2(lr.anchorMin.x, 1f);
                lr.anchorMax = new Vector2(lr.anchorMax.x, 1f);
                lr.pivot = new Vector2(lr.pivot.x, 1f);
                lr.anchoredPosition = new Vector2(lr.anchoredPosition.x, 0f);
            }
        }

        private RectTransform Child(string name) => _store.Find(name) as RectTransform;

        // The mod panel shown now (Haldor's board / Hildir's temper panel), null for vanilla-only traders.
        private Transform ModPanel()
        {
            foreach (Transform c in _gui.transform)
                if (c != _store && c.gameObject.activeSelf && (c.name == "MerchantPanel" || c.name == "TemperPanel")) return c;
            return null;
        }

        private void LateUpdate()
        {
            if (!_canvas || !_page || !_gui) return;
            if (_list == null && _buy == null) Measure();
            if (!_header) _header = _store.GetComponentInChildren<RuneWindowHeader>(true) ?? RuneWindowHeader.Ensure(_store, "page.store");
            // The trader's name; vanilla's own topic text where it has none (Bog Witch showed "-", run 20261107-154027).
            if (_header && _gui.m_trader)
                _header.Text(!string.IsNullOrEmpty(_gui.m_trader.m_name) ? _gui.m_trader.m_name : _topic && _topic.TryGetComponent<TMPro.TMP_Text>(out var tt) ? tt.text : "");
            if (_topic && !_gated.Contains(_topic.gameObject))
            {
                RenderGate.Set(_topic.gameObject, Gate, true);
                _gated.Add(_topic.gameObject);
            }
            // The vanilla backgrounds (their Morgott.UI panel stack = the page's opaque backing) draw first: vanilla's
            // "border (1)" is a frame drawn over the window, and as the page backing it covered the window header and the
            // Sell panel (run 20261107-143240 overdraw-tradepage covered=11).
            foreach (var n in new[] { "border (1)", "bkg" })
                if (Child(n) is RectTransform bg && bg.GetSiblingIndex() != 1) bg.SetSiblingIndex(1);
            // The window header face right after the page backing ("border (1)"; it followed "bkg" and stayed under the
            // backing, runs 20261107-144822 / -145553 overdraw covered RuneSkinWindowFace).
            if (_header) _header.DrawAfter = Child("border (1)");
            var mod = ModPanel();
            var board = mod && mod.name == "MerchantPanel";
            IHildirContracts contracts = null;
            var hildir = !mod && HildirBoard.For(_gui, out contracts);
            var currencies = mod ? mod.Find("Currencies") as RectTransform : null;
            var u = InfoPage.Full(_canvas).H / 1080f;
            // Top bar between the name banner and the Esc cap: coin purse / mod currencies + sundial (mockup 04).
            var bar = new Box(InfoPage.Full(_canvas).W - (155f + 24f + 470f) * u, 30f * u, 470f * u, 56f * u); // left of the Esc cap (InfoPage.Layout: W - 155)
            _page.NameX = 726f;
            _page.NameRightReserve = 470f + 24f + 26f;
            var inv = InventorySkin.Instance;
            var body = _page.Layout(inv ? inv.SkinScaleValue : 1f, _header);
            if (body.IsEmpty) return;
            // Mockup v2 body: x 48..1872, y 210 (under the title plaque) .. 1022.
            var full = InfoPage.Full(_canvas);
            body = new Box(48f * u, 210f * u, Mathf.Max(0f, full.W - 96f * u), Mathf.Max(0f, full.H - 268f * u));
            ElPageMath.Trader(body, mod || hildir, out var col, out var right, hildir ? ElPageMath.HildirStoreShare : ElPageMath.StoreShare);
            // Right after the page backing ("border (1)") and the header face that follows it, under the store widgets.
            if (_storeWell != null && _storeWell.Fill && (Child(RuneWindowHeader.FaceName) ?? Child("border (1)")) is RectTransform pb)
                _storeWell.First(_storeWell.Fill.transform.GetSiblingIndex() > pb.GetSiblingIndex() ? pb.GetSiblingIndex() + 1 : pb.GetSiblingIndex());
            _storeWell?.Place(_canvas, new Box(col.X - 12f * u, col.Y, col.W + 24f * u, col.H + 12f * u), true);
            // One key-cap frame behind the top-bar currencies (mockup 04 currency caps).
            _barCap?.Place(_canvas, new Box(bar.X - 8f * u, bar.Y, bar.W + 16f * u, bar.H), mod || _coins != null, fill: false); // frame only
            // Under the vanilla coin purse: the cap's frame art has a dark centre and, created after the purse, covered it (run
            // 20261107-193053: Bog Witch's coin icon + count barely visible). Epic Loot's currencies are later siblings anyway.
            if (_barCap != null && _barCap.Frame && _coins != null && _barCap.Frame.transform.GetSiblingIndex() > _coins.Rt.GetSiblingIndex())
                _barCap.First(_coins.Rt.GetSiblingIndex());
            StoreColumn(col, u);
            if (_coins != null)
            {
                Hide(_coins.Rt.gameObject, currencies);
                // Like the Epic Loot currency rows at Haldor / Hildir: its coin icon + count fitted into the bar (the bar cap is
                // the frame), its own plate off (scaled, its art sat on the screen top / read blank: runs 20261107-153302 / -154027).
                if (_coins.Rt.TryGetComponent<Image>(out var plate) && plate.enabled) plate.enabled = false;
                if (_coinKids.Count > 0) PagePart.Fit(_canvas, _coinKids, new Box(bar.X + 8f * u, bar.Y + 6f * u, bar.W - 16f * u, bar.H - 12f * u), left: true);
            }
            for (var i = board ? _wells.Length : mod || hildir ? 1 : 0; i < _wells.Length; i++) _wells[i]?.Place(_canvas, default, false);
            for (var i = 0; !board && i < Sections.Length; i++)
                if (_plates[i] && _plates[i].enabled) _plates[i].enabled = false;
            if (hildir)
            {
                _wells[0]?.Place(_canvas, right, true);
                (_hildir ??= new HildirBoard(_store)).Layout(_canvas, right, u, contracts);
                return;
            }
            _hildir?.Hide();
            if (!mod) return;
            // Drawn after the page (EL's TemperPanel moves itself to the first sibling, TemperPanel.cs:190/887/1010: under
            // the opaque store and gated by CoverCull there).
            if (mod.GetSiblingIndex() < _store.GetSiblingIndex()) mod.SetSiblingIndex(_store.GetSiblingIndex());
            if (!_mods.TryGetValue(mod, out var m)) _mods[mod] = m = Capture(mod);
            Chrome(mod);
            if (board && m.Sections.Count == Sections.Length)
            {
                // The board's own box spans the right zone first (its input blocker / dialogs follow it), then its sections.
                m.Whole?.Put(_canvas, right.W / m.Whole.Box0.W, right);
                // Mockup v2: Тайник across the top, Лотерея | Карты сокровищ | Задания below. Each widget is placed at the
                // page unit (one local unit = one 1080p px) and its rows / texts sized in px: the old uniform group fit
                // shrank rows, names and timers to ~8 px (rca: TraderPage :163).
                for (var i = 0; i < Sections.Length; i++)
                {
                    var cell = ElPageMath.BoardCell(right, i, 16f * u);
                    _wells[i]?.Place(_canvas, cell, true);
                    Section(m.SectionKids[i], i, cell, u);
                }
            }
            else if (m.Whole != null)
            {
                // Placed by its widgets (ElPageMath.AlignContent): their top on the frame's inner top (the panel box ran ~95 px
                // above them, run 20261107-154027).
                var pad = 24f * u;
                var zone = new Box(right.X + pad, right.Y + pad, right.W - 2f * pad, right.H - 2f * pad);
                var kw = m.Content.IsEmpty ? ElPageMath.Fit(m.Whole.Box0.W, m.Whole.Box0.H, zone, out var x0, out var y0)
                    : ElPageMath.AlignContent(m.Whole.Box0, m.Content, zone, out x0, out y0);
                m.Whole.Put(_canvas, kw, x0, y0);
                _wells[0]?.Place(_canvas, right, true);
            }
            // Both panels' currencies (and the board's sundial) in the top bar (mockup 04).
            // The currency icons / counts themselves (the container is far larger than its content: run 20261107-144822
            // drew them ~200 px wide) fitted into the bar.
            if (m.CurrencyParts.Count > 0)
                PagePart.Fit(_canvas, m.CurrencyParts, new Box(bar.X, bar.Y, bar.W - (m.Sundial != null ? bar.H + 8f * u : 0f), bar.H));
            m.Sundial?.Put(_canvas, bar.H / m.Sundial.Box0.H, bar.Right - bar.H, bar.Y);
        }

        private sealed class Mod
        {
            public PagePart Whole, Sundial;
            public Box Content; // union of its own widgets in the prefab layout (frame art / anchors excluded)
            public List<PagePart> CurrencyParts = new List<PagePart>();
            public readonly List<PagePart> Sections = new List<PagePart>();
            public readonly List<List<PagePart>> SectionKids = new List<List<PagePart>>();
        }

        private readonly Dictionary<Transform, Mod> _mods = new Dictionary<Transform, Mod>();

        private static float T(string token, float px) => ThemeTokens.GetFloat(token, px);

        // A part at the page unit (one local unit = u canvas units = one 1080p px) on box; null-safe.
        private void At(PagePart p, Box box, float u)
        {
            if (p != null && p.Unit > 0f) p.Put(_canvas, u / p.Unit, box);
        }

        // One board section in its cell (mockup v2): knot plate header (title; timer on line 2, Тайник: on the right of
        // one line), the list(s), the action button(s) centred at the bottom.
        private void Section(List<PagePart> kids, int i, Box cell, float u)
        {
            PagePart Kid(string n) => kids.Find(k => k.Rt && k.Rt.name == n);
            var stash = i == 0;
            var pad = 10f * u;
            ElPageMath.Section(cell, pad, (stash ? 52f : 70f) * u, 44f * u, 8f * u, out var head, out var list, out var act);
            Plate(_plates[i], head, u);
            var title = Kid("Title");
            var timer = Kid("TimeLeft");
            if (stash)
            {
                At(title, head, u);
                At(timer, new Box(head.Right - 340f * u - 24f * u, head.Y, 340f * u, head.H), u);
            }
            else
            {
                At(title, new Box(head.X, head.Y + 4f * u, head.W, 36f * u), u);
                At(timer, new Box(head.X, head.Y + 40f * u, head.W, 26f * u), u);
            }
            // >= size.text.header.min (28): below it a restyle pass re-classifies the title as body text (run 20261116-135513).
            Font(title?.Rt, Mathf.RoundToInt(T(stash ? "size.trader.title.wide" : "size.trader.title", stash ? 30f : 28f)), TextAnchor.MiddleCenter);
            Font(timer?.Rt, Mathf.RoundToInt(T("size.trader.timer", 18f)), stash ? TextAnchor.MiddleRight : TextAnchor.MiddleCenter);
            var row = T(stash ? "size.trader.row.wide" : "size.trader.row", stash ? 52f : 48f);
            if (i < 3)
            {
                var panel = Kid("Panel");
                At(panel, list, u);
                Rows(panel?.Rt, row, stash ? 2 : 1);
                Button(Kid(Sections[i] + "BuyButton"), ElPageMath.Centre(act, (stash ? 490f : 290f) * u), u);
                return;
            }
            // Задания: "Доступные" + one row well + Взять задание, "Принятые" + their list, Забрать награду + abandon (X).
            var sub = 24f * u;
            var y = list.Y;
            Box Next(float h, float gap)
            {
                var b = new Box(list.X, y, list.W, h);
                y += h + gap;
                return b;
            }
            var avail = Kid("AvailableLabel");
            At(avail, Next(sub, 2f * u), u);
            At(Kid("AvailableBountiesPanel"), Next(T("size.trader.row", 48f) * u + 8f * u, 6f * u), u);
            Button(Kid("AcceptBountyButton"), ElPageMath.Centre(Next(32f * u, 6f * u), 240f * u), u);
            var claim = Kid("ClaimLabel");
            At(claim, Next(sub, 2f * u), u);
            At(Kid("ClaimableBountiesPanel"), new Box(list.X, y, list.W, Mathf.Max(0f, list.Bottom - y)), u);
            foreach (var l in new[] { avail, claim }) Font(l?.Rt, Mathf.RoundToInt(T("size.trader.text", 18f)), TextAnchor.MiddleCenter);
            Rows(Kid("AvailableBountiesPanel")?.Rt, T("size.trader.row", 48f), 1);
            Rows(Kid("ClaimableBountiesPanel")?.Rt, T("size.trader.row.wide", 52f), 1);
            var x = act.H + 8f * u; // the abandon icon button right of the claim plate
            var claimBox = ElPageMath.Centre(new Box(act.X, act.Y, act.W - x, act.H), 300f * u);
            Button(Kid("ClaimBountyButton"), claimBox, u);
            At(Kid("AbandonBountyButton"), new Box(claimBox.Right + 8f * u, act.Y, act.H, act.H), u);
        }

        private void Button(PagePart b, Box box, float u)
        {
            At(b, box, u);
            if (b != null) Font(b.Rt.Find("Text"), Mathf.RoundToInt(T("size.trader.button", 22f)), TextAnchor.MiddleCenter);
        }

        private void Plate(Image plate, Box box, float u)
        {
            if (!plate) return;
            Geo.Place(_canvas, plate.rectTransform, box);
            InfoPage.Ends(_canvas, plate, Mathf.Min(box.H * 2.6f, box.W * 0.22f));
            if (plate.enabled != (bool)plate.sprite) plate.enabled = plate.sprite;
        }

        // A legacy Epic Loot text at a fixed px size (its prefab best-fit ran 1..16 / 1..20 in shrunken boxes).
        private static void Font(Transform t, int size, TextAnchor align)
        {
            if (!t || !t.TryGetComponent<Text>(out var text)) return;
            if (text.resizeTextForBestFit) text.resizeTextForBestFit = false;
            if (text.fontSize != size) text.fontSize = size;
            if (text.alignment != align) text.alignment = align;
        }

        // A scroll panel's rows across its width at rowH px (columns 2 = Тайник's grid, filled top-down like the mockup).
        private static void Rows(RectTransform panel, float rowH, int columns)
        {
            if (!panel || !(panel.Find("ItemList") is RectTransform list)) return;
            // Epic Loot's scrollbar is 465 px from the bottom in the prefab: along the whole panel instead.
            if (panel.Find("Scrollbar") is RectTransform bar && bar.anchorMax.y < 1f)
            {
                bar.anchorMin = new Vector2(1f, 0f);
                bar.anchorMax = new Vector2(1f, 1f);
                bar.pivot = new Vector2(1f, 0.5f);
                bar.anchoredPosition = Vector2.zero;
                bar.sizeDelta = new Vector2(8f, -8f);
            }
            if (columns > 1)
            {
                // LayoutGroup is DisallowMultipleComponent: Epic Loot's column goes before the grid comes.
                if (list.TryGetComponent<VerticalLayoutGroup>(out var v)) DestroyImmediate(v);
                if (!list.TryGetComponent<GridLayoutGroup>(out var g))
                {
                    g = list.gameObject.AddComponent<GridLayoutGroup>();
                    g.padding = new RectOffset(4, 14, 4, 4);
                    g.spacing = new Vector2(12f, 4f);
                    g.startAxis = GridLayoutGroup.Axis.Vertical;
                    g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                    g.constraintCount = columns;
                }
                var cell = new Vector2(Mathf.Max(1f, (list.rect.width - 18f - g.spacing.x * (columns - 1)) / columns), rowH);
                if ((g.cellSize - cell).sqrMagnitude > 0.25f) g.cellSize = cell;
            }
            else if (list.TryGetComponent<VerticalLayoutGroup>(out var v) && !v.childControlWidth)
            {
                v.childControlWidth = v.childForceExpandWidth = v.childControlHeight = true;
                v.childForceExpandHeight = false;
                v.padding = new RectOffset(4, 14, 4, 4);
                v.spacing = 4f;
            }
            foreach (Transform r in list)
                if (r is RectTransform rt && r.gameObject.activeSelf && r.TryGetComponent<LayoutElement>(out var le) && !Mathf.Approximately(le.minHeight, rowH))
                {
                    le.minHeight = rowH; // also this row's "laid out" mark: Epic Loot re-instantiates rows on refresh
                    Row(rt, columns > 1);
                }
        }

        // One Epic Loot row (BuyListElement / TreasureMapListElement / BountyListElement, local px): icon 36 left, name
        // 18 px, prices / rewards right-aligned at 18 px, bounty status 16 px under the name.
        private static void Row(RectTransform r, bool wide)
        {
            var text = Mathf.RoundToInt(wide ? T("size.trader.text.wide", 20f) : T("size.trader.text", 18f));
            var ics = wide ? 40f : 36f;
            foreach (var n in new[] { "MagicBG", "Icon" })
                if (r.Find(n) is RectTransform ic) Anchored(ic, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(6f + ics / 2f, 0f), new Vector2(ics, ics));
            var right = 8f;
            if (r.Find("Purchased") is RectTransform bought && bought.gameObject.activeSelf)
            {
                Font(bought, text, TextAnchor.MiddleRight);
                Zone(bought, 96f, right);
                right += 104f;
            }
            foreach (var n in new[] { "Price", "Rewards" })
                if (r.Find(n) is RectTransform price && price.TryGetComponent<HorizontalLayoutGroup>(out var h))
                {
                    var w = Amounts(price, h, text);
                    Zone(price, w, right);
                    right += w + 10f;
                }
            var status = r.Find("Status") as RectTransform;
            var two = status && status.gameObject.activeSelf;
            if (r.Find("Name") is RectTransform name)
            {
                Font(name, text, TextAnchor.MiddleLeft);
                if (name.TryGetComponent<Text>(out var nt))
                {
                    nt.verticalOverflow = VerticalWrapMode.Truncate;
                    // Treasure map rows: "<биом> #N" (mockup v2); the panel title already says "Карты сокровищ" and the
                    // tooltip keeps the full name. Epic Loot writes the name once per row (TreasureMapListElement.SetItem).
                    var cut = r.Find("Purchased") ? nt.text.LastIndexOf(": ", System.StringComparison.Ordinal) : -1;
                    if (cut >= 0) nt.text = nt.text.Substring(cut + 2);
                }
                Span(name, two ? 0.5f : 0f, 1f, ics + 16f, right, 0f, two ? 2f : 0f);
            }
            if (!two) return;
            Span(status, 0f, 0.5f, 52f, right, 2f, 0f);
            if (status.TryGetComponent<HorizontalLayoutGroup>(out var sh)) sh.childAlignment = TextAnchor.MiddleLeft;
            var size = Mathf.RoundToInt(T("size.trader.status", 16f));
            foreach (Transform l in status)
                if (l.TryGetComponent<Text>(out var lt))
                {
                    Font(l, size, TextAnchor.MiddleLeft);
                    lt.horizontalOverflow = HorizontalWrapMode.Overflow;
                    lt.verticalOverflow = VerticalWrapMode.Overflow;
                    ((RectTransform)l).sizeDelta = new Vector2(Mathf.Ceil(lt.preferredWidth) + 2f, 22f);
                }
        }

        // A row's price / reward strip (icon 20 + amount per currency shown), packed right; its width.
        private static float Amounts(RectTransform strip, HorizontalLayoutGroup h, int size)
        {
            h.childAlignment = TextAnchor.MiddleRight;
            h.spacing = 10f;
            var w = 0f;
            var n = 0;
            foreach (Transform e in strip)
            {
                if (!e.gameObject.activeSelf) continue;
                // The bounty "Награды:" caption (RewardLabel) is not in the mockup v2 row (icons + amounts only; the tooltip
                // lists the rewards) and squeezed the target name out of the row (run 20261116-143533-2).
                if (e.TryGetComponent<Text>(out _))
                {
                    e.gameObject.SetActive(false);
                    continue;
                }
                if (!(e.Find("Amount") is RectTransform a) || !a.TryGetComponent<Text>(out var amount)) continue;
                Font(a, size, TextAnchor.MiddleLeft);
                amount.horizontalOverflow = HorizontalWrapMode.Overflow;
                // Epic Loot's Truncate drops the whole line once 20 px glyphs outgrow the 24 px strip (run 20261116-151458).
                amount.verticalOverflow = VerticalWrapMode.Overflow;
                if (e.Find("Icon") is RectTransform ic) Anchored(ic, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(20f, 20f));
                a.anchorMin = Vector2.zero;
                a.anchorMax = Vector2.one;
                a.offsetMin = new Vector2(24f, 0f);
                a.offsetMax = Vector2.zero;
                var ew = 24f + Mathf.Ceil(amount.preferredWidth) + 2f;
                ((RectTransform)e).sizeDelta = new Vector2(ew, 24f);
                w += ew;
                n++;
            }
            return w + Mathf.Max(0, n - 1) * h.spacing;
        }

        private static void Anchored(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        // Full row height, w wide, `right` px from the row's right edge.
        private static void Zone(RectTransform rt, float w, float right)
        {
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-right, 0f);
            rt.sizeDelta = new Vector2(w, 0f);
        }

        // Vanilla's stock rows across the list at size.trader.store.row px: the template takes the size + layout, the
        // pitch is vanilla's own m_itemSpacing, and FillList re-clones the rows (StoreGui.FillList centres a row by
        // (list width - row width) / 2, so the margin stays even).
        private void StoreRows()
        {
            if (!_gui.m_listElement || !(_gui.m_listElement.transform is RectTransform t) || !_gui.m_listRoot || !_gui.m_trader) return;
            var h = T("size.trader.store.row", 80f);
            var w = _gui.m_listRoot.rect.width - 16f;
            if (w <= 1f || (Mathf.Abs(t.rect.width - w) < 0.5f && Mathf.Abs(t.rect.height - h) < 0.5f)) return;
            t.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, w);
            t.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
            _gui.m_itemSpacing = h + 8f;
            var size = T("size.trader.store.text", 24f);
            if (t.Find("icon") is RectTransform icon) Anchored(icon, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(40f, 0f), new Vector2(56f, 56f));
            var coin = t.Find("coin_bkg") as RectTransform;
            if (coin)
            {
                Anchored(coin, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-14f, 0f), new Vector2(110f, 32f));
                foreach (var img in coin.GetComponentsInChildren<Image>(true))
                    if (img.transform != coin) Anchored(img.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(26f, 26f));
            }
            foreach (var n in new[] { "name", "price" })
                if (Utils.FindChild(t, n) is Transform c && c.TryGetComponent<TMP_Text>(out var label))
                {
                    OwnFit.Claim(label); // fixed px size; the global row inset rule would re-fit it
                    var rt = label.rectTransform;
                    if (n == "name") Span(rt, 0f, 1f, 84f, coin ? 132f : 14f, 0f, 0f);
                    else Span(rt, 0f, 1f, 32f, 0f, 0f, 0f);
                    if (label.enableAutoSizing) label.enableAutoSizing = false;
                    label.fontSize = size;
                    label.textWrappingMode = TextWrappingModes.NoWrap;
                    label.overflowMode = TextOverflowModes.Ellipsis;
                    label.alignment = TextAlignmentOptions.MidlineLeft; // the number right after its coin (mockup v2)
                }
            _gui.FillList();
        }

        // Between x left and `right` from the right edge, over the row's y band [y0, y1] (shares), insets in px.
        private static void Span(RectTransform rt, float y0, float y1, float left, float right, float bottom, float top)
        {
            rt.anchorMin = new Vector2(0f, y0);
            rt.anchorMax = new Vector2(1f, y1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        private void StoreColumn(Box col, float u)
        {
            var row = 56f * u;
            var gap = 12f * u;
            // "ТОВАРЫ" header plate (mockup v2), the list under it.
            var head = new Box(col.X, col.Y, col.W, 52f * u);
            Plate(_plates[Sections.Length], head, u);
            if (_goods)
            {
                if (_goods.text != _goodsText) _goods.text = _goodsText;
                Geo.Place(_canvas, _goods.rectTransform, head);
                InfoArt.Label(_goods, TextAlignmentOptions.Center, T("size.trader.title.wide", 30f) * u, 16f * u, upper: true);
            }
            var top = head.Bottom + 8f * u;
            var listBox = new Box(col.X, top, col.W, Mathf.Max(0f, col.Bottom - row - gap - top));
            // At the page unit: rows are sized in px (StoreRows), not scaled.
            var kl = _list != null ? u / _list.Unit : 1f;
            _list?.Put(_canvas, kl, listBox);
            // Rows from the column top: vanilla's list viewport sits ~150 px into the ItemList part (run 20261107-152301);
            // the part moves up by that, its bottom stays.
            if (_list != null && _gui.m_listRoot && _gui.m_listRoot.parent is RectTransform view)
            {
                var dy = Geo.BoxOf(_canvas, view).Y - listBox.Y;
                if (dy > 0.5f) _list.Put(_canvas, kl, new Box(listBox.X, listBox.Y - dy, listBox.W, listBox.H + dy));
            }
            StoreRows();
            var y = listBox.Bottom + gap;
            // Mockup v2: a 375 px Buy plate from x 80, the rest of the row (Sell socket) right of it.
            var buy = new Box(col.X + 32f * u, y, Mathf.Min(375f * u, col.W * 0.66f), row);
            if (_buy != null) _buy.Put(_canvas, row / _buy.Box0.H, buy);
            var rest = new Box(buy.Right + gap, y, Mathf.Max(1f, col.Right - buy.Right - gap), row);
            if (_sell != null)
            {
                // Its own Morgott.UI panel stack off (the caps reached ~10 px under the column, run 20261107-154027): the Sell
                // panel box itself fits the Buy row.
                foreach (Transform c in _sell.Rt)
                    if (c.name.StartsWith("RuneSkin", System.StringComparison.Ordinal) && !_gated.Contains(c.gameObject))
                    {
                        RenderGate.Set(c.gameObject, Gate, true);
                        _gated.Add(c.gameObject);
                    }
                var k = Mathf.Min(row / _sell.Box0.H, rest.W / _sell.Box0.W);
                _sell.Put(_canvas, k, rest.X + (rest.W - _sell.Box0.W * k) / 2f, y + (row - _sell.Box0.H * k) / 2f);
            }
        }

        // Parts of a mod panel in its own prefab layout (first show, before the page moves it).
        private Mod Capture(Transform mod)
        {
            var m = new Mod { Whole = PagePart.Of(_canvas, (RectTransform)mod) };
            foreach (var n in Sections)
                if (mod.Find(n) is RectTransform s && PagePart.Of(_canvas, s) is PagePart p)
                {
                    m.Sections.Add(p);
                    m.SectionKids.Add(PagePart.Children(_canvas, s));
                }
            m.CurrencyParts = PagePart.Children(_canvas, mod.Find("Currencies"));
            m.Sundial = PagePart.Of(_canvas, mod.Find("Sundial") as RectTransform);
            var kids = PagePart.Children(_canvas, mod);
            kids.RemoveAll(k => k.Rt.name == "Frame" || k.Rt.name == "SelectedFrame" || k.Rt.name == "TooltipAnchor" || k.Rt.name == "Currencies" || k.Rt.name == "Sundial" || k.Rt.name == Compat.ModCompat.EpicLootDragSurface);
            // Full-panel layers (backgrounds, input blockers) are no widgets.
            if (m.Whole != null) kids.RemoveAll(k => k.Box0.W >= m.Whole.Box0.W * 0.95f && k.Box0.H >= m.Whole.Box0.H * 0.95f);
            m.Content = PagePart.Union(kids, true);
            return m;
        }
        // The mod panel's own window chrome on the page: the board's whole Morgott.UI stack (the wells frame its sections),
        // the temper panel's window header only (its frame stays; the header plate overlapped its column heads, run
        // 20261107-143240). The page title names the trader.
        private void Chrome(Transform mod)
        {
            // Both panels' own stacks (also the temper panel's on its "Frame" child: a second page frame inside the page,
            // run 20261107-152301); a page well frames the temper panel instead.
            // Epic Loot's move handle: the page is not movable.
            foreach (var t in mod.GetComponentsInChildren<RectTransform>(true))
                if (t.name == Compat.ModCompat.EpicLootDragHandle && !_gated.Contains(t.gameObject))
                {
                    RenderGate.Set(t.gameObject, Gate, true);
                    _gated.Add(t.gameObject);
                }
            // The board's own (gated) window header mirrored the top-left section title (Тайник) and so switched it
            // off (RuneWindowHeader.Refresh): a static title releases it; the page names the trader anyway.
            if (mod.name == "MerchantPanel")
                foreach (var h in mod.GetComponentsInChildren<RuneWindowHeader>(true)) h.Text(h.TitleText);
            foreach (var host in new[] { mod, mod.Find("Frame") })
                if (host)
                    foreach (Transform c in host)
                        if (c.name.StartsWith("RuneSkin", System.StringComparison.Ordinal) && !_gated.Contains(c.gameObject))
                        {
                            RenderGate.Set(c.gameObject, Gate, true);
                            _gated.Add(c.gameObject);
                        }
        }
        // The vanilla coin purse duplicates the mod panel's own coin count: hidden while that panel shows one.
        private void Hide(GameObject coins, Transform currencies)
        {
            var hide = (bool)currencies;
            if (hide && !_gated.Contains(coins))
            {
                RenderGate.Set(coins, Gate, true);
                _gated.Add(coins);
            }
            else if (!hide && _gated.Remove(coins)) RenderGate.Set(coins, Gate, false);
        }
    }
}
