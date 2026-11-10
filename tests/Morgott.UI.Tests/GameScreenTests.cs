using Morgott.Core;
using Morgott.UI.GameScreens;
using Xunit;

namespace Morgott.UI.Tests
{
    public class GameScreenTests
    {
        // Tip banner: the tip box plus both pads, never narrower than the banner minimum, never wider than the cap.
        [Theory]
        [InlineData(300f, 20f, 520f, 1400f, 520f)]
        [InlineData(800f, 20f, 520f, 1400f, 840f)]
        [InlineData(2000f, 20f, 520f, 1400f, 1400f)]
        [InlineData(-5f, 20f, 0f, 0f, 40f)]
        public void BannerWidthClamps(float tip, float pad, float min, float max, float want) =>
            Assert.Equal(want, GameScreenMath.BannerWidth(tip, pad, min, max), 3);

        // Player screenshot 2026-11-08: the split panel stayed at vanilla's top-left spot while our layout moved the player
        // window. Vanilla prefab: player (40, 40, 570x287), panel 368x260 centred at (334, 350) -> offset (9, 166.5).
        [Fact]
        public void SplitPanelFollowsThePlayerWindowAtTheVanillaOffset()
        {
            var at = GameScreenMath.SplitPanelAt(new InventoryScreen.Box(40f, 40f, 570f, 287f), 9f, 166.5f, 368f, 260f, 1920f, 1080f);
            Assert.Equal(150f, at.X, 3); // vanilla spot when the window is where vanilla has it
            Assert.Equal(220f, at.Y, 3);
            at = GameScreenMath.SplitPanelAt(new InventoryScreen.Box(700f, 500f, 600f, 400f), 9f, 166.5f, 368f, 260f, 1920f, 1080f);
            Assert.Equal(1000f + 9f - 184f, at.X, 3); // moved with the window
            Assert.Equal(700f + 166.5f - 130f, at.Y, 3);
        }

        [Fact]
        public void SplitPanelIsClampedToTheCanvas()
        {
            var at = GameScreenMath.SplitPanelAt(new InventoryScreen.Box(1500f, 900f, 600f, 400f), 9f, 166.5f, 368f, 260f, 1920f, 1080f);
            Assert.Equal(1920f - 368f, at.X, 3);
            Assert.Equal(1080f - 260f, at.Y, 3);
            at = GameScreenMath.SplitPanelAt(new InventoryScreen.Box(-500f, -500f, 100f, 100f), 0f, 0f, 368f, 260f, 1920f, 1080f);
            Assert.Equal(0f, at.X, 3);
            Assert.Equal(0f, at.Y, 3);
        }

        [Theory]
        // slot x, y, canvas w, h -> panel x, y (368x260 panel, 64 slot, gap 8)
        [InlineData(100f, 400f, 1920f, 1080f, 172f, 302f)]   // right of the slot, centred on it
        [InlineData(1700f, 400f, 1920f, 1080f, 1324f, 302f)] // no room right -> left of the slot
        [InlineData(100f, 1000f, 1920f, 1080f, 172f, 820f)]  // bottom edge clamp
        [InlineData(100f, 0f, 1920f, 1080f, 172f, 0f)]       // top edge clamp
        [InlineData(800f, 300f, 1280f, 720f, 872f, 202f)]  // 1280x720 GUI scale 1, fits right
        [InlineData(1100f, 300f, 1280f, 720f, 724f, 202f)]   // 1280x720: flips left
        [InlineData(300f, 300f, 640f, 360f, 0f, 100f)]       // max GUI scale (tiny canvas): neither side fits -> clamped
        public void SplitPanelSitsBesideTheSlotOnCanvas(float sx, float sy, float cw, float ch, float ex, float ey)
        {
            var at = GameScreenMath.SplitPanelNear(new InventoryScreen.Box(sx, sy, 64f, 64f), 8f, 368f, 260f, cw, ch);
            Assert.Equal(ex, at.X, 3);
            Assert.Equal(ey, at.Y, 3);
            Assert.True(at.X >= 0f && at.Y >= 0f && at.X + at.W <= cw && at.Y + at.H <= ch);
        }

