using System;
using System.Collections.Generic;
using Morgott.Core;
using Morgott.UI.InventoryScreen;
using Morgott.UI.Restyle;
using Morgott.UI.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Morgott.UI.CraftingScreen
{
    // Create tab view (approved mockup craftfilter mockup-v2, 2026-11-16): a controls row under the search (sort dropdown,
    // direction, "new only" funnel with the active-filter badge, All | Can | Can't), per recipe row the food stats
    // (heart / bolt / drop, the sorted one brighter), the craftable count, NEW and the chain link, in the detail a stat table
    // and the bonus portion chance. View state, order, filter and counts: Core ICraftingAvailability (Morgott.Patcher).
    public sealed partial class CraftingSkin
    {
        private static ICraftingAvailability View() => Services.TryGet<ICraftingAvailability>(out var v) ? v : null;

        private static int CountCap() => Mathf.Max(1, Mathf.RoundToInt(ThemeTokens.GetFloat("size.craft.amount.max", 99f)));

        private static readonly CraftSort[] SortKeys = (CraftSort[])Enum.GetValues(typeof(CraftSort));
        private static readonly CraftShow[] ShowKeys = (CraftShow[])Enum.GetValues(typeof(CraftShow));

        // Own graphic with a theme role (restyled on theme reloads); false = role missing (degraded).
        private static bool Styled(Graphic g, string role, string reason)
        {
            var mark = DollCell.Mark(g, role, reason);
            mark.Outcome = Restyler.Apply(g, mark, ThemeTokens.Role(role)) ? Outcome.Styled : Outcome.Degraded;
            return mark.Outcome == Outcome.Styled;
        }

        private static string Hex(string token, Color fallback, float alpha = 1f)
        {
            var c = ThemeTokens.GetColor(token, fallback);
            c.a *= alpha;
            return "#" + ColorUtility.ToHtmlStringRGBA(c);
        }

        private static string Num(float v) => Mathf.RoundToInt(v).ToString(System.Globalization.CultureInfo.InvariantCulture);

        private static bool IsFood(ItemDrop.ItemData.SharedData s) =>
            s.m_itemType == ItemDrop.ItemData.ItemType.Consumable && (s.m_food > 0f || s.m_foodStamina > 0f || s.m_foodEitr > 0f);

        private UITooltip Tip(GameObject go, string text)
        {
            var tip = go.AddComponent<UITooltip>();
            var list = _gui.m_recipeRequirementList;
            tip.m_tooltipPrefab = list != null && list.Length > 0 && list[0] && list[0].TryGetComponent<UITooltip>(out var reqTip) ? reqTip.m_tooltipPrefab : null;
            tip.m_topic = "";
            tip.m_text = text;
            return tip;
        }

        // ---------------------------------------------------------------- controls row

        private RectTransform _ctl;
        private TMP_Dropdown _sortDrop;
        private Image _dirGlyph, _newGlyph;
        private TMP_Text _badge;
        private readonly Button[] _showButtons = new Button[3];
        private readonly TMP_Text[] _showLabels = new TMP_Text[3];
        private string _sortLang;

        private Button PlateButton(string name, Action click, out Image plate)
        {
            plate = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
            plate.rectTransform.SetParent(_ctl, false);
            var button = plate.gameObject.AddComponent<Button>();
            button.targetGraphic = plate;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            Styled(plate, "craft.category", "crafting view control plate (square tab: selected = interactable off)");
            button.onClick.AddListener(() => click());
            return button;
        }

        private Image Glyph(RectTransform parent, string role)
        {
            var g = new GameObject("Glyph", typeof(RectTransform)).AddComponent<Image>();
            g.rectTransform.SetParent(parent, false);
            g.raycastTarget = false;
            g.preserveAspect = true;
            g.enabled = Styled(g, role, "crafting view glyph (" + role + ")");
            return g;
        }

        private void BuildControls()
        {
            _ctl = (RectTransform)new GameObject(Prefix + "View", typeof(RectTransform)).transform;
            _ctl.SetParent(_crafting, false);
            var src = CraftLabel();

            // Sort: a TMP_Dropdown built by TMP's own factory, every part on the kit's dropdown roles (M5 dropdown A).
            var drop = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
            drop.name = Prefix + "Sort";
            drop.transform.SetParent(_ctl, false);
            _sortDrop = drop.GetComponent<TMP_Dropdown>();
            _sortDrop.navigation = new Navigation { mode = Navigation.Mode.None };
            foreach (var t in drop.GetComponentsInChildren<TMP_Text>(true))
            {
                if (src)
                {
                    t.font = src.font;
                    t.fontSharedMaterial = src.fontSharedMaterial;
                }
                t.color = ThemeTokens.GetColor("color.text.body", Color.white);
                t.enableAutoSizing = false;
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.overflowMode = TextOverflowModes.Ellipsis;
                SkinMark.Own(t.gameObject, "content", "crafting sort dropdown text (craft label face)");
            }
            var template = _sortDrop.template;
            if (template && template.TryGetComponent<ScrollRect>(out var scroll) && scroll.verticalScrollbar)
            {
                Destroy(scroll.verticalScrollbar.gameObject); // nine keys always fit: the list is sized to them
                scroll.verticalScrollbar = null;
            }
            foreach (var image in drop.GetComponentsInChildren<Image>(true))
            {
                var n = image.name;
                if (image.gameObject == drop) Styled(image, "dropdown.field", "crafting sort dropdown field");
                else if (n == "Arrow") Styled(image, "dropdown.arrow", "crafting sort dropdown chevron");
                else if (n == "Template") Styled(image, "dropdown.list", "crafting sort dropdown list");
                else if (n == "Item Background") Styled(image, "dropdown.item", "crafting sort dropdown item");
                else if (n == "Item Checkmark") Styled(image, "toggle.check", "crafting sort dropdown check");
            }
            _sortDrop.onValueChanged.AddListener(i =>
            {
                if (View() is ICraftingAvailability v && i >= 0 && i < SortKeys.Length) v.Sort = SortKeys[i];
            });

            var dir = PlateButton(Prefix + "SortDir", () => { if (View() is ICraftingAvailability v) v.Descending = !v.Descending; }, out var dirPlate);
            _dirGlyph = Glyph(dirPlate.rectTransform, "craft.glyph.sort.desc");
            Tip(dir.gameObject, "$ui_craft_sort_dir");
            var funnel = PlateButton(Prefix + "NewOnly", () => { if (View() is ICraftingAvailability v) v.NewOnly = !v.NewOnly; }, out var newPlate);
            _newGlyph = Glyph(newPlate.rectTransform, "craft.glyph.filter");
            Tip(funnel.gameObject, "$ui_craft_new_only");
            _badge = NewText("Badge", newPlate.rectTransform, "", "content");
            _badge.fontStyle |= FontStyles.Bold;
            _badge.color = ThemeTokens.GetColor("color.text.accent", Color.cyan);
            for (var i = 0; i < ShowKeys.Length; i++)
            {
                var key = ShowKeys[i];
                _showButtons[i] = PlateButton(Prefix + "Show" + key, () => { if (View() is ICraftingAvailability v) v.Show = key; }, out var plate);
                _showLabels[i] = NewText("Label", plate.rectTransform, "", "text.body");
            }
            Plugin.Log.LogInfo("[Morgott.UI] crafting view controls built");
        }

        private void Controls(CraftingLayout o)
        {
            var view = View();
            if (o.Controls.IsEmpty || view == null)
            {
                if (_ctl && _ctl.gameObject.activeSelf) _ctl.gameObject.SetActive(false);
                return;
            }
            if (!_ctl) BuildControls();
            if (!_ctl.gameObject.activeSelf) _ctl.gameObject.SetActive(true);
            var k = _in.Scale;
            var (sortBox, dirBox, filterBox, showBox) = CraftingMath.ControlRow(o.Controls, o.List.W, _t.Gap * k);
            Geo.Place(_canvas, _ctl, o.Controls);
            var font = ThemeTokens.GetFloat("size.craft.font.body", 16f) * k;

            // Sort dropdown: options follow the language; caption "Sort: <key>" (mockup), list items the bare keys.
            var dropRt = (RectTransform)_sortDrop.transform;
            Geo.Place(_canvas, dropRt, sortBox);
            var lang = Localization.instance.GetSelectedLanguage();
            if (_sortLang != lang)
            {
                _sortLang = lang;
                _sortDrop.ClearOptions();
                var options = new List<string>(SortKeys.Length);
                foreach (var s in SortKeys) options.Add(Localization.instance.Localize("$ui_craft_sort_" + s.ToString().ToLowerInvariant()));
                _sortDrop.AddOptions(options);
            }
            var index = Array.IndexOf(SortKeys, view.Sort);
            if (_sortDrop.value != index) _sortDrop.SetValueWithoutNotify(index);
            var caption = Localization.instance.Localize("$ui_craft_sort") + ": " + _sortDrop.options[Mathf.Max(0, index)].text;
            if (_sortDrop.captionText && _sortDrop.captionText.text != caption) _sortDrop.captionText.text = caption;
            if (_sortDrop.captionText)
            {
                Geo.Place(_canvas, _sortDrop.captionText.rectTransform, new Box(sortBox.X + 8f * k, sortBox.Y, Mathf.Max(1f, sortBox.W - sortBox.H - 8f * k), sortBox.H));
                Label(_sortDrop.captionText, TextAlignmentOptions.MidlineLeft, font);
            }
            var arrow = dropRt.Find("Arrow") as RectTransform;
            if (arrow) Geo.Place(_canvas, arrow, new Box(sortBox.Right - sortBox.H * 0.8f, sortBox.Y + sortBox.H * 0.2f, sortBox.H * 0.6f, sortBox.H * 0.6f));
            var template = _sortDrop.template;
            if (template && !template.gameObject.activeSelf)
            {
                // Closed: size the list template to all keys at the field's row height (local units of the dropdown).
                var unit = dropRt.rect.height / Mathf.Max(1f, sortBox.H);
                var item = template.GetComponentInChildren<Toggle>(true);
                var itemH = sortBox.H * unit;
                if (item) ((RectTransform)item.transform).sizeDelta = new Vector2(0f, itemH);
                template.sizeDelta = new Vector2(0f, itemH * SortKeys.Length + 8f * unit);
                if (_sortDrop.itemText) _sortDrop.itemText.fontSize = font * unit;
            }

            Geo.Place(_canvas, (RectTransform)_dirGlyph.transform.parent, dirBox);
            Geo.Place(_canvas, _dirGlyph.rectTransform, CraftingMath.Inset(dirBox, dirBox.W * 0.2f, dirBox.H * 0.2f));
            var flip = view.Descending ? 1f : -1f; // ascending = the same arrow upside down
            if (!Mathf.Approximately(_dirGlyph.rectTransform.localScale.y, flip)) _dirGlyph.rectTransform.localScale = new Vector3(1f, flip, 1f);

            Geo.Place(_canvas, (RectTransform)_newGlyph.transform.parent, filterBox);
            Geo.Place(_canvas, _newGlyph.rectTransform, CraftingMath.Inset(filterBox, filterBox.H * 0.22f + (filterBox.W - filterBox.H) / 2f, filterBox.H * 0.22f));
            var glyphColor = ThemeTokens.GetColor(view.NewOnly ? "color.text.accent" : "color.text.body", Color.white);
            if (_newGlyph.color != glyphColor) _newGlyph.color = glyphColor;
            var active = CraftingMath.ActiveFilters((int)view.Show, view.NewOnly);
            var badge = active > 0 ? active.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
            if (_badge.text != badge) _badge.text = badge;
            var side = filterBox.H * 0.55f;
            Geo.Place(_canvas, _badge.rectTransform, new Box(filterBox.Right - side * 0.8f, filterBox.Y - side * 0.3f, side, side));
            Label(_badge, TextAlignmentOptions.Center, side * 0.9f);

            var w = showBox.W / ShowKeys.Length;
            for (var i = 0; i < ShowKeys.Length; i++)
            {
                var cell = new Box(showBox.X + i * w, showBox.Y, w, showBox.H);
                var button = _showButtons[i];
                Geo.Place(_canvas, (RectTransform)button.transform, cell);
                var selected = view.Show == ShowKeys[i];
                if (button.interactable == selected) button.interactable = !selected;
                var label = _showLabels[i];
                var text = Localization.instance.Localize("$ui_craft_show_" + ShowKeys[i].ToString().ToLowerInvariant());
                if (label.text != text) label.text = text;
                Geo.Place(_canvas, label.rectTransform, cell);
                Label(label, TextAlignmentOptions.Center, font);
            }
        }

        // ---------------------------------------------------------------- recipe rows

        private sealed class RowView
        {
            public RectTransform Root;
            public TMP_Text Health, Stamina, Eitr, Count, New;
            public Image HealthIcon, StaminaIcon, EitrIcon, Chain;
        }

        private readonly Dictionary<GameObject, RowView> _rowViews = new Dictionary<GameObject, RowView>();
        private readonly List<GameObject> _rowViewsGone = new List<GameObject>();

        private TMP_Text RowText(RowView v, string name, TextAlignmentOptions align)
        {
            var t = NewText(name, v.Root, "", "content");
            t.alignment = align;
            t.overflowMode = TextOverflowModes.Overflow;
            t.enableAutoSizing = false;
            t.richText = true;
            return t;
        }

        private Image RowIcon(RowView v, string name, string role)
        {
            var image = new GameObject(name, typeof(RectTransform)).AddComponent<Image>();
            image.rectTransform.SetParent(v.Root, false);
            image.raycastTarget = false;
            image.preserveAspect = true;
            image.enabled = Styled(image, role, "recipe row stat icon (" + role + ")");
            return image;
        }

        private RowView NewRowView(RectTransform row)
        {
            var v = new RowView { Root = (RectTransform)new GameObject(Prefix + "RowView", typeof(RectTransform)).transform };
            v.Root.SetParent(row, false);
            v.Root.anchorMin = Vector2.zero;
            v.Root.anchorMax = Vector2.one;
            v.Root.offsetMin = v.Root.offsetMax = Vector2.zero;
            v.Health = RowText(v, "Health", TextAlignmentOptions.MidlineLeft);
            v.HealthIcon = RowIcon(v, "HealthIcon", "craft.stat.health");
            v.Stamina = RowText(v, "Stamina", TextAlignmentOptions.MidlineLeft);
            v.StaminaIcon = RowIcon(v, "StaminaIcon", "craft.stat.stamina");
            v.Eitr = RowText(v, "Eitr", TextAlignmentOptions.MidlineLeft);
            v.EitrIcon = RowIcon(v, "EitrIcon", "craft.stat.eitr");
            v.Count = RowText(v, "Count", TextAlignmentOptions.MidlineRight);
            v.New = RowText(v, "New", TextAlignmentOptions.MidlineRight);
            v.Chain = RowIcon(v, "Chain", "craft.glyph.chain");
            return v;
        }

        private static void At(RectTransform rt, Vector2 anchor, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private static void Shown(Component c, bool on)
        {
            if (c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
        }

        // One stat of the sub-line: value then its icon, from x; returns the next x.
        private static float Stat(TMP_Text text, Image icon, bool on, string value, float x, float y, float h, float size)
        {
            Shown(text, on);
            Shown(icon, on);
            if (!on) return x;
            text.fontSize = size;
            if (text.text != value) text.text = value;
            var w = text.GetPreferredValues(value).x + 1f;
            At(text.rectTransform, Vector2.zero, x, y, w, h);
            var side = h * 0.8f;
            At(icon.rectTransform, Vector2.zero, x + w + 1f, y + (h - side) / 2f, side, side);
            return x + w + side + 8f;
        }

        // After every Rows() pass that touched the rows (rebuild, pitch, theme): the Create tab rows get the sub-line
        // (food stats, count), NEW and the chain link, the name moves to the upper part; other rows / tabs lose it.
        private void RowViews(List<InventoryGui.RecipeDataPair> rows)
        {
            var view = View();
            var on = view != null && view.ControlsShown;
            if (_rowViews.Count > 4 * Mathf.Max(64, rows.Count))
            {
                foreach (var key in _rowViews.Keys) if (!key) _rowViewsGone.Add(key);
                foreach (var key in _rowViewsGone) _rowViews.Remove(key);
                _rowViewsGone.Clear();
            }
            var cap = CountCap();
            var sort = on ? view.Sort : CraftSort.Default;
            var size = ThemeTokens.GetFloat("size.craft.font.row", 16f) * _in.Scale * 0.8f;
            var newText = Localization.instance.Localize("$ui_craft_new");
            foreach (var r in rows)
            {
                var e = r.InterfaceElement;
                if (!e) continue;
                _rowViews.TryGetValue(e, out var v);
                if (!on || r.ItemData != null || !r.Recipe || !r.Recipe.m_item)
                {
                    if (v != null) Shown(v.Root, false);
                    continue;
                }
                var row = (RectTransform)e.transform;
                if (v == null) _rowViews[e] = v = NewRowView(row);
                Shown(v.Root, true);
                v.Root.SetAsLastSibling();
                var h = row.rect.height;
                var left = h - 10f + 16f; // StyleRow: icon h - 10 at x 8, name from icon + 16
                var subH = h * 0.42f;
                var shared = r.Recipe.m_item.m_itemData.m_shared;
                var food = IsFood(shared);
                string Value(float value, string color, bool active) =>
                    "<color=" + Hex(color, Color.white, active ? 1f : 0.72f) + ">" + (active ? "<b>" + Num(value) + "</b>" : Num(value)) + "</color>";
                var x = Stat(v.Health, v.HealthIcon, food, Value(shared.m_food, "color.hud.health", sort == CraftSort.Health), left, 2f, subH, size);
                x = Stat(v.Stamina, v.StaminaIcon, food, Value(shared.m_foodStamina, "color.hud.stamina", sort == CraftSort.Stamina), x, 2f, subH, size);
                Stat(v.Eitr, v.EitrIcon, food && shared.m_foodEitr > 0f, Value(shared.m_foodEitr, "color.craft.stat.eitr", sort == CraftSort.Eitr), x, 2f, subH, size);

                var count = view.GetMaxCrafts(r.Recipe, 1, cap);
                var countText = "<color=" + Hex(count > 0 ? "color.stats.good" : "color.state.disabled", Color.white) + ">" + CraftingMath.CountLabel(count, cap) + "</color>";
                v.Count.fontSize = size;
                if (v.Count.text != countText) v.Count.text = countText;
                At(v.Count.rectTransform, new Vector2(1f, 0f), -6f, 2f, 64f, subH);

                // Top right: NEW (gold), the chain link left of it.
                var isNew = view.IsNew(r.Recipe);
                Shown(v.New, isNew);
                var right = 6f;
                if (isNew)
                {
                    var tag = "<color=" + Hex("color.text.display.gold", Color.yellow) + ">" + newText + "</color>";
                    v.New.fontSize = size * 0.85f;
                    if (v.New.text != tag) v.New.text = tag;
                    var w = v.New.GetPreferredValues(tag).x + 2f;
                    At(v.New.rectTransform, Vector2.one, -right, -3f, w, h * 0.38f);
                    right += w + 4f;
                }
                var chain = RowStock(row) == CraftingMath.Stock.Chain;
                Shown(v.Chain, chain && v.Chain.enabled);
                if (chain)
                {
                    var side = h * 0.34f;
                    At(v.Chain.rectTransform, Vector2.one, -right, -3f, side, side);
                    right += side + 4f;
                }
                if (row.Find("name") is RectTransform name)
                    Anchors(name, new Vector2(0f, 0.42f), Vector2.one, new Vector2(left, 0f), new Vector2(-right, -2f));
            }
        }

        // ---------------------------------------------------------------- detail stat table

        private RectTransform _stats;
        private readonly List<(TMP_Text Label, TMP_Text Value)> _statRows = new List<(TMP_Text, TMP_Text)>();
        private TMP_Text _bonus;
        private int _statKey;
        private readonly List<(string Label, string Value)> _statLines = new List<(string, string)>();
        private string _bonusText = "";

        // Food stats, yield and the craftable count under the description (above the chain box / requirements), plus the
        // bonus portion chance line; returns the description's remaining box.
        private Box StatTable(Box d, float k)
        {
            var view = View();
            var recipe = _gui.m_selectedRecipe.Recipe;
            var player = Player.m_localPlayer;
            if (view == null || !view.ControlsShown || !recipe || !recipe.m_item || _gui.m_selectedRecipe.ItemData != null || !player)
            {
                if (_stats && _stats.gameObject.activeSelf) _stats.gameObject.SetActive(false);
                return d;
            }
            var detail = _crafting.Find("Decription") as RectTransform;
            if (!detail) return d;
            if (!_stats)
            {
                _stats = (RectTransform)new GameObject(Prefix + "Stats", typeof(RectTransform)).transform;
                _stats.SetParent(detail, false);
                _bonus = NewText("Bonus", _stats, "", "content");
            }
            if (!_stats.gameObject.activeSelf) _stats.gameObject.SetActive(true);

            var shared = recipe.m_item.m_itemData.m_shared;
            var cap = CountCap();
            var count = view.GetMaxCrafts(recipe, 1, cap);
            var station = Station();
            var skill = station && station.m_craftingSkill != Skills.SkillType.None;
            var bonus = CraftingMath.BonusPercent(skill ? player.GetSkillFactor(station.m_craftingSkill) : 0f, _gui.m_craftBonusChance, shared.m_maxStackSize, skill);
            var key = ((recipe.GetInstanceID() * 31 + count) * 31 + bonus) * 31 + Localization.instance.GetSelectedLanguage().GetHashCode() + ThemeTokens.Revision * 7;
            if (key != _statKey)
            {
                _statKey = key;
                _statLines.Clear();
                string L(string token) => Localization.instance.Localize(token);
                string C(string token, string text) => "<color=" + Hex(token, Color.white) + ">" + text + "</color>";
                if (IsFood(shared))
                {
                    _statLines.Add((L("$ui_craft_stat_health"), C("color.hud.health", Num(shared.m_food))));
                    _statLines.Add((L("$ui_craft_stat_stamina"), C("color.hud.stamina", Num(shared.m_foodStamina))));
                    _statLines.Add((L("$ui_craft_stat_eitr"), shared.m_foodEitr > 0f ? C("color.craft.stat.eitr", Num(shared.m_foodEitr)) : "0"));
                    _statLines.Add((L("$ui_craft_stat_regen"), Localization.instance.Localize("$ui_craft_regen_value",
                        shared.m_foodRegen.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))));
                    _statLines.Add((L("$ui_craft_stat_duration"), Localization.instance.Localize("$ui_craft_minutes", Num(shared.m_foodBurnTime / 60f))));
                }
                _statLines.Add((L("$ui_craft_stat_yield"), "×" + Mathf.Max(1, recipe.m_amount)));
                _statLines.Add((L("$ui_craft_stat_craftable"), C(count > 0 ? "color.stats.good" : "color.state.disabled", CraftingMath.CountLabel(count, cap))));
                _bonusText = bonus > 0 ? C("color.text.accent", Localization.instance.Localize("$ui_craft_bonus", bonus.ToString(System.Globalization.CultureInfo.InvariantCulture))) : "";
                while (_statRows.Count < _statLines.Count)
                    _statRows.Add((NewText("Label", _stats, "", "text.body"), NewText("Value", _stats, "", "content")));
                for (var i = 0; i < _statRows.Count; i++)
                {
                    var on = i < _statLines.Count;
                    Shown(_statRows[i].Label, on);
                    Shown(_statRows[i].Value, on);
                    if (!on) continue;
                    _statRows[i].Label.text = _statLines[i].Label;
                    _statRows[i].Value.text = _statLines[i].Value;
                    _statRows[i].Value.richText = true;
                }
                _bonus.richText = true;
                _bonus.text = _bonusText;
            }

            var size = ThemeTokens.GetFloat("size.craft.font.body", 16f) * k;
            var rowH = size * 1.4f;
            var lines = _statLines.Count + (_bonusText.Length > 0 ? 1 : 0);
            var top = Mathf.Max(d.Y, d.Bottom - lines * rowH);
            Geo.Place(_canvas, _stats, new Box(d.X, top, d.W, d.Bottom - top));
            for (var i = 0; i < _statLines.Count; i++)
            {
                var b = new Box(d.X, top + i * rowH, d.W, rowH);
                Geo.Place(_canvas, _statRows[i].Label.rectTransform, b);
                Label(_statRows[i].Label, TextAlignmentOptions.MidlineLeft, size);
                Geo.Place(_canvas, _statRows[i].Value.rectTransform, b);
                Label(_statRows[i].Value, TextAlignmentOptions.MidlineRight, size);
            }
            Shown(_bonus, _bonusText.Length > 0);
            if (_bonusText.Length > 0)
            {
                Geo.Place(_canvas, _bonus.rectTransform, new Box(d.X, top + _statLines.Count * rowH, d.W, rowH));
                Label(_bonus, TextAlignmentOptions.MidlineLeft, size);
            }
            return new Box(d.X, d.Y, d.W, Mathf.Max(0f, top - _t.Gap * k * 0.5f - d.Y));
        }
    }
}
