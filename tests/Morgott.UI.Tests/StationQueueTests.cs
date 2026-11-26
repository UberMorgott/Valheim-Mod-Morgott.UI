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
        public void SingleOnlyForOneInputUnlessMultiOnlyOrMultipleEntries()
        {
            Assert.True(StationQueueMath.IsSingle(1));
            Assert.True(StationQueueMath.IsSingle(1, multiOnly: false));
            Assert.True(StationQueueMath.IsSingle(1, multiOnly: false, entryCount: 1));
            Assert.False(StationQueueMath.IsSingle(1, multiOnly: false, entryCount: 2));
            Assert.False(StationQueueMath.IsSingle(1, multiOnly: false, entryCount: 1, editing: true));
            Assert.False(StationQueueMath.IsSingle(1, multiOnly: true));
            Assert.False(StationQueueMath.IsSingle(0, multiOnly: true));
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
        public void FourTabsMapToModeAndKeepIndependently()
        {
            Assert.Equal(-1, StationQueueMath.TabOf(null));

            var make = StationQueueMath.NewEntry("Wood", StationQueueMode.Count, "Coal", keep: false);
            Assert.Equal(StationQueueMath.TabMake, StationQueueMath.TabOf(make));

            var keep = StationQueueMath.NewEntry("Wood", StationQueueMode.Count, "Coal", keep: true);
            Assert.Equal(StationQueueMath.TabKeep, StationQueueMath.TabOf(keep));

            var untilOneShot = StationQueueMath.NewEntry("Wood", StationQueueMode.Until, "Coal", keep: false);
            Assert.Equal(StationQueueMath.TabUntil, StationQueueMath.TabOf(untilOneShot));

            var inf = StationQueueMath.NewEntry("Wood", StationQueueMode.Infinite, "Coal", keep: true);
            Assert.Equal(StationQueueMath.TabInfinite, StationQueueMath.TabOf(inf));

            // Switching tabs on a new draft: Make -> Keep -> Condition (defaults Keep=true) -> Infinite (defaults Keep=true).
            var draft = StationQueueMath.NewEntry("Wood", StationQueueMode.Count, null, keep: false);
            StationQueueMath.SwitchTab(draft, StationQueueMath.TabKeep, "Coal");
            Assert.Equal(StationQueueMode.Count, draft.Mode);
            Assert.True(draft.Keep);

            StationQueueMath.SwitchTab(draft, StationQueueMath.TabUntil, "Coal", untilKeep: true);
            Assert.Equal(StationQueueMode.Until, draft.Mode);
            Assert.True(draft.Keep);
            Assert.Equal("Coal", draft.Condition);

            // Editing an existing Until one-shot preserves Keep=false when revisiting Condition.
            StationQueueMath.SwitchTab(draft, StationQueueMath.TabMake, "Coal");
            Assert.False(draft.Keep);
            StationQueueMath.SwitchTab(draft, StationQueueMath.TabUntil, "Coal", untilKeep: false);
            Assert.Equal(StationQueueMode.Until, draft.Mode);
            Assert.False(draft.Keep);

            StationQueueMath.SwitchTab(draft, StationQueueMath.TabInfinite, "Coal", infiniteKeep: true);
            Assert.Equal(StationQueueMode.Infinite, draft.Mode);
            Assert.True(draft.Keep);
        }

        [Fact]
        public void MakeYieldAndKeepReadoutFormatting()
        {
            var e = StationQueueMath.NewEntry("Recipe_Sausages", StationQueueMode.Count, "Sausages");
            e.Target = 10;
            Assert.Equal("10 раз → 40 шт.", StationQueueMath.MakeYield(10, 4, "{0} раз → {1} шт."));
            Assert.Equal("", StationQueueMath.MakeYield(10, 1, "{0} раз → {1} шт."));
            Assert.Equal("Колбаски ×4: 10 раз → 40 шт.", StationQueueMath.Summary(
                e, "Колбаски ×4", "", 0, "{0}: {1}", "{0}", "", null, null, outputAmount: 4, yieldCountTemplate: "{0}: {1} раз → {2} шт."));
            Assert.Equal("Сейчас: 12 в сундуках (+4 в работе)", StationQueueMath.KeepNowText(12, 4, "Сейчас: {0} в сундуках", "Сейчас: {0} в сундуках (+{1} в работе)"));
            Assert.Equal("Сейчас: 12 в сундуках", StationQueueMath.KeepNowText(12, 0, "Сейчас: {0} в сундуках", "Сейчас: {0} в сундуках (+{1} в работе)"));
        }

        [Fact]
        public void EntryStatusReadsSnapshotWithFallback()
        {
            var map = new Dictionary<int, StationQueueEntryStatus>
            {
                [10] = StationQueueEntryStatus.Stocked,
                [11] = StationQueueEntryStatus.MissingInput,
                [12] = StationQueueEntryStatus.Pending,
                [13] = StationQueueEntryStatus.Blocked,
                [14] = StationQueueEntryStatus.Running,
            };
            Assert.Equal(StationQueueEntryStatus.Stocked, StationQueueMath.EntryStatus(10, 0, 1, StationQueueStatus.Working, map));
            Assert.Equal(StationQueueEntryStatus.MissingInput, StationQueueMath.EntryStatus(11, 1, 1, StationQueueStatus.Waiting, map));
            Assert.Equal(StationQueueEntryStatus.Pending, StationQueueMath.EntryStatus(12, 2, -1, StationQueueStatus.Working, map));
            Assert.Equal(StationQueueEntryStatus.Blocked, StationQueueMath.EntryStatus(13, 3, -1, StationQueueStatus.Working, map));
            Assert.Equal(StationQueueEntryStatus.Running, StationQueueMath.EntryStatus(14, 4, -1, StationQueueStatus.Paused, map));
            // Absent id falls back cleanly when EntryStatuses is empty; when populated, only EntryStatuses[id] == Running highlights.
            Assert.Equal(StationQueueEntryStatus.Running, StationQueueMath.EntryStatus(99, 1, 1, StationQueueStatus.Working, null));
            Assert.Equal(StationQueueEntryStatus.Running, StationQueueMath.EntryStatus(99, 1, 1, StationQueueStatus.Working, new Dictionary<int, StationQueueEntryStatus>()));
            Assert.Equal(StationQueueEntryStatus.Queued, StationQueueMath.EntryStatus(99, 0, 1, StationQueueStatus.Working, map));
            Assert.Equal(StationQueueEntryStatus.Queued, StationQueueMath.EntryStatus(99, 1, 1, StationQueueStatus.Working, map));
            Assert.Equal(StationQueueEntryStatus.Stocked, StationQueueMath.EntryStatus(10, 0, 0, StationQueueStatus.Waiting, map));
            Assert.Equal("st_running", StationQueueMath.StatusKey(StationQueueEntryStatus.Running));
            Assert.Equal("st_missing", StationQueueMath.StatusKey(StationQueueEntryStatus.MissingInput));
            Assert.Equal("st_stocked", StationQueueMath.StatusKey(StationQueueEntryStatus.Stocked));
            Assert.Equal("st_pending", StationQueueMath.StatusKey(StationQueueEntryStatus.Pending));
            Assert.Equal("st_blocked", StationQueueMath.StatusKey(StationQueueEntryStatus.Blocked));
            Assert.Equal("st_queued", StationQueueMath.StatusKey(StationQueueEntryStatus.Queued));
        }

        [Fact]
        public void SingleStatusKeySurfacesRealEntryReasonInsteadOfIdle()
        {
            var e = StationQueueMath.NewEntry("SoftTissue", StationQueueMode.Count, "Eitr");
            e.Id = 7;
            foreach (var pair in new[]
            {
                (StationQueueEntryStatus.MissingInput, "st_missing"),
                (StationQueueEntryStatus.Stocked, "st_stocked"),
                (StationQueueEntryStatus.Pending, "st_pending"),
                (StationQueueEntryStatus.Blocked, "st_blocked"),
                (StationQueueEntryStatus.Running, "st_running"),
            })
            {
                var statuses = new Dictionary<int, StationQueueEntryStatus> { [7] = pair.Item1 };
                Assert.Equal(pair.Item2, StationQueueMath.SingleStatusKey(e, StationQueueStatus.Idle, statuses, out var liveSt));
                Assert.Equal(pair.Item1, liveSt);
            }

            // Paused overrides entry status; Queued / absent / no live entry falls back to station status.
            var missing = new Dictionary<int, StationQueueEntryStatus> { [7] = StationQueueEntryStatus.MissingInput };
            Assert.Equal("status_paused", StationQueueMath.SingleStatusKey(e, StationQueueStatus.Paused, missing, out var pausedSt));
            Assert.Null(pausedSt);
            Assert.Equal("status_idle", StationQueueMath.SingleStatusKey(null, StationQueueStatus.Idle, missing, out var nullSt));
            Assert.Null(nullSt);
            var queued = new Dictionary<int, StationQueueEntryStatus> { [7] = StationQueueEntryStatus.Queued };
            Assert.Equal("status_waiting", StationQueueMath.SingleStatusKey(e, StationQueueStatus.Waiting, queued, out var queuedSt));
            Assert.Null(queuedSt);
        }

        [Fact]
        public void EmptySingleTabAndEditUseLocalDraftWithoutLiveCommit()
        {
            StationQueueEntry draft = null;
            Assert.False(StationQueueMath.SingleTab(null, ref draft, StationQueueMath.TabMake, "SoftTissue", "Eitr", out var step1));
            Assert.NotNull(draft);
            Assert.Same(draft, step1);
            Assert.Equal(StationQueueMode.Count, draft.Mode);
            Assert.Equal(StationQueueMath.DefaultCount, draft.Target);

            // Adjusting count on empty _single edits draft locally without committing.
            Assert.False(StationQueueMath.SingleEdit(null, draft, e => e.Target = 50, out var step2));
            Assert.Equal(50, step2.Target);

            // Switching tabs keeps local draft uncommitted.
            Assert.False(StationQueueMath.SingleTab(null, ref draft, StationQueueMath.TabKeep, "SoftTissue", "Eitr", out _));
            Assert.True(draft.Keep);
            Assert.Equal(50, draft.Target);
            Assert.False(StationQueueMath.SingleTab(null, ref draft, StationQueueMath.TabUntil, "SoftTissue", "Eitr", out _));
            Assert.Equal(StationQueueMode.Until, draft.Mode);
            Assert.Equal("Eitr", draft.Condition);
            Assert.False(StationQueueMath.SingleTab(null, ref draft, StationQueueMath.TabInfinite, "SoftTissue", "Eitr", out _));
            Assert.Equal(StationQueueMode.Infinite, draft.Mode);
            Assert.False(StationQueueMath.SingleTab(null, ref draft, StationQueueMath.TabInfinite, "SoftTissue", "Eitr", out var noop));
            Assert.Null(noop);

            // Once an active entry exists, tab switches and edits commit immediately on a clone.
            var live = StationQueueMath.NewEntry("SoftTissue", StationQueueMode.Count, "Eitr", keep: false);
            live.Id = 1;
            Assert.True(StationQueueMath.SingleTab(live, ref draft, StationQueueMath.TabKeep, "SoftTissue", "Eitr", out var committedTab));
            Assert.True(committedTab.Keep);
            Assert.False(live.Keep);
            Assert.True(StationQueueMath.SingleEdit(live, null, e => e.Target = 40, out var committedEdit));
            Assert.Equal(40, committedEdit.Target);
            Assert.Equal(StationQueueMath.DefaultCount, live.Target);
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
            Assert.Equal(3, StationQueueMath.Save(list, -1, draft).Count); // new entry (index -1) appended
            Assert.Equal(2, StationQueueMath.Save(list, 1, draft).Count); // vanished/shifted edit never duplicates
        }

        [Fact]
        public void SaveFindsTheEditedEntryByIdAfterRowsAboveFinishedAndDropsVanishedEdits()
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
            // If the edited entry itself vanished meanwhile, Save does NOT silently create a duplicate.
            var vanishedSaved = StationQueueMath.Save(new List<StationQueueEntry> { done }, 0, draft);
            Assert.Single(vanishedSaved);
            Assert.Equal(1, vanishedSaved[0].Id);
            Assert.False(vanishedSaved[0].Keep);
            var fresh = StationQueueMath.NewEntry("Wood", StationQueueMode.Count, null); // opened unnumbered, the owner numbered it since
            Assert.Equal(1, StationQueueMath.Find(new List<StationQueueEntry> { done, kept }, 1, fresh));
            var replaced = StationQueueMath.Save(new List<StationQueueEntry> { done, kept }, 1, fresh);
            Assert.Equal(2, replaced.Count); // replaced, no duplicate
            Assert.Equal(2, replaced[1].Id); // keeps the owner's number
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
        [InlineData("$piece_charcoalkiln", "", "loaded_furnace")]
        [InlineData("$piece_smelter", "", "loaded_furnace")]
        [InlineData("$piece_blastfurnace", "", "loaded_furnace")]
        [InlineData("$piece_oven", "", "loaded_furnace")]
        [InlineData("$piece_eitrrefinery", "", "loaded_machine")]
        [InlineData("$piece_windmill", "", "loaded_machine")]
        [InlineData("$piece_spinningwheel", "", "loaded_machine")]
        [InlineData("$piece_fermenter", "", "loaded_barrel")]
        [InlineData("$piece_cookingstation", "", "loaded_fire")]
        [InlineData("$piece_bathtub", "", "loaded")]
        [InlineData("$piece_cauldron", "craft", "ui_sq_crafting")]
        [InlineData("$piece_MeadCauldron", "craft", "ui_sq_crafting")]
        public void LoadedWordPerStation(string token, string kind, string key) =>
            Assert.Equal(key, StationQueueMath.LoadedKey(token, kind));

        [Fact]
        public void MultiIngredientRecipeBindingAndFormatting()
        {
            var recipe = new StationQueueInput(null, null, true, 3, "Recipe_Sausages", 4,
                new[] { new StationQueueIngredient(null, 1, 5) });
            var timed = new StationQueueInput(null, null, true, 12, "Wood", 1, null);
            var inputs = new[] { recipe, timed };

            Assert.Same(recipe, StationQueueMath.FindInput(inputs, "Recipe_Sausages"));
            Assert.Same(timed, StationQueueMath.FindInput(inputs, "Wood"));
            Assert.Null(StationQueueMath.FindInput(inputs, "Recipe_Missing"));
            Assert.Null(StationQueueMath.FindInput(inputs, ""));

            Assert.Equal("Колбаски ×4", StationQueueMath.DisplayName("Колбаски", 4));
            Assert.Equal("Оленье рагу", StationQueueMath.DisplayName("Оленье рагу", 1));
            Assert.Equal("Оленье рагу", StationQueueMath.DisplayName("Оленье рагу", 0));

            var ok = StationQueueMath.IngredientsText(
                new[] { ("Малина", 4, 12), ("Мёд", 2, 8) },
                "A9B3B7", "E0413A");
            Assert.Equal("<color=#A9B3B7>Малина 12/4</color>, <color=#A9B3B7>Мёд 8/2</color>", ok);

            var missing = StationQueueMath.IngredientsText(
                new[] { ("Малина", 4, 12), ("Мёд", 2, 1) },
                null, "E0413A", "Ингредиенты: {0}");
            Assert.Equal("Ингредиенты: Малина 12/4, <color=#E0413A>Мёд 1/2</color>", missing);

            Assert.Equal("", StationQueueMath.IngredientsText(null, "A9B3B7", "E0413A"));
        }

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
            var drop = StationQueueMath.EditorDrop(680f, 526f, 12f);
            Assert.Equal(680f / 2f + 12f, drop + 526f / 2f);
        }

        [Fact]
        public void TabAndCardLabelsHaveTranslationsInBothLanguages()
        {
            var en = LoadTranslations("English");
            var ru = LoadTranslations("Russian");
            string[] keys =
            {
                "ui_sq_tab_make", "ui_sq_tab_keep", "ui_sq_tab_until", "ui_sq_tab_infinite",
                "ui_sq_add", "ui_sq_make_yield", "ui_sq_sum_count_yield",
                "ui_sq_keep_net_label", "ui_sq_keep_fuel_label", "ui_sq_keep_now", "ui_sq_keep_now_pending", "ui_sq_keep_now_fuel",
                "ui_sq_repeat", "ui_sq_oneshot", "ui_sq_choose_mode",
                "ui_sq_st_running", "ui_sq_st_missing", "ui_sq_st_stocked", "ui_sq_st_pending", "ui_sq_st_blocked", "ui_sq_st_queued",
                "ui_sq_start",
            };
            foreach (var k in keys)
            {
                Assert.True(en.TryGetValue(k, out var ev) && !string.IsNullOrWhiteSpace(ev), $"English.json missing {k}");
                Assert.True(ru.TryGetValue(k, out var rv) && !string.IsNullOrWhiteSpace(rv), $"Russian.json missing {k}");
            }
            Assert.Equal("Blocked", en["ui_sq_st_blocked"]);
            Assert.Equal("Заблокировано", ru["ui_sq_st_blocked"]);
            // Tab labels fit 135-unit editor tab plates (4..131 = 127 usable at ~18 px, or auto-sized down to 14 px).
            foreach (var k in new[] { "ui_sq_tab_make", "ui_sq_tab_keep", "ui_sq_tab_until", "ui_sq_tab_infinite" })
            {
                Assert.True(en[k].Length <= 12, $"EN tab '{en[k]}' too long");
                Assert.True(ru[k].Length <= 12, $"RU tab '{ru[k]}' too long");
            }
            // Status chips fit 118-unit status pills (110 usable at 15 px).
            foreach (var k in new[] { "ui_sq_st_running", "ui_sq_st_missing", "ui_sq_st_stocked", "ui_sq_st_pending", "ui_sq_st_blocked", "ui_sq_st_queued" })
            {
                Assert.True(en[k].Length <= 14, $"EN status '{en[k]}' too long");
                Assert.True(ru[k].Length <= 14, $"RU status '{ru[k]}' too long");
            }
        }

        private static Dictionary<string, string> LoadTranslations(string language)
        {
            using var s = typeof(StationQueueTests).Assembly.GetManifestResourceStream($"Morgott.UI.Translations.{language}.json");
            Assert.NotNull(s);
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(s);
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

        [Fact]
        public void PrefersOutputOnlyForRecipesOrMissingInputItem()
        {
            Assert.False(StationQueueMath.PrefersOutput(hasInputItem: true, ingredientCount: 0)); // Kiln Wood/FineWood/RoundLog -> show input, not Coal
            Assert.True(StationQueueMath.PrefersOutput(hasInputItem: true, ingredientCount: 2));  // Cauldron multi-ingredient recipe -> show output dish
            Assert.True(StationQueueMath.PrefersOutput(hasInputItem: false, ingredientCount: 0)); // recipe without single input item -> show output
        }
    }
}
