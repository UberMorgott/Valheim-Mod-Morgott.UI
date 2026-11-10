using System;
using Morgott.UI.PlayerHud;
using Xunit;

namespace Morgott.UI.Tests
{
    // BarFollow (user 2026-11-10: smooth-regen 1-hp ticks stepped a max-5 HP bar by 20 %): the drawn value follows regen
    // ticks at their measured rate, drops at once, eases irregular rises, snaps on Reset, rescales with the max.
    public class BarFollowTests
    {
        // Real value rises by `tick` every 1/rate s from `start`; returns the largest per-frame drawn step after warm-up as a
        // multiple of the real average per-frame rise, asserting monotonic and never above the real value.
        private static float StepRatio(float max, float rate, float tick, float start, float dt, float seconds = 4f)
        {
            var f = new BarFollow();
            float cur = start, next = 1f / rate, t = 0f, prev = -1f, maxStep = 0f;
            for (var i = 0; t < seconds; i++)
            {
                t += dt;
                while (t >= next) { cur = Math.Min(max, cur + tick); next += 1f / rate; }
                var shown = f.Step(cur, max, dt, 0.25f);
                Assert.True(shown <= cur + 1e-4f * max, $"fuller than real at {t}");
                if (prev >= 0f) Assert.True(shown >= prev - 1e-6f * max, $"not monotonic at {t}");
                if (t > 3.5f / rate && t > 0.5f && prev >= 0f) maxStep = Math.Max(maxStep, shown - prev);
                prev = shown;
            }
            Assert.True(cur < max, "test must not reach the cap");
            return Math.Max(0f, maxStep - 2.4e-7f * max) / (tick * rate * dt); // minus the float ulp of the returned value
        }

        [Theory]
        [InlineData(5f, 1f)]
        [InlineData(5f, 5f)]
        [InlineData(5f, 60f)]
        [InlineData(100000f, 1f)]
        [InlineData(100000f, 5f)]
        [InlineData(100000f, 60f)]
        public void RegenTicks_FollowAtTheirRate(float max, float rate)
        {
            var tick = max / (20f * rate); // full bar in 20 s
            foreach (var dt in new[] { 1f / 60f, 1f / 144f, 1f / 30f })
            {
                var r = StepRatio(max, rate, tick, 0f, dt);
                Assert.True(r <= 2.05f, $"max {max} rate {rate} dt {dt}: per-frame step {r:0.00} x real rate");
            }
        }

        [Fact]
        public void MaxFive_OneHpPerSecond_NoTwentyPercentJump()
        {
            // the reported case: per-frame step stays near 1 hp/s * dt / 5 (0.33 % of the bar at 60 fps), not 20 %
            var r = StepRatio(5f, 1f, 1f, 0f, 1f / 60f);
            Assert.True(r <= 2.05f, $"{r}");
        }

        [Theory]
        [InlineData(100000f, 0.1f, 1f, 50000f)] // Codex: float accumulation lost sub-ulp steps on a big pool at 144 fps
        [InlineData(5f, 0.05f, 1f, 0f)]         // Codex: 1 hp / 20 s at max 5 sprang then stood still
        [InlineData(5f, 1f / 90f, 1f, 0f)]      // Codex: 1 hp / 90 s (no interval cap)
        public void SlowTicks_StayContinuous(float max, float rate, float tick, float start)
        {
            var r = StepRatio(max, rate, tick, start, 1f / 144f, 4.6f / rate);
            Assert.True(r <= 2.05f, $"{r}");
        }

        [Fact]
        public void Hit_WhileTrailing_ReadsAtOnce()
        {
            var f = new BarFollow();
            f.Step(2f, 10f, 0.016f, 0.25f);
            for (var t = 0.016f; t < 3.5f; t += 0.016f) f.Step(2f + (float)Math.Floor(t), 10f, 0.016f, 0.25f); // 1 hp / s ticks, real 5
            var before = f.Shown;
            Assert.True(before < 5f - 0.1f, $"trails {before}");
            var after = f.Step(4.8f, 10f, 0.016f, 0.25f); // hit 0.2 while the fill still trails the last tick
            Assert.InRange(after, before - 0.2f - 1e-4f, before - 0.2f + 0.02f);
        }

        [Fact]
        public void TwoEqualHeals_ThirtySecondsApart_Ease()
        {
            // Codex: two equal one-off heals read as regen and crawled for 30 s
            var f = new BarFollow();
            f.Step(10f, 100f, 0.1f, 0.25f);
            for (var i = 0; i < 300; i++) f.Step(50f, 100f, 0.1f, 0.25f);
            for (var i = 0; i < 300; i++) f.Step(50f, 100f, 0.1f, 0.25f);
            for (var i = 0; i < 10; i++) f.Step(90f, 100f, 0.1f, 0.25f);
            Assert.Equal(90f, f.Shown, 2);
        }

        [Fact]
        public void Damage_IsInstant_AndHealEases()
        {
            var f = new BarFollow();
            f.Step(80f, 100f, 0.016f, 0.25f);
            Assert.Equal(30f, f.Step(30f, 100f, 0.016f, 0.25f), 4);
            var first = f.Step(80f, 100f, 0.016f, 0.25f);
            Assert.InRange(first, 30f, 40f); // potion: eased, no jump
            for (var i = 0; i < 60; i++) f.Step(80f, 100f, 0.016f, 0.25f);
            Assert.Equal(80f, f.Shown, 3);
        }

        [Fact]
        public void Reset_And_ZeroSeconds_Snap()
        {
            var f = new BarFollow();
            f.Step(1f, 100f, 0.016f, 0.25f);
            f.Reset(); // respawn / teleport / HUD shown again
            Assert.Equal(100f, f.Step(100f, 100f, 0.016f, 0.25f));
            Assert.Equal(10f, f.Step(10f, 100f, 0.016f, 0.25f));
            Assert.Equal(90f, f.Step(90f, 100f, 0.016f, 0f));
        }

        [Fact]
        public void MaxChange_RescalesWithoutJumpOrLag()
        {
            var f = new BarFollow();
            f.Step(50f, 50f, 0.016f, 0.25f);
            for (var m = 50f; m > 30f; m -= 0.1f) Assert.Equal(m, f.Step(m, m, 0.016f, 0.25f), 4); // food decay at full
            Assert.Equal(30f, f.Step(30f, 80f, 0.016f, 0.25f), 4); // food eaten: max up, value kept
        }
    }
}
