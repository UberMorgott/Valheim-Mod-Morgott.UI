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
        private RectTransform _topic;
        private readonly List<GameObject> _gated = new List<GameObject>();
        private readonly Image[] _wells = new Image[4];

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
            // The vanilla "topic" plate text: the page title shows the trader's name instead.
            _topic = Child("topic");
            for (var i = 0; i < _wells.Length; i++)
            {
                _wells[i] = InfoArt.OwnImage(_store, "RuneSkinTradeWell" + i, "craft.well", "trader page board section well (mockup 04)");
                _wells[i].preserveAspect = false;
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
            if (_header && _gui.m_trader) _header.Text(_gui.m_trader.m_name);
            if (_topic && !_gated.Contains(_topic.gameObject))
            {
                RenderGate.Set(_topic.gameObject, Gate, true);
                _gated.Add(_topic.gameObject);
            }
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
            ElPageMath.Trader(body, mod, out var col, out var right);
            StoreColumn(col, u);
            if (_coins != null)
            {
                Hide(_coins.Rt.gameObject, currencies);
                _coins.Put(_canvas, bar.H / _coins.Box0.H, bar.X, bar.Y);
            }
            for (var i = 0; i < _wells.Length; i++)
                if (_wells[i] && _wells[i].enabled != (board && _wells[i].sprite)) _wells[i].enabled = board && _wells[i].sprite;
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
                    if (_wells[i]) Geo.Place(_canvas, _wells[i].rectTransform, cell);
                    var part = m.Sections[i];
                    var k = ElPageMath.Fit(part.Box0.W, part.Box0.H, new Box(cell.X + pad, cell.Y + pad, cell.W - 2f * pad, cell.H - 2f * pad), out var x0, out var y0);
                    part.Put(_canvas, k, x0, y0);
                }
                if (m.Currencies != null)
                {
                    var room = bar.W - (m.Sundial != null ? bar.H + 8f * u : 0f);
                    m.Currencies.Put(_canvas, Mathf.Min(bar.H / m.Currencies.Box0.H, room / m.Currencies.Box0.W), bar.X, bar.Y);
                }
                m.Sundial?.Put(_canvas, bar.H / m.Sundial.Box0.H, bar.Right - bar.H, bar.Y);
            }
            else if (m.Whole != null)
                m.Whole.Put(_canvas, ElPageMath.Fit(m.Whole.Box0.W, m.Whole.Box0.H, right, out var x0, out var y0), x0, y0);
        }

        private sealed class Mod
        {
            public PagePart Whole, Currencies, Sundial;
            public readonly List<PagePart> Sections = new List<PagePart>();
        }

        private readonly Dictionary<Transform, Mod> _mods = new Dictionary<Transform, Mod>();
        private void StoreColumn(Box col, float u)
        {
            var row = 56f * u;
            var gap = 12f * u;
            var listBox = new Box(col.X, col.Y, col.W, Mathf.Max(0f, col.H - row - gap));
            _list?.Put(_canvas, col.W / _list.Box0.W, listBox);
            var y = listBox.Bottom + gap;
            var half = (col.W - gap) / 2f;
            if (_buy != null) _buy.Put(_canvas, row / _buy.Box0.H, new Box(col.X, y, half, row));
            if (_sell != null)
            {
                var k = Mathf.Min(row / _sell.Box0.H, half / _sell.Box0.W);
                _sell.Put(_canvas, k, col.X + half + gap + (half - _sell.Box0.W * k) / 2f, y + (row - _sell.Box0.H * k) / 2f);
            }
        }

        // Parts of a mod panel in its own prefab layout (first show, before the page moves it).
        private Mod Capture(Transform mod)
        {
            var m = new Mod { Whole = PagePart.Of(_canvas, (RectTransform)mod) };
            foreach (var n in Sections)
                if (mod.Find(n) is RectTransform s && PagePart.Of(_canvas, s) is PagePart p) m.Sections.Add(p);
            m.Currencies = PagePart.Of(_canvas, mod.Find("Currencies") as RectTransform);
            m.Sundial = PagePart.Of(_canvas, mod.Find("Sundial") as RectTransform);
            return m;
        }
        // The board's own window chrome (Morgott.UI panel stack + window header of the EpicLootBoard unit) is not drawn on
        // the page: the wells frame its sections. The temper panel keeps its frame (fitted whole).
        private void Chrome(Transform mod)
        {
            if (mod.name != "MerchantPanel") return;
            foreach (Transform c in mod)
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
