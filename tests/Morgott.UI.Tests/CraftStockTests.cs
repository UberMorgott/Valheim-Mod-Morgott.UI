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
    }
}
