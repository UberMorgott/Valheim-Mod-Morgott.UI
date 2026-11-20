using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Morgott.UI.Classify;
using Morgott.UI.InfoScreens;
using Morgott.UI.InventoryScreen;
using Morgott.UI.Restyle;
using Morgott.UI.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Morgott.UI.PreWorld
{
    // Pack changelog inside the vanilla changelog window (FejdStartup.m_changeLog; approved full-screen mockup
    // docs\mockups\2026-11-16-changelog\03-fullscreen.png, style-sheet.md there): a modal window filling the screen (Margin
    // to the edges, sized from the root canvas, so GUI scale and resolution follow) over a dimmed backdrop (click outside the
    // window, the close button or Esc closes it). Header "PACK CHANGES", close "× Close (Esc)", tabs Pack (ours) | Valheim
    // (the vanilla ScrollPanel + PatchlogScroll, text unchanged). Pack tab: left column period / type / mod filters (mod
    // search, All / None, a virtualized mod list with per-mod counts, hover "only" = solo, own scrollbar) and the
    // "Shown X of Y" footer; right a virtualized list (scroll position = first row index, only the rows in view are laid out
    // and measured, pooled per row kind) of mod + version + date groups. Data ChangelogModel
    // (BepInEx\config\Morgott.UI\changelog.json): parsed and laid out once while the menu builds (Init), on open only when
    // the file changed, so the open frame does no parsing (run 20261116-122424: 70 ms open hitch with the parse in the open
    // frame). The vanilla "new" badge also follows that file (MainMenuAdapter GetChangelogHash postfix, FileHash). Layout in
    // frame units (= canvas units; frame = the drawn RuneSkin stack): fixed left column, everything else anchored to the
    // frame edges, so a resize only moves anchors and re-runs the list layouts.
    [DisallowMultipleComponent]
    public sealed class ChangelogPanel : MonoBehaviour, IRevertible
    {
        public const string RootName = "MorgottChangelog";
        public const string ScrimName = "MorgottChangelogScrim";
        public static string FilePath => Path.Combine(BepInEx.Paths.ConfigPath, "Morgott.UI", "changelog.json");

        // The drawn frame (RuneSkin panel stack) reaches past the window by these (run 20261116-125447: stack 1170 x 776
        // around a 1050 x 676 window): the window is the frame minus them.
        private const float PadX = 60f, PadT = 60f, PadB = 40f;
        private const float Margin = 24f; // screen edge to the frame, canvas units
        // Layout (frame units, from the frame's top-left; Bottom / ListRight = insets from the bottom / right edge).
        private const float SideX = 36f, SideW = 460f, ListY = 156f, Bottom = 36f, ListRight = 64f, ListX = SideX + SideW + 48f;
        private const float ModListY = ListY + 480f, ModListW = SideW - 28f, ModRowH = 34f, FootH = 30f;
        private const float ModH = 58f, LineH = 40f, TagH = 30f, TagCol = 176f, TagGap = 14f;
        private static readonly string[] TypeColors = { "#9FD66B", "#E0413A", "#4FE0E6", "#E8B64A", "#8AB4F0", "#8E7BB8" };

        private FejdStartup _fejd;
        private RectTransform _window, _root, _pack, _list, _modList;
        private readonly List<(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax, bool active)> _vanilla = new List<(RectTransform, Vector2, Vector2, Vector2, Vector2, bool)>();
        private Vector2 _vanillaMin, _vanillaMax, _vanillaPivot, _vanillaPos, _vanillaSize;
        private RectTransform _topic, _scrollPanel, _scrollbar;
        private Canvas _canvas;
        private bool _addedCanvas, _addedRaycaster, _gameTab, _laidOut, _dirty = true;
        private (bool over, int layer, int order) _canvasOrig;
        private GameObject _scrim;
        private Vector2 _frame; // current frame size (Fit)

        // data
        private List<ChangelogEntry> _all = new List<ChangelogEntry>();
        private List<string> _mods = new List<string>(), _shownMods = new List<string>();
        private string _error, _modQuery = "";
        private DateTime _stamp = DateTime.MinValue;
        private long _size = -1;
        private readonly ChangelogFilter _filter = new ChangelogFilter();
        private ChangelogView _view;
        private int _preset = 1; // 0 = 7 days, 1 = 30 days (mockup default), 2 = all, -1 = typed dates
        private int _scroll, _modScroll;

        // widgets
        private Button _tabPack, _tabGame;
        private TMP_Text _title, _tabPackLabel, _tabGameLabel, _empty, _footer;
        private readonly List<TMP_Text> _serif = new List<TMP_Text>();
        private Image _underline;
        private Image[][] _tabRims;
        private TMP_InputField _from, _to, _search;
        private Bar _bar, _modBar;
        private readonly Button[] _presets = new Button[3];
        private readonly Image[][] _presetRims = new Image[3][];
        private readonly TMP_Text[] _presetLabels = new TMP_Text[3];
        private readonly Button[] _chips = new Button[6];
        private readonly Image[] _chipRims = new Image[6], _chipFills = new Image[6];
        private Image _rune;
        private readonly TMP_Text[] _chipLabels = new TMP_Text[6], _chipCounts = new TMP_Text[6], _chipDots = new TMP_Text[6];
        private readonly Pool _modHeads = new Pool(), _lines = new Pool();
        private readonly List<ModRow> _modRows = new List<ModRow>();

        private sealed class Row
        {
            public RectTransform Rt;
            public TMP_Text A, B;
            public RectTransform Tag;
            public Image Rule, Rim;
        }

        private sealed class Pool
        {
            public readonly List<Row> Items = new List<Row>();
            public int Used;
        }

        private sealed class ModRow
        {
            public Button Box, Solo;
            public Image Check, Fill;
            public TMP_Text Label, Count;
            public string Mod;
            public bool Hover;
        }

        // Scrollbar: grip (hit area: press / drag / wheel) with a track and a thumb inside.
        private sealed class Bar
        {
            public RectTransform Grip;
            public Image Track, Thumb;
        }

        // Wheel over the list / mod list, press + drag on the scrollbar (share 0..1 of the track from its top).
        private sealed class Input : MonoBehaviour, IScrollHandler, IPointerDownHandler, IInitializePotentialDragHandler, IDragHandler
        {
            public Action<float> Wheel;
            public Action<float> Drag;
            public void OnScroll(PointerEventData e) => Wheel?.Invoke(e.scrollDelta.y);
            public void OnInitializePotentialDrag(PointerEventData e) => e.useDragThreshold = false;
            public void OnPointerDown(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) Drag?.Invoke(Share(e)); }
            public void OnDrag(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) Drag?.Invoke(Share(e)); }

            private float Share(PointerEventData e)
            {
                var rt = (RectTransform)transform;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out var p);
                return Mathf.Clamp01((rt.rect.yMax - p.y) / Mathf.Max(rt.rect.height, 1e-6f));
            }
        }

        // Dimmed backdrop: a click outside the window closes it through the vanilla toggle (badge hash written as vanilla).
        private sealed class Backdrop : MonoBehaviour, IPointerClickHandler
        {
            public Action<PointerEventData> Click;
            public void OnPointerClick(PointerEventData e) => Click?.Invoke(e);
        }

        // Mod row hover (shows its "only" button).
        private sealed class Hover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public Action<bool> Set;
            public void OnPointerEnter(PointerEventData e) => Set?.Invoke(true);
            public void OnPointerExit(PointerEventData e) => Set?.Invoke(false);
        }

        public static ChangelogPanel Ensure(FejdStartup fejd, RectTransform window)
        {
            if (!fejd || !window) return null;
            if (window.TryGetComponent<ChangelogPanel>(out var p)) return p;
            p = window.gameObject.AddComponent<ChangelogPanel>();
            p.hideFlags = HideFlags.HideInInspector;
            p._fejd = fejd;
            p._window = window;
            p.Init();
            return p;
        }

        // The file's stable hash folded into the vanilla one (0 = no file: the vanilla badge alone); cached per stamp + size
        // (the toggle asks on every open).
        private static (DateTime stamp, long size, int hash) _hashCache = (DateTime.MinValue, -2, 0);

        public static int FileHash()
        {
            try
            {
                var fi = new FileInfo(FilePath);
                if (!fi.Exists) return 0;
                if (_hashCache.stamp == fi.LastWriteTimeUtc && _hashCache.size == fi.Length) return _hashCache.hash;
                _hashCache = (fi.LastWriteTimeUtc, fi.Length, File.ReadAllText(fi.FullName).GetStableHashCode());
                return _hashCache.hash;
            }
            catch (IOException) { return 0; }
            catch (UnauthorizedAccessException) { return 0; }
        }

        private static bool Russian => Localization.instance != null && Localization.instance.GetSelectedLanguage() == "Russian";
        private static string L(string token) => Localization.instance != null ? Localization.instance.Localize(token) : token;
        private static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;
        private static Color TypeColor(int t) => ThemeTokens.GetColor("color.changelog." + ChangelogModel.Types[t], Hex(TypeColors[t]));
        private static Color Body => ThemeTokens.GetColor("color.text.body", Hex("#E3E6E8"));
        private static Color Muted => ThemeTokens.GetColor("color.state.disabled", Hex("#8A9096"));
        private static Color Gold => ThemeTokens.GetColor("color.text.active", Hex("#E8B64A"));

        // ---------------------------------------------------------------- build

        private void Init()
        {
            // Modal, centred on the screen: the vanilla window sat at a fixed spot left of the centre.
            _vanillaMin = _window.anchorMin;
            _vanillaMax = _window.anchorMax;
            _vanillaPivot = _window.pivot;
            _vanillaPos = _window.anchoredPosition;
            _vanillaSize = _window.sizeDelta;
            _window.anchorMin = _window.anchorMax = _window.pivot = new Vector2(0.5f, 0.5f);
            _window.anchoredPosition = Vector2.zero;
            _topic = _window.Find("Topic") as RectTransform;
            _scrollPanel = _window.Find("ScrollPanel") as RectTransform;
            _scrollbar = _window.TryGetComponent<ChangeLog>(out var log) && log.m_scrollbar ? (RectTransform)log.m_scrollbar.transform : _window.Find("PatchlogScroll") as RectTransform;
            foreach (var rt in new[] { _topic, _scrollPanel, _scrollbar })
                if (rt) _vanilla.Add((rt, rt.anchorMin, rt.anchorMax, rt.offsetMin, rt.offsetMax, rt.gameObject.activeSelf));
            // Valheim tab: the vanilla text fills the content area under the tabs, its scrollbar at the right edge (window
            // anchors: follows the window size).
            if (_scrollPanel) Stretch(_scrollPanel, SideX - PadX, ListY + 6f - PadT, 64f - PadX, Bottom - PadB);
            if (_scrollbar)
            {
                _scrollbar.anchorMin = new Vector2(1f, 0f);
                _scrollbar.anchorMax = Vector2.one;
                _scrollbar.pivot = new Vector2(1f, 0.5f);
                _scrollbar.offsetMin = new Vector2(-36f + PadX - ThemeTokens.GetFloat("size.scrollbar.w", 16f), Bottom - PadB);
                _scrollbar.offsetMax = new Vector2(-36f + PadX, -(ListY + 6f - PadT));
            }

            // Backdrop: first child, reaching far past the window over the whole screen, drawn under the window.
            var scrim = new GameObject(ScrimName, typeof(RectTransform)).AddComponent<Image>();
            scrim.rectTransform.SetParent(_window, false);
            scrim.rectTransform.SetAsFirstSibling();
            Stretch(scrim.rectTransform, -6000f, -6000f, -6000f, -6000f);
            // Opaque: the game blends in linear space, so alpha 0.9 left 10 % linear light = ~31 % sRGB and the menu
            // texts in the 24 px margin (BepInEx Version, player.log link) read through at 80/255 vs 230 bare (run
            // 20261116-210336, ui-runeskin-m5-changelog vs -badge). The full-screen window leaves nothing to show behind.
            scrim.color = ThemeTokens.GetColor("color.changelog.scrim", new Color(0.016f, 0.027f, 0.039f, 1f));
            scrim.raycastTarget = true;
            DollCell.Mark(scrim, "content", "changelog modal backdrop");
            scrim.gameObject.AddComponent<Backdrop>().Click = e =>
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(_window, e.position, e.pressEventCamera)) return;
                if (_fejd && _fejd.m_changeLog && _fejd.m_changeLog.activeInHierarchy) _fejd.OnButtonShowChangelog();
            };
            _scrim = scrim.gameObject;

            _root = NewRect(RootName, _window);
            Stretch(_root, -PadX, -PadT, -PadX, -PadB); // root = the drawn frame (layout units)
            // Mockup 4a: soft vertical dark gradient over the inner glass (window rect), under everything of ours.
            var shade = new GameObject("Shade", typeof(RectTransform)).AddComponent<Image>();
            shade.rectTransform.SetParent(_root, false);
            Stretch(shade.rectTransform, PadX, PadT, PadX, PadB);
            shade.sprite = Shade;
            shade.raycastTarget = false;
            DollCell.Mark(shade, "content", "changelog glass gradient");
            // Below the close button (ChangelogClose adds it as the last sibling); the button in the frame band right of the
            // title, labelled "× Close (Esc)" (mockup).
            if (_window.Find(ChangelogClose.ButtonName) is RectTransform close)
            {
                _root.SetSiblingIndex(close.GetSiblingIndex());
                close.anchorMin = close.anchorMax = close.pivot = Vector2.one;
                close.anchoredPosition = new Vector2(PadX - 36f, PadT - 22f);
                close.sizeDelta = new Vector2(230f, 46f);
                if (close.GetComponentInChildren<TMP_Text>() is TMP_Text closeLabel)
                {
                    closeLabel.text = L("$ui_changelog_close");
                    closeLabel.fontSize = 21f;
                }
            }

            _title = Txt(_root, "Title", "window.title", TextAlignmentOptions.Center, 42f);
            _title.characterSpacing = 14f;
            _title.text = L("$ui_changelog_title");
            _serif.Add(_title);
            Band(_title, 300f, 300f, 10f, 60f);

            _tabPack = Btn(_root, "TabPack", "tab", out _tabPackLabel, () => { _gameTab = false; _dirty = true; });
            _tabGame = Btn(_root, "TabGame", "tab", out _tabGameLabel, () => { _gameTab = true; _dirty = true; });
            _tabPackLabel.text = L("$ui_changelog_tab_pack");
            _tabGameLabel.text = L("$ui_changelog_tab_game");
            foreach (var t in new[] { _tabPackLabel, _tabGameLabel }) { t.fontSize = 26f; _serif.Add(t); }
            _tabRims = new[] { Rim(_tabPack.transform), Rim(_tabGame.transform) };
            Put(_tabPack, -330f, 74f, 320f, 52f, 0.5f);
            Put(_tabGame, 10f, 74f, 320f, 52f, 0.5f);
            _underline = Rule(_root, "TabUnderline", Hex("#4FE0E6"));
            // Mockup 2a: cyan rune left of the selected tab label (placed in Stamp).
            _rune = new GameObject("TabRune", typeof(RectTransform)).AddComponent<Image>();
            _rune.rectTransform.SetParent(_root, false);
            _rune.sprite = _runeSprite ? _runeSprite : _runeSprite = Art("Morgott.UI.ChangelogRune.png", Vector4.zero);
            _rune.preserveAspect = true;
            _rune.raycastTarget = false;
            _rune.color = Hex("#4FE0E6");
            DollCell.Mark(_rune, "content", "changelog selected tab rune");
            Band(Rule(_root, "TabRule", Hex("#2395B7")), 28f, 28f, 140f, 1f);

            _pack = NewRect("Pack", _root);
            Stretch(_pack, 0f, 0f, 0f, 0f);
            BuildSidebar();
            BuildList();
            // Own canvas above the menu column and the bottom-left links (as SidePanel does), added now: a canvas added on
            // the open frame re-batched the whole window there (run 20261116-125447: 101 ms open frame).
            // The window may carry a canvas of its own that does not override sorting: then the menu texts after it in the
            // hierarchy (StartGui/Menu/modded_text, BepInEx Version) drew over the backdrop (run 20261116-131125). Override
            // sorting either way (originals kept for Revert).
            if (!_window.TryGetComponent(out _canvas))
            {
                _canvas = _window.gameObject.AddComponent<Canvas>();
                _addedCanvas = true;
            }
            else _canvasOrig = (_canvas.overrideSorting, _canvas.sortingLayerID, _canvas.sortingOrder);
            if (!_window.GetComponent<GraphicRaycaster>()) { _window.gameObject.AddComponent<GraphicRaycaster>(); _addedRaycaster = true; }
            Sort();
            Fit();
            // Parse, filter and the first layout now, while the menu builds (window shown for this one call and hidden in
            // the same frame, nothing drawn): the dynamic serif atlas gets its glyphs and the rows their measures here,
            // the open frame only draws (run 20261116-125447: layout 33.6 ms in the open frame).
            var sw = Stopwatch.StartNew();
            var was = _window.gameObject.activeSelf;
            if (!was) _window.gameObject.SetActive(true);
            Enforce();
            Load();
            Layout();
            foreach (var t in _root.GetComponentsInChildren<TMP_Text>(false)) t.ForceMeshUpdate(); // glyphs into the dynamic atlases now
            _laidOut = true;
            if (!was) _window.gameObject.SetActive(false);
            _dirty = false;
            Plugin.Log.LogInfo($"[Morgott.UI] changelog: {_all.Count} entries, {_view?.Shown ?? 0} shown, load+layout {sw.Elapsed.TotalMilliseconds:F1} ms at menu build{(_error != null ? ", " + _error : "")}");
        }

        private void BuildSidebar()
        {
            var p = _pack;
            Label(p, "PeriodLabel", "$ui_changelog_period", ListY);
            const float FieldW = (SideW - 12f) / 2f;
            _from = DateField(p, "From", "$ui_changelog_from", SideX, FieldW, true);
            _to = DateField(p, "To", "$ui_changelog_to", SideX + FieldW + 12f, FieldW, false);
            string[] presetKeys = { "$ui_changelog_days7", "$ui_changelog_days30", "$ui_changelog_all" };
            const float PresetW = (SideW - 20f) / 3f;
            for (var i = 0; i < 3; i++)
            {
                var n = i;
                _presets[i] = Btn(p, "Preset" + i, "field", out _presetLabels[i], () => { _preset = n; ApplyPreset(); });
                _presetRims[i] = Rim(_presets[i].transform);
                _presetLabels[i].text = L(presetKeys[i]);
                _presetLabels[i].fontSize = 19f;
                Put(_presets[i], SideX + i * (PresetW + 10f), ListY + 86f, PresetW, 36f);
            }
            Label(p, "TypeLabel", "$ui_changelog_type", ListY + 132f);
            for (var i = 0; i < 6; i++)
            {
                var n = i;
                // Mockup chip (1b / 3a): pill with a rim in the type colour over a dark tint of it, coloured dot, white
                // label, count in the type colour.
                _chips[i] = Btn(p, "Type" + i, null, out _chipLabels[i], () => { _filter.Types[n] = !_filter.Types[n]; _scroll = 0; _dirty = true; });
                (_chipFills[i], _chipRims[i]) = Pill(_chips[i].transform);
                _chipLabels[i].alignment = TextAlignmentOptions.Left;
                _chipLabels[i].text = L("$ui_changelog_type_" + ChangelogModel.Types[i]);
                _chipLabels[i].fontSize = 20f;
                Stretch(_chipLabels[i].rectTransform, 38f, 0f, 76f, 0f);
                _chipDots[i] = Txt(_chips[i].transform, "Dot", "text.body", TextAlignmentOptions.Center, 18f);
                _chipDots[i].text = "●";
                Put(_chipDots[i], 8f, 0f, 24f, 32f);
                _chipCounts[i] = Txt(_chips[i].transform, "Count", "text.body", TextAlignmentOptions.Right, 20f);
                Put(_chipCounts[i], SideW - 82f, 0f, 66f, 32f);
                Put(_chips[i], SideX, ListY + 170f + i * 36f, SideW, 32f);
            }
            Label(p, "ModLabel", "$ui_changelog_mod", ListY + 392f);
            // Search (substring, case-insensitive) + All / None over the mods it matches.
            _search = InputBox(p, "ModSearch", "", 0f, "$ui_changelog_search");
            _search.characterLimit = 40;
            _search.onValueChanged.AddListener(s => { _modQuery = s; _modScroll = 0; _dirty = true; });
            Put(_search, SideX, ListY + 430f, SideW - 216f, 38f);
            var all = Btn(p, "ModsAll", "field", out var allLabel, () => { ChangelogModel.ShowMods(_filter, _shownMods, true); _scroll = 0; _dirty = true; });
            var none = Btn(p, "ModsNone", "field", out var noneLabel, () => { ChangelogModel.ShowMods(_filter, _shownMods, false); _scroll = 0; _dirty = true; });
            allLabel.text = L("$ui_changelog_mods_all");
            noneLabel.text = L("$ui_changelog_mods_none");
            allLabel.fontSize = noneLabel.fontSize = 18f;
            Put(all, SideX + SideW - 208f, ListY + 430f, 100f, 38f);
            Put(none, SideX + SideW - 100f, ListY + 430f, 100f, 38f);
            _modList = NewRect("ModList", p);
            Column(_modList, SideX, ModListW, ModListY, Bottom + FootH + 12f);
            HitArea(_modList, "ModListHit").Wheel = d => { _modScroll -= Math.Sign(d); _dirty = true; };
            _modBar = NewBar(p, "ModScroll", s => { _modScroll = Mathf.RoundToInt(s * ModMax()); _dirty = true; }, d => { _modScroll -= Math.Sign(d); _dirty = true; });
            Column(_modBar.Grip, SideX + ModListW + 2f, 26f, ModListY, Bottom + FootH + 12f);
            _footer = Txt(p, "Footer", "text.muted", TextAlignmentOptions.Center, 20f);
            _footer.richText = true;
            var foot = _footer.rectTransform;
            foot.anchorMin = foot.anchorMax = foot.pivot = Vector2.zero;
            foot.anchoredPosition = new Vector2(SideX, Bottom);
            foot.sizeDelta = new Vector2(SideW, FootH);
            Column(Rule(p, "SideRule", Hex("#3D8290")), SideX + SideW + 24f, 1f, ListY, Bottom);
        }

        private void BuildList()
        {
            _list = NewRect("List", _pack);
            _list.gameObject.AddComponent<RectMask2D>();
            Stretch(_list, ListX, ListY, ListRight, Bottom);
            HitArea(_list, "ListHit").Wheel = d => { _scroll -= 3 * Math.Sign(d); _dirty = true; };
            _bar = NewBar(_pack, "ScrollGrip", s => { _scroll = Mathf.RoundToInt(s * MaxScroll()); _dirty = true; }, d => { _scroll -= 3 * Math.Sign(d); _dirty = true; });
            Column(_bar.Grip, -(ListRight - 12f), 30f, ListY, Bottom, 1f);
            _empty = Txt(_pack, "Empty", "text.muted", TextAlignmentOptions.Top, 22f);
            _empty.textWrappingMode = TextWrappingModes.Normal;
            _empty.fontStyle = FontStyles.Italic;
            Stretch(_empty.rectTransform, ListX + 20f, ListY + 40f, ListRight + 20f, Bottom);
        }

        // ---------------------------------------------------------------- lifecycle

        private void OnEnable()
        {
            if (!_window || !_laidOut) return; // Init's own show / hide
            Sort();
            // FejdStartup.Start's ScreenRegistry.Restyle put the role faces back after Init (run 20261116-131125).
            Stamp();
            // Re-laid on the first open, a changed file or a new day for the period presets; else shown as it was.
            if (_preset >= 0 && _preset < 2 && _filter.To != DateTime.Today) ApplyPreset();
            if (!_laidOut || FileChanged()) _dirty = true;
        }

        private void LateUpdate()
        {
            if (!_root) return;
            Enforce();
            if (Fit()) _dirty = true;
            Centre();
            if (!_dirty) return;
            _dirty = false;
            var sw = Stopwatch.StartNew();
            var loaded = Load();
            var first = !_laidOut;
            Layout();
            _laidOut = true;
            if (loaded || first) Plugin.Log.LogInfo($"[Morgott.UI] changelog: {_all.Count} entries, {_view?.Shown ?? 0} shown, {(loaded ? "load+" : "")}layout {sw.Elapsed.TotalMilliseconds:F1} ms{(_error != null ? ", " + _error : "")}");
        }

        // Frame = the root canvas (canvas units: GuiScaler scale + GUI scale setting applied) minus Margin per side; true
        // when the window was resized.
        private bool Fit()
        {
            // Topmost canvas, inactive parents included (Init runs with the menu hierarchy inactive).
            var canvases = _window.GetComponentsInParent<Canvas>(true);
            var rrt = canvases.Length > 0 ? (RectTransform)canvases[canvases.Length - 1].transform : null;
            var size = rrt ? rrt.rect.size * (rrt.lossyScale.x / Mathf.Max(_window.lossyScale.x, 1e-6f)) : Vector2.zero;
            if (size.x < 640f || size.y < 360f) size = new Vector2(1920f, 1080f); // canvas not laid out yet
            var frame = size - 2f * Margin * Vector2.one;
            if ((frame - _frame).sqrMagnitude < 0.25f) return false;
            _frame = frame;
            _window.sizeDelta = new Vector2(frame.x - 2f * PadX, frame.y - PadT - PadB);
            return true;
        }

        // Tab state: vanilla title always off (ours replaces it), vanilla text only on the Valheim tab.
        private void Enforce()
        {
            SetActive(_topic, false);
            SetActive(_scrollPanel, _gameTab);
            SetActive(_scrollbar, _gameTab);
            SetActive(_pack, !_gameTab);
        }

        // The drawn frame (the RuneSkin stack, PanelDecor) reaches further past the window on top than at the bottom
        // (run 20261116-125447: stack 10 px above the screen centre): the stack, not the window rect, is centred.
        private RectTransform _stack;

        private void Centre()
        {
            if (!_stack)
                foreach (var rt in _window.GetComponentsInChildren<RectTransform>(false))
                    if (rt.name == "RuneSkinPanel") { _stack = rt; break; }
            if (!_stack) return;
            var c = new Vector3[4];
            _stack.GetWorldCorners(c);
            var mid = _window.InverseTransformPoint((c[0] + c[2]) * 0.5f);
            var own = _window.rect.center;
            var want = -new Vector2(mid.x - own.x, mid.y - own.y); // offset of the stack inside the window: constant, so no drift
            if ((want - _window.anchoredPosition).sqrMagnitude > 0.25f) _window.anchoredPosition = want;
        }

        private static void SetActive(Component c, bool on)
        {
            if (c && c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
        }

        private static (DateTime stamp, long size) Stat()
        {
            try
            {
                var fi = new FileInfo(FilePath);
                return fi.Exists ? (fi.LastWriteTimeUtc, fi.Length) : (DateTime.MinValue, -1L);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return (DateTime.MinValue, -1L); }
        }

        private bool FileChanged()
        {
            var (stamp, size) = Stat();
            return stamp != _stamp || size != _size;
        }

        // Reads the file when it changed (stamp + size); true when it was (re)read.
        private bool Load()
        {
            var (stamp, size) = Stat();
            if (stamp == _stamp && size == _size && _view != null) return false;
            _stamp = stamp;
            _size = size;
            _all = new List<ChangelogEntry>();
            _error = null;
            if (size < 0) _error = "no file";
            else
            {
                try { _all = ChangelogModel.Parse(File.ReadAllText(FilePath), out _error) ?? new List<ChangelogEntry>(); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { _error = "unreadable: " + e.Message; }
            }
            _mods = ChangelogModel.Mods(_all);
            _filter.HiddenMods.IntersectWith(_mods);
            ApplyPreset(false);
            // The 30-day default on a pack not updated for a month: everything instead of an empty list.
            if (_preset == 1 && _all.Count > 0 && ChangelogModel.Build(_all, _filter).Shown == 0) { _preset = 2; ApplyPreset(false); }
            _view = null;
            return true;
        }

        private void ApplyPreset(bool dirty = true)
        {
            var today = DateTime.Today;
            _filter.To = _preset == 2 ? (DateTime?)null : today;
            _filter.From = _preset == 0 ? today.AddDays(-6) : _preset == 1 ? today.AddDays(-29) : (DateTime?)null;
            if (_preset == 2 && _all.Count > 0)
            {
                // "All": the fields show the data's span.
                SetField(_from, _all[_all.Count - 1].Date);
                SetField(_to, _all[0].Date);
            }
            else
            {
                SetField(_from, _filter.From);
                SetField(_to, _filter.To);
            }
            _scroll = 0;
            if (dirty) _dirty = true;
        }

        private static void SetField(TMP_InputField f, DateTime? d)
        {
            if (f) f.SetTextWithoutNotify(d.HasValue ? ChangelogModel.Day(d.Value) : "");
        }

        // ---------------------------------------------------------------- layout

        private void Layout()
        {
            Stamp();
            Put(_underline, _gameTab ? 10f : -330f, 126f, 320f, 3f, 0.5f);
            if (_gameTab) return;

            _view = ChangelogModel.Build(_all, _filter);
            var cyan = Hex("#4FE0E6");
            for (var i = 0; i < 3; i++)
            {
                var on = _preset == i;
                SetRim(_presetRims[i], on ? cyan : Color.clear);
                _presetLabels[i].color = on ? Body : Muted;
            }
            for (var i = 0; i < 6; i++)
            {
                var on = _filter.Types[i];
                var c = TypeColor(i);
                TintPill(_chipFills[i], _chipRims[i], on ? c : new Color(Muted.r, Muted.g, Muted.b, 0.5f), on);
                _chipDots[i].color = on ? c : Muted;
                _chipLabels[i].color = on ? Body : Muted;
                _chipCounts[i].text = _view.TypeCounts[i].ToString();
                _chipCounts[i].color = on ? c : Muted;
            }
            LayoutMods();
            LayoutRows();
            _footer.text = string.Format(L("$ui_changelog_shown"), $"<color=#E3E6E8>{ChangelogModel.Count(_view.Shown)}</color>", $"<color=#E3E6E8>{ChangelogModel.Count(_view.Total)}</color>");
            _empty.text = _error != null ? L("$ui_changelog_empty_file") : _view.Shown == 0 ? L("$ui_changelog_empty_filter") : "";
            _empty.gameObject.SetActive(_empty.text.Length > 0);
        }

        // Faces and colours a role restyle (menu Start, theme reload) sets back: serif display face (mixed case, mockup:
        // title, tabs, section titles, mod headers), tab state colours. Cheap when unchanged; run on layout and open.
        // Sort override above the menu canvas (run 20261116-133155: windowOrder=0 override=False, modded_text over the
        // backdrop): at menu build the parent chain is inactive and GetComponentInParent(false) found no canvas, so the
        // override was never set. Inactive parents included; re-applied on open.
        // One above the higher of its own root ("Canvas Changelog") and the menu's root (GUI: StartGui/Menu with the
        // BepInEx text and BottomLeftButtons), so popups (ModalLayer, base + 5) and tooltips (TopOverlay, base + 10) still
        // draw over it; the former +20 put the window above both.
        private void Sort()
        {
            var parent = _window.parent ? _window.parent.GetComponentInParent<Canvas>(true) : null;
            if (!parent || !_canvas) return;
            var menu = _fejd && _fejd.m_mainMenu ? _fejd.m_mainMenu.GetComponentInParent<Canvas>(true) : null;
            var order = Mathf.Max(parent.rootCanvas.sortingOrder, menu ? menu.rootCanvas.sortingOrder : int.MinValue) + 1;
            if (_canvas.overrideSorting && _canvas.sortingOrder == order) return;
            _canvas.overrideSorting = true;
            _canvas.sortingLayerID = parent.rootCanvas.sortingLayerID;
            _canvas.sortingOrder = order;
        }

        private void Stamp()
        {
            var serif = ThemeTokens.GetFont("font.map.header");
            if (serif) foreach (var t in _serif) if (t && t.font != serif) t.font = serif;
            var header = ThemeTokens.GetColor("color.text.header", Hex("#D9DCE0"));
            var title = ThemeTokens.GetColor("color.text.item.title", Hex("#F0C419"));
            foreach (var t in _serif)
            {
                if (!t) continue;
                var c = t.name == "A" && t.transform.parent.name.StartsWith("ModHead", StringComparison.Ordinal) ? title : header;
                if (t != _tabPackLabel && t != _tabGameLabel && t.color != c) t.color = c;
            }
            // Tabs: selected = not interactable (tab role: disabled sprite = selected plate), gold label + cyan underline.
            _tabPack.interactable = _gameTab;
            _tabGame.interactable = !_gameTab;
            _tabPackLabel.color = _gameTab ? Body : Gold;
            _tabGameLabel.color = _gameTab ? Gold : Body;
            // Selected tab: gold rim (mockup tabs-c glass + rune).
            SetRim(_tabRims[0], _gameTab ? Color.clear : Gold);
            SetRim(_tabRims[1], _gameTab ? Gold : Color.clear);
            // Rune + label centred as a pair (mockup 2a): the selected label moves right by half the rune slot.
            const float RuneW = 18f, RuneGap = 8f;
            var sel = _gameTab ? _tabGameLabel : _tabPackLabel;
            Stretch(sel.rectTransform, 4f + RuneW + RuneGap, 0f, 4f, 0f);
            Stretch((_gameTab ? _tabPackLabel : _tabGameLabel).rectTransform, 4f, 0f, 4f, 0f);
            var mid = (_gameTab ? 10f : -330f) + 160f + (RuneW + RuneGap) / 2f;
            Put(_rune, mid - sel.GetPreferredValues(sel.text).x / 2f - RuneGap - RuneW, 88f, RuneW, 24f, 0.5f);
        }

        private int ModVisible => Mathf.Max(1, Mathf.FloorToInt(_modList.rect.height / ModRowH));
        private int ModMax() => Mathf.Max(0, _shownMods.Count - ModVisible);

        // Mods matching the search, from _modScroll down; name, entry count (date + type filters), check = not hidden.
        private void LayoutMods()
        {
            _shownMods = ChangelogModel.MatchMods(_mods, _modQuery);
            var visible = ModVisible;
            _modScroll = Mathf.Clamp(_modScroll, 0, ModMax());
            for (var i = 0; i < Mathf.Max(visible, _modRows.Count); i++)
            {
                var k = _modScroll + i;
                if (i >= visible || k >= _shownMods.Count)
                {
                    if (i < _modRows.Count) { _modRows[i].Mod = null; _modRows[i].Box.gameObject.SetActive(false); }
                    continue;
                }
                if (i == _modRows.Count) _modRows.Add(NewModRow(i));
                var r = _modRows[i];
                r.Mod = _shownMods[k];
                r.Box.gameObject.SetActive(true);
                Put(r.Box, 0f, i * ModRowH, ModListW, ModRowH);
                r.Check.enabled = !_filter.HiddenMods.Contains(r.Mod);
                r.Label.text = r.Mod;
                var n = _view.ModCounts.TryGetValue(r.Mod, out var c) ? c : 0;
                r.Label.color = n > 0 ? Body : Muted;
                r.Count.text = n.ToString();
                r.Count.color = Muted;
                ApplyHover(r);
            }
            SetBar(_modBar, _modScroll, ModMax(), visible / (float)Mathf.Max(1, _shownMods.Count));
        }

        private static void ApplyHover(ModRow r)
        {
            SetActive(r.Solo, r.Hover && r.Mod != null);
            r.Fill.color = r.Hover ? new Color(0.91f, 0.71f, 0.29f, 0.14f) : Color.clear;
        }

        private ModRow NewModRow(int i)
        {
            var r = new ModRow();
            // Clear hit area over the row; box + check drawn at its left, count at its right, "only" on hover.
            var hit = new GameObject("Mod" + i, typeof(RectTransform)).AddComponent<Image>();
            hit.rectTransform.SetParent(_modList, false);
            hit.color = Color.clear;
            DollCell.Mark(hit, "content", "changelog mod row hit area");
            r.Box = hit.gameObject.AddComponent<Button>();
            r.Box.targetGraphic = hit;
            r.Box.transition = Selectable.Transition.None;
            r.Box.navigation = new Navigation { mode = Navigation.Mode.None };
            r.Box.onClick.AddListener(() =>
            {
                if (r.Mod == null) return;
                if (!_filter.HiddenMods.Remove(r.Mod)) _filter.HiddenMods.Add(r.Mod);
                _scroll = 0;
                _dirty = true;
            });
            r.Fill = Fill(hit.transform);
            var box = Img(hit.transform, "Box", "toggle.box");
            Put(box, 4f, 8f, 18f, 18f);
            r.Check = Img(box.transform, "Check", "toggle.check");
            Stretch(r.Check.rectTransform, 2f, 2f, 2f, 2f);
            r.Label = Txt(hit.transform, "Label", "text.body", TextAlignmentOptions.Left, 20f);
            Put(r.Label, 34f, 0f, ModListW - 34f - 70f, ModRowH);
            r.Count = Txt(hit.transform, "Count", "text.body", TextAlignmentOptions.Right, 19f);
            Put(r.Count, ModListW - 66f, 0f, 58f, ModRowH);
            r.Solo = Btn(hit.transform, "Solo", "field", out var soloLabel, () =>
            {
                if (r.Mod == null) return;
                ChangelogModel.Solo(_filter, _mods, r.Mod);
                _scroll = 0;
                _dirty = true;
            });
            soloLabel.text = L("$ui_changelog_only");
            soloLabel.fontSize = 16f;
            soloLabel.color = Gold;
            SetRim(Rim(r.Solo.transform), Gold);
            Put(r.Solo, ModListW - 66f - 100f, 5f, 92f, 24f);
            r.Solo.gameObject.SetActive(false);
            hit.gameObject.AddComponent<Hover>().Set = on => { r.Hover = on; ApplyHover(r); };
            return r;
        }

        // Rows from _scroll down to the list bottom; each line row measured (wrapped text) only when shown.
        private void LayoutRows()
        {
            var rows = _view.Rows;
            _scroll = Mathf.Clamp(_scroll, 0, MaxScroll());
            _modHeads.Used = _lines.Used = 0;
            var russian = Russian;
            var y = 0f;
            var viewH = _list.rect.height;
            var listW = _list.rect.width;
            for (var i = _scroll; i < rows.Count && y < viewH; i++) y += Place(rows[i], y, listW, russian);
            foreach (var pool in new[] { _modHeads, _lines })
                for (var i = pool.Used; i < pool.Items.Count; i++) Off(pool.Items[i].Rt);
            // Thumb share = rows in view / all rows.
            SetBar(_bar, _scroll, rows.Count > 0 ? MaxScroll() : 0, viewH / LineH / Mathf.Max(1, rows.Count));
        }

        // Last first-row index that still fills the view (unwrapped heights from the end).
        private int MaxScroll()
        {
            if (_view == null) return 0;
            var rows = _view.Rows;
            var h = 0f;
            var viewH = _list.rect.height;
            for (var i = rows.Count - 1; i >= 0; i--)
            {
                h += rows[i].Kind == ChangelogRowKind.Mod ? ModH : LineH;
                if (h > viewH) return Mathf.Min(rows.Count - 1, i + 1);
            }
            return 0;
        }

        // Data text inside a rich-text line (mod name + version): no tag can open.
        private static string Plain(string s) => s.Replace('<', '‹');

        private float Place(ChangelogRow row, float y, float listW, bool russian)
        {
            var e = row.Entry;
            if (row.Kind == ChangelogRowKind.Mod)
            {
                // Mockup group header: "Morgott.Creatures  0.37.0" left, the date right, a rule under it.
                var r = Next(_modHeads, NewModHead);
                Put(r.Rt, 0f, y, listW, ModH);
                r.A.text = e.Version.Length > 0 ? $"{Plain(e.Mod)}<space=24><color=#8AB4F0>{Plain(e.Version)}</color>" : Plain(e.Mod);
                r.B.text = ChangelogModel.Day(e.Date);
                Put(r.A, 0f, 8f, listW - 200f, 42f);
                Put(r.B, listW - 200f, 8f, 196f, 42f);
                Put(r.Rule, 0f, ModH - 5f, listW, 1f);
                return ModH;
            }
            var l = Next(_lines, NewLineRow);
            var col = TypeColor(e.Type);
            l.B.color = col;
            l.B.text = L("$ui_changelog_type_" + ChangelogModel.Types[e.Type]);
            var tagW = Mathf.Min(TagCol, l.B.GetPreferredValues(l.B.text).x + 28f);
            l.A.text = e.Text(russian);
            var textX = 4f + TagCol + TagGap;
            var textW = listW - textX - 8f;
            var h = Mathf.Max(LineH, l.A.GetPreferredValues(l.A.text, textW, 0f).y + 12f);
            Put(l.Rt, 0f, y, listW, h);
            Put(l.Tag, 4f, 5f, tagW, TagH);
            TintPill(l.Rule, l.Rim, col, true);
            Put(l.B, 4f, 5f, tagW, TagH);
            Put(l.A, textX, 6f, textW, h - 6f);
            return h;
        }

        private Row Next(Pool pool, Func<int, Row> make)
        {
            var r = pool.Used < pool.Items.Count ? pool.Items[pool.Used] : null;
            if (r == null) pool.Items.Add(r = make(pool.Items.Count));
            pool.Used++;
            if (!r.Rt.gameObject.activeSelf) r.Rt.gameObject.SetActive(true);
            return r;
        }

        private Row NewModHead(int n)
        {
            var r = new Row { Rt = NewRect("ModHead" + n, _list) };
            r.A = Txt(r.Rt, "A", "text.header", TextAlignmentOptions.Left, 32f);
            r.A.color = ThemeTokens.GetColor("color.text.item.title", Hex("#F0C419"));
            r.A.richText = true; // name + version colour; the data parts go through Plain
            r.B = Txt(r.Rt, "B", "text.header", TextAlignmentOptions.Right, 26f);
            r.B.richText = false;
            foreach (var t in new[] { r.A, r.B })
            {
                _serif.Add(t);
                if (ThemeTokens.GetFont("font.map.header") is TMP_FontAsset f) t.font = f;
            }
            r.Rule = Rule(r.Rt, "Rule", Hex("#3D8290"));
            return r;
        }

        private Row NewLineRow(int n)
        {
            var r = new Row { Rt = NewRect("Line" + n, _list) };
            r.Tag = NewRect("Tag", r.Rt);
            (r.Rule, r.Rim) = Pill(r.Tag); // Rule slot = tag fill on line rows
            r.B = Txt(r.Rt, "B", "text.body", TextAlignmentOptions.Center, 19f);
            r.B.color = Body;
            r.A = Txt(r.Rt, "A", "text.body", TextAlignmentOptions.TopLeft, 22f);
            r.A.textWrappingMode = TextWrappingModes.Normal;
            r.A.color = Body;
            r.A.richText = false; // entry text is data, never markup
            r.B.richText = false;
            return r;
        }

        // ---------------------------------------------------------------- widgets

        private static Bar NewBar(RectTransform parent, string name, Action<float> drag, Action<float> wheel)
        {
            var b = new Bar { Grip = NewRect(name, parent) };
            var input = HitArea(b.Grip, name + "Hit");
            input.Drag = drag;
            input.Wheel = wheel;
            b.Track = Img(b.Grip, "Track", "scrollbar.track");
            b.Thumb = Img(b.Grip, "Thumb", "scrollbar.handle");
            return b;
        }

        // Thumb height = share of the track (view / content), position = pos / max; hidden with nothing to scroll.
        private static void SetBar(Bar b, int pos, int max, float share)
        {
            SetActive(b.Track, max > 0);
            SetActive(b.Thumb, max > 0);
            if (max <= 0) return;
            var h = b.Grip.rect.height;
            var w = ThemeTokens.GetFloat("size.scrollbar.w", 16f) * 0.6f;
            var x = (b.Grip.rect.width - w) / 2f;
            var thumbH = Mathf.Max(40f, h * Mathf.Clamp01(share));
            Put(b.Track, x, 0f, w, h);
            Put(b.Thumb, x, (h - thumbH) * Mathf.Clamp01(pos / (float)max), w, thumbH);
        }

        // Thin coloured rim inside a plate (mockup chips / tags / active preset).
        private static Image[] Rim(Transform host) => OutlineFrame.Build(host, 1.5f);

        // Translucent accent fill inside a plate (chips, tags, mod row hover): plain quad, no role (a role restyle keeps its
        // colour off).
        private static Image Fill(Transform host)
        {
            var img = new GameObject("Fill", typeof(RectTransform)).AddComponent<Image>();
            img.rectTransform.SetParent(host, false);
            img.rectTransform.SetSiblingIndex(0);
            Stretch(img.rectTransform, 2f, 2f, 2f, 2f);
            img.color = Color.clear;
            img.raycastTarget = false;
            DollCell.Mark(img, "content", "changelog chip / tag fill");
            return img;
        }

        // Changelog art (Codex, docs\mockups\2026-11-20-changelog-art, embedded): white pill rim + its fill mask, 48 px
        // tall, caps = half the height (9-slice); rune glyph; the glass gradient is a generated alpha ramp.
        private const float PillPx = 48f;
        private static Sprite _pillRim, _pillFill, _runeSprite, _shade;

        private static Sprite Art(string resource, Vector4 border)
        {
            var tex = Icons.ModStatusIcons.LoadEmbedded(resource, resource);
            return tex ? Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border) : null;
        }

        // Clear at the top, black 0.2 at the bottom (mockup 4a: ~8 % darker at the bottom, no edge vignette).
        private static Sprite Shade
        {
            get
            {
                if (_shade) return _shade;
                var tex = new Texture2D(1, 32, TextureFormat.RGBA32, false) { name = "UI_ChangelogShade", wrapMode = TextureWrapMode.Clamp };
                for (var y = 0; y < 32; y++) tex.SetPixel(0, y, new Color(0f, 0f, 0f, 0.2f * (1f - y / 31f)));
                tex.Apply(false, true);
                return _shade = Sprite.Create(tex, new Rect(0f, 0f, 1f, 32f), new Vector2(0.5f, 0.5f), 100f);
            }
        }

        // Pill over the whole host: fill under the rim, both under the host's other children; tinted in TintPill.
        private static (Image fill, Image rim) Pill(Transform host)
        {
            if (!_pillRim) _pillRim = Art("Morgott.UI.ChangelogPill.png", Vector4.one * (PillPx / 2f));
            if (!_pillFill) _pillFill = Art("Morgott.UI.ChangelogPillFill.png", Vector4.one * (PillPx / 2f));
            var rim = PillPart(host, "PillRim", _pillRim);
            var fill = PillPart(host, "PillFill", _pillFill);
            return (fill, rim);
        }

        private static Image PillPart(Transform host, string name, Sprite sprite)
        {
            var img = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
            img.rectTransform.SetParent(host, false);
            img.rectTransform.SetSiblingIndex(0);
            Stretch(img.rectTransform, 0f, 0f, 0f, 0f);
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.color = Color.clear;
            img.raycastTarget = false;
            DollCell.Mark(img, "content", "changelog pill " + name);
            return img;
        }

        // Rim in the colour, fill a dark tint of it when on (mockup 3a: dark plates keep the coloured text readable).
        // Caps stay round at any height: the multiplier maps the 48 px sprite onto the rect height.
        private static void TintPill(Image fill, Image rim, Color c, bool on)
        {
            var refPpu = rim.canvas ? rim.canvas.referencePixelsPerUnit : 100f;
            var mul = PillPx / Mathf.Max(rim.rectTransform.rect.height, 1f) * refPpu / 100f;
            foreach (var img in new[] { fill, rim })
                if (!Mathf.Approximately(img.pixelsPerUnitMultiplier, mul)) img.pixelsPerUnitMultiplier = mul;
            if (rim.color != c) rim.color = c;
            var f = on ? new Color(c.r * 0.28f, c.g * 0.28f, c.b * 0.28f, 0.92f) : Color.clear;
            if (fill.color != f) fill.color = f;
        }

        private static void SetRim(Image[] rim, Color c)
        {
            foreach (var l in rim) if (l.color != c) l.color = c;
        }

        private void Label(Transform parent, string name, string token, float y)
        {
            var t = Txt(parent, name, "text.header", TextAlignmentOptions.Left, 26f);
            t.text = L(token);
            t.color = ThemeTokens.GetColor("color.text.header", Hex("#D9DCE0"));
            _serif.Add(t);
            Put(t, SideX, y, SideW, 34f);
        }

        private TMP_InputField DateField(Transform parent, string name, string prefixToken, float x, float w, bool from)
        {
            // Prefix "с" / "по" / "from" at a fixed width: measured width was 0 at menu build (run 20261116-122424).
            var f = InputBox(parent, name, L(prefixToken), 32f, null);
            Put(f, x, ListY + 40f, w, 36f);
            f.characterLimit = 10;
            f.onEndEdit.AddListener(s =>
            {
                if (ChangelogModel.TryParseDay(s, out var d))
                {
                    if (from) _filter.From = d; else _filter.To = d;
                    _preset = -1;
                    _scroll = 0;
                    _dirty = true;
                }
                else SetField(f, from ? _filter.From : _filter.To); // invalid: back to the current value
            });
            return f;
        }

        // Field plate with an optional fixed-width prefix and an optional placeholder (LOC token).
        private static TMP_InputField InputBox(Transform parent, string name, string prefix, float prefixW, string placeholderToken)
        {
            var back = Img(parent, name, "field", true);
            if (prefixW > 0f)
            {
                var pre = Txt(back.transform, "Prefix", "text.body", TextAlignmentOptions.Left, 19f);
                pre.text = prefix;
                pre.overflowMode = TextOverflowModes.Overflow;
                Column(pre, 12f, prefixW, 0f, 0f);
            }
            var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            area.SetParent(back.rectTransform, false);
            Stretch(area, 12f + (prefixW > 0f ? prefixW + 6f : 0f), 0f, 10f, 0f);
            var text = Txt(area, "Text", "text.body", TextAlignmentOptions.Left, 19f);
            Stretch(text.rectTransform, 0f, 0f, 0f, 0f);
            var f = back.gameObject.AddComponent<TMP_InputField>();
            f.textViewport = area;
            f.textComponent = text;
            if (placeholderToken != null)
            {
                var ph = Txt(area, "Placeholder", "text.muted", TextAlignmentOptions.Left, 19f);
                ph.text = L(placeholderToken);
                ph.fontStyle = FontStyles.Italic;
                ph.color = Muted;
                Stretch(ph.rectTransform, 0f, 0f, 0f, 0f);
                f.placeholder = ph;
            }
            f.targetGraphic = back;
            f.transition = Selectable.Transition.None;
            f.navigation = new Navigation { mode = Navigation.Mode.None };
            return f;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var rt = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rt.SetParent(parent, false);
            return rt;
        }

        private static Input HitArea(RectTransform host, string name)
        {
            var hit = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
            hit.rectTransform.SetParent(host, false);
            hit.rectTransform.SetAsFirstSibling();
            Stretch(hit.rectTransform, 0f, 0f, 0f, 0f);
            hit.color = Color.clear;
            hit.raycastTarget = true;
            DollCell.Mark(hit, "content", "changelog scroll hit area");
            // On the host: wheel / press events bubble up from the hit area and from raycast rows inside it (mod rows).
            return host.gameObject.AddComponent<Input>();
        }

        private static Image Rule(Transform parent, string name, Color color)
        {
            var img = Img(parent, name, "ib.rule");
            img.enabled = true;
            img.color = color;
            return img;
        }

        private static Image Img(Transform parent, string name, string role, bool raycast = false)
        {
            var image = InfoArt.OwnImage(parent, name, role, "changelog " + name);
            image.preserveAspect = role.StartsWith("toggle", StringComparison.Ordinal) || role == "dropdown.arrow";
            image.raycastTarget = raycast;
            return image;
        }

        private static TMP_Text Txt(Transform parent, string name, string role, TextAlignmentOptions align, float size)
        {
            var t = InfoArt.RowText(parent, name, role, align);
            OwnFit.Claim(t);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.enableAutoSizing = false;
            t.fontSize = size;
            return t;
        }

        // role null: clear hit plate (own art drawn inside, e.g. the type pills).
        private static Button Btn(Transform parent, string name, string role, out TMP_Text label, Action click)
        {
            var image = role != null ? InfoArt.OwnImage(parent, name, role, "changelog button " + name) : NewRect(name, parent).gameObject.AddComponent<Image>();
            if (role == null)
            {
                image.color = Color.clear;
                DollCell.Mark(image, "content", "changelog button hit area " + name);
            }
            image.preserveAspect = false;
            image.raycastTarget = true;
            var b = image.gameObject.AddComponent<Button>();
            b.targetGraphic = image;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.onClick.AddListener(() => click());
            // Styled after the Button exists: the role's sprite states go on its targetGraphic (BrowserPanel.Btn).
            if (role != null && image.GetComponent<SkinMark>() is SkinMark m) m.Outcome = Restyler.Apply(image, m, ThemeTokens.Role(role)) ? Outcome.Styled : Outcome.Degraded;
            label = Txt(image.transform, "Label", "text.body", TextAlignmentOptions.Center, 18f);
            Stretch(label.rectTransform, 4f, 0f, 4f, 0f);
            return b;
        }

        private static void Off(Component c)
        {
            if (c && c.gameObject.activeSelf) c.gameObject.SetActive(false);
        }

        // Top-left box (y down) from an anchor on the parent's top edge: ax 0 = left, 0.5 = centre, 1 = right.
        private static void Put(Component c, float x, float y, float w, float h, float ax = 0f)
        {
            var rt = (RectTransform)c.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(ax, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        // Fixed-width column at x from an anchor on the left (ax 0) or right (ax 1) edge, stretched between top and bottom insets.
        private static void Column(Component c, float x, float w, float top, float bottom, float ax = 0f)
        {
            var rt = (RectTransform)c.transform;
            rt.anchorMin = new Vector2(ax, 0f);
            rt.anchorMax = new Vector2(ax, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.offsetMin = new Vector2(x, bottom);
            rt.offsetMax = new Vector2(x + w, -top);
        }

        // Fixed-height band at y from the top, stretched between left and right insets.
        private static void Band(Component c, float left, float right, float y, float h)
        {
            var rt = (RectTransform)c.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0f, 1f);
            rt.offsetMin = new Vector2(left, -y - h);
            rt.offsetMax = new Vector2(-right, -y);
        }

        private static void Stretch(RectTransform rt, float left, float top, float right, float bottom)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        public void Revert()
        {
            foreach (var v in _vanilla)
            {
                if (!v.rt) continue;
                v.rt.anchorMin = v.aMin;
                v.rt.anchorMax = v.aMax;
                v.rt.offsetMin = v.oMin;
                v.rt.offsetMax = v.oMax;
                v.rt.gameObject.SetActive(v.active);
            }
            if (_window)
            {
                _window.anchorMin = _vanillaMin;
                _window.anchorMax = _vanillaMax;
                _window.pivot = _vanillaPivot;
                _window.anchoredPosition = _vanillaPos;
                _window.sizeDelta = _vanillaSize;
            }
            if (_addedRaycaster && _window && _window.TryGetComponent<GraphicRaycaster>(out var gr)) Destroy(gr);
            if (_addedCanvas && _canvas) Destroy(_canvas);
            else if (_canvas) (_canvas.overrideSorting, _canvas.sortingLayerID, _canvas.sortingOrder) = _canvasOrig;
            if (_scrim) Destroy(_scrim);
            if (_root) Destroy(_root.gameObject);
            Destroy(this);
        }
    }
}
