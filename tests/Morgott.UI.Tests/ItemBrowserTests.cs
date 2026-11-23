using System.Collections.Generic;
using System.Linq;
using Morgott.Core;
using Morgott.UI.ItemBrowser;
using Xunit;

namespace Morgott.UI.Tests
{
    // BrowserIndex: relation dedupe key, Obtained from / Used in lookups, pseudo sources, search, history, fingerprint.
    public class ItemBrowserTests
    {
        private static Relation Craft(string item, int quality = 1, params string[] ins) => new Relation
        {
            Kind = quality == 1 ? RelationKind.Craft : RelationKind.Upgrade,
            Source = "Recipe_" + item,
            Station = "forge",
            StationLevel = quality,
            Quality = quality,
            Inputs = ins.Select(i => new Stack(i, 2, 2)).ToArray(),
            Outputs = new[] { new Stack(item) }
        };

        private static Relation Drop(string from, string item, float chance = 0.5f, string cond = null) => new Relation
        {
            Kind = RelationKind.CreatureDrop,
            Source = from,
            Outputs = new[] { new Stack(item, 1, 1, chance) },
            Conditions = cond
        };

        private static BrowserIndex Sample()
        {
            var ix = new BrowserIndex();
            ix.Add(new Entry { Id = "Bronze", Name = "$item_bronze", Type = EntryType.Item, Mod = null });
            ix.Add(new Entry { Id = "Copper", Name = "$item_copper", Type = EntryType.Item });
            ix.Add(new Entry { Id = "Tin", Name = "$item_tin", Type = EntryType.Item });
            ix.Add(new Entry { Id = "forge", Name = "$piece_forge", Type = EntryType.Station });
            ix.Add(new Entry { Id = "HC_WildEgg", Name = "$hc_wildegg", Type = EntryType.Item, Mod = "morgott.hearthcraft" });
            ix.Add(new Entry { Id = "Seagal", Name = "$enemy_seagal", Type = EntryType.Creature });
            ix.Add(new Entry { Id = "Hidden", Name = "$hidden_bronze", Type = EntryType.Item, Hidden = "no icon" });
            ix.Add(Craft("Bronze", 1, "Copper", "Tin"));
            ix.Add(Drop("Seagal", "HC_WildEgg"));
            return ix;
        }

        private static string Loc(string t) => t switch
        {
            "$item_bronze" => "Bronze",
            "$item_copper" => "Copper",
            "$item_tin" => "Tin",
            "$piece_forge" => "Forge",
            "$hc_wildegg" => "Wild Egg",
            "$enemy_seagal" => "Seagull",
            "$hidden_bronze" => "Bronze nail",
            _ => t
        };

        private static string Mod(string guid) => guid == "morgott.hearthcraft" ? "Hearthcraft" : null;

        [Fact]
        public void ChanceConditionReadsAsOnePhrase()
        {
            Assert.Equal("Chance 3.3% per kill", PageModel.JoinChance(new List<string>(), "Chance 3.3%", "per kill"));
            Assert.Equal("x1-2  Chance 50%", PageModel.JoinChance(new List<string> { "x1-2" }, "Chance 50%", null));
            Assert.Equal("x2  night", PageModel.JoinChance(new List<string> { "x2" }, null, "night"));
            Assert.Equal("", PageModel.JoinChance(new List<string>(), null, null));
        }

        [Fact]
        public void EqualRelationIsDeduped()
        {
            var ix = Sample();
            Assert.False(ix.Add(Craft("Bronze", 1, "Copper", "Tin")));
            Assert.Single(ix.ObtainedFrom("Bronze"));
        }

        [Fact]
        public void VariantsDoNotMerge()
        {
            var ix = Sample();
            Assert.True(ix.Add(Craft("Bronze", 2, "Copper", "Tin")));                    // other quality
            Assert.True(ix.Add(Drop("Seagal", "HC_WildEgg", 0.5f, "$ib_cond_oneperplayer")));  // other conditions
            Assert.True(ix.Add(Drop("Seagal", "HC_WildEgg", 0.25f)));                     // other chance
            Assert.Equal(2, ix.ObtainedFrom("Bronze").Count);
            Assert.Equal(3, ix.ObtainedFrom("HC_WildEgg").Count);
        }

        [Fact]
        public void UsedInCoversInputsSourceAndStation()
        {
            var ix = Sample();
            Assert.Single(ix.UsedIn("Copper"));
            Assert.Single(ix.UsedIn("forge"));
            Assert.Single(ix.UsedIn("Seagal"));                       // a creature's drops
            Assert.Equal(new[] { "Bronze" }, ix.UsedInResults("Tin"));
            Assert.Equal(new[] { "HC_WildEgg" }, ix.UsedInResults("Seagal"));
        }

        [Fact]
        public void UnknownSourceBecomesPseudoEntry()
        {
            var ix = Sample();
            ix.Add(new Relation { Kind = RelationKind.LootTable, Source = "EL:loottable:Troll", Outputs = new[] { new Stack("Tin", 1, 1, float.NaN) } });
            Assert.Equal(EntryType.Other, ix.Entries["EL:loottable:Troll"].Type);
            Assert.Null(ix.Entries["EL:loottable:Troll"].Hidden);
            Assert.Single(ix.ObtainedFrom("Tin"));
        }

        [Fact]
        public void NaNChancesDedupe()
        {
            var ix = Sample();
            var r = new Relation { Kind = RelationKind.Mine, Source = "rock", Outputs = new[] { new Stack("Tin", 1, 2, float.NaN) } };
            Assert.True(ix.Add(r));
            Assert.False(ix.Add(new Relation { Kind = RelationKind.Mine, Source = "rock", Outputs = new[] { new Stack("Tin", 1, 2, float.NaN) } }));
        }

        [Fact]
        public void SearchMatchesLocalizedNameOrPrefabAndSkipsHidden()
        {
            var ix = Sample();
            Assert.Equal(new[] { "Bronze" }, ix.Search("bron", null, null, Loc, Mod).Select(e => e.Id));
            Assert.Equal(new[] { "HC_WildEgg" }, ix.Search("hc_wild", null, null, Loc, Mod).Select(e => e.Id));
            Assert.Equal(new[] { "HC_WildEgg" }, ix.Search("wild egg", null, null, Loc, Mod).Select(e => e.Id));
            Assert.Empty(ix.Search("nail", null, null, Loc, Mod));
        }

