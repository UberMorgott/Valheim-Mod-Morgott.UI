using System.Collections.Generic;
using Morgott.UI.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Morgott.UI.Compat;

namespace Morgott.UI.MapScreen
{
    // Large map side column = one filter list (mockup docs\mockups\2026-11-10\biome-overlay.png, design
    // docs\biome-overlay-design.md): МЕТКИ - one row per pin type group (MapFilterMath.Rows: recessed socket with the
    // vanilla icon button moved there when the type has one (IconPanel Icon0..4, IconPanel2 Death / Boss, IconPingPanel;
    // its onClick / right-click wiring and "Selected" frame untouched), else the type's own map icon; localized name;
    // checkbox = [Map Filters] entry -> Minimap.m_visibleIconTypes), then the buttons of mod filters (Epic Loot's
    // AdventurePinFilter*: the checkbox clicks the button and reads its grey tint, the vanilla filtered look,
    // Minimap.cs:2632); scrolled by the mouse wheel when the rows do not fit. ИГРОКИ - ZNet.GetPlayerList with a per-session
    // checkbox (hides that player's marker on the large map). СЛОИ - "Подсветка биомов" ([Map] BiomeOverlay) and the biome
    // colour key. Then the Public toggle panel and the zoom row pinned to the bottom (minus | slider on Minimap.LargeZoom |
    // plus). A key hint line runs under the map when the game's KeyHints setting is on (Minimap.SetMapMode, :1237). Buttons
    // stay children of their vanilla panels (mods find them by path); only their LayoutGroups are off and their rects are
    // placed each frame.
    public sealed partial class MinimapSkin
    {
        private sealed class PinRow
        {
            public RectTransform Root;
            public Image Socket, Glyph, Box, Check;
            public TMP_Text Name;
            public Button Toggle;
            public Button Icon;
            public FilterRow Row;
        }

        private sealed class Entry
        {
            public FilterRow Row;
            public Button Icon;
        }

        // Mouse wheel over the side column scrolls the pin rows (and never zooms the map: ZInput scroll muted there,
        // MapLegendInput).
        private sealed class LegendScroll : MonoBehaviour, IScrollHandler, IPointerEnterHandler, IPointerExitHandler
        {
            public float Delta;
            public bool Over;
            public Vector2 At;
            public Camera Cam;
            public void OnScroll(PointerEventData e)
            {
                Delta += e.scrollDelta.y;
                At = e.position;
                Cam = e.enterEventCamera;
            }
            public void OnPointerEnter(PointerEventData e) => Over = true;
            public void OnPointerExit(PointerEventData e) => Over = false;
            private void OnDisable() => Over = false;
        }

        internal static bool LegendHover => Instance && Instance._scroll && Instance._scroll.Over && Instance._large && Instance._large.gameObject.activeInHierarchy;

        private RectTransform _rows, _footer;
        private readonly List<PinRow> _pinRows = new List<PinRow>();
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly List<PinRow> _playerRows = new List<PinRow>();
        private readonly List<Button> _iconButtons = new List<Button>();
        private readonly HashSet<string> _hiddenPlayers = new HashSet<string>();
        private TMP_Text _pinsHeader, _playersHeader, _layersHeader, _zoomLabel;
        private Image _pinsDivider, _playersDivider, _layersDivider, _scrollTrack, _scrollThumb;
        private PinRow _biomeRow;
        private readonly List<(Image Swatch, TMP_Text Name)> _key = new List<(Image, TMP_Text)>();
        private LegendScroll _scroll;
        private int _firstRow, _firstPlayer;
        private float _playersTop, _playersBottom;
        private Image _playerTrack, _playerThumb;
        private Slider _zoom;
        private bool _zoomWrite;
        private int _loggedButtons = -1;
        private Dictionary<GameObject, Minimap.PinType> _types;

        // Harness: МЕТКИ rows in list order (FilterRow key or the mod button name), first shown row, rows in the window.
        public IReadOnlyList<string> FilterKeys
        {
            get
            {
                var keys = new List<string>();
                foreach (var e in _entries) keys.Add(e.Row != null ? e.Row.Key : e.Icon ? e.Icon.name : "?");
                return keys;
            }
        }

        public int FirstFilterRow => _firstRow;

