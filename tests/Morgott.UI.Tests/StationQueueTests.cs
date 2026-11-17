using System.Collections.Generic;
using Morgott.Core;
using Morgott.UI.StationQueue;
using Xunit;

namespace Morgott.UI.Tests
{
    public class StationQueueTests
    {
        private const string Until = "{0} — пока\n{1} в сети {2} {3}";

        [Fact]
        public void SingleOnlyForOneInput()
        {
            Assert.True(StationQueueMath.IsSingle(1));
            Assert.False(StationQueueMath.IsSingle(5));
            Assert.False(StationQueueMath.IsSingle(0));
        }

        [Fact]
        public void CardTextPerMode()
        {
            var e = StationQueueMath.NewEntry("FineWood", StationQueueMode.Count, "Coal");
            e.Target = 1240;
            Assert.Equal("1 240 × Качественная древесина", StationQueueMath.CardText(e, "Качественная древесина", "Уголь", Until));
            e.Mode = StationQueueMode.Infinite;
            Assert.Equal("Древесина — ∞", StationQueueMath.CardText(e, "Древесина", "Уголь", Until));
            e.Mode = StationQueueMode.Until;
            Assert.Equal("Древесина — пока\nУголь в сети < 100", StationQueueMath.CardText(e, "Древесина", "Уголь", Until));
            e.Compare = StationQueueCompare.AtLeast;
            Assert.Equal("Древесина — пока\nУголь в сети ≥ 100", StationQueueMath.CardText(e, "Древесина", "Уголь", Until));
        }

        [Fact]
        public void SummaryUntilHasCurrent()
        {
            var e = StationQueueMath.NewEntry("Wood", StationQueueMode.Until, "Coal");
            var s = StationQueueMath.Summary(e, "Древесина", "Уголь", 46, "{0}: {1}", "{0}", "{0} пока {1} в сети {2} {3} (сейчас {4}).");
            Assert.Equal("Древесина пока Уголь в сети < 100 (сейчас 46).", s);
            e.Mode = StationQueueMode.Count;
            Assert.Equal("Древесина: 10", StationQueueMath.Summary(e, "Древесина", "", 0, "{0}: {1}", "{0}", ""));
        }

        [Fact]
        public void HeaderJoinSkipsEmpty() =>
            Assert.Equal("В печи: 12/25 · Сеть: 3 сундука", StationQueueMath.Join("В печи: 12/25", null, "", "Сеть: 3 сундука"));

        [Theory]
        [InlineData(1, "сундук")]
        [InlineData(3, "сундука")]
        [InlineData(5, "сундуков")]
        [InlineData(11, "сундуков")]
        [InlineData(21, "сундук")]
        [InlineData(24, "сундука")]
        [InlineData(0, "сундуков")]
        public void RussianPlural(int n, string want) => Assert.Equal(want, StationQueueMath.Plural(n, "сундук", "сундука", "сундуков"));

        [Fact]
        public void ActiveOnlyFirstWhileRunning()
        {
            Assert.True(StationQueueMath.IsActive(0, StationQueueStatus.Working));
            Assert.True(StationQueueMath.IsActive(0, StationQueueStatus.Waiting));
            Assert.False(StationQueueMath.IsActive(0, StationQueueStatus.Paused));
            Assert.False(StationQueueMath.IsActive(1, StationQueueStatus.Working));
        }

        [Fact]
        public void ShareCountUsesFed()
        {
            var e = StationQueueMath.NewEntry("Wood", StationQueueMode.Count, null);
            e.Target = 50;
            e.Fed = 18;
            Assert.Equal(0.36f, StationQueueMath.Share(e, 0.9f), 3);
            e.Mode = StationQueueMode.Infinite;
            Assert.Equal(0.9f, StationQueueMath.Share(e, 0.9f), 3);
            Assert.Equal(0.66f, StationQueueMath.Share(132, 200), 3);
            Assert.Equal(1f, StationQueueMath.Share(300, 200), 3);
        }

        [Fact]
        public void StepAndClamp()
        {
            Assert.Equal(1, StationQueueMath.Step(1, -1, false));
            Assert.Equal(20, StationQueueMath.Step(10, 1, true));
            Assert.Equal(StationQueueMath.MaxValue, StationQueueMath.Clamp(int.MaxValue));
        }

        [Fact]
        public void MoveAndSave()
        {
            var a = StationQueueMath.NewEntry("A", StationQueueMode.Count, null);
            var b = StationQueueMath.NewEntry("B", StationQueueMode.Count, null);
            var list = new List<StationQueueEntry> { a, b };
            Assert.False(StationQueueMath.Move(list, 0, -1));
            Assert.True(StationQueueMath.Move(list, 0, 1));
            Assert.Equal("B", list[0].Item);

            var draft = b.Clone();
            draft.Target = 77;
            var saved = StationQueueMath.Save(list, 0, draft);
            Assert.Equal(2, saved.Count);
            Assert.Equal(77, saved[0].Target);
            Assert.Equal(10, list[0].Target); // source untouched
            Assert.Equal(3, StationQueueMath.Save(list, 1, draft).Count); // index 1 is another input: appended
        }

        [Fact]
        public void SingleWritesOneOrNone()
        {
            var e = StationQueueMath.NewEntry("SoftTissue", StationQueueMode.Until, "Eitr");
            var entries = new List<StationQueueEntry> { e };
            Assert.Same(e, StationQueueMath.SingleEntry(entries, "SoftTissue"));
            Assert.Null(StationQueueMath.SingleEntry(entries, "Barley"));
            Assert.Single(StationQueueMath.Single(e));
            Assert.Empty(StationQueueMath.Single(null));
        }

        [Fact]
        public void TextsAndSearch()
        {
            Assert.Equal("<color=#E8B64A>[Esc]</color> Закрыть", StationQueueMath.KeyHint("Esc", "Закрыть", "E8B64A"));
            Assert.True(StationQueueMath.Matches("Уголь", "уго"));
            Assert.True(StationQueueMath.Matches("Уголь", ""));
            Assert.False(StationQueueMath.Matches("Смола", "уго"));
        }
    }
}