        [Fact]
        public void UnlistedVanillaPrefabsAreHidden()
        {
            var ix = Sample();
            Assert.Equal("unlisted", ix.Add(new Entry { Id = "CapeTest", Name = "$item_cape_test", Type = EntryType.Item }).Hidden);
            Assert.Equal("unlisted", ix.Add(new Entry { Id = "IceShoes", Name = "$item_iceshoes", Type = EntryType.Item }).Hidden); // no recipe, no source (run 20261118-131013)
            Assert.Equal("unlisted", ix.Add(new Entry { Id = "IceSkates", Name = "$item_iceskates", Type = EntryType.Item }).Hidden);
            Assert.Equal("no icon", ix.Add(new Entry { Id = "TorchMist", Name = "$item_torch", Type = EntryType.Item, Hidden = "no icon" }).Hidden); // first reason kept
            Assert.Null(ix.Add(new Entry { Id = "CapeLinen", Name = "$item_cape_linen", Type = EntryType.Item }).Hidden);
            Assert.Equal(new[] { "CapeLinen" }, ix.Search("cape", null, null, Loc, Mod).Select(e => e.Id));
            Assert.Equal(new[] { "CapeLinen", "CapeTest" }, ix.Search("cape", null, null, Loc, Mod, unlisted: true).Select(e => e.Id).OrderBy(i => i));
            Assert.Empty(ix.Search("torch", null, null, Loc, Mod, unlisted: true)); // other hide reasons stay hidden
        }

        [Fact]
        public void NpcGearNothingProducesIsUnlisted()
        {
            var ix = Sample();
            ix.Add(new Entry { Id = "FW_CapeWolf", Name = "$item_cape_wolf", Type = EntryType.Item });
            ix.Add(new Entry { Id = "FW_KnifeSilver", Name = "$item_knife_silver", Type = EntryType.Item });
            ix.Add(Drop("FallenWarrior", "FW_KnifeSilver")); // its own CharacterDrop makes it obtainable
            foreach (var id in new[] { "FW_CapeWolf", "FW_KnifeSilver", "Bronze", "Seagal" }) ix.NpcGear.Add(id);
            ix.HideNpcOnlyGear();
            Assert.Equal(BrowserIndex.UnlistedReason, ix.Entries["FW_CapeWolf"].Hidden);
            Assert.Null(ix.Entries["FW_KnifeSilver"].Hidden);
            Assert.Null(ix.Entries["Bronze"].Hidden);  // crafted
            Assert.Null(ix.Entries["Seagal"].Hidden);  // no item
        }

        [Fact]
        public void ModFilterMatchesGuidOrName()
        {
            var ix = Sample();
            Assert.Equal(new[] { "HC_WildEgg" }, ix.Search("@hearth", null, null, Loc, Mod).Select(e => e.Id));
            Assert.Equal(new[] { "HC_WildEgg" }, ix.Search("@morgott.hearthcraft egg", null, null, Loc, Mod).Select(e => e.Id));
            Assert.Contains(ix.Search("@valheim", EntryType.Item, null, Loc, Mod), e => e.Id == "Copper");
        }

        [Fact]
        public void TypeAndFilterAndOrder()
        {
            var ix = Sample();
            Assert.Equal(new[] { "Bronze", "Copper", "Tin", "HC_WildEgg" }, ix.Search("", EntryType.Item, null, Loc, Mod).Select(e => e.Id));
            Assert.Equal(new[] { "forge" }, ix.Search("", EntryType.Station, null, Loc, Mod).Select(e => e.Id));
            Assert.Equal(new[] { "Tin" }, ix.Search("", null, e => e.Id == "Tin", Loc, Mod).Select(e => e.Id));
            // All: by type (enum order: items, pieces, creatures, stations, other), each by localized name.
            Assert.Equal(new[] { "Bronze", "Copper", "Tin", "HC_WildEgg", "Seagal", "forge" }, ix.Search("", null, null, Loc, Mod).Select(e => e.Id));
        }

        [Fact]
        public void FirstEntryWins()
        {
            var ix = Sample();
            var e = ix.Add(new Entry { Id = "Tin", Name = "$other", Type = EntryType.Other });
            Assert.Equal("$item_tin", e.Name);
        }

        [Fact]
        public void HistoryBackForwardAndTruncate()
        {
            var h = new History();
            h.Go("a");
            h.Go("b");
            h.Go("c");
            Assert.Equal("b", h.Back());
            Assert.Equal("a", h.Back());
            Assert.Null(h.Back());
            Assert.Equal("b", h.Forward());
            h.Go("d"); // drops "c"
            Assert.Null(h.Forward());
            Assert.Equal("b", h.Back());
            h.Go("b"); // same page: no new step
            Assert.Equal("d", h.Forward());
        }

        [Fact]
        public void FingerprintSeesContentNotOnlyCounts()
        {
            int Fp(int amount) => BrowserIndex.Mix(BrowserIndex.Mix(BrowserIndex.Seed, "Recipe_Bronze"), amount);
            Assert.NotEqual(Fp(2), Fp(3));
            Assert.Equal(Fp(2), Fp(2));
            Assert.NotEqual(BrowserIndex.Mix(BrowserIndex.Mix(BrowserIndex.Seed, "ab"), "c"), BrowserIndex.Mix(BrowserIndex.Mix(BrowserIndex.Seed, "a"), "bc"));
        }

        [Fact]
        public void ChangedKeysNamesRehashedAddedAndRemovedRecipes()
        {
            var a = new Dictionary<string, int> { ["Recipe_A"] = 1, ["Recipe_B"] = 2, ["Recipe_Gone"] = 3 };
            var b = new Dictionary<string, int> { ["Recipe_A"] = 1, ["Recipe_B"] = 5, ["Recipe_New"] = 4 };
            Assert.Equal("Recipe_B, Recipe_Gone, Recipe_New", BrowserIndex.ChangedKeys(a, b, 8));
            Assert.Equal("Recipe_B (+2)", BrowserIndex.ChangedKeys(a, b, 1));
            Assert.Equal("", BrowserIndex.ChangedKeys(a, a, 8));
        }

        // ---------------------------------------------------------------- PageModel (item page)

        private static readonly ItemDrop.ItemData.ItemType One = ItemDrop.ItemData.ItemType.OneHandedWeapon;