        private void BuildLegendRows()
        {
            _rows = HudArtRect("RuneSkin_LegendRows", _legend);
            _rows.SetAsFirstSibling(); // behind the vanilla pin panels: their icon buttons draw over the row sockets
            Top(_rows);
            _rows.offsetMin = _rows.offsetMax = Vector2.zero;
            _rows.anchorMin = Vector2.zero;
            _rows.anchorMax = Vector2.one;
            _scroll = _legend.gameObject.AddComponent<LegendScroll>();
            var headerSize = ThemeTokens.GetFloat("size.map.legend.header", 21f);
            _pinsHeader = _art.Text("PinsHeader", _rows, "font.display", "color.text.header", headerSize, TextAlignmentOptions.Center);
            _pinsDivider = _art.Image("PinsDivider", _rows, "sprite.divider", "color.frame", Image.Type.Sliced);
            _playersHeader = _art.Text("PlayersHeader", _rows, "font.display", "color.text.header", headerSize, TextAlignmentOptions.Center);
            _playersDivider = _art.Image("PlayersDivider", _rows, "sprite.divider", "color.frame", Image.Type.Sliced);
            _layersHeader = _art.Text("LayersHeader", _rows, "font.display", "color.text.header", headerSize, TextAlignmentOptions.Center);
            _layersDivider = _art.Image("LayersDivider", _rows, "sprite.divider", "color.frame", Image.Type.Sliced);
            _scrollTrack = _art.Image("ScrollTrack", _rows, "sprite.bar.skill.trough", "color.frame", Image.Type.Sliced);
            _scrollThumb = _art.Image("ScrollThumb", _scrollTrack.transform, "sprite.bar.skill.fill", "color.text.accent", Image.Type.Sliced);
            _playerTrack = _art.Image("PlayerScrollTrack", _rows, "sprite.bar.skill.trough", "color.frame", Image.Type.Sliced);
            _playerThumb = _art.Image("ScrollThumb", _playerTrack.transform, "sprite.bar.skill.fill", "color.text.accent", Image.Type.Sliced);
            _biomeRow = NewRow("LayerRow");
            _biomeRow.Glyph.sprite = MapIcons.Layers;
            _biomeRow.Glyph.enabled = _biomeRow.Glyph.sprite;
            foreach (var biome in MapFilterMath.Biomes)
            {
                var swatch = _art.Image("BiomeSwatch", _rows, null, "color.frame", Image.Type.Simple);
                swatch.color = BiomeOverlay.BiomeColor(biome);
                var name = _art.Text("BiomeName", _rows, "font.body", "color.text.body", ThemeTokens.GetFloat("size.map.biome.font", 15f), TextAlignmentOptions.Left);
                name.textWrappingMode = TextWrappingModes.NoWrap;
                name.overflowMode = TextOverflowModes.Ellipsis;
                _key.Add((swatch, name));
            }
            _zoomLabel = _art.Text("ZoomLabel", _rows, "font.body", "color.text.body", ThemeTokens.GetFloat("size.map.legend.font", 17f), TextAlignmentOptions.Left);
            BuildZoom();
            BuildHints(); // the vanilla key hint row, moved into the band under the map (MapHints.cs)
            // Vanilla panel hint graphics (mouse glyphs next to the icons) give way to the hint row under the map.
            foreach (var panel in LegendPanels)
            {
                var t = _legend.Find(panel);
                if (!t) continue;
                foreach (var group in t.GetComponents<LayoutGroup>()) group.enabled = false;
                if (t.TryGetComponent<ContentSizeFitter>(out var fitter)) fitter.enabled = false;
                for (var i = 0; i < t.childCount; i++)
                {
                    var c = t.GetChild(i);
                    if (c.GetComponentInChildren<Button>(true) || c.name == "Bkg") continue;
                    if (!c.TryGetComponent<CanvasGroup>(out var g)) g = c.gameObject.AddComponent<CanvasGroup>();
                    g.alpha = 0f;
                    g.blocksRaycasts = false;
                    Morgott.UI.Restyle.RenderGate.Set(c, "maplegend", true); // not drawn
                    _art.Suppressed(c, "vanilla legend hint graphic (RuneSkin key hint line under the map replaces it)");
                }
            }
        }

        private void BuildZoom()
        {
            var root = HudArtRect("Zoom", _rows);
            _zoom = root.gameObject.AddComponent<Slider>();
            var track = _art.Image("Track", root, "sprite.bar.skill.trough", "color.frame", Image.Type.Sliced, raycast: true);
            Stretch(track.rectTransform);
            var fillArea = HudArtRect("FillArea", root);
            Stretch(fillArea);
            var fill = _art.Image("Fill", fillArea, "sprite.bar.skill.fill", "color.text.accent", Image.Type.Sliced);
            Stretch(fill.rectTransform);
            var handleArea = HudArtRect("HandleArea", root);
            Stretch(handleArea);
            var handle = _art.Image("Handle", handleArea, "sprite.slider.handle.normal", "color.frame", Image.Type.Simple, raycast: true);
            handle.preserveAspect = true;
            handle.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            _zoom.fillRect = fill.rectTransform;
            _zoom.handleRect = handle.rectTransform;
            _zoom.targetGraphic = handle;
            _zoom.direction = Slider.Direction.LeftToRight;
            _zoom.minValue = 0f;
            _zoom.maxValue = 1f;
            var hover = ThemeTokens.GetSprite("sprite.slider.handle.hover");
            if (hover)
            {
                _zoom.transition = Selectable.Transition.SpriteSwap;
                _zoom.spriteState = new SpriteState { highlightedSprite = hover, pressedSprite = hover, selectedSprite = handle.sprite, disabledSprite = ThemeTokens.GetSprite("sprite.slider.handle.disabled") };
            }
            _zoom.onValueChanged.AddListener(v =>
            {
                if (_zoomWrite || !_map) return;
                _map.LargeZoom = MinimapMath.SliderToZoom(v, _map.m_minZoom, _map.m_maxZoom);
            });
            ZoomButton("Minus", "sprite.glyph.minus", -1);
            ZoomButton("Plus", "sprite.glyph.plus", +1);
        }

        private void ZoomButton(string name, string glyph, int dir)
        {
            var b = _art.Image("Zoom" + name, _rows, "sprite.button.round", "color.frame", Image.Type.Simple, raycast: true);
            b.preserveAspect = true;
            var g = _art.Image("Glyph", b.transform, glyph, "color.text.body", Image.Type.Simple);
            g.preserveAspect = true;
            Stretch(g.rectTransform);
            var inset = ThemeTokens.GetFloat("size.map.zoom.glyph.inset", 7f);
            g.rectTransform.offsetMin = new Vector2(inset, inset);
            g.rectTransform.offsetMax = new Vector2(-inset, -inset);
            var button = b.gameObject.AddComponent<Button>();
            button.targetGraphic = b;
            button.onClick.AddListener(() =>
            {
                if (!_zoom) return;
                _zoom.value = Mathf.Clamp01(_zoom.value + dir * ThemeTokens.GetFloat("ratio.map.zoom.step", 0.125f));
            });
        }

