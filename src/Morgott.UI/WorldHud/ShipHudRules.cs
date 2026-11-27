using System;
using System.Globalization;

namespace Morgott.UI.WorldHud
{
    public enum ShipSpeedUnit
    {
        MetersPerSecond,
        KilometersPerHour
    }

    public enum WindEfficiencyZone
    {
        DeadZone,
        CloseHauled,
        Optimal,
        Tailwind
    }

    public readonly struct WindArcSegment
    {
        public WindEfficiencyZone Zone { get; }
        public float StartDeg { get; }
        public float SpanDeg { get; }
        public string ColorHex { get; }

        public WindArcSegment(WindEfficiencyZone zone, float startDeg, float spanDeg, string colorHex)
        {
            Zone = zone;
            StartDeg = startDeg;
            SpanDeg = spanDeg;
            ColorHex = colorHex;
        }
    }

    // Pure ship wind efficiency arc & live speed/thrust math (mirrors Ship.GetWindAngleFactor / GetWindAngle).
    public static class ShipHudRules
    {
        // Ship.GetWindAngleFactor() drops to 0 when cos(angle) >= 0.80 (acos(0.8) ≈ 36.8699 deg from pure headwind).
        public const float DeadZoneHalfAngleDeg = 36.87f;
        public const float CloseHauledEndDeg = 60f;
        public const float OptimalEndDeg = 120f;
        private const float ArcHalfGapDeg = 1f;
        public const float ReadoutGap = 6f;
        public const float ReadoutLineHeight = 20f;
        public const float ReadoutLineGap = 2f;

        public static readonly float ReadoutBlockHeight = ReadoutGap + 2f * ReadoutLineHeight + ReadoutLineGap;

        // Dial segments clockwise from bow (12 o'clock = 0 deg) with 2 deg gaps centered on each zone boundary.
        public static readonly WindArcSegment[] ArcSegments =
        {
            new WindArcSegment(WindEfficiencyZone.DeadZone, -(DeadZoneHalfAngleDeg - ArcHalfGapDeg), 2f * (DeadZoneHalfAngleDeg - ArcHalfGapDeg), "#E0413AFF"),
            new WindArcSegment(WindEfficiencyZone.CloseHauled, DeadZoneHalfAngleDeg + ArcHalfGapDeg, (CloseHauledEndDeg - ArcHalfGapDeg) - (DeadZoneHalfAngleDeg + ArcHalfGapDeg), "#F08A3AFF"),
            new WindArcSegment(WindEfficiencyZone.Optimal, CloseHauledEndDeg + ArcHalfGapDeg, (OptimalEndDeg - ArcHalfGapDeg) - (CloseHauledEndDeg + ArcHalfGapDeg), "#38D47AFF"),
            new WindArcSegment(WindEfficiencyZone.Tailwind, OptimalEndDeg + ArcHalfGapDeg, (360f - OptimalEndDeg - ArcHalfGapDeg) - (OptimalEndDeg + ArcHalfGapDeg), "#E8B64AFF"),
            new WindArcSegment(WindEfficiencyZone.Optimal, 360f - OptimalEndDeg + ArcHalfGapDeg, (360f - CloseHauledEndDeg - ArcHalfGapDeg) - (360f - OptimalEndDeg + ArcHalfGapDeg), "#38D47AFF"),
            new WindArcSegment(WindEfficiencyZone.CloseHauled, 360f - CloseHauledEndDeg + ArcHalfGapDeg, (360f - DeadZoneHalfAngleDeg - ArcHalfGapDeg) - (360f - CloseHauledEndDeg + ArcHalfGapDeg), "#F08A3AFF"),
        };

        public static float WrapSigned180(float deg)
        {
            if (float.IsNaN(deg) || float.IsInfinity(deg)) return 0f;
            var a = (deg % 360f + 360f) % 360f;
            return a > 180f ? a - 360f : a;
        }

        // Clockwise angle on the WindIndicator dial from the ship bow (12 o'clock = headwind, +/-90 = beam, 180 = astern),
        // derived from Ship.GetWindAngle() = -YawFromDirection(shipLocalWind).
        public static float AngleFromBowDeg(float shipWindAngleDeg) => WrapSigned180(180f - shipWindAngleDeg);