        [Theory]
        [InlineData(ItemDrop.ItemData.ItemType.OneHandedWeapon, Skills.SkillType.Swords, false, 0f, false, ItemCategory.Weapons)]
        [InlineData(ItemDrop.ItemData.ItemType.Shield, Skills.SkillType.Blocking, false, 0f, false, ItemCategory.Weapons)]
        [InlineData(ItemDrop.ItemData.ItemType.Torch, Skills.SkillType.Clubs, false, 0f, false, ItemCategory.Weapons)]
        [InlineData(ItemDrop.ItemData.ItemType.TwoHandedWeapon, Skills.SkillType.Pickaxes, false, 0f, false, ItemCategory.Tools)]
        [InlineData(ItemDrop.ItemData.ItemType.OneHandedWeapon, Skills.SkillType.Fishing, false, 0f, false, ItemCategory.Tools)]
        [InlineData(ItemDrop.ItemData.ItemType.OneHandedWeapon, Skills.SkillType.Swords, true, 0f, false, ItemCategory.Tools)]   // Hammer: build pieces
        [InlineData(ItemDrop.ItemData.ItemType.Tool, Skills.SkillType.Swords, false, 0f, false, ItemCategory.Tools)]
        [InlineData(ItemDrop.ItemData.ItemType.Shoulder, Skills.SkillType.Swords, false, 0f, false, ItemCategory.Armor)]
        [InlineData(ItemDrop.ItemData.ItemType.Trinket, Skills.SkillType.Swords, false, 0f, false, ItemCategory.Utility)]
        [InlineData(ItemDrop.ItemData.ItemType.Utility, Skills.SkillType.Swords, false, 0f, false, ItemCategory.Utility)]
        [InlineData(ItemDrop.ItemData.ItemType.AmmoNonEquipable, Skills.SkillType.Swords, false, 0f, false, ItemCategory.Ammo)]
        [InlineData(ItemDrop.ItemData.ItemType.Consumable, Skills.SkillType.Swords, false, 20f, false, ItemCategory.Food)]
        [InlineData(ItemDrop.ItemData.ItemType.Consumable, Skills.SkillType.Swords, false, 0f, true, ItemCategory.Meads)]
        [InlineData(ItemDrop.ItemData.ItemType.Consumable, Skills.SkillType.Swords, false, 0f, false, ItemCategory.Misc)]
        [InlineData(ItemDrop.ItemData.ItemType.Material, Skills.SkillType.Swords, false, 0f, false, ItemCategory.Materials)]
        [InlineData(ItemDrop.ItemData.ItemType.Trophy, Skills.SkillType.Swords, false, 0f, false, ItemCategory.Trophies)]
        [InlineData(ItemDrop.ItemData.ItemType.Fish, Skills.SkillType.Swords, false, 0f, false, ItemCategory.Fish)]
        [InlineData(ItemDrop.ItemData.ItemType.Misc, Skills.SkillType.Swords, false, 0f, false, ItemCategory.Misc)]
        public void Categorize(ItemDrop.ItemData.ItemType type, Skills.SkillType skill, bool pieces, float food, bool effect, ItemCategory want) =>
            Assert.Equal(want, PageModel.Categorize(type, skill, pieces, food, 0f, 0f, effect));

        [Fact]
        public void EpicLootShardstonesAreGems()
        {
            const ItemDrop.ItemData.ItemType mat = ItemDrop.ItemData.ItemType.Material;
            Assert.Equal(ItemCategory.Gems, PageModel.Categorize(mat, Skills.SkillType.Swords, false, 0f, 0f, 0f, false, "1013|Mythic|ShardStone"));
            Assert.Equal(ItemCategory.Gems, PageModel.Categorize(mat, Skills.SkillType.Swords, false, 0f, 0f, 0f, false, "72|Legendary|ShardStone"));
            Assert.Equal(ItemCategory.Materials, PageModel.Categorize(mat, Skills.SkillType.Swords, false, 0f, 0f, 0f, false, "MagicCraftingMaterial"));
            Assert.Equal(ItemCategory.Materials, PageModel.Categorize(mat, Skills.SkillType.Swords, false, 0f, 0f, 0f, false));
            Assert.Equal(ItemCategory.Trophies, PageModel.Categorize(ItemDrop.ItemData.ItemType.Trophy, Skills.SkillType.Swords, false, 0f, 0f, 0f, false, "x|ShardStone"));
            Assert.Equal(ItemCategory.Misc - 1, ItemCategory.Gems); // the button row: gems right before "..."
        }

        [Fact]
        public void ModChipOnlyWithDevcommands()
        {
            Assert.Contains(FacetTable.ChipsOf("gems", true), c => c.Token == "$ib_f_mod");
            Assert.DoesNotContain(FacetTable.ChipsOf("gems", false), c => c.Token == "$ib_f_mod");
            Assert.Empty(FacetTable.ChipsOf("items", false)); // Plain = Mod only
            Assert.Contains(FacetTable.ChipsOf("armor", false), c => c.Token == "$ib_f_set" && c.Facets.Contains("mod")); // the set chip keeps its mod section
            var s = new FilterState();
            s.Toggle("mod", "valheim", false);
            s.Toggle("source", "killdrop", true);
            FacetTable.DropHidden(s, FacetTable.ChipsOf("gems", false));
            Assert.False(s.Picked.ContainsKey("mod"));
            Assert.True(s.Picked.ContainsKey("source"));
        }

        [Fact]
        public void SearchMatchesExtraText()
        {
            var ix = new BrowserIndex();
            ix.Add(new Entry { Id = "1013_Mythic_ShardStone", Name = "Rotting Heart T3", Type = EntryType.Item, Category = ItemCategory.Gems });
            ix.Add(new Entry { Id = "Iron", Name = "Iron", Type = EntryType.Item });
            var hits = ix.Search("bonemass", null, null, n => n, m => m, false, e => e.Category == ItemCategory.Gems ? "\nbonemass\nbonemass" : "");
            Assert.Equal("1013_Mythic_ShardStone", Assert.Single(hits).Id);
            Assert.Empty(ix.Search("bonemass", null, null, n => n, m => m));
        }

