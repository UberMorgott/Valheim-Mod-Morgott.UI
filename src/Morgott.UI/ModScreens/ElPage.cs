using System;
using HarmonyLib;
using Morgott.UI.InfoScreens;
using Morgott.UI.InventoryScreen;
using Morgott.UI.Restyle;
using UnityEngine;

namespace Morgott.UI.ModScreens
{
    // Epic Loot's enchanting table window (EnchantingTableUI.Root = the prefab's "Panel", a sibling of the StoreGui;
    // EnchantingTableUI.cs:122-143 CreateUI, :302-343 Show / Hide) as a full-screen page like the info pages (user
    // 2026-11-07: tab info windows, enchanting and traders open full screen; InfoPage, mockup 2026-11-04/02): Panel fills
    // the screen, opaque, HUD and every UI drawn before it gated (FullWindow / CoverCull), Esc key cap = Epic Loot's own
    // Hide, the window header = the page title. Epic Loot keeps its Esc / B handling (EnchantingTableUI.Update: a panel's
    // Cancel first, else Hide). Body (mockup 02 list | pane): the tab column ("Tabs", its TabScrim over it) at the content
    // box's left edge, the feature panels ("TabContent") right of it; both at one scale that fits the prefab height into
    // the content box, the content panel takes the remaining width. The gamepad trigger hints ride with the tab column.
    // The page is not movable (the unit's MovableWindow stays off while it shows).
    public sealed class ElPage : MonoBehaviour
    {
        private RectTransform _panel, _tabs, _content, _scrim, _hints, _canvas;
        private InfoPage _page;
        private RuneWindowHeader _header;
        private MovableWindow _move;
        private bool _measured;
        private Rect _tabs0, _content0, _hints0; // canvas boxes of the prefab layout (first open)
        private Vector2 _tabsSize0, _contentSize0;

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
            p._content = panel.Find("TabContent") as RectTransform;
            p._scrim = panel.Find("TabScrim") as RectTransform;
            p._hints = panel.Find("TabGamepadHints") as RectTransform;
            p._move = unit.GetComponent<MovableWindow>();
            p._page = InfoPage.Ensure(panel, panel, null, () => hide?.Invoke(null, null));
            var canvas = panel.GetComponentInParent<Canvas>(true);
            p._canvas = canvas ? (RectTransform)canvas.rootCanvas.transform : null;
        }

        private bool _moveWasOn;

        private void OnDisable()
        {
            if (_move && _moveWasOn) _move.enabled = true;
            _moveWasOn = false;
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
            if (!_measured)
            {
                // The prefab layout before the page touches it: column / panels / hints relative to each other.
                _measured = true;
                _tabs0 = Rect(Geo.BoxOf(_canvas, _tabs));
                _content0 = Rect(Geo.BoxOf(_canvas, _content));
                _hints0 = _hints ? Rect(Geo.BoxOf(_canvas, _hints)) : default;
                _tabsSize0 = _tabs.rect.size;
                _contentSize0 = _content.rect.size;
            }
            var inv = InventorySkin.Instance;
            var inner = _page.Layout(inv ? inv.SkinScaleValue : 1f, _header);
            if (inner.W <= 0f || inner.H <= 0f) return;
            // Canvas units per local unit of the panel's children at scale 1 (the page set the panel's own scale to 1).
            var unit = Mathf.Abs(_panel.lossyScale.x / Mathf.Max(1e-6f, _canvas.lossyScale.x));
            var gap0 = Mathf.Max(0f, _content0.xMin - _tabs0.xMax);
            var b = ElPageMath.Body(inner.W, inner.H, _tabsSize0.x * unit, Mathf.Max(_tabsSize0.y, _contentSize0.y) * unit, _contentSize0.x * unit,
                gap0 / Mathf.Max(1e-6f, _tabs0.width) * _tabsSize0.x * unit);
            var s = b.Scale;
            var tabs = new Box(inner.X, inner.Y, b.TabsW, inner.H);
            Geo.SetScale(_tabs, s);
            Geo.Place(_canvas, _tabs, tabs);
            Geo.SetScale(_content, s);
            Geo.Place(_canvas, _content, new Box(inner.X + b.ContentX, inner.Y, inner.W - b.ContentX, inner.H));
            if (_scrim)
            {
                Geo.SetScale(_scrim, s);
                Geo.Place(_canvas, _scrim, tabs);
            }
            if (_hints && _hints0.width > 0f)
            {
                // Same offset from the column's top-left as in the prefab, at the column's new scale.
                var r = b.TabsW / Mathf.Max(1e-6f, _tabs0.width);
                Geo.SetScale(_hints, s);
                Geo.Place(_canvas, _hints, new Box(tabs.X + (_hints0.xMin - _tabs0.xMin) * r, tabs.Y + (_hints0.yMin - _tabs0.yMin) * r, _hints0.width * r, _hints0.height * r));
            }
        }

        private static Rect Rect(Box b) => new Rect(b.X, b.Y, b.W, b.H);
    }
}
