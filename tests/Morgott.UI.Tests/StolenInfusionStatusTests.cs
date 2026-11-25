using System.Collections.Generic;
using Morgott.UI.PlayerHud;
using Xunit;

namespace Morgott.UI.Tests
{
    public class StolenInfusionStatusTests
    {
        [Fact]
        public void ZeroMaskClearsDestinationAndReturnsZero()
        {
            var ids = new List<int> { 99 };
            Assert.Equal(0, StolenInfusionStatus.ExtractIds(0UL, ids));
            Assert.Empty(ids);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(5)]
        [InlineData(31)]
        [InlineData(32)]
        [InlineData(47)]
        [InlineData(63)]
        public void SingleBitExtractsExactId(int bit)
        {
            var ids = new List<int>();
            Assert.Equal(1, StolenInfusionStatus.ExtractIds(1UL << bit, ids));
            Assert.Equal(new[] { bit }, ids);
        }

        [Fact]
        public void MultiBitIncludingSevenAndHighWordExtractsAscendingIds()
        {
            int[] expected = { 0, 2, 7, 15, 31, 32, 63 };
            ulong mask = 0;
            foreach (var bit in expected) mask |= 1UL << bit;

            var ids = new List<int>();
            Assert.Equal(expected.Length, StolenInfusionStatus.ExtractIds(mask, ids));
            Assert.Equal(expected, ids);
        }

        [Fact]
        public void NegativeSignedLongBit63DoesNotSignExtend()
        {
            var ids = new List<int>();
            var mask = unchecked((ulong)long.MinValue);
            Assert.Equal(1, StolenInfusionStatus.ExtractIds(mask, ids));
            Assert.Equal(new[] { 63 }, ids);
        }

        [Fact]
        public void FullMaskExtractsAll64IdsInOrder()
        {
            var ids = new List<int>();
            Assert.Equal(64, StolenInfusionStatus.ExtractIds(ulong.MaxValue, ids));
            for (var i = 0; i < 64; i++) Assert.Equal(i, ids[i]);
        }
    }
}
