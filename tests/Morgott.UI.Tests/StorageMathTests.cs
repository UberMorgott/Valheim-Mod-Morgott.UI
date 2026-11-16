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
            var rows = StorageMath.Rows(stock, r => r.Prefab, new HashSet<string> { "SwordIron", "Wood" }, r => r.Name, r => r.Quality);
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
        public void TakeBarAndBadgeFollowCountAndStack(int count, int maxStack, bool single, bool badge)
        {
            Assert.Equal(single, StorageMath.Single(count));
            Assert.Equal(badge, StorageMath.Badge(count, maxStack));
        }
    }
}
