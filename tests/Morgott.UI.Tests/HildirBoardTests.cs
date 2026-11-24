using System.Collections.Generic;
using Morgott.Core;
using Morgott.UI.GameScreens;
using Xunit;

namespace Morgott.UI.Tests
{
    // Hildir's contract board (GameScreens\HildirBoard, mockup 2026-11-24 hildir-bounty-board): game-free tab logic.
    public class HildirBoardTests
    {
        private static readonly UniqueBoss Brenna = new UniqueBoss { Prefab = "Skeleton_Hildir_nochest", Biome = 8, NameToken = "$enemy_skeletonhildir", UnlockKey = "hildir1" };
        private static readonly UniqueBoss Geirrhafa = new UniqueBoss { Prefab = "Fenring_Cultist_Hildir_nochest", Biome = 4, NameToken = "$enemy_fenringcultist_hildir", UnlockKey = "hildir2" };
        private static readonly UniqueBoss Bros = new UniqueBoss { Prefab = "GoblinBruteBros_nochest", Biome = 16, NameToken = "$enemy_goblinbrute_hildircombined", UnlockKey = "hildir3" };
        private static readonly UniqueBoss[] Bosses = { Brenna, Geirrhafa, Bros };

        private static HildirContract C(UniqueBoss b, HildirContractState s, string id) =>
            new HildirContract { Id = id, Prefab = b.Prefab, NameToken = b.NameToken, Biome = b.Biome, State = s, RewardCoins = 300, RewardGold = 2 };

        private static List<HildirRow>[] Tabs() => new[] { new List<HildirRow>(), new List<HildirRow>(), new List<HildirRow>() };

        [Fact]
        public void OffersFirstThenLockedBosses()
        {
            var tabs = Tabs();
            var keys = new HashSet<string> { "hildir1" };
            HildirBoardMath.Partition(new[] { C(Brenna, HildirContractState.Available, "a") }, new HildirContract[0], Bosses, keys.Contains, tabs);
            Assert.Equal(3, tabs[0].Count);
            Assert.Equal(HildirChip.Open, tabs[0][0].Chip);
            Assert.Equal("a", tabs[0][0].Id);
            Assert.Equal(300, tabs[0][0].Coins);
            Assert.Equal(2, tabs[0][0].Gold);
            Assert.Equal("hildir1", tabs[0][0].UnlockKey);
            Assert.All(tabs[0].GetRange(1, 2), r => Assert.Equal(HildirChip.Locked, r.Chip));
            Assert.Null(tabs[0][1].Id);
            Assert.Equal(0, tabs[0][1].Coins);
            Assert.Empty(tabs[1]);
            Assert.Empty(tabs[2]);
        }

        // Key set, no offer (accepted this refresh) and not running: not listed anywhere.
        [Fact]
        public void UnlockedBossWithoutOfferIsNotListed()
        {
            var tabs = Tabs();
            HildirBoardMath.Partition(new HildirContract[0], new HildirContract[0], Bosses, k => k == "hildir2", tabs);
            Assert.Equal(2, tabs[0].Count);
            Assert.DoesNotContain(tabs[0], r => r.Prefab == Geirrhafa.Prefab);
        }

        [Fact]
        public void ActiveSplitsIntoTakenAndDone()
        {
            var tabs = Tabs();
            var active = new[] { C(Brenna, HildirContractState.InProgress, "b"), C(Bros, HildirContractState.Complete, "c") };
            HildirBoardMath.Partition(null, active, Bosses, _ => true, tabs);
            Assert.Empty(tabs[0]);
            Assert.Equal(HildirChip.Taken, Assert.Single(tabs[1]).Chip);
            Assert.Equal(HildirChip.Done, Assert.Single(tabs[2]).Chip);
        }

        // A running contract of a boss whose key is unset (or no catalog key) is not doubled as a locked row.
        [Fact]
        public void RunningBossIsNotAlsoLocked()
        {
            var tabs = Tabs();
            HildirBoardMath.Partition(null, new[] { C(Brenna, HildirContractState.InProgress, "b") }, new[] { Brenna, new UniqueBoss { Prefab = "Free" } }, _ => false, tabs);
            Assert.Empty(tabs[0]);
            Assert.Single(tabs[1]);
        }

