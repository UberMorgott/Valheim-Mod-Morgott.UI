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
    // Pack changelog inside the vanilla changelog window (FejdStartup.m_changeLog; approved mockup
    // docs\mockups\2026-11-16-changelog\02-sidebar.png, style-sheet.md there): a modal window centred on the screen over a
    // dimmed backdrop (click outside the window, the close button or Esc closes it). Header "PACK CHANGES", tabs Pack
    // (ours) | Valheim (the vanilla ScrollPanel + PatchlogScroll, text unchanged). Pack tab: sidebar period / type / mod
    // filters, a virtualized list (scroll position = first row index, only the rows in view are laid out and measured,
    // pooled per row kind) and the "Shown X of Y" footer. Data ChangelogModel (BepInEx\config\Morgott.UI\changelog.json):
    // parsed and laid out once while the menu builds (Init), on open only when the file changed, so the open frame does
    // no parsing (run 20261116-122424: 70 ms open hitch with the parse in the open frame). The vanilla "new" badge also
    // follows that file (MainMenuAdapter GetChangelogHash postfix, FileHash). Layout in window units = 1080p canvas
    // units, measured from the mockup (mockup px x 1.148).
    [DisallowMultipleComponent]
    public sealed class ChangelogPanel : MonoBehaviour, IRevertible
    {
        public const string RootName = "MorgottChangelog";
        public const string ScrimName = "MorgottChangelogScrim";
        public static string FilePath => Path.Combine(BepInEx.Paths.ConfigPath, "Morgott.UI", "changelog.json");

        // Layout (window units, from the window's top-left).
        private const float W = 1050f, H = 676f;
        private const float SideX = 36f, SideW = 260f, ListX = 342f, ListY = 124f, ListW = 641f, ListBottom = 616f;
        private const float DateH = 48f, ModH = 38f, LineH = 37f, TagH = 24f, TagGap = 12f, ModRowH = 23f, ModListY = 530f;
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

        // data
        private List<ChangelogEntry> _all = new List<ChangelogEntry>();
        private List<string> _mods = new List<string>();
        private string _error;
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
        private Image _underline, _thumb, _track;
        private TMP_InputField _from, _to;
        private readonly Button[] _presets = new Button[3];
        private readonly Image[][] _presetRims = new Image[3][];
        private readonly TMP_Text[] _presetLabels = new TMP_Text[3];
        private readonly Button[] _chips = new Button[6];
        private readonly Image[][] _chipRims = new Image[6][];
        private readonly TMP_Text[] _chipLabels = new TMP_Text[6], _chipCounts = new TMP_Text[6], _chipDots = new TMP_Text[6];
        private readonly Pool _dates = new Pool(), _modHeads = new Pool(), _lines = new Pool();
        private readonly List<ModRow> _modRows = new List<ModRow>();

        private sealed class Row
        {
            public RectTransform Rt;
            public TMP_Text A, B;
            public Image Tag, Rule;
            public Image[] Rim;
        }

        private sealed class Pool
        {
            public readonly List<Row> Items = new List<Row>();
            public int Used;
        }

        private sealed class ModRow
        {
            public Button Box;
            public Image Check;
            public TMP_Text Label;
            public string Mod;
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

        // ---------------------------------------------------------------- build

        private void Init()
        {
            // Modal, centred on the screen (mockup 02): the vanilla window sat at a fixed spot left of the centre.
            _vanillaMin = _window.anchorMin;
            _vanillaMax = _window.anchorMax;
            _vanillaPivot = _window.pivot;
            _vanillaPos = _window.anchoredPosition;
            _vanillaSize = _window.sizeDelta;
            _window.anchorMin = _window.anchorMax = _window.pivot = new Vector2(0.5f, 0.5f);
            _window.anchoredPosition = Vector2.zero;
            _window.sizeDelta = new Vector2(ThemeTokens.GetFloat("size.changelog.w", W), ThemeTokens.GetFloat("size.changelog.h", H));
            _topic = _window.Find("Topic") as RectTransform;
            _scrollPanel = _window.Find("ScrollPanel") as RectTransform;
            _scrollbar = _window.TryGetComponent<ChangeLog>(out var log) && log.m_scrollbar ? (RectTransform)log.m_scrollbar.transform : _window.Find("PatchlogScroll") as RectTransform;
            foreach (var rt in new[] { _topic, _scrollPanel, _scrollbar })
                if (rt) _vanilla.Add((rt, rt.anchorMin, rt.anchorMax, rt.offsetMin, rt.offsetMax, rt.gameObject.activeSelf));
            // Valheim tab: the vanilla text fills the content area under the tabs, its scrollbar at the right edge.
            if (_scrollPanel) Stretch(_scrollPanel, 40f, ListY + 6f, 64f, 40f);
            if (_scrollbar)
            {
                _scrollbar.anchorMin = new Vector2(1f, 0f);
                _scrollbar.anchorMax = Vector2.one;
                _scrollbar.pivot = new Vector2(1f, 0.5f);
                _scrollbar.offsetMin = new Vector2(-36f - ThemeTokens.GetFloat("size.scrollbar.w", 16f), 40f);
                _scrollbar.offsetMax = new Vector2(-36f, -(ListY + 6f));
            }

            // Backdrop: first child, reaching far past the window over the whole screen, drawn under the window.
            var scrim = new GameObject(ScrimName, typeof(RectTransform)).AddComponent<Image>();
            scrim.rectTransform.SetParent(_window, false);
            scrim.rectTransform.SetAsFirstSibling();
            Stretch(scrim.rectTransform, -6000f, -6000f, -6000f, -6000f);
            scrim.color = ThemeTokens.GetColor("color.changelog.scrim", new Color(0.016f, 0.027f, 0.039f, 0.9f));
            scrim.raycastTarget = true;
            DollCell.Mark(scrim, "content", "changelog modal backdrop");
            scrim.gameObject.AddComponent<Backdrop>().Click = e =>
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(_window, e.position, e.pressEventCamera)) return;
                if (_fejd && _fejd.m_changeLog && _fejd.m_changeLog.activeInHierarchy) _fejd.OnButtonShowChangelog();
            };
            _scrim = scrim.gameObject;

            _root = NewRect(RootName, _window);
            Stretch(_root, 0f, 0f, 0f, 0f);
            // Below the close button (ChangelogClose adds it as the last sibling).
            if (_window.Find(ChangelogClose.ButtonName) is Transform close) _root.SetSiblingIndex(close.GetSiblingIndex());

            _title = Txt(_root, "Title", "window.title", TextAlignmentOptions.Center, 31f);
            _title.characterSpacing = 14f;
            _title.text = L("$ui_changelog_title");
            _serif.Add(_title);
            Put(_title, 120f, 12f, W - 240f, 40f);

            _tabPack = Btn(_root, "TabPack", "tab", out _tabPackLabel, () => { _gameTab = false; _dirty = true; });
            _tabGame = Btn(_root, "TabGame", "tab", out _tabGameLabel, () => { _gameTab = true; _dirty = true; });
            _tabPackLabel.text = L("$ui_changelog_tab_pack");
            _tabGameLabel.text = "Valheim";
            foreach (var t in new[] { _tabPackLabel, _tabGameLabel }) { t.fontSize = ThemeTokens.GetFloat("size.startgame.tab.font", 21f) + 1f; _serif.Add(t); }
            Put(_tabPack, 52f, 62f, 232f, 42f);
            Put(_tabGame, 300f, 62f, 222f, 42f);
            _underline = Rule(_root, "TabUnderline", Hex("#4FE0E6"));
            var band = Rule(_root, "TabRule", Hex("#2395B7"));
            Put(band, 28f, 108f, W - 56f, 1f);

            _pack = NewRect("Pack", _root);
            Stretch(_pack, 0f, 0f, 0f, 0f);
            BuildSidebar();
            BuildList();
            // Own canvas above the menu column and the bottom-left links (as SidePanel does), added now: a canvas added on
            // the open frame re-batched the whole window there (run 20261116-125447: 101 ms open frame).
            // The window may carry a canvas of its own that does not override sorting: then the menu texts after it in the
            // hierarchy (StartGui/Menu/modded_text, BepInEx Version) drew over the backdrop (run 20261116-131125). Override
            // sorting either way (originals kept for Revert).
            var rootCanvas = _window.parent ? _window.parent.GetComponentInParent<Canvas>() : null;
            if (!_window.TryGetComponent(out _canvas))
            {
                _canvas = _window.gameObject.AddComponent<Canvas>();
                _addedCanvas = true;
            }
            else _canvasOrig = (_canvas.overrideSorting, _canvas.sortingLayerID, _canvas.sortingOrder);
            if (!_window.GetComponent<GraphicRaycaster>()) { _window.gameObject.AddComponent<GraphicRaycaster>(); _addedRaycaster = true; }
            if (rootCanvas)
            {
                _canvas.overrideSorting = true;
                _canvas.sortingLayerID = rootCanvas.rootCanvas.sortingLayerID;
                _canvas.sortingOrder = rootCanvas.rootCanvas.sortingOrder + 20;
            }
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
            Label(p, "PeriodLabel", "$ui_changelog_period", 118f);
            _from = DateField(p, "From", "$ui_changelog_from", 152f, true);
            _to = DateField(p, "To", "$ui_changelog_to", 187f, false);
            string[] presetKeys = { "$ui_changelog_days7", "$ui_changelog_days30", "$ui_changelog_all" };
            float[] px = { 0f, 88f, 177f };
            for (var i = 0; i < 3; i++)
            {
                var n = i;
                _presets[i] = Btn(p, "Preset" + i, "field", out _presetLabels[i], () => { _preset = n; ApplyPreset(); });
                _presetRims[i] = Rim(_presets[i].transform);
                _presetLabels[i].text = L(presetKeys[i]);
                _presetLabels[i].fontSize = 16f;
                Put(_presets[i], SideX + px[i], 224f, 82f, 28f);
            }
            Label(p, "TypeLabel", "$ui_changelog_type", 258f);
            for (var i = 0; i < 6; i++)
            {
                var n = i;
                // Mockup chip: dark glass pill (field plate), thin rim in the type colour, coloured dot, light label,
                // count in the type colour.
                _chips[i] = Btn(p, "Type" + i, "field", out _chipLabels[i], () => { _filter.Types[n] = !_filter.Types[n]; _scroll = 0; _dirty = true; });
                _chipRims[i] = Rim(_chips[i].transform);
                _chipLabels[i].alignment = TextAlignmentOptions.Left;
                _chipLabels[i].text = L("$ui_changelog_type_" + ChangelogModel.Types[i]);
                _chipLabels[i].fontSize = 16f;
                Stretch(_chipLabels[i].rectTransform, 30f, 0f, 50f, 0f);
                _chipDots[i] = Txt(_chips[i].transform, "Dot", "text.body", TextAlignmentOptions.Center, 16f);
                _chipDots[i].text = "●";
                Put(_chipDots[i], 6f, 0f, 20f, 25f);
                _chipCounts[i] = Txt(_chips[i].transform, "Count", "text.body", TextAlignmentOptions.Right, 16f);
                Put(_chipCounts[i], SideW - 70f, 0f, 60f, 25f);
                Put(_chips[i], SideX, 288f + i * 29.4f, SideW, 25f);
            }
            Label(p, "ModLabel", "$ui_changelog_mod", 464f);
            var all = Btn(p, "AllMods", "dropdown.field", out var allLabel, () =>
            {
                if (_filter.HiddenMods.Count > 0) _filter.HiddenMods.Clear();
                else foreach (var m in _mods) _filter.HiddenMods.Add(m);
                _scroll = 0;
                _dirty = true;
            });
            allLabel.text = L("$ui_changelog_allmods");
            allLabel.alignment = TextAlignmentOptions.Left;
            allLabel.fontSize = 16f;
            Stretch(allLabel.rectTransform, 12f, 0f, 30f, 0f);
            var arrow = Img(all.transform, "Arrow", "dropdown.arrow");
            Put(arrow, SideW - 26f, 9f, 12f, 12f);
            Put(all, SideX, 494f, SideW, 30f);
            _modList = NewRect("ModList", p);
            Put(_modList, SideX, ModListY, SideW, ListBottom - ModListY + 4f);
            HitArea(_modList, "ModListHit").Wheel = d => { _modScroll -= Math.Sign(d); _dirty = true; };
            var vrule = Rule(p, "SideRule", Hex("#3D8290"));
            Put(vrule, 316f, ListY, 1f, ListBottom - ListY + 4f);
        }

        private void BuildList()
        {
            _list = NewRect("List", _pack);
            _list.gameObject.AddComponent<RectMask2D>();
            Put(_list, ListX, ListY, ListW, ListBottom - ListY);
            HitArea(_list, "ListHit").Wheel = d => { _scroll -= 3 * Math.Sign(d); _dirty = true; };
            _track = Img(_pack, "ScrollTrack", "scrollbar.track");
            _thumb = Img(_pack, "ScrollThumb", "scrollbar.handle");
            var grip = NewRect("ScrollGrip", _pack);
            Put(grip, 996f, ListY, 30f, ListBottom - ListY);
            var input = HitArea(grip, "ScrollGripHit");
            input.Drag = s => { _scroll = Mathf.RoundToInt(s * MaxScroll()); _dirty = true; };
            input.Wheel = d => { _scroll -= 3 * Math.Sign(d); _dirty = true; };
            _empty = Txt(_pack, "Empty", "text.muted", TextAlignmentOptions.Top, 18f);
            _empty.textWrappingMode = TextWrappingModes.Normal;
            _empty.fontStyle = FontStyles.Italic;
            Put(_empty, ListX + 20f, ListY + 40f, ListW - 40f, 120f);
            var foot = Rule(_pack, "FooterRule", Hex("#3D8290"));
            Put(foot, 28f, 622f, W - 56f, 1f);
            _footer = Txt(_pack, "Footer", "text.muted", TextAlignmentOptions.Left, 17f);
            _footer.richText = true;
            Put(_footer, 82f, 628f, 600f, 26f);
        }

        // ---------------------------------------------------------------- lifecycle

        private void OnEnable()
        {
            if (!_window || !_laidOut) return; // Init's own show / hide
            Stamp(); // FejdStartup.Start's ScreenRegistry.Restyle put the role faces back after Init (run 20261116-131125)
            // Re-laid on the first open, a changed file or a new day for the period presets; else shown as it was.
            if (_preset >= 0 && _preset < 2 && _filter.To != DateTime.Today) ApplyPreset();
            if (!_laidOut || FileChanged()) _dirty = true;
        }

        private void LateUpdate()
        {
            if (!_root) return;
            Enforce();
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
            Put(_underline, _gameTab ? 300f : 52f, 104f, _gameTab ? 222f : 232f, 3f);
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
                SetRim(_chipRims[i], on ? c : new Color(Muted.r, Muted.g, Muted.b, 0.5f));
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
        // title, tabs, section titles, dates, mod names), tab state colours. Cheap when unchanged; run on layout and open.
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
            var gold = ThemeTokens.GetColor("color.text.active", Hex("#E8B64A"));
            _tabPackLabel.color = _gameTab ? Body : gold;
            _tabGameLabel.color = _gameTab ? gold : Body;
        }

        private void LayoutMods()
        {
            var visible = Mathf.Max(1, Mathf.FloorToInt((ListBottom - ModListY) / ModRowH));
            _modScroll = Mathf.Clamp(_modScroll, 0, Mathf.Max(0, _mods.Count - visible));
            for (var i = 0; i < Mathf.Max(visible, _modRows.Count); i++)
            {
                var k = _modScroll + i;
                if (i >= visible || k >= _mods.Count)
                {
                    if (i < _modRows.Count) _modRows[i].Box.gameObject.SetActive(false);
                    continue;
                }
                if (i == _modRows.Count) _modRows.Add(NewModRow(i));
                var r = _modRows[i];
                r.Mod = _mods[k];
                r.Box.gameObject.SetActive(true);
                Put(r.Box, 10f, i * ModRowH + 2f, SideW - 10f, ModRowH);
                r.Check.enabled = !_filter.HiddenMods.Contains(r.Mod);
                r.Label.text = r.Mod;
                r.Label.color = _view.ModCounts.ContainsKey(r.Mod) ? Body : Muted;
            }
        }

        private ModRow NewModRow(int i)
        {
            var r = new ModRow();
            // Clear hit area over the row; box + check drawn at its left.
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
            var box = Img(hit.transform, "Box", "toggle.box");
            Put(box, 0f, 2f, 18f, 18f);
            r.Check = Img(box.transform, "Check", "toggle.check");
            Stretch(r.Check.rectTransform, 2f, 2f, 2f, 2f);
            r.Label = Txt(hit.transform, "Label", "text.body", TextAlignmentOptions.Left, 16f);
            Put(r.Label, 30f, 0f, SideW - 50f, ModRowH);
            return r;
        }

        // Rows from _scroll down to the list bottom; each line row measured (wrapped text) only when shown.
        private void LayoutRows()
        {
            var rows = _view.Rows;
            _scroll = Mathf.Clamp(_scroll, 0, MaxScroll());
            _dates.Used = _modHeads.Used = _lines.Used = 0;
            var russian = Russian;
            var y = 0f;
            var viewH = ListBottom - ListY;
            for (var i = _scroll; i < rows.Count && y < viewH; i++) y += Place(rows[i], y, russian);
            foreach (var pool in new[] { _dates, _modHeads, _lines })
                for (var i = pool.Used; i < pool.Items.Count; i++) Off(pool.Items[i].Rt);
            // Scrollbar: thumb share = rows in view / all rows.
            var show = rows.Count > 0 && MaxScroll() > 0;
            _track.gameObject.SetActive(show);
            _thumb.gameObject.SetActive(show);
            if (!show) return;
            var trackW = ThemeTokens.GetFloat("size.scrollbar.w", 16f) * 0.6f;
            Put(_track, 1008f - trackW / 2f, ListY, trackW, viewH);
            var thumbH = Mathf.Max(40f, viewH * Mathf.Clamp01(viewH / LineH / rows.Count));
            Put(_thumb, 1008f - trackW / 2f, ListY + (viewH - thumbH) * _scroll / Mathf.Max(1, MaxScroll()), trackW, thumbH);
        }

        // Last first-row index that still fills the view (unwrapped heights from the end).
        private int MaxScroll()
        {
            if (_view == null) return 0;
            var rows = _view.Rows;
            var h = 0f;
            var viewH = ListBottom - ListY;
            for (var i = rows.Count - 1; i >= 0; i--)
            {
                h += rows[i].Kind == ChangelogRowKind.Date ? DateH : rows[i].Kind == ChangelogRowKind.Mod ? ModH : LineH;
                if (h > viewH) return Mathf.Min(rows.Count - 1, i + 1);
            }
            return 0;
        }

        // Data text inside a rich-text line (mod name + version): no tag can open.
        private static string Plain(string s) => s.Replace('<', '‹');

        private float Place(ChangelogRow row, float y, bool russian)
        {
            var e = row.Entry;
            switch (row.Kind)
            {
                case ChangelogRowKind.Date:
                    {
                        var r = Next(_dates, NewDateRow);
                        Put(r.Rt, 0f, y, ListW, DateH);
                        r.A.text = ChangelogModel.DateHeader(e.Date, russian);
                        var w = r.A.GetPreferredValues(r.A.text).x;
                        Put(r.A, 0f, 6f, w + 4f, 34f);
                        Put(r.Rule, w + 18f, 24f, Mathf.Max(0f, ListW - w - 26f), 1f);
                        return DateH;
                    }
                case ChangelogRowKind.Mod:
                    {
                        // One text: the version follows the name at a fixed gap (mockup "Morgott.UI 0.29.40").
                        var r = Next(_modHeads, NewModHead);
                        Put(r.Rt, 0f, y, ListW, ModH);
                        r.A.text = e.Version.Length > 0 ? $"{Plain(e.Mod)}<space=10><color=#8AB4F0>{Plain(e.Version)}</color>" : Plain(e.Mod);
                        return ModH;
                    }
                default:
                    {
                        var r = Next(_lines, NewLineRow);
                        var c = TypeColor(e.Type);
                        SetRim(r.Rim, c);
                        r.B.text = L("$ui_changelog_type_" + ChangelogModel.Types[e.Type]);
                        var tagW = r.B.GetPreferredValues(r.B.text).x + 22f;
                        r.A.text = e.Text(russian);
                        var textX = 2f + tagW + TagGap;
                        var textW = ListW - textX - 8f;
                        var h = Mathf.Max(LineH, r.A.GetPreferredValues(r.A.text, textW, 0f).y + 12f);
                        Put(r.Rt, 0f, y, ListW, h);
                        Put(r.Tag, 2f, 6f, tagW, TagH);
                        Put(r.B, 2f, 6f, tagW, TagH);
                        Put(r.A, textX, 5f, textW, h - 6f);
                        return h;
                    }
            }
        }

        private Row Next(Pool pool, Func<int, Row> make)
        {
            var r = pool.Used < pool.Items.Count ? pool.Items[pool.Used] : null;
            if (r == null) pool.Items.Add(r = make(pool.Items.Count));
            pool.Used++;
            if (!r.Rt.gameObject.activeSelf) r.Rt.gameObject.SetActive(true);
            return r;
        }

        private Row NewDateRow(int n)
        {
            var r = new Row { Rt = NewRect("Date" + n, _list) };
            r.A = Txt(r.Rt, "A", "text.header", TextAlignmentOptions.Left, 24f);
            r.A.color = ThemeTokens.GetColor("color.text.header", Hex("#D9DCE0"));
            r.A.richText = false;
            _serif.Add(r.A);
            if (ThemeTokens.GetFont("font.map.header") is TMP_FontAsset f) r.A.font = f;
            r.Rule = Rule(r.Rt, "Rule", Hex("#3D8290"));
            return r;
        }

        private Row NewModHead(int n)
        {
            var r = new Row { Rt = NewRect("ModHead" + n, _list) };
            r.A = Txt(r.Rt, "A", "text.header", TextAlignmentOptions.Left, 24f);
            r.A.color = ThemeTokens.GetColor("color.text.item.title", Hex("#F0C419"));
            r.A.richText = true; // name + version colour; the data parts go through Plain
            _serif.Add(r.A);
            if (ThemeTokens.GetFont("font.map.header") is TMP_FontAsset f) r.A.font = f;
            Put(r.A, 0f, 2f, ListW, 34f);
            return r;
        }

        private Row NewLineRow(int n)
        {
            var r = new Row { Rt = NewRect("Line" + n, _list) };
            r.Tag = Img(r.Rt, "Tag", "field");
            r.Rim = Rim(r.Tag.transform);
            r.B = Txt(r.Rt, "B", "text.body", TextAlignmentOptions.Center, 16f);
            r.B.color = Body;
            r.A = Txt(r.Rt, "A", "text.body", TextAlignmentOptions.TopLeft, 17f);
            r.A.textWrappingMode = TextWrappingModes.Normal;
            r.A.color = Body;
            r.A.richText = false; // entry text is data, never markup
            r.B.richText = false;
            return r;
        }

        // ---------------------------------------------------------------- widgets

        // Thin coloured rim inside a plate (mockup chips / tags / active preset): four 1.5-unit lines, corners left open
        // for the plate's chamfer.
        private static Image[] Rim(Transform host)
        {
            var rim = new Image[4];
            for (var i = 0; i < 4; i++)
            {
                rim[i] = Rule(host, "Rim" + i, Color.clear);
                var rt = rim[i].rectTransform;
                var horizontal = i < 2;
                var edge = i == 0 || i == 2 ? 1f : 0f; // top / left = 1 / 0 anchor
                rt.anchorMin = horizontal ? new Vector2(0f, edge) : new Vector2(i == 3 ? 1f : 0f, 0f);
                rt.anchorMax = horizontal ? new Vector2(1f, edge) : new Vector2(i == 3 ? 1f : 0f, 1f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.offsetMin = horizontal ? new Vector2(4f, i == 0 ? -2f : 0.5f) : new Vector2(i == 3 ? -2f : 0.5f, 4f);
                rt.offsetMax = horizontal ? new Vector2(-4f, i == 0 ? -0.5f : 2f) : new Vector2(i == 3 ? -0.5f : 2f, -4f);
            }
            return rim;
        }

        private static void SetRim(Image[] rim, Color c)
        {
            foreach (var l in rim) if (l.color != c) l.color = c;
        }

        private void Label(Transform parent, string name, string token, float y)
        {
            var t = Txt(parent, name, "text.header", TextAlignmentOptions.Left, 22f);
            t.text = L(token);
            t.color = ThemeTokens.GetColor("color.text.header", Hex("#D9DCE0"));
            _serif.Add(t);
            Put(t, SideX, y, SideW, 30f);
        }

        private TMP_InputField DateField(Transform parent, string name, string prefixToken, float y, bool from)
        {
            const float PrefixW = 26f; // "с" / "по" / "from": measured width was 0 at menu build (run 20261116-122424)
            var back = Img(parent, name, "field", true);
            Put(back, SideX, y, SideW, 30f);
            var prefix = Txt(back.transform, "Prefix", "text.body", TextAlignmentOptions.Left, 16f);
            prefix.text = L(prefixToken);
            prefix.overflowMode = TextOverflowModes.Overflow;
            Put(prefix, 12f, 0f, PrefixW, 30f);
            var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            area.SetParent(back.rectTransform, false);
            Stretch(area, 12f + PrefixW + 6f, 0f, 10f, 0f);
            var text = Txt(area, "Text", "text.body", TextAlignmentOptions.Left, 16f);
            Stretch(text.rectTransform, 0f, 0f, 0f, 0f);
            var f = back.gameObject.AddComponent<TMP_InputField>();
            f.textViewport = area;
            f.textComponent = text;
            f.targetGraphic = back;
            f.transition = Selectable.Transition.None;
            f.navigation = new Navigation { mode = Navigation.Mode.None };
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

        private static Button Btn(Transform parent, string name, string role, out TMP_Text label, Action click)
        {
            var image = InfoArt.OwnImage(parent, name, role, "changelog button " + name);
            image.preserveAspect = false;
            image.raycastTarget = true;
            var b = image.gameObject.AddComponent<Button>();
            b.targetGraphic = image;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.onClick.AddListener(() => click());
            // Styled after the Button exists: the role's sprite states go on its targetGraphic (BrowserPanel.Btn).
            if (image.GetComponent<SkinMark>() is SkinMark m) m.Outcome = Restyler.Apply(image, m, ThemeTokens.Role(role)) ? Outcome.Styled : Outcome.Degraded;
            label = Txt(image.transform, "Label", "text.body", TextAlignmentOptions.Center, 18f);
            Stretch(label.rectTransform, 4f, 0f, 4f, 0f);
            return b;
        }

        private static void Off(Component c)
        {
            if (c && c.gameObject.activeSelf) c.gameObject.SetActive(false);
        }

        // Top-left box in the parent's space (y down).
        private static void Put(Component c, float x, float y, float w, float h)
        {
            var rt = (RectTransform)c.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
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
