using Morgott.UI.WorldHud;
using Xunit;

namespace Morgott.UI.Tests
{
    public class DamagePopupMathTests
    {
        [Fact]
        public void Numbers_start_above_the_enemy_hud_band()
        {
            Assert.Equal(1f, DamagePopupMath.RaiseAbove(500f, 596f, 100f, 4f), 3);
            Assert.Equal(0f, DamagePopupMath.RaiseAbove(500f, 480f, 100f, 4f));
            Assert.Equal(0f, DamagePopupMath.RaiseAbove(500f, 596f, 0f, 4f));
        }

        [Fact]
        public void Grade_styles_match_the_approved_mockup()
        {
            var vw = DamagePopupMath.Style(DamageGrade.VeryWeak, false);
            Assert.True(vw.Bold);
            Assert.Equal(1.20f, vw.Scale, 3);
            var w = DamagePopupMath.Style(DamageGrade.Weak, false);
            Assert.True(w.Bold);
            Assert.Equal(1.12f, w.Scale, 3);
            var n = DamagePopupMath.Style(DamageGrade.Normal, false);
            Assert.False(n.Bold);
            Assert.Equal(1f, n.Alpha);
            Assert.Equal(0f, n.Desaturate);
            var r = DamagePopupMath.Style(DamageGrade.Resistant, false);
            Assert.False(r.Bold);
            Assert.Equal(0.55f, r.Alpha, 3);
            Assert.Equal(0.20f, r.Desaturate, 3);
            var vr = DamagePopupMath.Style(DamageGrade.VeryResistant, false);
            Assert.True(vr.Alpha < r.Alpha);
            Assert.Equal(0.30f, vr.Desaturate, 3);
            var im = DamagePopupMath.Style(DamageGrade.Immune, false);
            Assert.Equal(r.Alpha, im.Alpha);
            Assert.Equal(r.Desaturate, im.Desaturate);
        }

        [Fact]
        public void Tick_is_smaller_and_keeps_the_grade_style()
        {
            var t = DamagePopupMath.Style(DamageGrade.Weak, true);
            Assert.True(t.Bold);
            Assert.Equal(1.06f * 0.9f, t.Scale, 3); // half the weak bump
            Assert.Equal(1.10f * 0.9f, DamagePopupMath.Style(DamageGrade.VeryWeak, true).Scale, 3);
            Assert.Equal(0.9f, DamagePopupMath.Style(DamageGrade.Normal, true).Scale, 3);
        }

        [Fact]
        public void Every_kind_has_its_own_token()
        {
            Assert.Null(DamagePopupMath.ColorToken(DamageKind.None));
            var seen = new System.Collections.Generic.HashSet<string>();
            for (var k = DamageKind.Blunt; k <= DamageKind.Spirit; k++) Assert.True(seen.Add(DamagePopupMath.ColorToken(k)));
            Assert.Equal("color.damage.type.fire", DamagePopupMath.ColorToken(DamageKind.Fire));
        }

        [Fact]
        public void Desaturate_moves_toward_grey_of_same_luma()
        {
            float r = 1f, g = 0.5f, b = 0f;
            DamagePopupMath.Desaturate(ref r, ref g, ref b, 1f);
            Assert.Equal(r, g, 4);
            Assert.Equal(g, b, 4);
            float r2 = 1f, g2 = 0.5f, b2 = 0f;
            DamagePopupMath.Desaturate(ref r2, ref g2, ref b2, 0f);
            Assert.Equal(1f, r2);
        }

        [Fact]
        public void Slots_spread_centre_right_left()
        {
            Assert.Equal(0, DamagePopupMath.Side(0));
            Assert.Equal(1, DamagePopupMath.Side(1));
            Assert.Equal(-1, DamagePopupMath.Side(2));
            Assert.Equal(2, DamagePopupMath.Side(3));
            Assert.Equal(-2, DamagePopupMath.Side(4));
        }

        [Fact]
        public void Priority_is_own_damage_heal_and_block_texts()
        {
            Assert.True(DamagePopupMath.IsPriority(DamageText.TextType.Normal, true));
            Assert.True(DamagePopupMath.IsPriority(DamageText.TextType.Heal, false));
            Assert.True(DamagePopupMath.IsPriority(DamageText.TextType.Blocked, false));
            Assert.False(DamagePopupMath.IsPriority(DamageText.TextType.Weak, false));
            Assert.False(DamagePopupMath.IsPriority(DamageText.TextType.Immune, false));
        }

