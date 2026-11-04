using System;
using System.Collections.Generic;
using Morgott.UI.Classify;
using Morgott.UI.InventoryScreen;
using Morgott.UI.Restyle;
using Morgott.UI.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Morgott.UI.InfoScreens
{
    // Full-screen info pages (user 2026-11-04, mockup docs/mockups/2026-11-04/02-tab-fullscreen-compendium.png): skills,
    // compendium, trophies, achievements and the character stats each fill the whole screen, opaque - no centred frame
    // over a dim, nothing of the world / HUD / other windows behind. One component per page root:
    // - enabled (every open path: vanilla OnOpen*, a mirror tab, a mod) = the one shown page: a FullWindow whose opaque
    //   area is the whole screen (inventory panels hidden, every UI part drawn before it gated by CoverCull until it
    //   closes), then every other open info page closes through its own vanilla close, so Esc / B (InventoryGui.Update
    //   :517-541) always acts on the visible page;
    // - every frame the vanilla blur (gui_blur grab pass) / darken outside the frame stay off and an own opaque backdrop
    //   (panel backing colour) fills the canvas right under the frame (over the vanilla click-outside closer);
    // - Layout: the frame fills the canvas; tab row (InfoTabs) top left, Esc key cap top right, the page title (window
    //   header) centred under them; returns the content box. The page is not movable (no WindowMove offset).
    public sealed class InfoPage : MonoBehaviour
    {
        public const string BackName = "RuneSkinPageBack";
        public const string EscName = "RuneSkinPageEsc";

        private static readonly List<InfoPage> All = new List<InfoPage>(6);

        private RectTransform _frame, _canvas;
        private Action _close;
        private InfoTabs _tabs;
        private Image _back;
        private Button _esc;
        private readonly List<Image> _dims = new List<Image>(2);
        private UIGroupHandler _group;
        private Selectable _focus;

        // Per-frame layout of a page without its own LateUpdate (compendium), with the skin scale.
        public Action<float> OnLayout;
        public RectTransform Canvas => _canvas;

        // Harness / diagnostics: the page shown now (null = none).
        public static InfoPage Shown
        {
            get
            {
                foreach (var p in All)
                    if (p && p.isActiveAndEnabled) return p;
                return null;
            }
        }

        private static Color Backing()
        {
            var c = ThemeTokens.GetColor("color.panel.backing", new Color(0.063f, 0.11f, 0.133f, 1f));
            c.a = 1f;
            return c;
        }

        public static Rect ScreenRect() => new Rect(0f, 0f, Screen.width, Screen.height);

        // frame null (stats: own painted chrome over its own opaque scrim): single-page + FullWindow only.
        public static InfoPage Ensure(RectTransform root, RectTransform frame, string openMethod, Action close, Selectable focus = null)
        {
            var p = root.GetComponent<InfoPage>() ?? root.gameObject.AddComponent<InfoPage>();
            p._frame = frame;
            p._close = close;
            p._focus = focus;
            var canvas = root.GetComponentInParent<Canvas>(true);
            p._canvas = canvas ? (RectTransform)canvas.rootCanvas.transform : null;
            p.EnsureGroup(root);
            if (!frame || p._back) return p;
            p._back = new GameObject(BackName, typeof(RectTransform)).AddComponent<Image>();
            p._back.transform.SetParent(frame.parent, false);
            p._back.raycastTarget = true; // clicks beside the frame's art stay on the page (vanilla closer under it)
            p._back.color = Backing();
            DollCell.Mark(p._back, "content", "full-screen info page backdrop (opaque)");
            foreach (var img in root.GetComponentsInChildren<Image>(true))
                if ((img.name == "blur" || img.name == "darken") && !img.transform.IsChildOf(frame)) p._dims.Add(img);
            var gui = root.GetComponentInParent<InventoryGui>(true);
            if (gui && openMethod != null) p._tabs = new InfoTabs(gui, frame, openMethod, close);
            p._esc = MakeEsc(frame, close);
            return p;
        }

        // Gamepad focus follows the page: vanilla OnOpen* only activates the inventory's Info group (m_uiGroups[2],
        // InventoryGui.cs:2349) - the header row, hidden under the page. A page without its own UIGroupHandler gets one
        // ranked above every inventory group (UIGroupHandler.IsHighestPriority: the active group of the highest priority
        // takes the focus), so while it shows the inventory groups are inactive and the pad lands on the page (its own
        // tab, else Esc / the given control); closed (inactive), the inventory's groups take over again.
        private void EnsureGroup(RectTransform root)
        {
            if (_group || root.GetComponentInChildren<UIGroupHandler>(true)) return;
            var gui = root.GetComponentInParent<InventoryGui>(true);
            var top = 0;
            if (gui && gui.m_uiGroups != null)
                foreach (var g in gui.m_uiGroups)
                    if (g) top = Mathf.Max(top, g.m_groupPriority);
            _group = root.gameObject.AddComponent<UIGroupHandler>();
            _group.m_groupPriority = top + 1;
        }

        private static Button MakeEsc(RectTransform frame, Action close)
        {
            var go = new GameObject(EscName, typeof(RectTransform));
            go.transform.SetParent(frame, false);
            var back = go.AddComponent<Image>();
            var b = go.AddComponent<Button>();
            b.targetGraphic = back;
            b.navigation = new Navigation { mode = Navigation.Mode.Automatic }; // gamepad reaches it from the tab row
            b.onClick.AddListener(() => close?.Invoke());
            var mark = DollCell.Mark(back, "info.tab", "page close key cap (mockup 02: Esc top right)");
            mark.Outcome = Restyler.Apply(back, mark, ThemeTokens.Role(mark.Role)) ? Outcome.Styled : Outcome.Degraded;
            var text = InfoArt.RowText(go.transform, "Label", "text.body", TextAlignmentOptions.Center);
            text.SetText("Esc");
            return b;
        }

        private void Awake() => All.Add(this);

        private void OnDestroy()
        {
            All.Remove(this);
            FullWindow.Exit(transform);
        }

        private void OnEnable()
        {
            FullWindow.Enter(transform, ScreenRect); // first: a closing page then never restores the inventory in between
            foreach (var p in All.ToArray())
                if (p && p != this && p.isActiveAndEnabled) p._close?.Invoke();
        }

        private void OnDisable() => FullWindow.Exit(transform);

        private void LateUpdate()
        {
            foreach (var d in _dims)
                if (d && d.enabled) d.enabled = false;
            if (_group)
            {
                var own = _tabs?.OwnButton;
                var focus = own ? own.gameObject : _esc ? _esc.gameObject : _focus ? _focus.gameObject : null;
                if (_group.m_defaultElement != focus) _group.m_defaultElement = focus;
            }
            if (!_back || !_canvas || !_frame) return;
            if (_back.color.a < 1f || _back.sprite) { _back.sprite = null; _back.color = Backing(); } // a restyle pass must not thin it
            var at = _back.transform.GetSiblingIndex();
            var f = _frame.GetSiblingIndex();
            if (at != f - 1) _back.transform.SetSiblingIndex(at < f ? f - 1 : f); // right under the frame
            Geo.Place(_canvas, _back.rectTransform, Full(_canvas));
            var inv = InventorySkin.Instance;
            if (OnLayout != null && inv) OnLayout(inv.SkinScaleValue);
        }

        public static Box Full(RectTransform canvas) => new Box(0f, 0f, canvas.rect.width, canvas.rect.height);

        // Lays the frame and the page chrome out; returns the content box (canvas units) under the title.
        // shortcut: the whole canvas, not Screen.safeArea (PC: equal); inset by the safe area when consoles / notches matter.
        public Box Layout(float k, RuneWindowHeader header, float auxW = 0f)
        {
            if (!_canvas || !_frame) return default;
            var full = Full(_canvas);
            Geo.SetScale(_frame, 1f);
            Geo.Place(_canvas, _frame, full);
            var pad = Mathf.Max(ThemeTokens.GetFloat("size.info.pad", 22f) * k, PanelDecor.KeepOut(_frame, _canvas));
            var inner = new Box(pad, pad, full.W - 2f * pad, full.H - 2f * pad);
            var gap = 8f * k;
            var barH = ThemeTokens.GetFloat("size.info.tabs", 70f) * k;
            if (_tabs != null)
            {
                var n = _tabs.Count;
                var tabGap = ThemeTokens.GetFloat("size.info.tab.gap", 10f) * k;
                var rowW = Mathf.Min(inner.W * 0.6f, n * barH * ThemeTokens.GetFloat("share.info.tab.aspect", 1.3f) + Mathf.Max(0, n - 1) * tabGap);
                _tabs.Layout(_canvas, new Box(inner.X, inner.Y, rowW, barH), k);
            }
            if (_esc)
            {
                var h = barH * 0.62f;
                var b = new Box(inner.Right - h * 1.5f, inner.Y + (barH - h) / 2f, h * 1.5f, h);
                Geo.Place(_canvas, (RectTransform)_esc.transform, b);
                if (_esc.transform.Find("Label") is RectTransform l && l.TryGetComponent<TMP_Text>(out var t))
                {
                    Geo.Place(_canvas, l, b);
                    InfoArt.Label(t, TextAlignmentOptions.Center, h * 0.5f, 12f);
                }
            }
            var y = inner.Y + barH + 2f * gap;
            var headerH = WindowHeaderMath.HeaderHeight(ThemeTokens.GetFloat("size.win.header", 40f), k);
            var titleW = Mathf.Min(inner.W, ThemeTokens.GetFloat("size.info.page.title.w", 920f) * k);
            if (header) header.Place(_canvas, new Box((full.W - titleW) / 2f, y, titleW, headerH), null, auxW);
            y += headerH + 2f * gap;
            return new Box(inner.X, y, inner.W, Mathf.Max(0f, inner.Bottom - y));
        }
    }
}