        [Theory]
        [InlineData(HildirChip.Open, true, false, false)]
        [InlineData(HildirChip.Locked, false, false, false)]
        [InlineData(HildirChip.Taken, false, false, true)]
        [InlineData(HildirChip.Done, false, true, false)]
        public void ButtonsFollowTheChip(HildirChip chip, bool accept, bool claim, bool abandon)
        {
            HildirBoardMath.Buttons(new HildirRow { Chip = chip }, out var a, out var c, out var x);
            Assert.Equal((accept, claim, abandon), (a, c, x));
        }

        [Fact]
        public void NoSelectionDisablesEveryButton()
        {
            HildirBoardMath.Buttons(null, out var a, out var c, out var x);
            Assert.False(a || c || x);
        }

        [Fact]
        public void SelectKeepsThePrefabElseTheFirstRow()
        {
            var rows = new List<HildirRow> { new HildirRow { Prefab = "A" }, new HildirRow { Prefab = "B" } };
            Assert.Equal(1, HildirBoardMath.Select(rows, "B"));
            Assert.Equal(0, HildirBoardMath.Select(rows, "gone"));
            Assert.Equal(-1, HildirBoardMath.Select(new List<HildirRow>(), "A"));
        }

        [Theory]
        [InlineData("Skeleton_Hildir_nochest", "skeleton_hildir")]
        [InlineData("Fenring_Cultist_Hildir_nochest", "fenring_cultist_hildir")]
        [InlineData("GoblinBruteBros_nochest", "goblinbrutebros")]
        [InlineData("Troll", "troll")]
        public void TextKeyDropsTheNoChestSuffix(string prefab, string key) => Assert.Equal(key, HildirBoardMath.TextKey(prefab));

        [Fact]
        public void TrophyLineKeepsOneLabel()
        {
            Assert.Equal("Трофей: Бренна", HildirBoardMath.LabelLine("Трофей", "Трофей: Бренна"));
            Assert.Equal("Trophy: Brenna's skull", HildirBoardMath.LabelLine("Trophy", "Brenna's skull"));
        }

        [Fact]
        public void BiomeTokensMatchVanillaFlags()
        {
            Assert.Equal("$biome_blackforest", HildirBoardMath.BiomeToken((int)Heightmap.Biome.BlackForest));
            Assert.Equal("$biome_mountain", HildirBoardMath.BiomeToken((int)Heightmap.Biome.Mountain));
            Assert.Equal("$biome_plains", HildirBoardMath.BiomeToken((int)Heightmap.Biome.Plains));
            Assert.Equal("$biome_mistlands", HildirBoardMath.BiomeToken((int)Heightmap.Biome.Mistlands));
            Assert.Equal("", HildirBoardMath.BiomeToken(0));
        }

        [Fact]
        public void MissingTranslationIsBracketed()
        {
            Assert.True(HildirBoardMath.Missing("[ui_hildir_sub_troll]"));
            Assert.False(HildirBoardMath.Missing("Пылающий скелет"));
        }

        [Fact]
        public void RefreshGateReadsOncePerPeriodPlusOneForcedReadPerAction()
        {
            var g = new HildirRefreshGate(1f);
            Assert.True(g.Take(0f));
            Assert.False(g.Take(0.1f));
            Assert.False(g.Take(0.9f));
            g.Force();
            g.Force(); // coalesced
            Assert.True(g.Take(0.95f));
            Assert.False(g.Take(0.96f));
            Assert.False(g.Take(1.9f));
            Assert.True(g.Take(1.95f));
        }

        [Fact]
        public void ButtonLabelKeepsOneLineSplitsTwoWordsShrinksOneWord()
        {
            static float W(int i, int j) => (j - i) * 10f; // 10 px per char at the full size
            Assert.Equal("ВЗЯТЬ ЗАКАЗ", HildirBoardMath.ButtonLabel("ВЗЯТЬ ЗАКАЗ", W, 120f, 24f, out var s1));
            Assert.Equal(24f, s1);
            Assert.Equal("ПОЛУЧИТЬ\nНАГРАДУ", HildirBoardMath.ButtonLabel("ПОЛУЧИТЬ НАГРАДУ", W, 100f, 24f, out var s2));
            Assert.Equal(24f, s2); // widest line 80 <= 100
            Assert.Equal("ОТКАЗАТЬСЯ", HildirBoardMath.ButtonLabel("ОТКАЗАТЬСЯ", W, 80f, 24f, out var s3));
            Assert.Equal(19.2f, s3, 3);
        }
    }
}
