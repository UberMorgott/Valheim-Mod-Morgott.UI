using System.Collections.Generic;
using System.Linq;
using Morgott.Core;
using Morgott.UI.MapScreen;
using UnityEngine;
using Xunit;

namespace Morgott.UI.Tests
{
    public class MapQuestSidebarTests
    {
        [Fact]
        public void MergesCoreQuestsWithVanillaPinsDedupesHildirAndGroupsBossesBelow()
        {
            var player = new Vector3(100f, 50f, 100f);
            var core = new List<MapQuest>
            {
                new MapQuest("t1", MapQuestKind.Treasure, "Sunken Chest", "Haldor · Swamp", new Vector3(600f, 0f, 100f), 40f),
                new MapQuest("b1", MapQuestKind.Bounty, "Brenna", "Haldor · Black Forest", new Vector3(400f, 0f, 100f), 30f),
                new MapQuest("b2", MapQuestKind.Bounty, "Troll King", "Haldor · Black Forest", new Vector3(200f, 0f, 100f), 30f),
                new MapQuest("h1", MapQuestKind.HildirBoss, "Geirrhafa", "Hildir · Mountain", new Vector3(900f, 0f, 100f), 0f),
            };
            var pins = new List<QuestPinInput>
            {
                // Within 30m of EpicLoot Hildir contract h1 -> must be deduplicated.
                new QuestPinInput(Minimap.PinType.Hildir2, "Frost Cave", new Vector3(915f, 10f, 110f)),
                // Far from h1 (> 30m) -> kept as vanilla Hildir objective.
                new QuestPinInput(Minimap.PinType.Hildir3, "Sealed Tower", new Vector3(1400f, 0f, 100f)),
                // Vanilla bosses -> grouped under Known Bosses below NPC quests.
                new QuestPinInput(Minimap.PinType.Boss, "Bonemass", new Vector3(800f, 0f, 100f)),
                new QuestPinInput(Minimap.PinType.Boss, "Eikthyr", new Vector3(250f, 0f, 100f)),
                // Non-quest pin -> ignored.
                new QuestPinInput(Minimap.PinType.Icon3, "Home [mp1:house:ECE5D8]", new Vector3(110f, 0f, 100f)),
            };

            var rows = new List<QuestRow>();
            var npcCount = MapQuestSidebarMath.Build(core, pins, player, "", rows);

            Assert.Equal(5, npcCount);
            Assert.Equal(7, rows.Count);
            // NPC quests sorted by Kind (Bounty -> HildirBoss -> Treasure), then distance ascending.
            Assert.Equal(new[] { "Troll King", "Brenna", "Geirrhafa", "Sealed Tower", "Sunken Chest" },
                rows.Take(npcCount).Select(r => r.Title));
            Assert.All(rows.Take(npcCount), r => Assert.False(r.IsBossGroup));

            // Known Bosses below NPC quests, sorted by distance ascending.
            Assert.Equal(new[] { "Eikthyr", "Bonemass" }, rows.Skip(npcCount).Select(r => r.Title));
            Assert.All(rows.Skip(npcCount), r => Assert.True(r.IsBossGroup));

            // Live XZ distances ignore Y difference.
            Assert.Equal(100f, rows[0].Distance, 1);
            Assert.Equal(300f, rows[1].Distance, 1);
            Assert.Equal(150f, rows[npcCount].Distance, 1);
        }

