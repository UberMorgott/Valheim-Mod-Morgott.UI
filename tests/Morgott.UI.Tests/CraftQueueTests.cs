using Morgott.UI.CraftingScreen;
using Xunit;

namespace Morgott.UI.Tests
{
    // CraftRun: multi-craft as N single crafts, one after another.
    public class CraftQueueTests
    {
        [Fact]
        public void RunsEveryCraftThenEnds()
        {
            var q = new CraftRun();
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
            var q = new CraftRun();
            q.Start(5);
            q.Finished(true);
            Assert.False(q.Finished(false)); // resources gone / inventory full
            Assert.False(q.Running);
            Assert.Equal(1, q.Done);
        }

        [Fact]
        public void CancelStopsAndLaterResultsAreIgnored()
        {
            var q = new CraftRun();
            q.Start(4);
            q.Finished(true);
            q.Stop();
            Assert.False(q.Running);
            Assert.False(q.Finished(true));
            Assert.Equal(1, q.Done);
        }

        [Fact]
        public void PendingResourcesWaitThenTheUnitCrafts()
        {
            var q = new CraftRun();
            q.Start(3);
            q.Finished(true);
            Assert.True(q.Wait(10f)); // SafeTake claim not landed yet
            Assert.True(q.Waiting);
            Assert.True(q.Wait(10f + CraftRun.PendingWait - 0.1f));
            q.Pressed();
            Assert.False(q.Waiting);
            Assert.True(q.Finished(true));
            Assert.True(q.Wait(20f)); // a crafted unit restarts the wait budget
            q.Pressed();
            Assert.False(q.Finished(true));
            Assert.Equal(3, q.Done);
        }

        [Fact]
        public void RealShortageEndsAfterTheBoundedWait()
        {
            var q = new CraftRun();
            q.Start(3);
            q.Finished(true);
            Assert.True(q.Wait(5f));
            Assert.False(q.Wait(5f + CraftRun.PendingWait));
            Assert.False(q.Waiting);
            Assert.Equal(1, q.Done);
        }

        [Fact]
        public void RefusedCraftRetriesWithinTheSameBudget()
        {
            var q = new CraftRun();
            q.Start(2);
            Assert.True(q.Wait(0f));
            q.Pressed();
            Assert.True(q.Finished(false, refused: true)); // DoCrafting refused for want of resources: retry, not counted
            Assert.Equal(0, q.Done);
            Assert.False(q.Wait(CraftRun.PendingWait)); // budget not reset by a refusal
        }

        [Fact]
        public void SingleCraftIsNoQueue()
        {
            var q = new CraftRun();
            q.Start(1);
            Assert.False(q.Running);
        }
    }
}