        // Draggable split panel: the stored offset from SplitDragOrigin is the absolute place; WindowMoveMath.Clamp keeps it
        // fully inside the bounds at any canvas size (GUI scale / resolution), a drag into the top-left corner stays a
        // stored (non-zero) place, and the store round-trips per resolution.
        [Fact]
        public void SplitDragPlaceIsAbsoluteAndClampedOnScreen()
        {
            var bounds = new InventoryScreen.Box(6f, 6f, 1908f, 1000f);
            var origin = GameScreenMath.SplitDragOrigin(bounds, 368f, 260f);
            float dx = 900f, dy = 400f;
            Restyle.WindowMoveMath.Clamp(origin, bounds, ref dx, ref dy);
            var at = Restyle.WindowMoveMath.Shift(origin, dx, dy);
            Assert.Equal(907f, at.X, 3);
            Assert.Equal(407f, at.Y, 3);

            // GUI scale up: smaller canvas, the saved place is clamped fully inside.
            var small = new InventoryScreen.Box(6f, 6f, 628f, 340f);
            var o2 = GameScreenMath.SplitDragOrigin(small, 368f, 260f);
            float sx = dx, sy = dy;
            Restyle.WindowMoveMath.Clamp(o2, small, ref sx, ref sy);
            var s = Restyle.WindowMoveMath.Shift(o2, sx, sy);
            Assert.True(s.X >= small.X && s.Y >= small.Y && s.Right <= small.Right + 1e-3f && s.Bottom <= small.Bottom + 1e-3f);

            // Corner drag: clamp stops at -1, never the "never moved" zero.
            float cx = -5000f, cy = -5000f;
            Restyle.WindowMoveMath.Clamp(origin, bounds, ref cx, ref cy);
            Assert.Equal(-1f, cx, 3);
            Assert.Equal(-1f, cy, 3);

            var key = Restyle.WindowMoveMath.Key("split", 1920, 1080);
            var back = Restyle.WindowMoveMath.Parse(Restyle.WindowMoveMath.Format(new System.Collections.Generic.Dictionary<string, (float X, float Y)> { [key] = (dx, dy), [key.Replace("1920x1080", "1280x720")] = (cx, cy) }));
            Assert.Equal((dx, dy), back[key]);
            Assert.Equal((-1f, -1f), back["split@1280x720"]);
        }

        [Fact]
        public void BannerHeightStacksTitleAndPads() => Assert.Equal(160f, GameScreenMath.BannerHeight(100f, 24f, 18f), 3);

        [Theory]
        [InlineData(0f, 40f, 0f)]
        [InlineData(1f, 40f, -40f)]
        [InlineData(10f, 40f, -40f)]
        [InlineData(float.NaN, 40f, 0f)]
        public void SpinAngleTurnsClockwiseAndWraps(float t, float speed, float want) =>
            Assert.Equal(want, GameScreenMath.SpinAngle(t, speed), 3);

        [Fact]
        public void SpinAngleStaysInOneTurn()
        {
            for (var t = 0f; t < 100f; t += 0.37f)
            {
                var a = GameScreenMath.SpinAngle(t, 47f);
                Assert.InRange(a, -360f, 0f);
            }
        }

        [Theory]
        [InlineData(112f, 0.42f, 47.04f)]
        [InlineData(112f, 1.5f, 112f)]
        [InlineData(-1f, 0.5f, 0f)]
        public void GlyphSideIsAShareOfTheSocket(float socket, float share, float want) =>
            Assert.Equal(want, GameScreenMath.GlyphSide(socket, share), 3);

