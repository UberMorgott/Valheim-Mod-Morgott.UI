using System.Collections.Generic;
using HarmonyLib;
using Morgott.Core;
using Morgott.UI.PlayerHud;
using Morgott.UI.Restyle;
using Morgott.UI.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Morgott.UI.MapScreen
{
    // Core IPortalPicker consumer (owner Morgott.Patcher serves IPortalTravel): while registered, walking into a portal
    // opens the large map with the left sidebar forced onto the ПОРТАЛЫ tab. On the normal M map, the same collapsible
    // left sidebar offers both ПОРТАЛЫ (browse/focus) and ЗАДАНИЯ (live NPC quests + known bosses).
    public sealed class PortalPickerService : IPortalPicker
    {
        public static readonly PortalPickerService Instance = new PortalPickerService();
        internal static bool Registered;
        internal static bool Dirty = true;

        public void OnPortalsChanged() => Dirty = true;

        // Resolved per use (Core Services rule: never cached).
        internal static IPortalTravel Travel() => Registered && Services.TryGet(out IPortalTravel t) ? t : null;

        // For read-only portal browsing on the normal M map even if travel-on-enter config is off.
        internal static IPortalTravel AnyTravel() => Services.TryGet(out IPortalTravel t) ? t : null;

        internal static bool Active => Travel() is IPortalTravel t && t.Active;
    }

    // Collapsible left map sidebar with tabs (ПОРТАЛЫ | ЗАДАНИЯ):
    // - Portal travel: auto-opens on ПОРТАЛЫ tab; click selects, double-click teleports, right-click favorites, pencil edits.
    // - Normal M map: collapsible via top-left button (remembers open/collapsed + tab across opens); ПОРТАЛЫ shows live
    //   distances from player position and focuses portals; ЗАДАНИЯ merges Core MapQuests + vanilla Hildir/Boss pins with
    //   live distances and smooth click-to-focus panning.
    public sealed partial class MinimapSkin
    {
        private enum SidebarTab { Portals, Quests }

        public sealed class PortalHit : MonoBehaviour, IPointerClickHandler
        {
            public System.Action<PointerEventData> Click;
            public void OnPointerClick(PointerEventData eventData) { if (!eventData.dragging) Click?.Invoke(eventData); }
        }

        private sealed class PortalRowUi
        {
            public RectTransform Root;
            public Image Back, Star, Swatch, Stone, Icon, Pencil, PencilGlyph;
            public TMP_Text Name, Biome, Distance;
            public int Look = -1;
            public int Index = -1;
        }

        private struct PortalView
        {
            public string Biome, Distance, Denied;
            public Color BiomeColor;
            public bool CanEdit;
        }

        private sealed class QuestRowUi
        {
            public RectTransform Root;
            public Image Back, Accent, BadgeBack;
            public TMP_Text Badge, Title, Subtitle, Distance;
            public int Look = -1;
            public int Index = -1;
        }

        private struct QuestView
        {
            public string Category, Subtitle, Distance;
            public Color Accent;
        }

        private RectTransform _pp, _ppList, _sbExpand;
        private Button _sbTabPortalsBtn, _sbTabQuestsBtn, _sbCollapseBtn, _sbExpandBtn;
        private Image _sbTabPortalsImg, _sbTabQuestsImg, _sbCollapseImg, _sbExpandImg;
        private TMP_Text _sbTabPortalsLabel, _sbTabQuestsLabel, _sbCollapseLabel, _sbExpandLabel, _sbEmptyText;
        private TMP_Text _ppFavHeader, _ppAllHeader, _qActiveHeader, _qBossHeader, _ppSearchHint, _ppHint;
        private Image _ppTitleLine, _ppFavLine, _ppAllLine, _qActiveLine, _qBossLine, _ppSearchBack, _ppSearchGlyph, _ppRing, _ppTrack, _ppThumb;
        private TMP_InputField _ppSearch;
        private GameObject _ppTooltip;
        private LegendScroll _ppScroll, _sbExpandScroll;

        private readonly List<PortalRowUi> _ppRowUi = new List<PortalRowUi>();
        private readonly List<PortalInfo> _ppPortals = new List<PortalInfo>();
        private readonly List<PortalRow> _ppRows = new List<PortalRow>();
        private readonly List<PortalView> _ppView = new List<PortalView>();
        private readonly PortalClicks _ppClicks = new PortalClicks();

        private readonly List<QuestRowUi> _qRowUi = new List<QuestRowUi>();
        private readonly List<MapQuest> _qCore = new List<MapQuest>();
        private readonly List<QuestPinInput> _qPins = new List<QuestPinInput>();
        private readonly List<QuestRow> _qRows = new List<QuestRow>();
        private readonly List<QuestView> _qView = new List<QuestView>();

        private int _ppFav, _qNpcCount;
        private float _ppScrollPx, _ppContentH, _ppViewH;
        private bool _ppShown; // true while a portal travel session is active
        private bool _sidebarOpen;
        private bool _sidebarNormalOpen = true;
        private SidebarTab _sidebarTab = SidebarTab.Portals;
        private SidebarTab _sidebarNormalTab = SidebarTab.Portals;
        private bool _largeWasOpen;
        private bool _qDirty = true;
        private int _qPinsVersion = -1;
        private float _sbNextLiveRefresh;
        private ZDOID _ppSel = ZDOID.None;
        private string _qSelId;
        private Minimap.PinData _ppRingPin;
        private int _ppRingVersion = -1;
        private bool _panActive;
        private Vector3 _panTargetOffset;

        // Harness / diagnostics.
        public bool PortalPanelShown => _sidebarOpen && _sidebarTab == SidebarTab.Portals && _pp && _pp.gameObject.activeInHierarchy;
        public bool QuestPanelShown => _sidebarOpen && _sidebarTab == SidebarTab.Quests && _pp && _pp.gameObject.activeInHierarchy;
        public IReadOnlyList<PortalRow> PortalRows => _ppRows;
        public IReadOnlyList<QuestRow> QuestRows => _qRows;
        public int PortalFavoriteCount => _ppFav;
        public ZDOID PortalSelected => _ppSel;
        public RectTransform PortalRing => _ppRing ? _ppRing.rectTransform : null;
        public Minimap.PinData PortalRingPin => _ppRingPin;
        public string PortalHint => _ppHint ? _ppHint.text : null;
        public TMP_InputField PortalSearch => _ppSearch;

        public GameObject PortalRowObject(ZDOID id)
        {
            foreach (var r in _ppRowUi)
                if (r.Index >= 0 && r.Index < _ppRows.Count && _ppRows[r.Index].Portal.Id == id) return r.Root.gameObject;
            return null;
        }

        // Also true while the portal editor or custom pin modal is open: Minimap.Update then leaves Esc / map keys alone.
        // Requires Minimap.IsOpen() so closing the map (e.g. player death) can never leave InTextInput stuck true.
        internal static bool PortalTyping => Instance && CustomPinMath.IsTypingInMap(
            Minimap.IsOpen(),
            Instance._pp && Instance._pp.gameObject.activeInHierarchy && Instance._ppSearch && Instance._ppSearch.isFocused,
            Instance.PortalEditorShown,
            Instance.CustomPinModalShown);

        internal static bool PortalHover => Instance && (
            (Instance._ppScroll && Instance._ppScroll.Over && Instance._pp && Instance._pp.gameObject.activeInHierarchy)
            || (Instance._sbExpandScroll && Instance._sbExpandScroll.Over && Instance._sbExpand && Instance._sbExpand.gameObject.activeInHierarchy));

        private void BuildPortalPanel()
        {
            _pp = HudArtRect("RuneSkin_PortalPicker", _large);
            _pp.SetSiblingIndex(_legend.GetSiblingIndex() + 1);
            var back = _pp.gameObject.AddComponent<Image>();
            _art.Restyle(back, "sprite.frame.inset", "color.frame", Image.Type.Sliced);
            back.raycastTarget = true; // clicks on the sidebar never fall through to the map
            _ppScroll = _pp.gameObject.AddComponent<LegendScroll>();

            var headerSize = ThemeTokens.GetFloat("size.map.legend.header", 21f);
            var font = ThemeTokens.GetFloat("size.map.legend.font", 17f);

            // Top tab bar: ПОРТАЛЫ | ЗАДАНИЯ + collapse button (normal map).
            _sbTabPortalsImg = _art.Image("TabPortals", _pp, "sprite.tab.selected", "color.frame", Image.Type.Sliced, raycast: true);
            _sbTabPortalsLabel = _art.Text("Label", _sbTabPortalsImg.transform, "font.display", "color.text.header", font - 1f, TextAlignmentOptions.Center);
            _sbTabPortalsLabel.overflowMode = TextOverflowModes.Ellipsis;
            Stretch(_sbTabPortalsLabel.rectTransform);
            _sbTabPortalsBtn = _sbTabPortalsImg.gameObject.AddComponent<Button>();
            _sbTabPortalsBtn.targetGraphic = _sbTabPortalsImg;
            _sbTabPortalsBtn.navigation = new Navigation { mode = Navigation.Mode.None };
            _sbTabPortalsBtn.onClick.AddListener(() => SelectSidebarTab(SidebarTab.Portals));

            _sbTabQuestsImg = _art.Image("TabQuests", _pp, "sprite.tab.normal", "color.frame", Image.Type.Sliced, raycast: true);
            _sbTabQuestsLabel = _art.Text("Label", _sbTabQuestsImg.transform, "font.display", "color.text.body", font - 1f, TextAlignmentOptions.Center);
            _sbTabQuestsLabel.overflowMode = TextOverflowModes.Ellipsis;
            Stretch(_sbTabQuestsLabel.rectTransform);
            _sbTabQuestsBtn = _sbTabQuestsImg.gameObject.AddComponent<Button>();
            _sbTabQuestsBtn.targetGraphic = _sbTabQuestsImg;
            _sbTabQuestsBtn.navigation = new Navigation { mode = Navigation.Mode.None };
            _sbTabQuestsBtn.onClick.AddListener(() => SelectSidebarTab(SidebarTab.Quests));

            _sbCollapseImg = _art.Image("Collapse", _pp, "sprite.button.icon.normal", "color.frame", Image.Type.Sliced, raycast: true);
            _sbCollapseLabel = _art.Text("Glyph", _sbCollapseImg.transform, "font.body", "color.text.header", font + 3f, TextAlignmentOptions.Center);
            _sbCollapseLabel.text = "‹";
            Stretch(_sbCollapseLabel.rectTransform);
            _sbCollapseBtn = _sbCollapseImg.gameObject.AddComponent<Button>();
            _sbCollapseBtn.targetGraphic = _sbCollapseImg;
            _sbCollapseBtn.navigation = new Navigation { mode = Navigation.Mode.None };
            _sbCollapseBtn.onClick.AddListener(() => SetSidebarNormalOpen(false));

            _ppTitleLine = _art.Image("TitleDivider", _pp, "sprite.divider", "color.frame", Image.Type.Sliced);
            _ppSearchBack = _art.Image("Search", _pp, "sprite.field.search", "color.frame", Image.Type.Sliced, raycast: true);
            _ppSearchGlyph = _art.Image("Glyph", _ppSearchBack.transform, "sprite.glyph.search", "color.text.body", Image.Type.Simple);
            _ppSearchGlyph.preserveAspect = true;
            _ppSearch = Field(_ppSearchBack, 24);
            _ppSearch.onValueChanged.AddListener(_ => { _ppScrollPx = 0f; PortalPickerService.Dirty = true; _qDirty = true; });
            _ppSearchHint = _art.Text("SearchHint", _ppSearchBack.transform, "font.body", "color.state.disabled", font, TextAlignmentOptions.Left);
            _ppList = HudArtRect("List", _pp);
            _ppList.gameObject.AddComponent<RectMask2D>();
            var portalDrag = _pp.gameObject.AddComponent<DragScroll>();
            portalDrag.Space = _ppList;
            portalDrag.CanStart = e => _ppContentH > _ppViewH + 0.5f && RectTransformUtility.RectangleContainsScreenPoint(_ppList, e.pressPosition, e.pressEventCamera);
            portalDrag.OnDeltaY = (dy, _) => _ppScrollPx += dy;

            _ppFavHeader = _art.Text("FavoritesHeader", _ppList, "font.display", "color.text.header", headerSize, TextAlignmentOptions.Center);
            _ppFavLine = _art.Image("FavoritesDivider", _ppList, "sprite.divider", "color.frame", Image.Type.Sliced);
            _ppAllHeader = _art.Text("AllHeader", _ppList, "font.display", "color.text.header", headerSize, TextAlignmentOptions.Center);
            _ppAllLine = _art.Image("AllDivider", _ppList, "sprite.divider", "color.frame", Image.Type.Sliced);

            _qActiveHeader = _art.Text("QuestsActiveHeader", _ppList, "font.display", "color.text.header", headerSize, TextAlignmentOptions.Center);
            _qActiveLine = _art.Image("QuestsActiveDivider", _ppList, "sprite.divider", "color.frame", Image.Type.Sliced);
            _qBossHeader = _art.Text("QuestsBossHeader", _ppList, "font.display", "color.text.header", headerSize, TextAlignmentOptions.Center);
            _qBossLine = _art.Image("QuestsBossDivider", _ppList, "sprite.divider", "color.frame", Image.Type.Sliced);

            _sbEmptyText = _art.Text("Empty", _ppList, "font.body", "color.state.disabled", font - 1f, TextAlignmentOptions.Center);
            _sbEmptyText.textWrappingMode = TextWrappingModes.Normal;

            _ppTrack = _art.Image("ScrollTrack", _pp, "sprite.bar.skill.trough", "color.frame", Image.Type.Sliced);
            _ppThumb = _art.Image("ScrollThumb", _ppTrack.transform, "sprite.bar.skill.fill", "color.text.accent", Image.Type.Sliced);
            foreach (var tip in _map.GetComponentsInChildren<UITooltip>(true))
                if (tip.m_tooltipPrefab) { _ppTooltip = tip.m_tooltipPrefab; break; }
            _ppHint = _art.Text("Hint", _pp, "font.body", "color.text.body", ThemeTokens.GetFloat("size.map.biome.font", 15f), TextAlignmentOptions.Center);
            _ppHint.textWrappingMode = TextWrappingModes.Normal;
            _pp.gameObject.SetActive(false);

            // Top-left compact expand button shown when sidebar is collapsed on normal M map.
            _sbExpand = HudArtRect("RuneSkin_SidebarExpand", _large);
            _sbExpand.SetSiblingIndex(_pp.GetSiblingIndex() + 1);
            _sbExpandImg = _sbExpand.gameObject.AddComponent<Image>();
            _art.Restyle(_sbExpandImg, "sprite.button.normal", "color.frame", Image.Type.Sliced);
            _sbExpandImg.raycastTarget = true;
            _sbExpandScroll = _sbExpand.gameObject.AddComponent<LegendScroll>();
            _sbExpandLabel = _art.Text("Label", _sbExpand, "font.display", "color.text.header", font - 1f, TextAlignmentOptions.Center);
            Stretch(_sbExpandLabel.rectTransform);
            _sbExpandBtn = _sbExpand.gameObject.AddComponent<Button>();
            _sbExpandBtn.targetGraphic = _sbExpandImg;
            _sbExpandBtn.navigation = new Navigation { mode = Navigation.Mode.None };
            var hover = ThemeTokens.GetSprite("sprite.button.hover");
            if (hover)
            {
                _sbExpandBtn.transition = Selectable.Transition.SpriteSwap;
                _sbExpandBtn.spriteState = new SpriteState { highlightedSprite = hover, pressedSprite = ThemeTokens.GetSprite("sprite.button.pressed") ?? hover };
            }
            _sbExpandBtn.onClick.AddListener(() => SetSidebarNormalOpen(true));
            _sbExpand.gameObject.SetActive(false);

            if (PortalPickerService.AnyTravel() == null) _sidebarNormalTab = SidebarTab.Quests;
        }

        private void SelectSidebarTab(SidebarTab tab)
        {
            var es = EventSystem.current;
            if (es && es.currentSelectedGameObject) es.SetSelectedGameObject(null);
            if (_sidebarTab == tab) return;
            _sidebarTab = tab;
            if (!_ppShown) _sidebarNormalTab = tab;
            _ppScrollPx = 0f;
            if (tab == SidebarTab.Portals) PortalPickerService.Dirty = true;
            else _qDirty = true;
        }

        private void SetSidebarNormalOpen(bool open)
        {
            var es = EventSystem.current;
            if (es && es.currentSelectedGameObject) es.SetSelectedGameObject(null);
            _sidebarNormalOpen = open;
            if (!_ppShown)
            {
                _sidebarOpen = open;
                _pp.gameObject.SetActive(open);
                if (_sbExpand) _sbExpand.gameObject.SetActive(!open);
                if (open)
                {
                    PortalPickerService.Dirty = true;
                    _qDirty = true;
                }
            }
        }

        private TMP_InputField Field(Image back, int limit)
        {
            var area = HudArtRect("Text Area", back.transform);
            area.gameObject.AddComponent<RectMask2D>();
            Stretch(area);
            var text = _art.Text("Text", area, "font.body", "color.text.body", ThemeTokens.GetFloat("size.map.legend.font", 17f), TextAlignmentOptions.Left);
            Stretch(text.rectTransform);
            var f = back.gameObject.AddComponent<TMP_InputField>();
            f.textViewport = area;
            f.textComponent = text;
            f.targetGraphic = back;
            f.characterLimit = limit;
            f.transition = Selectable.Transition.None;
            f.navigation = new Navigation { mode = Navigation.Mode.None };
            return f;
        }

        private PortalRowUi NewPortalRow()
        {
            var r = new PortalRowUi { Root = HudArtRect("PortalRow", _ppList) };
            Top(r.Root);
            r.Back = r.Root.gameObject.AddComponent<Image>();
            r.Back.raycastTarget = true;
            r.Star = _art.Image("Star", r.Root, "sprite.ib.glyph.star.full", "color.text.display.gold", Image.Type.Simple);
            r.Star.preserveAspect = true;
            r.Name = _art.Text("Name", r.Root, "font.body", "color.text.body", ThemeTokens.GetFloat("size.map.legend.font", 17f), TextAlignmentOptions.Left);
            r.Name.overflowMode = TextOverflowModes.Ellipsis;
            r.Swatch = _art.Image("BiomeSwatch", r.Root, null, "color.frame", Image.Type.Simple);
            r.Biome = _art.Text("Biome", r.Root, "font.body", "color.text.body", ThemeTokens.GetFloat("size.map.biome.font", 15f), TextAlignmentOptions.Left);
            r.Biome.overflowMode = TextOverflowModes.Ellipsis;
            r.Distance = _art.Text("Distance", r.Root, "font.body", "color.text.body", ThemeTokens.GetFloat("size.map.biome.font", 15f), TextAlignmentOptions.Right);
            r.Stone = _art.Image("Stone", r.Root, "sprite.glyph.teleport.swirl", "color.state.disabled", Image.Type.Simple);
            r.Stone.preserveAspect = true;
            r.Icon = _art.Image("Icon", r.Root, null, "color.frame", Image.Type.Simple);
            r.Icon.preserveAspect = true;
            r.Pencil = _art.Image("Edit", r.Root, "sprite.button.icon.normal", "color.frame", Image.Type.Sliced, raycast: true);
            r.PencilGlyph = _art.Image("Glyph", r.Pencil.transform, null, "color.text.accent", Image.Type.Simple);
            r.PencilGlyph.sprite = MapIcons.Pencil;
            r.PencilGlyph.preserveAspect = true;
            Stretch(r.PencilGlyph.rectTransform);
            r.PencilGlyph.rectTransform.offsetMin = new Vector2(5f, 5f);
            r.PencilGlyph.rectTransform.offsetMax = new Vector2(-5f, -5f);
            r.Pencil.gameObject.AddComponent<Button>().onClick.AddListener(() =>
            {
                if (r.Index >= 0 && r.Index < _ppRows.Count) OpenPortalEditor(_ppRows[r.Index].Portal.Id);
            });
            if (_ppTooltip)
            {
                var tip = r.Pencil.gameObject.AddComponent<UITooltip>();
                tip.m_tooltipPrefab = _ppTooltip;
                tip.m_topic = "";
                tip.m_text = "";
            }
            r.Root.gameObject.AddComponent<PortalHit>().Click = e => OnPortalRowClick(r, e);
            return r;
        }

        private QuestRowUi NewQuestRow()
        {
            var r = new QuestRowUi { Root = HudArtRect("QuestRow", _ppList) };
            Top(r.Root);
            r.Back = r.Root.gameObject.AddComponent<Image>();
            r.Back.raycastTarget = true;
            r.Accent = _art.Image("Accent", r.Root, null, "color.frame", Image.Type.Simple);
            r.BadgeBack = _art.Image("BadgeBack", r.Root, "sprite.slot.recess", "color.frame", Image.Type.Sliced);
            r.Badge = _art.Text("Badge", r.BadgeBack.transform, "font.body", "color.text.body", ThemeTokens.GetFloat("size.map.biome.font", 15f) - 2f, TextAlignmentOptions.Center);
            r.Badge.overflowMode = TextOverflowModes.Ellipsis;
            r.Badge.textWrappingMode = TextWrappingModes.NoWrap;
            Stretch(r.Badge.rectTransform);
            r.Title = _art.Text("Title", r.Root, "font.body", "color.text.body", ThemeTokens.GetFloat("size.map.legend.font", 17f) - 1f, TextAlignmentOptions.Left);
            r.Title.overflowMode = TextOverflowModes.Ellipsis;
            r.Title.textWrappingMode = TextWrappingModes.NoWrap;
            r.Subtitle = _art.Text("Subtitle", r.Root, "font.body", "color.state.disabled", ThemeTokens.GetFloat("size.map.biome.font", 15f) - 1f, TextAlignmentOptions.Left);
            r.Subtitle.overflowMode = TextOverflowModes.Ellipsis;
            r.Subtitle.textWrappingMode = TextWrappingModes.NoWrap;
            r.Distance = _art.Text("Distance", r.Root, "font.body", "color.text.body", ThemeTokens.GetFloat("size.map.biome.font", 15f), TextAlignmentOptions.Right);
            r.Root.gameObject.AddComponent<PortalHit>().Click = e => OnQuestRowClick(r, e);
            return r;
        }

        private void OnPortalRowClick(PortalRowUi r, PointerEventData e)
        {
            if (r.Index < 0 || r.Index >= _ppRows.Count || _ppRows[r.Index].Here) return;
            var id = _ppRows[r.Index].Portal.Id;
            if (e.button == PointerEventData.InputButton.Right) PortalPickerService.AnyTravel()?.ToggleFavorite(id);
            else if (e.button == PointerEventData.InputButton.Left) PortalClick(id);
        }

        private void OnQuestRowClick(QuestRowUi r, PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || r.Index < 0 || r.Index >= _qRows.Count) return;
            var row = _qRows[r.Index];
            _qSelId = row.Id;
            FocusWorldPosition(row.Position);
        }

        // One click path for rows and pins (only teleports while portal travel session is active).
        public void PortalClick(ZDOID id)
        {
            if (id == ZDOID.None) return;
            if (_ppShown && _ppClicks.IsDouble(id, Time.unscaledTime)) PortalTeleport(id);
            else PortalSelect(id);
        }

        // On failure Morgott.Patcher already showed the centre message (IPortalTravel.TryTeleport).
        private static void PortalTeleport(ZDOID id) => PortalPickerService.Travel()?.TryTeleport(id, out _);

        public void PortalSelect(ZDOID id)
        {
            var i = PortalPickerMath.IndexOf(_ppRows, id);
            if (i < 0 || _ppRows[i].Here) return;
            _ppSel = id;
            _ppRingVersion = -1;
            _panActive = false;
            // Minimap.UpdateMap centres the large map on player position + m_mapOffset (Minimap.cs:1118-1165).
            if (Player.m_localPlayer) _map.m_mapOffset = _ppRows[i].Portal.Position - Player.m_localPlayer.transform.position - VisibleCenterShift();
            KeepVisible(i);
        }

        private void FocusWorldPosition(Vector3 worldPos)
        {
            if (!Player.m_localPlayer || !_map) return;
            var target = worldPos - Player.m_localPlayer.transform.position - VisibleCenterShift();
            target.y = 0f;
            _panTargetOffset = target;
            _panActive = true;
        }

        private void UpdateSmoothPan()
        {
            if (!_panActive || !_map) return;
            if (_map.m_dragView || Mathf.Abs(ZInput.GetJoyLeftStickX()) > 0.1f || Mathf.Abs(ZInput.GetJoyLeftStickY()) > 0.1f)
            {
                _panActive = false;
                return;
            }
            var cur = _map.m_mapOffset;
            cur.y = 0f;
            var t = 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime);
            var next = Vector3.Lerp(cur, _panTargetOffset, t);
            if ((next - _panTargetOffset).sqrMagnitude < 1f)
            {
                next = _panTargetOffset;
                _panActive = false;
            }
            _map.m_mapOffset = next;
            _map.m_pinUpdateRequired = true;
        }

        // Screen centre of the visible map area accounting for BOTH the left sidebar and the right MapLegend.
        public Vector2 PortalVisibleCenter()
        {
            var map = _map.m_mapImageLarge.rectTransform;
            var cam = UiCamera(map);
            var m = new Vector3[4];
            map.GetWorldCorners(m);
            var bl = RectTransformUtility.WorldToScreenPoint(cam, m[0]);
            var tr = RectTransformUtility.WorldToScreenPoint(cam, m[2]);
            var left = bl.x;
            var right = tr.x;
            if (_pp && _pp.gameObject.activeInHierarchy)
            {
                var p = new Vector3[4];
                _pp.GetWorldCorners(p);
                left = Mathf.Max(left, RectTransformUtility.WorldToScreenPoint(cam, p[2]).x);
            }
            if (_legend && _legend.gameObject.activeInHierarchy)
            {
                var l = new Vector3[4];
                _legend.GetWorldCorners(l);
                right = Mathf.Min(right, RectTransformUtility.WorldToScreenPoint(cam, l[0]).x);
            }
            return PortalPickerMath.VisibleCenter(left, right, bl.y, tr.y);
        }

        private static Camera UiCamera(Transform t)
        {
            var canvas = t.GetComponentInParent<Canvas>();
            return canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        }

        private Vector3 VisibleCenterShift()
        {
            var map = _map.m_mapImageLarge.rectTransform;
            var mid = RectTransformUtility.WorldToScreenPoint(UiCamera(map), map.TransformPoint(map.rect.center));
            var shift = _map.ScreenToWorldPoint(PortalVisibleCenter()) - _map.ScreenToWorldPoint(mid);
            shift.y = 0f;
            return shift;
        }

        internal void OnLargeMapClosed()
        {
            _panActive = false;
            CancelDeferredPinClick();
            CloseCustomPinModal();
            ClosePortalEditor();
            if (_ppShown) PortalSession(false);
            if (_pp && _pp.gameObject.activeSelf) _pp.gameObject.SetActive(false);
            if (_sbExpand && _sbExpand.gameObject.activeSelf) _sbExpand.gameObject.SetActive(false);
            _sidebarOpen = false;
            _largeWasOpen = false;
        }

        // Per frame while the large map is open.
        private void PortalLateUpdate(float inset, float top, float band)
        {
            if (!_pp) return;
            var travel = PortalPickerService.Travel();
            var travelActive = travel != null && travel.Active;
            if (!_largeWasOpen)
            {
                _largeWasOpen = true;
                PortalPickerService.Dirty = true;
                _qDirty = true;
                _sbNextLiveRefresh = Time.unscaledTime + 0.5f;
                if (!travelActive)
                {
                    _sidebarTab = _sidebarNormalTab;
                    _sidebarOpen = _sidebarNormalOpen;
                    _pp.gameObject.SetActive(_sidebarOpen);
                    if (_sbExpand) _sbExpand.gameObject.SetActive(!_sidebarOpen);
                }
            }
            if (travelActive != _ppShown) PortalSession(travelActive);

            // Keep expand button positioned and visible whenever collapsed on normal M map.
            if (!travelActive)
            {
                if (_sidebarOpen != _sidebarNormalOpen)
                {
                    _sidebarOpen = _sidebarNormalOpen;
                    _pp.gameObject.SetActive(_sidebarOpen);
                }
                if (_sbExpand)
                {
                    var showExpand = !_sidebarOpen;
                    if (_sbExpand.gameObject.activeSelf != showExpand) _sbExpand.gameObject.SetActive(showExpand);
                    if (showExpand) LayoutExpandButton(inset, top);
                }
            }
            else if (_sbExpand && _sbExpand.gameObject.activeSelf)
            {
                _sbExpand.gameObject.SetActive(false);
            }

            if (!_sidebarOpen)
            {
                if (_ppRing && _ppRing.gameObject.activeSelf) _ppRing.gameObject.SetActive(false);
                return;
            }

            // Periodic refresh for live player distance while map stays open.
            var now = Time.unscaledTime;
            if (now >= _sbNextLiveRefresh)
            {
                _sbNextLiveRefresh = now + 0.5f;
                if (!_ppShown) PortalPickerService.Dirty = true;
                _qDirty = true;
            }
            if (_qPinsVersion != PinsVersion)
            {
                _qPinsVersion = PinsVersion;
                _qDirty = true;
            }

            var anyTravel = PortalPickerService.AnyTravel();
            if (PortalPickerService.Dirty) PortalRebuild(anyTravel, travelActive);
            if (_qDirty) QuestRebuild();

            PortalEditorUpdate();
            if (_ppShown && _sidebarTab == SidebarTab.Portals && !PortalEditorShown && !CustomPinModalShown) PortalGamepad();
            SidebarLayout(inset, top, band, travelActive);
            if (_sidebarTab == SidebarTab.Portals) PortalRingUpdate();
            else if (_ppRing && _ppRing.gameObject.activeSelf) _ppRing.gameObject.SetActive(false);
        }

        private void LayoutExpandButton(float inset, float top)
        {
            var label = "▸ " + Words.Get("$ui_map_sidebar_open");
            if (_sbExpandLabel.text != label) _sbExpandLabel.text = label;
            var w = Mathf.Max(118f, _sbExpandLabel.GetPreferredValues(label).x + 28f);
            Place(_sbExpand, inset + 8f, top + 8f, w, 34f);
        }

        private void PortalSession(bool active)
        {
            _ppShown = active;
            if (_keyRow) _keyRow.gameObject.SetActive(!active);
            _ppSel = ZDOID.None;
            _ppRingPin = null;
            _ppScrollPx = 0f;
            ClosePortalEditor();
            if (_ppRing) _ppRing.gameObject.SetActive(false);
            if (active)
            {
                _sidebarTab = SidebarTab.Portals;
                _sidebarOpen = true;
                _pp.gameObject.SetActive(true);
                if (_sbExpand) _sbExpand.gameObject.SetActive(false);
                if (_ppSearch.text.Length > 0) _ppSearch.text = "";
                PortalPickerService.Dirty = true;
                _qDirty = true;
            }
            else if (_large && _large.gameObject.activeInHierarchy)
            {
                _sidebarTab = _sidebarNormalTab;
                _sidebarOpen = _sidebarNormalOpen;
                _pp.gameObject.SetActive(_sidebarOpen);
                if (_sbExpand) _sbExpand.gameObject.SetActive(!_sidebarOpen);
            }
            else
            {
                _sidebarOpen = false;
                _pp.gameObject.SetActive(false);
            }
            Plugin.Log.LogInfo($"[Morgott.UI] portal picker {(active ? "open" : "closed")}: panel={_pp.gameObject.activeSelf} vanillaKeyRow={(_keyRow ? _keyRow.gameObject.activeSelf.ToString() : "none")}");
        }

        private void PortalRebuild(IPortalTravel travel, bool travelActive)
        {
            PortalPickerService.Dirty = false;
            _ppPortals.Clear();
            travel?.GetPortals(_ppPortals);
            var shown = new List<PortalInfo>(_ppPortals.Count);
            foreach (var p in _ppPortals)
            {
                var c = p;
                c.Name = Words.Get(p.Name);
                shown.Add(c);
            }
            PortalInfo? here = null;
            if (travelActive && travel != null)
            {
                var entry = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(travel.Entry) : null;
                if (entry != null)
                {
                    var portalTag = entry.GetString(ZDOVars.s_tag);
                    var rgb = entry.GetInt(ColorKey, PortalStyle.DefaultColor);
                    var custom = rgb != PortalStyle.DefaultColor && PortalStyle.IsColor(rgb);
                    here = new PortalInfo
                    {
                        Id = entry.m_uid,
                        Name = Words.Get(string.IsNullOrEmpty(portalTag) ? "$piece_portal" : portalTag),
                        Position = entry.GetPosition(),
                        CustomColor = custom,
                        Color = custom ? ToColor(rgb) : Color.white,
                        Icon = entry.GetInt(IconKey, PortalStyle.NoIcon),
                    };
                }
            }
            var playerPos = Player.m_localPlayer ? Player.m_localPlayer.transform.position : Vector3.zero;
            var referencePos = here.HasValue ? here.Value.Position : playerPos;
            _ppFav = PortalPickerMath.Build(shown, here, referencePos, _ppSearch.text, _ppRows);
            _ppView.Clear();
            foreach (var row in _ppRows)
            {
                var biome = WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(row.Portal.Position) : Heightmap.Biome.None;
                var denied = "";
                var canEdit = travel != null && travel.CanEdit(row.Portal.Id, out denied);
                _ppView.Add(new PortalView
                {
                    CanEdit = canEdit,
                    Denied = canEdit ? Words.Get("$ui_portal_edit_tip") : denied,
                    Biome = Words.Get(MapFilterMath.NameKey(biome)),
                    BiomeColor = BiomeOverlay.BiomeColor(biome),
                    Distance = row.Here ? Words.Get("$ui_portal_here") : Localization.instance.Localize("$ui_portal_meters", PortalPickerMath.Meters(row.Distance)),
                });
            }
            if (_ppSel != ZDOID.None && PortalPickerMath.IndexOf(_ppRows, _ppSel) < 0) _ppSel = ZDOID.None;
            _ppRingVersion = -1;
        }

        private void QuestRebuild()
        {
            _qDirty = false;
            MapQuests.GetAll(_qCore);
            _qPins.Clear();
            if (_map && _map.m_pins != null)
            {
                foreach (var pin in _map.m_pins)
                {
                    if (pin == null) continue;
                    if (pin.m_type == Minimap.PinType.Boss || MapQuestSidebarMath.IsHildirPin(pin.m_type))
                        _qPins.Add(new QuestPinInput(pin.m_type, pin.m_name, pin.m_pos));
                }
            }
            var playerPos = Player.m_localPlayer ? Player.m_localPlayer.transform.position : Vector3.zero;
            _qNpcCount = MapQuestSidebarMath.Build(
                _qCore, _qPins, playerPos, _ppSearch.text, _qRows,
                k => Words.Get(MapQuestSidebarMath.KindLabelToken(k)),
                s => Words.Get(s),
                key => ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(key));
            _qView.Clear();
            for (var i = 0; i < _qRows.Count; i++)
            {
                var row = _qRows[i];
                var sub = row.Subtitle;
                if (string.IsNullOrEmpty(sub) && WorldGenerator.instance != null)
                {
                    var biome = WorldGenerator.instance.GetBiome(row.Position);
                    if (biome != Heightmap.Biome.None) sub = Words.Get(MapFilterMath.NameKey(biome));
                }
                _qView.Add(new QuestView
                {
                    Category = Words.Get(MapQuestSidebarMath.KindLabelToken(row.Kind)),
                    Subtitle = sub ?? "",
                    Distance = Localization.instance.Localize("$ui_portal_meters", PortalPickerMath.Meters(row.Distance)),
                    Accent = MapQuestSidebarMath.KindAccentColor(row.Kind),
                });
            }
        }

        private void PortalGamepad()
        {
            if (!ZInput.IsGamepadActive() || PortalTyping || _ppRows.Count == 0) return;
            var step = ZInput.GetButtonDown("JoyDPadDown") || ZInput.GetButtonDown("JoyLStickDown") ? 1
                : ZInput.GetButtonDown("JoyDPadUp") || ZInput.GetButtonDown("JoyLStickUp") ? -1 : 0;
            if (step != 0)
            {
                var next = PortalPickerMath.Step(_ppRows, PortalPickerMath.IndexOf(_ppRows, _ppSel), step);
                if (next >= 0) PortalSelect(_ppRows[next].Portal.Id);
            }
            else if (ZInput.GetButtonDown("JoyButtonA") && _ppSel != ZDOID.None) PortalTeleport(_ppSel);
            else if (ZInput.GetButtonDown("JoyButtonY") && _ppSel != ZDOID.None) PortalPickerService.Travel()?.ToggleFavorite(_ppSel);
        }

        private float PortalRowH => ThemeTokens.GetFloat("size.map.portal.row", 54f);
        private QuestRowLayout QuestLayout => MapQuestSidebarMath.ComputeRowLayout(
            ThemeTokens.GetFloat("size.map.legend.font", 20f),
            ThemeTokens.GetFloat("size.map.biome.font", 18f));
        private float QuestRowH => QuestLayout.RowHeight;
        private float PortalSectionH => ThemeTokens.GetFloat("size.map.legend.header.h", 30f) + ThemeTokens.GetFloat("size.map.legend.divider", 11f) + 4f;

        // Content y (from the list top) of portal row i: section headers above it.
        private float PortalRowY(int i) => (_ppFav > 0 ? PortalSectionH : 0f) + (i >= _ppFav && _ppRows.Count > _ppFav ? PortalSectionH : 0f) + i * PortalRowH;

        // Content y (from the list top) of quest row i: NPC header + Known Bosses header above it.
        private float QuestRowY(int i) =>
            (_qNpcCount > 0 ? PortalSectionH : 0f)
            + (i >= _qNpcCount && _qRows.Count > _qNpcCount ? PortalSectionH : 0f)
            + i * QuestRowH;

        private void KeepVisible(int i)
        {
            if (!_ppList) return;
            var view = _ppList.rect.height;
            var y = PortalRowY(i);
            if (y < _ppScrollPx) _ppScrollPx = y;
            else if (y + PortalRowH > _ppScrollPx + view) _ppScrollPx = y + PortalRowH - view;
        }

        private void SidebarLayout(float inset, float top, float band, bool travelActive)
        {
            var w = ThemeTokens.GetFloat("size.map.legend", 300f);
            _pp.anchorMin = Vector2.zero;
            _pp.anchorMax = new Vector2(0f, 1f);
            _pp.pivot = new Vector2(0f, 0.5f);
            var min = new Vector2(inset, inset + band);
            var max = new Vector2(inset + w, -top);
            if (_pp.offsetMin != min) _pp.offsetMin = min;
            if (_pp.offsetMax != max) _pp.offsetMax = max;
            var h = _pp.rect.height;
            var pad = ThemeTokens.GetFloat("size.map.legend.pad", 16f);
            var headerH = ThemeTokens.GetFloat("size.map.legend.header.h", 30f);
            var divider = ThemeTokens.GetFloat("size.map.legend.divider", 11f);
            var searchH = ThemeTokens.GetFloat("size.map.portal.search", 36f);
            var rowW = w - 2f * pad;

            // Top tab bar: ПОРТАЛЫ | ЗАДАНИЯ (+ collapse button on normal map).
            const float tabH = 34f, tabGap = 6f;
            var collapseW = travelActive ? 0f : 32f;
            var tabsW = rowW - (collapseW > 0f ? collapseW + tabGap : 0f);
            var tabW = (tabsW - tabGap) * 0.5f;
            Place(_sbTabPortalsImg.rectTransform, pad, pad, tabW, tabH);
            Place(_sbTabQuestsImg.rectTransform, pad + tabW + tabGap, pad, tabW, tabH);
            if (_sbCollapseImg.gameObject.activeSelf != !travelActive) _sbCollapseImg.gameObject.SetActive(!travelActive);
            if (!travelActive) Place(_sbCollapseImg.rectTransform, pad + rowW - collapseW, pad, collapseW, tabH);

            var onPortals = _sidebarTab == SidebarTab.Portals;
            StyleTab(_sbTabPortalsImg, _sbTabPortalsLabel, onPortals);
            StyleTab(_sbTabQuestsImg, _sbTabQuestsLabel, !onPortals);
            var pLabel = _ppRows.Count > 0 ? $"{Words.Get("$ui_portal_title")} ({_ppRows.Count})" : Words.Get("$ui_portal_title");
            var qLabel = _qRows.Count > 0 ? $"{Words.Get("$ui_map_quests")} ({_qRows.Count})" : Words.Get("$ui_map_quests");
            if (_sbTabPortalsLabel.text != pLabel) _sbTabPortalsLabel.text = pLabel;
            if (_sbTabQuestsLabel.text != qLabel) _sbTabQuestsLabel.text = qLabel;

            var y = pad + tabH + 2f;
            Place(_ppTitleLine.rectTransform, pad, y, rowW, divider);
            y += divider + 4f;

            // Shared search input.
            Place(_ppSearchBack.rectTransform, pad, y, rowW, searchH);
            var g = searchH * 0.6f;
            Place(_ppSearchGlyph.rectTransform, 8f, (searchH - g) * 0.5f, g, g);
            var area = _ppSearch.textViewport;
            area.offsetMin = new Vector2(g + 16f, 0f);
            area.offsetMax = new Vector2(-8f, 0f);
            Place(_ppSearchHint.rectTransform, g + 16f, 0f, rowW - g - 24f, searchH);
            var hint = Words.Get("$ui_portal_search");
            if (_ppSearchHint.text != hint) _ppSearchHint.text = hint;
            var showHint = _ppSearch.text.Length == 0 && !_ppSearch.isFocused;
            if (_ppSearchHint.gameObject.activeSelf != showHint) _ppSearchHint.gameObject.SetActive(showHint);
            y += searchH + 8f;

            // Bottom hint line.
            UpdateSidebarHint(travelActive, onPortals);
            var hintH = travelActive ? ThemeTokens.GetFloat("size.map.portal.hint.h", 66f) : 40f;
            Place(_ppHint.rectTransform, pad, h - pad - hintH, rowW, hintH);
            var rowH = onPortals ? PortalRowH : QuestRowH;
            var view = Mathf.Max(rowH, h - y - pad - hintH - 6f);
            Place(_ppList, pad, y, rowW, view);

            var content = onPortals
                ? (_ppRows.Count > 0 ? PortalRowY(_ppRows.Count) : 0f)
                : (_qRows.Count > 0 ? QuestRowY(_qRows.Count) : 0f);
            _ppContentH = content;
            _ppViewH = view;
            if (_ppScroll && Mathf.Abs(_ppScroll.Delta) > 1e-5f)
            {
                _ppScrollPx -= ScrollSpeed.WheelPx(_ppScroll.Delta, rowH, view);
                _ppScroll.Delta = 0f;
            }
            _ppScrollPx = Mathf.Clamp(_ppScrollPx, 0f, Mathf.Max(0f, content - view));
            var scrolling = content > view + 0.5f;
            var barW = ThemeTokens.GetFloat("size.map.legend.scroll", 8f);
            var listW = rowW - (scrolling ? barW + 6f : 0f);
            if (_ppTrack.gameObject.activeSelf != scrolling) _ppTrack.gameObject.SetActive(scrolling);
            if (scrolling)
            {
                Place(_ppTrack.rectTransform, pad + rowW - barW, y, barW, view);
                var thumb = _ppThumb.rectTransform;
                thumb.anchorMin = new Vector2(0f, 1f - (_ppScrollPx + view) / content);
                thumb.anchorMax = new Vector2(1f, 1f - _ppScrollPx / content);
                thumb.offsetMin = thumb.offsetMax = Vector2.zero;
            }

            if (onPortals) LayoutPortalsContent(listW, view, headerH, divider);
            else LayoutQuestsContent(listW, view, headerH, divider);
        }

        private void StyleTab(Image img, TMP_Text label, bool active)
        {
            var token = active ? "sprite.tab.selected" : "sprite.tab.normal";
            if (img.sprite != ThemeTokens.GetSprite(token)) _art.Restyle(img, token, "color.frame", Image.Type.Sliced);
            label.color = ThemeTokens.GetColor(active ? "color.text.header" : "color.state.disabled", Color.white);
        }

        private void UpdateSidebarHint(bool travelActive, bool onPortals)
        {
            if (!_ppHint) return;
            string text;
            if (travelActive && onPortals)
            {
                var mouse = KeyLabels.Latin["$button_mouse0"];
                text = ZInput.IsGamepadActive()
                    ? Localization.instance.Localize("$ui_portal_hint_pad")
                    : Localization.instance.Localize("$ui_portal_hint", mouse, KeyLabels.Latin["$button_mouse1"], "Esc");
            }
            else if (onPortals)
            {
                text = Localization.instance.Localize("$ui_portal_hint_browse", KeyLabels.Latin["$button_mouse0"], KeyLabels.Latin["$button_mouse1"]);
            }
            else
            {
                text = Localization.instance.Localize("$ui_map_quests_hint", KeyLabels.Latin["$button_mouse0"]);
            }
            if (_ppHint.text != text) _ppHint.text = text;
        }

        private void LayoutPortalsContent(float listW, float view, float headerH, float divider)
        {
            // Hide quest-only widgets.
            if (_qActiveHeader.gameObject.activeSelf) { _qActiveHeader.gameObject.SetActive(false); _qActiveLine.gameObject.SetActive(false); }
            if (_qBossHeader.gameObject.activeSelf) { _qBossHeader.gameObject.SetActive(false); _qBossLine.gameObject.SetActive(false); }
            for (var i = 0; i < _qRowUi.Count; i++)
                if (_qRowUi[i].Root.gameObject.activeSelf) _qRowUi[i].Root.gameObject.SetActive(false);

            var empty = _ppRows.Count == 0;
            if (_sbEmptyText.gameObject.activeSelf != empty) _sbEmptyText.gameObject.SetActive(empty);
            if (empty)
            {
                Place(_sbEmptyText.rectTransform, 8f, 20f, listW - 16f, 60f);
                var msg = Words.Get("$ui_portal_empty");
                if (_sbEmptyText.text != msg) _sbEmptyText.text = msg;
            }

            var favShown = _ppFav > 0;
            var allShown = _ppRows.Count > _ppFav;
            if (_ppFavHeader.gameObject.activeSelf != favShown) { _ppFavHeader.gameObject.SetActive(favShown); _ppFavLine.gameObject.SetActive(favShown); }
            if (_ppAllHeader.gameObject.activeSelf != allShown) { _ppAllHeader.gameObject.SetActive(allShown); _ppAllLine.gameObject.SetActive(allShown); }
            if (favShown) Section(_ppFavHeader, _ppFavLine, "$ui_portal_favorites", 0f, -_ppScrollPx, listW, headerH, divider);
            if (allShown) Section(_ppAllHeader, _ppAllLine, "$ui_portal_all", 0f, PortalRowY(_ppFav) - PortalSectionH - _ppScrollPx, listW, headerH, divider);

            var rowH = PortalRowH;
            while (_ppRowUi.Count < _ppRows.Count) _ppRowUi.Add(NewPortalRow());
            for (var i = 0; i < _ppRowUi.Count; i++)
            {
                var r = _ppRowUi[i];
                var ry = i < _ppRows.Count ? PortalRowY(i) - _ppScrollPx : 0f;
                var on = i < _ppRows.Count && ry + rowH > 0f && ry < view;
                if (r.Root.gameObject.activeSelf != on) r.Root.gameObject.SetActive(on);
                r.Index = on ? i : -1;
                if (!on) continue;
                LayPortalRow(r, _ppRows[i], _ppView[i], ry, listW, rowH);
            }
        }

        private void LayoutQuestsContent(float listW, float view, float headerH, float divider)
        {
            // Hide portal-only widgets.
            if (_ppFavHeader.gameObject.activeSelf) { _ppFavHeader.gameObject.SetActive(false); _ppFavLine.gameObject.SetActive(false); }
            if (_ppAllHeader.gameObject.activeSelf) { _ppAllHeader.gameObject.SetActive(false); _ppAllLine.gameObject.SetActive(false); }
            for (var i = 0; i < _ppRowUi.Count; i++)
                if (_ppRowUi[i].Root.gameObject.activeSelf) _ppRowUi[i].Root.gameObject.SetActive(false);

            var empty = _qRows.Count == 0;
            if (_sbEmptyText.gameObject.activeSelf != empty) _sbEmptyText.gameObject.SetActive(empty);
            if (empty)
            {
                Place(_sbEmptyText.rectTransform, 8f, 20f, listW - 16f, 60f);
                var msg = Words.Get("$ui_map_quests_empty");
                if (_sbEmptyText.text != msg) _sbEmptyText.text = msg;
            }

            var npcShown = _qNpcCount > 0;
            var bossShown = _qRows.Count > _qNpcCount;
            if (_qActiveHeader.gameObject.activeSelf != npcShown) { _qActiveHeader.gameObject.SetActive(npcShown); _qActiveLine.gameObject.SetActive(npcShown); }
            if (_qBossHeader.gameObject.activeSelf != bossShown) { _qBossHeader.gameObject.SetActive(bossShown); _qBossLine.gameObject.SetActive(bossShown); }
            if (npcShown) Section(_qActiveHeader, _qActiveLine, "$ui_map_quests_active", 0f, -_ppScrollPx, listW, headerH, divider);
            if (bossShown) Section(_qBossHeader, _qBossLine, "$ui_map_quests_bosses", 0f, QuestRowY(_qNpcCount) - PortalSectionH - _ppScrollPx, listW, headerH, divider);

            var rowH = QuestRowH;
            while (_qRowUi.Count < _qRows.Count) _qRowUi.Add(NewQuestRow());
            for (var i = 0; i < _qRowUi.Count; i++)
            {
                var r = _qRowUi[i];
                var ry = i < _qRows.Count ? QuestRowY(i) - _ppScrollPx : 0f;
                var on = i < _qRows.Count && ry + rowH > 0f && ry < view;
                if (r.Root.gameObject.activeSelf != on) r.Root.gameObject.SetActive(on);
                r.Index = on ? i : -1;
                if (!on) continue;
                LayQuestRow(r, _qRows[i], _qView[i], ry, listW, rowH);
            }
        }

        private void LayQuestRow(QuestRowUi r, QuestRow row, QuestView view, float y, float w, float rowH)
        {
            var lay = QuestLayout;
            Place(r.Root, 0f, y, w, lay.CardHeight);
            var look = row.Id == _qSelId ? 1 : 0;
            if (r.Look != look)
            {
                r.Look = look;
                if (look == 0) { r.Back.sprite = null; r.Back.color = new Color(0f, 0f, 0f, 0.16f); }
                else _art.Restyle(r.Back, "sprite.row.recipe.selected", "color.frame", Image.Type.Sliced);
                r.Back.enabled = true;
            }
            Place(r.Accent.rectTransform, 0f, 2f, 4f, lay.CardHeight - 4f);
            r.Accent.color = view.Accent;

            const float padX = 10f;
            var distW = Mathf.Clamp(w * 0.28f, 68f, 100f);
            if (Mathf.Abs(r.Badge.fontSize - lay.BadgeFont) > 0.05f) r.Badge.fontSize = lay.BadgeFont;
            if (Mathf.Abs(r.Distance.fontSize - lay.SubFont) > 0.05f) r.Distance.fontSize = lay.SubFont;
            if (Mathf.Abs(r.Title.fontSize - lay.TitleFont) > 0.05f) r.Title.fontSize = lay.TitleFont;
            if (Mathf.Abs(r.Subtitle.fontSize - lay.SubFont) > 0.05f) r.Subtitle.fontSize = lay.SubFont;

            var badgeW = Mathf.Clamp(r.Badge.GetPreferredValues(view.Category).x + 14f, 54f, w - padX - distW - 10f);
            Place(r.BadgeBack.rectTransform, padX, lay.BadgeY, badgeW, lay.BadgeH);
            r.BadgeBack.color = new Color(view.Accent.r * 0.45f, view.Accent.g * 0.45f, view.Accent.b * 0.45f, 0.85f);
            if (r.Badge.text != view.Category) r.Badge.text = view.Category;
            r.Badge.color = view.Accent;

            Place(r.Distance.rectTransform, w - distW - 6f, lay.BadgeY - 1f, distW, lay.BadgeH + 2f);
            if (r.Distance.text != view.Distance) r.Distance.text = view.Distance;
            r.Distance.color = ThemeTokens.GetColor("color.text.body", Color.white);

            Place(r.Title.rectTransform, padX, lay.TitleY, w - padX - 8f, lay.TitleH);
            if (r.Title.text != row.Title) r.Title.text = row.Title;
            r.Title.color = ThemeTokens.GetColor("color.text.header", Color.white);

            Place(r.Subtitle.rectTransform, padX, lay.SubY, w - padX - 8f, lay.SubH);
            if (r.Subtitle.text != view.Subtitle) r.Subtitle.text = view.Subtitle;
        }

        private void LayPortalRow(PortalRowUi r, PortalRow row, PortalView view, float y, float w, float rowH)
        {
            Place(r.Root, 0f, y, w, rowH - 2f);
            var look = row.Here ? 2 : row.Portal.Id == _ppSel ? 1 : 0;
            if (r.Look != look)
            {
                r.Look = look;
                if (look == 0) { r.Back.sprite = null; r.Back.color = Color.clear; }
                else _art.Restyle(r.Back, look == 1 ? "sprite.row.recipe.selected" : "sprite.row.recipe.unavailable", "color.frame", Image.Type.Sliced);
                r.Back.enabled = true;
            }
            var star = rowH * 0.4f;
            if (r.Star.enabled != row.Portal.Favorite) r.Star.enabled = row.Portal.Favorite;
            Place(r.Star.rectTransform, 4f, (rowH - star) * 0.5f, star, star);
            var iconSize = rowH * 0.78f;
            var icon = PortalIcons.Instance.Get(row.Portal.Icon);
            if (r.Icon.sprite != icon) r.Icon.sprite = icon;
            if (r.Icon.enabled != (bool)icon) r.Icon.enabled = icon;
            Place(r.Icon.rectTransform, 6f + star, (rowH - iconSize) * 0.5f, iconSize, iconSize);
            var x = 10f + star + iconSize;
            var pencil = rowH * 0.62f;
            var right = w - pencil - 6f;
            Place(r.Pencil.rectTransform, right + 2f, (rowH - pencil) * 0.5f, pencil, pencil);
            r.PencilGlyph.color = ThemeTokens.GetColor(view.CanEdit ? "color.text.accent" : "color.state.disabled", Color.white);
            if (r.Pencil.TryGetComponent<UITooltip>(out var tip) && tip.m_text != view.Denied) tip.m_text = view.Denied;
            var distW = right * 0.32f;
            var half = (rowH - 2f) * 0.5f;
            Place(r.Name.rectTransform, x, 2f, right - x - distW, half);
            if (r.Name.text != row.Portal.Name) r.Name.text = row.Portal.Name;
            var sw = half * 0.45f;
            Place(r.Swatch.rectTransform, x + 2f, half + (half - sw) * 0.5f, sw, sw);
            r.Swatch.color = view.BiomeColor;
            Place(r.Biome.rectTransform, x + sw + 8f, half, right - x - sw - 8f - distW, half);
            if (r.Biome.text != view.Biome) r.Biome.text = view.Biome;
            var stone = row.Portal.Stone;
            if (r.Stone.enabled != stone) r.Stone.enabled = stone;
            if (stone)
            {
                var gs = half * 0.7f;
                var nameW = Mathf.Min(r.Name.GetPreferredValues(r.Name.text).x, right - x - distW - gs - 6f);
                Place(r.Stone.rectTransform, x + nameW + 6f, 2f + (half - gs) * 0.5f, gs, gs);
            }
            Place(r.Distance.rectTransform, right - distW, 0f, distW - 4f, rowH - 2f);
            if (r.Distance.text != view.Distance) r.Distance.text = view.Distance;
            var dim = ThemeTokens.GetColor(row.Here ? "color.state.disabled" : "color.text.body", Color.white);
            r.Biome.color = r.Distance.color = dim;
            r.Name.color = row.Here ? dim : row.Portal.CustomColor ? row.Portal.Color : dim;
        }

        // Ring on the selected portal's pin marker (a child: follows pan / zoom).
        private void PortalRingUpdate()
        {
            if (_ppSel == ZDOID.None)
            {
                if (_ppRing) _ppRing.gameObject.SetActive(false);
                return;
            }
            if (_ppRingVersion != PinsVersion || _ppRingPin == null || !_map.m_pins.Contains(_ppRingPin))
            {
                _ppRingVersion = PinsVersion;
                var i = PortalPickerMath.IndexOf(_ppRows, _ppSel);
                _ppRingPin = null;
                var best = float.MaxValue;
                if (i >= 0)
                    foreach (var pin in _map.m_pins)
                    {
                        if (pin.m_type != MapFilterMath.Portal || Words.Get(pin.m_name) != _ppRows[i].Portal.Name) continue;
                        var d = PortalPickerMath.DistanceXZ(pin.m_pos, _ppRows[i].Portal.Position);
                        if (d < best) { best = d; _ppRingPin = pin; }
                    }
            }
            var marker = _ppRingPin?.m_uiElement;
            if (!marker)
            {
                if (_ppRing) _ppRing.gameObject.SetActive(false);
                return;
            }
            if (!_ppRing)
            {
                _ppRing = _art.Image("RuneSkin_PortalRing", marker, "sprite.ring.pin.select", "color.text.accent", Image.Type.Simple);
                _ppRing.preserveAspect = true;
            }
            var rt = _ppRing.rectTransform;
            if (rt.parent != marker) rt.SetParent(marker, false);
            var mid = new Vector2(0.5f, 0.5f);
            if (rt.anchorMin != mid || rt.anchorMax != mid || rt.pivot != mid || rt.anchoredPosition != Vector2.zero)
            {
                rt.anchorMin = rt.anchorMax = rt.pivot = mid;
                rt.anchoredPosition = Vector2.zero;
            }
            var size = marker.sizeDelta * ThemeTokens.GetFloat("ratio.map.portal.ring", 2.2f);
            if (rt.sizeDelta != size) rt.sizeDelta = size;
            if (!_ppRing.gameObject.activeSelf) _ppRing.gameObject.SetActive(true);
        }
    }

    // Map clicks and pin edits during a portal travel session: clicks on a portal pin go through the picker's click path,
    // and pin editing / pings stay off. Minimap.InTextInput is handled in CustomPinInput (always active when Minimap owned).
    [HarmonyPatch]
    public static class PortalPickerInput
    {
        private static bool Prepare() => PortalPickerService.Registered && Ownership.OwnershipRegistry.IsOwned(Ownership.OwnershipRegistry.Minimap);

        private static bool Active => PortalPickerService.Active;

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnMapLeftClick)), HarmonyPrefix]
        private static bool OnLeftClick(Minimap __instance)
        {
            if (!Active) return true;
            var travel = PortalPickerService.Travel();
            var id = travel.PortalAt(__instance.ScreenToWorldPoint(ZInput.pointerPosition), __instance.PinInteractRadius);
            MinimapSkin.Instance?.PortalClick(id);
            return false;
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnMapDblClick)), HarmonyPrefix] private static bool NoDblClick() => !Active;
        [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnMapMiddleClick)), HarmonyPrefix] private static bool NoPing() => !Active;
        [HarmonyPatch(typeof(Minimap), nameof(Minimap.RemovePinUnderPointer)), HarmonyPrefix] private static bool NoRemove() => !Active;
        [HarmonyPatch(typeof(Minimap), nameof(Minimap.ShowPinNameInput)), HarmonyPrefix] private static bool NoNewPin() => !Active;
        [HarmonyPatch(typeof(Minimap), nameof(Minimap.SelectIcon)), HarmonyPrefix] private static bool NoIcon() => !Active;

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.RemovePin), typeof(Vector3), typeof(float)), HarmonyPrefix]
        private static bool NoRemoveAt(ref bool __result) => __result = !Active;

        [HarmonyPatch(typeof(Chat), nameof(Chat.SendPing)), HarmonyPrefix] private static bool NoChatPing() => !Active;
    }
}
