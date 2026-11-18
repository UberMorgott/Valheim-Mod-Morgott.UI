using System.Collections.Generic;
using System.Linq;
using Morgott.Core;
using Morgott.UI.ItemBrowser;
using Xunit;

namespace Morgott.UI.Tests
{
    // Stage rule (DESIGN.md section 3), facets and list grouping of the item browser's progression filters.
    public class StageSolverTests
    {
        private static StagePath At(int biome, string kind = "spawn") => new StagePath { Kind = kind, Base = biome * 2 };

        private static StageSolver Solved(StageSolver s)
        {
            s.Solve();
            return s;
        }

        [Fact]
        public void Entry_is_min_over_paths_and_path_is_max_over_requirements()
        {
            var s = new StageSolver();
            s.Add("Copper", At(1));
            s.Add("Tin", At(2));
            s.Add("Bronze", new StagePath { Kind = "craft" }.Need("Copper").Need("Tin"));
            s.Add("Bronze", new StagePath { Kind = "trader", Base = 8 });
            Solved(s);
            Assert.Equal(4, s.Result("Bronze").Stage);
            Assert.Equal("craft", s.Result("Bronze").Kind);
            Assert.Equal(StageVia.Derived, s.Result("Bronze").Via);
        }

        [Fact]
        public void Any_one_ingredient_takes_the_cheapest()
        {
            var s = new StageSolver();
            s.Add("A", At(5));
            s.Add("B", At(1));
            s.Add("X", new StagePath().NeedAny(new[] { "A", "B", "Nothing" }));
            Assert.Equal(2, Solved(s).Result("X").Stage);
        }

        [Fact]
        public void Station_level_needs_the_kth_cheapest_extension()
        {
            var s = new StageSolver();
            s.Add("E1", At(0));
            s.Add("E2", At(3));
            s.Add("E3", At(1));
            s.Add("L3", new StagePath().NeedAny(new[] { "E1", "E2", "E3" }, 2));
            s.Add("L4", new StagePath().NeedAny(new[] { "E1", "E2", "E3" }, 3));
            s.Add("L5", new StagePath().NeedAny(new[] { "E1", "E2", "E3" }, 4));
            Solved(s);
            Assert.Equal(2, s.Result("L3").Stage);
            Assert.Equal(6, s.Result("L4").Stage);
            Assert.Equal(-1, s.Result("L5").Stage);
        }

        [Fact]
        public void A_cycle_never_starts_itself_but_resolves_from_outside()
        {
            var s = new StageSolver();
            s.Add("A", new StagePath().Need("B"));
            s.Add("B", new StagePath().Need("A"));
            s.Add("C", new StagePath().Need("D"));
            s.Add("D", new StagePath().Need("C"));
            s.Add("D", At(2));
            Solved(s);
            Assert.Equal(-1, s.Result("A").Stage);
            Assert.Equal(-1, s.Result("B").Stage);
            Assert.Equal(4, s.Result("C").Stage);
        }

        [Fact]
        public void Incidental_counts_only_without_any_other_path()
        {
            var s = new StageSolver();
            s.Add("Chest", At(1));
            s.Add("OnlyChest", new StagePath { Kind = "container", Incidental = true }.Need("Chest"));
            s.Add("Made", new StagePath { Kind = "container", Incidental = true }.Need("Chest"));
            s.Add("Made", At(4));
            s.Add("Blocked", new StagePath { Kind = "container", Incidental = true }.Need("Chest"));
            s.Add("Blocked", new StagePath { Kind = "craft" }.Need("Nothing"));
            s.Add("FromChestItem", new StagePath { Kind = "craft" }.Need("OnlyChest"));
            Solved(s);
            Assert.Equal(StageVia.Incidental, s.Result("OnlyChest").Via);
            Assert.Equal(2, s.Result("OnlyChest").Stage);
            Assert.Equal(8, s.Result("Made").Stage); // a chest never lowers an entry with a real path
            Assert.Equal(-1, s.Result("Blocked").Stage);
            Assert.Equal(StageVia.Incidental, s.Result("FromChestItem").Via);
        }