        [Fact]
        public void EitrOrStaminaFoodIsFood()
        {
            Assert.Equal(ItemCategory.Food, PageModel.Categorize(ItemDrop.ItemData.ItemType.Consumable, Skills.SkillType.Swords, false, 0f, 0f, 30f, true));
            Assert.Equal(ItemCategory.Food, PageModel.Categorize(ItemDrop.ItemData.ItemType.Consumable, Skills.SkillType.Swords, false, 0f, 10f, 0f, false));
            Assert.Equal(ItemCategory.Weapons, PageModel.Categorize(One, Skills.SkillType.Swords, false, 0f, 0f, 0f, false));
        }

        // SwordBronze: craft at forge (q1), a second modded recipe (q1, workbench), upgrades q2..q3, a drop, used in an atgeir.
        private static BrowserIndex Sword()
        {
            var ix = Sample();
            ix.Add(new Entry { Id = "SwordBronze", Name = "$item_sword_bronze", Type = EntryType.Item, MaxQuality = 3 });
            ix.Add(new Entry { Id = "Draugr", Name = "$enemy_draugr", Type = EntryType.Creature });
            ix.Add(Craft("SwordBronze", 1, "Bronze", "Wood"));
            ix.Add(new Relation { Kind = RelationKind.Craft, Source = "Recipe_SwordBronze_alt", Station = "piece_workbench", StationLevel = 2, Quality = 1, Inputs = new[] { new Stack("Copper", 6, 6), new Stack("Tin", 2, 2) }, Outputs = new[] { new Stack("SwordBronze") } });
            ix.Add(Craft("SwordBronze", 2, "Bronze"));
            ix.Add(Craft("SwordBronze", 3, "Bronze", "Tin"));
            ix.Add(Drop("Draugr", "SwordBronze", 0.05f));
            ix.Add(new Relation { Kind = RelationKind.Craft, Source = "Recipe_AtgeirBronze", Station = "forge", Quality = 1, Inputs = new[] { new Stack("SwordBronze", 1, 1), new Stack("Bronze", 10, 10) }, Outputs = new[] { new Stack("AtgeirBronze") } });
            return ix;
        }

        [Fact]
        public void ObtainAtQualityOneHasRecipesThenDrops()
        {
            var rows = PageModel.Obtain(Sword(), "SwordBronze", 1);
            Assert.Equal(new[] { RelationKind.Craft, RelationKind.Craft, RelationKind.CreatureDrop }, rows.Select(r => r.R.Kind));
            Assert.True(rows[0].Recipe);
            Assert.Equal("forge", rows[0].Right);
            Assert.Equal(new[] { "Bronze", "Wood" }, rows[0].Inputs.Select(s => s.Prefab));
            Assert.False(rows[2].Recipe);
            Assert.Equal("Draugr", rows[2].Icon);
            Assert.Null(rows[2].Right);
        }

        [Fact]
        public void UpgradeRowStartsWithTheItemOneQualityLowerAndDropsAreQualityOneOnly()
        {
            var rows = PageModel.Obtain(Sword(), "SwordBronze", 3);
            var up = Assert.Single(rows);
            Assert.Equal(RelationKind.Upgrade, up.R.Kind);
            Assert.Equal(new[] { "SwordBronze", "Bronze", "Tin" }, up.Inputs.Select(s => s.Prefab));
            Assert.Equal(1, up.Inputs[0].Min);
            Assert.Equal(2, up.BaseQuality);
            Assert.Equal(3, up.Quality);
            Assert.Equal(2, up.Lines);
            Assert.Equal(PageModel.RowH + PageModel.LineH, PageModel.Height(up, false));
        }

        [Fact]
        public void UsedInListsEveryRelationAndOwnUpgradesUnfiltered()
        {
            var rows = PageModel.UsedIn(Sword(), "SwordBronze");
            Assert.Equal(new[] { "SwordBronze", "SwordBronze", "AtgeirBronze" }, rows.Select(r => r.Icon));
            Assert.Equal(new[] { 2, 3, 0 }, rows.Select(r => r.Quality));
            Assert.Equal("forge", rows[2].Right);
            // A creature's page: its drops, no source repeated on the right.
            var drop = Assert.Single(PageModel.UsedIn(Sword(), "Draugr"));
            Assert.Equal("SwordBronze", drop.Icon);
            Assert.Null(drop.Right);
            Assert.Empty(drop.Inputs);
            // A relation with several results: a row each.
            var ix = Sword();
            ix.Add(new Relation { Kind = RelationKind.LootTable, Source = "EL:loottable:Draugr", Outputs = new[] { new Stack("Tin", 1, 1, float.NaN), new Stack("Copper", 1, 1, float.NaN) } });
            Assert.Equal(new[] { "Tin", "Copper" }, PageModel.UsedIn(ix, "EL:loottable:Draugr").Select(r => r.Icon));
            // A cost sink (its own output): a row on its input's page, none on its own page.
            ix.Add(new Relation { Kind = RelationKind.Other, Source = "EL:enchant:Rare", Station = "piece_enchanter", Inputs = new[] { new Stack("Tin", 5, 5) }, Outputs = new[] { new Stack("EL:enchant:Rare") } });
            Assert.Contains(PageModel.UsedIn(ix, "Tin"), r => r.Icon == "EL:enchant:Rare" && r.Right == "piece_enchanter");
            Assert.Empty(PageModel.UsedIn(ix, "EL:enchant:Rare"));
        }

