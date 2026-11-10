using System;
using System.Collections.Generic;
using Morgott.Core;

namespace Morgott.UI.ItemBrowser
{
    // Category row under "Items" (mockup docs\mockups\2026-11-04\04-items-right-panel.png), enum order = button order.
    public enum ItemCategory { Weapons, Armor, Utility, Ammo, Tools, Food, Meads, Materials, Trophies, Fish, Misc }

    // Item page section a hover key opens (ItemBrowser.RecipeKey / UsedInKey).
    public enum PageSection { None, Recipe, UsedIn }

    /// <summary>One row of the item page: an "Obtained from" or "Used in" relation as the page draws it.</summary>
    public sealed class PageRow
    {
        public Relation R;
        public string Icon;      // prefab heading the row (the item made / dropped, or the source of a drop)
        public int Quality;      // > 1: the row is an upgrade to this quality
        public Stack[] Inputs;   // an upgrade starts with the item one quality lower x1
        public int BaseQuality;  // quality of Inputs[0] when it is that upgrade base, else 0
        public string Right;     // station / tool / trader drawn on the right, null = by hand or none
        public bool Recipe;      // inputs + station layout; false = source name + chance
        public string Tag;       // source row: readable prefab name when another source has the same name but other drops

        public int Lines => Recipe ? Math.Max(1, (Inputs.Length + 1) / 2) : 1;
    }

    // Unity-free item page model (docs\item-browser-design.md section 11): categories, the page's rows per quality, recipe
    // variants (also the Ctrl take), the row-height scroll.
    public static class PageModel
    {
        public const float RowH = 61f, UsedRowH = 83f, LineH = 34f, InputsPerLine = 2;

        // Tools first (a weapon whose skill is Pickaxes / Fishing is a tool), then the item type.
        public static ItemCategory Categorize(ItemDrop.ItemData.ItemType type, Skills.SkillType skill, bool buildPieces, float food, float stamina, float eitr, bool consumeEffect)
        {
            if (type == ItemDrop.ItemData.ItemType.Tool || buildPieces || skill == Skills.SkillType.Pickaxes && Weapon(type) || skill == Skills.SkillType.Fishing && Weapon(type))
                return ItemCategory.Tools;
            if (Weapon(type) || type == ItemDrop.ItemData.ItemType.Shield || type == ItemDrop.ItemData.ItemType.Torch) return ItemCategory.Weapons;
            switch (type)
            {
                case ItemDrop.ItemData.ItemType.Helmet:
                case ItemDrop.ItemData.ItemType.Chest:
                case ItemDrop.ItemData.ItemType.Legs:
                case ItemDrop.ItemData.ItemType.Hands:
                case ItemDrop.ItemData.ItemType.Shoulder: return ItemCategory.Armor;
                case ItemDrop.ItemData.ItemType.Utility:
                case ItemDrop.ItemData.ItemType.Trinket: return ItemCategory.Utility;
                case ItemDrop.ItemData.ItemType.Ammo:
                case ItemDrop.ItemData.ItemType.AmmoNonEquipable: return ItemCategory.Ammo;
                case ItemDrop.ItemData.ItemType.Consumable:
                    return food > 0f || stamina > 0f || eitr > 0f ? ItemCategory.Food : consumeEffect ? ItemCategory.Meads : ItemCategory.Misc;
                case ItemDrop.ItemData.ItemType.Material: return ItemCategory.Materials;
                case ItemDrop.ItemData.ItemType.Trophy: return ItemCategory.Trophies;
                case ItemDrop.ItemData.ItemType.Fish: return ItemCategory.Fish;
                default: return ItemCategory.Misc;
            }
        }

        private static bool Weapon(ItemDrop.ItemData.ItemType t) =>
            t == ItemDrop.ItemData.ItemType.OneHandedWeapon || t == ItemDrop.ItemData.ItemType.TwoHandedWeapon || t == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft ||
            t == ItemDrop.ItemData.ItemType.Bow || t == ItemDrop.ItemData.ItemType.Attach_Atgeir;

        private static bool IsRecipe(RelationKind k) => k == RelationKind.Craft || k == RelationKind.Upgrade;

        // Recipes first, then conversions / builds, then every other source (index order inside each rank).
        private static int Rank(RelationKind k) => IsRecipe(k) ? 0 : k == RelationKind.Conversion ? 1 : k == RelationKind.Build ? 2 : 3;

        /// <summary>The recipes that make the item at this quality (the variant buttons and the Ctrl take).</summary>
        public static List<Relation> Variants(BrowserIndex ix, string id, int quality)
        {
            var list = new List<Relation>();
            foreach (var r in ix.ObtainedFrom(id))
                if (r.Kind == RelationKind.Craft ? quality == 1 : r.Kind == RelationKind.Upgrade && r.Quality == quality) list.Add(r);
            return list;
        }

