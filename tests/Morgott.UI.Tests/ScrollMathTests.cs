using Morgott.UI.Restyle;
using Xunit;

namespace Morgott.UI.Tests
{
    public class ScrollMathTests
    {
        // Trader row 80 px in a 600 px list, Valheim's 0.05 delta per notch: one notch = 1.5 rows = 120 px.
        [Fact]
        public void NotchMovesOneAndAHalfRows()
        {
            var px = ScrollMath.NotchPx(80f, 600f, 1f);
            Assert.Equal(120f, px, 3);
            Assert.Equal(2400f, ScrollMath.Sensitivity(px, 0.05f), 3);
        }

        // A tall row in a short view is capped at half the view; the speed multiplier scales after the cap.
        [Fact]
        public void NotchCapsAtHalfViewThenScales()
        {
            Assert.Equal(100f, ScrollMath.NotchPx(300f, 200f, 1f), 3);
            Assert.Equal(200f, ScrollMath.NotchPx(300f, 200f, 2f), 3);
        }

        // No row found: a sixth of the view; no input module (delta 0): sensitivity = pixels.
        [Fact]
        public void FallbacksStayFinite()
        {
            Assert.Equal(50f, ScrollMath.NotchPx(0f, 300f, 1f), 3);
            Assert.Equal(1f, ScrollMath.NotchPx(0f, 0f, 1f), 3);
            Assert.Equal(36f, ScrollMath.Sensitivity(36f, 0f), 3);
        }

        // GemVaultTab: 30px section header + nested GridLayoutGroup (88 + 10 = 98px) -> picks 98px, not 30px or 12px badge.
        [Fact]
        public void SelectPitchPrefersNestedGridOverHeader()
        {
            var pitch = ScrollMath.SelectPitch(new[] { 98f }, 1, new[] { 44f, 44f }, 2);
            Assert.Equal(98f, pitch, 3);
            Assert.Equal(147f, ScrollMath.NotchPx(pitch, 654f, 1f), 3);
        }

        // GemTab: 28+4=32px group header followed by 54+4=58px gem rows -> picks 58px (87 px/notch).
        [Fact]
        public void SelectPitchPrefersItemRowsOverGroupHeader()
        {
            var pitch = ScrollMath.SelectPitch(null, 0, new[] { 32f, 58f, 58f, 58f, 32f, 58f }, 6);
            Assert.Equal(58f, pitch, 3);
            Assert.Equal(87f, ScrollMath.NotchPx(pitch, 514f, 1f), 3);
        }

        // Multi-section list with a single leading header and one row, or with an outlier tall banner.
        [Fact]
        public void SelectPitchHandlesSmallAndOutlierLists()
        {
            Assert.Equal(58f, ScrollMath.SelectPitch(null, 0, new[] { 32f, 58f }, 2), 3);
            Assert.Equal(58f, ScrollMath.SelectPitch(null, 0, new[] { 30f, 58f, 58f, 58f, 200f }, 5), 3);
        }

        // Text-panel fallback ignores tiny < 16pt badges whenever body text exists.
        [Fact]
        public void SelectTextLineIgnoresTinyBadges()
        {
            Assert.Equal(20f, ScrollMath.SelectTextLine(new[] { 12f, 14f, 20f }, 3), 3);
            Assert.Equal(18f, ScrollMath.SelectTextLine(new[] { 12f, 18f, 28f }, 3), 3);
            Assert.Equal(14f, ScrollMath.SelectTextLine(new[] { 12f, 14f }, 2), 3);
        }

