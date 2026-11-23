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
        public void Authored_fills_its_own_gap_visibly_and_stops_there()
        {
            var s = new StageSolver();
            s.SetAuthored("Set", 4);
            s.Add("Set", new StagePath { Kind = "craft" }.Need("Nothing"));
            s.Add("Upgrade", new StagePath { Kind = "craft" }.Need("Set"));
            Solved(s);
            Assert.Equal(StageVia.Authored, s.Result("Set").Via);
            Assert.Equal(8, s.Result("Set").Stage);
            Assert.Equal(StageVia.Unknown, s.Result("Upgrade").Via);
            Assert.Equal(-1, s.Result("Upgrade").Stage);
        }

        [Fact]
        public void Magic_never_has_a_biome()
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
            Assert.Equal(StageVia.Magic, s.Result("Shard").Via); // even with a derivable path (user decision 4)
            Assert.Equal(-1, s.Result("Shard").Stage);
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

    // v0.40.1 showed Copper / forge in Mistlands and Bronze in Ashlands (run 20261118-225750).
    public class StageCorrectnessTests
    {
        private static Relation R(RelationKind kind, string source, string output, string station, params string[] inputs) => new Relation
        {
            Kind = kind,
            Source = source,
            Station = station,
            Quality = kind == RelationKind.Craft ? 1 : 0,
            Inputs = inputs.Select(i => new Stack(i)).ToArray(),
            Outputs = new[] { new Stack(output) }
        };

        // The vanilla bronze chain, plus the late side paths that produced the wrong biomes.
        private static (BrowserIndex, FoundIn) Chain()
        {
            var ix = new BrowserIndex();
            var d = new FoundIn();
            var w = d.World;
            w.Anchor("Pickable_Stone", Heightmap.Biome.Meadows, true);
            ix.Add(R(RelationKind.Pickable, "Pickable_Stone", "Stone", null));
            ix.Add(R(RelationKind.Craft, "Recipe_Hammer", "Hammer", null, "Stone"));
            ix.Add(R(RelationKind.Craft, "Recipe_PickaxeAntler", "PickaxeAntler", null, "Stone"));
            w.Tools.Add(new StageTool("PickaxeAntler", true, false, 2));
            w.Anchor("Greydwarf", Heightmap.Biome.BlackForest, true);
            ix.Add(R(RelationKind.CreatureDrop, "Greydwarf", "SurtlingCore", null));
            ix.Add(R(RelationKind.Build, "Hammer", "smelter", null, "SurtlingCore"));
            ix.Add(new Relation { Kind = RelationKind.Mine, Source = "MineRock_Copper", MinToolTier = 2, Outputs = new[] { new Stack("CopperOre") } });
            ix.Add(new Relation { Kind = RelationKind.Mine, Source = "MineRock_Tin", MinToolTier = 2, Outputs = new[] { new Stack("TinOre") } });
            ix.Add(R(RelationKind.Conversion, "smelter", "Copper", "smelter", "CopperOre"));
            ix.Add(R(RelationKind.Conversion, "smelter", "Tin", "smelter", "TinOre"));
            ix.Add(R(RelationKind.Build, "Hammer", "forge", null, "Stone", "Copper"));
            ix.Add(R(RelationKind.Craft, "Recipe_Bronze", "Bronze", "forge", "Copper", "Tin"));
            ix.Add(R(RelationKind.Craft, "Recipe_SwordBronze", "SwordBronze", "forge", "Bronze"));
            // Late side paths: rubble in the Ashlands drops Bronze, a Mistlands-tier item recycles into Copper.
            w.Anchor("Ashlands_Rubble", Heightmap.Biome.AshLands, true);
            ix.Add(R(RelationKind.Destroy, "Ashlands_Rubble", "Bronze", null));
            ix.Add(R(RelationKind.Conversion, "smelter", "Copper", "smelter", "MistRelic"));
            return (ix, d);
        }

        private static readonly ItemFact[] Facts = { new ItemFact { Prefab = "MistRelic", Biome = 5 } };

        private static StageSolver Solve(BrowserIndex ix, FoundIn d)
        {
            var s = new StageSolver();
            foreach (var _ in StageGraph.Build(ix, d.World, Facts, s)) { }
            s.Solve();
            return s;
        }

        [Fact]
        public void An_authored_stage_describes_only_its_own_item()
        {
            var (ix, d) = Chain();
            var s = Solve(ix, d);
            Assert.Equal(10, s.Result("MistRelic").Stage);
            Assert.Equal(StageVia.Authored, s.Result("MistRelic").Via);
            Assert.Equal(-1, s.Result("Copper").Stage);
            Assert.Equal(-1, s.Result("forge").Stage);
        }

        [Fact]
        public void An_unread_location_leaves_only_stages_above_its_biome_unknown()
        {
            var (ix, d) = Chain();
            d.Place(new Scanned(), Heightmap.Biome.BlackForest, "L:Mine"); // failed load / line from before the objects column
            var s = Solve(ix, d);
            Assert.Equal(-1, s.Result("Bronze").Stage); // Ashlands rubble would be a wrong, too late answer: the mine may hold its ore
            Assert.Equal("stale:L:Mine", s.Missing("Bronze"));
            Assert.Equal(0, s.Result("Stone").Stage); // at or below the location's biome nothing it holds can lower it
            Assert.Equal(StageVia.Derived, s.Result("Stone").Via);
            Assert.Equal(2, s.Result("SurtlingCore").Stage);
            Assert.Equal(10, s.Result("MistRelic").Stage); // authored data describes its item either way
        }

        [Fact]
        public void With_the_ore_deposits_placed_the_bronze_chain_lands_in_the_black_forest()
        {
            var (ix, d) = Chain();
            d.Place(new Scanned { Objects = new List<string> { "MineRock_Copper", "MineRock_Tin" } }, Heightmap.Biome.BlackForest, "L:Mine");
            var s = Solve(ix, d);
            foreach (var id in new[] { "Copper", "Tin", "forge", "Bronze", "SwordBronze" })
            {
                Assert.Equal(2, s.Result(id).Stage);
                Assert.Equal(StageVia.Derived, s.Result(id).Via);
            }
        }

        [Fact]
        public void Magic_beats_a_derived_biome_and_gives_none_to_what_needs_it()
        {
            var s = new StageSolver();
            s.Add("Rune", new StagePath { Kind = "creaturedrop" });
            s.SetMagic("Rune");
            s.Add("Enchanted", new StagePath { Kind = "craft" }.Need("Rune"));
            s.Solve();
            Assert.Equal(StageVia.Magic, s.Result("Rune").Via);
            Assert.Equal(-1, s.Result("Rune").Stage);
            Assert.Equal(-1, s.Result("Enchanted").Stage);
        }

        [Fact]
        public void Missing_prefers_a_tool_that_has_a_source_over_a_dev_item_without_one()
        {
            var s = new StageSolver();
            s.Add("PickaxeIron", new StagePath { Kind = "craft" }.Need("Iron"));
            s.Add("Ore", new StagePath { Kind = "mine" }.NeedAny(new[] { "SwordCheat", "PickaxeIron" }));
            s.Solve();
            Assert.Equal("Iron", s.Missing("Ore"));
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

        [Fact]
        public void Set_tier_is_the_biome_of_its_latest_ingredient_not_the_order_in_its_line()
        {
            var s = new StageSolver();
            s.Add("LeatherScraps", new StagePath { Kind = "spawn", Base = 0 });
            s.Add("TrollHide", new StagePath { Kind = "spawn", Base = 2 });
            s.Add("WolfPelt", new StagePath { Kind = "spawn", Base = 6 });
            s.Add("Silver", new StagePath { Kind = "mine", Base = 6 });
            s.Add("ArmorWolfChest", new StagePath { Kind = "craft" }.Need("Silver").Need("WolfPelt"));
            s.Add("ArmorWolfLegs", new StagePath { Kind = "craft" }.Need("WolfPelt"));
            s.Add("HelmetDrake", new StagePath { Kind = "craft" }.Need("LeatherScraps")); // early piece of a Mountain set
            s.Add("ArmorTrollLeatherChest", new StagePath { Kind = "craft" }.Need("TrollHide"));
            s.Solve();
            var facts = new[]
            {
                new ItemFact { Prefab = "ArmorWolfChest", LineId = "wolfguard", LineTier = 0 },
                new ItemFact { Prefab = "ArmorWolfLegs", LineId = "wolfguard", LineTier = 0 },
                new ItemFact { Prefab = "HelmetDrake", LineId = "wolfguard", LineTier = 0 },
                new ItemFact { Prefab = "ArmorTrollLeatherChest", LineId = "shadow", LineTier = 0 },
                new ItemFact { Prefab = "Unsolved", LineId = "x", LineTier = 0 },
                new ItemFact { Prefab = "Weapon", Biome = 2 },
            };
            var b = StageRules.SetBiomes(StageRules.SetMembers(facts, new string[0], _ => null), s.Result);
            Assert.Equal(3, b["ArmorWolfChest"]); // Mountain, though tier 0 of its line
            Assert.Equal(3, b["HelmetDrake"]);    // the set aligns to its latest piece
            Assert.Equal(1, b["ArmorTrollLeatherChest"]);
            Assert.False(b.ContainsKey("Unsolved"));
            Assert.False(b.ContainsKey("Weapon"));
        }

        [Fact]
        public void Armour_outside_a_line_joins_the_line_of_its_vanilla_set()
        {
            var facts = new[]
            {
                new ItemFact { Prefab = "ArmorTrollLeatherChest", LineId = "shadow", LineTier = 0 },
                new ItemFact { Prefab = "HelmetTrollLeather", LineId = "shadow", LineTier = 0 },
                new ItemFact { Prefab = "SwordIron", Biome = 2 },
            };
            var set = new Dictionary<string, string> { ["ArmorTrollLeatherChest"] = "troll", ["HelmetTrollLeather"] = "troll", ["SwordIron"] = "" };
            var d = StageRules.LinesBySet(facts, p => set.TryGetValue(p, out var s) ? s : null);
            Assert.Equal("shadow", d["troll"].LineId); // CapeTrollHide (set "troll", no line fact) lists under shadow
            Assert.Single(d);
        }

        [Fact]
        public void An_attached_cape_counts_in_its_set_biome()
        {
            var s = new StageSolver();
            s.Add("TrollHide", new StagePath { Kind = "spawn", Base = 2 });
            s.Add("WolfPelt", new StagePath { Kind = "spawn", Base = 6 });
            s.Add("ArmorTrollLeatherChest", new StagePath { Kind = "craft" }.Need("TrollHide"));
            s.Add("CapeTrollHide", new StagePath { Kind = "craft" }.Need("WolfPelt")); // later than its set's line pieces
            s.Solve();
            var facts = new[] { new ItemFact { Prefab = "ArmorTrollLeatherChest", LineId = "shadow", LineTier = 0 } };
            var set = new Dictionary<string, string> { ["ArmorTrollLeatherChest"] = "troll", ["CapeTrollHide"] = "troll" };
            var m = StageRules.SetMembers(facts, new[] { "ArmorTrollLeatherChest", "CapeTrollHide", "SwordIron" }, p => set.TryGetValue(p, out var n) ? n : null);
            Assert.Equal("shadow", m["CapeTrollHide"].LineId);
            Assert.False(m.ContainsKey("SwordIron"));
            var b = StageRules.SetBiomes(m, s.Result);
            Assert.Equal(3, b["ArmorTrollLeatherChest"]); // the cape raises the whole set
            Assert.Equal(3, b["CapeTrollHide"]);
        }

        [Fact]
        public void Armour_without_a_line_set_takes_its_own_stage_as_tier()
        {
            var sets = new Dictionary<string, int> { ["ArmorSpellSlingerChest_BlackForest_TW"] = 1 };
            Assert.Equal(1, StageRules.TierBiome("ArmorSpellSlingerChest_BlackForest_TW", sets, new StageInfo { Stage = 6 })); // the set wins
            Assert.Equal(1, StageRules.TierBiome("CapeSpellslinger_Blackforest_TW", sets, new StageInfo { Stage = 2, Via = StageVia.Derived })); // no set: own biome
            Assert.Equal(-1, StageRules.TierBiome("BackpackUnknown", sets, StageInfo.None));
            Assert.Equal(-1, StageRules.TierBiome("MagicCape", sets, new StageInfo { Stage = -1, Via = StageVia.Magic }));
        }

        [Fact]
        public void A_vanilla_set_outside_every_line_aligns_to_its_latest_piece()
        {
            var stage = new Dictionary<string, int> { ["HelmetX"] = 2, ["ChestX"] = 6, ["LegsX"] = -1, ["Lined"] = 2, ["Solo"] = 4 };
            var name = new Dictionary<string, string> { ["HelmetX"] = "x", ["ChestX"] = "x", ["LegsX"] = "x", ["Lined"] = "x" };
            var sets = new Dictionary<string, int> { ["Lined"] = 1 };
            StageRules.AddNameSets(sets, stage.Keys, p => name.TryGetValue(p, out var n) ? n : null, p => new StageInfo { Stage = stage[p] });
            Assert.Equal(3, sets["HelmetX"]);
            Assert.Equal(3, sets["LegsX"]); // an unsolved piece still lists with its set
            Assert.Equal(1, sets["Lined"]); // a line set keeps its own biome
            Assert.False(sets.ContainsKey("Solo"));
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
            Assert.Contains(FacetTable.ChipsOf("armor"), c => c.Token == "$ib_f_set" && c.Facets[0] == "line" && c.Facets.Contains("mod") && !c.Facets.Contains("set"));
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
        public void Sub_line_drops_whole_parts_instead_of_cutting_a_name()
        {
            float M(string s) => s.Length;
            Assert.Equal("7500 HP · Trophy, Tear", ListBlocks.WholeParts("7500 HP · Trophy, Tear, Crystal", 25f, M));
            Assert.Equal("7500 HP", ListBlocks.WholeParts("7500 HP · Trophy, Tear, Crystal", 10f, M));
            Assert.Equal("Averyverylongname", ListBlocks.WholeParts("Averyverylongname", 5f, M)); // one part: the ellipsis takes it
            Assert.Equal("· 30 min · Pot 5", ListBlocks.WholeParts("· 30 min · Pot 5", 99f, M));
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
        public void Table_lines_without_objects_leave_derived_stages_above_them_unknown()
        {
            var old = FoundInTable.Parse("L:Altar\t0\t0\tBonemass:b,Draugr:w\n", out _, out _)["L:Altar"];
            Assert.Null(old.Objects); // FoundInScan.Lookup rereads such a cache line once; the table never has one
            var d = new FoundIn();
            d.Place(old, Heightmap.Biome.Swamp, "L:Altar");
            Assert.False(d.World.Complete);
            Assert.Equal(4, d.World.StaleCap(out var why));
            Assert.Equal("stale:L:Altar", why);
            var ix = new BrowserIndex();
            ix.Add(new Relation { Kind = RelationKind.CreatureDrop, Source = "Bonemass", Outputs = new[] { new Stack("Wishbone") } });
            ix.Add(new Relation { Kind = RelationKind.Pickable, Source = "ChestOnlyInLocation", Outputs = new[] { new Stack("Thing") } });
            var s = new StageSolver();
            foreach (var _ in StageGraph.Build(ix, d.World, new[] { new ItemFact { Prefab = "Thing", Biome = 2 } }, s)) { }
            s.Solve();
            Assert.Equal(-1, s.Result("Wishbone").Stage);
            Assert.Equal(StageVia.Authored, s.Result("Thing").Via);
        }

        [Theory]
        [InlineData(Skills.SkillType.Swords, false, false)] // tankards
        [InlineData(Skills.SkillType.Knives, false, false)] // butcher knife
        [InlineData(Skills.SkillType.Swords, true, true)]
        [InlineData(Skills.SkillType.BloodMagic, false, true)] // summoning staves
        [InlineData(Skills.SkillType.Farming, false, true)]
        [InlineData(Skills.SkillType.None, true, false)]
        public void Weapon_class_needs_damage_on_melee_skills(Skills.SkillType skill, bool deals, bool want) => Assert.Equal(want, StageRules.HasWeaponClass(skill, deals));

        [Fact]
        public void Bosses_lead_their_stage_and_rich_text_does_not_decide_the_name_order()
        {
            var ix = new BrowserIndex();
            ix.Add(new Entry { Id = "Hildir_Boss", Name = "<color=orange>Гейрафа</color>", Type = EntryType.Creature });
            ix.Add(new Entry { Id = "Wolf", Name = "Волк", Type = EntryType.Creature });
            ix.Add(new Entry { Id = "Dragon", Name = "Матерь", Type = EntryType.Creature });
            var found = ix.Search("", EntryType.Creature, null, n => n, m => m);
            Assert.Equal(new[] { "Wolf", "Hildir_Boss", "Dragon" }, found.Select(e => e.Id));
            Assert.Empty(ix.Search("orange", EntryType.Creature, null, n => n, m => m));
            var ordered = ListBlocks.Order(found, _ => new StageInfo { Stage = 6, Via = StageVia.Derived }, e => e.Id == "Dragon");
            Assert.Equal(new[] { "Dragon", "Wolf", "Hildir_Boss" }, ordered.Select(e => e.Id));
        }

        [Fact]
        public void Sap_needs_the_collector_and_its_root_so_it_lands_where_the_root_grows()
        {
            var ix = new BrowserIndex();
            var w = new StageWorld();
            w.Anchor("piece_sapcollector", Heightmap.Biome.Meadows, true);
            w.Tapped.Add(("Sap", "piece_sapcollector", "YggdrasilRoot"));
            var s = new StageSolver();
            foreach (var _ in StageGraph.Build(ix, w, null, s)) { }
            s.Solve();
            Assert.Equal(-1, s.Result("Sap").Stage);
            w.Anchor("YggdrasilRoot", Heightmap.Biome.Mistlands, false);
            s = new StageSolver();
            foreach (var _ in StageGraph.Build(ix, w, null, s)) { }
            s.Solve();
            Assert.Equal(10, s.Result("Sap").Stage);
        }

        [Fact]
        public void Table_round_trips_objects_and_summons_and_old_lines_stay_unscanned()
        {
            var s = new Scanned { Themes = 2, Interior = true };
            s.Add("Draugr", Origin.World);
            s.AddObject("Pickable_X");
            s.AddObject("TreasureChest");
            s.Summons.Add(("Bonemass", "WitheredBone", 10));
            var text = FoundInTable.Write("1.0", new Dictionary<string, Scanned> { ["L:Loc"] = s });
            var back = FoundInTable.Parse(text + "L:Old\t0\t0\tBoar:w\n", out _, out _);
            Assert.Equal(new[] { "Pickable_X", "TreasureChest" }, back["L:Loc"].Objects);
            Assert.Equal(("Bonemass", "WitheredBone", 10), back["L:Loc"].Summons[0]);
            Assert.Null(back["L:Old"].Objects);
        }
    }
}
