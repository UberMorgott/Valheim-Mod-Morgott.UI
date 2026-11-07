using Morgott.UI.InventoryScreen;
using Morgott.UI.ModScreens;
using Xunit;

namespace Morgott.UI.Tests
{
    // Full-screen mod pages (ModScreens\ElPage / TraderPage, user 2026-11-07). Canvas units at 1920x1080 (u = 1); the
    // content box of mockup 02 is x 61, y 232, 1799 x 766.
    public class ElPageTests
    {
        private static readonly Box Body = new Box(61f, 232f, 1799f, 766f);

        // Mockup 02: list column 61..574 (scrollbar included), pane from 612 to the box's right edge.
        [Fact]
        public void SplitMatchesMockup02()
        {
            ElPageMath.Split(Body, true, out var col, out var pane);
            Assert.Equal(61f, col.X, 2);
            Assert.Equal(574f, col.Right, 1);
            Assert.Equal(612f, pane.X, 1);
            Assert.Equal(Body.Right, pane.Right, 2);
            Assert.Equal(Body.H, pane.H, 2);
        }

        [Fact]
        public void NoListGivesThePaneTheWholeBody()
        {
            ElPageMath.Split(Body, false, out var col, out var pane);
            Assert.True(col.IsEmpty);
            Assert.Equal(Body.W, pane.W, 2);
        }

        // A 1000 x 500 group into a 1200 x 700 area: width-bound (1.2), top-aligned, centred.
        [Fact]
        public void FitTakesTheTighterAxis()
        {
            var k = ElPageMath.Fit(1000f, 500f, new Box(100f, 50f, 1200f, 700f), out var x0, out var y0);
            Assert.Equal(1.2f, k, 4);
            Assert.Equal(100f, x0, 2);
            Assert.Equal(50f, y0, 2);
            k = ElPageMath.Fit(400f, 700f, new Box(100f, 50f, 1200f, 700f), out x0, out _);
            Assert.Equal(1f, k, 4);
            Assert.Equal(100f + 400f, x0, 2);
        }

        // Mockup 02 tab caps: 85 x 66 from x 109, pitch 104; 8 Epic Loot tabs end left of the name banner (x 949).
        [Fact]
        public void TabRowFitsLeftOfTheBanner()
        {
            Assert.Equal(213f, ElPageMath.Tab(1, 1f).X, 2);
            Assert.True(ElPageMath.TabRow(8, 1f).Right < 949f);
        }

        // Mockup 04: store column ~30 % left, board right; Bog Witch (no board): the store centred.
        [Fact]
        public void TraderSplitsOrCentres()
        {
            ElPageMath.Trader(Body, true, out var store, out var right);
            Assert.Equal(Body.X, store.X, 2);
            Assert.True(right.X > store.Right && System.Math.Abs(right.Right - Body.Right) < 0.01f);
            ElPageMath.Trader(Body, false, out store, out right);
            Assert.Equal(Body.X + Body.W / 2f, store.X + store.W / 2f, 2);
            Assert.True(right.IsEmpty);
        }

        [Fact]
        public void GridCellsTileTheArea()
        {
            var a = new Box(0f, 0f, 1010f, 610f);
            var c = ElPageMath.Cell(a, 1, 1, 10f);
            Assert.Equal(510f, c.X, 2);
            Assert.Equal(310f, c.Y, 2);
            Assert.Equal(a.Right, c.Right, 2);
            Assert.Equal(a.Bottom, c.Bottom, 2);
        }
    }
}
