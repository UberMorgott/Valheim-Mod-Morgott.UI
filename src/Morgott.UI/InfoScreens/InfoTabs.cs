using System;
using System.Collections.Generic;
using Morgott.UI.Classify;
using Morgott.UI.InventoryScreen;
using Morgott.UI.Restyle;
using Morgott.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace Morgott.UI.InfoScreens
{
    // In-panel tab row of the info panels (skills-a / trophies-a): one square tab per info button of the inventory
    // header (InventoryGui.m_info: texts, skills, trophies, ... - every active Button child, so mod tabs appear too; the
    // PVP Toggle is not a tab). Each tab mirrors its source: same icon sprite, a click invokes the source Button's
    // onClick (vanilla OnOpenTexts / OnOpenSkills / OnOpenTrophies, or whatever a mod wired or prefixed - Almanac opens
    // from OnOpenTrophies). The panel's own tab (persistent onClick target = its open method) is drawn selected; a click
    // on it closes the panel (the vanilla close button is hidden, mockups have none). Before another tab opens its
    // screen, this panel closes, so two info panels never stack.
    internal sealed class InfoTabs
    {
        private const string Prefix = "RuneSkinInfoTab";

        private readonly InventoryGui _gui;
        private readonly RectTransform _parent;
        private readonly string _openMethod;
        private readonly Action _close;
        private readonly List<Tab> _tabs = new List<Tab>(8);
        private readonly List<Button> _sources = new List<Button>(8);
        private Image _strip;

        private sealed class Tab
        {
            public Button Source;
            public Button Button;
            public Image Back, Icon;
            public bool Own;
        }

        public InfoTabs(InventoryGui gui, RectTransform parent, string openMethod, Action close)
        {
            _gui = gui;
            _parent = parent;
            _openMethod = openMethod;
            _close = close;
        }

        // The tab of this panel (drawn selected): the gamepad's default element of the page (InfoPage).
        public Button OwnButton
        {
            get
            {
                foreach (var t in _tabs)
                    if (t.Own && t.Button.gameObject.activeSelf) return t.Button;
                return null;
            }
        }

        public int Count
        {
            get
            {
                Collect();
                return _sources.Count;
            }
        }

        private void Collect()
        {
            _sources.Clear();
            var info = _gui ? _gui.m_info : null;
            if (!info) return;
            foreach (var c in Children.Of(info))
                if (c.gameObject.activeSelf && c.TryGetComponent<Button>(out var b) && !c.name.StartsWith("RuneSkin", StringComparison.Ordinal))
                    _sources.Add(b);
        }

        // The panel's own tab: its persistent onClick target is `method` (vanilla tabs), or the tab is named `method`
        // (own buttons wired by code: the stats window's CharacterStats).
        private static bool Opens(Button b, string method)
        {
            if (b.name == method) return true;
            var ev = b.onClick;
            for (var i = 0; i < ev.GetPersistentEventCount(); i++)
                if (ev.GetPersistentMethodName(i) == method) return true;
            return false;
        }

        // Source icon: the Icon / Image child (InventorySkin.Tab sizes the same children), else the first child image.
        private static Sprite IconOf(Button source)
        {
            Image any = null;
            foreach (var c in Children.Of(source.transform))
            {
                if (!c.TryGetComponent<Image>(out var img) || !img.sprite) continue;
                if (c.name == "Icon" || c.name == "Image") return img.sprite;
                if (!any && c.name != "Background" && c.name != "Selected") any = img;
            }
            return any ? any.sprite : null;
        }

        private Tab Make(int index)
        {
            var go = new GameObject(Prefix + index, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_parent, false);
            var t = new Tab { Back = go.AddComponent<Image>() };
            t.Button = go.AddComponent<Button>();
            t.Button.targetGraphic = t.Back;
            t.Button.navigation = new Navigation { mode = Navigation.Mode.Automatic }; // gamepad walks the row (InfoPage focus group)
            var tab = t;
            t.Button.onClick.AddListener(() => Click(tab));
            var mark = DollCell.Mark(t.Back, "info.tab", "info panel tab (mirror of an InventoryGui.m_info button)");
            mark.Outcome = Restyler.Apply(t.Back, mark, ThemeTokens.Role(mark.Role)) ? Outcome.Styled : Outcome.Degraded;
            t.Icon = new GameObject("Icon", typeof(RectTransform)).AddComponent<Image>();
            t.Icon.rectTransform.SetParent(rt, false);
            t.Icon.raycastTarget = false;
            t.Icon.preserveAspect = true;
            DollCell.Mark(t.Icon, "content", "info tab icon (the source tab's own sprite)");
            return t;
        }

        private void Click(Tab t)
        {
            if (!t.Source) return;
            _close?.Invoke();
            if (!t.Own) t.Source.onClick.Invoke();
        }

        // Lays the row out centred in `row` (canvas box); tabs a little wider than tall, never past the row; `gap` in canvas
        // units (default: the size.info.tab.gap token).
        public void Layout(RectTransform canvas, Box row, float k, float gap = -1f)
        {
            Collect();
            while (_tabs.Count < _sources.Count) _tabs.Add(Make(_tabs.Count));
            var n = _sources.Count;
            // Carved ledge under the row (tab_strip; the tabs overlap its top by ~4 px, art README "M3 polish").
            if (!_strip)
            {
                _strip = InfoArt.OwnImage(_parent, Prefix + "Strip", "info.tab.strip", "ledge under the info tab row (tab_strip)");
                _strip.preserveAspect = false;
                var stack = _parent.Find(PanelDecor.ChildName);
                _strip.rectTransform.SetSiblingIndex(stack ? stack.GetSiblingIndex() + 1 : 0); // above the panel glass, under the tabs
            }
            if (_strip.enabled && n > 0) Geo.Place(canvas, _strip.rectTransform, new Box(row.X, row.Bottom - 4f * k, row.W, 16f * k));
            var cells = InfoMath.TabRow(row, n, ThemeTokens.GetFloat("share.info.tab.aspect", 1.3f), gap >= 0f ? gap : ThemeTokens.GetFloat("size.info.tab.gap", 10f) * k);
            for (var i = 0; i < _tabs.Count; i++)
            {
                var t = _tabs[i];
                var on = i < n && !cells[i].IsEmpty;
                if (t.Back.gameObject.activeSelf != on) t.Back.gameObject.SetActive(on);
                if (!on) continue;
                var src = _sources[i];
                t.Source = src;
                var own = Opens(src, _openMethod);
                if (own != t.Own || t.Back.GetComponent<SkinMark>() is SkinMark m0 && m0.Role != (own ? "info.tab.selected" : "info.tab"))
                {
                    t.Own = own;
                    if (t.Back.GetComponent<SkinMark>() is SkinMark m)
                    {
                        m.Role = own ? "info.tab.selected" : "info.tab";
                        m.Outcome = Restyler.Apply(t.Back, m, ThemeTokens.Role(m.Role)) ? Outcome.Styled : Outcome.Degraded;
                    }
                }
                var sprite = IconOf(src);
                if (t.Icon.sprite != sprite) t.Icon.sprite = sprite;
                if (t.Icon.enabled != (bool)sprite) t.Icon.enabled = sprite;
                var tint = ThemeTokens.GetColor("color.info.tab.icon", Color.white); // mockup 02: copper icons
                if (t.Icon.color != tint) t.Icon.color = tint;
                var b = cells[i];
                Geo.Place(canvas, t.Back.rectTransform, b);
                var side = Mathf.Min(b.W, b.H) * (1f - 2f * ThemeTokens.GetFloat("share.inv.tab.icon.inset", 0.16f));
                Geo.Place(canvas, t.Icon.rectTransform, new Box(b.X + (b.W - side) / 2f, b.Y + (b.H - side) / 2f, side, side));
                if (src.interactable != t.Button.interactable) t.Button.interactable = src.interactable;
            }
        }
    }
}