        // DESIGN.md "Conflicts to review": authored biome vs derived biome; derived always wins.
        [Theory]
        [InlineData("AxeBronze", 2, 1)]
        [InlineData("AxeEarly", 2, 0)]
        [InlineData("AxeFlint", 1, 0)]
        [InlineData("AxeIron", 3, 2)]
        [InlineData("KnifeChitin", 3, 1)]
        [InlineData("ShieldSerpentscale", 3, 2)]
        [InlineData("HelmetDverger", 5, 1)]
        [InlineData("HelmetFishingHat", 0, 7)]
        public void Derived_beats_authored(string id, int authored, int derived)
        {
            var s = new StageSolver();
            s.Add(id, At(derived, "craft"));
            s.SetAuthored(id, authored);
            var r = Solved(s).Result(id);
            Assert.Equal(derived * 2, r.Stage);
            Assert.Equal(StageVia.Derived, r.Via);
        }

        [Fact]
        public void Authored_fills_gaps_visibly_and_reaches_dependents()
        {
            var s = new StageSolver();
            s.SetAuthored("Set", 4);
            s.Add("Set", new StagePath { Kind = "craft" }.Need("Nothing"));
            s.Add("Upgrade", new StagePath { Kind = "craft" }.Need("Set"));
            Solved(s);
            Assert.Equal(StageVia.Authored, s.Result("Set").Via);
            Assert.Equal(8, s.Result("Set").Stage);
            Assert.Equal(StageVia.Authored, s.Result("Upgrade").Via);
        }

        [Fact]
        public void Magic_applies_only_without_a_derivable_stage()
        {
            var s = new StageSolver();
            s.Add("Dust", new StagePath().Need("Nothing"));
            s.SetMagic("Dust");
            s.Add("Shard", At(1));
            s.SetMagic("Shard");
            s.Add("Needs", new StagePath().Need("Dust"));
            Solved(s);
            Assert.Equal(StageVia.Magic, s.Result("Dust").Via);
            Assert.Equal(-1, s.Result("Dust").Stage);
            Assert.Null(s.Missing("Dust"));
            Assert.Equal(StageVia.Unknown, s.Result("Needs").Via);
            Assert.Equal(StageVia.Derived, s.Result("Shard").Via); // keeps its biome
            Assert.Equal(2, s.Result("Shard").Stage);
        }

        [Fact]
        public void Missing_names_the_first_real_prefab_without_a_source()
        {
            var s = new StageSolver();
            s.Add("Sword", new StagePath().Need("Ok").Need("Bar"));
            s.Add("Ok", At(0));
            s.Add("Bar", new StagePath().Need("@tool:p:2").Need("Ore"));
            Solved(s);
            Assert.Equal("Ore", s.Missing("Sword"));
            Assert.Null(s.Missing("Ok"));
        }

        [Fact]
        public void Stepping_gives_the_same_result_as_solving()
        {
            var s = new StageSolver();
            s.Add("A", At(3));
            s.Add("B", new StagePath().Need("A"));
            var steps = 0;
            while (!s.Step(1)) Assert.True(++steps < 100);
            Assert.Equal(6, s.Result("B").Stage);
        }
    }

    public class StageGraphTests
    {
        private static Relation R(RelationKind kind, string source, string output, params string[] inputs) => new Relation
        {
            Kind = kind,
            Source = source,
            Inputs = inputs.Select(i => new Stack(i)).ToArray(),
            Outputs = new[] { new Stack(output) }
        };

        private static StageSolver Solve(BrowserIndex ix, StageWorld w, IEnumerable<ItemFact> facts = null)
        {
            var s = new StageSolver();
            foreach (var _ in StageGraph.Build(ix, w, facts, s)) { }
            s.Solve();
            return s;
        }

        private static (BrowserIndex, StageWorld) Base()
        {
            var ix = new BrowserIndex();
            var w = new StageWorld();
            w.Anchor("Pickable_Wood", Heightmap.Biome.Meadows, true);
            ix.Add(R(RelationKind.Pickable, "Pickable_Wood", "Wood"));
            ix.Add(R(RelationKind.Build, "Hammer", "Karve", "Wood"));
            ix.Add(new Relation { Kind = RelationKind.Craft, Source = "Recipe_Hammer", Outputs = new[] { new Stack("Hammer") }, Inputs = new[] { new Stack("Wood") } });
            w.Anchor("Greydwarf", Heightmap.Biome.BlackForest, true);
            ix.Add(R(RelationKind.CreatureDrop, "Greydwarf", "Resin"));
            return (ix, w);
        }

