using System;
using System.Collections.Generic;
using Morgott.Core;
using Morgott.UI.Icons;
using Morgott.UI.MapScreen;
using Morgott.UI.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Morgott.UI.ItemBrowser
{
    // Progression filters of the ITEMS list (docs\mockups\item-browser-progression, DESIGN.md + mockups 01-05): stage strip,
    // facet chips with a dropdown, list rows under stage headers (default) or the grid under the same headers. All filter
    // work happens in Results() on a change; layout reads the cached lists. Storage mode never comes here.
    public sealed partial class BrowserPanel
    {
        private const float StripH = 70f, ChipRowDy = 87f, ChipH = 31f, CaptionDy = 128f, ListDy = 34f, ListRowH = 53f, HeadH = 31f, PopRowH = 30f, PopW = 270f, CaptionW = 254f;
        private const int Plaques = 10, PopRows = 14;
        private static readonly Func<string, StageInfo> InfoOf = StageScan.Info;

        private readonly Dictionary<string, FilterState> _filters = new Dictionary<string, FilterState>(StringComparer.Ordinal);
        private FilterState _fs = new FilterState();
        private string _scope = "";
        private int _filterRev, _resultsFilter = -1, _resultsStage = -1, _stageRev = -1, _chipOpen = -1, _popScroll, _popMax;
        private bool _listMode = true, _resultsList = true, _plainGrid; // _plainGrid: the filter UI failed, the classic grid takes over
        private List<Entry> _scoped = new List<Entry>();
        private List<ListBlock> _blocks = new List<ListBlock>();
        private readonly List<float> _blockH = new List<float>();
        private readonly int[] _counts = new int[Plaques];
        private readonly Dictionary<string, string> _subs = new Dictionary<string, string>(StringComparer.Ordinal);

        private readonly Button[] _plaques = new Button[Plaques];
        private readonly TMP_Text[] _plaqueName = new TMP_Text[Plaques], _plaqueCount = new TMP_Text[Plaques];
        private readonly Image[] _plaqueGlyph = new Image[Plaques];
        private readonly CanvasGroup[] _plaqueFade = new CanvasGroup[Plaques];
        private readonly List<FacetButton> _chips = new List<FacetButton>();
        private Button _reset, _layoutBtn;
        private TMP_Text _resetLabel, _fm, _pageWhy;
        private CanvasGroup _resetFade;
        private Image _layoutGrid, _layoutList, _popBack, _popBlock, _popFill;
        private readonly Pool<Head> _heads = new Pool<Head>();
        private readonly Pool<Image> _rowRules = new Pool<Image>();
        private readonly List<PopRow> _popRows = new List<PopRow>();
        private readonly List<PopItem> _popItems = new List<PopItem>();
        private static Sprite _diamond;

        private sealed class FacetButton
        {
            public Button B;
            public TMP_Text Label;
            public Image Arrow;
            public float X, W;
        }

        private sealed class Head
        {
            public TMP_Text Label;
            public Image Line;
        }

        private sealed class PopRow
        {
            public Image Back, Check;
            public TMP_Text Label, Count;
            public int Item;
        }

        private struct PopItem
        {
            public string Facet, Key, Label;
            public int Count;
            public bool Header;
        }

        // Extra parts of a list row, built with the grid cell they belong to.
        private sealed class RowParts
        {
            public Slot Hit;
            public TMP_Text Sub, Marker, TagText;
            public Image Diamond, Chest, Ocean, Tag;
            public readonly Image[] Stat = new Image[3];
            public readonly TMP_Text[] StatText = new TMP_Text[3];
        }

        private static Sprite Diamond()
        {
            if (_diamond) return _diamond;
            var tex = ModStatusIcons.LoadEmbedded("Morgott.UI.StageDiamond.png", "UI_StageDiamond");
            if (tex) _diamond = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            return _diamond;
        }

        private static void OnClick(Component c, Action act) =>
            c.gameObject.AddComponent<Hit>().Click = e => { if (e.button == PointerEventData.InputButton.Left) act(); };

        private void PlainGrid(Exception e)
        {
            Plugin.Log.LogWarning($"[Morgott.UI] item browser filters off, plain grid: {e}");
            _plainGrid = true;
            _filterRev++;
            _dirty = true;
            FiltersOff();
        }

        private void Touch()
        {
            _filterRev++;
            _gridScroll = 0;
            _dirty = true;
        }

        private void BuildFilters(Transform g)
        {
            _fm = Txt(g, "FilterMeasure", "text.body", TextAlignmentOptions.Left);
            for (var i = 0; i < Plaques; i++)
            {
                var n = i;
                _plaques[i] = Btn(g, "Plaque" + i, "ib.square", out _plaqueName[i], () => { });
                OnClick(_plaques[i], () => { _fs.Plaques ^= 1 << n; Touch(); });
                _plaqueFade[i] = _plaques[i].gameObject.AddComponent<CanvasGroup>();
                _plaqueCount[i] = Txt(_plaques[i].transform, "Count", "text.body", TextAlignmentOptions.Center);
                _plaqueGlyph[i] = i == StageRules.PlaqueMagic ? Img(_plaques[i].transform, "Glyph", "ib.glyph.magic") : Img(_plaques[i].transform, "Glyph", "content");
                if (i != StageRules.PlaqueMagic) SetSprite(_plaqueGlyph[i], Diamond());
            }
            _reset = Btn(g, "FilterReset", "ib.square", out _resetLabel, () => { });
            _resetFade = _reset.gameObject.AddComponent<CanvasGroup>();
            OnClick(_reset, () => { _fs.Clear(); _chipOpen = -1; Touch(); });
            _layoutBtn = Btn(g, "LayoutToggle", "ib.square", out var layoutLabel, () => { });
            Off(layoutLabel);
            OnClick(_layoutBtn, () => { _listMode = !_listMode; Touch(); });
            _layoutGrid = Img(_layoutBtn.transform, "Grid", "ib.glyph.grid");
            _layoutList = Img(_layoutBtn.transform, "List", "ib.glyph.list");
            _popBlock = Img(g, "FacetBlock", "content", true);
            _popBlock.color = Color.clear;
            OnClick(_popBlock, CloseChip);
            // Solid fill under the list plate: the plate let ~5 % of the list rows through (run 20261120-170547: row text
            // 227 -> 17 over the 7,16,23 plate); same opaque backing as InfoPage.Backing.
            _popFill = Img(g, "FacetPopupFill", "content");
            _popFill.preserveAspect = false;
            var fill = ThemeTokens.GetColor("color.info.page.bg", new Color(0f, 0.086f, 0.11f, 1f));
            fill.a = 1f;
            _popFill.color = fill;
            _popBack = Img(g, "FacetPopup", "dropdown.list", true); // the game's dropdown list plate (crafting sort list)
            _popFill.gameObject.SetActive(false);
            _popBlock.gameObject.SetActive(false);
            _popBack.gameObject.SetActive(false);
        }

        private void CloseChip()
        {
            if (_chipOpen < 0) return;
            _chipOpen = -1;
            _dirty = true;
        }

        private void FiltersOff()
        {
            foreach (var b in _plaques) { Off(b); if (b && _halos.TryGetValue(b, out var h)) Off(h); }
            foreach (var c in _chips) { Off(c.B); if (_halos.TryGetValue(c.B, out var h)) Off(h); }
            Off(_reset);
            Off(_layoutBtn);
            Off(_popBlock);
            Off(_popFill);
            Off(_popBack);
            _heads.Used = _rowRules.Used = 0;
            RestList();
        }

        private void RestList()
        {
            for (var i = _heads.Used; i < _heads.Items.Count; i++) { Off(_heads.Items[i].Label); Off(_heads.Items[i].Line); }
            for (var i = _rowRules.Used; i < _rowRules.Items.Count; i++) Off(_rowRules.Items[i]);
        }

        // ---------------------------------------------------------------- results

        private List<Entry> Filtered(int cat, bool known, HashSet<string> fav, bool dev)
        {
            var scope = FacetTable.Scope(_type, cat, CatKeys);
            if (scope != _scope)
            {
                _scope = scope;
                _chipOpen = -1;
                if (!_filters.TryGetValue(scope, out _fs)) _filters[scope] = _fs = new FilterState();
            }
            _scoped = Browser.Index.Search(_query, TypeOf[_type],
                e => (cat < 0 || (int)e.Category == cat) && (!known || Browser.Known(e)) && (fav == null || fav.Contains(e.Id)), L, Browser.ModName, dev);
            if (_plainGrid) return _scoped;
            try { return Staged(); }
            catch (Exception e)
            {
                PlainGrid(e);
                return _scoped;
            }
        }

        private List<Entry> Staged()
        {
            Array.Clear(_counts, 0, _counts.Length);
            var facets = StageScan.Facets;
            var kept = new List<Entry>(_scoped.Count);
            foreach (var e in _scoped)
            {
                if (!facets.Matches(e.Id, _fs)) continue;
                var info = StageScan.Info(e.Id);
                _counts[StageRules.Plaque(info)]++; // the strip's own selection never changes its counts
                if (StageRules.PassesStrip(_fs.Plaques, info)) kept.Add(e);
            }
            var ordered = ListBlocks.Order(kept, InfoOf, e => e.Type == EntryType.Creature && IsBoss(e.Id));
            _blocks = ListBlocks.Build(ordered, InfoOf, _listMode ? 1 : 6);
            _blockH.Clear();
            foreach (var b in _blocks) _blockH.Add(b.Header ? HeadH : _listMode ? ListRowH : CellPitchY);
            _subs.Clear();
            return ordered;
        }

        // ---------------------------------------------------------------- strip + chips

        private static Heightmap.Biome BiomeAt(int biome) => FoundIn.Order[biome];

        // Theme biome colours are map tints: dark ones are lifted to a readable brightness, hue kept.
        private static Color Readably(Color c)
        {
            Color.RGBToHSV(c, out var h, out var s, out var v);
            return Color.HSVToRGB(h, s, Mathf.Max(v, 0.78f));
        }

        private static Color GroupColor(int group)
        {
            if (group == StageRules.GroupMagic) return ThemeTokens.GetColor("color.text.stat", Color.white);
            if (group == StageRules.GroupUnknown) return ThemeTokens.GetColor("color.text.unavailable", Color.gray);
            return Readably(BiomeOverlay.BiomeColor(BiomeAt(group / 2)));
        }

        private static string Hex(string token, Color fallback) => ColorUtility.ToHtmlStringRGB(ThemeTokens.GetColor(token, fallback));

        private float MeasureF(string s, float size)
        {
            Text(_fm, "", size);
            return _fm.GetPreferredValues(s).x / Mathf.Max(_k * _f, 1e-6f);
        }

        // y = top of the strip; returns the caption row's y.
        private float LayoutFilters(float y)
        {
            var magic = _counts[StageRules.PlaqueMagic] > 0;
            var n = magic ? Plaques : Plaques - 1;
            var w = (UnitW - (n - 1) * 6f) / n;
            for (var i = 0; i < Plaques; i++)
            {
                if (i == StageRules.PlaqueMagic && !magic)
                {
                    Off(_plaques[i]);
                    if (_halos.TryGetValue(_plaques[i], out var ho)) Off(ho);
                    continue;
                }
                // "?" stays last; the Magic plaque stands right before it.
                var pos = i < 8 ? i : i == StageRules.PlaqueMagic ? 8 : n - 1;
                var x = pos * (w + 6f);
                var on = (_fs.Plaques & (1 << i)) != 0;
                Square(_plaques[i], on, x, y, w, StripH);
                _plaqueFade[i].alpha = _counts[i] == 0 && !on ? 0.45f : 1f;
                Put(_plaqueGlyph[i], x + w / 2f - 9f, y + 5f, 18f, 18f);
                if (i != StageRules.PlaqueMagic) _plaqueGlyph[i].color = i == StageRules.PlaqueUnknown ? GroupColor(StageRules.GroupUnknown) : GroupColor(i * 2);
                Put(_plaqueName[i], x + 1f, y + 23f, w - 2f, 23f);
                Fit(_plaqueName[i], i == StageRules.PlaqueUnknown ? "?" : L(i == StageRules.PlaqueMagic ? "$ib_stage_magic_s" : "$ib_stage_s" + i), 18f);
                Put(_plaqueCount[i], x + 1f, y + 45f, w - 2f, 22f);
                Fit(_plaqueCount[i], _counts[i].ToString(), 18f);
            }
            LayoutChips(y + ChipRowDy);
            var capY = y + CaptionDy;
            Square(_layoutBtn, false, CaptionW + 6f, capY, 26f, 26f);
            Put(_listMode ? _layoutGrid : _layoutList, CaptionW + 10f, capY + 4f, 18f, 18f);
            Off(_listMode ? _layoutList : _layoutGrid);
            return capY;
        }

        private string ValueLabel(string facet, string key)
        {
            if (key == Facet.NoneKey) return L("$ib_f_none");
            return StageScan.Facets.Facets.TryGetValue(facet, out var f) && f.Labels.TryGetValue(key, out var label) ? L(label) : key;
        }

        private string FirstPicked(FacetChip chip, out int more)
        {
            more = -1;
            string first = null;
            foreach (var id in chip.Facets)
                if (_fs.Picked.TryGetValue(id, out var set))
                    foreach (var k in set)
                    {
                        more++;
                        first ??= ValueLabel(id, k);
                    }
            return first;
        }

        private string ChipText(FacetChip chip)
        {
            var chipName = L(chip.Token);
            var v = FirstPicked(chip, out var more);
            return v == null ? chipName : $"{chipName}: <color=#{Hex("color.text.active", Color.yellow)}>{v}{(more > 0 ? " +" + more : "")}</color>";
        }

        private void LayoutChips(float y)
        {
            var defs = FacetTable.ChipsOf(_scope);
            while (_chips.Count < defs.Length)
            {
                var n = _chips.Count;
                var c = new FacetButton { B = Btn(_gridView.transform, "FacetChip" + n, "ib.square", out var label, () => { }) };
                c.Label = label;
                label.alignment = TextAlignmentOptions.Left;
                c.Arrow = Img(c.B.transform, "Arrow", "ib.glyph.chevron");
                OnClick(c.B, () => { _chipOpen = _chipOpen == n ? -1 : n; _popScroll = 0; _dirty = true; });
                _chips.Add(c);
            }
            const float gap = 9f, resetW = 30f;
            var room = UnitW - resetW - gap * defs.Length;
            var sum = 0f;
            for (var i = 0; i < defs.Length; i++)
            {
                _chips[i].W = MeasureF(System.Text.RegularExpressions.Regex.Replace(ChipText(defs[i]), "<[^>]+>", ""), 17f) + 34f;
                sum += _chips[i].W;
            }
            var extra = sum < room ? (room - sum) / Math.Max(1, defs.Length) : 0f;
            var scale = sum > room ? room / sum : 1f; // long picked values: every chip gives way, none collapses
            var x = 0f;
            for (var i = 0; i < _chips.Count; i++)
            {
                var c = _chips[i];
                if (i >= defs.Length)
                {
                    Off(c.B);
                    if (_halos.TryGetValue(c.B, out var ho)) Off(ho);
                    continue;
                }
                c.W = c.W * scale + extra;
                c.X = x;
                Square(c.B, i == _chipOpen, x, y, c.W, ChipH);
                Put(c.Label, x + 9f, y, c.W - 28f, ChipH);
                Fit(c.Label, ChipText(defs[i]), 17f);
                Put(c.Arrow, x + c.W - 19f, y + 10f, 12f, 12f);
                x += c.W + gap;
            }
            Square(_reset, false, UnitW - resetW, y, resetW, ChipH);
            Put(_resetLabel, UnitW - resetW, y, resetW, ChipH);
            Fit(_resetLabel, "×", 20f);
            _resetFade.alpha = _fs.Any ? 1f : 0.45f;
            if (_chipOpen >= 0 && _chipOpen < defs.Length) LayoutPopup(defs[_chipOpen], _chips[_chipOpen].X, y + ChipH + 3f);
            else { Off(_popBlock); Off(_popFill); Off(_popBack); }
        }

        // ---------------------------------------------------------------- dropdown

        private void FillPopup(FacetChip chip)
        {
            _popItems.Clear();
            foreach (var id in chip.Facets)
            {
                if (!StageScan.Facets.Facets.TryGetValue(id, out var f)) continue;
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var e in _scoped)
                {
                    if (!f.ByEntry.TryGetValue(e.Id, out var keys)) { counts.TryGetValue(Facet.NoneKey, out var c0); counts[Facet.NoneKey] = c0 + 1; continue; }
                    foreach (var k in keys) { counts.TryGetValue(k, out var c); counts[k] = c + 1; }
                }
                var rows = new List<PopItem>(counts.Count);
                foreach (var kv in counts)
                    if (kv.Key != Facet.NoneKey) rows.Add(new PopItem { Facet = id, Key = kv.Key, Label = ValueLabel(id, kv.Key), Count = kv.Value });
                rows.Sort((a, b) => id == "duration" || id == "tier" || id == "level" ? string.CompareOrdinal(a.Key, b.Key) : StringComparer.CurrentCultureIgnoreCase.Compare(a.Label, b.Label));
                if (rows.Count == 0) continue;
                if (chip.Facets.Length > 1) _popItems.Add(new PopItem { Header = true, Label = L("$ib_f_" + id) });
                _popItems.AddRange(rows);
                if (counts.TryGetValue(Facet.NoneKey, out var none)) _popItems.Add(new PopItem { Facet = id, Key = Facet.NoneKey, Label = L("$ib_f_none"), Count = none });
            }
        }

        private PopRow NewPopRow(int i)
        {
            var r = new PopRow { Back = Img(_popBack.transform, "Row" + i, "content", true) };
            r.Back.color = Color.clear;
            r.Check = Img(_popBack.transform, "Check" + i, "toggle.check");
            r.Label = Txt(_popBack.transform, "Label" + i, "text.body", TextAlignmentOptions.Left);
            r.Count = Txt(_popBack.transform, "Count" + i, "text.muted", TextAlignmentOptions.Right);
            OnClick(r.Back, () =>
            {
                if (r.Item >= _popItems.Count || _popItems[r.Item].Header) return;
                var it = _popItems[r.Item];
                _fs.Toggle(it.Facet, it.Key, StageScan.Facets.Facets.TryGetValue(it.Facet, out var f) && f.Multi);
                Touch();
            });
            return r;
        }

        private void LayoutPopup(FacetChip chip, float x, float y)
        {
            FillPopup(chip);
            // Everything sits inside the plate's rim (its sliced border at the native scale, like the game's dropdown list).
            var s = _popBack.sprite;
            var pad = (s ? Mathf.Max(Mathf.Max(s.border.x, s.border.z), Mathf.Max(s.border.y, s.border.w)) / Mathf.Max(_popBack.pixelsPerUnit, 1e-3f) / _k : 0f) + 4f;
            // Rows that fit above the panel's bottom; the box widens to its longest label, at most the panel (then ellipsis).
            var shown = Mathf.Clamp(Mathf.FloorToInt((_a.H / _k - y - 2f * pad) / PopRowH), 1, Math.Min(PopRows, Math.Max(1, _popItems.Count)));
            _popMax = Math.Max(0, _popItems.Count - shown);
            _popScroll = Mathf.Clamp(_popScroll, 0, _popMax);
            var popW = PopW;
            foreach (var it in _popItems) popW = Mathf.Max(popW, MeasureF(it.Label, 17f) + (it.Header ? 8f : 70f) + 2f * pad);
            popW = Mathf.Min(popW, UnitW);
            x = Mathf.Clamp(x, 0f, UnitW - popW);
            Put(_popBlock, 0f, 0f, UnitW, _a.H / _k);
            _popBlock.transform.SetAsLastSibling();
            Put(_popFill, x + pad - 4f, y + pad - 4f, popW - 2f * pad + 8f, shown * PopRowH + 8f); // inside the rim: the corners stay the plate's
            _popFill.transform.SetAsLastSibling();
            Put(_popBack, x, y, popW, shown * PopRowH + 2f * pad);
            _popBack.transform.SetAsLastSibling();
            x += pad;
            y += pad;
            popW -= 2f * pad;
            while (_popRows.Count < shown) _popRows.Add(NewPopRow(_popRows.Count));
            for (var i = 0; i < _popRows.Count; i++)
            {
                var r = _popRows[i];
                var at = i + _popScroll;
                if (i >= shown || at >= _popItems.Count) { Off(r.Back); Off(r.Check); Off(r.Label); Off(r.Count); continue; }
                var it = _popItems[at];
                r.Item = at;
                var ry = y + i * PopRowH;
                Put(r.Back, x, ry, popW, PopRowH);
                var on = !it.Header && _fs.Picked.TryGetValue(it.Facet, out var set) && set.Contains(it.Key);
                if (on) Put(r.Check, x + 3f, ry + 7f, 16f, 16f);
                else Off(r.Check);
                Put(r.Label, x + (it.Header ? 0f : 22f), ry, popW - (it.Header ? 0f : 70f), PopRowH);
                Fit(r.Label, it.Header ? $"<color=#{Hex("color.text.unavailable", Color.gray)}>{it.Label}</color>" : on ? $"<color=#{Hex("color.text.active", Color.yellow)}>{it.Label}</color>" : it.Label, 17f);
                if (it.Header) Off(r.Count);
                else
                {
                    Put(r.Count, x + popW - 42f, ry, 42f, PopRowH);
                    Fit(r.Count, it.Count.ToString(), 16f);
                }
            }
        }

        private string FilterCaption(string what, int count)
        {
            string part = null;
            foreach (var chip in FacetTable.ChipsOf(_scope))
                if ((part = FirstPicked(chip, out _)) != null) break;
            if (part == null && _fs.Plaques != 0 && (_fs.Plaques & (_fs.Plaques - 1)) == 0)
            {
                var i = 0;
                while ((_fs.Plaques & (1 << i)) == 0) i++;
                part = i == StageRules.PlaqueUnknown ? L("$ib_stage_unknown") : i == StageRules.PlaqueMagic ? L("$ib_stage_magic") : L(MapFilterMath.NameKey(BiomeAt(i)));
            }
            return part == null ? $"{what} · {count}" : $"{what} · {part} · {count}";
        }

        // ---------------------------------------------------------------- entries

        private static string GroupName(int group)
        {
            if (group == StageRules.GroupMagic) return L("$ib_stage_magic");
            if (group == StageRules.GroupUnknown) return L("$ib_stage_unknown");
            var biomeName = L(MapFilterMath.NameKey(BiomeAt(group / 2)));
            return (group & 1) == 1 ? $"{biomeName} · {L("$ib_stage_afterboss")}" : biomeName;
        }

        private void LayoutEntries(List<Entry> list, float top, float h)
        {
            Off(_selHalo);
            Off(_selRim);
            _heads.Used = _rowRules.Used = 0;
            var view = h - top;
            _gridScrollMax = PageModel.MaxFirst(_blockH, view);
            _gridScroll = Mathf.Clamp(_gridScroll, 0, _gridScrollMax);
            var fav = Browser.Favorites();
            var y = top;
            var used = 0;
            var visible = 0;
            for (var b = _gridScroll; b < _blocks.Count && y + _blockH[b] <= h + 0.5f; b++, visible++)
            {
                var block = _blocks[b];
                if (block.Header) Header(block, y);
                else
                    for (var i = 0; i < block.Count; i++)
                    {
                        while (_cells.Count <= used) _cells.Add(GridCell(_cells.Count));
                        var c = _cells[used++];
                        var e = list[block.From + i];
                        if (_listMode) ListRow(c, e, y, fav);
                        else GridEntry(c, e, i * CellPitchX, y, fav);
                    }
                y += _blockH[b];
            }
            for (var i = used; i < _cells.Count; i++)
            {
                var c = _cells[i];
                c.Id = null;
                c.Store = null;
                Off(c.Back);
                Off(c.A);
                HideRow(c);
            }
            RestList();
            if (_chipOpen >= 0) // cells built this pass must stay under the open dropdown
            {
                _popBlock.transform.SetAsLastSibling();
                _popFill.transform.SetAsLastSibling();
                _popBack.transform.SetAsLastSibling();
            }
            if (_gridScrollMax > 0)
            {
                var viewH = view - 8f;
                var thumbH = PageModel.ThumbH(viewH, visible, _blocks.Count);
                _gridViewH = viewH;
                _gridThumbH = thumbH;
                Put(_track, UnitW - 5f, top, 4f, viewH);
                Put(_thumb, UnitW - 6f, top + (viewH - thumbH) * _gridScroll / _gridScrollMax, 6f, thumbH);
                Put(_grip, UnitW - 18f, top, 18f, viewH);
            }
            else { Off(_track); Off(_thumb); Off(_grip); }
            if (list.Count == 0 && Browser.KnownFilter)
            {
                Put(_empty, 20f, top + 20f, UnitW - 40f, 80f);
                Fit(_empty, L("$ib_empty_known"), 18f);
            }
            else Off(_empty);
        }

        private void Header(ListBlock b, float y)
        {
            var head = _heads.Next(i => new Head
            {
                Label = Txt(_gridView.transform, "StageHead" + i, "text.body", TextAlignmentOptions.Left, FontStyles.Bold),
                Line = Img(_gridView.transform, "StageLine" + i, "ib.rule")
            });
            var color = GroupColor(b.Group);
            var text = $"{GroupName(b.Group).ToUpperInvariant()} · {-b.Count}";
            Text(head.Label, $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{text}</color>", 18f);
            var w = Mathf.Min(head.Label.GetPreferredValues(text).x / Mathf.Max(_k * _f, 1e-6f) + 4f, UnitW - 60f); // measured in the label's own bold style
            Put(head.Label, 0f, y + 3f, w, 25f);
            if (_listMode)
            {
                Put(head.Line, w + 8f, y + 15f, UnitW - 14f - w - 8f, 2f);
                head.Line.color = color;
            }
            else
            {
                Put(head.Line, 0f, y + HeadH - 3f, UnitW - 14f, 2f); // mockup 05: the grid's band rule runs under the header
                head.Line.color = RuleColor;
            }
        }

        private RowParts Parts(Slot c)
        {
            if (c.Row != null) return c.Row;
            var g = _gridView.transform;
            var n = _cells.IndexOf(c);
            var r = new RowParts { Hit = NewSlot(g, "Row" + n, null) };
            r.Hit.Rt.SetSiblingIndex(c.Rt.GetSiblingIndex()); // under the socket
            Off(r.Hit.Icon);
            r.Sub = Txt(g, "RowSub" + n, "text.muted", TextAlignmentOptions.Left);
            r.Marker = Txt(g, "RowMarker" + n, "text.body", TextAlignmentOptions.Right);
            r.Diamond = Img(g, "RowDiamond" + n, "content");
            r.Chest = Img(g, "RowChest" + n, "ib.glyph.chest");
            r.Ocean = Img(g, "RowOcean" + n, "ib.glyph.ocean");
            r.Tag = Img(g, "RowTag" + n, "ib.rule");
            r.TagText = Txt(g, "RowTagText" + n, "text.body", TextAlignmentOptions.Center, FontStyles.Bold);
            string[] roles = { "ib.stat.health", "ib.stat.stamina", "ib.stat.eitr" };
            for (var i = 0; i < 3; i++)
            {
                r.Stat[i] = Img(g, "RowStat" + n + "_" + i, roles[i]);
                r.StatText[i] = Txt(g, "RowStatText" + n + "_" + i, "text.body", TextAlignmentOptions.Left); // mockup 03: values in the body tone
                r.StatText[i].raycastTarget = false;
            }
            r.Sub.raycastTarget = r.Marker.raycastTarget = r.TagText.raycastTarget = c.A.raycastTarget = false; // the row's hit area takes the clicks
            return c.Row = r;
        }

        private void HideRow(Slot c)
        {
            if (c.Corner) Off(c.Corner);
            var r = c.Row;
            if (r == null) return;
            r.Hit.Id = null;
            Off(r.Hit.Back);
            Off(r.Sub);
            Off(r.Marker);
            Off(r.Diamond);
            Off(r.Chest);
            Off(r.Ocean);
            Off(r.Tag);
            Off(r.TagText);
            for (var i = 0; i < 3; i++) { Off(r.Stat[i]); Off(r.StatText[i]); }
        }

        private void Paw(Slot c, Entry e, float x, float y)
        {
            var paw = Browser.TameableBadge.Value && e.Type == EntryType.Creature ? Browser.TameBadge(Browser.Tameable(c.Id, out _)) : null;
            if (!paw) { Off(c.Badge); return; }
            Put(c.Badge, x, y, 22f, 22f);
            if (c.Badge.sprite != paw) c.Badge.sprite = paw;
        }

        private void GridEntry(Slot c, Entry e, float cx, float cy, HashSet<string> fav)
        {
            HideRow(c);
            c.Id = e.Id;
            c.Store = null;
            c.Quality = 1;
            Put(c.Back, cx, cy, CellW, CellH);
            Put(c.Icon, cx + 8f, cy + 5f, CellW - 16f, CellH - 10f);
            SetIcon(c.Icon, c.Id);
            Put(c.D, cx + CellW - 22f, cy + 2f, 18f, 18f);
            Text(c.D, "★", 16f);
            c.D.enabled = fav.Contains(c.Id);
            Off(c.B);
            Paw(c, e, cx + 3f, cy + CellH - 25f);
            if (!c.Corner) { c.Corner = Img(c.Rt, "StageCorner", "content"); SetSprite(c.Corner, Diamond()); }
            Put(c.Corner, cx + 4f, cy + 4f, 15f, 15f);
            c.Corner.color = GroupColor(StageRules.Group(StageScan.Info(e.Id)));
            Put(c.A, cx + 1f, cy + CellH + 1f, CellW - 2f, 41f);
            c.A.alignment = TextAlignmentOptions.Top;
            c.A.textWrappingMode = TextWrappingModes.Normal;
            Fit(c.A, L(e.Name), 17.5f);
        }

        private void ListRow(Slot c, Entry e, float y, HashSet<string> fav)
        {
            if (c.Corner) Off(c.Corner);
            var r = Parts(c);
            c.Id = r.Hit.Id = e.Id;
            c.Store = null;
            c.Quality = r.Hit.Quality = 1;
            const float nameX = 67f;
            Put(r.Hit.Back, 0f, y, UnitW - 12f, ListRowH);
            Put(c.Back, 0f, y + 3f, 54f, 47f);
            Put(c.Icon, 4f, y + 6f, 46f, 41f);
            SetIcon(c.Icon, c.Id);
            Put(c.D, 35f, y + 4f, 18f, 18f);
            Text(c.D, "★", 16f);
            c.D.enabled = fav.Contains(c.Id);
            Off(c.B);
            var nameW = Marker(r, StageScan.Info(e.Id), y) - 8f - nameX; // name and sub line use all the room the marker leaves
            var entryName = L(e.Name);
            var tagged = e.Type == EntryType.Creature && StageScan.Data != null && StageScan.Data.Solver != null && IsBoss(e.Id);
            var shownW = Mathf.Min(MeasureF(entryName, 19f) + 3f, nameW - (tagged ? 60f : e.Type == EntryType.Creature ? 26f : 0f));
            Put(c.A, nameX, y + 3f, shownW, 25f);
            c.A.alignment = TextAlignmentOptions.Left;
            c.A.textWrappingMode = TextWrappingModes.NoWrap;
            Text(c.A, entryName, 19f);
            if (tagged)
            {
                Put(r.Tag, nameX + shownW + 6f, y + 6f, 50f, 19f);
                r.Tag.color = ThemeTokens.GetColor("color.text.active", Color.yellow);
                Put(r.TagText, nameX + shownW + 6f, y + 4f, 50f, 23f); // taller than the plate: an ellipsis text drops a line its box cannot hold
                Text(r.TagText, $"<color=#141414>{L("$ib_tag_boss")}</color>", 16f);
            }
            else
            {
                Off(r.Tag);
                Off(r.TagText);
            }
            Paw(c, e, nameX + shownW + (tagged ? 62f : 5f), y + 5f);
            var sx = nameX;
            if (e.Category == ItemCategory.Food && e.Type == EntryType.Item && Food(e.Id, out var hp, out var st, out var ei))
            {
                float[] vals = { hp, st, ei };
                for (var i = 0; i < 3; i++)
                {
                    Put(r.Stat[i], sx, y + 31f, 14f, 14f);
                    var t = Mathf.RoundToInt(vals[i]).ToString();
                    Put(r.StatText[i], sx + 16f, y + 27f, 30f, 22f);
                    Text(r.StatText[i], t, 16f);
                    sx += 46f;
                }
            }
            else for (var i = 0; i < 3; i++) { Off(r.Stat[i]); Off(r.StatText[i]); }
            var subW = nameX + nameW - sx;
            var sub = SubLine(e);
            if (sx > nameX && sub.Length > 0) sub = "· " + sub; // mockup 03: "94 · 30 min"
            Put(r.Sub, sx, y + 27f, subW, 22f);
            Fit(r.Sub, ListBlocks.WholeParts(sub, subW - 4f, s => MeasureF(s, 16f)), 16f);
            Rule2(y + ListRowH - 1.5f);
        }

        private void Rule2(float y)
        {
            var rule = _rowRules.Next(n => Img(_gridView.transform, "RowRule" + n, "ib.rule"));
            Put(rule, 0f, y, UnitW - 12f, 1f);
            var c = RuleColor;
            c.a *= 0.3f; // mockups 01-04: rows part on a faint line, the stage bands keep the bright rule
            rule.color = c;
        }

        private static Color RuleColor => ThemeTokens.GetColor("color.ib.rule", new Color(0.24f, 0.51f, 0.56f));

        // Returns the left edge of the marker with its glyphs.
        private float Marker(RowParts r, StageInfo i, float y)
        {
            var group = StageRules.Group(i);
            var text = (i.Via == StageVia.Authored ? "≈ " : "") + GroupName(group);
            var right = UnitW - 16f;
            var w = Mathf.Min(MeasureF(text, 16f) + 3f, 150f);
            Put(r.Marker, right - w, y + 3f, w, ListRowH - 6f);
            Fit(r.Marker, text, 16f);
            var x = right - w - 19f;
            Put(r.Diamond, x, y + 19f, 14f, 14f);
            SetSprite(r.Diamond, Diamond());
            r.Diamond.color = GroupColor(group);
            if (i.Via == StageVia.Incidental) { x -= 19f; Put(r.Chest, x, y + 18f, 16f, 16f); }
            else Off(r.Chest);
            if (i.Ocean) { x -= 19f; Put(r.Ocean, x, y + 18f, 16f, 16f); }
            else Off(r.Ocean);
            return x;
        }

        // ---------------------------------------------------------------- row text

        private static bool IsBoss(string id) => Browser.Prefab(id) is GameObject go && go.TryGetComponent<Character>(out var c) && c.m_boss;

        private static bool Food(string id, out float hp, out float st, out float ei)
        {
            hp = st = ei = 0f;
            if (!(Browser.Prefab(id) is GameObject go) || !go.TryGetComponent<ItemDrop>(out var d)) return false;
            var s = d.m_itemData.m_shared;
            hp = s.m_food;
            st = s.m_foodStamina;
            ei = s.m_foodEitr;
            return true;
        }

        private static string StationText(string id)
        {
            foreach (var r in Browser.Index.ObtainedFrom(id))
                if ((r.Kind == RelationKind.Craft || r.Kind == RelationKind.Build || r.Kind == RelationKind.Conversion) && r.Station != null)
                    return r.StationLevel > 1 ? $"{NameOf(r.Station)} {r.StationLevel}" : NameOf(r.Station);
            return null;
        }

        private static void Join(System.Text.StringBuilder sb, string part)
        {
            if (string.IsNullOrEmpty(part)) return;
            if (sb.Length > 0) sb.Append(" · ");
            sb.Append(part);
        }

        private static void Dmg(List<string> list, string kind, float v)
        {
            if (v > 0f) list.Add($"{L("$inventory_" + kind).ToLowerInvariant()} {Mathf.RoundToInt(v)}");
        }

        private string SubLine(Entry e)
        {
            if (_subs.TryGetValue(e.Id, out var s)) return s;
            var sb = new System.Text.StringBuilder();
            var go = Browser.Prefab(e.Id);
            if (e.Type == EntryType.Item && go && go.TryGetComponent<ItemDrop>(out var d))
            {
                var sh = d.m_itemData.m_shared;
                switch (e.Category)
                {
                    case ItemCategory.Weapons:
                        var parts = new List<string>();
                        var dm = sh.m_damages;
                        Dmg(parts, "blunt", dm.m_blunt);
                        Dmg(parts, "slash", dm.m_slash);
                        Dmg(parts, "pierce", dm.m_pierce);
                        Dmg(parts, "fire", dm.m_fire);
                        Dmg(parts, "frost", dm.m_frost);
                        Dmg(parts, "lightning", dm.m_lightning);
                        Dmg(parts, "poison", dm.m_poison);
                        Dmg(parts, "spirit", dm.m_spirit);
                        Join(sb, string.Join(", ", parts));
                        break;
                    case ItemCategory.Armor:
                        Join(sb, L("$ib_itemtype_" + sh.m_itemType.ToString().ToLowerInvariant()));
                        Join(sb, $"{L("$ib_armor")} {Mathf.RoundToInt(sh.m_armor)}");
                        break;
                    case ItemCategory.Food:
                        if (sh.m_foodBurnTime > 0f) Join(sb, $"{Mathf.RoundToInt(sh.m_foodBurnTime / 60f)} {L("$ib_min")}");
                        break;
                    default:
                        Join(sb, TypeName(e));
                        break;
                }
                Join(sb, StationText(e.Id));
            }
            else if (e.Type == EntryType.Creature && go && go.TryGetComponent<Character>(out var c))
            {
                Join(sb, $"{Mathf.RoundToInt(c.m_health)} {L("$ib_hp")}");
                var drops = new List<string>();
                foreach (var r in Browser.Index.UsedIn(e.Id))
                    if (r.Source == e.Id && r.Kind == RelationKind.CreatureDrop && drops.Count < 3)
                        foreach (var o in r.Outputs)
                            if (drops.Count < 3 && !drops.Contains(NameOf(o.Prefab))) drops.Add(NameOf(o.Prefab));
                Join(sb, string.Join(", ", drops));
            }
            else
            {
                Join(sb, TypeName(e));
                Join(sb, StationText(e.Id));
            }
            return _subs[e.Id] = sb.ToString();
        }

        // Item page: why an entry has no stage (the first requirement nothing provides).
        private static string StageWhy(Entry e)
        {
            var data = StageScan.Data;
            if (data == null || e.Type == EntryType.Other) return null;
            var i = StageScan.Info(e.Id);
            if (i.Stage >= 0 || i.Via == StageVia.Magic) return null;
            try
            {
                var m = data.Solver.Missing(e.Id);
                return m == null || m == e.Id || m.StartsWith("stale:", StringComparison.Ordinal) ? null : $"{L("$ib_stage_why")}: {NameOf(m)}"; // stale: a world-data gap, no item to name
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Morgott.UI] item browser stages: {e.Id}: {ex.Message}");
                return null;
            }
        }
    }
}
