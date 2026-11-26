using System;
using Morgott.Core;
using Morgott.UI.MapScreen;
using Xunit;

namespace Morgott.UI.Tests
{
    public class MinimapSeasonTests
    {
        private static SeasonSnapshot MakeSnapshot(
            Season season,
            float progress,
            double secondsRemaining = 3600.0,
            SeasonWeatherEvent ev = SeasonWeatherEvent.None,
            float intensity = 0f) =>
            new SeasonSnapshot(season, progress, secondsRemaining, 18000.0, ev, intensity, 0.0, 0f, 0f, false, 0f, 0f, 0);

        [Theory]
        [InlineData(Season.Spring, "Morgott.UI.Seasons.minimap_spring.png", "$ui_season_spring")]
        [InlineData(Season.Summer, "Morgott.UI.Seasons.minimap_summer.png", "$ui_season_summer")]
        [InlineData(Season.Fall, "Morgott.UI.Seasons.minimap_fall.png", "$ui_season_fall")]
        [InlineData(Season.Winter, "Morgott.UI.Seasons.minimap_winter.png", "$ui_season_winter")]
        public void SelectsCorrectOverlayAndKeyForAllFourSeasons(Season season, string resource, string key)
        {
            Assert.Equal(resource, MinimapSeasonRules.ResourceName(season));
            Assert.Equal(key, MinimapSeasonRules.SeasonKey(season));

            var state = MinimapSeasonRules.Evaluate(MakeSnapshot(season, 0.5f), biome: 1, showOverlay: true);
            Assert.True(state.HasPrimary);
            Assert.Equal(season, state.PrimarySeason);
            Assert.Equal(1f, state.PrimaryAlpha, 4);
            Assert.False(state.HasSecondary);
        }

        [Fact]
        public void CrossFadeWeightsRemainContinuousAcrossSeasonBoundaries()
        {
            var seasons = new[] { Season.Spring, Season.Summer, Season.Fall, Season.Winter };
            for (var i = 0; i < seasons.Length; i++)
            {
                var cur = seasons[i];
                var next = seasons[(i + 1) & 3];
                var endState = MinimapSeasonRules.Evaluate(MakeSnapshot(cur, 1f), biome: 1, showOverlay: true);
                var startState = MinimapSeasonRules.Evaluate(MakeSnapshot(next, 0f), biome: 1, showOverlay: true);

                Assert.Equal(0.5f, endState.PrimaryAlpha, 3);
                Assert.Equal(0.5f, endState.SecondaryAlpha, 3);
                Assert.Equal(0.5f, startState.PrimaryAlpha, 3);
                Assert.Equal(0.5f, startState.SecondaryAlpha, 3);

                MinimapSeasonRules.ComputeCrossFadeWeights(cur, 0.96f, false, out var s0, out var su0, out var f0, out var w0);
                var sum0 = s0 + su0 + f0 + w0;
                Assert.Equal(1f, sum0, 4);

                MinimapSeasonRules.ComputeCrossFadeWeights(next, 0.04f, false, out var s1, out var su1, out var f1, out var w1);
                var sum1 = s1 + su1 + f1 + w1;
                Assert.Equal(1f, sum1, 4);
            }
        }

        [Fact]
        public void AshlandsSuppressesWinterSnowCapAndBlizzardRim()
        {
            const int ashlands = MinimapSeasonRules.BiomeAshLands;
            var winterAsh = MinimapSeasonRules.Evaluate(
                MakeSnapshot(Season.Winter, 0.5f, ev: SeasonWeatherEvent.Blizzard, intensity: 1f),
                ashlands,
                showOverlay: true);
            Assert.False(winterAsh.HasPrimary);
            Assert.False(winterAsh.HasSecondary);
            Assert.False(winterAsh.HasBlizzard);

            // Late fall in Ashlands fades fall toward 0 without bringing up winter snow.
            MinimapSeasonRules.ComputeCrossFadeWeights(Season.Fall, 0.96f, ashlands: true, out _, out _, out var fall, out var winter);
            Assert.True(fall > 0.5f && fall < 1f);
            Assert.Equal(0f, winter);

            // Non-winter seasonal overlays remain visible in Ashlands when Spring arrives.
            var springAsh = MinimapSeasonRules.Evaluate(MakeSnapshot(Season.Spring, 0.5f), ashlands, showOverlay: true);
            Assert.True(springAsh.HasPrimary);
            Assert.Equal(Season.Spring, springAsh.PrimarySeason);
            Assert.Equal(1f, springAsh.PrimaryAlpha, 4);
        }

