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
    }
}
