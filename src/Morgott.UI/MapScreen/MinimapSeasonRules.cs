using System;
using System.Globalization;
using Morgott.Core;

namespace Morgott.UI.MapScreen
{
    public readonly struct SeasonOverlayState
    {
        public Season PrimarySeason { get; }
        public float PrimaryAlpha { get; }
        public Season SecondarySeason { get; }
        public float SecondaryAlpha { get; }
        public float BlizzardAlpha { get; }
        public float ThawAlpha { get; }

        public bool HasPrimary => PrimaryAlpha > 0.001f;
        public bool HasSecondary => SecondaryAlpha > 0.001f;
        public bool HasBlizzard => BlizzardAlpha > 0.001f;
        public bool HasThaw => ThawAlpha > 0.001f;

        public SeasonOverlayState(
            Season primarySeason,
            float primaryAlpha,
            Season secondarySeason,
            float secondaryAlpha,
            float blizzardAlpha,
            float thawAlpha = 0f)
        {
            PrimarySeason = primarySeason;
            PrimaryAlpha = primaryAlpha;
            SecondarySeason = secondarySeason;
            SecondaryAlpha = secondaryAlpha;
            BlizzardAlpha = blizzardAlpha;
            ThawAlpha = thawAlpha;
        }
    }

    // Pure seasonal minimap ring overlay math and timer formatting (Unity-free, unit-tested).
    public static class MinimapSeasonRules
    {
        public const float TransitionWindow = 0.08f;
        public const int BiomeAshLands = 32; // Heightmap.Biome.AshLands (Heightmap.cs:24)
        public const string BlizzardResourceName = "Morgott.UI.Seasons.minimap_blizzard_rim.png";
        public const string ThawResourceName = "Morgott.UI.Seasons.minimap_thaw_rim.png";

        public static bool IsAshlands(int biome) => (biome & BiomeAshLands) != 0;

        public static string ResourceName(Season season)
        {
            switch (Normalize(season))
            {
                case Season.Spring: return "Morgott.UI.Seasons.minimap_spring.png";
                case Season.Summer: return "Morgott.UI.Seasons.minimap_summer.png";
                case Season.Fall: return "Morgott.UI.Seasons.minimap_fall.png";
                default: return "Morgott.UI.Seasons.minimap_winter.png";
            }
        }

        public static string SeasonKey(Season season)
        {
            switch (Normalize(season))
            {
                case Season.Spring: return "$ui_season_spring";
                case Season.Summer: return "$ui_season_summer";
                case Season.Fall: return "$ui_season_fall";
                default: return "$ui_season_winter";
            }
        }

        // C1-continuous cross-fade across all 4 season boundaries ([1 - TransitionWindow .. 1] -> [0 .. TransitionWindow]).
        // In AshLands the winter snow cap weight is suppressed to 0 while temperate seasons keep their own fade.
        public static void ComputeCrossFadeWeights(
            Season season,
            float progress,
            bool ashlands,
            out float spring,
            out float summer,
            out float fall,
            out float winter)
        {
            var s = (int)Normalize(season);
            var p = Clamp01(progress);
            var w0 = 0f;
            var w1 = 0f;
            var w2 = 0f;
            var w3 = 0f;

            void Add(int idx, float val)
            {
                switch (idx & 3)
                {
                    case 0: w0 += val; break;
                    case 1: w1 += val; break;
                    case 2: w2 += val; break;
                    default: w3 += val; break;
                }
            }

            if (p < TransitionWindow)
            {
                var t = SmoothStep(0f, TransitionWindow, p);
                Add(s, 0.5f + 0.5f * t);
                Add(s + 3, 0.5f - 0.5f * t);
            }
            else if (p > 1f - TransitionWindow)
            {
                var t = SmoothStep(1f - TransitionWindow, 1f, p);
                Add(s, 1f - 0.5f * t);
                Add(s + 1, 0.5f * t);
            }
            else
            {
                Add(s, 1f);
            }

            if (ashlands) w3 = 0f;

            spring = w0;
            summer = w1;
            fall = w2;
            winter = w3;
        }

        public static float ComputeBlizzardAlpha(SeasonSnapshot snapshot, int biome) =>
            ComputeEventAlpha(snapshot, biome, SeasonWeatherEvent.Blizzard);

        public static float ComputeThawAlpha(SeasonSnapshot snapshot, int biome) =>
            ComputeEventAlpha(snapshot, biome, SeasonWeatherEvent.Thaw);

        private static float ComputeEventAlpha(SeasonSnapshot snapshot, int biome, SeasonWeatherEvent targetEvent)
        {
            if (IsAshlands(biome) || snapshot.ActiveEvent != targetEvent) return 0f;
            var intensity = Clamp01(snapshot.EventIntensity);
            if (intensity <= 0f) return 0f;
            // Subtle boost at mid-low intensity so translucent frost/thaw accents stay crisp over winter snow.
            return Clamp01(intensity * 0.85f + (float)Math.Sqrt(intensity) * 0.15f);
        }

