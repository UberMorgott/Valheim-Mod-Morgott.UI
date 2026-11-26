using System;
using Morgott.UI.WorldHud;
using Xunit;

namespace Morgott.UI.Tests
{
    public class ShipHudRulesTests
    {
        [Theory]
        [InlineData(-180f, 0f)]   // headwind -> arrow at bow (12 o'clock)
        [InlineData(180f, 0f)]
        [InlineData(0f, 180f)]    // tailwind -> arrow at stern (6 o'clock)
        [InlineData(-90f, 90f)]   // wind from port -> shipWindAngle -90 -> wait: 180 - (-90) = -90 (port), +90 -> +90 (starboard)
        [InlineData(90f, 90f)]
        public void WindAngleFromBowMatchesValheimCompassGeometry(float shipWindAngle, float expectedAbsBowDeg)
        {
            var bowDeg = ShipHudRules.AngleFromBowDeg(shipWindAngle);
            Assert.Equal(expectedAbsBowDeg, Math.Abs(bowDeg), 3);
        }

        [Theory]
        [InlineData(0f, 0)]     // pure headwind dead zone
        [InlineData(30f, 0)]    // inside cos >= 0.80 dead zone
        [InlineData(-35f, 0)]   // port dead zone
        [InlineData(36.8f, 0)]  // just inside acos(0.8) zero-thrust boundary
        [InlineData(40f, 52)]   // Ship.GetWindAngleFactor(40 deg) ≈ 0.523 -> 52% thrust
        [InlineData(90f, 100)]  // beam reach peak efficiency in Ship.GetWindAngleFactor
        [InlineData(-90f, 100)]
        [InlineData(180f, 70)]  // pure tailwind = 0.70 in Ship.GetWindAngleFactor
        public void ThrustPercentMatchesShipWindAngleFactor(float bowDeg, int expectedPct)
        {
            var factor = ShipHudRules.WindAngleFactor(bowDeg);
            Assert.Equal(expectedPct, ShipHudRules.ThrustPercent(factor));
        }

        [Theory]
        [InlineData(0f, WindEfficiencyZone.DeadZone)]
        [InlineData(36.8f, WindEfficiencyZone.DeadZone)]
        [InlineData(-36.8f, WindEfficiencyZone.DeadZone)]
        [InlineData(37f, WindEfficiencyZone.CloseHauled)]
        [InlineData(-37f, WindEfficiencyZone.CloseHauled)]
        [InlineData(40f, WindEfficiencyZone.CloseHauled)]
        [InlineData(-40f, WindEfficiencyZone.CloseHauled)]
        [InlineData(45f, WindEfficiencyZone.CloseHauled)]
        [InlineData(-55f, WindEfficiencyZone.CloseHauled)]
        [InlineData(90f, WindEfficiencyZone.Optimal)]
        [InlineData(-110f, WindEfficiencyZone.Optimal)]
        [InlineData(150f, WindEfficiencyZone.Tailwind)]
        [InlineData(180f, WindEfficiencyZone.Tailwind)]
        public void ZoneClassificationCoversEntireDialSymmetrically(float bowDeg, WindEfficiencyZone expected)
        {
            Assert.Equal(expected, ShipHudRules.ClassifyZone(bowDeg));
        }

        [Fact]
        public void ArcSegmentsPartitionDialWithoutOverlapAndHaveValidDeadZone()
        {
            var totalSpan = 0f;
            foreach (var seg in ShipHudRules.ArcSegments)
            {
                Assert.True(seg.SpanDeg > 0f);
                totalSpan += seg.SpanDeg;
            }
            // 6 segments with 2 deg visual gaps between each pair = 360 - 12 = 348 deg.
            Assert.Equal(348f, totalSpan, 3);
            var dead = ShipHudRules.ArcSegments[0];
            Assert.Equal(WindEfficiencyZone.DeadZone, dead.Zone);
            Assert.Equal(-35.87f, dead.StartDeg, 2);
            Assert.Equal(71.74f, dead.SpanDeg, 2);
            Assert.True(dead.StartDeg + dead.SpanDeg <= ShipHudRules.DeadZoneHalfAngleDeg + 0.001f);
            var closeStbd = ShipHudRules.ArcSegments[1];
            Assert.Equal(WindEfficiencyZone.CloseHauled, closeStbd.Zone);
            Assert.True(closeStbd.StartDeg < 40f && closeStbd.StartDeg + closeStbd.SpanDeg > 40f);
        }

        [Fact]
        public void SpeedFormattingConvertsAndSmoothsCleanly()
        {
            Assert.Equal(5f, ShipHudRules.HorizontalSpeed(3f, 4f), 4);
            Assert.Equal("8.4 м/с", ShipHudRules.FormatSpeed(8.4f, ShipSpeedUnit.MetersPerSecond, "м/с", "км/ч"));
            Assert.Equal("30.2 км/ч", ShipHudRules.FormatSpeed(8.4f, ShipSpeedUnit.KilometersPerHour, "м/с", "км/ч"));
            Assert.Equal("0.0 m/s", ShipHudRules.FormatSpeed(0.02f, ShipSpeedUnit.MetersPerSecond, "m/s", "km/h"));

            var s = ShipHudRules.StepSmoothedSpeed(0f, 10f, 0.25f);
            Assert.True(s > 5f && s < 10f);
            Assert.Equal(0f, ShipHudRules.StepSmoothedSpeed(0.01f, 0f, 0.25f));
        }

        [Fact]
        public void ThrustFormattingHandlesSailOarsAndStop()
        {
            Assert.Equal("Тяга паруса 88%", ShipHudRules.FormatThrust(4, 0.88f, "Тяга паруса {0}%", "Вёсла", "Штиль"));
            Assert.Equal("Sail thrust 100%", ShipHudRules.FormatThrust(3, 1f, "Sail thrust {0}%", "Oars", "Calm"));
            Assert.Equal("Вёсла", ShipHudRules.FormatThrust(2, 0.9f, "Тяга паруса {0}%", "Вёсла", "Штиль"));
            Assert.Equal("Oars", ShipHudRules.FormatThrust(1, 0.5f, "Sail thrust {0}%", "Oars", "Calm"));
            Assert.Equal("Штиль", ShipHudRules.FormatThrust(0, 1f, "Тяга паруса {0}%", "Вёсла", "Штиль"));
        }
    }
}
