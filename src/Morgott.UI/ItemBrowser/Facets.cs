using System;
using System.Collections.Generic;
using Morgott.Core;

namespace Morgott.UI.ItemBrowser
{
    /// <summary>One filter dimension: value key -> label (token or literal), entry -> its value keys.</summary>
    public sealed class Facet
    {
        public const string NoneKey = "";
        public readonly string Id;
        public readonly bool Multi;
        public readonly Dictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly Dictionary<string, List<string>> ByEntry = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        public Facet(string id, bool multi)
        {
            Id = id;
            Multi = multi;
        }

        public bool Has(string entry, HashSet<string> wanted)
        {
            if (!ByEntry.TryGetValue(entry, out var keys)) return wanted.Contains(NoneKey);
            foreach (var k in keys) if (wanted.Contains(k)) return true;
            return false;
        }
    }

    /// <summary>A chip of the facet row: its label + the facets its popup lists, one section each.</summary>
    public sealed class FacetChip
    {
        public readonly string Token;
        public readonly string[] Facets;

        public FacetChip(string token, params string[] facets)
        {
            Token = token;
            Facets = facets;
        }
    }

    /// <summary>What the user picked in one scope (category / tab); kept for the session.</summary>
    public sealed class FilterState
    {
        public int Plaques; // bit = StageRules plaque index
        public readonly Dictionary<string, HashSet<string>> Picked = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        public bool Any
        {
            get
            {
                if (Plaques != 0) return true;
                foreach (var kv in Picked) if (kv.Value.Count > 0) return true;
                return false;
            }
        }

        public void Toggle(string facet, string key, bool multi)
        {
            if (!Picked.TryGetValue(facet, out var set)) Picked[facet] = set = new HashSet<string>(StringComparer.Ordinal);
            if (set.Remove(key)) return;
            if (!multi) set.Clear();
            set.Add(key);
        }

        public void Clear()
        {
            Plaques = 0;
            Picked.Clear();
        }
    }

    /// <summary>Stage strip / list grouping rules (DESIGN.md section 2).</summary>
    public static class StageRules
    {
        public const int PlaqueUnknown = 8, PlaqueMagic = 9, GroupMagic = 16, GroupUnknown = 17, Groups = 18;

        public static int Plaque(StageInfo i) => i.Via == StageVia.Magic ? PlaqueMagic : i.Stage < 0 ? PlaqueUnknown : i.Biome;

        /// <summary>List order: stage (after-boss right behind its biome), Magic, Unknown last.</summary>
        public static int Group(StageInfo i) => i.Via == StageVia.Magic ? GroupMagic : i.Stage < 0 ? GroupUnknown : i.Stage;

        public static bool PassesStrip(int plaques, StageInfo i) => plaques == 0 || (plaques & (1 << Plaque(i))) != 0;

        /// <summary>Food focus: eitr if largest, balanced when health and stamina are within 20 %, else the larger.</summary>
        public static string FoodFocus(float health, float stamina, float eitr)
        {
            if (eitr > 0f && eitr >= health && eitr >= stamina) return "eitr";
            var hi = Math.Max(health, stamina);
            if (hi <= 0f) return null;
            return Math.Min(health, stamina) >= hi * 0.8f ? "balanced" : health > stamina ? "health" : "stamina";
        }

        public static string SourceKey(RelationKind k)
        {
            switch (k)
            {
                case RelationKind.Upgrade: return null; // never a first source
                case RelationKind.KillDrop: return "creaturedrop";
                default: return k.ToString().ToLowerInvariant();
            }
        }
    }

    /// <summary>All facets of one index generation + which chips each scope shows. No Unity calls.</summary>
    public sealed class FacetTable
    {
        public readonly Dictionary<string, Facet> Facets = new Dictionary<string, Facet>(StringComparer.Ordinal);

        private static readonly HashSet<string> MultiIds = new HashSet<string>(StringComparer.Ordinal) { "damage", "source", "kind", "habitat", "usedin", "loot" };

        public void Add(string facet, string entry, string key, string label)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (!Facets.TryGetValue(facet, out var f)) Facets[facet] = f = new Facet(facet, MultiIds.Contains(facet));
            f.Labels[key] = label;
            if (!f.ByEntry.TryGetValue(entry, out var keys)) f.ByEntry[entry] = keys = new List<string>(1);
            if (!keys.Contains(key)) keys.Add(key);
        }

        /// <summary>AND between facets, OR inside one.</summary>
        public bool Matches(string entry, FilterState state)
        {
            foreach (var kv in state.Picked)
                if (kv.Value.Count > 0 && Facets.TryGetValue(kv.Key, out var f) && !f.Has(entry, kv.Value)) return false;
            return true;
        }

        private static readonly FacetChip Mod = new FacetChip("$ib_f_mod", "mod");
        private static readonly FacetChip[] Plain = { Mod };

