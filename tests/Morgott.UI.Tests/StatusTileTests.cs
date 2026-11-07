using System;
using System.Collections.Generic;
using Morgott.UI.PlayerHud;
using Morgott.UI.Restyle;
using Xunit;

namespace Morgott.UI.Tests
{
    // Labelled status tiles above the strip (user 2026-11-07: icons too small, timers / "Комфорт: 3" ran into each
    // other): icon + name line + time line per effect (vanilla Hud.UpdateStatusEffects), laid out as HudCluster.Status
    // does on every screen / GUI scale - tiles never overlap each other, stay inside the strip, the chat clears them.
    public class StatusTileTests
    {
        private static float F(string token) => StripLayoutTests.Floats[token];

        public static TheoryData<float, float, float> Screens => new TheoryData<float, float, float>
        {
            { 1280f, 720f, 1f }, { 1600f, 900f, 1f }, { 1920f, 1080f, 1f }, { 2560f, 1440f, 1f }, { 3840f, 2160f, 1f },
            { 1680f, 1050f, 1f }, { 1920f, 1200f, 1f }, { 2560f, 1080f, 1f }, { 3440f, 1440f, 1f },
            { 1920f, 1080f, 1.15f }, { 2560f, 1440f, 1.15f }, { 1280f, 720f, 0.8f }
        };

        private struct Box
        {
            public float X, Y, W, H;
        }

        [Fact]
        public void TileStacksIconNameAndTime()
        {
            var t = HudMath.StatusTile(64f, 15f);
            Assert.Equal(64f, t.Icon);
            Assert.True(t.W > t.Icon); // the name has more room than the icon
            Assert.True(t.Line >= 15f * 1.3f); // a TMP line never clipped by its box (Ellipsis drops a too-short line)
            Assert.Equal(t.Icon + HudMath.TileTextGap + 2f * t.Line, t.H, 3);
            // floored font (small screens) grows the lines, not into the icon
            var small = HudMath.StatusTile(64f, 30f);
            Assert.True(small.H > t.H && small.Line > t.Line);
        }

        // user 2026-11-08: tiles closer together - width follows the widest label, never under the icon / over 2 icons
        [Fact]
        public void TileWidthFollowsTheLabel()
        {
            Assert.Equal(64f, HudMath.StatusTile(64f, 15f, 10f).W); // short label: the icon
            Assert.Equal(90f + HudMath.TileLabelPad, HudMath.StatusTile(64f, 15f, 90f).W);
            Assert.Equal(128f, HudMath.StatusTile(64f, 15f, 500f).W); // long name: capped, ellipsis
            Assert.Equal(128f, HudMath.StatusTile(64f, 15f).W); // unmeasured: the widest tile
            Assert.True(F("size.hud.status.strip.gap") < 8f);
        }

        // vanilla Hud.UpdateFood (Hud.cs:1037-1046): ceil minutes from 60 s, floor seconds below
        [Theory]
        [InlineData(9.7f, true, 9, false)]
        [InlineData(59.9f, true, 59, false)]
        [InlineData(60f, true, 1, true)]
        [InlineData(61f, true, 2, true)]
        [InlineData(1500f, true, 25, true)]
        [InlineData(0.4f, true, 0, false)]
        [InlineData(0f, false, 0, false)]
        public void FoodTimeIsVanillaForm(float s, bool shown, int n, bool minutes)
        {
            Assert.Equal(shown, HudMath.FoodTime(s, out var gotN, out var gotMin));
            if (!shown) return;
            Assert.Equal(n, gotN);
            Assert.Equal(minutes, gotMin);
        }

        [Fact]
        public void IconsBiggerThanTheOldTiles() =>
            Assert.True(0.75f * F("size.hud.strip.cell") > 0.6f * F("size.hud.strip.cell") * 1.2f);

        [Fact]
        public void ChatClearsTheRightGroup()
        {
            Assert.Equal(100f, StripMath.StatusClearTop(100f, 60f, 0f)); // empty group: the strip top
            Assert.Equal(100f, StripMath.StatusClearTop(100f, 60f, 30f));
            Assert.Equal(60f + 250f, StripMath.StatusClearTop(100f, 60f, 250f));
        }