        [Fact]
        public void Lowest_land_biome_wins_and_open_world_beats_locations()
        {
            var (ix, w) = Base();
            w.Anchor("Draugr", Heightmap.Biome.Swamp, true);
            w.Anchor("Draugr", Heightmap.Biome.Meadows, false);
            w.Anchor("Crypt", Heightmap.Biome.Swamp | Heightmap.Biome.BlackForest, false);
            var s = Solve(ix, w);
            Assert.Equal(4, s.Result("Draugr").Stage);
            Assert.Equal(2, s.Result("Crypt").Stage);
            Assert.Equal("location", s.Result("Crypt").Kind);
        }

        [Fact]
        public void Ocean_only_sources_take_the_karve_stage_with_a_mark_that_reaches_drops()
        {
            var (ix, w) = Base();
            ix.Add(R(RelationKind.Build, "Hammer", "KarveReal", "Resin"));
            w.Anchor("Serpent", Heightmap.Biome.Ocean, true);
            ix.Add(R(RelationKind.CreatureDrop, "Serpent", "SerpentScale"));
            ix.Add(new Relation { Kind = RelationKind.Craft, Source = "Recipe_Shield", Outputs = new[] { new Stack("ShieldSerpent") }, Inputs = new[] { new Stack("SerpentScale") } });
            var s = Solve(ix, w);
            Assert.Equal(0, s.Result("Serpent").Stage); // Karve here costs only meadows wood
            Assert.True(s.Result("Serpent").Ocean);
            Assert.True(s.Result("SerpentScale").Ocean);
            Assert.False(s.Result("ShieldSerpent").Ocean); // crafted: the mark stays on the sea sources
        }

        [Fact]
        public void Boss_is_altar_plus_summon_item_and_its_drops_and_key_come_after_it()
        {
            var (ix, w) = Base();
            w.Bosses.Add("Bonemass");
            w.Anchor("Pickable_Ooze", Heightmap.Biome.Swamp, true);
            ix.Add(R(RelationKind.Pickable, "Pickable_Ooze", "WitheredBone"));
            w.Summons.Add(("Bonemass", "WitheredBone", Heightmap.Biome.BlackForest));
            ix.Add(R(RelationKind.CreatureDrop, "Bonemass", "Wishbone"));
            StageWorld.Multi(w.Defeats, "defeated_bonemass", "Bonemass");
            ix.Add(new Relation { Kind = RelationKind.Trader, Source = "Haldor", Outputs = new[] { new Stack("Fancy") }, RequiredKey = "defeated_bonemass" });
            w.Anchor("Haldor", Heightmap.Biome.Meadows, false);
            var s = Solve(ix, w);
            Assert.Equal(4, s.Result("Bonemass").Stage);
            Assert.Equal(5, s.Result("Wishbone").Stage);
            Assert.True(s.Result("Wishbone").AfterBoss);
            Assert.Equal(5, s.Result("Fancy").Stage);
        }

        [Fact]
        public void Quest_key_stock_is_incidental()
        {
            var (ix, w) = Base();
            w.Anchor("Hildir", Heightmap.Biome.Meadows, false);
            ix.Add(new Relation { Kind = RelationKind.Trader, Source = "Hildir", Outputs = new[] { new Stack("Dress") }, RequiredKey = "hildir1" });
            Assert.Equal(StageVia.Incidental, Solve(ix, w).Result("Dress").Via);
        }

        [Fact]
        public void Mining_needs_the_cheapest_tool_of_that_tier()
        {
            var (ix, w) = Base();
            w.Anchor("Rock", Heightmap.Biome.Meadows, true);
            ix.Add(new Relation { Kind = RelationKind.Mine, Source = "Rock", Outputs = new[] { new Stack("Ore") }, MinToolTier = 2 });
            w.Anchor("Low", Heightmap.Biome.Meadows, true);
            w.Anchor("Mid", Heightmap.Biome.Swamp, true);
            w.Anchor("High", Heightmap.Biome.Plains, true);
            w.Tools.Add(new StageTool("Low", true, false, 1));
            w.Tools.Add(new StageTool("Axe", false, true, 9));
            w.Tools.Add(new StageTool("Mid", true, false, 2));
            w.Tools.Add(new StageTool("High", true, false, 3));
            Assert.Equal(4, Solve(ix, w).Result("Ore").Stage);
        }

