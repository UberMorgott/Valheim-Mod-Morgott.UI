using System;
using System.Collections.Generic;

namespace Morgott.UI.ItemBrowser
{
    /// <summary>How a creature appears besides a plain world spawn (World has no tag on the page).</summary>
    [Flags]
    public enum Origin { None = 0, World = 1, Location = 2, Dungeon = 4, Event = 8, Boss = 16, Nest = 32, Tree = 64, Egg = 128, Summon = 256 }

    /// <summary>"Found in" line of a creature page: biome flags + origin flags per creature prefab, OR-merged over every
    /// source (FoundInScan fills it). No Unity calls: unit-tested.</summary>
    public sealed class FoundIn
    {
        public readonly Dictionary<string, (Heightmap.Biome Biomes, Origin Kinds)> ByPrefab =
            new Dictionary<string, (Heightmap.Biome, Origin)>(StringComparer.Ordinal);

        public void Add(string prefab, Heightmap.Biome biomes, Origin kind)
        {
            if (string.IsNullOrEmpty(prefab) || kind == Origin.None) return;
            ByPrefab.TryGetValue(prefab, out var o);
            ByPrefab[prefab] = (o.Biomes | biomes, o.Kinds | kind);
        }

        /// <summary>Map legend order (MapFilterMath.Biomes), Ocean last.</summary>
        public static readonly Heightmap.Biome[] Order =
        {
            Heightmap.Biome.Meadows, Heightmap.Biome.BlackForest, Heightmap.Biome.Swamp, Heightmap.Biome.Mountain,
            Heightmap.Biome.Plains, Heightmap.Biome.Mistlands, Heightmap.Biome.AshLands, Heightmap.Biome.DeepNorth, Heightmap.Biome.Ocean,
        };

        private static readonly (Origin Kind, string Token)[] TagTokens =
        {
            (Origin.Location, "$ib_from_location"), (Origin.Dungeon, "$ib_from_dungeon"), (Origin.Event, "$ib_from_event"),
            (Origin.Boss, "$ib_from_boss"), (Origin.Nest, "$ib_from_nest"), (Origin.Tree, "$ib_from_tree"),
            (Origin.Egg, "$ib_from_egg"), (Origin.Summon, "$ib_from_summon"),
        };

        public static List<Heightmap.Biome> Biomes(Heightmap.Biome mask)
        {
            var list = new List<Heightmap.Biome>();
            foreach (var b in Order) if ((mask & b) != 0) list.Add(b);
            return list;
        }

        public static List<string> Tags(Origin kinds)
        {
            var list = new List<string>();
            foreach (var (k, t) in TagTokens) if ((kinds & k) != 0) list.Add(t);
            return list;
        }

        /// <summary>"Meadows, Black Forest · dungeon, raid": biomes in legend order (every land biome = one "anywhere"
        /// word), then the origin tags. <paramref name="biome"/> renders one biome (localized, coloured); null = nothing known.</summary>
        public static string Text((Heightmap.Biome Biomes, Origin Kinds) o, Func<string, string> localize, Func<Heightmap.Biome, string> biome)
        {
            var parts = new List<string>();
            if ((o.Biomes & Heightmap.Biome.Land) == Heightmap.Biome.Land)
            {
                parts.Add(localize("$ib_anywhere"));
                if ((o.Biomes & Heightmap.Biome.Ocean) != 0) parts.Add(biome(Heightmap.Biome.Ocean));
            }
            else foreach (var b in Biomes(o.Biomes)) parts.Add(biome(b));
            var tags = new List<string>();
            foreach (var t in Tags(o.Kinds)) tags.Add(localize(t));
            if (parts.Count == 0 && tags.Count == 0) return null;
            var head = string.Join(", ", parts);
            var tail = string.Join(", ", tags);
            return head.Length == 0 ? tail : tail.Length == 0 ? head : head + " · " + tail;
        }
    }
}