        /// <summary>"Obtained from" at a quality: its recipes; every other source only at quality 1.</summary>
        public static List<PageRow> Obtain(BrowserIndex ix, string id, int quality)
        {
            var rows = new List<PageRow>();
            foreach (var r in ix.ObtainedFrom(id))
            {
                if (r.Kind == RelationKind.Upgrade ? r.Quality != quality : quality != 1) continue;
                rows.Add(r.Inputs.Length > 0 || IsRecipe(r.Kind)
                    ? Recipe(r, id)
                    : new PageRow { R = r, Icon = r.Source, Inputs = Array.Empty<Stack>(), Right = null });
            }
            Dedupe(ix, id, rows);
            // Stable sort by rank (List.Sort is not stable).
            var ordered = new List<PageRow>(rows.Count);
            for (var rank = 0; rank <= 3; rank++) foreach (var row in rows) if (Rank(row.R.Kind) == rank) ordered.Add(row);
            return ordered;
        }

        /// <summary>Pages whose second section is loot ("Loot", not "Used in"): creatures and Epic Loot tables / sets.</summary>
        public static bool LootPage(EntryType type, string id) => type == EntryType.Creature || SourceNames.EpicLootPseudo(id);

        /// <summary>Creature tooltip text: its description, then "$ib_tameable" and "$ib_breedable" lines (tokens; UITooltip localizes).</summary>
        public static string CreatureTip(string desc, bool tame, bool breed)
        {
            if (!tame) return desc;
            var lines = breed ? "$ib_tameable\n$ib_breedable" : "$ib_tameable";
            return string.IsNullOrEmpty(desc) ? lines : desc + "\n\n" + lines;
        }

        /// <summary>Source row label: "Grows from: calf" for a Growup source, the readable prefab tag when names collide.</summary>
        public static string SourceLabel(PageRow r, string name, string growsFrom)
        {
            if (r.Tag != null) name = $"{name} ({r.Tag})";
            return r.R.Kind == RelationKind.Grow ? $"{growsFrom} {name}" : name;
        }

