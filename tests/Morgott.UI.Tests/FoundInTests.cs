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
            d.Add("Hen", B.None, Origin.Egg);
            d.Add("Hen", B.None, Origin.Summon);
            d.Add("Ghost", B.None, Origin.World); // a spawn list without biome: no word to show
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

        // Altar offerings carry their count ("boss<item*n"); a line from an older dump (no "*n") reads count 0 = unknown.
        [Fact]
        public void Summon_counts_round_trip_and_old_lines_read_unknown()
        {
            var s = new Scanned { Objects = new System.Collections.Generic.List<string> { "Rock" } };
            s.Summons.Add(("Eikthyr", "TrophyDeer", 2));
            s.Summons.Add(("Dragon", "DragonEgg", 3));
            s.Summons.Add(("Fader", "Bell|BellFragment", 3)); // a stand group with alternatives
            var text = FoundInTable.Write("v", new[] { new System.Collections.Generic.KeyValuePair<string, Scanned>("L:Eikthyrnir", s) });
            Assert.Contains("Rock,Eikthyr<TrophyDeer*2,Dragon<DragonEgg*3,Fader<Bell|BellFragment*3", text);
            Assert.Equal(new[] { ("Eikthyr", "TrophyDeer", 2), ("Dragon", "DragonEgg", 3), ("Fader", "Bell|BellFragment", 3) }, FoundInTable.Parse(text, out _, out _)["L:Eikthyrnir"].Summons);
            // Several stand groups of one altar: one spec, parsed back whole, groups recovered.
            var spec = FoundInTable.Spec(new System.Collections.Generic.List<(string, int)> { ("A|B", 2), ("C", 1) });
            Assert.Equal(("A|B*2+C*1", 0), spec);
            var multi = new Scanned { Objects = new System.Collections.Generic.List<string>() };
            multi.Summons.Add(("Mixed", spec.Item, spec.Count));
            var back = FoundInTable.Parse(FoundInTable.Write("v", new[] { new System.Collections.Generic.KeyValuePair<string, Scanned>("L:M", multi) }), out _, out _)["L:M"];
            Assert.Equal(("Mixed", "A|B*2+C*1", 0), Assert.Single(back.Summons));
            Assert.Equal(new[] { ("A|B", 2), ("C", 1) }, FoundInTable.Groups("A|B*2+C*1", 0));
            Assert.Equal(new[] { ("TrophyDeer", 2) }, FoundInTable.Groups("TrophyDeer", 2));
            var d = new FoundIn();
            d.Place(back, Heightmap.Biome.Plains, "L:M");
            Assert.Equal(("Mixed", "A|B+C", Heightmap.Biome.Plains), Assert.Single(d.World.Summons)); // one path: every group, any alternative
            var old = FoundInTable.Parse("L:GDKing\t0\t0\tgd_king:b\tgd_king<AncientSeed\n", out _, out _)["L:GDKing"];
            Assert.Equal(("gd_king", "AncientSeed", 0), Assert.Single(old.Summons));
        }

        // The embedded vanilla table (dumped by autotest item-browser-found-in): parses whole, every dumped prefab kept.
        [Fact]
        public void Embedded_table_covers_every_dumped_location()
        {
            var path = System.IO.Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Morgott.UI", "ItemBrowser", "FoundInTable.txt");
            var map = FoundInTable.Parse(System.IO.File.ReadAllText(path), out var version, out var count);
            Assert.False(string.IsNullOrEmpty(version));
            Assert.True(count > 300, $"count {count}");
            Assert.Equal(count, map.Count);
            Assert.Contains(("Eikthyr", Origin.Boss), map["L:Eikthyrnir"].Creatures);
            Assert.True(map["L:Crypt2"].Interior);
            Assert.Contains(("Skeleton", Origin.World), map["L:Crypt2"].Creatures);
        }

        [Theory]
        [InlineData("1.0.30\nd7f768daaa38+", "1.0.30 d7f768daaa38+")]
        [InlineData("  1.0.30\r\n\t d7f768daaa38+  b12345 \n", "1.0.30 d7f768daaa38+ b12345")]
        [InlineData(null, "")]
        [InlineData(" \r\n\t ", "")]
        public void Sanitize_game_version_collapses_control_whitespace(string raw, string want) =>
            Assert.Equal(want, FoundInTable.SanitizeGameVersion(raw));

        [Fact]
        public void Table_write_sanitizes_multiline_version_and_keys()
        {
            var s = new Scanned();
            s.Add("Boar", Origin.World);
            var text = FoundInTable.Write("1.0.30\nd7f768daaa38+", new[]
            {
                new System.Collections.Generic.KeyValuePair<string, Scanned>("L:Loc@1.0.30\nd7f768daaa38+#a2", s),
                new System.Collections.Generic.KeyValuePair<string, Scanned>("L:Tab\tKey", s),
            });
            Assert.Equal(3, text.TrimEnd('\n').Split('\n').Length);
            var map = FoundInTable.Parse(text, out var version, out var count);
            Assert.Equal("1.0.30 d7f768daaa38+", version);
            Assert.Equal(2, count);
            Assert.Equal(2, map.Count);
            Assert.True(map.ContainsKey("L:Loc@1.0.30 d7f768daaa38+#a2"));
            Assert.True(map.ContainsKey("L:Tab Key"));
        }
    }
}