        [Fact]
        public void Budget_keeps_reserve_and_caps()
        {
            Assert.Equal(6, DamagePopupMath.OthersBudget(0, 0, 0));
            Assert.Equal(6, DamagePopupMath.OthersBudget(2, 0, 0)); // priority used its reserve, others keep 6
            Assert.Equal(5, DamagePopupMath.OthersBudget(3, 0, 0));
            Assert.Equal(0, DamagePopupMath.OthersBudget(0, 6, 0));
            Assert.Equal(2, DamagePopupMath.OthersBudget(0, 0, 18)); // 24 - 4 - 18
            Assert.Equal(0, DamagePopupMath.OthersBudget(0, 0, 30));
            Assert.True(DamagePopupMath.AdmitPriority(7, 23));
            Assert.False(DamagePopupMath.AdmitPriority(8, 0));
            Assert.False(DamagePopupMath.AdmitPriority(0, 24));
        }

        [Fact]
        public void Over_budget_drops_ticks_first_then_smallest_hits()
        {
            var tick = new[] { true, false, true, false, false };
            var amount = new[] { 50f, 3f, 9f, 40f, 12f };
            var keep = new bool[5];
            Assert.Equal(3, DamagePopupMath.SelectBest(tick, amount, 5, 3, keep));
            Assert.Equal(new[] { false, true, false, true, true }, keep);
            Assert.Equal(2, DamagePopupMath.SelectBest(tick, amount, 5, 2, keep));
            Assert.Equal(new[] { false, false, false, true, true }, keep);
            Assert.Equal(4, DamagePopupMath.SelectBest(tick, amount, 5, 4, keep));
            Assert.Equal(new[] { true, true, false, true, true }, keep);
            Assert.Equal(0, DamagePopupMath.SelectBest(tick, amount, 5, 0, keep));
            Assert.Equal(5, DamagePopupMath.SelectBest(tick, amount, 5, 9, keep));
            Assert.Equal(2, DamagePopupMath.WorstIndex(tick, amount, 5));
            Assert.Equal(0, DamagePopupMath.WorstIndex(tick, amount, 2)); // a tick of 50 is worse than a hit of 3
        }

        [Fact]
        public void Dim_alpha_evens_out_hue_brightness_within_bounds()
        {
            var grey = DamagePopupMath.Luma(0.89f, 0.902f, 0.91f); // color.damage.normal
            var poison = DamagePopupMath.Luma(0.663f, 0.847f, 0.459f);
            var a = DamagePopupMath.DimAlpha(0.55f, poison, grey, 0.8f);
            Assert.True(a > 0.6f && a < 0.8f, $"poison dim alpha {a}");
            Assert.Equal(0.55f * grey, a * poison, 3); // same effective brightness as dimmed grey
            Assert.Equal(0.55f, DamagePopupMath.DimAlpha(0.55f, grey, grey, 0.8f), 3);
            Assert.Equal(0.55f, DamagePopupMath.DimAlpha(0.55f, 1f, grey, 0.8f), 3); // never below the grade
            Assert.Equal(0.8f, DamagePopupMath.DimAlpha(0.55f, 0.1f, grey, 0.8f), 3); // capped: still clearly dim
            Assert.Equal(0.8f, DamagePopupMath.DimAlpha(0.45f, 0f, grey, 0.8f), 3);
            Assert.Equal(1f, DamagePopupMath.DimAlpha(1f, 0.1f, grey, 0.8f), 3); // full grades untouched
            Assert.True(DamagePopupMath.DimAlpha(0.45f, poison, grey, 0.8f) < a); // grades stay ordered
        }

        [Fact]
        public void Tick_dim_alpha_is_floored_and_keeps_grades_ordered()
        {
            var grey = DamagePopupMath.Luma(0.89f, 0.902f, 0.91f);
            var poison = DamagePopupMath.Luma(0.663f, 0.847f, 0.459f);
            var r = DamagePopupMath.TickDimAlpha(DamagePopupMath.DimAlpha(0.55f, poison, grey, 0.8f), 0.55f, 0.69f, 0.8f);
            var vr = DamagePopupMath.TickDimAlpha(DamagePopupMath.DimAlpha(0.45f, poison, grey, 0.8f), 0.45f, 0.69f, 0.8f);
            Assert.Equal(0.69f, r, 3);
            Assert.True(vr < r && vr >= 0.59f - 0.001f, $"very resistant tick {vr} vs resistant {r}");
            Assert.Equal(0.8f, DamagePopupMath.TickDimAlpha(0.5f, 0.55f, 0.95f, 0.8f), 3); // floor capped
            Assert.Equal(0.4f, DamagePopupMath.TickDimAlpha(0.4f, 1f, 0.69f, 0.8f), 3); // full grades untouched
        }
    }
}