        // Source rows: prefabs with one name and the same yield of this item (Bjorn + Bjorn_sleeping) are one row; one name
        // with different yields keeps every row, each tagged with its readable prefab name.
        private static void Dedupe(BrowserIndex ix, string id, List<PageRow> rows)
        {
            string NameKey(PageRow r) => (int)r.R.Kind + "|" + (ix.Entries.TryGetValue(r.R.Source, out var e) ? e.Name : r.R.Source);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var names = new Dictionary<string, int>(StringComparer.Ordinal);
            rows.RemoveAll(r =>
            {
                if (r.Recipe) return false;
                var sb = new System.Text.StringBuilder(NameKey(r)).Append('|').Append(r.R.Conditions);
                foreach (var s in r.R.Outputs)
                    if (s.Prefab == id) sb.Append('|').Append(s.Min).Append('-').Append(s.Max).Append('@').Append(s.Chance.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                return !seen.Add(sb.ToString());
            });
            foreach (var r in rows) if (!r.Recipe) names[NameKey(r)] = names.TryGetValue(NameKey(r), out var n) ? n + 1 : 1;
            foreach (var r in rows) if (!r.Recipe && names[NameKey(r)] > 1) r.Tag = SourceNames.Readable(r.R.Source);
        }

        /// <summary>"Used in": a row for every relation the entry takes part in, its own upgrades first (all qualities).</summary>
        public static List<PageRow> UsedIn(BrowserIndex ix, string id)
        {
            var rows = new List<PageRow>();
            foreach (var r in ix.ObtainedFrom(id)) if (r.Kind == RelationKind.Upgrade) rows.Add(Recipe(r, id));
            // One row per result: a loot table / conversion with several outputs lists each (Codex review).
            // A cost sink is its own output (Morgott.EpicLoot `EL:enchant:Rare`): its page lists it under its source, not again here.
            foreach (var r in ix.UsedIn(id)) foreach (var o in r.Outputs) if (o.Prefab != id) rows.Add(Recipe(r, o.Prefab, id));
            return rows;
        }

        // item = what the row makes; self = the page's entry (a drop of the page's own creature names no source on the right).
        private static PageRow Recipe(Relation r, string item, string self = null)
        {
            var up = r.Kind == RelationKind.Upgrade;
            var ins = r.Inputs;
            if (up)
            {
                ins = new Stack[r.Inputs.Length + 1];
                ins[0] = new Stack(item, 1, 1);
                Array.Copy(r.Inputs, 0, ins, 1, r.Inputs.Length);
            }
            var right = r.Station ?? (IsRecipe(r.Kind) || r.Source == self ? null : r.Source);
            return new PageRow { R = r, Icon = item, Quality = up ? r.Quality : 0, Inputs = ins, BaseQuality = up ? r.Quality - 1 : 0, Right = right, Recipe = true };
        }

        // lines = input lines as laid out (the panel flows chips at rendered widths); 0 = two per line.
        public static float Height(PageRow row, bool used, int lines = 0) => (used ? UsedRowH : RowH) + LineH * ((lines > 0 ? lines : row.Lines) - 1);

        /// <summary>Materials of a recipe row (Ctrl take): prefab, amount, quality (the upgrade base at quality - 1).</summary>
        public static List<(string Prefab, int Amount, int Quality)> Materials(PageRow row)
        {
            var list = new List<(string, int, int)>();
            for (var i = 0; i < row.Inputs.Length; i++)
                list.Add((row.Inputs[i].Prefab, row.Inputs[i].Min, i == 0 && row.BaseQuality > 0 ? row.BaseQuality : 1));
            return list;
        }

        /// <summary>Ctrl take of an item at a quality with the chosen variant (clamped); null when nothing makes it.</summary>
        public static List<(string Prefab, int Amount, int Quality)> Materials(BrowserIndex ix, string id, int quality, int variant)
        {
            var v = Variants(ix, id, quality);
            return v.Count == 0 ? null : Materials(Recipe(v[Math.Max(0, Math.Min(variant, v.Count - 1))], id));
        }

        /// <summary>First block index whose tail fits the view: the largest scroll (0 when everything fits).</summary>
        // shortcut: whole-block steps; a single row taller than the view (30+ inputs) shows from its top only, upgrade to
        // a unit offset scroll if such a recipe appears.
        public static int MaxFirst(IList<float> heights, float view)
        {
            var sum = 0f;
            for (var i = heights.Count - 1; i >= 0; i--)
            {
                sum += heights[i];
                if (sum > view) return Math.Min(i + 1, heights.Count - 1); // a block taller than the view still shows alone
            }
            return 0;
        }

        /// <summary>Amounts a CharacterDrop can roll: GenerateDropList (CharacterDrop.cs:139) takes int Random.Range(min, max),
        /// max exclusive; min when equal.</summary>
        // shortcut: a reversed pair (no vanilla drop has one) shows min only, as before.
        public static (int Min, int Max) DropRange(int min, int max) => max > min ? (min, max - 1) : (min, min);

        /// <summary>The KnownOnly filter applies unless devcommands is on (then every entry is listed).</summary>
        public static bool KnownFilter(bool knownOnly, bool devcommands) => knownOnly && !devcommands;

        /// <summary>Vanilla Recipe.GetRequiredStationLevel (Recipe.cs): max(1, min level) + quality - 1.</summary>
        public static int StationLevel(int minStationLevel, int quality) => Math.Max(1, minStationLevel) + quality - 1;

        /// <summary>Hover key over an item: recipe key wins over used-in; nothing while a text field / chat has focus.</summary>
        public static PageSection HoverKey(bool recipe, bool usedIn, bool typing) =>
            typing ? PageSection.None : recipe ? PageSection.Recipe : usedIn ? PageSection.UsedIn : PageSection.None;

        /// <summary>Core IItemBrowser section -> page section ("Obtained from" = recipe / sources at the top).</summary>
        public static PageSection SectionOf(ItemPageSection s) => s == ItemPageSection.UsedIn ? PageSection.UsedIn : PageSection.Recipe;

        /// <summary>Page scroll (block index) a hover key opens at: the "Used in" header block, else the top.</summary>
        public static int SectionScroll(PageSection s, int usedHeaderBlock, int maxScroll) =>
            s == PageSection.UsedIn ? Math.Max(0, Math.Min(usedHeaderBlock, maxScroll)) : 0;

        /// <summary>Variant button labels: station + level, a repeated label becomes "variant N".</summary>
        public static List<string> VariantLabels(List<Relation> variants, Func<string, string> name, string byHand, string level, string variant)
        {
            var labels = new List<string>();
            for (var i = 0; i < variants.Count; i++)
            {
                var r = variants[i];
                var s = r.Station == null ? byHand : r.StationLevel > 0 ? $"{name(r.Station)} {level}{r.StationLevel}" : name(r.Station);
                labels.Add(labels.Contains(s) ? $"{variant} {i + 1}" : s);
            }
            return labels;
        }
    }
}