        // Consumers that make no item (fuel, guardian stone, turret target) and altar offerings: a "Used in" row on the
        // consumed item's page, hidden pseudo entries, nothing added to the host's own "Obtained from".
        [Fact]
        public void UsesAndOfferingsReachUsedIn()
        {
            var ix = Sword();
            ix.Use("use:power:BossStone_Eikthyr", "$GP_Eikthyr", "BossStone_Eikthyr", null, new[] { new Stack("TrophyEikthyr") }, "$ib_use_power", true);
            ix.Use("use:target:piece_turret:TrophyGreydwarf", "$enemy_greydwarf", "Greydwarf", "piece_turret", new[] { new Stack("TrophyGreydwarf") }, "$ib_use_target");
            ix.Use("use:fuel:fire_pit", "$ib_use_fuel", "fire_pit", "fire_pit", new[] { new Stack("Wood") }, null, true);
            ix.Use("use:fuel:empty", "$ib_use_fuel", "empty", "empty", new Stack[0], null);
            ix.Offering("Eikthyr", new[] { ("TrophyDeer", 2) });
            ix.Offering("Dragon", new[] { ("", 0) }); // an older table line without the item: nothing
            var power = Assert.Single(PageModel.UsedIn(ix, "TrophyEikthyr"));
            Assert.Equal("use:power:BossStone_Eikthyr", power.Icon);
            Assert.Equal("BossStone_Eikthyr", power.Right); // its host, never "By hand" (Codex review)
            Assert.Equal("piece_turret", Assert.Single(PageModel.UsedIn(ix, "TrophyGreydwarf")).Right);
            Assert.Contains(PageModel.UsedIn(ix, "Wood"), r => r.Icon == "use:fuel:fire_pit" && r.Right == "fire_pit");
            ix.Use("use:weapon:Bow", "$ib_use_ammo", "Bow", null, new[] { new Stack("ArrowWood"), new Stack("ArrowFire") }, null, true);
            Assert.Equal("Bow", Assert.Single(PageModel.UsedIn(ix, "ArrowFire")).Right); // host on the right
            Assert.Empty(PageModel.UsedIn(ix, "Bow")); // the weapon page does not list its own ammo
            var altar = Assert.Single(PageModel.UsedIn(ix, "TrophyDeer"));
            Assert.Equal("Eikthyr", altar.Icon);
            Assert.Equal("use:altar:Eikthyr", altar.Right);
            Assert.Equal(2, altar.Inputs[0].Min);
            Assert.Equal("use:altar:Eikthyr", Assert.Single(ix.ObtainedFrom("Eikthyr")).Source);
            Assert.Empty(ix.ObtainedFrom("fire_pit"));
            Assert.False(ix.Entries.ContainsKey("use:fuel:empty"));
            Assert.False(ix.Entries.ContainsKey("use:altar:Dragon"));
            Assert.All(new[] { "use:power:BossStone_Eikthyr", "use:altar:Eikthyr", "use:fuel:fire_pit" }, id => Assert.Equal("use", ix.Entries[id].Hidden));
        }

        // Player report: a trophy's crafting recipe sat below the incinerator's ~50-trophy rows and turret targets, off the first
        // screen of "Used in". Recipes lead, then conversions, then the rest.
        [Fact]
        public void UsedInListsRecipesFirst()
        {
            var ix = new BrowserIndex();
            ix.Use("use:target:piece_turret:TrophyGreydwarf", "$enemy_greydwarf", "Greydwarf", "piece_turret", new[] { new Stack("TrophyGreydwarf") }, "$ib_use_target");
            ix.Add(new Relation { Kind = RelationKind.Conversion, Source = "incinerator", Station = "incinerator", Inputs = new[] { new Stack("TrophyGreydwarf"), new Stack("TrophyBoar") }, Outputs = new[] { new Stack("Coal") }, AnyInput = true });
            ix.Add(new Relation { Kind = RelationKind.Craft, Source = "Recipe_FistBronze_TW", Inputs = new[] { new Stack("Bronze", 10, 10), new Stack("TrophyGreydwarf", 2, 2) }, Outputs = new[] { new Stack("FistBronze_TW") } });
            Assert.Equal(new[] { "FistBronze_TW", "Coal", "use:target:piece_turret:TrophyGreydwarf" }, PageModel.UsedIn(ix, "TrophyGreydwarf").Select(r => r.Icon));
        }

        // Item-stand altars: one row per altar; a group with alternatives takes any of them on each stand (1..stands each,
        // "one per stand" + total), a plain group its exact count; no alternative is ever dropped.
        [Fact]
        public void AltarGroupsAndAlternatives()
        {
            var ix = new BrowserIndex();
            ix.Offering("Fader", new[] { ("Bell|BellFragment", 3) });
            var any = Assert.Single(ix.UsedIn("Bell"));
            Assert.False(any.AnyInput);
            Assert.Equal("$ib_cond_stands 3", any.Conditions);
            Assert.Equal(new[] { ("Bell", 1, 3), ("BellFragment", 1, 3) }, any.Inputs.Select(s => (s.Prefab, s.Min, s.Max)));
            ix.Offering("Mixed", new[] { ("A|B", 2), ("C", 1) });
            var row = Assert.Single(ix.ObtainedFrom("Mixed"));
            Assert.Equal("A:1-2,B:1-2,C:1-1", string.Join(",", row.Inputs.Select(s => $"{s.Prefab}:{s.Min}-{s.Max}")));
            ix.Offering("Eikthyr", new[] { ("TrophyDeer", 2) });
            Assert.Null(Assert.Single(ix.ObtainedFrom("Eikthyr")).Conditions);
        }
        [Fact]
        public void VariantFeedsTheMaterialsTake()
        {
            var ix = Sword();
            Assert.Equal(2, PageModel.Variants(ix, "SwordBronze", 1).Count);
            Assert.Equal(new[] { ("Bronze", 2, 1), ("Wood", 2, 1) }, PageModel.Materials(ix, "SwordBronze", 1, 0));
            Assert.Equal(new[] { ("Copper", 6, 1), ("Tin", 2, 1) }, PageModel.Materials(ix, "SwordBronze", 1, 1));
            Assert.Equal(new[] { ("Copper", 6, 1), ("Tin", 2, 1) }, PageModel.Materials(ix, "SwordBronze", 1, 9)); // clamped
            Assert.Null(PageModel.Materials(ix, "Tin", 1, 0));
        }

        [Fact]
        public void QualityCarriesIntoTheTake()
        {
            var ix = Sword();
            // Quality 3: the q3 upgrade's inputs plus the base at quality 2 (was ObjectDB.GetRecipe + hardcoded quality 1).
            Assert.Equal(new[] { ("SwordBronze", 1, 2), ("Bronze", 2, 1), ("Tin", 2, 1) }, PageModel.Materials(ix, "SwordBronze", 3, 0));
            Assert.Single(PageModel.Variants(ix, "SwordBronze", 2));
            Assert.Empty(PageModel.Variants(ix, "SwordBronze", 4));
        }

        [Fact]
        public void VariantLabelsNameStationAndLevelThenNumberRepeats()
        {
            var ix = Sword();
            ix.Add(new Relation { Kind = RelationKind.Craft, Source = "Recipe_SwordBronze_3", Station = "forge", StationLevel = 1, Quality = 1, Inputs = new[] { new Stack("Tin", 9, 9) }, Outputs = new[] { new Stack("SwordBronze") } });
            var labels = PageModel.VariantLabels(PageModel.Variants(ix, "SwordBronze", 1), id => id == "forge" ? "Forge" : "Workbench", "By hand", "lvl", "Variant");
            Assert.Equal(new[] { "Forge lvl1", "Workbench lvl2", "Variant 3" }, labels);
        }

