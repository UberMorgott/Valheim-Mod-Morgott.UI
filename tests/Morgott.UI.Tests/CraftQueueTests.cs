using Morgott.UI.CraftingScreen;
using Xunit;

namespace Morgott.UI.Tests
{
    // CraftQueue: multi-craft as N single crafts, one after another.
    public class CraftQueueTests
    {
        [Fact]
        public void RunsEveryCraftThenEnds()
        {
            var q = new CraftQueue();
            q.Start(3);
            Assert.True(q.Running);
            Assert.Equal("1/3", q.Step);
            Assert.True(q.Finished(true));
            Assert.Equal("2/3", q.Step);
            Assert.True(q.Finished(true));
            Assert.Equal("3/3", q.Step);
            Assert.False(q.Finished(true));
            Assert.False(q.Running);
            Assert.Equal(3, q.Done);
        }

        [Fact]
        public void FailedCraftStopsQueueWithoutCountingIt()
        {
            var q = new CraftQueue();
            q.Start(5);
            q.Finished(true);
            Assert.False(q.Finished(false)); // resources gone / inventory full
            Assert.False(q.Running);
            Assert.Equal(1, q.Done);
        }

        [Fact]
        public void CancelStopsAndLaterResultsAreIgnored()
        {
            var q = new CraftQueue();
            q.Start(4);
            q.Finished(true);
            q.Stop();
            Assert.False(q.Running);
            Assert.False(q.Finished(true));
            Assert.Equal(1, q.Done);
        }

        [Fact]
        public void SingleCraftIsNoQueue()
        {
            var q = new CraftQueue();
            q.Start(1);
            Assert.False(q.Running);
        }
    }
}