        // Drag-scroll threshold floor (>= 6px), click suppression, discrete row accumulation, and OnDisable reset.
        [Fact]
        public void DragScrollThresholdSuppressionAndSteps()
        {
            Assert.Equal(6f, ScrollMath.DragThreshold(3f), 3);
            Assert.Equal(10f, ScrollMath.DragThreshold(10f), 3);

            var drag = new ScrollMath.DragScrollDelta();
            drag.Begin(100f, 100f);
            // Movement below threshold does not scroll and does not suppress click.
            Assert.False(drag.Move(100f, 104f, 6f, out var dx, out var dy));
            Assert.Equal(0f, dx, 3);
            Assert.Equal(0f, dy, 3);
            Assert.False(drag.End());

            // Crossing threshold starts drag, accumulates into whole rows, and suppresses click on release.
            drag.Begin(100f, 100f);
            Assert.True(drag.Move(100f, 130f, 6f, out _, out dy));
            Assert.Equal(30f, dy, 3);
            Assert.Equal(0, drag.StepY(dy, 50f));
            Assert.True(drag.Move(100f, 160f, 6f, out _, out dy));
            Assert.Equal(1, drag.StepY(dy, 50f)); // 30 + 30 = 60 -> +1 row, 10px remainder
            Assert.True(drag.Move(100f, 100f, 6f, out _, out dy));
            Assert.Equal(-1, drag.StepY(dy, 50f)); // 10 - 60 = -50 -> -1 row
            Assert.True(drag.End());
            Assert.False(drag.Active);

            // Reset (OnDisable mid-drag) clears active state cleanly.
            drag.Begin(0f, 0f);
            Assert.True(drag.Move(0f, 20f, 6f, out _, out _));
            drag.Reset();
            Assert.False(drag.Active);
            Assert.False(drag.Dragging);
            Assert.False(drag.Move(0f, 50f, 6f, out _, out _));
        }

        // Custom IScrollHandler wheel callers (PortalPicker, MapLegend): Valheim emits 0.05 per notch, not 1.0.
        [Fact]
        public void WheelTicksAndPortalPickerNotchPixelsMatchScrollRectSpeed()
        {
            Assert.Equal(1f, ScrollMath.WheelTicks(0.05f, 0.05f), 3);
            Assert.Equal(-3f, ScrollMath.WheelTicks(-0.15f, 0.05f), 3);
            Assert.Equal(0.5f, ScrollMath.WheelTicks(0.5f, 0f), 3);

            // PortalPicker: rowH = 54, view = 400, speed = 1 -> 1 notch = 81 px (1.5 rows), same as ScrollRect.
            Assert.Equal(81f, ScrollMath.WheelTicks(0.05f, 0.05f) * ScrollMath.NotchPx(54f, 400f, 1f), 3);
            Assert.Equal(-81f, ScrollMath.WheelTicks(-0.05f, 0.05f) * ScrollMath.NotchPx(54f, 400f, 1f), 3);
            Assert.Equal(162f, ScrollMath.WheelTicks(0.05f, 0.05f) * ScrollMath.NotchPx(54f, 400f, 2f), 3);
        }

        // MapLegend discrete row stepper: single notch >= +-1 even below 1x speed; multi-notch bursts and speed scale proportionally.
        [Fact]
        public void WheelRowsPreservesBurstsAndSpeedWithoutSquashing()
        {
            Assert.Equal(0, ScrollMath.WheelRows(0f, 0.05f, 1f));
            Assert.Equal(1, ScrollMath.WheelRows(0.05f, 0.05f, 1f));
            Assert.Equal(-1, ScrollMath.WheelRows(-0.05f, 0.05f, 1f));
            Assert.Equal(1, ScrollMath.WheelRows(0.05f, 0.05f, 0.25f));
            Assert.Equal(-1, ScrollMath.WheelRows(-0.05f, 0.05f, 0.25f));
            Assert.Equal(3, ScrollMath.WheelRows(0.15f, 0.05f, 1f));
            Assert.Equal(-3, ScrollMath.WheelRows(-0.15f, 0.05f, 1f));
            Assert.Equal(6, ScrollMath.WheelRows(0.15f, 0.05f, 2f));
            Assert.Equal(2, ScrollMath.WheelRows(0.05f, 0.05f, 1.5f));
            Assert.Equal(3, ScrollMath.WheelRows(0.10f, 0.05f, 1.5f));
        }
    }
}
