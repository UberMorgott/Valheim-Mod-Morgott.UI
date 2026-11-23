using System;
using System.Collections.Generic;
using Morgott.Core;

namespace Morgott.UI.ItemBrowser
{
    /// <summary>A place a prefab exists: spawn list / vegetation (Open) or a location / dungeon room.</summary>
    public readonly struct StageAnchor
    {
        public readonly Heightmap.Biome Biomes;
        public readonly bool Open;
        public readonly string Key;

        public StageAnchor(Heightmap.Biome biomes, bool open, string key)
        {
            Biomes = biomes;
            Open = open;
            Key = string.IsNullOrEmpty(key) ? null : key;
        }
    }

    public readonly struct StageTool
    {
        public readonly string Prefab;
        public readonly bool Pick, Chop;
        public readonly int Tier;

        public StageTool(string prefab, bool pick, bool chop, int tier)
        {
            Prefab = prefab;
            Pick = pick;
            Chop = chop;
            Tier = tier;
        }
    }

    /// <summary>World facts behind the stage rule, filled by FoundInScan / StageScan. No Unity calls.</summary>
    public sealed class StageWorld
    {
        public readonly Dictionary<string, List<StageAnchor>> Anchors = new Dictionary<string, List<StageAnchor>>(StringComparer.Ordinal);
        public readonly Dictionary<string, List<string>> Defeats = new Dictionary<string, List<string>>(StringComparer.Ordinal); // global key -> creatures setting it
        public readonly Dictionary<string, List<string>> Extensions = new Dictionary<string, List<string>>(StringComparer.Ordinal); // station -> extension pieces
        public readonly HashSet<string> Bosses = new HashSet<string>(StringComparer.Ordinal);
        public readonly List<(string Boss, string Item, Heightmap.Biome Altar)> Summons = new List<(string, string, Heightmap.Biome)>();
        public readonly List<(string Item, string Creature)> ItemSpawns = new List<(string, string)>(); // an item's attack summons it
        public readonly List<StageTool> Tools = new List<StageTool>();
        public readonly List<(string Item, string Producer, string Root)> Tapped = new List<(string, string, string)>(); // SapCollector on its root

        /// <summary>Locations / rooms read without their placed objects (failed load, old line) -> their biomes. A source may be
        /// missing there, so a derived stage above the lowest of these biomes could be too late (<see cref="StaleCap"/>).</summary>
        public readonly Dictionary<string, Heightmap.Biome> Stale = new Dictionary<string, Heightmap.Biome>(StringComparer.Ordinal);

        public bool Complete => Stale.Count == 0;

        /// <summary>Highest stage no stale location can lower (int.MaxValue = none stale); ocean-only counts as stage 0.</summary>
        public int StaleCap(out string why)
        {
            var cap = int.MaxValue;
            why = null;
            foreach (var kv in Stale)
            {
                var b = StageGraph.LowestBiome(kv.Value);
                var c = b == -1 ? int.MaxValue : b == -2 ? 0 : b * 2;
                if (c < cap || c == cap && string.CompareOrdinal(kv.Key, why) < 0) { cap = c; why = kv.Key; }
            }
            if (why != null) why = "stale:" + why;
            return cap;
        }

        public void Anchor(string prefab, Heightmap.Biome biomes, bool open, string key = null)
        {
            if (string.IsNullOrEmpty(prefab) || biomes == Heightmap.Biome.None) return;
            if (!Anchors.TryGetValue(prefab, out var list)) Anchors[prefab] = list = new List<StageAnchor>();
            var a = new StageAnchor(biomes, open, key);
            if (!list.Contains(a)) list.Add(a);
        }

        public static void Multi(Dictionary<string, List<string>> map, string key, string value)
        {
            if (!map.TryGetValue(key, out var list)) map[key] = list = new List<string>();
            if (!list.Contains(value)) list.Add(value);
        }
    }

    /// <summary>Turns index relations + world facts + authored facts into solver paths (DESIGN.md section 3).</summary>
    public static class StageGraph
    {
        public const string Karve = "Karve"; // ocean-only sources = when the first ship is available (user decision 3)

        /// <summary>Lowest land biome of a mask as stage biome 0..7; -1 = none, -2 = ocean only.</summary>
        public static int LowestBiome(Heightmap.Biome mask)
        {
            for (var i = 0; i < 8; i++)
                if ((mask & FoundIn.Order[i]) != Heightmap.Biome.None) return i;
            return (mask & Heightmap.Biome.Ocean) != Heightmap.Biome.None ? -2 : -1;
        }

        private static bool Place(StagePath p, Heightmap.Biome mask)
        {
            var b = LowestBiome(mask);
            if (b == -1) return false;
            if (b == -2)
            {
                p.Ocean = true;
                p.Need(Karve);
            }
            else p.Base = b * 2;
            return true;
        }

        // A key some creature's death sets is a requirement; any other (quest keys) makes the path incidental.
        private static void Key(StagePath p, string key, StageWorld w)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (w.Defeats.ContainsKey(key)) p.Need("@key:" + key);
            else p.Incidental = true;
        }

