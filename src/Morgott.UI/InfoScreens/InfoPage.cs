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
    // - Layout (mockup 02 geometry): the frame's panel stack runs along the screen edge (rails, ~90 px corner caps, page
    //   background); top bar = tab row (InfoTabs), ITEMS key (the inventory's ITEMS tab), character name banner, Esc key
    //   cap, a rule under it; the page title (window header) centred under the bar; returns the content box. The page is
    //   not movable (no WindowMove offset).
    public sealed class InfoPage : MonoBehaviour
    {
        public const string BackName = "RuneSkinPageBack";
        public const string EscName = "RuneSkinPageEsc";

        private static readonly List<InfoPage> All = new List<InfoPage>(6);

        private RectTransform _frame, _canvas;
        private Action _close;
        private InfoTabs _tabs;
        private Image _back;
        private Button _esc, _items;
        private string _itemsLabel = "";
        private Image _name, _rule;
        private TMP_Text _nameText;
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
            var c = ThemeTokens.GetColor("color.info.page.bg", new Color(0f, 0.086f, 0.11f, 1f));
            c.a = 1f;
            return c;
        }

        // The whole screen plus one screen on each side: a full-screen page covers every UI part, also those reaching past the
        // screen edge, which CoverCull only gates when they lie fully inside the opaque area (run 20261104-205914: the
        // minimap's RuneSkin_DayPhase stayed drawn under every page).
        public static Rect ScreenRect() => new Rect(-Screen.width, -Screen.height, 3f * Screen.width, 3f * Screen.height);

        // frame null (stats: own painted chrome over its own opaque scrim): single-page + FullWindow only.
        public static InfoPage Ensure(RectTransform root, RectTransform frame, string openMethod, Action close, Selectable focus = null)
        {
            var p = root.GetComponent<InfoPage>() ?? root.gameObject.AddComponent<InfoPage>();
            p._frame = frame;
            p._close = close;
            p._focus = focus;
            var canvas = root.GetComponentInParent<Canvas>(true);
            p._canvas = canvas ? (RectTransform)canvas.rootCanvas.transform : null;
            if (!frame || p._back) return p;
            p._back = new GameObject(BackName, typeof(RectTransform)).AddComponent<Image>();
            // A page whose root IS its frame (Epic Loot's enchanting Panel, ElPage): the backdrop is its first child, so it
            // shows / hides with the page and is no earlier sibling CoverCull would gate.
            p._back.transform.SetParent(root == frame ? frame : frame.parent, false);
            p._back.raycastTarget = true; // clicks beside the frame's art stay on the page (vanilla closer under it)
            p._back.color = Backing();
            DollCell.Mark(p._back, "content", "full-screen info page backdrop (opaque)");
            foreach (var img in root.GetComponentsInChildren<Image>(true))
                // Vanilla blur / darken and the full-screen click-outside closer (no outside on a full page; drawn
                // transparent under the backdrop: run 20261104-214645 overdraw-texts).
                if ((img.name == "blur" || img.name == "darken" || img.name == "Closebutton") && !img.transform.IsChildOf(frame)) p._dims.Add(img);
            var gui = root.GetComponentInParent<InventoryGui>(true);
            if (gui && openMethod != null) p._tabs = new InfoTabs(gui, frame, openMethod, close);
            p._esc = MakeKey(frame, EscName, "page close key cap (mockup 02: Esc top right)", () => close?.Invoke());
            // ITEMS (mockup 02 top bar): the item browser lives only in the inventory's right-hand ITEMS tab (user
            // 2026-11-04) - the page closes (the inventory stays) and that tab opens.
            // Inventory pages only (a page opened from the world, e.g. the enchanting table, has no inventory tab to go to).
            if (p._tabs != null) p._items = MakeKey(frame, "RuneSkinPageItems", "page top bar: inventory ITEMS tab (mockup 02)", () =>
            {
                close?.Invoke();
                ItemBrowser.BrowserPanel.Instance?.Open();
            });
            p._itemsLabel = ItemBrowser.Browser.Localize("$ib_tab");
            p._name = InfoArt.OwnImage(frame, "RuneSkinPageName", "decor.plaque", "page top bar: character name banner (mockup 02)");
            p._name.preserveAspect = false;
            p._nameText = InfoArt.RowText(p._name.transform, "Label", "dialog.subject", TextAlignmentOptions.Center); // bold sans (mockup 02)
            p._rule = new GameObject("RuneSkinPageRule", typeof(RectTransform)).AddComponent<Image>();
            p._rule.transform.SetParent(frame, false);
            p._rule.raycastTarget = false;
            p._rule.color = ThemeTokens.GetColor("color.info.page.rule", new Color(0.36f, 0.5f, 0.53f, 1f));
            DollCell.Mark(p._rule, "content", "page top bar separator (mockup 02: rule under the bar)");
            return p;
        }

        // Gamepad focus follows the page: vanilla OnOpen* only activates the inventory's Info group (m_uiGroups[2],
        // InventoryGui.cs:2349) - the header row, hidden under the page - and the group of the highest priority that is
        // active in the hierarchy takes the focus (UIGroupHandler.IsHighestPriority; a user-inactive one then blocks every
        // lower one: run 20261104-205914 skills / texts had no active group at all). On open the page's own groups (or one
        // added to its root when it has none) are lifted above every other group, relative order kept (achievements
        // details popup), the outermost one user-active; its default element = the page's own tab (else Esc / the given
        // control) when it has none. Closed (inactive), the inventory's groups take over again.
        private void Focus()
        {
            // Only groups that take part now: an inactive vanilla sub-group lifted alone left skills / texts without any
            // active group (run 20261104-214645).
            var inside = new List<UIGroupHandler>();
            foreach (var g in GetComponentsInChildren<UIGroupHandler>(false))
                if (g.enabled) inside.Add(g);
            if (inside.Count == 0) inside.Add(gameObject.AddComponent<UIGroupHandler>());
            var top = int.MinValue;
            foreach (var g in FindObjectsByType<UIGroupHandler>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (g && !g.transform.IsChildOf(transform)) top = Mathf.Max(top, g.m_groupPriority);
            var min = int.MaxValue;
            _group = null;
            foreach (var g in inside)
            {
                min = Mathf.Min(min, g.m_groupPriority);
                if (!_group || Depth(g.transform) < Depth(_group.transform)) _group = g;
            }
            if (top != int.MinValue && min <= top)
                foreach (var g in inside) g.m_groupPriority += top + 1 - min;
            _group.SetActive(true);
        }

        private int Depth(Transform t)
        {
            var d = 0;
            for (; t && t != transform; t = t.parent) d++;
            return d;
        }
        private static Button MakeKey(RectTransform frame, string name, string reason, Action click)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(frame, false);
            var back = go.AddComponent<Image>();
            var b = go.AddComponent<Button>();
            b.targetGraphic = back;
            b.navigation = new Navigation { mode = Navigation.Mode.Automatic }; // gamepad reaches it from the tab row
            b.onClick.AddListener(() => click());
            var mark = DollCell.Mark(back, "info.tab", reason);
            mark.Outcome = Restyler.Apply(back, mark, ThemeTokens.Role(mark.Role)) ? Outcome.Styled : Outcome.Degraded;
            InfoArt.RowText(go.transform, "Label", "text.body", TextAlignmentOptions.Center).fontStyle |= FontStyles.Bold;
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
            Focus();
        }

        private void OnDisable() => FullWindow.Exit(transform);

        private void LateUpdate()
        {
            // InventoryGui.Hide clears every full window (FullWindowAdapter.Hide_Postfix); a page that stays open (opened
            // from the world: the enchanting table) registers again.
            if (_tabs == null && _frame && !FullWindow.Has(transform)) FullWindow.Enter(transform, ScreenRect);
            foreach (var d in _dims)
                if (d && d.enabled) d.enabled = false;
            if (_group)
            {
                var own = _tabs?.OwnButton;
                var focus = own ? own.gameObject : _esc ? _esc.gameObject : _focus ? _focus.gameObject : null;
                if (focus && (!_group.m_defaultElement || !_group.m_defaultElement.activeInHierarchy)) _group.m_defaultElement = focus;
            }
            if (!_back || !_canvas || !_frame) return;
            // Drawn only where the frame's own opaque panel backing does not fill the screen (run 20261104-205914
            // overdraw-skills / -trophies: fully under SkillsFrame/RuneSkinPanel/Backing); hidden it still takes the clicks
            // beside the frame's art (alpha 0 + culled mesh: not drawn, still a raycast target).
            var want = Backing();
            if (BackingCovers()) want.a = 0f;
            if (!_back.canvasRenderer.cullTransparentMesh) _back.canvasRenderer.cullTransparentMesh = true;
            if (_back.color != want || _back.sprite) { _back.sprite = null; _back.color = want; } // a restyle pass must not change it
            if (_back.transform.parent == _frame)
            {
                if (_back.transform.GetSiblingIndex() != 0) _back.transform.SetAsFirstSibling();
            }
            else
            {
                var at = _back.transform.GetSiblingIndex();
                var f = _frame.GetSiblingIndex();
                if (at != f - 1) _back.transform.SetSiblingIndex(at < f ? f - 1 : f); // right under the frame
            }
            Geo.Place(_canvas, _back.rectTransform, Full(_canvas));
            var inv = InventorySkin.Instance;
            if (OnLayout != null && inv) OnLayout(inv.SkinScaleValue);
        }

        // The page frame's own panel stack (on the frame, or on its vanilla "bkg" child: compendium).
        private PanelDecor Decor() => PanelDecor.Of(_frame) ?? (_frame.Find("bkg") is Transform b ? PanelDecor.Of(b) : null);

        private bool BackingCovers()
        {
            var decor = Decor();
            var b = decor ? decor.Backing : null;
            if (!b || !b.gameObject.activeInHierarchy || !b.TryGetComponent<Image>(out var img) || !img.enabled || img.color.a < 0.999f) return false;
            var box = Geo.BoxOf(_canvas, b);
            var full = Full(_canvas);
            return box.X <= 0.5f && box.Y <= 0.5f && box.Right >= full.W - 0.5f && box.Bottom >= full.H - 0.5f;
        }

        public static Box Full(RectTransform canvas) => new Box(0f, 0f, canvas.rect.width, canvas.rect.height);

        // Mockup 02 geometry (px at 1920x1080) mapped by the canvas height: u = canvas units per mockup px. Rows of the
        // top bar keep their left offsets, the Esc key cap its right offset; the content box spans what is left.
        private const float MockH = 1080f;

        // Lays the frame and the page chrome out; returns the content box (canvas units): list / pane area of mockup 02
        // (x 61 .. W-60, y 232 .. H-82).
        // shortcut: the whole canvas, not Screen.safeArea (PC: equal); inset by the safe area when consoles / notches matter.
        public Box Layout(float k, RuneWindowHeader header, float auxW = 0f)
        {
            if (!_canvas || !_frame) return default;
            var full = Full(_canvas);
            var u = full.H / MockH;
            Geo.SetScale(_frame, 1f);
            var decor = Decor();
            var host = decor ? (RectTransform)decor.transform.parent : null;
            // The stack reaches past its host by its pad: the host is inset by that reach so the rails run along the
            // screen edge (before, the compendium's 40 px pad pushed rails and corners off screen).
            var reach = 0f;
            if (decor && host)
            {
                var d = (RectTransform)decor.transform;
                reach = Mathf.Max(0f, (d.rect.width - host.rect.width) / 2f) * Mathf.Abs(host.lossyScale.x / Mathf.Max(1e-6f, _canvas.lossyScale.x));
            }
            var inset = new Box(reach, reach, full.W - 2f * reach, full.H - 2f * reach);
            Geo.Place(_canvas, _frame, host == _frame ? inset : full);
            if (host && host != _frame) Geo.Place(_canvas, host, inset);
            if (decor) Skin(decor, u, host);
            if (_tabs != null)
            {
                var n = _tabs.Count;
                var w = 85f * u;
                var g = 19f * u;
                _tabs.Layout(_canvas, new Box(109f * u, 27f * u, n * w + Mathf.Max(0, n - 1) * g, 66f * u), k, g);
            }
            var esc = new Box(full.W - 155f * u, 37f * u, 75f * u, 48f * u);
            if (_esc) Key(_esc, esc, "Esc", 26f * u);
            if (_items) Key(_items, new Box(639f * u, 32f * u, 266f * u, 56f * u), _itemsLabel, 28f * u);
            if (_name)
            {
                var x = 949f * u;
                var nb = new Box(x, 33f * u, Mathf.Max(0f, Mathf.Min(768f * u, esc.X - 48f * u - x)), 55f * u);
                Geo.Place(_canvas, _name.rectTransform, nb);
                Ends(_name, ThemeTokens.GetFloat("size.info.page.title.end", 200f) * u * 55f / 68f);
                if (_nameText)
                {
                    var who = Player.m_localPlayer ? Player.m_localPlayer.GetPlayerName() : "";
                    if (_nameText.text != who) _nameText.text = who;
                    var silver = ThemeTokens.GetColor("color.info.page.name", new Color(0.92f, 0.9f, 0.89f, 1f)); // mockup 02 #EBE5E3
                    if (_nameText.color != silver) _nameText.color = silver;
                    Geo.Place(_canvas, _nameText.rectTransform, new Box(nb.X + nb.W * 0.2f, nb.Y, nb.W * 0.6f, nb.H));
                    InfoArt.Label(_nameText, TextAlignmentOptions.Center, 30f * u, 14f, upper: true);
                }
            }
            if (_rule) Geo.Place(_canvas, _rule.rectTransform, new Box(19f * u, 111f * u, Mathf.Max(0f, full.W - 38f * u), 2f * u));
            var titleW = Mathf.Min(full.W, ThemeTokens.GetFloat("size.info.page.title.w", 920f) * u);
            if (header)
            {
                header.Place(_canvas, new Box((full.W - titleW) / 2f, 135f * u, titleW, 68f * u), null, auxW);
                // Mockup 02 page title: bold sans caps (the theme's font.body.bold), not the window headers' display face.
                if (header.FaceToken != "font.body.bold") header.FaceToken = "font.body.bold";
                Ends(header.Plate, ThemeTokens.GetFloat("size.info.page.title.end", 200f) * u);
                if (header.Title && (header.Title.fontStyle & FontStyles.UpperCase) != FontStyles.UpperCase) header.Title.fontStyle |= FontStyles.UpperCase;
            }
            var top = 232f * u;
            return new Box(61f * u, top, Mathf.Max(0f, full.W - 121f * u), Mathf.Max(0f, full.H - top - 82f * u));
        }

        // The page look of its panel stack: corner caps with a ~90 mockup px ornament (the cap sprite rect is ~1.4x its
        // visible copper: rect 126 -> ~90 visible; run 20261104-214645 measured 64 at rect 90), the page background (backing + a faint glass:
        // mockup 02 empty patch #01161B; the inventory windows keep the shared #101C22 + full glass).
        private void Skin(PanelDecor decor, float u, RectTransform host)
        {
            var ratio = Mathf.Abs(host.lossyScale.x / Mathf.Max(1e-6f, _canvas.lossyScale.x));
            var caps = decor.CornerCaps;
            if (caps != null)
                foreach (var c in caps)
                {
                    if (!c || c.rect.width <= 0f) continue;
                    var s = ThemeTokens.GetFloat("size.info.page.corner", 90f) * u / (c.rect.width * ratio);
                    if (Mathf.Abs(c.localScale.x - s) > 0.001f) c.localScale = new Vector3(s, s, 1f);
                }
            Tint(decor.Backing, "color.info.page.bg");
            Tint(decor.transform.Find("Glass"), "color.info.page.glass");
        }

        // Banner end blocks at `end` canvas units (mockup 02: ~200 px knotwork per end on the 68 px title band; at the
        // header_plaque's default density they drew ~105): the plate's sprite density is set so its fixed left border
        // (the knotwork block, header_plaque border 230 sprite px) spans `end`; the art keeps its aspect (rails scale too).
        private void Ends(Image plate, float end)
        {
            if (!plate || !plate.sprite || end <= 0f || plate.sprite.border.x <= 0f || !plate.canvas) return;
            var r = Mathf.Abs(plate.rectTransform.lossyScale.x / Mathf.Max(1e-6f, _canvas.lossyScale.x));
            var mul = plate.sprite.border.x * plate.canvas.referencePixelsPerUnit / (plate.sprite.pixelsPerUnit * end / Mathf.Max(1e-6f, r));
            if (Mathf.Abs(plate.pixelsPerUnitMultiplier - mul) > 0.0005f) plate.pixelsPerUnitMultiplier = mul;
        }

        private static void Tint(Transform t, string token)
        {
            if (!t || !t.TryGetComponent<Image>(out var img)) return;
            var c = ThemeTokens.GetColor(token, img.color);
            if (img.color != c) img.color = c;
        }

        private void Key(Button b, Box box, string label, float size)
        {
            Geo.Place(_canvas, (RectTransform)b.transform, box);
            if (b.transform.Find("Label") is RectTransform l && l.TryGetComponent<TMP_Text>(out var t))
            {
                if (t.text != label) t.text = label;
                Geo.Place(_canvas, l, box);
                InfoArt.Label(t, TextAlignmentOptions.Center, size, 12f, upper: true);
            }
        }
    }
}