        private static void Top(RectTransform rt)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
        }

        // Button GameObject -> pin type: the Selected frame images of Minimap.m_selectedIcons sit under their icon button
        // (ToggleIconFilter tints selectedIcon.transform.parent, Minimap.cs:2632); the ping frame likewise.
        private Dictionary<GameObject, Minimap.PinType> Types()
        {
            if (_types != null) return _types;
            _types = new Dictionary<GameObject, Minimap.PinType>();
            foreach (var pair in _map.m_selectedIcons)
                if (pair.Value && pair.Value.transform.parent) _types[pair.Value.transform.parent.gameObject] = pair.Key;
            if (_map.m_selectedIconPing && _map.m_selectedIconPing.transform.parent) _types[_map.m_selectedIconPing.transform.parent.gameObject] = Minimap.PinType.Ping;
            return _types;
        }

        // ---------------------------------------------------------------- filters <-> config

        // Config -> m_visibleIconTypes (map start, SettingChanged): vanilla's own write + tint (ToggleIconFilter,
        // Minimap.cs:2625-2634, without its gamepad rumble).
        internal void ApplyFilters()
        {
            if (!_map || _map.m_visibleIconTypes == null) return;
            HookFilters();
            var changed = false;
            foreach (var row in MapFilterMath.Rows)
                changed |= MapFilterMath.Apply(_map.m_visibleIconTypes, row, MapConfig.Shown(row));
            if (!changed) return;
            _map.m_pinUpdateRequired = true;
            foreach (var pair in _map.m_selectedIcons)
                if (pair.Value && pair.Value.transform.parent && pair.Value.transform.parent.TryGetComponent<Image>(out var img))
                    img.color = (int)pair.Key < _map.m_visibleIconTypes.Length && _map.m_visibleIconTypes[(int)pair.Key] ? Color.white : Color.gray;
        }

        private static bool _filtersHooked;

        private static void HookFilters()
        {
            if (_filtersHooked) return;
            _filtersHooked = true;
            foreach (var e in MapConfig.Filters.Values) e.SettingChanged += (_, __) => Instance?.ApplyFilters();
        }

        // Vanilla toggled one type (AddPin of a new pin re-shows a hidden type, Minimap.cs:2346): its row's entry follows.
        // Not while saved pins load (SetMapData -> AddPin, Minimap.cs:2158): the saved filters win there and are applied
        // again after the load (MapFilterInput).
        internal void OnVanillaToggle(Minimap.PinType type)
        {
            if (!_map || MapFilterInput.LoadingPins) return;
            var visible = _map.m_visibleIconTypes;
            foreach (var row in MapFilterMath.Rows)
            {
                if (System.Array.IndexOf(row.Types, type) < 0 || !MapConfig.Filters.TryGetValue(row.Key, out var entry)) continue;
                var shown = MapFilterMath.Shown(visible, row);
                if (entry.Value != shown) entry.Value = shown; // SettingChanged -> ApplyFilters: the whole group follows
            }
        }

        // ---------------------------------------------------------------- mod filter names

        private static readonly List<Button> ButtonScratch = new List<Button>(16);
        // Mod button labels per button (reflection over Epic Loot fields, name string work): computed once per button
        // and language, not per frame (perf audit B2: the open map allocated ~2 KB per frame).
        // Re-read every RefreshFrames (a mod may set its tooltip / filter fields after the map first opened).
        private readonly Dictionary<Button, KeyValuePair<string, int>> _modNames = new Dictionary<Button, KeyValuePair<string, int>>();
        private const int RefreshFrames = 120;

        private string CachedModName(Button b)
        {
            if (_modNames.TryGetValue(b, out var cached) && Time.frameCount - cached.Value < RefreshFrames) return cached.Key;
            if (_modNames.Count >= 128) _modNames.Clear(); // buttons are rebuilt by their mods now and then
            var label = ModName(b);
            _modNames[b] = new KeyValuePair<string, int>(label, Time.frameCount);
            return label;
        }