        private static readonly Dictionary<string, FacetChip[]> Chips = new Dictionary<string, FacetChip[]>(StringComparer.Ordinal)
        {
            ["weapons"] = new[] { new FacetChip("$ib_f_class", "class", "grip"), new FacetChip("$ib_f_damage", "damage"), new FacetChip("$ib_f_source", "source"), Mod },
            ["armor"] = new[] { new FacetChip("$ib_f_line", "line", "set", "mod"), new FacetChip("$ib_f_slot", "slot"), new FacetChip("$ib_f_tier", "tier") },
            ["utility"] = new[] { new FacetChip("$ib_f_type", "type"), Mod },
            ["ammo"] = new[] { new FacetChip("$ib_f_type", "type"), Mod },
            ["tools"] = new[] { new FacetChip("$ib_f_type", "type"), new FacetChip("$ib_f_level", "level"), Mod },
            ["food"] = new[] { new FacetChip("$ib_f_focus", "focus"), new FacetChip("$ib_f_station", "station"), new FacetChip("$ib_f_duration", "duration") },
            ["meads"] = new[] { new FacetChip("$ib_f_effect", "effect"), new FacetChip("$ib_f_station", "station"), Mod },
            ["materials"] = new[] { new FacetChip("$ib_f_source", "source"), new FacetChip("$ib_f_usedin", "usedin"), Mod },
            ["trophies"] = new[] { new FacetChip("$ib_f_source", "source", "creature"), Mod },
            ["fish"] = new[] { new FacetChip("$ib_f_source", "source", "creature"), Mod },
            ["misc"] = new[] { new FacetChip("$ib_f_source", "source"), Mod },
            ["piece"] = new[] { new FacetChip("$ib_f_section", "section"), new FacetChip("$ib_f_station", "station"), new FacetChip("$ib_f_comfort", "comfort"), Mod },
            ["station"] = new[] { new FacetChip("$ib_f_kind", "stationkind"), Mod },
            ["creature"] = new[] { new FacetChip("$ib_f_kind", "kind"), new FacetChip("$ib_f_habitat", "habitat"), new FacetChip("$ib_f_loot", "loot"), new FacetChip("$ib_f_faction", "faction") },
        };

        public static FacetChip[] ChipsOf(string scope) => Chips.TryGetValue(scope, out var c) ? c : Plain;

        /// <summary>Facets computable from the index alone: mod, sources, where used, creature loot / source.</summary>
        public void AddIndexFacets(BrowserIndex ix, Entry e, Func<string, string> modName)
        {
            var mod = e.Mod ?? "valheim";
            Add("mod", e.Id, mod, modName(e.Mod));
            foreach (var r in ix.ObtainedFrom(e.Id))
            {
                if (StageRules.SourceKey(r.Kind) is string k) Add("source", e.Id, k, "$ib_kind_" + k);
                if ((r.Kind == RelationKind.CreatureDrop || r.Kind == RelationKind.KillDrop) && ix.Entries.TryGetValue(r.Source, out var c) && c.Type == EntryType.Creature)
                    Add("creature", e.Id, c.Id, c.Name);
            }
            foreach (var r in ix.UsedIn(e.Id))
            {
                if (r.Kind == RelationKind.Upgrade || r.Source == e.Id) continue;
                if (r.Station != null && ix.Entries.TryGetValue(r.Station, out var st)) Add("usedin", e.Id, st.Id, st.Name);
            }
            if (e.Type != EntryType.Creature) return;
            foreach (var r in ix.UsedIn(e.Id))
                if (r.Source == e.Id && (r.Kind == RelationKind.CreatureDrop || r.Kind == RelationKind.KillDrop))
                    foreach (var o in r.Outputs)
                        if (ix.Entries.TryGetValue(o.Prefab, out var item) && item.Type == EntryType.Item)
                            Add("loot", e.Id, item.Category.ToString().ToLowerInvariant(), "$ib_cat_" + CatKey(item.Category));
        }

        public static string CatKey(ItemCategory c) => c == ItemCategory.Utility ? "utility" : c.ToString().ToLowerInvariant();

        public static string Scope(int type, int category, string[] catKeys)
        {
            switch (type)
            {
                case 1: return category >= 0 ? catKeys[category] : "items";
                case 2: return "piece";
                case 3: return "creature";
                case 4: return "station";
                case 5: return "favorites";
                default: return "all";
            }
        }
    }

    /// <summary>Flat display of a filtered list: stage headers + rows, one scroll unit each. Rebuilt with the results only.</summary>
    public struct ListBlock
    {
        public int Group, From, Count; // Count < 0: header of Group with -Count entries
        public bool Header => Count < 0;
    }

    public static class ListBlocks
    {
        /// <summary>Stable bucket order by stage group; list keeps the search order inside a group.</summary>
        public static List<Entry> Order(List<Entry> list, Func<string, StageInfo> info)
        {
            var buckets = new List<Entry>[StageRules.Groups];
            foreach (var e in list)
            {
                var g = StageRules.Group(info(e.Id));
                (buckets[g] ??= new List<Entry>()).Add(e);
            }
            var r = new List<Entry>(list.Count);
            foreach (var b in buckets) if (b != null) r.AddRange(b);
            return r;
        }

        /// <summary>Headers + rows of perRow entries (1 = list, 6 = grid).</summary>
        public static List<ListBlock> Build(List<Entry> ordered, Func<string, StageInfo> info, int perRow)
        {
            var blocks = new List<ListBlock>();
            for (var i = 0; i < ordered.Count;)
            {
                var g = StageRules.Group(info(ordered[i].Id));
                var end = i;
                while (end < ordered.Count && StageRules.Group(info(ordered[end].Id)) == g) end++;
                blocks.Add(new ListBlock { Group = g, From = i, Count = -(end - i) });
                for (; i < end; i += Math.Min(perRow, end - i)) blocks.Add(new ListBlock { Group = g, From = i, Count = Math.Min(perRow, end - i) });
            }
            return blocks;
        }
    }
}
