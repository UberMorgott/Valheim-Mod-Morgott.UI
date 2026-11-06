using Morgott.UI.ModScreens;
using Xunit;

namespace Morgott.UI.Tests
{
    // Epic Loot enchanting table as a full-screen page (ModScreens\ElPage, user 2026-11-07). Canvas units; the content box
    // of mockup 02 at 1920x1080 is 1799 x 766.
    public class ElPageTests
    {
        // Height-fit: the 700-high prefab body grows to the box height, the panel takes the rest of the width.
        [Fact]
        public void FitsHeightAndPanelSpansTheRest()
        {
            var l = ElPageMath.Body(1799f, 766f, 160f, 700f, 1000f, 20f);
            var s = 766f / 700f;
            Assert.Equal(s, l.Scale, 4);
            Assert.Equal(160f * s, l.TabsW, 3);
            Assert.Equal(180f * s, l.ContentX, 3);
            Assert.True(1799f - l.ContentX >= 1000f * s); // the panel never narrower than the prefab's at that scale
        }

        // A narrow box (4:3, 1024 wide): the width limits the scale, column + gap + panel fill it exactly.
        [Fact]
        public void NarrowBoxShrinksToTheWidth()
        {
            var l = ElPageMath.Body(900f, 766f, 160f, 700f, 1000f, 20f);
            Assert.Equal(900f / 1180f, l.Scale, 4);
            Assert.Equal(900f - 1000f * l.Scale, l.ContentX, 3);
        }

        [Fact]
        public void EmptyBoxKeepsScaleOne() => Assert.Equal(1f, ElPageMath.Body(0f, 766f, 160f, 700f, 1000f, 20f).Scale);
    }
}
