using System;
using System.Collections.Generic;
using System.Linq;
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
    // The ITEMS tab of the crafting window (docs\item-browser-design.md section 1, mockups docs\mockups\item-browser\
    // a-crafting-tab-grid.png = grid view, b-crafting-tab-sources.png = item page). Tab = clone of m_tabUpgrade in the
    // tab row as VNEI does (MainVneiHandler.GetTabButton); up = RecipeList + Decription off, this panel over the crafting
    // body (CraftingSkin.Apply places it in the box VNEI's browser had). Everything is laid out in mockup units: the
    // panel is 550 units wide, every box below is scaled by the body width.
    public sealed class BrowserPanel : MonoBehaviour
    {
        public const string TabName = "MorgottItemsTab";
        private const float UnitW = 550f, Cell = 61f, RowH = 76f;
        // Mockup A grid (docs\mockups\item-browser\a-crafting-tab-grid.png at 1920x1080): tall sockets 56x78, pitch 60 x 84,
        // grid well left of a detail well, both from y 90; pager + hint row under the wells.
        private const float WellTop = 108f, CellX = 60f, CellY = 84f, CellTop = 124f, Footer = 82f, GridRows = 6f;
        private static readonly string[] TypeTokens = { "$ib_type_all", "$ib_type_items", "$ib_type_pieces", "$ib_type_creatures", "$ib_type_stations", "$ib_type_favorites" };
        private static readonly float[] TypeW = { 58f, 74f, 72f, 96f, 92f, 118f };
        private static readonly EntryType?[] TypeOf = { null, EntryType.Item, EntryType.Piece, EntryType.Creature, EntryType.Station, null };

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

        // state
        private string _query = "";
        private int _type = 1, _gridPage, _rowScroll, _rowScrollMax, _gridPages = 1; // maxima from the last layout
        private bool _usedTab;
        private string _selected, _pageId;
        private readonly History _history = new History();
        private List<Entry> _results = new List<Entry>();
        private int _resultsGen = -1;
        private bool _resultsKnownOnly, _resultsDev;
        private string _resultsQuery;
        private int _resultsType = -1;

        // grid view
        private TMP_InputField _search;
        private Image _searchBack, _searchGlyph, _knownBox, _knownCheck;
        private TMP_Text _knownLabel, _pagerText, _hint;
        private Button _knownButton, _prev, _next;
        private readonly Button[] _types = new Button[6];
        private readonly TMP_Text[] _typeLabels = new TMP_Text[6];
        private readonly List<Slot> _cells = new List<Slot>();
        private Image _gridWell, _prevPlate, _nextPlate, _detailWell, _detailIcon, _divTop, _divBottom, _stationIcon;
        private TMP_Text _detailName, _detailType, _stationText, _arrow, _noRecipe;
        private readonly List<Slot> _reqRows = new List<Slot>();
        private Slot _outRow;
        private Button _usedButton, _fromButton;
        private GameObject _gridView;

        // item page
        private GameObject _pageView;
        private Button _back;
        private TMP_Text _crumb, _pageName, _pageType, _pageDesc;
        private Image _pageIcon, _backPlate, _pageDivTop, _pageDivBottom, _pageDivTabs;
        private Button _fromTab, _usedTabButton;
        private TMP_Text _fromTabLabel, _usedTabLabel;
        private readonly List<Slot> _rows = new List<Slot>();
        private readonly List<Slot> _tiles = new List<Slot>();

        // One pooled interactive element: back plate, icon, up to three texts, the entry it shows.
        private sealed class Slot
        {
            public RectTransform Rt;
            public Image Back, Icon;
            public TMP_Text A, B, C, D;
            public string Id;
            public int Quality = 1;
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
            Jotunn.Managers.GUIManager.BlockInput(on);
        }

        private void Keys()
        {
            if (ZInput.GetMouseButtonDown(3) || ZInput.GetKeyDown(KeyCode.LeftArrow)) Nav(_history.Back(), false);
            else if (ZInput.GetMouseButtonDown(4) || ZInput.GetKeyDown(KeyCode.RightArrow)) { var f = _history.Forward(); if (f != null) Nav(f, false); }
            var wheel = ZInput.GetMouseScrollWheel();
            if (wheel == 0f || !RectTransformUtility.RectangleContainsScreenPoint(_root, ZInput.pointerPosition, null)) return;
            // Relayout only when the position moves: a tick at either end changes nothing on screen.
            var step = wheel < 0 ? 1 : -1;
            if (_pageId != null)
            {
                var next = Mathf.Clamp(_rowScroll + step, 0, _rowScrollMax);
                if (next == _rowScroll) return;
                _rowScroll = next;
            }
            else
            {
                var next = Mathf.Clamp(_gridPage + step, 0, _gridPages - 1);
                if (next == _gridPage) return;
                _gridPage = next;
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
            foreach (var list in new[] { _cells, _rows, _tiles })
                foreach (var s in list)
                    if (s.Rt.gameObject.activeSelf && s.Id != null && s.Icon && !s.Icon.sprite) SetIcon(s.Icon, s.Id);
        }

        // ---------------------------------------------------------------- build

        private void Build()
        {
            _gridView = Child("Grid").gameObject;
            var g = _gridView.transform;
            _searchBack = Img(g, "SearchField", "craft.search", true);
            _searchGlyph = Img(_searchBack.transform, "Glyph", "craft.glyph.search");
            _search = Field(_searchBack);
            _knownButton = Btn(g, "KnownOnly", null, out _, () => { Browser.KnownOnly.Value = !Browser.KnownOnly.Value; _dirty = true; });
            _knownBox = Img(_knownButton.transform, "Box", "toggle.box");
            _knownCheck = Img(_knownBox.transform, "Check", "toggle.check");
            _knownLabel = Txt(_knownButton.transform, "Label", "text.body", TextAlignmentOptions.Left);
            for (var i = 0; i < _types.Length; i++)
            {
                var n = i;
                _types[i] = Btn(g, "Type" + i, "craft.category", out _typeLabels[i], () => { _type = n; _gridPage = 0; _dirty = true; });
            }
            _gridWell = Img(g, "GridWell", "craft.well");
            _detailWell = Img(g, "DetailWell", "craft.well");
            _detailIcon = Img(g, "DetailIcon", "content", true);
            Tip(_detailIcon.gameObject, () => _selected, () => 1);
            _detailName = Txt(g, "DetailName", "text.body", TextAlignmentOptions.Center, FontStyles.Bold);
            _detailType = Txt(g, "DetailType", "text.muted", TextAlignmentOptions.Center, FontStyles.Italic);
            _divTop = Img(g, "DividerTop", "divider");
            _divBottom = Img(g, "DividerBottom", "divider");
            _stationIcon = Img(g, "StationIcon", "content");
            _stationText = Txt(g, "Station", "text.body", TextAlignmentOptions.Left);
            _arrow = Txt(g, "Arrow", "text.body", TextAlignmentOptions.Center);
            _arrow.text = "↓";
            _noRecipe = Txt(g, "NoRecipe", "text.muted", TextAlignmentOptions.Center, FontStyles.Italic);
            for (var i = 0; i < 6; i++) _reqRows.Add(LineSlot(g, "Req" + i));
            _outRow = LineSlot(g, "Out");
            _usedButton = Btn(g, "UsedIn", "craft.category", out _, () => OpenPage(_selected, true));
            Img(_usedButton.transform, "Glyph", "glyph.link");
            _fromButton = Btn(g, "ObtainedFrom", "craft.category", out _, () => OpenPage(_selected, false));
            Img(_fromButton.transform, "Glyph", "glyph.panel.chest");
            _prevPlate = Img(g, "PrevPlate", "craft.category");
            _nextPlate = Img(g, "NextPlate", "craft.category");
            _prev = Btn(g, "Prev", "craft.arrow.left", out _, () => { _gridPage--; _dirty = true; });
            _next = Btn(g, "Next", "craft.arrow.right", out _, () => { _gridPage++; _dirty = true; });
            _pagerText = Txt(g, "Pager", "text.body", TextAlignmentOptions.Center);
            _hint = Txt(g, "Hint", "text.muted", TextAlignmentOptions.Center, FontStyles.Italic);

            _pageView = Child("Page").gameObject;
            var p = _pageView.transform;
            _backPlate = Img(p, "BackPlate", "craft.category");
            _pageDivTop = Img(p, "DividerTop", "divider");
            _pageDivBottom = Img(p, "DividerBottom", "divider");
            _pageDivTabs = Img(p, "DividerTabs", "divider");
            _back = Btn(p, "Back", "craft.arrow.left", out _, () => Nav(_history.Back(), false));
            _crumb = Txt(p, "Crumb", "text.body", TextAlignmentOptions.Left);
            _pageIcon = Img(p, "Icon", "content", true);
            Tip(_pageIcon.gameObject, () => _pageId, () => 1);
            _pageName = Txt(p, "Name", "text.body", TextAlignmentOptions.Left, FontStyles.Bold);
            _pageType = Txt(p, "Type", "text.body", TextAlignmentOptions.Left);
            _pageDesc = Txt(p, "Desc", "text.muted", TextAlignmentOptions.TopLeft, FontStyles.Italic);
            _pageDesc.textWrappingMode = TextWrappingModes.Normal;
            _fromTab = Btn(p, "FromTab", "craft.tab", out _fromTabLabel, () => { _usedTab = false; _rowScroll = 0; _dirty = true; });
            _usedTabButton = Btn(p, "UsedTab", "craft.tab", out _usedTabLabel, () => { _usedTab = true; _rowScroll = 0; _dirty = true; });
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
            t.fontStyle = style;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        // Button with an optional plate role (null = clear hit area) and a label.
        private static Button Btn(Transform parent, string name, string role, out TMP_Text label, Action click)
        {
            var image = role != null ? InfoArt.OwnImage(parent, name, role, "item browser button " + name) : new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
            if (role == null)
            {
                image.rectTransform.SetParent(parent, false);
                image.color = Color.clear;
                DollCell.Mark(image, "content", "item browser hit area");
            }
            image.preserveAspect = role != null && role.StartsWith("craft.arrow", StringComparison.Ordinal);
            image.raycastTarget = true;
            var b = image.gameObject.AddComponent<Button>();
            b.targetGraphic = image;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.onClick.AddListener(() => click());
            // Styled after the Button exists: the role's sprite states go on its targetGraphic (CraftingSkin.StepButton).
            if (role != null && image.GetComponent<SkinMark>() is SkinMark m) m.Outcome = Restyler.Apply(image, m, ThemeTokens.Role(role)) ? Outcome.Styled : Outcome.Degraded;
            label = role == null || role.StartsWith("craft.arrow", StringComparison.Ordinal) ? null : Txt(image.transform, "Label", role == "craft.tab" ? "craft.tab.label" : "text.body", TextAlignmentOptions.Center);
            return b;
        }

        private TMP_InputField Field(Image back)
        {
            var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            area.SetParent(back.rectTransform, false);
            var text = Txt(area, "Text", "text.body", TextAlignmentOptions.Left);
            var hint = Txt(area, "Placeholder", "text.muted", TextAlignmentOptions.Left, FontStyles.Italic);
            hint.text = Browser.Localize("$ib_search");
            var f = back.gameObject.AddComponent<TMP_InputField>();
            f.textViewport = area;
            f.textComponent = text;
            f.placeholder = hint;
            f.targetGraphic = back;
            f.transition = Selectable.Transition.None;
            f.navigation = new Navigation { mode = Navigation.Mode.None };
            f.onValueChanged.AddListener(s =>
            {
                _query = s;
                _gridPage = 0;
                if (_pageId != null) { _pageId = null; _history.Clear(); }
                _dirty = true;
            });
            return f;
        }

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
            return s;
        }

        private Slot LineSlot(Transform parent, string name)
        {
            var s = NewSlot(parent, name, null);
            s.A = Txt(s.Rt, "Text", "text.body", TextAlignmentOptions.Left);
            Hook(s, e => { if (e.button == PointerEventData.InputButton.Left && s.Id != null) OpenPage(s.Id, false); });
            Tip(s.Back.gameObject, () => s.Id, () => s.Quality);
            return s;
        }

        private void Hook(Slot s, Action<PointerEventData> click) => s.Back.gameObject.AddComponent<Hit>().Click = click;

        // Vanilla tooltip (UITooltip + the player grid's tooltip prefab), text filled on enter from the vanilla path.
        private void Tip(GameObject go, Func<string> id, Func<int> quality)
        {
            if (!_tooltipPrefab) return;
            var hit = go.GetComponent<Hit>() ?? go.AddComponent<Hit>();
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

        // Text shrunk to fit its box (Russian tab names are longer than the mockup's English ones).
        private void Fit(TMP_Text t, string s, float size)
        {
            if (t.text != s) t.text = s;
            var fs = size * _k * _f;
            if (t.enableAutoSizing && Mathf.Abs(t.fontSizeMax - fs) < 0.05f) return;
            t.enableAutoSizing = true;
            t.fontSizeMax = fs;
            t.fontSizeMin = fs * 0.7f;
        }

        private void Off(Component c)
        {
            if (c && c.gameObject.activeSelf) c.gameObject.SetActive(false);
        }

        private void Text(TMP_Text t, string s, float size)
        {
            if (t.text != s) t.text = s;
            var fs = size * _k * _f;
            if (Mathf.Abs(t.fontSize - fs) > 0.05f) { t.enableAutoSizing = false; t.fontSize = fs; }
        }

        private static void SetIcon(Image image, string id)
        {
            var s = id != null ? Browser.Icon(id, out _) : null;
            if (image.sprite != s) image.sprite = s;
            if (image.enabled != (bool)s) image.enabled = s;
        }

        private static string L(string token) => Browser.Localize(token);
        private static string NameOf(string id) => Browser.Index != null && Browser.Index.Entries.TryGetValue(id, out var e) ? L(e.Name) : id;

        // ---------------------------------------------------------------- grid view (mockup A)

        private List<Entry> Results()
        {
            var known = Browser.KnownOnly.Value;
            var dev = Browser.Devcommands;
            if (_resultsGen == Browser.Generation && _resultsQuery == _query && _resultsType == _type && _resultsKnownOnly == known && _resultsDev == dev) return _results;
            var fav = _type == 5 ? Browser.Favorites() : null;
            _results = Browser.Index.Search(_query, TypeOf[_type], e => (!known || Browser.Known(e)) && (fav == null || fav.Contains(e.Id)), L, Browser.ModName, dev);
            _resultsDev = dev;
            _resultsGen = Browser.Generation;
            _resultsQuery = _query;
            _resultsType = _type;
            _resultsKnownOnly = known;
            return _results;
        }

        private void LayoutGrid()
        {
            Off(_pageView.transform);
            _searchBack.transform.SetParent(_gridView.transform, false);
            Put(_gridView.transform, 0f, 0f, UnitW, _a.H / _k);
            if (Browser.Index == null) return;
            var h = _a.H / _k;
            Put(_searchBack, 0f, 0f, 362f, 42f);
            Put(_searchGlyph, 12f, 8f, 26f, 26f);
            Put(_search.textViewport, 48f, 4f, 306f, 34f);
            foreach (var t in _search.textViewport.GetComponentsInChildren<TMP_Text>(true)) { Put(t, 48f, 4f, 306f, 34f); Text(t, t.text, 21f); }
            Put(_knownButton, 394f, 5f, 156f, 32f);
            Put(_knownBox, 394f, 8f, 26f, 26f);
            Put(_knownCheck, 394f, 8f, 26f, 26f);
            _knownCheck.enabled = Browser.KnownOnly.Value;
            Put(_knownLabel, 430f, 5f, 120f, 32f);
            Fit(_knownLabel, L("$ib_known_only"), 20f);
            var x = 0f;
            for (var i = 0; i < _types.Length; i++)
            {
                Put(_types[i], x, 58f, TypeW[i], 38f);
                Put(_typeLabels[i], x + 3f, 58f, TypeW[i] - 6f, 38f);
                Fit(_typeLabels[i], (i == 5 ? "★ " : "") + L(TypeTokens[i]), 19f);
                if (_types[i].interactable != (i != _type)) _types[i].interactable = i != _type;
                x += TypeW[i] + 8f;
            }

            var list = Results();
            var wellBottom = h - Footer;
            Put(_gridWell, 0f, WellTop, 362f, wellBottom - WellTop);
            var rows = Math.Max(1, (int)Math.Min(GridRows, (wellBottom - CellTop + CellY - CellX) / CellY)); // mockup A: six rows, space below
            var pitch = Math.Max(CellY, (wellBottom - CellTop - 40f) / rows); // taller body: taller sockets, the mockup's gap under row six kept
            var per = rows * 6;
            if (_selected == null && list.Count > 0) _selected = list[0].Id; // mockup A: the page opens on a selected entry
            var pages = Math.Max(1, (list.Count + per - 1) / per);
            _gridPages = pages;
            _gridPage = Mathf.Clamp(_gridPage, 0, pages - 1);
            while (_cells.Count < per) _cells.Add(GridCell(_cells.Count));
            var fav = Browser.Favorites();
            for (var i = 0; i < _cells.Count; i++)
            {
                var c = _cells[i];
                if (i >= per) { Off(c.Back); continue; }
                var n = _gridPage * per + i;
                c.Id = n < list.Count ? list[n].Id : null;
                float cx = 3f + i % 6 * CellX, cy = CellTop + i / 6 * pitch;
                Put(c.Back, cx, cy, CellX - 4f, pitch - 6f);
                Put(c.Icon, cx + 6f, cy + (pitch - 6f - (CellX - 16f)) / 2f, CellX - 16f, CellX - 16f);
                SetIcon(c.Icon, c.Id);
                Put(c.D, cx + CellX - 24f, cy + 2f, 18f, 18f);
                Text(c.D, "★", 16f);
                c.D.enabled = c.Id != null && fav.Contains(c.Id);
                var sel = c.Id != null && c.Id == _selected;
                if (c.Back.GetComponent<SkinMark>() is SkinMark m && m.Role != (sel ? "craft.row.selected" : "craft.socket"))
                {
                    m.Role = sel ? "craft.row.selected" : "craft.socket";
                    m.Outcome = Restyler.Apply(c.Back, m, ThemeTokens.Role(m.Role)) ? Outcome.Styled : Outcome.Degraded;
                }
            }
            Detail(wellBottom);
            var py = wellBottom + 12f;
            const float mid = UnitW / 2f;
            Put(_prevPlate, mid - 90f, py, 38f, 34f);
            Put(_prev, mid - 82f, py + 6f, 22f, 22f);
            Put(_pagerText, mid - 40f, py, 80f, 34f);
            Text(_pagerText, $"{_gridPage + 1} / {pages}", 18f);
            Put(_nextPlate, mid + 52f, py, 38f, 34f);
            Put(_next, mid + 60f, py + 6f, 22f, 22f);
            _prev.interactable = _gridPage > 0;
            _next.interactable = _gridPage < pages - 1;
            if (Browser.CanTake)
            {
                Put(_hint, 0f, py + 44f, UnitW, 24f);
                Text(_hint, L("$ib_hint_take"), 16f);
            }
            else Off(_hint);
        }

        private Slot GridCell(int i)
        {
            var s = NewSlot(_gridView.transform, "Cell" + i, "craft.socket");

            s.D = Txt(s.Rt, "FavStar", "craft.title", TextAlignmentOptions.Center);
            s.D.raycastTarget = false;
            Hook(s, e => CellClick(s, e));
            Tip(s.Back.gameObject, () => s.Id, () => 1);
            return s;
        }

        private void CellClick(Slot s, PointerEventData e)
        {
            if (s.Id == null) return;
            switch (e.button)
            {
                case PointerEventData.InputButton.Right:
                    Take(s.Id, 1);
                    return;
                case PointerEventData.InputButton.Middle:
                    Browser.ToggleFavorite(s.Id);
                    _resultsGen = -1;
                    break;
                default:
                    if (e.clickCount > 1 || s.Id == _selected) { OpenPage(s.Id, false); return; }
                    _selected = s.Id;
                    break;
            }
            _dirty = true;
        }

        private static void Take(string id, int quality)
        {
            if (!Browser.CanTake) return;
            var shift = ZInput.GetKey(KeyCode.LeftShift) || ZInput.GetKey(KeyCode.RightShift);
            var ctrl = ZInput.GetKey(KeyCode.LeftControl) || ZInput.GetKey(KeyCode.RightControl);
            var why = Browser.Take(id, quality, ctrl ? Browser.TakeMode.Materials : shift ? Browser.TakeMode.Stack : Browser.TakeMode.One);
            if (why != null) Player.m_localPlayer?.Message(MessageHud.MessageType.Center, why);
        }

        // Detail pane: the selected entry's craft (else build / conversion) recipe, quality 1 (mockup A).
        private void Detail(float bottom)
        {
            const float X = 368f, W = 182f, Y = WellTop;
            var h = bottom - Y;
            Put(_detailWell, X, Y, W, h);
            var e = _selected != null && Browser.Index.Entries.TryGetValue(_selected, out var x) ? x : null;
            if (e == null)
            {
                foreach (var c in new Component[] { _detailIcon, _detailName, _detailType, _divTop, _divBottom, _stationIcon, _stationText, _arrow, _noRecipe, _usedButton, _fromButton }) Off(c);
                foreach (var r in _reqRows) Off(r.Back);
                Off(_outRow.Back);
                return;
            }
            Put(_detailIcon, X + 38f, Y + 10f, 100f, 90f);
            SetIcon(_detailIcon, e.Id);
            Put(_detailName, X + 6f, Y + 104f, W - 12f, 30f);
            Fit(_detailName, L(e.Name), 24f);
            Put(_detailType, X + 6f, Y + 134f, W - 12f, 22f);
            Text(_detailType, TypeName(e), 16f);
            Put(_divTop, X + 8f, Y + 162f, W - 16f, 3f);
            Relation recipe = null;
            foreach (var r in Browser.Index.ObtainedFrom(e.Id))
                if (recipe == null || Rank(r.Kind) < Rank(recipe.Kind)) recipe = r;
            var y = Y + 172f;
            if (recipe == null || Rank(recipe.Kind) > 2)
            {
                foreach (var c in new Component[] { _stationIcon, _stationText, _arrow }) Off(c);
                foreach (var r in _reqRows) Off(r.Back);
                Off(_outRow.Back);
                Put(_noRecipe, X + 6f, y, W - 12f, 26f);
                Text(_noRecipe, L(recipe == null ? "$ib_no_source" : "$ib_see_sources"), 15f);
                y += 34f;
            }
            else
            {
                Off(_noRecipe);
                var station = recipe.Station ?? (recipe.Kind == RelationKind.Build ? recipe.Source : null);
                Put(_stationIcon, X + 8f, y, 26f, 26f);
                SetIcon(_stationIcon, station);
                Put(_stationText, X + 40f, y, W - 44f, 26f);
                Text(_stationText, station == null ? L("$ib_by_hand") : recipe.StationLevel > 0 ? $"{NameOf(station)} ({L("$ib_level")} {recipe.StationLevel})" : NameOf(station), 17f);
                y += 36f;
                var shown = Math.Min(recipe.Inputs.Length, _reqRows.Count);
                for (var i = 0; i < _reqRows.Count; i++)
                {
                    var r = _reqRows[i];
                    if (i >= shown) { Off(r.Back); continue; }
                    Line(r, recipe.Inputs[i], X, y, W);
                    y += 34f;
                }
                Put(_arrow, X, y, W, 26f);
                Text(_arrow, "↓", 20f);
                y += 30f;
                Line(_outRow, recipe.Outputs[0], X, y, W);
                y += 34f;
            }
            Put(_divBottom, X + 8f, y + 6f, W - 16f, 3f);
            var by = y + 22f; // mockup A: the two page buttons right under the recipe
            Put(_usedButton, X + 6f, by, W - 12f, 38f);
            Put(_usedButton.transform.Find("Glyph"), X + 14f, by + 8f, 22f, 22f);
            Put(_usedButton.transform.Find("Label"), X + 40f, by, W - 52f, 38f);
            Text(_usedButton.transform.Find("Label").GetComponent<TMP_Text>(), L("$ib_used_in"), 17f);
            Put(_fromButton, X + 6f, by + 46f, W - 12f, 38f);
            Put(_fromButton.transform.Find("Glyph"), X + 14f, by + 54f, 22f, 22f);
            Put(_fromButton.transform.Find("Label"), X + 40f, by + 46f, W - 52f, 38f);
            Text(_fromButton.transform.Find("Label").GetComponent<TMP_Text>(), L("$ib_obtained_from"), 17f);
        }

        private static int Rank(RelationKind k) => k == RelationKind.Craft ? 0 : k == RelationKind.Build ? 1 : k == RelationKind.Conversion ? 2 : 3;

        private void Line(Slot r, Stack s, float x, float y, float w)
        {
            r.Id = s.Prefab;
            Put(r.Back, x + 8f, y, w - 16f, 30f);
            Put(r.Icon, x + 10f, y, 30f, 30f);
            SetIcon(r.Icon, s.Prefab);
            Put(r.A, x + 48f, y, w - 56f, 30f);
            Text(r.A, $"{Amount(s)} x {NameOf(s.Prefab)}", 17f);
            if (r == _outRow) r.A.text = $"{Amount(s)} {NameOf(s.Prefab)}";
        }

        private static string Amount(Stack s) => s.Min == s.Max ? s.Min.ToString() : $"{s.Min}-{s.Max}";

        private static string TypeName(Entry e)
        {
            if (e.Type == EntryType.Item && Browser.Prefab(e.Id) is GameObject go && go.TryGetComponent<ItemDrop>(out var d))
                return L("$ib_itemtype_" + d.m_itemData.m_shared.m_itemType.ToString().ToLowerInvariant());
            return L("$ib_entry_" + e.Type.ToString().ToLowerInvariant());
        }

        // ---------------------------------------------------------------- item page (mockup B)

        private void OpenPage(string id, bool usedIn)
        {
            if (id == null || Browser.Index == null || !Browser.Index.Entries.ContainsKey(id)) return;
            _usedTab = usedIn;
            Nav(id, true);
        }

        // id null = back to the grid.
        private void Nav(string id, bool track)
        {
            if (track) _history.Go(id);
            if (id == null) _history.Clear();
            _pageId = id;
            if (id != null) _selected = id;
            _rowScroll = 0;
            _dirty = true;
        }

        private void LayoutPage()
        {
            Off(_gridView.transform);
            if (Browser.Index == null || !Browser.Index.Entries.TryGetValue(_pageId, out var e)) { _pageId = null; LayoutGrid(); return; }
            var h = _a.H / _k;
            Put(_pageView.transform, 0f, 0f, UnitW, h);
            // The one search field moves to the page's top row (typing there returns to the grid).
            _searchBack.transform.SetParent(_pageView.transform, false);
            Put(_backPlate, 0f, 0f, 40f, 40f);
            Put(_back, 9f, 9f, 22f, 22f);
            Put(_crumb, 54f, 2f, 292f, 36f);
            Fit(_crumb, $"{L(TypeTokens[_type])} > {L(e.Name)}", 20f);
            Put(_pageDivTop, 0f, 48f, UnitW, 3f);
            Put(_searchBack, 356f, 0f, 194f, 36f);
            Put(_searchGlyph, 362f, 7f, 22f, 22f);
            Put(_search.textViewport, 390f, 2f, 154f, 32f);
            foreach (var t in _search.textViewport.GetComponentsInChildren<TMP_Text>(true)) { Put(t, 390f, 2f, 154f, 32f); Text(t, t.text, 16f); }
            Put(_pageIcon, 10f, 60f, 110f, 110f);
            SetIcon(_pageIcon, e.Id);
            Put(_pageName, 136f, 62f, 414f, 38f);
            Fit(_pageName, L(e.Name), 30f);
            Put(_pageType, 136f, 102f, 414f, 26f);
            Text(_pageType, TypeName(e), 19f);
            Put(_pageDesc, 136f, 130f, 414f, 46f);
            Text(_pageDesc, L(e.Desc) ?? "", 18f);
            _pageDesc.textWrappingMode = TextWrappingModes.Normal;

            var from = Browser.Index.ObtainedFrom(e.Id).OrderBy(r => Rank(r.Kind)).ToList(); // recipes first (stable), then drops
            var used = Browser.Index.UsedInResults(e.Id);
            Put(_pageDivTabs, 0f, 226f, UnitW, 3f);
            Put(_fromTab, 26f, 184f, 196f, 38f);
            Put(_fromTabLabel, 26f, 184f, 196f, 38f);
            Fit(_fromTabLabel, L("$ib_obtained_from"), 20f);
            Put(_usedTabButton, 232f, 184f, 164f, 38f);
            Put(_usedTabLabel, 232f, 184f, 164f, 38f);
            Fit(_usedTabLabel, $"{L("$ib_used_in")} ({used.Count})", 20f);
            if (_fromTab.interactable == !_usedTab) _fromTab.interactable = _usedTab;
            if (_usedTabButton.interactable == _usedTab) _usedTabButton.interactable = !_usedTab;

            var y = 238f;
            var rowsShown = 0;
            if (!_usedTab)
            {
                var maxRows = Math.Max(1, (int)((h - y - (used.Count > 0 ? 132f : 0f) - 8f) / RowH));
                _rowScrollMax = Math.Max(0, from.Count - maxRows);
                _rowScroll = Mathf.Clamp(_rowScroll, 0, _rowScrollMax);
                rowsShown = Math.Min(maxRows, from.Count - _rowScroll);
                while (_rows.Count < rowsShown) _rows.Add(SourceRow(_rows.Count));
                for (var i = 0; i < rowsShown; i++) Row(_rows[i], from[_rowScroll + i], e.Id, y + i * RowH, i == 0);
                y += rowsShown * RowH + 18f;
            }
            for (var i = rowsShown; i < _rows.Count; i++) Off(_rows[i].Back);
            var maxTiles = Math.Max(0, (int)((h - y) / 132f)) * 7;
            var first = 0;
            if (_usedTab)
            {
                // Whole tile rows: the last page stays column-aligned, scrolling stops at the last row.
                _rowScrollMax = Math.Max(0, (used.Count + 6) / 7 - maxTiles / 7);
                _rowScroll = Mathf.Clamp(_rowScroll, 0, _rowScrollMax);
                first = _rowScroll * 7;
            }
            else maxTiles = Math.Min(maxTiles, 7);
            var tiles = Math.Min(maxTiles, used.Count - first);
            while (_tiles.Count < tiles) _tiles.Add(Tile(_tiles.Count));
            for (var i = 0; i < _tiles.Count; i++)
            {
                var t = _tiles[i];
                if (i >= tiles) { Off(t.Back); Off(t.A); continue; }
                t.Id = used[first + i];
                var tx = i % 7 * (UnitW / 7f);
                var ty = y + i / 7 * 132f;
                Put(t.Back, tx + 2f, ty, UnitW / 7f - 4f, 66f);
                Put(t.Icon, tx + 8f, ty, UnitW / 7f - 16f, 66f);
                SetIcon(t.Icon, t.Id);
                Put(t.A, tx, ty + 68f, UnitW / 7f, 58f);
                Text(t.A, NameOf(t.Id), 16f);
            }
            if (tiles > 0) Put(_pageDivBottom, 0f, y + (tiles + 6) / 7 * 132f + 4f, UnitW, 3f);
            else Off(_pageDivBottom);
        }

        private Slot SourceRow(int i)
        {
            var s = NewSlot(_pageView.transform, "Row" + i, "craft.row");
            s.A = Txt(s.Rt, "Name", "text.body", TextAlignmentOptions.Left);
            s.B = Txt(s.Rt, "Kind", "text.muted", TextAlignmentOptions.Left);
            s.C = Txt(s.Rt, "Amount", "text.body", TextAlignmentOptions.Center);
            s.D = Txt(s.Rt, "Chance", "text.body", TextAlignmentOptions.Right);
            s.D.textWrappingMode = TextWrappingModes.Normal; // long conditions ("weighted, draws 2-4") wrap instead of an ellipsis
            Hook(s, e =>
            {
                if (e.button == PointerEventData.InputButton.Right) { Take(s.Id, 1); return; }
                OpenPage(s.Id, true);
            });
            Tip(s.Back.gameObject, () => s.Id, () => 1);
            return s;
        }

        private Slot Tile(int i)
        {
            var s = NewSlot(_pageView.transform, "Tile" + i, "craft.socket"); // mockup B: framed tiles
            // Name under the socket, a sibling of it: as a child CellFit.KeepTexts pulls it back into the cell every
            // LateUpdate while each relayout puts it back (labels jittered on every wheel tick).
            s.A = Txt(_pageView.transform, "TileName" + i, "text.body", TextAlignmentOptions.Top);
            s.A.textWrappingMode = TextWrappingModes.Normal;
            Hook(s, e =>
            {
                if (e.button == PointerEventData.InputButton.Right) { Take(s.Id, 1); return; }
                OpenPage(s.Id, false);
            });
            Tip(s.Back.gameObject, () => s.Id, () => 1);
            return s;
        }

        // One "Obtained from" row: who (source, or the station for recipes), kind + inputs, amount, chance / conditions.
        private void Row(Slot s, Relation r, string item, float y, bool first)
        {
            var who = r.Kind == RelationKind.Craft || r.Kind == RelationKind.Upgrade ? r.Station : r.Source;
            s.Id = who;
            var role = first ? "craft.row.selected" : "craft.row";
            if (s.Back.GetComponent<SkinMark>() is SkinMark m && m.Role != role)
            {
                m.Role = role;
                m.Outcome = Restyler.Apply(s.Back, m, ThemeTokens.Role(role)) ? Outcome.Styled : Outcome.Degraded;
            }
            Put(s.Back, 0f, y, UnitW, RowH - 2f);
            Put(s.Icon, 16f, y + 7f, 62f, 62f);
            SetIcon(s.Icon, who);
            Put(s.A, 100f, y + 10f, 250f, 28f);
            Fit(s.A, who == null ? L("$ib_by_hand") : r.StationLevel > 0 ? $"{NameOf(who)} ({L("$ib_level")} {r.StationLevel})" : NameOf(who), 21f);
            var kind = L("$ib_kind_" + r.Kind.ToString().ToLowerInvariant());
            if (r.Quality > 1) kind += " → " + L("$ib_quality") + " " + r.Quality;
            if (r.Inputs.Length > 0)
            {
                var ins = new List<string>();
                foreach (var x in r.Inputs) ins.Add($"{Amount(x)} {NameOf(x.Prefab)}");
                kind += ": " + string.Join(", ", ins);
            }
            Put(s.B, 100f, y + 40f, 270f, 26f);
            Fit(s.B, kind, 18f);
            var o = Array.Find(r.Outputs, x => x.Prefab == item);
            Put(s.C, 360f, y, 70f, RowH);
            Text(s.C, "x" + Amount(o), 21f);
            Put(s.D, 420f, y, 120f, RowH);
            var chance = float.IsNaN(o.Chance) ? L("$ib_weighted") : o.Chance >= 1f && r.Conditions == null ? "" : $"{o.Chance * 100f:0.#}%";
            if (r.Conditions != null) chance = (chance + " " + L(r.Conditions)).Trim();
            Fit(s.D, chance, 20f);
        }
    }
}