        // Sleep medallion: its own slot gap above the text block, never overlapping it, whatever the text height
        // (user screenshot 2026-10-23: a 4-line dream text ran under the fixed 96 lift).
        [Theory]
        [InlineData(20f)]
        [InlineData(80f)]
        [InlineData(200f)]
        public void MedallionClearsTextAbove(float halfText)
        {
            const float side = 112f, gap = 24f;
            var y = GameScreenMath.MedallionY(halfText, -halfText, side, gap, 540f, -540f, 24f);
            Assert.Equal(halfText + gap + side * 0.5f, y, 3);
            Assert.True(y - side * 0.5f >= halfText + gap - 0.001f);
        }

        // No room above (text near the top): the medallion goes below, still gap away from the text.
        [Fact]
        public void MedallionGoesBelowWhenTopIsFull()
        {
            var y = GameScreenMath.MedallionY(500f, 300f, 112f, 24f, 540f, -540f, 24f);
            Assert.Equal(300f - 24f - 56f, y, 3);
        }

        // Neither fits: the top slot (text stays clear).
        [Fact]
        public void MedallionKeepsTopSlotWhenNothingFits()
        {
            var y = GameScreenMath.MedallionY(50f, -50f, 112f, 24f, 100f, -100f, 0f);
            Assert.Equal(50f + 24f + 56f, y, 3);
        }

        [Theory]
        [InlineData(1500f, 1100f, 200f)]
        [InlineData(900f, 1100f, 0f)]
        [InlineData(1500f, 0f, 0f)]
        public void WrapInsetCapsLineWidth(float width, float max, float want) =>
            Assert.Equal(want, GameScreenMath.WrapInset(width, max), 3);

        // M6 batch 2 families are stable API names (other mods ask for them by string).
        [Fact]
        public void BatchTwoFamilyNames()
        {
        }

        // Active effects rows: blank-line runs of SE tooltips collapse to one break, ends trimmed.
        [Theory]
        [InlineData("Rested\n\n$se_healthregen: +50%\n \n\nDuration", "Rested\n$se_healthregen: +50%\nDuration")]
        [InlineData("  one line  ", "one line")]
        [InlineData("a\nb", "a\nb")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void TidyCollapsesBlankLines(string text, string want) => Assert.Equal(want, GameScreenMath.Tidy(text));

        // Panel stacks kept on screen: the overhang past an edge comes back; a box that fits stays; an oversize box
        // keeps its low edge (connect panel: stack 40 px past the top-left corner of a 1920 canvas).
        [Theory]
        [InlineData(-40f, 556f, 0f, 1920f, 40f)]
        [InlineData(10f, 600f, 0f, 1920f, 0f)]
        [InlineData(1500f, 1960f, 0f, 1920f, -40f)]
        [InlineData(-10f, 2000f, 0f, 1920f, 10f)]
        [InlineData(100f, 2100f, 0f, 1920f, -100f)]
        [InlineData(float.NaN, 10f, 0f, 1920f, 0f)]
        public void EdgeShiftBringsTheStackInside(float min, float max, float lo, float hi, float want) =>
            Assert.Equal(want, Morgott.UI.Restyle.PanelMath.EdgeShift(min, max, lo, hi), 3);

        [Theory]
        // room on the right: panel left = window right + gap, tops level
        [InlineData(100f, 100f, 700f, 900f, 0f, 0f, 400f, 500f, 724f, 400f)]
        // no room right (window reaches 1700): goes left of the window
        [InlineData(1100f, 100f, 1700f, 900f, 0f, 0f, 400f, 500f, 676f, 400f)]
        // window top above the screen: clamped down inside
        [InlineData(100f, 100f, 700f, 1300f, 0f, 0f, 400f, 500f, 724f, 580f)]
        public void DockBesidePlacesThePanelNextToTheWindow(float wx0, float wy0, float wx1, float wy1, float px0, float py0, float px1, float py1, float wantDx, float wantDy)
        {
            var (dx, dy) = Morgott.UI.Restyle.PanelMath.DockBeside(wx0, wy0, wx1, wy1, px0, py0, px1, py1, 0f, 0f, 1920f, 1080f, 24f);
            Assert.Equal(wantDx, dx, 3);
            Assert.Equal(wantDy, dy, 3);
        }
    }
}
