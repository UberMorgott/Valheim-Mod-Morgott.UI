using Morgott.UI.CraftingScreen;
using Xunit;
using Stock = Morgott.UI.CraftingScreen.CraftingMath.Stock;

namespace Morgott.UI.Tests
{
    // CraftingMath.Stock: the one craftability rule of recipe rows, requirement cells and the Create button.
    public class CraftStockTests
    {
        [Theory]
        [InlineData(true, false, Stock.Have)]
        [InlineData(true, true, Stock.Have)]     // enough now wins over a chain
        [InlineData(false, true, Stock.Chain)]
        [InlineData(false, false, Stock.Missing)]
        public void StockOf(bool enough, bool chain, Stock expected) => Assert.Equal(expected, CraftingMath.StockOf(enough, chain));

        [Theory]
        [InlineData(Stock.Have, "color.text.ok", "craft.socket")]
        [InlineData(Stock.Chain, "color.text.chain", "craft.socket")]
        [InlineData(Stock.Missing, "color.text.unavailable", "craft.socket.missing")]
        public void CellLook(Stock s, string text, string socket)
        {
            Assert.Equal(text, CraftingMath.CellTextColor(s));
            Assert.Equal(socket, CraftingMath.SocketRole(s));
        }

        // A chain-craftable row is bright like a directly craftable one: only Missing gets the unavailable strip.
        [Theory]
        [InlineData(Stock.Have, "craft.row")]
        [InlineData(Stock.Chain, "craft.row")]
        [InlineData(Stock.Missing, "craft.row.unavailable")]
        public void RowStrip(Stock s, string role) =>
            Assert.Equal(role, CraftingMath.RowImageRole("bkg", true, false, s != Stock.Missing));

        [Theory]
        [InlineData(0, 1, 0)]
        [InlineData(-3, 1, 0)]
        [InlineData(1, 1, 1)]
        [InlineData(5, 2, 3)]   // two per craft: 3 crafts for 5
        [InlineData(4, 2, 2)]
        [InlineData(3, 0, 3)]   // a bad m_amount counts as one per craft
        public void CraftsFor(int shortBy, int perCraft, int crafts) => Assert.Equal(crafts, CraftingMath.CraftsFor(shortBy, perCraft));

        [Fact]
        public void ApplyKeyIgnoresStockEpochAndInvalidatesOnRealPanelChanges()
        {
            var baseState = new CraftingMath.ApplyState
            {
                LayoutHash = 1001,
                WindowMoveRevision = 1,
                RecipeId = 42,
                ItemKey = 0,
                Max = 99,
                Want = 1,
                Flags = 4 | 64,
                QueueKey = 0,
                TabSelMask = 1,
                RowCount = 24,
                FirstRowId = 777,
                StationId = 12,
                StationDetached = 0,
                LevelShown = 1,
                LevelTextHash = 5,
                ViewKey = 256,
                PanelsMask = 3,
                DescChanged = false,
                LanguageHash = 10,
                ThemeRevision = 3,
            };

            // Identical panel state (as when nearby chest/drawer Inventory.Changed bumps stock epoch without changing layout/max) keeps ApplyKey stable.
            var same = baseState;
            Assert.Equal(CraftingMath.ApplyKey(in baseState), CraftingMath.ApplyKey(in same));
            Assert.Null(typeof(CraftingMath.ApplyState).GetField("StockEpoch"));

            var baseline = CraftingMath.ApplyKey(in baseState);
            void AssertDiffers(CraftingMath.ApplyState next, string label) =>
                Assert.True(CraftingMath.ApplyKey(in next) != baseline, label);

            var layout = baseState; layout.LayoutHash = 1002; AssertDiffers(layout, "layout");
            var recipe = baseState; recipe.RecipeId = 43; AssertDiffers(recipe, "recipe");
            var upgrade = baseState; upgrade.ItemKey = 99; AssertDiffers(upgrade, "item/quality");
            var max = baseState; max.Max = 10; AssertDiffers(max, "max crafts");
            var want = baseState; want.Want = 2; AssertDiffers(want, "stepper want");
            var tab = baseState; tab.TabSelMask = 2; AssertDiffers(tab, "tab selection");
            var craftable = baseState; craftable.Flags = 4; AssertDiffers(craftable, "craft button interactable");
            var rows = baseState; rows.RowCount = 25; AssertDiffers(rows, "row count");
            var station = baseState; station.StationDetached = 1; AssertDiffers(station, "station network");
            var desc = baseState; desc.DescChanged = true; AssertDiffers(desc, "description");
            var theme = baseState; theme.ThemeRevision = 4; AssertDiffers(theme, "theme");
        }

        [Theory]
        [InlineData(16f, 34f, 61.2f, 9f, 16f)]              // normal "42/40" stays at baseSize
        [InlineData(16f, 61.2f, 61.2f, 9f, 16f)]            // exact fit stays at baseSize
        [InlineData(16f, 78f, 61.2f, 9f, 12.553846f)]       // long "20000/100" shrinks to fit maxWidth
        [InlineData(16f, 160f, 61.2f, 9f, 9f)]              // extreme counter clamps at min floor
        [InlineData(16f, 0f, 61.2f, 9f, 16f)]               // zero preferred width keeps baseSize
        [InlineData(16f, 78f, 0f, 9f, 16f)]                 // unlaid-out socket keeps baseSize
        [InlineData(12f, 60f, 45f, 6.75f, 9f)]              // scaled panel shrinks proportionally
        public void AmountFontSizeFitsLongCountersWithFloor(float baseSize, float preferredWidth, float maxWidth, float minSize, float expected)
        {
            var actual = CraftingMath.AmountFontSize(baseSize, preferredWidth, maxWidth, minSize);
            Assert.Equal(expected, actual, 3);
            Assert.True(actual <= baseSize + 0.001f);
            if (preferredWidth > maxWidth && maxWidth > 0f)
                Assert.True(actual >= minSize - 0.001f);
        }
    }
}
