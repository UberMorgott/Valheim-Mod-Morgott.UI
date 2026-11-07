using System.Linq;
using Morgott.UI.GameScreens;
using Xunit;

namespace Morgott.UI.Tests
{
    // Rune stone reveal: 1:1 remap onto glyphs the vanilla rune face draws, and the hold -> decode wave timing.
    public class RuneRevealTests
    {
        // The vanilla rune TTF's empty glyphs (0 contours, resources.assets Font "rune", 2026-11-07).
        private const string Empty = "CEБЕЁЗКПФЦЧceбеёзкпфцч";
        private static bool VanillaFace(char c) => Empty.IndexOf(c) < 0;

        [Fact]
        public void RemapKeepsLengthSpacesAndTagsAndDrawsEveryLetter()
        {
            const string ru = "Здесь пал воин. <color=red>Ищи</color> Камень, Цепь и Чашу — FACE!";
            var r = RuneRevealMath.Remap(ru, VanillaFace);
            Assert.Equal(ru.Length, r.Length);
            for (var i = 0; i < ru.Length; i++)
                if (!char.IsLetter(ru[i])) Assert.Equal(ru[i], r[i]);
            Assert.Contains("<color=red>", r);
            var outsideTags = System.Text.RegularExpressions.Regex.Replace(r, "<[^>]*>", "");
            Assert.DoesNotContain(outsideTags.Where(char.IsLetter), c => !VanillaFace(c));
            Assert.Equal(char.IsUpper(ru[0]), char.IsUpper(r[0]));
        }

        [Fact]
        public void RemapLeavesDrawnLettersAndFallsBackToLatin()
        {
            Assert.Equal("Дом", RuneRevealMath.Remap("Дом", VanillaFace));
            // A face without Cyrillic at all: transliterated to Latin runes.
            Assert.Equal("Dom", RuneRevealMath.Remap("Дом", c => c < 0x400));
        }

        [Fact]
        public void ProgressHoldsThenSweepsLeftToRight()
        {
            const float hold = 0.6f, decode = 1.2f;
            Assert.Equal(0f, RuneRevealMath.Progress(0, 40, 0.3f, hold, decode));
            Assert.Equal(0f, RuneRevealMath.Progress(0, 40, hold, hold, decode));
            var mid = hold + decode / 2f;
            Assert.Equal(1f, RuneRevealMath.Progress(5, 40, mid, hold, decode));
            Assert.Equal(0f, RuneRevealMath.Progress(35, 40, mid, hold, decode));
            for (var i = 1; i < 40; i++)
                Assert.True(RuneRevealMath.Progress(i, 40, mid, hold, decode) <= RuneRevealMath.Progress(i - 1, 40, mid, hold, decode));
            Assert.Equal(1f, RuneRevealMath.Progress(39, 40, hold + decode - 0.001f, hold, decode));
            Assert.True(RuneRevealMath.Done(hold + decode, hold, decode));
            Assert.Equal(1f, RuneRevealMath.Progress(39, 40, float.MaxValue, hold, decode)); // skip
        }

        [Fact]
        public void VanillaRuneLineHiddenOnlyWhileRevealOn()
        {
            Assert.Equal("", RuneRevealMath.VanillaRuneLine(true, "Here fell a warrior"));
            Assert.Equal("Here fell a warrior", RuneRevealMath.VanillaRuneLine(false, "Here fell a warrior"));
        }

        [Fact]
        public void ProgressIsMonotoneInTime()
        {
            var prev = 0f;
            for (var t = 0f; t < 2f; t += 0.01f)
            {
                var p = RuneRevealMath.Progress(20, 40, t, 0.6f, 1.2f);
                Assert.True(p >= prev);
                prev = p;
            }
        }
    }
}
