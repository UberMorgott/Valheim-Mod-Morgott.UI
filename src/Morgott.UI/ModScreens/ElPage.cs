using System;
using System.Collections.Generic;
using HarmonyLib;
using Morgott.UI.InfoScreens;
using Morgott.UI.InventoryScreen;
using Morgott.UI.Restyle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Morgott.UI.ModScreens
{
    // Epic Loot's enchanting table window (EnchantingTableUI.Root = the prefab's "Panel", a sibling of the StoreGui;
    // EnchantingTableUI.cs:122-143 CreateUI, :302-343 Show / Hide) as a full-screen page in mockup 02's shape (user
    // 2026-11-07: rebuild the feature panels into the list column + one framed pane, content up, page haze): Panel fills
    // the screen, opaque, HUD and every UI drawn before it gated (InfoPage / FullWindow / CoverCull), Esc key cap = Epic
    // Loot's own Hide, page title = the table's name. Epic Loot keeps its Esc / B handling and every widget's
    // wiring: its widgets are only re-placed (PagePart), never rebuilt.
    // - Tabs: Epic Loot's tab column becomes the top-bar row of icon caps (mockup 02 tab row; ElTabCell icon only, the
    //   column layout ElTabColumn stands down), its TabScrim over the row, the LT / RT gamepad hints at the row's ends.
    // - Body, per feature panel (TabContent/<X>Content): its main item list (MultiSelectItemList / SacrificeItemList /
    //   ItemList) = the list column (stretched to the body height), every other widget of the panel = one group fitted
    //   into the framed pane (RuneSkinElPane: the compendium pane's fill + thin frame) at one scale, Epic Loot's own arrangement kept; the panel's
    //   Title = the pane heading. Boxes come from the prefab layout captured on the first open.
    // The page is not movable (the unit's MovableWindow stays off while it shows).
    public sealed class ElPage : MonoBehaviour
    {
        private sealed class Feature
        {
            public RectTransform Root;
            public PagePart List, Title;
            public readonly List<PagePart> Rest = new List<PagePart>();
        }

        private RectTransform _panel, _tabs, _content, _scrim, _hints, _canvas;
        private TabHandler _tabHandler;
        private InfoPage _page;
        private RuneWindowHeader _header;
        private MovableWindow _move;
        private bool _moveWasOn, _measured;
        private PagePart.Pane _pane;
        private readonly List<Feature> _features = new List<Feature>();

        public static bool Shows(Transform t) => t && t.TryGetComponent<ElPage>(out var p) && p.isActiveAndEnabled;

        // unit = the EnchantingTableUI object (ForeignRoles.After, family EpicLootEnchanting).
        public static void Ensure(Transform unit)
        {
            var type = Morgott.Core.TypeLookup.ByFullName(Compat.ModCompat.EpicLootEnchantingUi);
            var ui = unit && type != null ? unit.GetComponent(type) : null;
            if (!ui || !(AccessTools.Field(ui.GetType(), "Root")?.GetValue(ui) is GameObject root) || !(root.transform is RectTransform panel)) return;
            if (panel.GetComponent<ElPage>()) return;
            var hide = AccessTools.Method(ui.GetType(), "Hide", Type.EmptyTypes);
            var p = panel.gameObject.AddComponent<ElPage>();
            p._panel = panel;
            p._tabs = panel.Find("Tabs") as RectTransform;
            p._tabHandler = p._tabs ? p._tabs.GetComponent<TabHandler>() : null;
            p._content = panel.Find("TabContent") as RectTransform;
            p._scrim = panel.Find("TabScrim") as RectTransform;
            p._hints = panel.Find("TabGamepadHints") as RectTransform;
            p._move = unit.GetComponent<MovableWindow>();
            p._page = InfoPage.Ensure(panel, panel, null, () => hide?.Invoke(null, null));
            var canvas = panel.GetComponentInParent<Canvas>(true);
            p._canvas = canvas ? (RectTransform)canvas.rootCanvas.transform : null;
        }

        private void OnDisable()
        {
            if (_move && _moveWasOn) _move.enabled = true;
            _moveWasOn = false;
        }

        // The prefab layout before the page touches it.
        private void Measure()
        {
            _measured = true;
            foreach (Transform x in _content)
            {
                if (!(x is RectTransform xr) || x.name.StartsWith("RuneSkin", StringComparison.Ordinal)) continue;
                var f = new Feature { Root = xr };
                foreach (var part in PagePart.Children(_canvas, x))
                {
                    var n = part.Rt.name;
                    if (n == "Title") f.Title = part;
                    else if (f.List == null && (n == "MultiSelectItemList" || n == "SacrificeItemList" || n == "ItemList")) f.List = part;
                    else f.Rest.Add(part);
                }
                _features.Add(f);
            }
            // The pane well under every feature panel (frame_inset, mockup 02 pane frame).
            _pane = PagePart.Pane.Make(_content, "RuneSkinElPane", "enchanting page pane (mockup 02 framed pane)");
        }

        private void LateUpdate()
        {
            if (!_move) _move = transform.parent ? transform.parent.GetComponent<MovableWindow>() : null;
            if (_move && _move.enabled)
            {
                _moveWasOn = true;
                _move.enabled = false;
            }
            if (!_header) _header = GetComponentInChildren<RuneWindowHeader>(true);
            if (!_canvas || !_tabs || !_content || !_page) return;
            if (!_measured) Measure();
            Title();
            var inv = InventorySkin.Instance;
            var body = _page.Layout(inv ? inv.SkinScaleValue : 1f, _header);
            if (body.W <= 0f || body.H <= 0f) return;
            var u = InfoPage.Full(_canvas).H / 1080f;
            TabRow(u);
            Geo.SetScale(_content, 1f / Mathf.Max(1e-6f, Mathf.Abs(_panel.lossyScale.x / Mathf.Max(1e-6f, _canvas.lossyScale.x))));
            Geo.Place(_canvas, _content, body);
            foreach (var f in _features)
                if (f.Root && f.Root.gameObject.activeSelf) LayFeature(f, body, u);
        }

        // Page title = the table's own name (EnchantingTable.GetHoverText's key); the feature's Title heads the pane. The band
        // title the header adopted was the first tab's label on every tab (run 20261107-023023).
        private const string TableName = "$mod_epicloot_assets_enchantingtable";

        private void Title()
        {
            if (_header) _header.Text(TableName);
        }
        private void TabRow(float u)
        {
            var shown = 0;
            if (_tabHandler)
                foreach (var tab in _tabHandler.m_tabs)
                    if (tab != null && tab.m_button && tab.m_button.gameObject.activeSelf)
                    {
                        if (tab.m_button.TryGetComponent<ElTabCell>(out var cell)) cell.IconOnly = true;
                        Geo.SetScale(tab.m_button.transform, 1f);
                        Geo.Place(_canvas, (RectTransform)tab.m_button.transform, ElPageMath.Tab(shown++, u));
                    }
            var row = ElPageMath.TabRow(shown, u);
            Geo.SetScale(_tabs, 1f);
            Geo.Place(_canvas, _tabs, row);
            if (_scrim)
            {
                Geo.SetScale(_scrim, 1f);
                Geo.Place(_canvas, _scrim, row);
            }
            if (!_hints) return;
            Geo.SetScale(_hints, 1f);
            Geo.Place(_canvas, _hints, new Box(row.X - 56f * u, row.Y, row.W + 112f * u, row.H));
            var key = new Box(0f, row.Y + 9f * u, 48f * u, 48f * u);
            if (_hints.Find("LTrigger") is RectTransform l) Geo.PlaceFit(_canvas, l, new Box(row.X - 52f * u, key.Y, key.W, key.H), 4f);
            if (_hints.Find("RTrigger") is RectTransform r) Geo.PlaceFit(_canvas, r, new Box(row.Right + 4f * u, key.Y, key.W, key.H), 4f);
        }

        private void LayFeature(Feature f, Box body, float u)
        {
            ElPageMath.Split(body, f.List != null, out var column, out var pane);
            _pane?.First(0);
            _pane?.Place(_canvas, pane, true);
            f.List?.Put(_canvas, column.W / f.List.Box0.W, column);
            var pad = 24f * u;
            var y = pane.Y + pad;
            if (f.Title != null)
            {
                var h = 52f * u;
                f.Title.Put(_canvas, h / f.Title.Box0.H, new Box(pane.X + pad, y, pane.W - 2f * pad, h));
                if (f.Title.Rt.TryGetComponent<TMP_Text>(out var t) && t.alignment != TextAlignmentOptions.MidlineLeft) t.alignment = TextAlignmentOptions.MidlineLeft;
                y += h + 8f * u;
            }
            PagePart.Fit(_canvas, f.Rest, new Box(pane.X + pad, y, pane.W - 2f * pad, Mathf.Max(0f, pane.Bottom - pad - y)));
        }
    }
}
