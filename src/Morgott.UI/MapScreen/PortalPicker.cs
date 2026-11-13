using System.Collections.Generic;
using HarmonyLib;
using Morgott.Core;
using Morgott.UI.PlayerHud;
using Morgott.UI.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Morgott.UI.MapScreen
{
    // Core IPortalPicker consumer (owner Morgott.Patcher serves IPortalTravel): while registered, walking into a portal
    // opens the large map as a destination picker (mockup docs\mockups\portal-picker-v1.png; the map is full screen, so
    // the list is a left overlay built like the right МЕТКИ column). Registered in Plugin.Awake when [Map] PortalPicker
    // is on and Morgott.UI owns the minimap; off = not registered = vanilla portals.
    public sealed class PortalPickerService : IPortalPicker
    {
        public static readonly PortalPickerService Instance = new PortalPickerService();
        internal static bool Registered;
        internal static bool Dirty = true;

        public void OnPortalsChanged() => Dirty = true;

        // Resolved per use (Core Services rule: never cached).
        internal static IPortalTravel Travel() => Registered && Services.TryGet(out IPortalTravel t) ? t : null;

        internal static bool Active => Travel() is IPortalTravel t && t.Active;
    }

    // Left column during a travel session: ПОРТАЛЫ title, search, ИЗБРАННОЕ / ВСЕ ПОРТАЛЫ rows (PortalPickerMath). Row or
    // pin: click = select (map centred on the portal, ring on its pin), double click = travel, right click = favourite;
    // pencil on a row = style editor (PortalEditor.cs). Gamepad: d-pad select, A travel, Y favourite. The key hint band under
    // the map shows the picker keys instead of the pin-edit keys.
    public sealed partial class MinimapSkin
    {
        public sealed class PortalHit : MonoBehaviour, IPointerClickHandler
        {
            public System.Action<PointerEventData> Click;
            public void OnPointerClick(PointerEventData eventData) => Click?.Invoke(eventData);
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

        private RectTransform _pp, _ppList;
        private TMP_Text _ppTitle, _ppFavHeader, _ppAllHeader, _ppSearchHint, _ppHint;
        private Image _ppTitleLine, _ppFavLine, _ppAllLine, _ppSearchBack, _ppSearchGlyph, _ppRing, _ppTrack, _ppThumb;
        private TMP_InputField _ppSearch;
        private GameObject _ppTooltip;
        private LegendScroll _ppScroll;
        private readonly List<PortalRowUi> _ppRowUi = new List<PortalRowUi>();
        private readonly List<PortalInfo> _ppPortals = new List<PortalInfo>();
        private readonly List<PortalRow> _ppRows = new List<PortalRow>();
        private readonly List<PortalView> _ppView = new List<PortalView>();
        private readonly PortalClicks _ppClicks = new PortalClicks();
        private int _ppFav;
        private float _ppScrollPx;
        private bool _ppShown;
        private ZDOID _ppSel = ZDOID.None;
        private Minimap.PinData _ppRingPin;
        private int _ppRingVersion = -1;

        // Harness / diagnostics.
        public bool PortalPanelShown => _ppShown && _pp && _pp.gameObject.activeInHierarchy;
        public IReadOnlyList<PortalRow> PortalRows => _ppRows;
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

        // Also true while the editor is open: Minimap.Update then leaves Esc / map keys alone (Minimap.cs:698-706).
        internal static bool PortalTyping => Instance && (Instance._ppSearch && Instance._ppSearch.isFocused || Instance.PortalEditorShown);

        internal static bool PortalHover => Instance && Instance._ppScroll && Instance._ppScroll.Over && Instance.PortalPanelShown;

        private void BuildPortalPanel()
        {
            if (!PortalPickerService.Registered) return;
            _pp = HudArtRect("RuneSkin_PortalPicker", _large);
            _pp.SetSiblingIndex(_legend.GetSiblingIndex() + 1);
            var back = _pp.gameObject.AddComponent<Image>();
            _art.Restyle(back, "sprite.frame.inset", "color.frame", Image.Type.Sliced);
            back.raycastTarget = true; // clicks on the list never fall through to the map
            _ppScroll = _pp.gameObject.AddComponent<LegendScroll>();
            var headerSize = ThemeTokens.GetFloat("size.map.legend.header", 21f);
            _ppTitle = _art.Text("Title", _pp, "font.display", "color.text.header", headerSize, TextAlignmentOptions.Center);
            _ppTitleLine = _art.Image("TitleDivider", _pp, "sprite.divider", "color.frame", Image.Type.Sliced);
            _ppSearchBack = _art.Image("Search", _pp, "sprite.field.search", "color.frame", Image.Type.Sliced, raycast: true);
            _ppSearchGlyph = _art.Image("Glyph", _ppSearchBack.transform, "sprite.glyph.search", "color.text.body", Image.Type.Simple);
            _ppSearchGlyph.preserveAspect = true;
            _ppSearch = Field(_ppSearchBack, 24);
            _ppSearch.onValueChanged.AddListener(_ => { _ppScrollPx = 0f; PortalPickerService.Dirty = true; });
            _ppSearchHint = _art.Text("SearchHint", _ppSearchBack.transform, "font.body", "color.state.disabled", ThemeTokens.GetFloat("size.map.legend.font", 17f), TextAlignmentOptions.Left);
            _ppList = HudArtRect("List", _pp);
            _ppList.gameObject.AddComponent<RectMask2D>();
            _ppFavHeader = _art.Text("FavoritesHeader", _ppList, "font.display", "color.text.header", headerSize, TextAlignmentOptions.Center);
            _ppFavLine = _art.Image("FavoritesDivider", _ppList, "sprite.divider", "color.frame", Image.Type.Sliced);
            _ppAllHeader = _art.Text("AllHeader", _ppList, "font.display", "color.text.header", headerSize, TextAlignmentOptions.Center);
            _ppAllLine = _art.Image("AllDivider", _ppList, "sprite.divider", "color.frame", Image.Type.Sliced);
            _ppTrack = _art.Image("ScrollTrack", _pp, "sprite.bar.skill.trough", "color.frame", Image.Type.Sliced);
            _ppThumb = _art.Image("ScrollThumb", _ppTrack.transform, "sprite.bar.skill.fill", "color.text.accent", Image.Type.Sliced);
            foreach (var tip in _map.GetComponentsInChildren<UITooltip>(true))
                if (tip.m_tooltipPrefab) { _ppTooltip = tip.m_tooltipPrefab; break; }
            // Key hint at the panel bottom (mockup), not in the map's KeyHints band: vanilla hides that band with the game's
            // KeyHints setting off (Minimap.cs:1238-1242, HintBand() = 0; run 20261111-152231 showed no hint at all).
            _ppHint = _art.Text("Hint", _pp, "font.body", "color.text.body", ThemeTokens.GetFloat("size.map.biome.font", 15f), TextAlignmentOptions.Center);
            _ppHint.textWrappingMode = TextWrappingModes.Normal;
            _pp.gameObject.SetActive(false);
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
            // shortcut: no pencil glyph in the theme yet (art = Codex image gen); the name-sort glyph stands in.
            r.PencilGlyph = _art.Image("Glyph", r.Pencil.transform, "sprite.glyph.sort.name", "color.text.accent", Image.Type.Simple);
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

        private void OnPortalRowClick(PortalRowUi r, PointerEventData e)
        {
            if (r.Index < 0 || r.Index >= _ppRows.Count || _ppRows[r.Index].Here) return; // "вы здесь": not a destination
            var id = _ppRows[r.Index].Portal.Id;
            if (e.button == PointerEventData.InputButton.Right) PortalPickerService.Travel()?.ToggleFavorite(id);
            else if (e.button == PointerEventData.InputButton.Left) PortalClick(id);
        }

        // One click path for rows and pins.
        public void PortalClick(ZDOID id)
        {
            if (id == ZDOID.None) return;
            if (_ppClicks.IsDouble(id, Time.unscaledTime)) PortalTeleport(id);
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
            // Minimap.UpdateMap centres the large map on player position + m_mapOffset (Minimap.cs:1118-1165).
            // The list covers the map image's left part: shift by the world distance from the image centre to the centre of
            // the visible part (ScreenToWorldPoint is linear in screen space at the current zoom, Minimap.cs:2371).
            if (Player.m_localPlayer) _map.m_mapOffset = _ppRows[i].Portal.Position - Player.m_localPlayer.transform.position - VisibleCenterShift();
            KeepVisible(i);
        }

        // Screen centre of the visible map area: from the list's right edge to the map image's right edge.
        public Vector2 PortalVisibleCenter()
        {
            var map = _map.m_mapImageLarge.rectTransform;
            var cam = UiCamera(map);
            var m = new Vector3[4];
            map.GetWorldCorners(m);
            var bl = RectTransformUtility.WorldToScreenPoint(cam, m[0]);
            var tr = RectTransformUtility.WorldToScreenPoint(cam, m[2]);
            var left = bl.x;
            if (_pp && _pp.gameObject.activeInHierarchy)
            {
                var p = new Vector3[4];
                _pp.GetWorldCorners(p);
                left = Mathf.Max(left, RectTransformUtility.WorldToScreenPoint(cam, p[2]).x);
            }
            return PortalPickerMath.VisibleCenter(left, tr.x, bl.y, tr.y);
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

        // Per frame while the large map is open.
        private void PortalLateUpdate(float inset, float top, float band)
        {
            if (!_pp) return;
            var travel = PortalPickerService.Travel();
            var active = travel != null && travel.Active;
            if (active != _ppShown) PortalSession(active);
            if (!active) return;
            if (PortalPickerService.Dirty) PortalRebuild(travel);
            PortalEditorUpdate();
            if (!PortalEditorShown) PortalGamepad();
            PortalLayout(inset, top, band);
            PortalRingUpdate();
        }

        private void PortalSession(bool active)
        {
            _ppShown = active;
            _pp.gameObject.SetActive(active);
            if (_keyRow) _keyRow.gameObject.SetActive(!active);
            _ppSel = ZDOID.None;
            _ppRingPin = null;
            _ppScrollPx = 0f;
            ClosePortalEditor();
            if (_ppRing) _ppRing.gameObject.SetActive(false);
            Plugin.Log.LogInfo($"[Morgott.UI] portal picker {(active ? "open" : "closed")}: panel={_pp.gameObject.activeSelf} vanillaKeyRow={(_keyRow ? _keyRow.gameObject.activeSelf.ToString() : "none")}");
            if (!active) return;
            if (_ppSearch.text.Length > 0) _ppSearch.text = "";
            PortalPickerService.Dirty = true;
            var mouse = KeyLabels.Latin["$button_mouse0"];
            if (_ppHint) _ppHint.text = ZInput.IsGamepadActive()
                ? Localization.instance.Localize("$ui_portal_hint_pad")
                : Localization.instance.Localize("$ui_portal_hint", mouse, KeyLabels.Latin["$button_mouse1"], "Esc");
        }

        private void PortalRebuild(IPortalTravel travel)
        {
            PortalPickerService.Dirty = false;
            travel.GetPortals(_ppPortals);
            var shown = new List<PortalInfo>(_ppPortals.Count);
            foreach (var p in _ppPortals)
            {
                var c = p;
                c.Name = Words.Get(p.Name);
                shown.Add(c);
            }
            PortalInfo? here = null;
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
            _ppFav = PortalPickerMath.Build(shown, here, _ppSearch.text, _ppRows);
            _ppView.Clear();
            foreach (var row in _ppRows)
            {
                var biome = WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(row.Portal.Position) : Heightmap.Biome.None;
                var canEdit = travel.CanEdit(row.Portal.Id, out var denied);
                _ppView.Add(new PortalView
                {
                    CanEdit = canEdit,
                    Denied = canEdit ? Words.Get("$ui_portal_edit_tip") : denied,
                    Biome = Words.Get(MapFilterMath.NameKey(biome)),
                    BiomeColor = BiomeOverlay.BiomeColor(biome),
                    Distance = row.Here ? Words.Get("$ui_portal_here") : Localization.instance.Localize("$ui_portal_meters", PortalPickerMath.Meters(row.Distance)),
                });
            }
            if (_ppSel != ZDOID.None && PortalPickerMath.IndexOf(_ppRows, _ppSel) < 0) _ppSel = ZDOID.None; // filtered out / gone
            _ppRingVersion = -1;
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
        private float PortalSectionH => ThemeTokens.GetFloat("size.map.legend.header.h", 30f) + ThemeTokens.GetFloat("size.map.legend.divider", 11f) + 4f;

        // Content y (from the list top) of row i: section headers above it.
        private float PortalRowY(int i) => (_ppFav > 0 ? PortalSectionH : 0f) + (i >= _ppFav ? PortalSectionH : 0f) + i * PortalRowH;

        private void KeepVisible(int i)
        {
            if (!_ppList) return;
            var view = _ppList.rect.height;
            var y = PortalRowY(i);
            if (y < _ppScrollPx) _ppScrollPx = y;
            else if (y + PortalRowH > _ppScrollPx + view) _ppScrollPx = y + PortalRowH - view;
        }

        private void PortalLayout(float inset, float top, float band)
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

            var y = Section(_ppTitle, _ppTitleLine, "$ui_portal_title", pad, pad, rowW, headerH, divider);
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

            var hintH = ThemeTokens.GetFloat("size.map.portal.hint.h", 66f);
            Place(_ppHint.rectTransform, pad, h - pad - hintH, rowW, hintH);
            var view = Mathf.Max(PortalRowH, h - y - pad - hintH - 6f);
            Place(_ppList, pad, y, rowW, view);
            var rowH = PortalRowH;
            var content = PortalRowY(_ppRows.Count);
            if (_ppScroll && Mathf.Abs(_ppScroll.Delta) > 0.01f)
            {
                _ppScrollPx -= _ppScroll.Delta * rowH;
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

            var favShown = _ppFav > 0;
            if (_ppFavHeader.gameObject.activeSelf != favShown) { _ppFavHeader.gameObject.SetActive(favShown); _ppFavLine.gameObject.SetActive(favShown); }
            if (favShown) Section(_ppFavHeader, _ppFavLine, "$ui_portal_favorites", 0f, -_ppScrollPx, listW, headerH, divider);
            Section(_ppAllHeader, _ppAllLine, "$ui_portal_all", 0f, PortalRowY(_ppFav) - PortalSectionH - _ppScrollPx, listW, headerH, divider);

            while (_ppRowUi.Count < _ppRows.Count) _ppRowUi.Add(NewPortalRow());
            var selected = -1;
            for (var i = 0; i < _ppRowUi.Count; i++)
            {
                var r = _ppRowUi[i];
                var ry = i < _ppRows.Count ? PortalRowY(i) - _ppScrollPx : 0f;
                var on = i < _ppRows.Count && ry + rowH > 0f && ry < view;
                if (r.Root.gameObject.activeSelf != on) r.Root.gameObject.SetActive(on);
                r.Index = on ? i : -1;
                if (!on) continue;
                if (_ppRows[i].Portal.Id == _ppSel) selected = i;
                LayPortalRow(r, _ppRows[i], _ppView[i], ry, listW, rowH);
            }

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
            // Style icon slot (Core IPortalIcons via PortalIcons; none = empty slot, mockup_window).
            var iconSize = rowH * 0.78f;
            var icon = PortalIcons.Instance.Get(row.Portal.Icon);
            if (r.Icon.sprite != icon) r.Icon.sprite = icon;
            if (r.Icon.enabled != (bool)icon) r.Icon.enabled = icon;
            Place(r.Icon.rectTransform, 6f + star, (rowH - iconSize) * 0.5f, iconSize, iconSize);
            var x = 10f + star + iconSize;
            // Pencil at the right edge: dimmed when the ward rule denies editing (click then shows why).
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
            // Stone-portal mark right after the name, centred on the name line.
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
            // Name in its style colour (Core PortalInfo.Color), the body colour by default; "you are here" stays dimmed.
            r.Name.color = row.Here ? dim : row.Portal.CustomColor ? row.Portal.Color : dim;
        }
        // Ring on the selected portal's pin marker (a child: follows pan / zoom). The pin is looked up again when the
        // selection or the pin markers change (UpdatePins rebuilds markers, MinimapAdapter bumps PinsVersion).
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
            if (rt.parent != marker)
            {
                rt.SetParent(marker, false);
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
            }
            var size = marker.sizeDelta * ThemeTokens.GetFloat("ratio.map.portal.ring", 2.2f);
            if (rt.sizeDelta != size) rt.sizeDelta = size;
            if (!_ppRing.gameObject.activeSelf) _ppRing.gameObject.SetActive(true);
        }
    }

    // Map clicks and pin edits during a travel session (Minimap.cs:2395-2507, gamepad :944-1003): a click on a portal pin
    // goes through the picker's click path, pin editing / pings are off (mirrors Morgott.Patcher's prefixes before 0.5.8),
    // and the map keys stay off while the list's text fields have focus.
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

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.InTextInput)), HarmonyPostfix]
        private static void Typing(ref bool __result) => __result |= MinimapSkin.PortalTyping;
    }
}
