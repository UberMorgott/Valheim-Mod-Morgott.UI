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

        // 9 tabs compact to 78 x 66 with 10 px gap (x 109..891), keeping RTrigger (row.Right + 4 + 48 = 943) left of NameX (949).
        [Fact]
        public void NineTabsAndTriggerFitLeftOfTheBanner()
        {
            var row = ElPageMath.TabRow(9, 1f);
            Assert.Equal(109f, row.X, 2);
            Assert.Equal(891f, row.Right, 2);
            var last = ElPageMath.Tab(8, 9, 1f);
            Assert.Equal(78f, last.W, 2);
            Assert.Equal(891f, last.Right, 2);
            Assert.True(row.Right + 4f + 48f < 949f);
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

        // Content starting 95 below the panel's own top lands on the area top; the panel sits that much (scaled) above.
        [Fact]
        public void AlignContentPutsTheWidgetsOnTheAreaTop()
        {
            var k = ElPageMath.AlignContent(new Box(0f, 0f, 1000f, 700f), new Box(20f, 95f, 960f, 560f), new Box(600f, 200f, 1200f, 800f), out var x, out var y);
            Assert.Equal(1.25f, k, 4);
            Assert.Equal(200f - 95f * k, y, 2);
            Assert.Equal(600f + (1200f - 960f * k) / 2f - 20f * k, x, 2);
        }

        // Trader v2 mockup at 1920x1080: body 48..1872 x 210..1040 -> store 48..620, board 636..1872; Тайник 210..610,
        // Лотерея 636..1037 | Карты 1053..1454 | Задания 1470..1872 at 626..1040 (spec "v2 layout", ±2 px).
        [Fact]
        public void TraderBoardMatchesMockupV2()
        {
            var body = new Box(48f, 210f, 1824f, 830f);
            ElPageMath.Trader(body, true, out var store, out var right);
            Assert.Equal(620f, store.Right, 1);
            Assert.Equal(636f, right.X, 1);
            ElPageMath.Trader(body, true, out var hStore, out var hRight, ElPageMath.HildirStoreShare);
            Assert.Equal(560f, hStore.Right, 1);
            Assert.Equal(576f, hRight.X, 1);
            var stash = ElPageMath.BoardCell(right, 0, 16f);
            Assert.Equal(610f, stash.Bottom, 1);
            Assert.Equal(1872f, stash.Right, 1);
            var xs = new[] { 636f, 1053f, 1470f };
            for (var i = 1; i <= 3; i++)
            {
                var c = ElPageMath.BoardCell(right, i, 16f);
                Assert.True(System.Math.Abs(c.X - xs[i - 1]) <= 2f, $"cell {i} x {c.X}");
                Assert.Equal(626f, c.Y, 1);
                Assert.Equal(1040f, c.Bottom, 1);
                Assert.True(System.Math.Abs(c.W - 401f) <= 2f, $"cell {i} w {c.W}");
            }
            Assert.Equal(1872f, ElPageMath.BoardCell(right, 3, 16f).Right, 1);
        }

        // Header on top, action row at the bottom, the list between; a centred button never outgrows its row.
        [Fact]
        public void SectionStacksHeaderListAction()
        {
            ElPageMath.Section(new Box(0f, 0f, 400f, 400f), 10f, 70f, 44f, 8f, out var head, out var list, out var act);
            Assert.Equal(10f, head.Y, 2);
            Assert.Equal(380f, head.W, 2);
            Assert.Equal(390f, act.Bottom, 2);
            Assert.Equal(head.Bottom + 8f, list.Y, 2);
            Assert.Equal(act.Y - 8f, list.Bottom, 2);
            Assert.Equal(150f, ElPageMath.Centre(act, 100f).X, 2);
            Assert.Equal(act.W, ElPageMath.Centre(act, 900f).W, 2);
        }

        // Single-row top-bar currencies: mixed-height UpperLeft children (65.15 Coins + 50 icons/counts, or 50 + 60 TemperPanel)
        // share the bar's vertical centre while ElPageMath.Fit alone stays top-aligned.
        [Fact]
        public void SingleRowCentersMixedHeightChildren()
        {
            var bar = new Box(1271f, 30f, 470f, 56f);
            var barCenterY = bar.Y + bar.H / 2f;
            var area = new Box(bar.X + 8f, bar.Y + 6f, bar.W - 16f - bar.H, bar.H - 12f);
            var k = ElPageMath.Fit(460f, 65.15f, area, out _, out var y0);
            Assert.Equal(area.Y, y0, 2);
            foreach (var h in new[] { 65.15f, 50f })
            {
                var py = ElPageMath.CenterY(area, h, k);
                Assert.Equal(barCenterY, py + h * k / 2f, 2);
            }
            var temperArea = new Box(bar.X + 8f, bar.Y + 6f, bar.W - 16f, bar.H - 12f);
            var kw = ElPageMath.Fit(600f, 60f, temperArea, out _, out _);
            foreach (var h in new[] { 60f, 50f })
            {
                var py = ElPageMath.CenterY(temperArea, h, kw);
                Assert.Equal(barCenterY, py + h * kw / 2f, 2);
            }
        }
    }
}