        [Fact]
        public void SearchMatchesTitleSubtitleAndCategoryCaseInsensitive()
        {
            var core = new List<MapQuest>
            {
                new MapQuest("b1", MapQuestKind.Bounty, "Shadow Wolf", "Haldor · Mountain", new Vector3(300f, 0f, 0f)),
                new MapQuest("t1", MapQuestKind.Treasure, "Buried Gold", "Haldor · Plains", new Vector3(500f, 0f, 0f)),
            };
            var pins = new List<QuestPinInput>
            {
                new QuestPinInput(Minimap.PinType.Boss, "Moder", new Vector3(900f, 0f, 0f)),
            };
            var rows = new List<QuestRow>();
            static string Cat(MapQuestKind k) => k == MapQuestKind.Treasure ? "Клад" : k == MapQuestKind.Bounty ? "Охота" : "Босс";

            var npc = MapQuestSidebarMath.Build(core, pins, Vector3.zero, "plains", rows, Cat);
            Assert.Equal(1, npc);
            Assert.Equal("Buried Gold", Assert.Single(rows).Title);

            npc = MapQuestSidebarMath.Build(core, pins, Vector3.zero, "  КЛАД ", rows, Cat);
            Assert.Equal(1, npc);
            Assert.Equal("Buried Gold", Assert.Single(rows).Title);

            npc = MapQuestSidebarMath.Build(core, pins, Vector3.zero, "mod", rows, Cat);
            Assert.Equal(0, npc);
            Assert.Equal("Moder", Assert.Single(rows).Title);
        }

        [Fact]
        public void SkipsCompletedVanillaHildirDungeonsViaGlobalKeysWithoutHidingEpicLootContracts()
        {
            var completedKeys = new HashSet<string> { "hildir1", "hildir3" };
            var core = new List<MapQuest>
            {
                // Active EpicLoot Hildir contract must remain visible even when vanilla hildir1 global key is set.
                new MapQuest("el-h1", MapQuestKind.HildirBoss, "Brenna (Contract)", "Hildir · Black Forest", new Vector3(500f, 0f, 0f)),
            };
            var pins = new List<QuestPinInput>
            {
                new QuestPinInput(Minimap.PinType.Hildir1, "Smouldering Tomb", new Vector3(1200f, 0f, 0f)), // completed -> skip
                new QuestPinInput(Minimap.PinType.Hildir2, "Howling Cavern", new Vector3(1600f, 0f, 0f)),   // incomplete -> keep
                new QuestPinInput(Minimap.PinType.Hildir3, "Sealed Tower", new Vector3(2000f, 0f, 0f)),     // completed -> skip
            };
            var rows = new List<QuestRow>();
            var npc = MapQuestSidebarMath.Build(core, pins, Vector3.zero, "", rows, hasGlobalKey: k => completedKeys.Contains(k));

            Assert.Equal(2, npc);
            Assert.Equal(new[] { "Brenna (Contract)", "Howling Cavern" }, rows.Select(r => r.Title));
            Assert.Equal("hildir1", MapQuestSidebarMath.HildirGlobalKey(Minimap.PinType.Hildir1));
            Assert.Equal("hildir2", MapQuestSidebarMath.HildirGlobalKey(Minimap.PinType.Hildir2));
            Assert.Equal("hildir3", MapQuestSidebarMath.HildirGlobalKey(Minimap.PinType.Hildir3));
            Assert.Null(MapQuestSidebarMath.HildirGlobalKey(Minimap.PinType.Boss));
        }

        [Fact]
        public void QuestRowLayoutGivesTitleAndSubtitleRoomAboveFontLineHeight()
        {
            // Default theme tokens: size.map.legend.font = 20, size.map.biome.font = 18.
            var lay = MapQuestSidebarMath.ComputeRowLayout(20f, 18f);
            Assert.True(lay.TitleH >= 26f && lay.TitleH >= lay.TitleFont * 1.4f, $"TitleH={lay.TitleH}, TitleFont={lay.TitleFont}");
            Assert.True(lay.SubH >= 22f && lay.SubH >= lay.SubFont * 1.4f, $"SubH={lay.SubH}, SubFont={lay.SubFont}");
            Assert.True(lay.BadgeH >= 20f && lay.BadgeH >= lay.BadgeFont * 1.4f, $"BadgeH={lay.BadgeH}, BadgeFont={lay.BadgeFont}");
            Assert.True(lay.BadgeY + lay.BadgeH <= lay.TitleY);
            Assert.True(lay.TitleY + lay.TitleH <= lay.SubY);
            Assert.True(lay.SubY + lay.SubH <= lay.CardHeight);
            Assert.True(lay.CardHeight < lay.RowHeight);
        }
    }
}
