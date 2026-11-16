using System.Collections.Generic;
using System.Linq;
using Morgott.UI.ItemBrowser;
using Xunit;

namespace Morgott.UI.Tests
{
    // STORAGE tab rules: take amount bounds, Russian plural forms, grid filter.
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

        [Fact]
        public void CountGroupsThousands()
        {
            Assert.Equal("1 240", StorageMath.Count(1240));
            Assert.Equal("87", StorageMath.Count(87));
        }

        [Fact]
        public void FilterKeepsStockedItemsOfTheCategory()
        {
            var ix = new BrowserIndex();
            ix.Add(new Entry { Id = "Wood", Name = "Wood", Type = EntryType.Item, Category = ItemCategory.Materials });
            ix.Add(new Entry { Id = "Stone", Name = "Stone", Type = EntryType.Item, Category = ItemCategory.Materials });
            ix.Add(new Entry { Id = "Raspberry", Name = "Raspberries", Type = EntryType.Item, Category = ItemCategory.Food });
            ix.Add(new Entry { Id = "Flint", Name = "Flint", Type = EntryType.Item, Category = ItemCategory.Materials });
            ix.Add(new Entry { Id = "forge", Name = "Forge", Type = EntryType.Station });
            var stock = new Dictionary<string, int> { ["Wood"] = 1240, ["Raspberry"] = 38, ["Flint"] = 0, ["forge"] = 1 };
            List<string> Ids(string q, int cat) => ix.Search(q, EntryType.Item, StorageMath.Filter(stock, cat), s => s, _ => null).Select(e => e.Id).OrderBy(s => s).ToList();
            Assert.Equal(new[] { "Raspberry", "Wood" }, Ids("", -1)); // Stone not stored, Flint at 0, station not an item
            Assert.Equal(new[] { "Wood" }, Ids("", (int)ItemCategory.Materials));
            Assert.Equal(new[] { "Raspberry" }, Ids("rasp", -1));
        }

        [Fact]
        public void SameComparesCounts()
        {
            var a = new Dictionary<string, int> { ["Wood"] = 5 };
            Assert.True(StorageMath.Same(a, new Dictionary<string, int> { ["Wood"] = 5 }));
            Assert.False(StorageMath.Same(a, new Dictionary<string, int> { ["Wood"] = 4 }));
            Assert.False(StorageMath.Same(a, new Dictionary<string, int>()));
        }
    }
}
