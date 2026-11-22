using System.Collections.Generic;
using Morgott.Core;
using Morgott.UI.Restyle;
using Morgott.UI.StationQueue;
using Xunit;

namespace Morgott.UI.Tests
{
    public class StationQueueTests
    {
        private const string Until = "{0} — пока\n{1} в сети {2} {3}";

        // Run 20261117-183057: the window closed in the frame after it opened because it judged the station centre (5 m
        // cap) while the hotkey opens anything hovered within reach of its surface (Player.cs:4296, m_maxInteractDistance 5).
        [Fact]
        public void EverythingTheHotkeyCanOpenStaysOpen()
        {
            Assert.True(StationQueueMath.KeepsOpen(5f, 5f)); // hovered at the edge of reach
            Assert.True(StationQueueMath.KeepsOpen(6.4f, 5f)); // one step back
            Assert.False(StationQueueMath.KeepsOpen(7f, 5f)); // walked away
        }

        [Fact]
        public void SingleOnlyForOneInput()
        {
            Assert.True(StationQueueMath.IsSingle(1));
            Assert.False(StationQueueMath.IsSingle(5));
            Assert.False(StationQueueMath.IsSingle(0));
        }

        [Fact]
        public void ShownTargetDefaultsWhenUnset()
        {
            Assert.Equal(StationQueueMath.DefaultCount, StationQueueMath.ShownTarget(0));
            Assert.Equal(50, StationQueueMath.ShownTarget(50));
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
        public void ActiveIsTheProvidersEntryWhileRunning()
        {
            Assert.True(StationQueueMath.IsActive(0, 0, StationQueueStatus.Working));
            Assert.True(StationQueueMath.IsActive(0, 0, StationQueueStatus.Waiting));
            Assert.False(StationQueueMath.IsActive(0, 0, StationQueueStatus.Paused));
            Assert.False(StationQueueMath.IsActive(1, 0, StationQueueStatus.Working));
            Assert.True(StationQueueMath.IsActive(1, 1, StationQueueStatus.Working)); // a resting keep entry above it
            Assert.False(StationQueueMath.IsActive(0, -1, StationQueueStatus.Working)); // all resting
        }

        [Fact]
        public void KeepInStockTexts()
        {
            var e = StationQueueMath.NewEntry("Wood", StationQueueMode.Count, "Coal");
            e.Target = 1;
            e.Keep = true;
            Assert.Equal("Древесина — держать\n1 × Уголь в запасе", StationQueueMath.CardText(e, "Древесина", "Уголь", Until, "{0} — держать\n{2} × {1} в запасе", "Уголь"));
            Assert.Equal("1 × Древесина", StationQueueMath.CardText(e, "Древесина", "Уголь", Until)); // no keep template: plain count
            Assert.Equal("Древесина: Уголь < 1 (3)", StationQueueMath.Summary(e, "Древесина", "Уголь", 3, "{0}: {1}", "{0}", "", "{0}: {1} < {2} ({3})"));
            e.Mode = StationQueueMode.Until;
            Assert.Equal("Древесина пока Уголь < 100 (0). Остаётся.", StationQueueMath.Summary(e, "Древесина", "Уголь", 0, "", "", "{0} пока {1} {2} {3} ({4}).", "", "Остаётся."));
            Assert.False(StationQueueMath.CanKeep(StationQueueMode.Infinite));
            Assert.True(StationQueueMath.CanKeep(StationQueueMode.Until));
        }

        [Fact]
        public void HeaderSelectorSwitchesEveryEntryAndNewOnesTakeIt()
        {
            var none = new List<StationQueueEntry>();
            Assert.False(StationQueueMath.IsStock(none, false));
            Assert.True(StationQueueMath.IsStock(none, true)); // empty queue: the window's choice
            var q = new List<StationQueueEntry> { StationQueueMath.NewEntry("Wood", StationQueueMode.Count, "Coal"), StationQueueMath.NewEntry("TinOre", StationQueueMode.Count, "Tin") };
            q[1].Id = 7;
            Assert.False(StationQueueMath.IsStock(q, true)); // entries decide once there are any
            var stock = StationQueueMath.WithKeep(q, true);
            Assert.All(stock, e => Assert.True(e.Keep));
            Assert.Equal(7, stock[1].Id); // same entry for the owner's merge
            Assert.False(q[0].Keep); // the read state is never edited in place
            Assert.True(StationQueueMath.IsStock(stock, false));
            Assert.All(StationQueueMath.WithKeep(stock, false), e => Assert.False(e.Keep));
            Assert.True(StationQueueMath.NewEntry("Wood", StationQueueMode.Count, "Coal", keep: true).Keep);
        }

        [Fact]
        public void KeepShareIsStockOverTarget()
        {
            var e = StationQueueMath.NewEntry("Wood", StationQueueMode.Count, "Coal");
            e.Fed = 9;
            e.Keep = true;
            Assert.Equal(0.3f, StationQueueMath.Share(e, 0f, 3), 3);
            e.Keep = false;
            Assert.Equal(0.9f, StationQueueMath.Share(e, 0f, 3), 3);
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
        public void SaveFindsTheEditedEntryByIdAfterRowsAboveFinished()
        {
            var done = StationQueueMath.NewEntry("Wood", StationQueueMode.Count, null);
            done.Id = 1;
            var kept = StationQueueMath.NewEntry("Wood", StationQueueMode.Count, null);
            kept.Id = 2;
            var draft = kept.Clone(); // editor opened on index 1
            draft.Keep = true;
            var now = new List<StationQueueEntry> { kept }; // the owner dropped entry 1 meanwhile
            var saved = StationQueueMath.Save(now, 1, draft);
            Assert.Single(saved);
            Assert.True(saved[0].Keep);
            Assert.Equal(0, StationQueueMath.Find(now, 1, draft));
            Assert.Equal(-1, StationQueueMath.Find(new List<StationQueueEntry> { done }, 0, draft)); // same input, other entry: never overwritten
        }

        [Fact]
        public void DragMoveTo()
        {
            var list = new List<StationQueueEntry>();
            foreach (var id in new[] { "A", "B", "C", "D" }) list.Add(StationQueueMath.NewEntry(id, StationQueueMode.Count, null));
            Assert.True(StationQueueMath.MoveTo(list, 0, 2));
            Assert.Equal("BCAD", string.Concat(list.ConvertAll(e => e.Item)));
            Assert.True(StationQueueMath.MoveTo(list, 3, -5));
            Assert.Equal("DBCA", string.Concat(list.ConvertAll(e => e.Item)));
            Assert.False(StationQueueMath.MoveTo(list, 1, 1));
            Assert.Equal(0, StationQueueMath.SlotAt(-3f, 82f, 0));
            Assert.Equal(3, StationQueueMath.SlotAt(170f, 82f, 1));
        }

        [Theory]
        [InlineData("$piece_charcoalkiln", "loaded_furnace")]
        [InlineData("$piece_smelter", "loaded_furnace")]
        [InlineData("$piece_blastfurnace", "loaded_furnace")]
        [InlineData("$piece_oven", "loaded_furnace")]
        [InlineData("$piece_eitrrefinery", "loaded_machine")]
        [InlineData("$piece_windmill", "loaded_machine")]
        [InlineData("$piece_spinningwheel", "loaded_machine")]
        [InlineData("$piece_fermenter", "loaded_barrel")]
        [InlineData("$piece_cookingstation", "loaded_fire")]
        [InlineData("$piece_bathtub", "loaded")]
        public void LoadedWordPerStation(string token, string key) => Assert.Equal(key, StationQueueMath.LoadedKey(token));

        [Fact]
        public void HeaderLinesMatchMockups()
        {
            var kiln = StationQueueMath.Join(StationQueueMath.Loaded("В печи: {0}/{1}", 12, 25, null), "Уголь в сети: 46",
                "Сеть: 3 " + StationQueueMath.Plural(3, "сундук", "сундука", "сундуков"));
            Assert.Equal("В печи: 12/25 · Уголь в сети: 46 · Сеть: 3 сундука", kiln);
            var eitr = StationQueueMath.Join(StationQueueMath.Loaded("В машине: {0}/{1}", 4, 20, "ткани"), "Живица 9/20", "Работает");
            Assert.Equal("В машине: 4/20 ткани · Живица 9/20 · Работает", eitr);
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

        [Fact]
        public void SelectedModeMarkTakesTheAccent()
        {
            Assert.Equal("<color=#4FE0E6>◉</color>  Пока", StationQueueMath.ModeText(true, "Пока", "4FE0E6"));
            Assert.Equal("○  Пока", StationQueueMath.ModeText(false, "Пока", "4FE0E6"));
        }

        [Fact]
        public void EditorBottomHangsBelowTheQueueWindow()
        {
            var drop = StationQueueMath.EditorDrop(680f, 576f, 12f);
            Assert.Equal(680f / 2f + 12f, drop + 576f / 2f);
        }

        [Fact]
        public void LongTitleStaysBetweenTheKnots()
        {
            float left = 40f, right = 40f;
            WindowHeaderMath.ClearOfKnots(600f, 10f, 10f, 105f, ref left, ref right);
            Assert.Equal(115f, left);
            Assert.Equal(115f, right);
            Assert.True(WindowHeaderMath.FitFont(30f, 408f, 600f - left - right, WindowHeaderMath.TitleMinShare) < 30f);
        }

        // Measured in run 20261118-095140: single window plate 515 of a 604 band, knots 105, title font 30 at 20 units a
        // capital (11.85 at the 60 % floor).
        private const float Band = 604f, Plate = 515f, Knot = 105f, FullFont = 30f, CapWidth = 20f;

        private static (float knot, float room, float font) FitTitle(float plate, string title)
        {
            var natural = title.Length * CapWidth;
            var side = (Band - plate) / 2f;
            var knot = WindowHeaderMath.KnotForTitle(plate, Knot, natural);
            float left = side, right = side;
            WindowHeaderMath.ClearOfKnots(Band, side, side, knot, ref left, ref right);
            var room = Band - left - right;
            return (knot, room, WindowHeaderMath.FitFont(FullFont, natural, room, WindowHeaderMath.TitleMinShare));
        }

        [Theory]
        [InlineData("МАШИНА ДЛЯ ПЕРЕРАБОТКИ ЭЙТРА")]
        [InlineData("ЖЕЛЕЗНАЯ СТОЙКА ДЛЯ ГОТОВКИ")]
        [InlineData("УГЛЕВЫЖИГАТЕЛЬНАЯ ПЕЧЬ")]
        public void LongestStationNamesAreNeverCut(string title)
        {
            var (knot, room, font) = FitTitle(Plate, title);
            Assert.True(title.Length * CapWidth * font / FullFont <= room, $"{title}: font {font}, room {room}");
            Assert.True(knot >= Knot * WindowHeaderMath.KnotMinShare);
        }

        [Fact]
        public void KnotsYieldOnlyWhenTheTitleNeedsTheRoom()
        {
            Assert.True(FitTitle(Plate, "МАШИНА ДЛЯ ПЕРЕРАБОТКИ ЭЙТРА").knot < Knot);
            Assert.Equal(Knot, FitTitle(415f, "ЗАПИСЬ ОЧЕРЕДИ").knot);
            Assert.Equal(Knot, FitTitle(Plate, "ПРЯЛКА").knot);
        }

        [Theory]
        [InlineData(2f)]
        [InlineData(1.5f)]
        public void OutlineCornerClosesTheGapBetweenEdges(float thick)
        {
            const float chamfer = 4f, inset = 0.5f;
            OutlineMath.Corner(chamfer, inset, thick, out var centre, out var length);
            var line = inset + thick / 2f;
            var halfSpan = length / 2f / (float)System.Math.Sqrt(2d); // per-axis reach of the stroke from its centre
            Assert.True(centre - halfSpan <= line, "must reach the edge centre line");
            Assert.True(centre + halfSpan >= chamfer, "must reach where the edge stroke stops");
            Assert.True(length <= (chamfer - line) * System.Math.Sqrt(2d) + thick, "must not run far past the joints");
        }

        [Fact]
        public void KnotsSqueezeOnANarrowPlateAndKeepWiderInsets()
        {
            float left = 12f, right = 150f;
            WindowHeaderMath.ClearOfKnots(120f, 10f, 10f, 105f, ref left, ref right);
            Assert.Equal(60f, left); // plate 100: each knot squeezed to 50
            Assert.Equal(150f, right);
        }
    }
}