        public static SeasonOverlayState Evaluate(SeasonSnapshot snapshot, int biome, bool showOverlay)
        {
            if (!showOverlay) return default;
            var ashlands = IsAshlands(biome);
            ComputeCrossFadeWeights(snapshot.Season, snapshot.Progress, ashlands,
                out var w0, out var w1, out var w2, out var w3);
            PickTopTwo(w0, w1, w2, w3, out var primary, out var primaryAlpha, out var secondary, out var secondaryAlpha);
            var blizzard = ComputeBlizzardAlpha(snapshot, biome);
            var thaw = ComputeThawAlpha(snapshot, biome);
            return new SeasonOverlayState(primary, primaryAlpha, secondary, secondaryAlpha, blizzard, thaw);
        }

        public static void PickTopTwo(
            float spring,
            float summer,
            float fall,
            float winter,
            out Season primarySeason,
            out float primaryAlpha,
            out Season secondarySeason,
            out float secondaryAlpha)
        {
            var pSeason = Season.Spring;
            var pAlpha = 0f;
            var sSeason = Season.Spring;
            var sAlpha = 0f;

            void Consider(Season s, float w)
            {
                if (!(w > 0f)) return;
                if (w > pAlpha)
                {
                    sSeason = pSeason;
                    sAlpha = pAlpha;
                    pSeason = s;
                    pAlpha = w;
                }
                else if (w > sAlpha)
                {
                    sSeason = s;
                    sAlpha = w;
                }
            }

            Consider(Season.Spring, Clamp01(spring));
            Consider(Season.Summer, Clamp01(summer));
            Consider(Season.Fall, Clamp01(fall));
            Consider(Season.Winter, Clamp01(winter));

            primarySeason = pSeason;
            primaryAlpha = pAlpha;
            secondarySeason = sSeason;
            secondaryAlpha = sAlpha;
        }

        public static float StepFade(float current, float target, float dt, float durationSeconds = 0.8f)
        {
            var cur = Clamp01(current);
            var tgt = Clamp01(target);
            if (!(dt > 0f) || float.IsNaN(dt) || float.IsInfinity(dt)) return cur;
            if (!(durationSeconds > 0f)) return tgt;
            var step = dt / durationSeconds;
            if (cur < tgt) return Math.Min(tgt, cur + step);
            if (cur > tgt) return Math.Max(tgt, cur - step);
            return tgt;
        }

        public static string FormatDuration(double secondsRemaining, string dayUnit, string hourUnit, string minUnit, string secUnit)
        {
            var sUnit = secUnit ?? "s";
            if (double.IsNaN(secondsRemaining) || double.IsInfinity(secondsRemaining) || secondsRemaining <= 0.0)
                return "0" + sUnit;
            var total = (long)Math.Ceiling(secondsRemaining);
            if (total <= 0L) return "0" + sUnit;

            var dUnit = dayUnit ?? "d";
            var hUnit = hourUnit ?? "h";
            var mUnit = minUnit ?? "m";

            var days = total / 86400L;
            var hours = (total % 86400L) / 3600L;
            var mins = (total % 3600L) / 60L;
            var secs = total % 60L;

            if (days > 0)
                return hours > 0
                    ? days.ToString(CultureInfo.InvariantCulture) + dUnit + " " + hours.ToString(CultureInfo.InvariantCulture) + hUnit
                    : days.ToString(CultureInfo.InvariantCulture) + dUnit;
            if (hours > 0)
                return mins > 0
                    ? hours.ToString(CultureInfo.InvariantCulture) + hUnit + " " + mins.ToString(CultureInfo.InvariantCulture) + mUnit
                    : hours.ToString(CultureInfo.InvariantCulture) + hUnit;
            if (mins > 0)
                return secs > 0
                    ? mins.ToString(CultureInfo.InvariantCulture) + mUnit + " " + secs.ToString(CultureInfo.InvariantCulture) + sUnit
                    : mins.ToString(CultureInfo.InvariantCulture) + mUnit;
            return secs.ToString(CultureInfo.InvariantCulture) + sUnit;
        }

        public static string FormatTimerLine(
            string seasonName,
            double secondsRemaining,
            string dayUnit,
            string hourUnit,
            string minUnit,
            string secUnit,
            string eventLabel = null)
        {
            var head = seasonName ?? "";
            if (!string.IsNullOrEmpty(eventLabel))
                head = head + " (" + eventLabel + ")";
            var dur = FormatDuration(secondsRemaining, dayUnit, hourUnit, minUnit, secUnit);
            return string.IsNullOrEmpty(head) ? dur : head + " · " + dur;
        }

        private static Season Normalize(Season s) => (Season)(((int)s % 4 + 4) & 3);

        private static float Clamp01(float v) => float.IsNaN(v) || v < 0f ? 0f : v > 1f ? 1f : v;

        private static float SmoothStep(float edge0, float edge1, float x)
        {
            if (edge1 <= edge0) return x >= edge1 ? 1f : 0f;
            var t = Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}