        [Fact]
        public void GridThumbHasAFloorAndDragsProportionally()
        {
            Assert.Equal(PageModel.ThumbMinH, PageModel.ThumbH(448f, 4, 603)); // 3614 entries: share 3 units -> floor
            Assert.Equal(224f, PageModel.ThumbH(448f, 4, 8));                  // half visible -> half the track
            Assert.Equal(40f, PageModel.ThumbH(40f, 1, 100));                 // floor never exceeds the track
            Assert.Equal(0, PageModel.ScrollAt(-30f, 448f, 48f, 599));
            Assert.Equal(300, PageModel.ScrollAt(200f, 448f, 48f, 599));       // middle of the travel -> ~half
            Assert.Equal(599, PageModel.ScrollAt(500f, 448f, 48f, 599));
            Assert.Equal(0, PageModel.ScrollAt(10f, 48f, 48f, 5));             // no travel
            Assert.Equal(10f, PageModel.GrabOffset(110f, 100f, 48f));          // grabbed inside the thumb
            Assert.Equal(24f, PageModel.GrabOffset(300f, 100f, 48f));          // track click: thumb centres on the pointer
        }

        [Fact]
        public void MaxFirstStopsWhenTheTailFits()
        {
            var h = new[] { 44f, 61f, 91f, 44f, 83f };
            Assert.Equal(0, PageModel.MaxFirst(h, 400f));
            Assert.Equal(2, PageModel.MaxFirst(h, 220f));  // 91 + 44 + 83 = 218 fits, + 61 does not
            Assert.Equal(4, PageModel.MaxFirst(h, 50f));   // the last block alone is taller: it is the last stop
            Assert.Equal(0, PageModel.MaxFirst(new float[0], 10f));
        }

        [Fact]
        public void Row_taller_than_the_view_splits_into_rows_that_fit()
        {
            var ins = new Stack[7];
            for (var i = 0; i < ins.Length; i++) ins[i] = new Stack("T" + i, 1, 1);
            var row = new PageRow { Icon = "Coal", Inputs = ins, BaseQuality = 2, Right = "incinerator", Recipe = true };
            var lines = new[] { 0, 0, 1, 1, 2, 2, 3 };
            Assert.Same(row, Assert.Single(PageModel.SplitByLines(row, lines, 4)));
            var parts = PageModel.SplitByLines(row, lines, 2);
            Assert.Equal(new[] { 4, 3 }, parts.ConvertAll(p => p.Inputs.Length));
            Assert.Equal(new[] { "T4", "T5", "T6" }, parts[1].Inputs.Select(s => s.Prefab));
            Assert.Equal(new[] { 2, 0 }, parts.ConvertAll(p => p.BaseQuality)); // the upgrade base is only the first input
            Assert.All(parts, p => Assert.Equal("incinerator", p.Right));
            Assert.Equal(4, PageModel.SplitByLines(row, lines, 1).Count);
        }

        [Fact]
        public void Long_conditions_wrap_at_word_bounds()
        {
            float M(string s) => s.Length; // one unit per char
            Assert.Equal(new[] { "Any creature", "killed in", "this biome" }, PageModel.Wrap("Any creature killed in  this biome", 12f, M));
            Assert.Equal(new[] { "short" }, PageModel.Wrap("short", 12f, M));
            Assert.Equal(new[] { "a", "Averyverylongword", "b" }, PageModel.Wrap("a Averyverylongword b", 5f, M)); // own line, never split
            Assert.Empty(PageModel.Wrap("", 5f, M));
        }

        [Theory]
        [InlineData(true, false, true)]   // KnownOnly on: filtered
        [InlineData(true, true, false)]   // devcommands overrides KnownOnly
        [InlineData(false, false, false)]
        [InlineData(false, true, false)]
        public void Devcommands_lists_unknown_entries(bool knownOnly, bool dev, bool filtered) =>
            Assert.Equal(filtered, PageModel.KnownFilter(knownOnly, dev));

        [Theory]
        [InlineData(true, false, false, PageSection.Recipe)]
        [InlineData(false, true, false, PageSection.UsedIn)]
        [InlineData(true, true, false, PageSection.Recipe)]  // both in one frame: recipe wins
        [InlineData(true, false, true, PageSection.None)]    // typing in a text field / chat
        [InlineData(false, true, true, PageSection.None)]
        [InlineData(false, false, false, PageSection.None)]
        public void Hover_keys_route_to_a_section(bool recipe, bool used, bool typing, PageSection want) =>
            Assert.Equal(want, PageModel.HoverKey(recipe, used, typing));

        [Fact]
        public void Core_item_browser_sections_map_to_page_sections()
        {
            Assert.Equal(PageSection.Recipe, PageModel.SectionOf(ItemPageSection.Sources));
            Assert.Equal(PageSection.UsedIn, PageModel.SectionOf(ItemPageSection.UsedIn));
        }

        [Fact]
        public void Used_in_key_scrolls_to_the_used_in_header()
        {
            Assert.Equal(0, PageModel.SectionScroll(PageSection.Recipe, 4, 6));
            Assert.Equal(4, PageModel.SectionScroll(PageSection.UsedIn, 4, 6));
            Assert.Equal(2, PageModel.SectionScroll(PageSection.UsedIn, 4, 2)); // clamped: the tail already fits
            Assert.Equal(0, PageModel.SectionScroll(PageSection.UsedIn, -1, 2));
        }

        [Theory]
        [InlineData(0, 1, 1)]
        [InlineData(1, 2, 2)]
        [InlineData(2, 4, 5)]  // Recipe.GetRequiredStationLevel: max(1, min) + quality - 1
        public void Upgrade_station_level_matches_vanilla(int min, int quality, int want) =>
            Assert.Equal(want, PageModel.StationLevel(min, quality));

