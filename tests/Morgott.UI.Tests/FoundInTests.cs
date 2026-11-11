using Morgott.UI.ItemBrowser;
using Xunit;
using B = Heightmap.Biome;

namespace Morgott.UI.Tests
{
    // FoundIn: OR-merge of sources per creature, biome order, tags, the rendered line.
    public class FoundInTests
    {
        private static string Line(FoundIn d, string id) => FoundIn.Text(d.ByPrefab[id], t => t.TrimStart('$'), b => b.ToString());

        [Fact]
        public void Merges_sources_and_dedupes_biomes()
        {
            var d = new FoundIn();
            d.Add("Skeleton", B.BlackForest, Origin.Dungeon);
            d.Add("Skeleton", B.BlackForest | B.Meadows, Origin.World);
            d.Add("Skeleton", B.BlackForest, Origin.Dungeon);
            Assert.Equal((B.Meadows | B.BlackForest, Origin.World | Origin.Dungeon), d.ByPrefab["Skeleton"]);
            Assert.Equal("Meadows, BlackForest · ib_from_dungeon", Line(d, "Skeleton"));
        }

        [Fact]
        public void Legend_order_ocean_last_and_world_untagged()
        {
            var d = new FoundIn();
            d.Add("Serpent", B.Ocean | B.Plains | B.Swamp, Origin.World);
            Assert.Equal("Swamp, Plains, Ocean", Line(d, "Serpent"));
        }

        [Fact]
        public void Every_land_biome_is_one_word()
        {
            var d = new FoundIn();
            d.Add("Wolf", B.All, Origin.Event);
            Assert.Equal("ib_anywhere, Ocean · ib_from_event", Line(d, "Wolf"));
        }

        [Fact]
        public void Tags_only_and_unknown()
        {
            var d = new FoundIn();
            d.Add("Hen", 0, Origin.Egg);
            d.Add("Hen", 0, Origin.Summon);
            d.Add("Ghost", 0, Origin.World); // a spawn list without biome: no word to show
            d.Add("", B.Meadows, Origin.World);
            d.Add("Nothing", B.Meadows, Origin.None);
            Assert.Equal("ib_from_egg, ib_from_summon", Line(d, "Hen"));
            Assert.Null(Line(d, "Ghost"));
            Assert.Equal(2, d.ByPrefab.Count);
        }

        [Fact]
        public void Table_round_trips_and_skips_bad_lines()
        {
            var s = new Scanned { Themes = 9, Interior = true };
            s.Add("Draugr", Origin.World);
            s.Add("Draugr", Origin.World);
            s.Add("Skeleton", Origin.Nest);
            s.Add("Eikthyr", Origin.Boss);
            var text = FoundInTable.Write("1.0.21@abc", new[] { new System.Collections.Generic.KeyValuePair<string, Scanned>("L:Crypt2", s), new System.Collections.Generic.KeyValuePair<string, Scanned>("R:empty", new Scanned()) });
            var map = FoundInTable.Parse(text + "garbage line\nL:x\tnotanumber\t0\t\n", out var version, out var count);
            Assert.Equal("1.0.21@abc", version);
            Assert.Equal(2, count);
            Assert.Equal(2, map.Count);
            Assert.True(map["L:Crypt2"].Interior);
            Assert.Equal(9, map["L:Crypt2"].Themes);
            Assert.Equal(new[] { ("Draugr", Origin.World), ("Skeleton", Origin.Nest), ("Eikthyr", Origin.Boss) }, map["L:Crypt2"].Creatures);
            Assert.Empty(map["R:empty"].Creatures);
        }
    }
}