        [Theory]
        [MemberData(nameof(Screens))]
        public void TilesNeverOverlapAtAnyCount(float w, float h, float gui)
        {
            var s = Math.Min(w / 1920f, h / 1080f) * gui;
            var slotPx = StripMath.StripSlotPx(StripMath.SlotPx(F("size.slot"), 1f, s), F("scale.hud.strip"), 1f);
            var ppu = StripMath.UnitPx(slotPx, F("size.hud.strip.cell"));
            HudMath.HudBox(0f, w, h, true, F("ratio.hud.ultrawide"), out _, out var boxW);
            var input = StripLayoutTests.In(1, 3, 4, StripMath.WidthPx(boxW, 1f) / ppu);
            var l = StripMath.Compute(input);
            // the floor HudArt.FloorPx gives the text (run 20261107-235706: tile sized with the timer floor 12 px, the text got
            // the number floor 14 px -> at 720p TMP Ellipsis dropped every name line)
            var font = F("size.hud.font.status");
            var textFont = StripMath.FontFloor(font, font >= F("size.hud.font.number") ? F("floor.text.number.px") : F("floor.text.timer.px"), ppu);
            // every measured width: icon-only, a mid label, unmeasured (widest)
            foreach (var label in new[] { 0f, 90f, -1f })
                TilesNeverOverlap(w, h, gui, l, input, textFont, HudMath.StatusTile(0.75f * F("size.hud.strip.cell"), textFont, label));
        }

        private static void TilesNeverOverlap(float w, float h, float gui, StripMath.Layout l, StripMath.Input input, float textFont, HudMath.Tile tile)
        {
            Assert.True(tile.Line >= textFont * 1.3f, $"{w}x{h}@{gui}: line {tile.Line} too short for font {textFont}");
            var gap = F("size.hud.status.strip.gap");
            var pad = F("size.hud.status.pad");
            var sideL = l.StatusL - gap;
            var sideR = l.StatusR + gap;
            var perRow = StripMath.StatusPerRow(10, tile.W, gap, sideL - pad);
            var rowW = StripMath.RowWidth(perRow, tile.W, gap);
            var statusBase = l.WingH + F("size.hud.strip.status.gap") + pad;
            const int rows = 2;
            for (var n = 1; n <= 14; n++)
            {
                var boxes = new List<Box>();
                var groupH = 0f;
                foreach (var right in new[] { false, true })
                {
                    var flow = HudMath.Wrap(n, tile.W, gap, rowW, HudMath.MaxRows(rows * (tile.H + gap), tile.H, gap));
                    Assert.Equal(n, flow.Visible + flow.Overflow);
                    var rootX = right ? sideR : sideL - rowW;
                    var drawn = flow.Visible + (flow.MoreCell ? 1 : 0);
                    for (var i = 0; i < drawn; i++)
                    {
                        HudMath.FlowCell(i, flow.Columns, tile.W, tile.H, gap, true, !right, out var x, out var y);
                        boxes.Add(new Box { X = rootX + x, Y = statusBase + y, W = tile.W, H = tile.H });
                    }
                    if (right) groupH = flow.Rows * (tile.H + gap);
                }
                for (var a = 0; a < boxes.Count; a++)
                {
                    var b = boxes[a];
                    Assert.True(b.X >= -0.01f && b.X + b.W <= input.Width + 0.01f, $"{w}x{h}@{gui} n={n}: tile {a} x {b.X:0.0}..{b.X + b.W:0.0} outside strip 0..{input.Width:0.0}");
                    Assert.True(b.X + b.W <= l.StatusL + 0.01f || b.X >= l.StatusR - 0.01f, $"{w}x{h}@{gui} n={n}: tile {a} over the centre");
                    Assert.True(b.Y >= l.WingH, $"{w}x{h}@{gui}: tile under the wing top (food / potion pad)");
                    for (var c = a + 1; c < boxes.Count; c++)
                    {
                        var o = boxes[c];
                        var hit = LayoutMath.Overlaps(b.X, b.X + b.W, o.X, o.X + o.W, 0.01f) && LayoutMath.Overlaps(b.Y, b.Y + b.H, o.Y, o.Y + o.H, 0.01f);
                        Assert.False(hit, $"{w}x{h}@{gui} n={n}: tiles {a} and {c} overlap");
                    }
                }
                // the chat (bottom right) is lifted over the right group's top
                var top = StripMath.StatusClearTop(l.Height, statusBase, groupH);
                foreach (var b in boxes)
                    if (b.X >= l.StatusR) Assert.True(b.Y + b.H <= top + 0.01f);
            }
            // 10 effects (one side) stay labelled: at most the "+N" tile folds the rest
            Assert.True(perRow * rows >= 4, $"{w}x{h}@{gui}: only {perRow} tiles a row");
        }
    }
}
