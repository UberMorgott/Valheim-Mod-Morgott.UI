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
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Morgott.UI.ItemBrowser
{
    // The ITEMS tab of the crafting window (docs\item-browser-design.md sections 1 + 11, approved mockups
    // docs\mockups\2026-11-04\04-items-right-panel.png = list, 05-items-right-panel-item-page.png = item page). Tab = clone
    // of m_tabUpgrade in the tab row as VNEI does (MainVneiHandler.GetTabButton); up = RecipeList + Decription off, this
    // panel over the crafting body (CraftingSkin.Apply). Laid out in mockup units: the panel is 550 units wide (mockup px
    // x 0.905), every box below is scaled by the body width.
    public sealed class BrowserPanel : MonoBehaviour
    {
        public const string TabName = "MorgottItemsTab";
        private const float UnitW = 550f;
        // List (mockup 04): search, type row, category row, caption, 6-column grid with a thin scrollbar.
        private const float TypeY = 50f, TypeH = 34f, CatY = 97f, CatH = 43f, CellPitchX = 89.5f, CellW = 84f, CellH = 74f, CellPitchY = 114f;
        private static readonly string[] TypeTokens = { "$ib_type_all", "$ib_type_items", "$ib_type_pieces", "$ib_type_creatures", "$ib_type_stations", null };
        private static readonly float[] TypeW = { 62f, 94f, 94f, 101f, 93f, 36f };
        private static readonly EntryType?[] TypeOf = { null, EntryType.Item, EntryType.Piece, EntryType.Creature, EntryType.Station, null };
        private static readonly string[] CatKeys = { "weapons", "armor", "utility", "ammo", "tools", "food", "meads", "materials", "trophies", "fish", "misc" };

        public static BrowserPanel Instance { get; private set; }
        public static bool Up => Instance && Instance._up;
        public static bool Typing => Up && Instance._search && Instance._search.isFocused;
        public static Transform Root => Instance ? Instance._root : null;

        private InventoryGui _gui;
        private Button _tab;
        private RectTransform _root, _canvas;
        private GameObject _tooltipPrefab;
        private bool _up;
        private Box _a;
        private float _k, _f;
        private bool _dirty = true;

        // state; back to the list keeps search, type, category and scroll
        private string _query = "";
        private int _type = 1, _cat = -1, _gridScroll, _gridScrollMax, _pageScroll, _pageScrollMax, _quality = 1, _variant;
        private string _pageId;
        private readonly History _history = new History();
        private List<Entry> _results = new List<Entry>();
        private int _resultsGen = -1, _resultsType = -1, _resultsCat = -2;
        private bool _resultsKnownOnly, _resultsDev;
        private string _resultsQuery;

        // list view
        private GameObject _gridView;
        private TMP_InputField _search;
        private Image _searchBack, _searchGlyph, _track, _thumb, _typeStar;
        private TMP_Text _caption, _empty, _hint;
        private readonly Button[] _types = new Button[6];
        private readonly TMP_Text[] _typeLabels = new TMP_Text[6];
        private readonly Button[] _cats = new Button[CatKeys.Length];
        private readonly Image[] _catGlyphs = new Image[CatKeys.Length];
        private readonly List<Slot> _cells = new List<Slot>();

        // item page
        private GameObject _pageView;
        private Button _back, _fav;
        private Image _backPlate, _pageSocket, _pageIcon, _favOn, _favOff;
        private TMP_Text _crumb, _pageName, _pageType, _pageDesc, _pageMod, _qualLabel;
        private readonly List<Button> _quals = new List<Button>(), _variants = new List<Button>();
        private Image _fromHeader, _usedHeader;
        private TMP_Text _fromLabel, _usedLabel, _fromNote, _usedNote;
        private readonly Pool<Slot> _sockets = new Pool<Slot>(), _inChips = new Pool<Slot>(), _titles = new Pool<Slot>(), _stations = new Pool<Slot>();
        private readonly Pool<TMP_Text> _chances = new Pool<TMP_Text>();
        private readonly Pool<Image> _rules = new Pool<Image>();

        // One pooled interactive element: back plate (or clear hit area), icon, texts, the entry it shows.
        private sealed class Slot
        {
            public RectTransform Rt;
            public Image Back, Icon;
            public TMP_Text A, D;
            public string Id;
            public int Quality = 1;
        }

        private sealed class Pool<T> where T : class
        {
            public readonly List<T> Items = new List<T>();
            public int Used;

            public T Next(Func<int, T> make)
            {
                if (Used == Items.Count) Items.Add(make(Items.Count));
                return Items[Used++];
            }
        }

        // Pointer relay: click / enter callbacks per slot, the tooltip filled on enter (before UITooltip's own handler).
        private sealed class Hit : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler
        {
            public Action<PointerEventData> Click;
            public Action Enter;
            public void OnPointerClick(PointerEventData e) => Click?.Invoke(e);
            public void OnPointerEnter(PointerEventData e) => Enter?.Invoke();
        }

        // ---------------------------------------------------------------- attach / tab wiring

        public static void Attach(InventoryGui gui)
        {
            if (!gui || !gui.m_tabUpgrade || gui.GetComponent<BrowserPanel>()) return;
            gui.gameObject.AddComponent<BrowserPanel>().Init(gui);
        }

        private void Init(InventoryGui gui)
        {
            Instance = this;
            _gui = gui;
            var canvas = gui.GetComponentInParent<Canvas>();
            _canvas = canvas ? (RectTransform)canvas.rootCanvas.transform : null;
            var src = gui.m_tabUpgrade;
            _tab = Instantiate(src, src.transform.parent);
            _tab.name = TabName;
            _tab.onClick = new Button.ButtonClickedEvent(); // drops the persistent OnTabUpgradePressed too
            _tab.onClick.AddListener(Open);
            foreach (var l in _tab.GetComponentsInChildren<Localize>(true)) Destroy(l);
            var label = _tab.GetComponentInChildren<TMP_Text>(true);
            if (label) label.text = Browser.Localize("$ib_tab");
            _tab.transform.SetSiblingIndex(src.transform.GetSiblingIndex() + 1);
            _tab.gameObject.SetActive(true);
            _tab.interactable = true;
            var tip = gui.m_playerGrid && gui.m_playerGrid.m_elementPrefab ? gui.m_playerGrid.m_elementPrefab.GetComponentInChildren<UITooltip>(true) : null;
            _tooltipPrefab = tip ? tip.m_tooltipPrefab : null;
            _root = (RectTransform)new GameObject("MorgottItemBrowser", typeof(RectTransform)).transform;
            _root.SetParent(gui.m_crafting, false);
            Build();
            _root.gameObject.SetActive(false);
            Api.ScreenRegistry.Restyle(gui.m_crafting); // the new tab takes the crafting tab look
        }

        private void OnDestroy()
        {
            Block(false);
            if (Instance == this) Instance = null;
        }

        public void Open()
        {
            if (!_up)
            {
                // The vanilla tab selection (selected = not interactable) comes back on Close (Codex review: a workbench
                // reopened on Upgrade otherwise).
                _craftWas = _gui.m_tabCraft.interactable;
                _upgradeWas = _gui.m_tabUpgrade.interactable;
            }
            _up = true;
            Browser.EnsureReady();
            _resultsGen = -1; // known sets may have grown since the last open
            _dirty = true;
            Enforce();
        }

        private bool _craftWas = true, _upgradeWas = true;
        private float _nextCheck;

        public void Close()
        {
            if (!_up) return;
            _up = false;
            Block(false);
            _root.gameObject.SetActive(false);
            UITooltip.HideTooltip();
            Browser.DropQueue();
            if (_tab) _tab.interactable = true;
            _gui.m_tabCraft.interactable = _craftWas;
            _gui.m_tabUpgrade.interactable = _upgradeWas;
            SetBody(true);
        }

        // Vanilla UpdateCraftingPanel / SetupCrafting and other mods re-set the tab interactables / the vanilla body
        // after our click: the up state is re-applied each frame (change-only writes).
        internal void Enforce()
        {
            if (!_up) return;
            if (_tab.interactable) _tab.interactable = false;
            if (_gui.m_tabCraft.gameObject.activeSelf && !_gui.m_tabCraft.interactable) _gui.m_tabCraft.interactable = true;
            if (_gui.m_tabUpgrade.gameObject.activeSelf && !_gui.m_tabUpgrade.interactable) _gui.m_tabUpgrade.interactable = true;
            SetBody(false);
            if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);
        }

        private void SetBody(bool on)
        {
            foreach (var part in new[] { "RecipeList", "Decription" })
            {
                var t = _gui.m_crafting.Find(part);
                if (t && t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
            }
        }

        // ---------------------------------------------------------------- per frame

        private void Update()
        {
            if (Browser.Hotkey != null && Browser.Hotkey.Value.IsDown() && Player.m_localPlayer && !(Chat.instance && Chat.instance.HasFocus()) && !Console.IsVisible() && !Menu.IsVisible())
            {
                if (InventoryGui.IsVisible() && _up) _gui.Hide();
                else
                {
                    if (!InventoryGui.IsVisible()) _gui.Show(null);
                    Open();
                }
                return;
            }
            if (!_up) return;
            if (!InventoryGui.IsVisible())
            {
                Close();
                return;
            }
            Enforce();
            var typing = _search && _search.isFocused;
            Block(typing);
            // Fingerprint (hash over every recipe) once a second, not per frame.
            if (Time.unscaledTime >= _nextCheck)
            {
                _nextCheck = Time.unscaledTime + 1f;
                if (Browser.EnsureReady() != _resultsGen) _dirty = true;
            }
            if (!typing) Keys();
            Browser.RenderQueued();
        }

        // Game input off while the search field has focus; toggled on change only (BlockInput is shared with other mods).
        private bool _blocked;

        private void Block(bool on)
        {
            if (_blocked == on) return;
            _blocked = on;
            _dirty = true; // the search hint hides while the field has focus
            Jotunn.Managers.GUIManager.BlockInput(on);
        }

        private void Keys()
        {
            if (ZInput.GetMouseButtonDown(3) || ZInput.GetKeyDown(KeyCode.LeftArrow)) { if (_pageId != null) Nav(_history.Back(), false); }
            else if (ZInput.GetMouseButtonDown(4) || ZInput.GetKeyDown(KeyCode.RightArrow)) { var f = _history.Forward(); if (f != null) Nav(f, false); }
            var wheel = ZInput.GetMouseScrollWheel();
            if (wheel == 0f || !RectTransformUtility.RectangleContainsScreenPoint(_root, ZInput.pointerPosition, null)) return;
            // Relayout only when the position moves: a tick at either end changes nothing on screen.
            var step = wheel < 0 ? 1 : -1;
            if (_pageId != null)
            {
                var next = Mathf.Clamp(_pageScroll + step, 0, _pageScrollMax);
                if (next == _pageScroll) return;
                _pageScroll = next;
            }
            else
            {
                var next = Mathf.Clamp(_gridScroll + step, 0, _gridScrollMax);
                if (next == _gridScroll) return;
                _gridScroll = next;
            }
            _dirty = true;
        }

        // CraftingSkin.Apply: the body box (canvas units) while the tab is up.
        public void Place(RectTransform canvas, Box area)
        {
            _canvas = canvas;
            Geo.Place(canvas, _root, area);
            var k = area.W / UnitW;
            if (!_dirty && Same(area, _a) && Mathf.Approximately(k, _k)) { Icons(); return; }
            _a = area;
            _k = k;
            _f = Geo.WorldPerUnit(canvas) / Mathf.Max(_root.lossyScale.x, 1e-6f);
            _dirty = false;
            if (_pageId != null) LayoutPage();
            else LayoutGrid();
        }

        private static bool Same(Box a, Box b) => Mathf.Approximately(a.X, b.X) && Mathf.Approximately(a.Y, b.Y) && Mathf.Approximately(a.W, b.W) && Mathf.Approximately(a.H, b.H);

        // Pending renders land without a relayout: only the icons of visible slots are re-read.
        private void Icons()
        {
            foreach (var list in new[] { _cells, _sockets.Items, _inChips.Items, _stations.Items })
                foreach (var s in list)
                    if (s.Rt.gameObject.activeSelf && s.Id != null && s.Icon && s.Icon.gameObject.activeSelf && !s.Icon.sprite) SetIcon(s.Icon, s.Id);
            if (_pageId != null && !_pageIcon.sprite) SetIcon(_pageIcon, _pageId);
        }

        // ---------------------------------------------------------------- build

        private void Build()
        {
            _gridView = Child("Grid").gameObject;
            var g = _gridView.transform;
            _searchBack = Img(g, "SearchField", "craft.search", true);
            _searchGlyph = Img(_searchBack.transform, "Glyph", "craft.glyph.search");
            _search = Field(_searchBack);
            for (var i = 0; i < _types.Length; i++)
            {
                var n = i;
                _types[i] = Btn(g, "Type" + i, "craft.tab", out _typeLabels[i], () => { _type = n; _gridScroll = 0; _dirty = true; });
            }
            _typeStar = Img(_types[5].transform, "Star", "ib.star.type");
            for (var i = 0; i < _cats.Length; i++)
            {
                var n = i;
                _cats[i] = Btn(g, "Cat" + i, "ib.square", out _, () => { _cat = _cat == n ? -1 : n; _gridScroll = 0; _dirty = true; });
                _catGlyphs[i] = Img(_cats[i].transform, "Glyph", "ib.cat." + CatKeys[i]);
                if (i == 0)
                {
                    // Mockup 04: diagonal sword. Rotated inside an unrotated box (Geo.Place positions by world corners).
                    var box = (RectTransform)new GameObject("GlyphBox", typeof(RectTransform)).transform;
                    box.SetParent(_cats[0].transform, false);
                    var g0 = _catGlyphs[0].rectTransform;
                    g0.SetParent(box, false);
                    g0.anchorMin = Vector2.zero;
                    g0.anchorMax = Vector2.one;
                    g0.offsetMin = g0.offsetMax = Vector2.zero;
                    g0.localRotation = Quaternion.Euler(0f, 0f, -45f);
                }
            }
            _caption = Txt(g, "Caption", "text.body", TextAlignmentOptions.Left);
            _empty = Txt(g, "Empty", "text.muted", TextAlignmentOptions.Top, FontStyles.Italic);
            _empty.textWrappingMode = TextWrappingModes.Normal;
            _track = Img(g, "ScrollTrack", "scrollbar.track");
            _thumb = Img(g, "ScrollThumb", "scrollbar.handle");

            _pageView = Child("Page").gameObject;
            var p = _pageView.transform;
            _backPlate = Img(p, "BackPlate", "ib.square");
            _back = Btn(p, "Back", "craft.arrow.left", out _, () => Nav(_history.Back(), false));
            _crumb = Txt(p, "Crumb", "text.body", TextAlignmentOptions.Left);
            _pageSocket = Img(p, "IconSocket", "plate.side");
            _pageIcon = Img(p, "Icon", "content", true);
            Tip(_pageIcon.gameObject, () => _pageId, () => _quality);
            _pageIcon.gameObject.GetComponent<Hit>().Click = e => { if (e.button == PointerEventData.InputButton.Right) TakeUI(_pageId, _quality, _variant); };
            _pageName = Txt(p, "Name", "text.ember", TextAlignmentOptions.Left, FontStyles.Bold);
            _fav = Btn(p, "Favorite", "ib.square", out _, () => { Browser.ToggleFavorite(_pageId); _resultsGen = -1; _dirty = true; });
            _favOn = Img(_fav.transform, "StarOn", "ib.star.full");
            _favOff = Img(_fav.transform, "StarOff", "ib.star.empty");
            _pageType = Txt(p, "Type", "text.body", TextAlignmentOptions.Left);
            _pageDesc = Txt(p, "Desc", "text.muted", TextAlignmentOptions.TopLeft, FontStyles.Italic);
            _pageDesc.textWrappingMode = TextWrappingModes.Normal;
            _pageMod = Txt(p, "Mod", "text.muted", TextAlignmentOptions.Left, FontStyles.Italic);
            _qualLabel = Txt(p, "QualityLabel", "text.body", TextAlignmentOptions.Right);
            _fromHeader = Img(p, "FromHeader", "decor.plaque");
            _fromLabel = Txt(p, "FromLabel", "text.body", TextAlignmentOptions.Center, FontStyles.Bold);
            _fromNote = Txt(p, "FromNote", "text.muted", TextAlignmentOptions.Center, FontStyles.Italic);
            _usedHeader = Img(p, "UsedHeader", "decor.plaque");
            _usedLabel = Txt(p, "UsedLabel", "text.body", TextAlignmentOptions.Center, FontStyles.Bold);
            _usedNote = Txt(p, "UsedNote", "text.muted", TextAlignmentOptions.Center, FontStyles.Italic);
            _pageView.SetActive(false);
        }

        private RectTransform Child(string name)
        {
            var rt = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rt.SetParent(_root, false);
            return rt;
        }

        private static Image Img(Transform parent, string name, string role, bool raycast = false)
        {
            var image = role == "content" ? new GameObject(name, typeof(RectTransform)).AddComponent<Image>() : InfoArt.OwnImage(parent, name, role, "item browser " + name);
            if (role == "content")
            {
                image.rectTransform.SetParent(parent, false);
                image.preserveAspect = true;
                DollCell.Mark(image, "content", "item browser icon");
            }
            image.raycastTarget = raycast;
            return image;
        }

        private static TMP_Text Txt(Transform parent, string name, string role, TextAlignmentOptions align, FontStyles style = FontStyles.Normal)
        {
            var t = InfoArt.RowText(parent, name, role, align);
            OwnFit.Claim(t); // sized by this layout: TextInset's truncation shrink took ellipsized chips down to 12 (run 20261104-225215)
            t.fontStyle = style;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        // Button with a plate role and a label (arrows: glyph only).
        private static Button Btn(Transform parent, string name, string role, out TMP_Text label, Action click)
        {
            var image = InfoArt.OwnImage(parent, name, role, "item browser button " + name);
            image.preserveAspect = role.StartsWith("craft.arrow", StringComparison.Ordinal);
            image.raycastTarget = true;
            var b = image.gameObject.AddComponent<Button>();
            b.targetGraphic = image;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.onClick.AddListener(() => click());
            // Styled after the Button exists: the role's sprite states go on its targetGraphic (CraftingSkin.StepButton).
            if (image.GetComponent<SkinMark>() is SkinMark m) m.Outcome = Restyler.Apply(image, m, ThemeTokens.Role(role)) ? Outcome.Styled : Outcome.Degraded;
            label = role.StartsWith("craft.arrow", StringComparison.Ordinal) ? null : Txt(image.transform, "Label", "text.body", TextAlignmentOptions.Center);
            return b;
        }

        private TMP_InputField Field(Image back)
        {
            var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            area.SetParent(back.rectTransform, false);
            var text = Txt(area, "Text", "text.body", TextAlignmentOptions.Left);
            // Hint as a sibling of the field, shown by the layout (inside the RectMask2D viewport the TMP placeholder
            // stayed invisible in run 20261104-213108).
            _hint = Txt(_gridView.transform, "SearchHint", "text.muted", TextAlignmentOptions.Left, FontStyles.Italic);
            var f = back.gameObject.AddComponent<TMP_InputField>();
            f.textViewport = area;
            f.textComponent = text;
            f.targetGraphic = back;
            f.transition = Selectable.Transition.None;
            f.navigation = new Navigation { mode = Navigation.Mode.None };
            f.onValueChanged.AddListener(s =>
            {
                _query = s;
                _gridScroll = 0;
                if (_pageId != null) { _pageId = null; _history.Clear(); }
                _dirty = true;
            });
            return f;
        }

        // role null = clear hit area (no CellFit cell: its text children stay where the layout puts them).
        private Slot NewSlot(Transform parent, string name, string role)
        {
            var s = new Slot { Back = role != null ? InfoArt.OwnImage(parent, name, role, "item browser " + name) : new GameObject(name, typeof(RectTransform)).AddComponent<Image>() };
            if (role == null)
            {
                s.Back.rectTransform.SetParent(parent, false);
                s.Back.color = Color.clear;
                DollCell.Mark(s.Back, "content", "item browser hit area");
            }
            s.Back.preserveAspect = false;
            s.Back.raycastTarget = true;
            s.Rt = s.Back.rectTransform;
            s.Icon = Img(s.Rt, "Icon", "content");
            // Left opens the entry's page, right takes it (at the slot's quality), middle toggles the favorite.
            Hook(s, e =>
            {
                if (s.Id == null) return;
                if (e.button == PointerEventData.InputButton.Right) TakeUI(s.Id, s.Quality, 0);
                else if (e.button == PointerEventData.InputButton.Middle) { Browser.ToggleFavorite(s.Id); _resultsGen = -1; _dirty = true; }
                else OpenPage(s.Id);
            });
            Tip(s.Back.gameObject, () => s.Id, () => s.Quality);
            return s;
        }

        private Slot Chip(string name, string textRole)
        {
            var s = NewSlot(_pageView.transform, name, null);
            s.A = Txt(s.Rt, "Text", textRole, TextAlignmentOptions.Left, textRole == "text.ember" ? FontStyles.Bold : FontStyles.Normal);
            return s;
        }

        private static void Hook(Slot s, Action<PointerEventData> click) => s.Back.gameObject.AddComponent<Hit>().Click = click;

        // Vanilla tooltip (UITooltip + the player grid's tooltip prefab), text filled on enter from the vanilla path.
        private void Tip(GameObject go, Func<string> id, Func<int> quality)
        {
            var hit = go.GetComponent<Hit>() ?? go.AddComponent<Hit>();
            if (!_tooltipPrefab) return;
            var tip = go.AddComponent<UITooltip>();
            tip.m_tooltipPrefab = _tooltipPrefab;
            hit.Enter = () =>
            {
                var e = id() is string s && Browser.Index != null && Browser.Index.Entries.TryGetValue(s, out var x) ? x : null;
                if (e == null) { tip.Set("", ""); return; }
                Browser.Tooltip(e, quality(), out var topic, out var text);
                tip.Set(topic, text);
            };
        }

        // ---------------------------------------------------------------- layout helpers (mockup units)

        private void Put(Component c, float x, float y, float w, float h)
        {
            var rt = (RectTransform)c.transform;
            if (!rt.gameObject.activeSelf) rt.gameObject.SetActive(true);
            Geo.Place(_canvas, rt, new Box(_a.X + x * _k, _a.Y + y * _k, w * _k, h * _k));
        }

        // Text shrunk to fit its box (Russian labels are longer than the mockup's).
        private void Fit(TMP_Text t, string s, float size, float min = 0.85f)
        {
            if (t.text != s) t.text = s;
            var fs = size * _k * _f;
            if (t.enableAutoSizing && Mathf.Abs(t.fontSizeMax - fs) < 0.05f && Mathf.Abs(t.fontSizeMin - fs * min) < 0.05f) return;
            t.enableAutoSizing = true;
            t.fontSizeMax = fs;
            t.fontSizeMin = fs * min;
        }

        private static void Off(Component c)
        {
            if (c && c.gameObject.activeSelf) c.gameObject.SetActive(false);
        }

        private void Text(TMP_Text t, string s, float size)
        {
            if (t.text != s) t.text = s;
            var fs = size * _k * _f;
            if (t.enableAutoSizing) t.enableAutoSizing = false; // a pooled text Fit sized before keeps no autosize
            if (Mathf.Abs(t.fontSize - fs) > 0.05f) t.fontSize = fs;
        }

        private static void SetIcon(Image image, string id)
        {
            var s = id != null ? Browser.Icon(id, out _) : null;
            if (image.sprite != s) image.sprite = s;
            if (image.enabled != (bool)s) image.enabled = s;
        }

        // Selected = not interactable (craft.category's disabled sprite is its selected plate).
        private void Select(Button b, bool on)
        {
            if (b.interactable == on) b.interactable = !on;
            PlateFit((Image)b.targetGraphic, b.targetGraphic.GetComponent<SkinMark>()?.Role == "ib.square" ? 0.8f : 2.2f); // squares: a full cyan rim; tabs: light corners
        }

        private static string L(string token) => Browser.Localize(token);
        private static string NameOf(string id) => Browser.Index != null && Browser.Index.Entries.TryGetValue(id, out var e) ? L(e.Name) : id;
        private static string CatName(ItemCategory c) => L("$ib_cat_" + CatKeys[(int)c]);

        // ---------------------------------------------------------------- list view (mockup 04)

        private List<Entry> Results()
        {
            var known = Browser.KnownOnly.Value;
            var dev = Browser.Devcommands;
            var cat = _type == 1 ? _cat : -1;
            if (_resultsGen == Browser.Generation && _resultsQuery == _query && _resultsType == _type && _resultsCat == cat && _resultsKnownOnly == known && _resultsDev == dev) return _results;
            var fav = _type == 5 ? Browser.Favorites() : null;
            _results = Browser.Index.Search(_query, TypeOf[_type],
                e => (cat < 0 || (int)e.Category == cat) && (!known || Browser.Known(e)) && (fav == null || fav.Contains(e.Id)), L, Browser.ModName, dev);
            _resultsDev = dev;
            _resultsGen = Browser.Generation;
            _resultsQuery = _query;
            _resultsType = _type;
            _resultsCat = cat;
            _resultsKnownOnly = known;
            return _results;
        }

        private void LayoutGrid()
        {
            Off(_pageView.transform);
            Put(_gridView.transform, 0f, 0f, UnitW, _a.H / _k);
            if (Browser.Index == null) return;
            var h = _a.H / _k;
            Put(_searchBack, 0f, 0f, UnitW, 36f);
            Put(_searchGlyph, 10f, 7f, 22f, 22f);
            Put(_search.textViewport, 40f, 2f, 500f, 32f);
            foreach (var t in _search.textViewport.GetComponentsInChildren<TMP_Text>(true)) { Put(t, 40f, 2f, 500f, 32f); Text(t, t.text, 20f); }
            if (_query.Length == 0 && !_search.isFocused)
            {
                Put(_hint, 40f, 2f, 500f, 32f);
                Text(_hint, L("$ib_search"), 20f);
            }
            else Off(_hint);
            var x = 0f;
            for (var i = 0; i < _types.Length; i++)
            {
                Put(_types[i], x, TypeY, TypeW[i], TypeH);
                if (i == 5) Put(_typeStar, x + 7f, TypeY + 6f, TypeW[i] - 14f, TypeH - 12f);
                else
                {
                    Put(_typeLabels[i], x + 3f, TypeY, TypeW[i] - 6f, TypeH);
                    Fit(_typeLabels[i], L(TypeTokens[i]), 19f);
                }
                Select(_types[i], i == _type);
                x += TypeW[i] + 14f;
            }
            var items = _type == 1;
            for (var i = 0; i < _cats.Length; i++)
            {
                if (!items) { Off(_cats[i]); continue; }
                var cx = i * (UnitW - CatH) / (_cats.Length - 1);
                Put(_cats[i], cx, CatY, CatH, CatH);
                Put(i == 0 ? _catGlyphs[i].transform.parent : _catGlyphs[i].transform, cx + 7f, CatY + 7f, CatH - 14f, CatH - 14f);
                Select(_cats[i], i == _cat);
            }
            var list = Results();
            var capY = items ? 149f : CatY;
            Put(_caption, 0f, capY, UnitW, 26f);
            Fit(_caption, $"{(items && _cat >= 0 ? CatName((ItemCategory)_cat) : _type == 5 ? L("$ib_type_favorites") : L(TypeTokens[_type]))} · {list.Count}", 20f);

            var top = capY + 37f;
            var rows = Math.Max(1, (int)((h - top + 4f) / CellPitchY));
            var per = rows * 6;
            var total = (list.Count + 5) / 6;
            _gridScrollMax = Math.Max(0, total - rows);
            _gridScroll = Mathf.Clamp(_gridScroll, 0, _gridScrollMax);
            while (_cells.Count < per) _cells.Add(GridCell(_cells.Count));
            var fav = Browser.Favorites();
            for (var i = 0; i < _cells.Count; i++)
            {
                var c = _cells[i];
                var n = _gridScroll * 6 + i;
                if (i >= per || n >= list.Count) { c.Id = null; Off(c.Back); Off(c.A); continue; }
                c.Id = list[n].Id;
                float cx = i % 6 * CellPitchX, cy = top + i / 6 * CellPitchY;
                Put(c.Back, cx, cy, CellW, CellH);
                Put(c.Icon, cx + 8f, cy + 5f, CellW - 16f, CellH - 10f);
                SetIcon(c.Icon, c.Id);
                Put(c.D, cx + CellW - 22f, cy + 2f, 18f, 18f);
                Text(c.D, "★", 16f);
                c.D.enabled = fav.Contains(c.Id);
                Put(c.A, cx, cy + CellH + 2f, CellW, 38f); // within the cell width: neighbours never touch
                Fit(c.A, L(list[n].Name), 16f);
            }
            // Thin scrollbar: track over the grid's rows, thumb = visible share.
            if (_gridScrollMax > 0)
            {
                var viewH = rows * CellPitchY - 8f;
                var thumbH = Mathf.Max(24f, viewH * rows / total);
                Put(_track, UnitW - 5f, top, 4f, viewH);
                Put(_thumb, UnitW - 6f, top + (viewH - thumbH) * _gridScroll / _gridScrollMax, 6f, thumbH);
            }
            else { Off(_track); Off(_thumb); }
            // Known-only is config-only (no checkbox): an empty list names the filter.
            if (list.Count == 0 && Browser.KnownOnly.Value)
            {
                Put(_empty, 20f, top + 20f, UnitW - 40f, 80f);
                Fit(_empty, L("$ib_empty_known"), 18f);
            }
            else Off(_empty);
        }

        private Slot GridCell(int i)
        {
            var s = NewSlot(_gridView.transform, "Cell" + i, "craft.socket");
            s.D = Txt(s.Rt, "FavStar", "text.ember", TextAlignmentOptions.Center);
            s.D.raycastTarget = false;
            // Name under the socket, a sibling of it: as a child CellFit.KeepTexts pulls it into the cell (467b038).
            s.A = Txt(_gridView.transform, "CellName" + i, "text.body", TextAlignmentOptions.Top);
            s.A.textWrappingMode = TextWrappingModes.Normal;
            return s;
        }

        private static void TakeUI(string id, int quality, int variant)
        {
            if (id == null || !Browser.CanTake) return;
            var shift = ZInput.GetKey(KeyCode.LeftShift) || ZInput.GetKey(KeyCode.RightShift);
            var ctrl = ZInput.GetKey(KeyCode.LeftControl) || ZInput.GetKey(KeyCode.RightControl);
            var why = Browser.Take(id, quality, ctrl ? Browser.TakeMode.Materials : shift ? Browser.TakeMode.Stack : Browser.TakeMode.One, variant);
            if (why != null) Player.m_localPlayer?.Message(MessageHud.MessageType.Center, why);
        }

        private static string TypeName(Entry e)
        {
            if (e.Type == EntryType.Item && Browser.Prefab(e.Id) is GameObject go && go.TryGetComponent<ItemDrop>(out var d))
                return $"{CatName(e.Category)} · {L("$ib_itemtype_" + d.m_itemData.m_shared.m_itemType.ToString().ToLowerInvariant())}";
            return L("$ib_entry_" + e.Type.ToString().ToLowerInvariant());
        }

        private static string Crumb(Entry e) => e.Type switch
        {
            EntryType.Item => $"{L("$ib_type_items")} > {CatName(e.Category)}",
            EntryType.Piece => L("$ib_type_pieces"),
            EntryType.Creature => L("$ib_type_creatures"),
            EntryType.Station => L("$ib_type_stations"),
            _ => L("$ib_type_all")
        };

        // ---------------------------------------------------------------- item page (mockup 05)

        private void OpenPage(string id)
        {
            if (id == null || Browser.Index == null || !Browser.Index.Entries.ContainsKey(id)) return;
            Nav(id, true);
        }

        // id null = back to the list (search, type, category and its scroll kept).
        private void Nav(string id, bool track)
        {
            if (track) _history.Go(id);
            if (id == null) _history.Clear();
            _pageId = id;
            _pageScroll = 0;
            _quality = 1;
            _variant = 0;
            _dirty = true;
        }

        private void LayoutPage()
        {
            Off(_gridView.transform);
            if (Browser.Index == null || !Browser.Index.Entries.TryGetValue(_pageId, out var e)) { _pageId = null; LayoutGrid(); return; }
            var ix = Browser.Index;
            var h = _a.H / _k;
            Put(_pageView.transform, 0f, 0f, UnitW, h);
            Put(_backPlate, 0f, 0f, 38f, 36f);
            PlateFit(_backPlate, 1.6f);
            Put(_back, 8f, 7f, 22f, 22f);
            Put(_crumb, 56f, 0f, UnitW - 56f, 36f);
            Fit(_crumb, Crumb(e), 20f);

            // Header: icon, name, favorite, type, description, mod + prefab id, quality selector.
            Put(_pageSocket, 0f, 50f, 100f, 100f);
            PlateFit(_pageSocket, 0.6f); // a heavier bevel than the list sockets (mockup 05 header)
            Put(_pageIcon, 8f, 58f, 84f, 84f);
            SetIcon(_pageIcon, e.Id);
            Put(_pageName, 113f, 50f, 390f, 34f);
            Fit(_pageName, L(e.Name), 28f);
            Put(_fav, UnitW - 38f, 46f, 36f, 36f);
            PlateFit((Image)_fav.targetGraphic, 1.6f);
            var isFav = Browser.Favorites().Contains(e.Id);
            if (isFav) { Put(_favOn, UnitW - 32f, 52f, 24f, 24f); Off(_favOff); }
            else { Put(_favOff, UnitW - 32f, 52f, 24f, 24f); Off(_favOn); }
            Put(_pageType, 113f, 86f, 250f, 22f);
            Fit(_pageType, TypeName(e), 18f);
            Put(_pageDesc, 113f, 109f, 250f, 24f);
            Text(_pageDesc, System.Text.RegularExpressions.Regex.Replace(L(e.Desc) ?? "", "</?size[^>]*>", ""), 16f); // mods size their own tags
            Put(_pageMod, 113f, 133f, 250f, 20f);
            Text(_pageMod, $"{Browser.ModName(e.Mod)} · {e.Id}", 15f);
            _quality = Mathf.Clamp(_quality, 1, e.MaxQuality);
            var top = 152f;
            if (e.MaxQuality > 1)
            {
                Put(_qualLabel, 360f, 108f, 48f, 31f);
                Fit(_qualLabel, L("$ib_quality_short"), 18f);
                // Four per line; modded qualities above 4 wrap to the next line.
                while (_quals.Count < e.MaxQuality)
                {
                    var n = _quals.Count + 1;
                    _quals.Add(Btn(_pageView.transform, "Q" + n, "ib.square", out _, () => { _quality = n; _variant = 0; _pageScroll = 0; _dirty = true; }));
                }
                for (var i = 0; i < _quals.Count; i++)
                {
                    if (i >= e.MaxQuality) { Off(_quals[i]); continue; }
                    float qx = 412f + i % 4 * 35f, qy = 108f + i / 4 * 35f;
                    Put(_quals[i], qx, qy, 31f, 31f);
                    var lab = _quals[i].transform.Find("Label").GetComponent<TMP_Text>();
                    Put(lab, qx, qy, 31f, 31f);
                    Fit(lab, (i + 1).ToString(), 18f);
                    Select(_quals[i], i + 1 == _quality);
                    top = Mathf.Max(top, qy + 35f);
                }
            }
            else
            {
                Off(_qualLabel);
                foreach (var q in _quals) Off(q);
            }
            // Recipe variants at this quality (two or more): pick the one the Ctrl take gives.
            var variants = PageModel.Variants(ix, e.Id, _quality);
            _variant = Mathf.Clamp(_variant, 0, Math.Max(0, variants.Count - 1));
            var vLabels = variants.Count > 1 ? PageModel.VariantLabels(variants, NameOf, L("$ib_by_hand"), L("$ib_level_short"), L("$ib_variant")) : new List<string>();
            while (_variants.Count < vLabels.Count)
            {
                var n = _variants.Count;
                _variants.Add(Btn(_pageView.transform, "Variant" + n, "craft.tab", out _, () => { _variant = n; _dirty = true; }));
            }
            var vy = Mathf.Max(top, 158f) + 8f;
            for (var i = 0; i < _variants.Count; i++)
            {
                if (i >= vLabels.Count) { Off(_variants[i]); continue; }
                float bx = 2f + i % 3 * 177f, by = vy + i / 3 * 42f;
                Put(_variants[i], bx, by, 163f, 34f);
                var lab = _variants[i].transform.Find("Label").GetComponent<TMP_Text>();
                Put(lab, bx + 4f, by, 155f, 34f);
                Fit(lab, vLabels[i], 18f);
                Select(_variants[i], i == _variant);
                top = by + 34f;
            }
            top += 18f;

            // Scrolled body: the two sections as blocks of real heights, scrolled block by block.
            _sockets.Used = _inChips.Used = _titles.Used = _stations.Used = _chances.Used = _rules.Used = 0;
            var from = PageModel.Obtain(ix, e.Id, _quality);
            var used = PageModel.UsedIn(ix, e.Id);
            var blocks = new List<(int Kind, PageRow Row)> { (0, null) };
            if (from.Count == 0) blocks.Add((2, null));
            foreach (var r in from) blocks.Add((1, r));
            blocks.Add((3, null));
            if (used.Count == 0) blocks.Add((5, null));
            foreach (var r in used) blocks.Add((4, r));
            var heights = new List<float>(blocks.Count);
            foreach (var b in blocks) heights.Add(b.Kind == 0 || b.Kind == 3 ? 44f : b.Kind == 2 || b.Kind == 5 ? 34f : PageModel.Height(b.Row, b.Kind == 4));
            _pageScrollMax = PageModel.MaxFirst(heights, h - top);
            _pageScroll = Mathf.Clamp(_pageScroll, 0, _pageScrollMax);
            var shown = new bool[6];
            var y = top;
            for (var i = _pageScroll; i < blocks.Count && (i == _pageScroll || y + heights[i] <= h + 0.5f); i++)
            {
                var (kind, row) = blocks[i];
                shown[kind] = true;
                switch (kind)
                {
                    case 0: Header(_fromHeader, _fromLabel, L("$ib_obtained_from"), y); break;
                    case 3: Header(_usedHeader, _usedLabel, L("$ib_used_in"), y); break;
                    case 2: Put(_fromNote, 0f, y, UnitW, 30f); Fit(_fromNote, L("$ib_no_source"), 17f); break;
                    case 5: Put(_usedNote, 0f, y, UnitW, 30f); Fit(_usedNote, L("$ib_not_used"), 17f); break;
                    default: Row(row, y, heights[i], kind == 4); break;
                }
                y += heights[i];
            }
            if (!shown[0]) { Off(_fromHeader); Off(_fromLabel); }
            if (!shown[3]) { Off(_usedHeader); Off(_usedLabel); }
            if (!shown[2]) Off(_fromNote);
            if (!shown[5]) Off(_usedNote);
            Rest(_sockets);
            Rest(_inChips);
            Rest(_titles);
            Rest(_stations);
            for (var i = _chances.Used; i < _chances.Items.Count; i++) Off(_chances.Items[i]);
            for (var i = _rules.Used; i < _rules.Items.Count; i++) Off(_rules.Items[i]);
        }

        private static void Rest(Pool<Slot> p)
        {
            for (var i = p.Used; i < p.Items.Count; i++)
            {
                p.Items[i].Id = null;
                Off(p.Items[i].Back);
            }
        }

        private void Header(Image plate, TMP_Text label, string text, float y)
        {
            Put(plate, 0f, y, UnitW, 36f);
            PlateFit(plate, 1f); // knotwork ends scale with the band
            Put(label, 100f, y, UnitW - 200f, 36f);
            Fit(label, text, 20f);
        }

        // One page row (mockup 05): "Obtained from" = socket + inputs (or source name + chance) + station; "Used in" = socket,
        // ember title (+quality), inputs line(s), station. A divider closes every row.
        private void Row(PageRow r, float y, float h, bool used)
        {
            var sock = _sockets.Next(n => NewSlot(_pageView.transform, "RowIcon" + n, "craft.socket"));
            sock.Id = r.Icon;
            sock.Quality = Math.Max(1, r.Quality);
            float s = used ? 74f : 59f, sh = used ? 72f : 54f;
            Put(sock.Back, 4f, y + 4f, s, sh);
            Put(sock.Icon, 10f, y + 9f, s - 12f, sh - 10f);
            SetIcon(sock.Icon, r.Icon);
            var x0 = used ? 94f : 81f;
            var iy = y + (used ? 38f : (h - PageModel.LineH * r.Lines) / 2f);
            if (used)
            {
                var title = _titles.Next(n => Chip("RowTitle" + n, "text.ember"));
                title.Id = r.Icon;
                title.Quality = sock.Quality;
                Chip(title, x0, y + 6f, 290f, 30f, 0f, NameOf(r.Icon) + (r.Quality > 1 ? " +" + r.Quality : ""), 22f);
            }
            else if (!r.Recipe)
            {
                var src = _stations.Next(n => Chip("Station" + n, "text.body"));
                src.Id = r.Icon;
                src.Quality = 1;
                Chip(src, x0, y, 290f, h, 0f, NameOf(r.Icon), 19f);
            }
            // Two chips per line, ending 10 units before the station rule (x 390); the amount has its own label so a
            // long name loses letters, never its quantity.
            var chipW = (380f - x0 - 6f) / 2f;
            for (var i = 0; i < r.Inputs.Length; i++)
            {
                var c = _inChips.Next(n =>
                {
                    var s = Chip("InChip" + n, "text.accent");
                    s.D = Txt(s.Rt, "Amount", "text.accent", TextAlignmentOptions.Left);
                    return s;
                });
                var st = r.Inputs[i];
                c.Id = st.Prefab;
                c.Quality = i == 0 && r.BaseQuality > 0 ? r.BaseQuality : 1;
                var cx = x0 + i % 2 * (chipW + 6f);
                var cy = iy + i / 2 * PageModel.LineH;
                var amount = "x" + Amount(st);
                var amountW = 10f * amount.Length + 4f;
                Chip(c, cx, cy, chipW - amountW, PageModel.LineH, 34f, NameOf(st.Prefab), 18f);
                // Amount right after the name (rendered width, local -> layout units), at most at the chip's end.
                var nameW = chipW - amountW - 40f;
                var local = c.A.rectTransform.rect.width;
                var shown = local > 0f ? Mathf.Min(nameW, c.A.GetPreferredValues(c.A.text).x * nameW / local) : nameW;
                Put(c.D, cx + 40f + shown + 5f, cy, amountW, PageModel.LineH);
                Text(c.D, amount, 18f);
            }
            if (r.Right != null || r.Recipe && r.Inputs.Length > 0)
            {
                Rule(390f, y + 8f, 1.5f, h - 16f);
                var st = _stations.Next(n => Chip("Station" + n, "text.body"));
                st.Id = r.Right;
                st.Quality = 1;
                var label = r.Right == null ? L("$ib_by_hand") : r.R.StationLevel > 1 ? $"{NameOf(r.Right)} {L("$ib_level_short")}{r.R.StationLevel}" : NameOf(r.Right);
                // Output amount / conditions ("any one ingredient", per star) under the station when there are any.
                var note = Chance(r, used ? r.Icon : _pageId);
                Chip(st, 398f, note.Length > 0 ? y + 2f : y + (h - 42f) / 2f, UnitW - 398f, note.Length > 0 ? 34f : 42f, r.Right == null ? 0f : note.Length > 0 ? 34f : 40f, label, 18f);
                if (note.Length > 0)
                {
                    var t = _chances.Next(n => Txt(_pageView.transform, "Chance" + n, "text.accent", TextAlignmentOptions.Right));
                    Put(t, 398f, y + 34f, UnitW - 400f, 24f);
                    Fit(t, note, 15f);
                }
            }
            else
            {
                var t = _chances.Next(n => Txt(_pageView.transform, "Chance" + n, "text.accent", TextAlignmentOptions.Right));
                Put(t, 330f, y, UnitW - 340f, h);
                Fit(t, Chance(r, used ? r.Icon : _pageId), 18f);
            }
            Rule(0f, y + h - 2f, UnitW, 1.5f);
        }

        // Clear hit area with an optional icon (size 0 = text only) and its text.
        private void Chip(Slot c, float x, float y, float w, float h, float icon, string text, float size)
        {
            Put(c.Back, x, y, w, h);
            if (icon > 0f)
            {
                Put(c.Icon, x, y + (h - icon) / 2f, icon, icon);
                SetIcon(c.Icon, c.Id);
            }
            else Off(c.Icon);
            var tx = icon > 0f ? icon + 6f : 0f;
            Put(c.A, x + tx, y, w - tx, h);
            Text(c.A, text, size); // one size per row (a long name gets an ellipsis, never a smaller font)
        }

        // Sliced plate drawn at its own proportions for this height (CraftingSkin.FrameInset formula); thin > 1 = thinner
        // borders (small buttons: the tab art's rim at ~1/4 of its height crowded the numerals).
        private void PlateFit(Image plate, float thin)
        {
            var s = plate.sprite;
            var h = plate.rectTransform.rect.height;
            var canvas = _canvas ? _canvas.GetComponent<Canvas>() : null;
            if (!s || h <= 0f || !canvas) return;
            var mult = thin * s.rect.height * canvas.referencePixelsPerUnit / (s.pixelsPerUnit * h);
            if (Mathf.Abs(plate.pixelsPerUnitMultiplier - mult) > 0.001f) plate.pixelsPerUnitMultiplier = mult;
        }

        private void Rule(float x, float y, float w, float h) => Put(_rules.Next(n => Img(_pageView.transform, "Rule" + n, "ib.rule")), x, y, w, h);

        private static string Amount(Stack s) => s.Min == s.Max ? s.Min.ToString() : $"{s.Min}-{s.Max}";

        // Source row / creature drop: amount, base chance (weighted tables: no number), conditions.
        private static string Chance(PageRow r, string item)
        {
            var o = r.R.Outputs.Length > 0 ? r.R.Outputs[0] : default;
            foreach (var s in r.R.Outputs) if (s.Prefab == item) o = s;
            var parts = new List<string>();
            if (o.Prefab != null && !(o.Min == 1 && o.Max == 1)) parts.Add("x" + Amount(o));
            if (float.IsNaN(o.Chance)) parts.Add(L("$ib_weighted"));
            else if (o.Chance < 1f) parts.Add($"{L("$ib_chance")} {o.Chance * 100f:0.#}%");
            if (r.R.Conditions != null) parts.Add(L(r.R.Conditions));
            return string.Join("  ", parts);
        }
    }
}