        [Fact]
        public void Station_level_uses_extensions_and_upgrades_never_count()
        {
            var (ix, w) = Base();
            w.Anchor("Bench", Heightmap.Biome.Meadows, true);
            w.Anchor("Ext1", Heightmap.Biome.Meadows, true);
            w.Anchor("Ext2", Heightmap.Biome.Mountain, true);
            StageWorld.Multi(w.Extensions, "Bench", "Ext1");
            StageWorld.Multi(w.Extensions, "Bench", "Ext2");
            ix.Add(new Relation { Kind = RelationKind.Craft, Source = "R1", Station = "Bench", StationLevel = 3, Quality = 1, Outputs = new[] { new Stack("Item") } });
            ix.Add(new Relation { Kind = RelationKind.Upgrade, Source = "R2", Quality = 2, Outputs = new[] { new Stack("Cheap") } });
            var s = Solve(ix, w);
            Assert.Equal(6, s.Result("Item").Stage);
            Assert.Equal(-1, s.Result("Cheap").Stage);
        }

        [Fact]
        public void Facts_give_fallback_and_magic()
        {
            var (ix, w) = Base();
            var s = Solve(ix, w, new[] { new ItemFact { Prefab = "Set", Biome = 3 }, new ItemFact { Prefab = "Rune", Magic = true }, new ItemFact { Prefab = "Wood", Biome = 7 } });
            Assert.Equal(StageVia.Authored, s.Result("Set").Via);
            Assert.Equal(StageVia.Magic, s.Result("Rune").Via);
            Assert.Equal(0, s.Result("Wood").Stage);
        }
    }

    public class FacetTests
    {
        private static FacetTable Table()
        {
            var t = new FacetTable();
            t.Add("class", "Sword", "swords", "$skill_swords");
            t.Add("damage", "Sword", "slash", "$inventory_slash");
            t.Add("damage", "Sword", "spirit", "$inventory_spirit");
            t.Add("class", "Club", "clubs", "$skill_clubs");
            t.Add("damage", "Club", "blunt", "$inventory_blunt");
            return t;
        }

        [Fact]
        public void Or_inside_a_facet_and_between_facets_and_explicit_none()
        {
            var t = Table();
            var f = new FilterState();
            f.Toggle("damage", "slash", true);
            f.Toggle("damage", "blunt", true);
            Assert.True(t.Matches("Sword", f));
            Assert.True(t.Matches("Club", f));
            f.Toggle("class", "clubs", false);
            Assert.False(t.Matches("Sword", f));
            Assert.True(t.Matches("Club", f));
            f.Toggle("class", "swords", false); // single choice replaces
            Assert.True(t.Matches("Sword", f));
            Assert.False(t.Matches("Club", f));
            var none = new FilterState();
            none.Toggle("class", Facet.NoneKey, false);
            Assert.True(t.Matches("Shieldless", none));
            Assert.False(t.Matches("Sword", none));
        }

        [Theory]
        [InlineData(30f, 15f, 90f, "eitr")]
        [InlineData(50f, 45f, 0f, "balanced")]
        [InlineData(50f, 20f, 0f, "health")]
        [InlineData(10f, 40f, 5f, "stamina")]
        [InlineData(0f, 0f, 0f, null)]
        public void Food_focus(float hp, float st, float ei, string want) => Assert.Equal(want, StageRules.FoodFocus(hp, st, ei));

        [Fact]
        public void Every_section_4_scope_has_chips_of_at_most_four()
        {
            foreach (var scope in new[] { "weapons", "armor", "utility", "ammo", "tools", "food", "meads", "materials", "trophies", "fish", "piece", "station", "creature" })
            {
                var chips = FacetTable.ChipsOf(scope);
                Assert.InRange(chips.Length, 1, 4);
            }
            Assert.Contains(FacetTable.ChipsOf("weapons"), c => c.Facets.Contains("grip"));
            Assert.Contains(FacetTable.ChipsOf("armor"), c => c.Facets.Contains("set") && c.Facets.Contains("mod"));
        }

