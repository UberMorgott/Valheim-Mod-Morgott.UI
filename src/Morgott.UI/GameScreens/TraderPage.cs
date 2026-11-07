using System.Collections.Generic;
using Morgott.UI.InfoScreens;
using Morgott.UI.InventoryScreen;
using Morgott.UI.ModScreens;
using Morgott.UI.Restyle;
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
    // - right: Haldor's Epic Loot MerchantPanel sections SecretStash / Gamble / TreasureMap / Bounties as a 2 x 2 grid in
    //   wells (frame_inset), its Currencies + Sundial in the top bar; Hildir's TemperPanel fitted whole;
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
            var currencies = mod ? mod.Find("Currencies") as RectTransform : null;
            var u = InfoPage.Full(_canvas).H / 1080f;
            // Top bar between the name banner and the Esc cap: coin purse / mod currencies + sundial (mockup 04).
            var bar = new Box(InfoPage.Full(_canvas).W - (155f + 24f + 470f) * u, 30f * u, 470f * u, 56f * u); // left of the Esc cap (InfoPage.Layout: W - 155)
            _page.NameX = 726f;
            _page.NameRightReserve = 470f + 24f + 26f;
            var inv = InventorySkin.Instance;
            var body = _page.Layout(inv ? inv.SkinScaleValue : 1f, _header);
            if (body.IsEmpty) return;
            // Mockup 04 body ~15 px under the title plaque (run 20261107-151323: the store well touched it at y 207).
            body = new Box(body.X, body.Y - 18f * u, body.W, body.H + 18f * u);
            ElPageMath.Trader(body, mod, out var col, out var right);
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
            for (var i = board ? _wells.Length : mod ? 1 : 0; i < _wells.Length; i++) _wells[i]?.Place(_canvas, default, false);
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
                var g = 16f * u;
                var pad = 12f * u;
                for (var i = 0; i < Sections.Length; i++)
                {
                    var cell = ElPageMath.Cell(right, i % 2, i / 2, g);
                    _wells[i]?.Place(_canvas, cell, true);
                    var part = m.Sections[i];
                    // The section's children as one group (its title / timer sit above its own rect: run 20261107-145553
                    // drew the lower titles in the gap between the rows).
                    PagePart.Fit(_canvas, m.SectionKids[i], new Box(cell.X + pad, cell.Y + pad, cell.W - 2f * pad, cell.H - 2f * pad));
                    // Its lists across the cell (Epic Loot's fixed-width scroll panels: SecretStash/Panel, Bounties/*Panel).
                    foreach (Transform c in part.Rt)
                        if (c is RectTransform list && c.name.EndsWith("Panel", System.StringComparison.Ordinal) && c.gameObject.activeSelf)
                        {
                            var b = Geo.BoxOf(_canvas, list);
                            Geo.Place(_canvas, list, new Box(cell.X + pad, b.Y, cell.W - 2f * pad, b.H));
                        }
                        // Its action buttons at half the cell (Epic Loot's ~150 px plates: run 20261107-152301 vs mockup 04 ~330),
                        // centred where they were; the small icon button (abandon) keeps its size.
                        else if (c is RectTransform btn && c.gameObject.activeSelf && c.TryGetComponent<Button>(out _) && btn.rect.width > btn.rect.height * 2f)
                        {
                            var b = Geo.BoxOf(_canvas, btn);
                            var w = Mathf.Max(b.W, cell.W * 0.5f);
                            Geo.Place(_canvas, btn, new Box(b.X + (b.W - w) / 2f, b.Y, w, b.H));
                        }
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
        private void StoreColumn(Box col, float u)
        {
            var row = 56f * u;
            var gap = 12f * u;
            var listBox = new Box(col.X, col.Y, col.W, Mathf.Max(0f, col.H - row - gap));
            // Vanilla rows at ~1.25x (mockup 04 row height), the list stretched to the column.
            var kl = Mathf.Min(1.25f, col.W / _list?.Box0.W ?? 1f);
            _list?.Put(_canvas, kl, listBox);
            // Rows from the column top: vanilla's list viewport sits ~150 px into the ItemList part (run 20261107-152301);
            // the part moves up by that, its bottom stays.
            if (_list != null && _gui.m_listRoot && _gui.m_listRoot.parent is RectTransform view)
            {
                var dy = Geo.BoxOf(_canvas, view).Y - listBox.Y;
                if (dy > 0.5f) _list.Put(_canvas, kl, new Box(listBox.X, listBox.Y - dy, listBox.W, listBox.H + dy));
            }
            var y = listBox.Bottom + gap;
            var half = (col.W - gap) / 2f;
            if (_buy != null) _buy.Put(_canvas, row / _buy.Box0.H, new Box(col.X, y, half, row));
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
                var k = Mathf.Min(row / _sell.Box0.H, half / _sell.Box0.W);
                _sell.Put(_canvas, k, col.X + half + gap + (half - _sell.Box0.W * k) / 2f, y + (row - _sell.Box0.H * k) / 2f);
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
