using System.Collections.Generic;
using System.Linq;
using Morgott.UI.ItemBrowser;
using Xunit;

namespace Morgott.UI.Tests
{
    // STORAGE tab rules: take amount bounds, Russian plural forms, grid rows, selection by identity, take bar mode.
    public class StorageMathTests
    {
        [Theory]
        [InlineData(25, 1240, 700, 25)] // inside
        [InlineData(50, 30, 700, 30)] // stack button past the stock
        [InlineData(50, 1240, 14, 14)] // stack button past the inventory room
        [InlineData(0, 10, 10, 1)] // "-" below one
        [InlineData(11, 10, 50, 10)] // "+" past the stock
        [InlineData(5, 0, 50, 0)] // nothing in stock
        [InlineData(5, 10, 0, 0)] // inventory full
        public void AmountClamps(int amount, int stock, int room, int want) => Assert.Equal(want, StorageMath.Clamp(amount, stock, room));

        [Theory]
        [InlineData(1240, 700, 700)]
        [InlineData(12, 700, 12)]
        [InlineData(12, -3, 0)]
        public void AllIsStockOrRoom(int stock, int room, int want) => Assert.Equal(want, StorageMath.Max(stock, room));

        [Theory]
        [InlineData(1, 1)]
        [InlineData(21, 1)]
        [InlineData(101, 1)]
        [InlineData(2, 2)]
        [InlineData(3, 2)]
        [InlineData(4, 2)]
        [InlineData(22, 2)]
        [InlineData(104, 2)]
        [InlineData(0, 5)]
        [InlineData(5, 5)]
        [InlineData(11, 5)]
        [InlineData(12, 5)]
        [InlineData(14, 5)]
        [InlineData(111, 5)]
        [InlineData(112, 5)]
        [InlineData(25, 5)]
        public void RussianPlural(int n, int form) => Assert.Equal(form, StorageMath.Plural(n));

        [Theory]
        [InlineData(1, "в 1 сундуке", "in 1 chest")]
        [InlineData(2, "в 2 сундуках", "in 2 chests")]
        [InlineData(5, "в 5 сундуках", "in 5 chests")]
        [InlineData(21, "в 21 сундуке", "in 21 chests")] // RU singular form, EN plural
        [InlineData(11, "в 11 сундуках", "in 11 chests")]
        public void InChestsUsesThePrepositionalPlural(int n, string ru, string en)
        {
            string Words(string language, string token)
            {
                using var s = typeof(StorageMathTests).Assembly.GetManifestResourceStream($"Morgott.UI.Translations.{language}.json");
                return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(s)[token.TrimStart('$')];
            }
            Assert.Equal(ru, StorageMath.InChests(n, "Russian", t => Words("Russian", t)));
            Assert.Equal(en, StorageMath.InChests(n, "English", t => Words("English", t)));
        }

        [Theory]
        [InlineData(1, 1, 1)]
        [InlineData(2, 2, 5)]
        [InlineData(5, 5, 5)]
        [InlineData(21, 1, 5)]
        [InlineData(0, 5, 5)]
        public void FormFollowsTheLanguage(int n, int ru, int en)
        {
            Assert.Equal(ru, StorageMath.Form(n, "Russian"));
            Assert.Equal(en, StorageMath.Form(n, "English"));
            Assert.Equal(en, StorageMath.Form(n, "German")); // no German words: English ones, English rules
        }

        [Fact]
        public void CountGroupsThousands()
        {
            Assert.Equal("1 240", StorageMath.Count(1240));
            Assert.Equal("87", StorageMath.Count(87));
        }

        private sealed class Row
        {
            public string Prefab, Name;
            public int Quality;
            public object Key;
        }

        [Fact]
        public void RowsKeepShownPrefabsByNameThenQualityDesc()
        {
            var stock = new[]
            {
                new Row { Prefab = "SwordIron", Name = "Iron sword", Quality = 1, Key = "a" },
                new Row { Prefab = "Wood", Name = "Wood", Quality = 1, Key = "w" },
                new Row { Prefab = "SwordIron", Name = "Iron sword", Quality = 3, Key = "b" },
                new Row { Prefab = "Stone", Name = "Stone", Quality = 1, Key = "s" },
                new Row { Prefab = "SwordIron", Name = "Iron sword", Quality = 1, Key = "c" }, // same name + quality: provider order
            };
            var rows = StorageMath.Rows(stock, r => r.Prefab != "Stone", r => r.Name, r => r.Quality);
            Assert.Equal(new object[] { "b", "a", "c", "w" }, rows.Select(r => r.Key).ToArray()); // Stone not shown
        }

        [Fact]
        public void PickFindsTheSameIdentityInANewStock()
        {
            var fresh = new[] { new Row { Key = (1, "SwordIron", 2) }, new Row { Key = (2, "Wood", 1) } };
            Assert.Same(fresh[0], StorageMath.Pick(fresh, (1, "SwordIron", 2), r => r.Key)); // equal key, new object
            Assert.Null(StorageMath.Pick(fresh, (1, "SwordIron", 3), r => r.Key)); // gone
            Assert.Null(StorageMath.Pick(fresh, null, r => r.Key)); // nothing selected
        }

        [Theory]
        [InlineData(1, 1, true, false)] // one sword: only "Take", no count
        [InlineData(1, 50, true, true)] // one wood: only "Take", stackable shows its count
        [InlineData(2, 1, false, true)] // defensive: Core lists non-stackables one per entry
        [InlineData(240, 50, false, true)]
        public void TakeBarAndBadgeFollowCountAndStack(int count, int maxStack, bool singleItem, bool badge)
        {
            Assert.Equal(singleItem, StorageMath.IsSingleItem(count));
            Assert.Equal(badge, StorageMath.Badge(count, maxStack));
        }

        [Fact]
        public void SameIgnoresOrderAndSeesKeyCountChestChanges()
        {
            (object Key, int Count, int Chests)[] Stock(params (object, int, int)[] e) => e;
            bool Same((object Key, int Count, int Chests)[] a, (object Key, int Count, int Chests)[] b) =>
                StorageMath.Same(a, b, e => e.Key, e => StorageMath.Sig(e.Count, e.Chests));
            var a = Stock(("wood", 240, 3), ("sword#1", 1, 1));
            Assert.True(Same(a, Stock(("sword#1", 1, 1), ("wood", 240, 3)))); // provider order only
            Assert.False(Same(a, Stock(("wood", 239, 3), ("sword#1", 1, 1)))); // count
            Assert.False(Same(a, Stock(("wood", 240, 2), ("sword#1", 1, 1)))); // chests
            Assert.False(Same(a, Stock(("wood", 240, 3), ("sword#2", 1, 1)))); // other item
            Assert.False(Same(a, Stock(("wood", 240, 3)))); // one gone
        }

        [Fact]
        public void RoomCountsOnlyStacksTheItemWouldJoin()
        {
            var held = new[]
            {
                ("$item_wood", 1, 0, 30), // joins: 20 left
                ("$item_wood", 1, 1, 10), // other world level
                ("$item_wood", 2, 0, 10), // other quality (vanilla FindFreeStackSpace would count it)
                ("$item_wood", 1, 0, 50), // full
                ("$item_stone", 1, 0, 1), // other item
            };
            Assert.Equal(3 * 50 + 20, StorageMath.Room(3, 50, "$item_wood", 1, 0, held));
            Assert.Equal(2, StorageMath.Room(2, 1, "$item_sword", 1, 0, held)); // non-stackable: free slots only
        }
    }
}