        [Fact]
        public void Upgrade_rows_carry_their_quality_and_station_level()
        {
            var ix = new BrowserIndex();
            ix.Add(Craft("AxeAncient", 1, "Wood"));
            ix.Add(Craft("AxeAncient", 2, "Wood"));
            ix.Add(Craft("AxeAncient", 3, "Wood"));
            var ups = PageModel.UsedIn(ix, "AxeAncient").Where(r => r.R.Kind == RelationKind.Upgrade).ToList();
            Assert.Equal(new[] { 2, 3 }, ups.Select(r => r.Quality));           // the RowQuality square's number
            Assert.Equal(new[] { 2, 3 }, ups.Select(r => r.R.StationLevel));
            Assert.Equal(new[] { 1, 2 }, ups.Select(r => r.BaseQuality));       // the base item one quality lower
        }

        [Theory]
        [InlineData("ashland_pot1_green", "Ashlands pot (green)")]
        [InlineData("CastleKit_brazier", "Castle brazier")]
        [InlineData("Pickable_Stone", "Pickable stone")]
        [InlineData("vfx_Destroyed(Clone)", "Destroyed")]
        public void Unnamed_sources_get_readable_names(string id, string want) => Assert.Equal(want, SourceNames.Readable(id));

        [Theory]
        [InlineData(1, 3, 1, 2)]  // CharacterDrop: int Random.Range(min, max), max exclusive
        [InlineData(1, 2, 1, 1)]
        [InlineData(2, 2, 2, 2)]  // equal: Random.Range returns min
        [InlineData(1, 1, 1, 1)]
        public void Creature_drop_range_matches_vanilla(int min, int max, int wantMin, int wantMax) =>
            Assert.Equal((wantMin, wantMax), PageModel.DropRange(min, max));

        [Fact]
        public void Same_name_same_drop_sources_are_one_row()
        {
            var ix = new BrowserIndex();
            ix.Add(new Entry { Id = "Bjorn", Name = "$enemy_bjorn", Type = EntryType.Creature });
            ix.Add(new Entry { Id = "Bjorn_sleeping", Name = "$enemy_bjorn", Type = EntryType.Creature });
            ix.Add(Drop("Bjorn", "TrophyBjorn"));
            ix.Add(Drop("Bjorn_sleeping", "TrophyBjorn"));
            var row = Assert.Single(PageModel.Obtain(ix, "TrophyBjorn", 1));
            Assert.Null(row.Tag);
            Assert.Equal("Bjorn", row.R.Source);
        }

        [Fact]
        public void Same_name_other_drop_sources_get_a_tag()
        {
            var ix = new BrowserIndex();
            ix.Add(new Entry { Id = "Bjorn", Name = "$enemy_bjorn", Type = EntryType.Creature });
            ix.Add(new Entry { Id = "Bjorn_sleeping", Name = "$enemy_bjorn", Type = EntryType.Creature });
            ix.Add(Drop("Bjorn", "TrophyBjorn", 0.5f));
            ix.Add(Drop("Bjorn_sleeping", "TrophyBjorn", 0.1f));
            var rows = PageModel.Obtain(ix, "TrophyBjorn", 1);
            Assert.Equal(new[] { "Bjorn", "Bjorn sleeping" }, rows.Select(r => r.Tag));
            Assert.Equal("Bear (Bjorn sleeping)", PageModel.SourceLabel(rows[1], "Bear", "Grows from:"));
        }

        [Fact]
        public void Grow_source_row_says_grows_from()
        {
            var ix = new BrowserIndex();
            ix.Add(new Entry { Id = "Lox_Calf", Name = "$enemy_loxcalf", Type = EntryType.Creature });
            ix.Add(new Relation { Kind = RelationKind.Grow, Source = "Lox_Calf", Outputs = new[] { new Stack("Lox") } });
            var row = Assert.Single(PageModel.Obtain(ix, "Lox", 1));
            Assert.Equal("Grows from: Lox calf", PageModel.SourceLabel(row, "Lox calf", "Grows from:"));
        }

        [Fact]
        public void Epic_loot_tables_get_readable_names_and_loot_pages()
        {
            var ix = new BrowserIndex();
            ix.Add(new Entry { Id = "Troll", Name = "$enemy_troll", Type = EntryType.Creature });
            ix.Add(new Relation { Kind = RelationKind.LootTable, Source = "EL:loottable:Troll", Outputs = new[] { new Stack("Tin", 1, 1, float.NaN) } });
            ix.Add(new Relation { Kind = RelationKind.LootTable, Source = "EL:loottable:Unknown_thing", Outputs = new[] { new Stack("Tin", 1, 1, float.NaN) } });
            ix.Add(new Relation { Kind = RelationKind.LootTable, Source = "EL:itemset:ShardUnique", Outputs = new[] { new Stack("Tin", 1, 1, float.NaN) } });
            Assert.Equal("$ib_el_loottable $enemy_troll", ix.Entries["EL:loottable:Troll"].Name);
            Assert.Equal("$ib_el_loottable Unknown thing", ix.Entries["EL:loottable:Unknown_thing"].Name);
            Assert.Equal("$ib_el_itemset $ib_el_set_shardunique", ix.Entries["EL:itemset:ShardUnique"].Name);
            Assert.Equal("$ib_el_itemset $ib_el_set_weapons, $ib_el_tier 2", SourceNames.EpicLootName("EL:itemset:Tier2weapons", _ => null));
            Assert.Equal("$ib_el_itemset Other mod set 3", SourceNames.EpicLootName("EL:itemset:OtherModSet3", _ => null));
            Assert.Equal("Troll", SourceNames.LootTableOwner("EL:loottable:Troll"));
            Assert.Null(SourceNames.LootTableOwner("EL:itemset:ShardUnique"));
            Assert.True(PageModel.LootPage(EntryType.Other, "EL:loottable:Troll"));
            Assert.True(PageModel.LootPage(EntryType.Creature, "Troll"));
            Assert.False(PageModel.LootPage(EntryType.Item, "Tin"));
        }

        // Tame badge spec: tame line only on tameable creatures, breed line under it, summon line alone (no breed), blank
        // line after a description.
        [Theory]
        [InlineData("$enemy_boar_desc", TameKind.Tame, true, "$enemy_boar_desc\n\n$ib_tameable\n$ib_breedable")]
        [InlineData("$enemy_wolf_desc", TameKind.Tame, false, "$enemy_wolf_desc\n\n$ib_tameable")]
        [InlineData("", TameKind.Tame, false, "$ib_tameable")]
        [InlineData("$enemy_greydwarf_desc", TameKind.None, false, "$enemy_greydwarf_desc")]
        [InlineData("$enemy_skeleton_summoned", TameKind.Summon, true, "$enemy_skeleton_summoned\n\n$ib_summonable")]
        public void CreatureTipAddsTameLines(string desc, TameKind kind, bool breed, string want) =>
            Assert.Equal(want, PageModel.CreatureTip(desc, kind, breed));