        // Mod filter button: its own tooltip text, else its object name without the usual prefixes.
        private static string ModName(Button b)
        {
            var tip = b.GetComponent<UITooltip>();
            if (tip && !string.IsNullOrEmpty(tip.m_text)) return Words.Get(tip.m_text);
            var known = EpicLootFilterName(b);
            if (known != null) return known;
            if (tip && !string.IsNullOrEmpty(tip.m_topic)) return Words.Get(tip.m_topic);
            var n = b.name.Replace("(Clone)", "").Replace("PinFilter", " ").Replace("Filter", " ").Trim('_', ' ');
            // Words of a CamelCase object name ("AdventurePinFilter800" -> "Adventure 800").
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < n.Length; i++)
            {
                if (i > 0 && (char.IsUpper(n[i]) && char.IsLower(n[i - 1]) || char.IsDigit(n[i]) && char.IsLetter(n[i - 1])) && n[i - 1] != ' ') sb.Append(' ');
                sb.Append(n[i]);
            }
            var s = sb.ToString().Trim();
            return string.IsNullOrEmpty(s) ? Words.Get("$ui_pin_mod") : s;
        }

        // Epic Loot's adventure pin filters (static fields _bountyPinFilter / _treasurePinFilter, its own pin names
        // $mod_epicloot_merchant_bounties / $mod_epicloot_merchant_treasuremaps): untitled buttons, named here
        // with Epic Loot's own localized section names (its pin-name keys are "$1" formats) (Codex M4 r3: generic "mod pins" labels).
        private static System.Collections.Generic.List<(System.Reflection.FieldInfo Field, string Key)> _elFilters;

        private static string EpicLootFilterName(Button b)
        {
            if (_elFilters == null)
            {
                _elFilters = new System.Collections.Generic.List<(System.Reflection.FieldInfo, string)>();
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name != ModCompat.EpicLootAssembly) continue;
                    System.Type[] types;
                    try { types = asm.GetTypes(); }
                    catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types; }
                    foreach (var t in types)
                    {
                        if (t == null) continue;
                        const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                        var bounty = t.GetField("_bountyPinFilter", F);
                        var treasure = t.GetField("_treasurePinFilter", F);
                        if (bounty != null) _elFilters.Add((bounty, "$mod_epicloot_merchant_bounties"));
                        if (treasure != null) _elFilters.Add((treasure, "$mod_epicloot_merchant_treasuremaps"));
                    }
                }
                Plugin.Log.LogInfo($"[Morgott.UI] map legend: Epic Loot pin filter fields {_elFilters.Count} [{string.Join(",", _elFilters.ConvertAll(f => f.Field.DeclaringType?.Name + "." + f.Field.Name + (f.Field.IsStatic ? " static" : "")))}]");
            }
            foreach (var (field, key) in _elFilters)
            {
                object v = null;
                try
                {
                    if (field.IsStatic) v = field.GetValue(null);
                    else if (typeof(Component).IsAssignableFrom(field.DeclaringType))
                        foreach (var owner in UnityEngine.Object.FindObjectsByType(field.DeclaringType, FindObjectsInactive.Include, FindObjectsSortMode.None))
                            if ((v = field.GetValue(owner)) != null) break;
                }
                catch (System.Exception) { continue; }
                var go = v is GameObject g ? g : (v as Component) ? ((Component)v).gameObject : null;
                var hit = go && (go == b.gameObject || b.transform.IsChildOf(go.transform) || go.transform.IsChildOf(b.transform));
                // The field holds Epic Loot's AdventurePinFilter object (run 20261022-071214), which keeps its button /
                // GameObject in its own fields.
                if (!hit && v != null && !go)
                    foreach (var f in v.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
                    {
                        object inner;
                        try { inner = f.GetValue(v); }
                        catch (System.Exception) { continue; }
                        var ig = inner is GameObject ig0 ? ig0 : (inner as Component) ? ((Component)inner).gameObject : null;
                        if (ig && ig == b.gameObject) { hit = true; go = ig; break; } // exact: both filters share parent refs (run 20261022-072233 named both rows "bounties")
                    }
                if (!hit) continue;
                var label = Localization.instance != null ? Localization.instance.Localize(key) : key;
                return string.IsNullOrEmpty(label) || label.StartsWith("[", System.StringComparison.Ordinal) ? null : label;
            }
            return null;
        }

        // ---------------------------------------------------------------- rows

        private PinRow NewRow(string name = "PinRow")
        {
            var r = new PinRow { Root = HudArtRect(name, _rows) };
            Top(r.Root);
            r.Socket = _art.Image("Socket", r.Root, "sprite.slot.recess", "color.frame", Image.Type.Sliced);
            r.Glyph = _art.Image("Glyph", r.Socket.transform, null, "color.text.body", Image.Type.Simple);
            r.Glyph.preserveAspect = true;
            r.Glyph.color = Color.white;
            r.Name = _art.Text("Name", r.Root, "font.body", "color.text.body", ThemeTokens.GetFloat("size.map.legend.font", 17f), TextAlignmentOptions.Left);
            r.Name.textWrappingMode = TextWrappingModes.NoWrap;
            r.Name.overflowMode = TextOverflowModes.Ellipsis;
            r.Box = _art.Image("Check", r.Root, "sprite.checkbox.box", "color.frame", Image.Type.Sliced, raycast: true);
            r.Check = _art.Image("Mark", r.Box.transform, "sprite.glyph.check", "color.text.accent", Image.Type.Simple);
            r.Check.preserveAspect = true;
            Stretch(r.Check.rectTransform);
            r.Toggle = r.Box.gameObject.AddComponent<Button>();
            r.Toggle.targetGraphic = r.Box;
            r.Toggle.onClick.AddListener(() => OnToggle(r));
            return r;
        }

        private void OnToggle(PinRow r)
        {
            if (r == _biomeRow)
            {
                if (MapConfig.BiomeOverlay != null) MapConfig.BiomeOverlay.Value = !MapConfig.BiomeOverlay.Value;
                return;
            }
            if (_playerRows.Contains(r))
            {
                var key = r.Name.text;
                if (!_hiddenPlayers.Remove(key)) _hiddenPlayers.Add(key);
                return;
            }
            if (r.Row != null)
            {
                var on = !MapFilterMath.Shown(_map.m_visibleIconTypes, r.Row);
                if (MapConfig.Filters.TryGetValue(r.Row.Key, out var entry)) entry.Value = on; // SettingChanged -> ApplyFilters
                else if (MapFilterMath.Apply(_map.m_visibleIconTypes, r.Row, on)) _map.m_pinUpdateRequired = true;
                ApplyFilters();
                return;
            }
            if (!r.Icon) return;
            // A typed mod filter (in m_selectedIcons) toggles like a vanilla one, else its own button handler does.
            if (Types().TryGetValue(r.Icon.gameObject, out var type) && (int)type < _map.m_visibleIconTypes.Length) _map.ToggleIconFilter(type);
            else r.Icon.onClick.Invoke();
        }

        private bool Visible(PinRow r)
        {
            if (r.Row != null) return MapFilterMath.Shown(_map.m_visibleIconTypes, r.Row);
            if (r.Icon && Types().TryGetValue(r.Icon.gameObject, out var type) && (int)type < _map.m_visibleIconTypes.Length) return _map.m_visibleIconTypes[(int)type];
            var c = r.Icon && r.Icon.targetGraphic ? r.Icon.targetGraphic.color : Color.white;
            return c.grayscale > 0.75f;
        }

        private static bool Covered(Minimap.PinType type)
        {
            foreach (var row in MapFilterMath.Rows)
                if (System.Array.IndexOf(row.Types, type) >= 0) return true;
            return false;
        }

        // The rows of this frame: every present filter group (with its vanilla button when it has one), then mod buttons.
        private void CollectEntries()
        {
            _iconButtons.Clear();
            foreach (var panel in LegendPanels)
            {
                var t = _legend.Find(panel);
                if (!t || !t.gameObject.activeSelf) continue;
                t.GetComponentsInChildren(false, ButtonScratch);
                _iconButtons.AddRange(ButtonScratch);
                ButtonScratch.Clear();
            }
            var types = Types();
            var visible = _map.m_visibleIconTypes;
            var n = 0;
            Entry Next()
            {
                if (n == _entries.Count) _entries.Add(new Entry());
                return _entries[n++];
            }
            foreach (var row in MapFilterMath.Rows)
            {
                if (!MapFilterMath.Present(visible, row)) continue;
                Button icon = null;
                foreach (var b in _iconButtons)
                    if (types.TryGetValue(b.gameObject, out var ty) && System.Array.IndexOf(row.Types, ty) >= 0) { icon = b; break; }
                if (!icon && !_map.GetSprite(row.Types[0])) continue; // nothing to show it with
                var e = Next();
                e.Row = row;
                e.Icon = icon;
            }
            // Mod filter buttons: untyped ones and those whose type has no row (Epic Loot adds its bounty / treasure types
            // to m_selectedIcons, run 20261110-155326: AdventurePinFilter101 / 102).
            foreach (var b in _iconButtons)
            {
                if (types.TryGetValue(b.gameObject, out var ty) && Covered(ty)) continue;
                var e = Next();
                e.Row = null;
                e.Icon = b;
            }
            _entries.RemoveRange(n, _entries.Count - n);
            if (_iconButtons.Count != _loggedButtons)
            {
                _loggedButtons = _iconButtons.Count;
                Plugin.Log.LogInfo($"[Morgott.UI] map filter rows: {string.Join(", ", FilterKeys)}");
            }
        }

        // Per frame while the large map is open: the whole column, top to bottom; the pin rows take what the fixed
        // sections leave and scroll when they do not fit.
        private void LegendRows(float inset)
        {
            if (!_rows) return;
            CollectEntries();
            var w = _legend.rect.width;
            var h = _legend.rect.height;
            var pad = ThemeTokens.GetFloat("size.map.legend.pad", 16f);
            var headerH = ThemeTokens.GetFloat("size.map.legend.header.h", 30f);
            var divider = ThemeTokens.GetFloat("size.map.legend.divider", 11f);
            var labelH = ThemeTokens.GetFloat("size.map.legend.label.h", 24f);
            var zoomH = ThemeTokens.GetFloat("size.map.zoom.h", 34f);
            var playerRow = ThemeTokens.GetFloat("size.map.player.row", 26f);
            var keyRow = ThemeTokens.GetFloat("size.map.biome.row", 22f);
            var rowPref = ThemeTokens.GetFloat("size.map.legend.row", 40f);
            var rowMin = ThemeTokens.GetFloat("size.map.legend.row.min", 28f);
            var section = headerH + divider + 4f;
            var rowW = w - 2f * pad;

            // Fixed blocks, bottom up: zoom, Public toggle, СЛОИ (layer row + biome key), ИГРОКИ.
            var zoomTop = h - pad - zoomH;
            var toggles = 0f;
            foreach (var panel in TogglePanels)
            {
                var t = _legend.Find(panel) as RectTransform;
                if (t && t.gameObject.activeSelf) toggles += t.rect.height + 6f;
            }
            var barW2 = ThemeTokens.GetFloat("size.map.legend.scroll", 8f);
            var keyRows = (MapFilterMath.Biomes.Length + 1) / 2;
            var layersH = section + playerRow + keyRows * keyRow + 6f;
            var players = ZNet.instance ? ZNet.instance.GetPlayerList() : null;
            var playerCount = players?.Count ?? 0;
            var playersShown = Mathf.Min(playerCount, Mathf.RoundToInt(ThemeTokens.GetFloat("count.map.players.max", 4f)));
            var playersH = section + playersShown * playerRow + 6f;
            var listTop = pad + section;
            var listBottom = zoomTop - labelH - toggles - layersH - playersH - 6f;

            // МЕТКИ.
            Place(_pinsHeader.rectTransform, pad, pad, rowW, headerH);
            HeaderFace(_pinsHeader);
            _pinsHeader.text = Words.Get("$ui_map_pins");
            Place(_pinsDivider.rectTransform, pad, pad + headerH, rowW, divider);
            var avail = Mathf.Max(rowMin, listBottom - listTop);
            var rowH = MinimapMath.RowHeight(avail, _entries.Count, rowPref, rowMin);
            var window = MapFilterMath.Window(avail, rowH);
            var scrolling = _entries.Count > window;
            if (_scroll && Mathf.Abs(_scroll.Delta) > 0.01f)
            {
                // Over the ИГРОКИ block (last frame's place) the wheel scrolls the players, elsewhere the pin rows.
                var step = Mathf.RoundToInt(Mathf.Sign(_scroll.Delta) * Mathf.Max(1f, Mathf.Abs(_scroll.Delta)));
                var overPlayers = RectTransformUtility.ScreenPointToLocalPointInRectangle(_legend, _scroll.At, _scroll.Cam, out var lp)
                    && _legend.rect.yMax - lp.y >= _playersTop && _legend.rect.yMax - lp.y < _playersBottom;
                if (overPlayers) _firstPlayer -= step;
                else _firstRow -= step;
                _scroll.Delta = 0f;
            }
            _firstRow = MapFilterMath.ClampScroll(_firstRow, _entries.Count, window);
            var barW = scrolling ? ThemeTokens.GetFloat("size.map.legend.scroll", 8f) : 0f;
            var listW = rowW - (scrolling ? barW + 6f : 0f);
            while (_pinRows.Count < _entries.Count) _pinRows.Add(NewRow());
            var k = _legend.lossyScale.x;
            var y = listTop;
            for (var i = 0; i < _pinRows.Count; i++)
            {
                var r = _pinRows[i];
                var has = i < _entries.Count;
                var shown = has && i >= _firstRow && i < _firstRow + window;
                if (r.Root.gameObject.activeSelf != shown) r.Root.gameObject.SetActive(shown);
                var e = has ? _entries[i] : null;
                if (r.Icon && (!has || r.Icon != e.Icon)) ShowButton(r.Icon, true); // the button moved to another row / left
                r.Row = e?.Row;
                r.Icon = e?.Icon;
                if (!has) continue;
                if (r.Icon) ShowButton(r.Icon, shown);
                if (!shown) continue;
                LayRow(r, pad, y, listW, rowH, k);
                var label = r.Row != null ? Words.Get(r.Row.NameToken) : CachedModName(r.Icon);
                if (r.Name.text != label) r.Name.text = label;
                // Own icon for a group without a vanilla button (bed, events, Hildir, memorial, shout, portals).
                var sprite = r.Icon ? null : _map.GetSprite(r.Row.Types[0]);
                if (r.Glyph.sprite != sprite) r.Glyph.sprite = sprite;
                if (r.Glyph.enabled != (bool)sprite) r.Glyph.enabled = sprite;
                var visible = Visible(r);
                if (r.Check.enabled != visible) r.Check.enabled = visible;
                r.Name.color = ThemeTokens.GetColor(visible ? "color.text.body" : "color.state.disabled", Color.white);
                y += rowH;
            }
            if (_scrollTrack.gameObject.activeSelf != scrolling) _scrollTrack.gameObject.SetActive(scrolling);
            if (scrolling)
            {
                var trackH = window * rowH;
                Place(_scrollTrack.rectTransform, pad + rowW - barW, listTop, barW, trackH);
                var thumb = _scrollThumb.rectTransform;
                thumb.anchorMin = new Vector2(0f, 1f - (float)(_firstRow + window) / _entries.Count);
                thumb.anchorMax = new Vector2(1f, 1f - (float)_firstRow / _entries.Count);
                thumb.offsetMin = thumb.offsetMax = Vector2.zero;
            }

            // ИГРОКИ.
            y = listTop + window * rowH + 6f;
            y = Section(_playersHeader, _playersDivider, "$ui_map_players", pad, y, rowW, headerH, divider);
            var localName = Game.instance && Game.instance.GetPlayerProfile() != null ? Game.instance.GetPlayerProfile().GetName() : null;
            _playersTop = y;
            _firstPlayer = MapFilterMath.ClampScroll(_firstPlayer, playerCount, playersShown);
            var playerScroll = playerCount > playersShown;
            while (_playerRows.Count < playersShown) _playerRows.Add(NewRow("PlayerRow"));
            if (_playerTrack.gameObject.activeSelf != playerScroll) _playerTrack.gameObject.SetActive(playerScroll);
            if (playerScroll)
            {
                Place(_playerTrack.rectTransform, pad + rowW - barW2, y, barW2, playersShown * playerRow);
                var thumb = _playerThumb.rectTransform;
                thumb.anchorMin = new Vector2(0f, 1f - (float)(_firstPlayer + playersShown) / playerCount);
                thumb.anchorMax = new Vector2(1f, 1f - (float)_firstPlayer / playerCount);
                thumb.offsetMin = thumb.offsetMax = Vector2.zero;
            }
            for (var i = 0; i < _playerRows.Count; i++)
            {
                var r = _playerRows[i];
                var on = i < playersShown;
                if (r.Root.gameObject.activeSelf != on) r.Root.gameObject.SetActive(on);
                if (!on) continue;
                var p = players[_firstPlayer + i];
                var me = p.m_name == localName;
                LayRow(r, pad, y, playerScroll ? rowW - barW2 - 6f : rowW, playerRow, k);
                var sprite = me && _map.m_largeMarker && _map.m_largeMarker.TryGetComponent<Image>(out var mk) ? mk.sprite : _map.GetSprite(Minimap.PinType.Player);
                if (r.Glyph.sprite != sprite) { r.Glyph.sprite = sprite; r.Glyph.enabled = sprite; }
                if (r.Name.text != p.m_name) r.Name.text = p.m_name;
                var hidden = _hiddenPlayers.Contains(p.m_name);
                if (r.Check.enabled == hidden) r.Check.enabled = !hidden;
                r.Name.color = ThemeTokens.GetColor(!hidden && (me || p.m_publicPosition) ? "color.text.body" : "color.state.disabled", Color.white);
                y += playerRow;
            }

            _playersBottom = y;

            // СЛОИ: biome highlight + its colour key (two columns).
            y += 6f;
            y = Section(_layersHeader, _layersDivider, "$ui_map_layers", pad, y, rowW, headerH, divider);
            LayRow(_biomeRow, pad, y, rowW, playerRow, k);
            _biomeRow.Name.text = Words.Get("$ui_map_biomes");
            var overlay = MapConfig.BiomeOverlay?.Value ?? true;
            if (_biomeRow.Check.enabled != overlay) _biomeRow.Check.enabled = overlay;
            _biomeRow.Name.color = ThemeTokens.GetColor(overlay ? "color.text.body" : "color.state.disabled", Color.white);
            y += playerRow;
            var colW = rowW * 0.5f;
            var sw = keyRow * 0.6f;
            for (var i = 0; i < _key.Count; i++)
            {
                var (swatch, name) = _key[i];
                var x = pad + (i % 2) * colW + 6f;
                var ky = y + (i / 2) * keyRow;
                Place(swatch.rectTransform, x, ky + (keyRow - sw) * 0.5f, sw, sw);
                swatch.color = BiomeOverlay.BiomeColor(MapFilterMath.Biomes[i]);
                Place(name.rectTransform, x + sw + 6f, ky, colW - sw - 18f, keyRow);
                var label = Words.Get(MapFilterMath.NameKey(MapFilterMath.Biomes[i]));
                if (name.text != label) name.text = label;
                name.color = ThemeTokens.GetColor(overlay ? "color.text.body" : "color.state.disabled", Color.white);
            }
            y += keyRows * keyRow + 6f;

            // Public toggle panel (vanilla toggle + label).
            foreach (var panel in TogglePanels)
            {
                var t = _legend.Find(panel) as RectTransform;
                if (!t || !t.gameObject.activeSelf) continue;
                MinimapSkin.Place(t, new Vector2(0f, -y));
                y += t.rect.height + 6f;
            }

            // Zoom row pinned to the bottom: label above, minus | slider | plus.
            Place(_zoomLabel.rectTransform, pad, zoomTop - labelH, rowW, labelH);
            _zoomLabel.text = Words.Get("$ui_map_zoom");
            var minus = _rows.Find("ZoomMinus") as RectTransform;
            var plus = _rows.Find("ZoomPlus") as RectTransform;
            if (minus) Place(minus, pad, zoomTop, zoomH, zoomH);
            if (plus) Place(plus, w - pad - zoomH, zoomTop, zoomH, zoomH);
            var trackH2 = ThemeTokens.GetFloat("size.map.zoom.track", 12f);
            var zr = (RectTransform)_zoom.transform;
            Place(zr, pad + zoomH + 10f, zoomTop + (zoomH - trackH2) * 0.5f, w - 2f * pad - 2f * zoomH - 20f, trackH2);
            var hs = ThemeTokens.GetFloat("size.map.zoom.handle", 26f);
            var hr = _zoom.handleRect;
            if (hr && Mathf.Abs(hr.sizeDelta.x - hs) > 0.1f) hr.sizeDelta = new Vector2(hs, hs - trackH2);
            var v = MinimapMath.ZoomToSlider(_map.LargeZoom, _map.m_minZoom, _map.m_maxZoom);
            if (Mathf.Abs(_zoom.value - v) > 0.002f)
            {
                _zoomWrite = true;
                _zoom.value = v;
                _zoomWrite = false;
            }

            LayoutHints(inset);
        }

        private float Section(TMP_Text header, Image line, string token, float pad, float y, float w, float headerH, float divider)
        {
            Place(header.rectTransform, pad, y, w, headerH);
            HeaderFace(header);
            var text = Words.Get(token);
            if (header.text != text) header.text = text;
            Place(line.rectTransform, pad, y + headerH, w, divider);
            return y + headerH + divider + 4f;
        }

        // Section titles in the map's own header face (mockup: a wide serif in capitals), font.display until it loads.
        private static void HeaderFace(TMP_Text header)
        {
            var face = ThemeTokens.GetFont("font.map.header") ?? ThemeTokens.GetFont("font.display");
            if (face && header.font != face)
            {
                header.font = face;
                if (face.material) header.fontSharedMaterial = face.material;
            }
            var size = ThemeTokens.GetFloat("size.map.legend.header", 21f);
            if (Mathf.Abs(header.fontSize - size) > 0.05f) header.fontSize = size;
            if ((header.fontStyle & FontStyles.UpperCase) == 0) header.fontStyle |= FontStyles.UpperCase;
        }

        // Socket | name | checkbox; a vanilla icon button rides the socket (world placement: it stays in its own panel).
        private void LayRow(PinRow r, float x, float y, float w, float rowH, float k)
        {
            Place(r.Root, x, y, w, rowH);
            var cell = rowH - 4f;
            Place(r.Socket.rectTransform, 0f, 2f, cell, cell);
            var gi = ThemeTokens.GetFloat("size.map.legend.icon.inset", 8f) * 0.5f;
            Place(r.Glyph.rectTransform, gi, gi, cell - 2f * gi, cell - 2f * gi);
            var box = Mathf.Min(ThemeTokens.GetFloat("size.map.legend.check", 24f), rowH - 8f);
            Place(r.Box.rectTransform, w - box, (rowH - box) * 0.5f, box, box);
            Place(r.Name.rectTransform, cell + 10f, 0f, w - cell - 10f - box - 6f, rowH);
            // Ellipsis also drops a line taller than the row: the label shrinks with squeezed rows (run 20261110-155326,
            // 1280x720 @ GUI 1.5: names gone at row 28 / font 24).
            var font = Mathf.Min(ThemeTokens.GetFloat("size.map.legend.font", 17f), rowH * 0.68f);
            if (Mathf.Abs(r.Name.fontSize - font) > 0.05f) r.Name.fontSize = font;
            if (!r.Icon) return;
            var icon = (RectTransform)r.Icon.transform;
            icon.anchorMin = icon.anchorMax = icon.pivot = new Vector2(0.5f, 0.5f);
            var parentK = icon.parent ? icon.parent.lossyScale.x : 1f;
            var s = (cell - ThemeTokens.GetFloat("size.map.legend.icon.inset", 8f)) * (parentK > 0f ? k / parentK : 1f);
            if (Mathf.Abs(icon.sizeDelta.x - s) > 0.1f || Mathf.Abs(icon.sizeDelta.y - s) > 0.1f) icon.sizeDelta = new Vector2(s, s);
            var socket = r.Socket.rectTransform;
            var world = socket.TransformPoint(socket.rect.center);
            if ((icon.position - world).sqrMagnitude > 1e-4f) icon.position = world;
        }

        // A vanilla / mod icon button of a row scrolled out of the window: not drawn, not clickable (it stays active
        // in its panel, mods find it there).
        private static void ShowButton(Button b, bool visible)
        {
            if (!b) return;
            var go = b.gameObject;
            if (!go.TryGetComponent<CanvasGroup>(out var g))
            {
                if (visible) return;
                g = go.AddComponent<CanvasGroup>();
            }
            var a = visible ? 1f : 0f;
            if (g.alpha != a) g.alpha = a;
            if (g.blocksRaycasts != visible) g.blocksRaycasts = visible;
            Morgott.UI.Restyle.RenderGate.Set(go, "maplegend.scroll", !visible);
        }

        // ИГРОКИ checkboxes: a hidden player's marker (Minimap.m_playerPins, index-aligned with m_tempPlayerInfo by
        // UpdatePlayerPins, Minimap.cs:1491-1530; the own marker m_largeMarker) is not drawn on the large map; its name
        // label follows the marker (PinLabels.Hidden).
        private void HidePlayers()
        {
            var infos = _map.m_tempPlayerInfo;
            var pins = _map.m_playerPins;
            for (var i = 0; i < pins.Count && i < infos.Count; i++)
                if (pins[i].m_uiElement) Gate(pins[i].m_uiElement.gameObject, !_hiddenPlayers.Contains(infos[i].m_name));
            var me = Game.instance && Game.instance.GetPlayerProfile() != null ? Game.instance.GetPlayerProfile().GetName() : null;
            if (_map.m_largeMarker) Gate(_map.m_largeMarker.gameObject, me == null || !_hiddenPlayers.Contains(me));
        }

        private static void Gate(GameObject go, bool visible)
        {
            if (!go.TryGetComponent<CanvasGroup>(out var g))
            {
                if (visible) return;
                g = go.AddComponent<CanvasGroup>();
                g.interactable = false;
            }
            var a = visible ? 1f : 0f;
            if (g.alpha != a) g.alpha = a;
            if (g.blocksRaycasts != visible) g.blocksRaycasts = visible;
            Morgott.UI.Restyle.RenderGate.Set(go, "map.players", !visible);
        }

        // Band under the map image that holds the key hint row (0 while the game's KeyHints setting is off: vanilla then
        // hides m_hints, Minimap.cs:1238-1242).
        private float HintBand() => _footer && PlatformPrefs.GetInt("KeyHints", 1) == 1 ? ThemeTokens.GetFloat("size.map.hint.h", 28f) : 0f;

        // Top-left placement inside the legend (y down from the top edge).
        private static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            if (rt.anchorMin != new Vector2(0f, 1f) || rt.anchorMax != new Vector2(0f, 1f) || rt.pivot != new Vector2(0f, 1f)) Top(rt);
            var p = new Vector2(x, -y);
            if ((rt.anchoredPosition - p).sqrMagnitude > 0.01f) rt.anchoredPosition = p;
            var s = new Vector2(w, h);
            if ((rt.sizeDelta - s).sqrMagnitude > 0.01f) rt.sizeDelta = s;
        }
    }

    // [Map Filters] vs vanilla's own filter writes (Minimap.ToggleIconFilter :2626, saved pins loading in SetMapData :2158).
    [HarmonyLib.HarmonyPatch]
    public static class MapFilterInput
    {
        internal static bool LoadingPins;

        private static bool Prepare() => Ownership.OwnershipRegistry.IsOwned(Ownership.OwnershipRegistry.Minimap);

        [HarmonyLib.HarmonyPatch(typeof(Minimap), nameof(Minimap.SetMapData))]
        [HarmonyLib.HarmonyPrefix]
        private static void SetMapData_Prefix() => LoadingPins = true;

        [HarmonyLib.HarmonyPatch(typeof(Minimap), nameof(Minimap.SetMapData))]
        [HarmonyLib.HarmonyFinalizer]
        private static void SetMapData_Finalizer()
        {
            LoadingPins = false;
            MinimapSkin.Instance?.ApplyFilters();
        }

        [HarmonyLib.HarmonyPatch(typeof(Minimap), nameof(Minimap.ToggleIconFilter))]
        [HarmonyLib.HarmonyPostfix]
        private static void ToggleIconFilter_Postfix(Minimap.PinType type) => MinimapSkin.Instance?.OnVanillaToggle(type);
    }

    // Mouse wheel over the map's side column scrolls its pin rows, not the map zoom: Minimap.UpdateMap reads
    // ZInput.GetMouseScrollWheel wherever the pointer is (Minimap.cs:941).
    [HarmonyLib.HarmonyPatch]
    public static class MapLegendInput
    {
        private static bool Prepare() => Ownership.OwnershipRegistry.IsOwned(Ownership.OwnershipRegistry.Minimap);

        [HarmonyLib.HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
        [HarmonyLib.HarmonyPrefix]
        private static bool GetMouseScrollWheel_Prefix(ref float __result)
        {
            if (!MinimapSkin.LegendHover) return true;
            __result = 0f;
            return false;
        }
    }
}
