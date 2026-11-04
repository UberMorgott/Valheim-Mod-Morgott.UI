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
            Assert.True(ix.Add(Drop("Seagal", "HC_WildEgg", 0.5f, "$ib_cond_perstar")));  // other conditions
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
            Assert.Equal("no icon", ix.Add(new Entry { Id = "TorchMist", Name = "$item_torch", Type = EntryType.Item, Hidden = "no icon" }).Hidden); // first reason kept
            Assert.Null(ix.Add(new Entry { Id = "CapeLinen", Name = "$item_cape_linen", Type = EntryType.Item }).Hidden);
            Assert.Equal(new[] { "CapeLinen" }, ix.Search("cape", null, null, Loc, Mod).Select(e => e.Id));
            Assert.Equal(new[] { "CapeLinen", "CapeTest" }, ix.Search("cape", null, null, Loc, Mod, unlisted: true).Select(e => e.Id).OrderBy(i => i));
            Assert.Empty(ix.Search("torch", null, null, Loc, Mod, unlisted: true)); // other hide reasons stay hidden
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

        [Theory]
        [InlineData("ashland_pot1_green", "Ashlands pot (green)")]
        [InlineData("CastleKit_brazier", "Castle brazier")]
        [InlineData("Pickable_Stone", "Pickable stone")]
        [InlineData("vfx_Destroyed(Clone)", "Destroyed")]
        public void Unnamed_sources_get_readable_names(string id, string want) => Assert.Equal(want, SourceNames.Readable(id));
    }
}