        // Eligibility (vanilla taming path, Tameable.cs:409 / MonsterAI.cs:642): Boar (feeds) = Tame; Tar golem /
        // Skeleton_Friendly (Tameable, nothing to feed, summon mark) = Summon; Hen (startsTamed, feeds, no summon mark;
        // run 20261110-175238 showed it blue; domestic) = Tame; startsTamed summon with food = Summon; Tameable with no
        // feed and no summon mark = None; no Tameable = None.
        [Theory]
        [InlineData(true, false, true, false, TameKind.Tame)]
        [InlineData(true, false, true, true, TameKind.Tame)]
        [InlineData(true, false, false, true, TameKind.Summon)]
        [InlineData(true, true, true, false, TameKind.Tame)]
        [InlineData(true, true, true, true, TameKind.Summon)]
        [InlineData(true, true, false, true, TameKind.Summon)]
        [InlineData(true, true, false, false, TameKind.None)]
        [InlineData(true, false, false, false, TameKind.None)]
        [InlineData(false, false, true, true, TameKind.None)]
        public void KindFollowsVanillaTamingPath(bool tameable, bool startsTamed, bool feeds, bool summonMark, TameKind want) =>
            Assert.Equal(want, PageModel.Kind(tameable, startsTamed, feeds, summonMark));

        // Shipped Epic Loot sets (baseconfig loottables.json ItemSets + patch MT_BossGems): every name is tokens only, each in
        // both translation files (screenshot 2026-11-08: "Набор EpicLoot: Tier 1 mats").
        [Theory]
        [InlineData("English")]
        [InlineData("Russian")]
        public void Epic_loot_set_names_are_translated(string language)
        {
            using var stream = typeof(ItemBrowserTests).Assembly.GetManifestResourceStream($"Morgott.UI.Translations.{language}.json");
            var words = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, string>>(stream);
            var sets = "EnchantingMats Tier0Mats Tier5Mats EnchantingRunestones Tier3Runestone ModUtility ShardUnique ShardSlotChisel "
                       + "Tier0Weapons Tier1Tools Tier2Armor Tier4Shields Tier5Trinkets Tier6Utility Tier8Everything MT_BossGems";
            foreach (var set in sets.Split(' '))
                foreach (var word in SourceNames.SetName(set).Split(' ', ','))
                    if (word.Length > 0 && !char.IsDigit(word[0]))
                        Assert.True(word[0] == '$' && words.ContainsKey(word.Substring(1)), $"{set}: '{word}' untranslated in {language}.json");
        }

        // Run 20261108-193552: "[enemy_deerwhite]", DvergerTest, "EL:loottable:Tier3Mob" without a name / icon.
        [Fact]
        public void Translated_token_beats_a_literal_container_name()
        {
            string Loc(string t) => t == "$piece_chest" ? "Сундук" : t == "$enemy_hive" ? "[enemy_hive]" : t;
            Assert.Equal("$piece_chest", SourceNames.BestName("Chest", new[] { "Chest", "$piece_chest" }, Loc, "Readable"));
            Assert.Equal("Chest", SourceNames.BestName("Chest", new string[] { null, "Chest" }, Loc, "Readable"));       // no token: the literal stays
            Assert.Equal("Hive", SourceNames.BestName("$enemy_hive", new[] { "$enemy_hive", "Hive" }, Loc, "Readable")); // untranslated token
            Assert.Equal("Readable", SourceNames.BestName("$enemy_hive", new[] { "$enemy_hive" }, Loc, "Readable"));
            Assert.Equal("$piece_chest", SourceNames.BestName("$piece_chest", new[] { "Chest" }, Loc, "Readable"));
        }

        [Fact]
        public void Untranslated_tokens_dev_prefabs_and_tier_mob_tables()
        {
            string Loc(string t) => t == "$enemy_hive" ? "[enemy_hive]" : t == "$enemy_troll" ? "Troll" : t;
            Assert.False(SourceNames.Translated("$enemy_hive", Loc));
            Assert.True(SourceNames.Translated("$enemy_troll", Loc));
            Assert.True(SourceNames.Translated("Plain mod name", Loc));
            Assert.False(SourceNames.Translated(null, Loc));
            Assert.True(SourceNames.DevPrefab("DvergerTest"));
            Assert.True(SourceNames.DevPrefab("Rock_destructible_test"));
            Assert.False(SourceNames.DevPrefab("Troll"));
            Assert.True(SourceNames.TemplatePrefab("_ShardStone"));
            Assert.True(SourceNames.TemplatePrefab("JVLmock_Wood"));
            Assert.False(SourceNames.TemplatePrefab("1013_Mythic_ShardStone"));
            Assert.False(SourceNames.TemplatePrefab(null));
            Assert.Equal("$ib_el_loottable $ib_el_set_elitemob, $ib_el_tier 3", SourceNames.EpicLootName("EL:loottable:Tier3EliteMob", _ => null));
            Assert.Equal("$ib_el_loottable $ib_el_set_mob, $ib_el_tier 0", SourceNames.EpicLootName("EL:loottable:Tier0Mob", _ => null));
        }

        [Fact]
        public void Provider_source_name_and_icon_win()
        {
            var ix = new BrowserIndex();
            ix.Add(new Relation { Kind = RelationKind.LootTable, Source = "CR:treasure:epicloot:Rare", SourceName = "$cr_cond_treasure", SourceIcon = "Coins", Outputs = new[] { new Stack("EssenceRare", 1, 2) } });
            ix.Add(new Relation { Kind = RelationKind.LootTable, Source = "CR:treasure:epicloot:Rare", SourceName = "$other", Outputs = new[] { new Stack("DustRare", 1, 2) } });
            Assert.Equal("$cr_cond_treasure", ix.Entries["CR:treasure:epicloot:Rare"].Name);
            Assert.Equal("Coins", ix.Entries["CR:treasure:epicloot:Rare"].Icon);
        }
    }
}