        // Exact Ship.GetWindAngleFactor (Ship.cs:507-513) evaluated at dial angleFromBowDeg (0 = headwind, 180 = tailwind).
        public static float WindAngleFactor(float angleFromBowDeg)
        {
            if (float.IsNaN(angleFromBowDeg) || float.IsInfinity(angleFromBowDeg)) return 0f;
            var num = (float)Math.Cos(angleFromBowDeg * (Math.PI / 180.0));
            var num2 = 0.7f + 0.3f * (1f - Math.Abs(num));
            var t = Clamp01((num - 0.75f) / 0.05f);
            return num2 * (1f - t);
        }

        public static int ThrustPercent(float windAngleFactor)
        {
            var pct = (int)Math.Round(Clamp01(windAngleFactor) * 100f, MidpointRounding.AwayFromZero);
            return pct < 0 ? 0 : pct > 100 ? 100 : pct;
        }

        public static WindEfficiencyZone ClassifyZone(float angleFromBowDeg)
        {
            var abs = Math.Abs(WrapSigned180(angleFromBowDeg));
            if (abs <= DeadZoneHalfAngleDeg) return WindEfficiencyZone.DeadZone;
            if (abs < CloseHauledEndDeg) return WindEfficiencyZone.CloseHauled;
            if (abs <= OptimalEndDeg) return WindEfficiencyZone.Optimal;
            return WindEfficiencyZone.Tailwind;
        }

        public static float HorizontalSpeed(float vx, float vz)
        {
            if (float.IsNaN(vx) || float.IsNaN(vz) || float.IsInfinity(vx) || float.IsInfinity(vz)) return 0f;
            return (float)Math.Sqrt((double)vx * vx + (double)vz * vz);
        }

        public static float StepSmoothedSpeed(float current, float target, float dt, float smoothTime = 0.25f)
        {
            if (float.IsNaN(current) || float.IsInfinity(current) || current < 0f) current = 0f;
            if (float.IsNaN(target) || float.IsInfinity(target) || target < 0f) target = 0f;
            if (!(dt > 0f) || float.IsNaN(dt) || float.IsInfinity(dt)) return current;
            var alpha = 1f - (float)Math.Exp(-dt / Math.Max(0.01f, smoothTime));
            var next = current + (target - current) * alpha;
            return next < 0.03f && target < 0.03f ? 0f : next;
        }

        public static float ConvertSpeed(float metersPerSecond, ShipSpeedUnit unit)
        {
            var v = float.IsNaN(metersPerSecond) || float.IsInfinity(metersPerSecond) || metersPerSecond < 0f ? 0f : metersPerSecond;
            return unit == ShipSpeedUnit.KilometersPerHour ? v * 3.6f : v;
        }

        public static string FormatSpeed(float metersPerSecond, ShipSpeedUnit unit, string msUnit, string kmhUnit)
        {
            var clean = float.IsNaN(metersPerSecond) || float.IsInfinity(metersPerSecond) || metersPerSecond < 0.05f ? 0f : metersPerSecond;
            var val = ConvertSpeed(clean, unit);
            var label = unit == ShipSpeedUnit.KilometersPerHour ? (kmhUnit ?? "km/h") : (msUnit ?? "m/s");
            return val.ToString("0.0", CultureInfo.InvariantCulture) + " " + label;
        }

        // Ship.Speed: Stop 0, Back 1, Slow 2, Half 3, Full 4.
        public static string FormatThrust(int speedSetting, float windAngleFactor, string thrustTemplate, string oarsText, string calmText)
        {
            if (speedSetting == 3 || speedSetting == 4)
            {
                var pct = ThrustPercent(windAngleFactor);
                var tpl = string.IsNullOrEmpty(thrustTemplate) ? "Sail thrust {0}%" : thrustTemplate;
                return tpl.IndexOf("{0}", StringComparison.Ordinal) >= 0
                    ? string.Format(CultureInfo.InvariantCulture, tpl, pct.ToString(CultureInfo.InvariantCulture))
                    : tpl + " " + pct.ToString(CultureInfo.InvariantCulture) + "%";
            }
            if (speedSetting == 1 || speedSetting == 2) return oarsText ?? "Oars";
            return calmText ?? "Calm";
        }

        private static float Clamp01(float v) => float.IsNaN(v) || v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