        [Fact]
        public void BlizzardRimAlphaTracksEventIntensityAndSuppressesWhenOff()
        {
            Assert.Equal(0f, MinimapSeasonRules.ComputeBlizzardAlpha(MakeSnapshot(Season.Winter, 0.5f, ev: SeasonWeatherEvent.None, intensity: 1f), 1));
            Assert.Equal(0f, MinimapSeasonRules.ComputeBlizzardAlpha(MakeSnapshot(Season.Winter, 0.5f, ev: SeasonWeatherEvent.Thaw, intensity: 1f), 1));
            Assert.Equal(0f, MinimapSeasonRules.ComputeBlizzardAlpha(MakeSnapshot(Season.Winter, 0.5f, ev: SeasonWeatherEvent.Blizzard, intensity: 0f), 1));

            var low = MinimapSeasonRules.ComputeBlizzardAlpha(MakeSnapshot(Season.Winter, 0.5f, ev: SeasonWeatherEvent.Blizzard, intensity: 0.25f), 1);
            var mid = MinimapSeasonRules.ComputeBlizzardAlpha(MakeSnapshot(Season.Winter, 0.5f, ev: SeasonWeatherEvent.Blizzard, intensity: 0.50f), 1);
            var high = MinimapSeasonRules.ComputeBlizzardAlpha(MakeSnapshot(Season.Winter, 0.5f, ev: SeasonWeatherEvent.Blizzard, intensity: 1.00f), 1);

            Assert.True(low > 0.25f && low < mid);
            Assert.True(mid < high);
            Assert.Equal(1f, high, 4);

            var hidden = MinimapSeasonRules.Evaluate(MakeSnapshot(Season.Winter, 0.5f, ev: SeasonWeatherEvent.Blizzard, intensity: 1f), 1, showOverlay: false);
            Assert.False(hidden.HasPrimary);
            Assert.False(hidden.HasBlizzard);
        }

        [Fact]
        public void TimerFormattingHandlesZeroSecondsMinutesHoursDaysAndEvents()
        {
            Assert.Equal("0с", MinimapSeasonRules.FormatDuration(0, "д", "ч", "м", "с"));
            Assert.Equal("0s", MinimapSeasonRules.FormatDuration(-5, "d", "h", "m", "s"));
            Assert.Equal("45с", MinimapSeasonRules.FormatDuration(44.2, "д", "ч", "м", "с"));
            Assert.Equal("5м 30с", MinimapSeasonRules.FormatDuration(330, "д", "ч", "м", "с"));
            Assert.Equal("4ч 12м", MinimapSeasonRules.FormatDuration(4 * 3600 + 12 * 60, "д", "ч", "м", "с"));
            Assert.Equal("2d 3h", MinimapSeasonRules.FormatDuration(2 * 86400 + 3 * 3600, "d", "h", "m", "s"));
            Assert.Equal("Зима · 4ч 12м", MinimapSeasonRules.FormatTimerLine("Зима", 4 * 3600 + 12 * 60, "д", "ч", "м", "с"));
            Assert.Equal("Winter (Blizzard) · 12m", MinimapSeasonRules.FormatTimerLine("Winter", 720, "d", "h", "m", "s", "Blizzard"));
        }
    }
}
