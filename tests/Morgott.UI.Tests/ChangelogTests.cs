using System;
using System.Linq;
using Morgott.UI.PreWorld;
using Xunit;

namespace Morgott.UI.Tests
{
    // Pack changelog window (ChangelogModel): parse, filters, counts, grouping.
    public class ChangelogTests
    {
        private static string E(string id, string date, string mod, string ver, string type, string en, string ru = null) =>
            $"{{\"id\":\"{id}\",\"date\":\"{date}\",\"mod\":\"{mod}\",\"version\":\"{ver}\",\"type\":\"{type}\",\"text\":{{\"en\":\"{en}\"{(ru != null ? $",\"ru\":\"{ru}\"" : "")}}},\"src\":\"tag\"}}";

        private static string File(params string[] entries) => "{\"schemaVersion\":1,\"generated\":\"2026-11-16T00:00:00Z\",\"entries\":[" + string.Join(",", entries) + "]}";

        private static readonly string Sample = File(
            E("a", "2026-11-16", "Morgott.UI", "0.29.40", "feature", "Changelog window", "Окно изменений"),
            E("b", "2026-11-16", "Morgott.UI", "0.29.40", "fix", "Tooltip flicker"),
            E("c", "2026-11-16", "ValheimPlus", "", "balance", "Honey 4 -> 10"),
            E("d", "2026-11-15", "EpicLoot", "", "mods", "EpicLoot 0.11.4 -> 0.11.5"),
            E("e", "2026-11-15", "Morgott.Performance", "0.18.3", "perf", "Fewer hitches"),
            E("f", "2026-10-01", "Morgott.UI", "0.20.0", "change", "Old change"),
            E("x", "2026-10-01", "Morgott.UI", "", "chore", "dropped: unknown type"),
            E("y", "bad", "Morgott.UI", "", "fix", "dropped: bad date"));

        [Fact]
        public void ParsesKnownEntriesNewestFirst()
        {
            var all = ChangelogModel.Parse(Sample, out var error);
            Assert.Null(error);
            Assert.Equal(new[] { "a", "b", "c", "d", "e", "f" }, all.Select(e => e.Id));
            Assert.Equal("Окно изменений", all[0].Text(true));
            Assert.Equal("Tooltip flicker", all[1].Text(true)); // no ru: en
            Assert.Equal("Changelog window", all[0].Text(false));
        }

        [Fact]
        public void SortsByDateKeepingFileOrderInsideADay()
        {
            var all = ChangelogModel.Parse(File(E("o", "2026-01-01", "M", "", "fix", "old"), E("n1", "2026-02-01", "M", "", "fix", "n1"), E("n2", "2026-02-01", "M", "", "fix", "n2")), out _);
            Assert.Equal(new[] { "n1", "n2", "o" }, all.Select(e => e.Id));
        }

        [Theory]
        [InlineData("")]
        [InlineData("{not json")]
        [InlineData("[1,2]")]
        [InlineData("{\"schemaVersion\":2,\"entries\":[]}")]
        [InlineData("{\"entries\":[]}")]
        public void BadFilesGiveAnErrorNotAnException(string json)
        {
            Assert.Null(ChangelogModel.Parse(json, out var error));
            Assert.NotNull(error);
        }

        [Fact]
        public void GroupsDateThenModVersionThenLines()
        {
            var v = ChangelogModel.Build(ChangelogModel.Parse(Sample, out _), new ChangelogFilter());
            var shape = string.Join(" ", v.Rows.Select(r => r.Kind == ChangelogRowKind.Date ? "D" : r.Kind == ChangelogRowKind.Mod ? "M:" + r.Entry.Mod : "l"));
            Assert.Equal("D M:Morgott.UI l l M:ValheimPlus l D M:EpicLoot l M:Morgott.Performance l D M:Morgott.UI l", shape);
            Assert.Equal(6, v.Shown);
            Assert.Equal(6, v.Total);
        }

        [Fact]
        public void PeriodIsInclusive()
        {
            var all = ChangelogModel.Parse(Sample, out _);
            var f = new ChangelogFilter { From = new DateTime(2026, 11, 15), To = new DateTime(2026, 11, 15) };
            var v = ChangelogModel.Build(all, f);
            Assert.Equal(2, v.Shown);
            Assert.All(v.Rows, r => Assert.Equal(new DateTime(2026, 11, 15), r.Entry.Date));
        }

        [Fact]
        public void CountsRespectTheOtherFilters()
        {
            var all = ChangelogModel.Parse(Sample, out _);
            var f = new ChangelogFilter();
            f.Types[1] = false; // fix off
            f.HiddenMods.Add("ValheimPlus");
            var v = ChangelogModel.Build(all, f);
            // Type counts ignore the type filter, honour the mod filter: balance (ValheimPlus) 0, fix still 1.
            Assert.Equal(new[] { 1, 1, 1, 0, 1, 1 }, v.TypeCounts);
            // Mod counts ignore the mod filter, honour the type filter: Morgott.UI loses its fix, ValheimPlus still 1.
            Assert.Equal(2, v.ModCounts["Morgott.UI"]);
            Assert.Equal(1, v.ModCounts["ValheimPlus"]);
            Assert.Equal(4, v.Shown);
            Assert.DoesNotContain(v.Rows, r => r.Entry.Mod == "ValheimPlus" || r.Entry.Type == 1);
        }

        [Fact]
        public void ModsAreDistinctAndSorted()
        {
            Assert.Equal(new[] { "EpicLoot", "Morgott.Performance", "Morgott.UI", "ValheimPlus" }, ChangelogModel.Mods(ChangelogModel.Parse(Sample, out _)));
        }

        [Fact]
        public void FormatsDatesAndCounts()
        {
            Assert.Equal("16 ноября 2026", ChangelogModel.DateHeader(new DateTime(2026, 11, 16), true));
            Assert.Equal("16 November 2026", ChangelogModel.DateHeader(new DateTime(2026, 11, 16), false));
            Assert.True(ChangelogModel.TryParseDay(" 01.11.2026 ", out var d));
            Assert.Equal(new DateTime(2026, 11, 1), d);
            Assert.False(ChangelogModel.TryParseDay("2026-11-01", out _));
            Assert.Equal("01.11.2026", ChangelogModel.Day(d));
            Assert.Equal("2 341", ChangelogModel.Count(2341));
        }

        [Fact]
        public void HandlesAThousandsOfEntriesQuickly()
        {
            var entries = Enumerable.Range(0, 2600).Select(i => E("i" + i, new DateTime(2026, 11, 16).AddDays(-(i / 10)).ToString("yyyy-MM-dd"), "Mod" + (i % 30), "1." + (i / 50), ChangelogModel.Types[i % 6], "Entry " + i)).ToArray();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var all = ChangelogModel.Parse(File(entries), out var error);
            var v = ChangelogModel.Build(all, new ChangelogFilter());
            sw.Stop();
            Assert.Null(error);
            Assert.Equal(2600, v.Shown);
            Assert.True(sw.ElapsedMilliseconds < 500, $"parse+build {sw.ElapsedMilliseconds} ms");
        }
    }
}