        [Fact]
        public void Index_facets_read_sources_uses_and_creature_loot()
        {
            var ix = new BrowserIndex();
            ix.Add(new Entry { Id = "Boar", Name = "$boar", Type = EntryType.Creature });
            ix.Add(new Entry { Id = "Hide", Name = "$hide", Type = EntryType.Item, Category = ItemCategory.Materials });
            ix.Add(new Entry { Id = "Bench", Name = "$bench", Type = EntryType.Station });
            ix.Add(new Relation { Kind = RelationKind.CreatureDrop, Source = "Boar", Outputs = new[] { new Stack("Hide") } });
            ix.Add(new Relation { Kind = RelationKind.Craft, Source = "R", Station = "Bench", Inputs = new[] { new Stack("Hide") }, Outputs = new[] { new Stack("Cape") } });
            var t = new FacetTable();
            foreach (var e in ix.Entries.Values.ToList()) t.AddIndexFacets(ix, e, m => m ?? "Valheim");
            Assert.Equal(new[] { "creaturedrop" }, t.Facets["source"].ByEntry["Hide"]);
            Assert.Equal(new[] { "Bench" }, t.Facets["usedin"].ByEntry["Hide"]);
            Assert.Equal(new[] { "Boar" }, t.Facets["creature"].ByEntry["Hide"]);
            Assert.Equal(new[] { "materials" }, t.Facets["loot"].ByEntry["Boar"]);
            Assert.Equal(new[] { "valheim" }, t.Facets["mod"].ByEntry["Boar"]);
        }

        [Fact]
        public void Strip_mask_and_groups_put_unknown_last()
        {
            var plain = new StageInfo { Stage = 7, Via = StageVia.Derived };
            Assert.Equal(3, StageRules.Plaque(plain));
            Assert.True(plain.AfterBoss);
            Assert.Equal(StageRules.PlaqueUnknown, StageRules.Plaque(StageInfo.None));
            Assert.Equal(StageRules.PlaqueMagic, StageRules.Plaque(new StageInfo { Stage = -1, Via = StageVia.Magic }));
            Assert.True(StageRules.PassesStrip(0, plain));
            Assert.True(StageRules.PassesStrip(1 << 3, plain));
            Assert.False(StageRules.PassesStrip(1 << 2, plain));
        }

        [Fact]
        public void Blocks_group_by_stage_keep_search_order_and_wrap_grid_rows()
        {
            var info = new Dictionary<string, StageInfo>
            {
                ["a"] = StageInfo.None,
                ["b"] = new StageInfo { Stage = 2, Via = StageVia.Derived },
                ["c"] = new StageInfo { Stage = 3, Via = StageVia.Derived },
                ["d"] = new StageInfo { Stage = 2, Via = StageVia.Derived },
                ["e"] = new StageInfo { Stage = -1, Via = StageVia.Magic },
            };
            var list = info.Keys.Select(k => new Entry { Id = k }).ToList();
            var ordered = ListBlocks.Order(list, k => info[k]);
            Assert.Equal("bdcea", string.Concat(ordered.Select(e => e.Id)));
            var rows = ListBlocks.Build(ordered, k => info[k], 1);
            Assert.Equal(9, rows.Count); // 4 headers + 5 rows
            Assert.True(rows[0].Header);
            Assert.Equal(-2, rows[0].Count);
            var grid = ListBlocks.Build(ordered, k => info[k], 6);
            Assert.Equal(2, grid[1].Count);
            Assert.Equal(8, grid.Count);
        }

        [Fact]
        public void Table_round_trips_objects_and_summons_and_old_lines_stay_unscanned()
        {
            var s = new Scanned { Themes = 2, Interior = true };
            s.Add("Draugr", Origin.World);
            s.AddObject("Pickable_X");
            s.AddObject("TreasureChest");
            s.Summons.Add(("Bonemass", "WitheredBone"));
            var text = FoundInTable.Write("1.0", new Dictionary<string, Scanned> { ["L:Loc"] = s });
            var back = FoundInTable.Parse(text + "L:Old\t0\t0\tBoar:w\n", out _, out _);
            Assert.Equal(new[] { "Pickable_X", "TreasureChest" }, back["L:Loc"].Objects);
            Assert.Equal(("Bonemass", "WitheredBone"), back["L:Loc"].Summons[0]);
            Assert.Null(back["L:Old"].Objects);
        }
    }
}
