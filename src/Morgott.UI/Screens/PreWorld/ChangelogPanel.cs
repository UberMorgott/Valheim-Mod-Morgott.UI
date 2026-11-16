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
    // docs\mockups\2026-11-16-changelog\02-sidebar.png, style-sheet.md there). Header "PACK CHANGES", tabs Pack (ours) |
    // Valheim (the vanilla ScrollPanel + PatchlogScroll, text unchanged). Pack tab: sidebar period / type / mod filters, a
    // virtualized list (scroll position = first row index, only the rows in view are laid out and measured, pooled) and
    // the "Shown X of Y" footer. Data ChangelogModel (BepInEx\config\Morgott.UI\changelog.json, reread when it changed);
    // the vanilla "new" badge also follows that file (MainMenuAdapter GetChangelogHash postfix, FileHash). Layout in
    // window units = 1080p canvas units, measured from the mockup (mockup px x 1.148).
    [DisallowMultipleComponent]
    public sealed class ChangelogPanel : MonoBehaviour, IRevertible
    {
        public const string RootName = "MorgottChangelog";
        public static string FilePath => Path.Combine(BepInEx.Paths.ConfigPath, "Morgott.UI", "changelog.json");

        // Layout (window units, from the window's top-left).
        private const float W = 1050f, H = 676f;
        private const float SideX = 36f, SideW = 260f, ListX = 342f, ListY = 124f, ListW = 641f, ListBottom = 616f;
        private const float DateH = 48f, ModH = 38f, LineH = 37f, TagH = 24f, TagGap = 12f, ModRowH = 23f, ModListY = 530f;
        private static readonly string[] TypeColors = { "#9FD66B", "#E0413A", "#4FE0E6", "#E8B64A", "#8AB4F0", "#8E7BB8" };

        private FejdStartup _fejd;
        private RectTransform _window, _root, _pack, _list, _modList;
        private readonly List<(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax, bool active)> _vanilla = new List<(RectTransform, Vector2, Vector2, Vector2, Vector2, bool)>();
        private Vector2 _vanillaSize;
        private RectTransform _topic, _scrollPanel, _scrollbar;
        private bool _gameTab, _dirty = true;

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
        private TMP_Text _tabPackLabel, _tabGameLabel, _empty, _footer;
        private Image _underline, _thumb, _track;
        private TMP_InputField _from, _to;
        private readonly Button[] _presets = new Button[3];
        private readonly TMP_Text[] _presetLabels = new TMP_Text[3];
        private readonly Button[] _chips = new Button[6];
        private readonly Image[] _chipFills = new Image[6];
        private readonly TMP_Text[] _chipLabels = new TMP_Text[6], _chipCounts = new TMP_Text[6];
        private readonly List<Row> _rows = new List<Row>();
        private readonly List<ModRow> _modRows = new List<ModRow>();
        private int _rowsUsed;

        private sealed class Row
        {
            public RectTransform Rt;
            public TMP_Text A, B;
            public Image Tag, Rule;
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

        // The file's stable hash folded into the vanilla one (0 = no file: the vanilla badge alone).
        public static int FileHash()
        {
            try { return File.Exists(FilePath) ? File.ReadAllText(FilePath).GetStableHashCode() : 0; }
            catch (IOException) { return 0; }
            catch (UnauthorizedAccessException) { return 0; }
        }

        private static bool Russian => Localization.instance != null && Localization.instance.GetSelectedLanguage() == "Russian";
        private static string L(string token) => Localization.instance != null ? Localization.instance.Localize(token) : token;
        private static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;
        private static Color TypeColor(int t) => ThemeTokens.GetColor("color.changelog." + ChangelogModel.Types[t], Hex(TypeColors[t]));

        // ---------------------------------------------------------------- build

        private void Init()
        {
            _vanillaSize = _window.sizeDelta;
            if (Mathf.Approximately(_window.anchorMin.x, _window.anchorMax.x) && Mathf.Approximately(_window.anchorMin.y, _window.anchorMax.y))
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

            _root = NewRect(RootName, _window);
            Stretch(_root, 0f, 0f, 0f, 0f);
            // Below the close button (ChangelogClose adds it as the last sibling).
            if (_window.Find(ChangelogClose.ButtonName) is Transform close) _root.SetSiblingIndex(close.GetSiblingIndex());

            var title = Txt(_root, "Title", "window.title", TextAlignmentOptions.Center, 30f);
            title.characterSpacing = 18f;
            title.text = L("$ui_changelog_title");
            Put(title, 120f, 12f, W - 240f, 40f);

            _tabPack = Btn(_root, "TabPack", "tab", out _tabPackLabel, () => { _gameTab = false; _dirty = true; });
            _tabGame = Btn(_root, "TabGame", "tab", out _tabGameLabel, () => { _gameTab = true; _dirty = true; });
            _tabPackLabel.text = L("$ui_changelog_tab_pack");
            _tabGameLabel.text = "Valheim";
            foreach (var t in new[] { _tabPackLabel, _tabGameLabel }) { t.font = title.font; t.fontSize = ThemeTokens.GetFloat("size.startgame.tab.font", 21f); }
            Put(_tabPack, 52f, 62f, 232f, 42f);
            Put(_tabGame, 300f, 62f, 222f, 42f);
            _underline = Rule(_root, "TabUnderline", Hex("#4FE0E6"));
            var band = Rule(_root, "TabRule", Hex("#2395B7"));
            Put(band, 28f, 108f, W - 56f, 1f);

            _pack = NewRect("Pack", _root);
            Stretch(_pack, 0f, 0f, 0f, 0f);
            BuildSidebar();
            BuildList();
        }

        private void BuildSidebar()
        {
            var p = _pack;
            Label(p, "PeriodLabel", "$ui_changelog_period", 120f);
            _from = DateField(p, "From", "$ui_changelog_from", 152f, true);
            _to = DateField(p, "To", "$ui_changelog_to", 187f, false);
            string[] presetKeys = { "$ui_changelog_days7", "$ui_changelog_days30", "$ui_changelog_all" };
            float[] px = { 0f, 88f, 177f };
            for (var i = 0; i < 3; i++)
            {
                var n = i;
                _presets[i] = Btn(p, "Preset" + i, "field", out _presetLabels[i], () => { _preset = n; ApplyPreset(); });
                _presetLabels[i].text = L(presetKeys[i]);
                _presetLabels[i].fontSize = 16f;
                Put(_presets[i], SideX + px[i], 224f, 82f, 28f);
            }
            Label(p, "TypeLabel", "$ui_changelog_type", 260f);
            for (var i = 0; i < 6; i++)
            {
                var n = i;
                _chips[i] = Btn(p, "Type" + i, "field", out _chipLabels[i], () => { _filter.Types[n] = !_filter.Types[n]; _scroll = 0; _dirty = true; });
                _chipFills[i] = Img(_chips[i].transform, "Fill", "ib.rule");
                _chipFills[i].transform.SetAsFirstSibling();
                Stretch(_chipFills[i].rectTransform, 3f, 3f, 3f, 3f);
                _chipLabels[i].alignment = TextAlignmentOptions.Left;
                _chipLabels[i].text = L("$ui_changelog_type_" + ChangelogModel.Types[i]);
                _chipLabels[i].fontSize = 16f;
                Stretch(_chipLabels[i].rectTransform, 30f, 0f, 50f, 0f);
                var dot = Txt(_chips[i].transform, "Dot", "text.body", TextAlignmentOptions.Center, 16f);
                dot.text = "●";
                dot.color = TypeColor(i);
                Put(dot, 6f, 0f, 20f, 25f);
                _chipCounts[i] = Txt(_chips[i].transform, "Count", "text.body", TextAlignmentOptions.Right, 16f);
                _chipCounts[i].color = TypeColor(i);
                Put(_chipCounts[i], SideW - 70f, 0f, 60f, 25f);
                Put(_chips[i], SideX, 288f + i * 29.4f, SideW, 25f);
            }
            Label(p, "ModLabel", "$ui_changelog_mod", 466f);
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

        private void OnEnable() => _dirty = true;

        private void LateUpdate()
        {
            if (!_root) return;
            Enforce();
            if (!_dirty) return;
            _dirty = false;
            var sw = Stopwatch.StartNew();
            var loaded = Load();
            Layout();
            if (loaded) Plugin.Log.LogInfo($"[Morgott.UI] changelog: {_all.Count} entries, {_view?.Shown ?? 0} shown, load+layout {sw.Elapsed.TotalMilliseconds:F1} ms{(_error != null ? ", " + _error : "")}");
        }

        // Tab state: vanilla title always off (ours replaces it), vanilla text only on the Valheim tab.
        private void Enforce()
        {
            SetActive(_topic, false);
            SetActive(_scrollPanel, _gameTab);
            SetActive(_scrollbar, _gameTab);
            SetActive(_pack, !_gameTab);
        }

        private static void SetActive(Component c, bool on)
        {
            if (c && c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
        }

        // Reads the file when it changed (stamp + size); true when it was (re)read.
        private bool Load()
        {
            var path = FilePath;
            DateTime stamp;
            long size;
            try
            {
                var fi = new FileInfo(path);
                stamp = fi.Exists ? fi.LastWriteTimeUtc : DateTime.MinValue;
                size = fi.Exists ? fi.Length : -1;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { stamp = DateTime.MinValue; size = -1; }
            if (stamp == _stamp && size == _size && _view != null) return false;
            _stamp = stamp;
            _size = size;
            _all = new List<ChangelogEntry>();
            _error = null;
            if (size < 0) _error = "no file";
            else
            {
                try { _all = ChangelogModel.Parse(File.ReadAllText(path), out _error) ?? new List<ChangelogEntry>(); }
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
            // Tabs: selected = not interactable (tab role: disabled sprite = selected plate), gold label + cyan underline.
            _tabPack.interactable = _gameTab;
            _tabGame.interactable = !_gameTab;
            var gold = ThemeTokens.GetColor("color.text.active", Hex("#E8B64A"));
            var body = ThemeTokens.GetColor("color.text.body", Hex("#E3E6E8"));
            _tabPackLabel.color = _gameTab ? body : gold;
            _tabGameLabel.color = _gameTab ? gold : body;
            Put(_underline, _gameTab ? 300f : 52f, 104f, _gameTab ? 222f : 232f, 3f);
            if (_gameTab) return;

            _view = ChangelogModel.Build(_all, _filter);
            var muted = ThemeTokens.GetColor("color.state.disabled", Hex("#8A9096"));
            for (var i = 0; i < 3; i++) Selected(_presets[i], _presetLabels[i], _preset == i, Hex("#4FE0E6"));
            for (var i = 0; i < 6; i++)
            {
                var on = _filter.Types[i];
                var c = TypeColor(i);
                ((Image)_chips[i].targetGraphic).color = on ? c : muted;
                _chipFills[i].color = on ? new Color(c.r, c.g, c.b, 0.22f) : Color.clear;
                _chipLabels[i].color = on ? body : muted;
                _chipCounts[i].text = _view.TypeCounts[i].ToString();
                _chipCounts[i].color = on ? c : muted;
            }
            LayoutMods(body, muted);
            LayoutRows();
            _footer.text = string.Format(L("$ui_changelog_shown"), $"<color=#E3E6E8>{ChangelogModel.Count(_view.Shown)}</color>", $"<color=#E3E6E8>{ChangelogModel.Count(_view.Total)}</color>");
            _empty.text = _error != null ? L("$ui_changelog_empty_file") : _view.Shown == 0 ? L("$ui_changelog_empty_filter") : "";
            _empty.gameObject.SetActive(_empty.text.Length > 0);
        }

        private static void Selected(Button b, TMP_Text label, bool on, Color accent)
        {
            ((Image)b.targetGraphic).color = on ? accent : Color.white;
            label.color = on ? ThemeTokens.GetColor("color.text.body", Color.white) : ThemeTokens.GetColor("color.state.disabled", Color.gray);
        }

        private void LayoutMods(Color body, Color muted)
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
                r.Label.color = _view.ModCounts.ContainsKey(r.Mod) ? body : muted;
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
            _rowsUsed = 0;
            var russian = Russian;
            var y = 0f;
            var viewH = ListBottom - ListY;
            for (var i = _scroll; i < rows.Count && y < viewH; i++) y += Place(rows[i], y, russian);
            for (var i = _rowsUsed; i < _rows.Count; i++) if (_rows[i].Rt.gameObject.activeSelf) _rows[i].Rt.gameObject.SetActive(false);
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

        private float Place(ChangelogRow row, float y, bool russian)
        {
            var r = _rowsUsed < _rows.Count ? _rows[_rowsUsed] : NewRow();
            _rowsUsed++;
            r.Rt.gameObject.SetActive(true);
            var e = row.Entry;
            switch (row.Kind)
            {
                case ChangelogRowKind.Date:
                    {
                        Put(r.Rt, 0f, y, ListW, DateH);
                        Style(r.A, "text.header", 23f, ThemeTokens.GetColor("color.text.header", Hex("#D9DCE0")));
                        r.A.text = ChangelogModel.DateHeader(e.Date, russian);
                        var w = r.A.GetPreferredValues(r.A.text).x;
                        Put(r.A, 0f, 6f, w + 4f, 34f);
                        Off(r.B);
                        Off(r.Tag);
                        r.Rule.gameObject.SetActive(true);
                        Put(r.Rule, w + 18f, 23f, Mathf.Max(0f, ListW - w - 18f), 1f);
                        return DateH;
                    }
                case ChangelogRowKind.Mod:
                    {
                        Put(r.Rt, 0f, y, ListW, ModH);
                        Style(r.A, "text.header", 23f, ThemeTokens.GetColor("color.text.item.title", Hex("#F0C419")));
                        r.A.text = e.Mod;
                        var w = r.A.GetPreferredValues(r.A.text).x;
                        Put(r.A, 0f, 2f, Mathf.Min(w + 4f, ListW), 32f);
                        if (e.Version.Length > 0)
                        {
                            r.B.gameObject.SetActive(true);
                            Style(r.B, "text.header", 21f, ThemeTokens.GetColor("color.text.stat", Hex("#8AB4F0")));
                            r.B.text = e.Version;
                            Put(r.B, w + 14f, 3f, 160f, 32f);
                        }
                        else Off(r.B);
                        Off(r.Tag);
                        Off(r.Rule);
                        return ModH;
                    }
                default:
                    {
                        var c = TypeColor(e.Type);
                        r.Tag.gameObject.SetActive(true);
                        r.Tag.color = c;
                        Style(r.B, "text.body", 16f, c);
                        r.B.text = L("$ui_changelog_type_" + ChangelogModel.Types[e.Type]);
                        var tagW = r.B.GetPreferredValues(r.B.text).x + 22f;
                        Style(r.A, "text.body", 17f, ThemeTokens.GetColor("color.text.body", Hex("#E3E6E8")));
                        r.A.textWrappingMode = TextWrappingModes.Normal;
                        r.A.text = e.Text(russian);
                        var textX = 2f + tagW + TagGap;
                        var textW = ListW - textX - 8f;
                        var h = Mathf.Max(LineH, r.A.GetPreferredValues(r.A.text, textW, 0f).y + 12f);
                        Put(r.Rt, 0f, y, ListW, h);
                        Put(r.Tag, 2f, 6f, tagW, TagH);
                        Put(r.B, 2f, 6f, tagW, TagH);
                        Off(r.Rule);
                        Put(r.A, textX, 5f, textW, h - 6f);
                        return h;
                    }
            }
        }

        private Row NewRow()
        {
            var r = new Row { Rt = NewRect("Row" + _rows.Count, _list) };
            r.Rule = Rule(r.Rt, "Rule", Hex("#3D8290"));
            r.Tag = Img(r.Rt, "Tag", "field");
            r.B = Txt(r.Rt, "B", "text.body", TextAlignmentOptions.Center, 16f);
            r.A = Txt(r.Rt, "A", "text.body", TextAlignmentOptions.TopLeft, 17f);
            r.A.richText = false; // entry text is data, never markup
            r.B.richText = false;
            _rows.Add(r);
            return r;
        }

        private static void Style(TMP_Text t, string role, float size, Color color)
        {
            if (t.GetComponent<SkinMark>() is SkinMark m && m.Role != role)
            {
                m.Role = role;
                m.Outcome = Restyler.Apply(t, m, ThemeTokens.Role(role)) ? Outcome.Styled : Outcome.Degraded;
            }
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.enableAutoSizing = false;
            if (Mathf.Abs(t.fontSize - size) > 0.05f) t.fontSize = size;
            t.color = color;
            t.gameObject.SetActive(true);
        }

        // ---------------------------------------------------------------- widgets

        private void Label(Transform parent, string name, string token, float y)
        {
            var t = Txt(parent, name, "text.header", TextAlignmentOptions.Left, 21f);
            t.text = L(token);
            Put(t, SideX, y, SideW, 28f);
        }

        private TMP_InputField DateField(Transform parent, string name, string prefixToken, float y, bool from)
        {
            var back = Img(parent, name, "field", true);
            Put(back, SideX, y, SideW, 30f);
            var prefix = Txt(back.transform, "Prefix", "text.body", TextAlignmentOptions.Left, 16f);
            prefix.text = L(prefixToken);
            var pw = prefix.GetPreferredValues(prefix.text).x;
            Put(prefix, 12f, 0f, pw + 2f, 30f);
            var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            area.SetParent(back.rectTransform, false);
            Stretch(area, 12f + pw + 6f, 0f, 10f, 0f);
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
            if (_window) _window.sizeDelta = _vanillaSize;
            if (_root) Destroy(_root.gameObject);
            Destroy(this);
        }
    }
}