        private static string Tool(char kind, int tier) => "@tool:" + kind + ":" + tier;

        /// <summary>Adds everything to a new solver; yields so the caller can spread the work over frames.</summary>
        public static IEnumerable<object> Build(BrowserIndex ix, StageWorld w, IEnumerable<ItemFact> facts, StageSolver s, int chunk = 500)
        {
            foreach (var y in Derived(ix, w, s, chunk)) yield return y;
            s.Cap(w.StaleCap(out var why), why);
            if (facts != null)
                foreach (var f in facts)
                {
                    if (f.Magic) s.SetMagic(f.Prefab);
                    else if (f.Biome >= 0) s.SetAuthored(f.Prefab, f.Biome);
                }
        }

        private static IEnumerable<object> Derived(BrowserIndex ix, StageWorld w, StageSolver s, int chunk)
        {
            var tiers = new HashSet<string>(StringComparer.Ordinal);
            var n = 0;
            foreach (var r in ix.Relations)
            {
                if (++n % chunk == 0) yield return null;
                if (r.Kind == RelationKind.Upgrade || r.Quality > 1) continue;
                var p = Path(r, ix, w, tiers);
                foreach (var o in r.Outputs) s.Add(o.Prefab, p);
            }
            foreach (var kv in w.Anchors)
            {
                var open = kv.Value.Exists(a => a.Open);
                foreach (var a in kv.Value)
                {
                    if (a.Open != open) continue; // open-world placement beats a location (measured: false early biomes otherwise)
                    var p = new StagePath { Kind = a.Open ? "spawn" : "location" };
                    if (!Place(p, a.Biomes)) continue;
                    if (p.Ocean) p.Kind = "ocean";
                    Key(p, a.Key, w);
                    s.Add(kv.Key, p);
                }
            }
            yield return null;
            foreach (var (boss, item, altar) in w.Summons)
            {
                var p = new StagePath { Kind = "summon" };
                if (!Place(p, altar)) continue;
                if (!string.IsNullOrEmpty(item))
                    foreach (var g in item.Split('+')) // every stand group of the altar, any one alternative of it
                        if (g.IndexOf('|') < 0) p.Need(g);
                        else p.NeedAny(g.Split('|'));
                s.Add(boss, p);
            }
            foreach (var (item, creature) in w.ItemSpawns) s.Add(creature, new StagePath { Kind = "summon" }.Need(item));
            foreach (var (item, producer, root) in w.Tapped) s.Add(item, new StagePath { Kind = "produce" }.Need(producer).Need(root));
            foreach (var kv in w.Defeats)
                foreach (var who in kv.Value)
                    s.Add("@key:" + kv.Key, new StagePath { Kind = "key", AfterBoss = w.Bosses.Contains(who) }.Need(who));
            foreach (var id in tiers)
            {
                var kind = id[6];
                var tier = int.Parse(id.Substring(8), System.Globalization.CultureInfo.InvariantCulture);
                var tools = new List<string>();
                foreach (var t in w.Tools)
                    if (t.Tier >= tier && (kind == 'a' ? t.Pick || t.Chop : kind == 'p' ? t.Pick : t.Chop)) tools.Add(t.Prefab);
                if (tools.Count > 0) s.Add(id, new StagePath { Kind = "tool" }.NeedAny(tools.ToArray()));
            }
            yield return null;
        }

        private static StagePath Path(Relation r, BrowserIndex ix, StageWorld w, HashSet<string> tiers)
        {
            var p = new StagePath { Kind = r.Kind.ToString().ToLowerInvariant(), Incidental = r.Incidental };
            if (r.AnyInput && r.Inputs.Length > 0)
            {
                var any = new string[r.Inputs.Length];
                for (var i = 0; i < any.Length; i++) any[i] = r.Inputs[i].Prefab;
                p.NeedAny(any);
            }
            else foreach (var i in r.Inputs) p.Need(i.Prefab);
            if (r.Station != null)
            {
                p.Need(r.Station);
                // Level L = the L-1 cheapest distinct extensions; a level no visible extension explains adds nothing.
                if (r.StationLevel > 1 && w.Extensions.TryGetValue(r.Station, out var ext) && ext.Count > 0)
                    p.NeedAny(ext.ToArray(), Math.Min(r.StationLevel - 1, ext.Count));
            }
            if (r.Kind != RelationKind.Craft && r.Source != r.Station)
            {
                if (r.Biomes != 0) Place(p, (Heightmap.Biome)r.Biomes);
                else
                {
                    p.Need(r.Source);
                    p.Source = r.Source;
                }
            }
            Key(p, r.RequiredKey, w);
            var tool = r.Kind == RelationKind.Mine ? 'p' : r.Kind == RelationKind.Tree ? 'c' : r.MinToolTier > 0 ? 'a' : '\0';
            if (tool != '\0')
            {
                var id = Tool(tool, r.MinToolTier);
                tiers.Add(id);
                p.Need(id);
            }
            p.AfterBoss = (r.Kind == RelationKind.CreatureDrop || r.Kind == RelationKind.KillDrop) && w.Bosses.Contains(r.Source);
            return p;
        }
    }
}
