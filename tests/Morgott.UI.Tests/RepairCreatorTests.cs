using System.Collections.Generic;
using System.Text.Json;
using Morgott.UI.WorldHud;
using Xunit;

namespace Morgott.UI.Tests
{
    public class RepairCreatorTests
    {
        private static Dictionary<string, string> Load(string language)
        {
            using var s = typeof(RepairCreatorTests).Assembly.GetManifestResourceStream($"Morgott.UI.Translations.{language}.json");
            Assert.NotNull(s);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(s);
        }

        private static readonly (string, string)[] History =
        {
            ("ServerOne", "SteamOne"),
            ("", "SteamTwo"),
            ((string)null, (string)null),
        };

        private static (string, string) HistoryAt(int i) => History[i];

        private sealed class FilterStub
        {
            public int Calls;

            public string Filter(string name, long id)
            {
                Calls++;
                return name == "BadName" ? "***" : name == "Blocked" ? "" : name;
            }
        }

        [Fact]
        public void ResolveCreatorNameFollowsExactPriorityOrder()
        {
            // 1. IPlayerNames service wins over everything.
            Assert.Equal("ServiceName", PlayHudMath.ResolveCreatorName(
                creatorId: 100, serviceName: "ServiceName", localPlayerId: 100, localPlayerName: "Local",
                livePlayerName: _ => "Live", History.Length, HistoryAt, 0, "Unknown"));

            // 2. Local player when service has no entry.
            Assert.Equal("Local", PlayHudMath.ResolveCreatorName(
                creatorId: 100, serviceName: null, localPlayerId: 100, localPlayerName: "Local",
                livePlayerName: _ => "Live", History.Length, HistoryAt, 0, "Unknown"));

            // 3. Live Player.s_players match when not local and not in service.
            Assert.Equal("LivePeer", PlayHudMath.ResolveCreatorName(
                creatorId: 200, serviceName: "", localPlayerId: 100, localPlayerName: "Local",
                livePlayerName: id => id == 200 ? "LivePeer" : null, History.Length, HistoryAt, 0, "Unknown"));

            // 4. Vanilla m_playerHistory fallback: prefers server-assigned name, else displayName.
            Assert.Equal("ServerOne", PlayHudMath.ResolveCreatorName(
                creatorId: 300, serviceName: null, localPlayerId: 100, localPlayerName: "Local",
                livePlayerName: _ => null, History.Length, HistoryAt, 0, "Unknown"));
            Assert.Equal("SteamTwo", PlayHudMath.ResolveCreatorName(
                creatorId: 300, serviceName: null, localPlayerId: 100, localPlayerName: "Local",
                livePlayerName: _ => null, History.Length, HistoryAt, 1, "Unknown"));

            // 5. Out-of-bounds or empty history falls back to unknown.
            Assert.Equal("Unknown", PlayHudMath.ResolveCreatorName(
                creatorId: 300, serviceName: null, localPlayerId: 100, localPlayerName: "Local",
                livePlayerName: _ => null, History.Length, HistoryAt, -1, "Unknown"));
            Assert.Equal("Unknown", PlayHudMath.ResolveCreatorName(
                creatorId: 300, serviceName: null, localPlayerId: 100, localPlayerName: "Local",
                livePlayerName: _ => null, History.Length, HistoryAt, 99, "Unknown"));
            Assert.Equal("Unknown", PlayHudMath.ResolveCreatorName(
                creatorId: 300, serviceName: null, localPlayerId: 100, localPlayerName: "Local",
                livePlayerName: _ => null, History.Length, HistoryAt, 2, "Unknown"));
        }

        [Fact]
        public void RepairCreatorFormatsAndTranslatesCleanly()
        {
            var en = Load("English");
            var ru = Load("Russian");
            Assert.True(en.TryGetValue("ui_repair_built_by", out var enFmt));
            Assert.True(ru.TryGetValue("ui_repair_built_by", out var ruFmt));
            Assert.Equal("·  Built by: Ragnar", PlayHudMath.FormatRepairCreator(enFmt, "Ragnar"));
            Assert.Equal("·  Построил: Рагнар", PlayHudMath.FormatRepairCreator(ruFmt, "Рагнар"));
            Assert.Equal("", PlayHudMath.FormatRepairCreator("", "Ragnar"));
        }

        [Fact]
        public void HideRepairCreatorWhenBuildSelectionMenuIsOpen()
        {
            Assert.False(PlayHudMath.ShouldShowRepairCreator(pieceSelectionVisible: true, inRepairMode: true, creatorId: 123));
            Assert.False(PlayHudMath.ShouldShowRepairCreator(pieceSelectionVisible: false, inRepairMode: false, creatorId: 123));
            Assert.False(PlayHudMath.ShouldShowRepairCreator(pieceSelectionVisible: false, inRepairMode: true, creatorId: 0));
            Assert.True(PlayHudMath.ShouldShowRepairCreator(pieceSelectionVisible: false, inRepairMode: true, creatorId: 123));
        }

        [Fact]
        public void ResolveCreatorNameAppliesUgcFilterAndPreservesLiteralMarkup()
        {
            var stub = new FilterStub();

            Assert.Equal("***", PlayHudMath.ResolveCreatorName(
                200, "BadName", 100, "Local", _ => null, 0, null, -1, "Unknown", stub.Filter));
            Assert.Equal(1, stub.Calls);

            // Empty filtered name falls back to unknown; unknown fallback itself is not passed to UGC filter.
            Assert.Equal("Unknown", PlayHudMath.ResolveCreatorName(
                200, "Blocked", 100, "Local", _ => null, 0, null, -1, "Unknown", stub.Filter));
            Assert.Equal(2, stub.Calls);
            Assert.Equal("Unknown", PlayHudMath.ResolveCreatorName(
                200, null, 100, "Local", _ => null, 0, null, -1, "Unknown", stub.Filter));
            Assert.Equal(2, stub.Calls);

            // Literal TMP tags and braces in player names format without exceptions (rendered plain via richText = false).
            var tagged = "<color=red><size=500>{Troll}</size></color>";
            Assert.Equal("·  Built by: " + tagged, PlayHudMath.FormatRepairCreator("Built by: {0}", tagged));
        }
    }
}
