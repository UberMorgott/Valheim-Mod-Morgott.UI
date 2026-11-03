using RuneSkin.InfoScreens;
using RuneSkin.Screens;
using Xunit;

namespace RuneSkin.Tests
{
    // Trinket adrenaline block: gains, bar size, full-bar effect per second, decay (vanilla values from the 1.0.20 dump:
    // TrinketBronzeHealth max 50, SE 60 s health regen x1.25; Player perfect dodge 5, stagger 3; shield block 2 / parry 5).
    public class AdrenalineTextTests
    {
        private static AdrenalineFacts Bronze() => new AdrenalineFacts
        {
            HasPlayer = true,
            Hit = 1f,
            Block = 2f,
            Parry = 5f,
            Dodge = 5f,
            Stagger = 3f,
            ItemMax = 50f,
            TotalMax = 50f,
            Duration = 60f,
            FullEffect = true,
            HealthPerSec = StatsMath.HealthRegen(6f, 0.25f), // three foods of 2 hp/tick: 0.6 hp/s base, +25%
            DelayMin = 2f,
            DelayMax = 2f,
            DecayMin = 1f,
            DecayMax = 3f,
        };

        [Fact]
        public void TrinketBlockStatesGainsSizeEffectAndDecay()
        {
            Assert.Equal("tip\n\n<color=orange>$runeskin_adr_title</color>" +
                         "\n$runeskin_adr_gain: $runeskin_adr_hit <color=orange>+1</color>, $runeskin_adr_block <color=orange>+2</color>, " +
                         "$runeskin_adr_parry <color=orange>+5</color>, $runeskin_adr_dodge <color=orange>+5</color>, " +
                         "$runeskin_adr_stagger <color=orange>+3</color>" +
                         "\n$runeskin_adr_max: <color=orange>50</color>" +
                         "\n$runeskin_adr_full: <color=orange>$runeskin_adr_effect 60 $runeskin_stats_sec, $runeskin_adr_reset</color>" +
                         "\n  $runeskin_stats_health_regen: <color=orange>+0.15$runeskin_stats_per_second</color>" +
                         "\n$runeskin_adr_decay: <color=orange>−1–3$runeskin_stats_per_second</color> $runeskin_adr_after 2 $runeskin_stats_sec",
                AdrenalineText.Append("tip", Bronze(), false));
        }

        [Fact]
        public void RussianCommaWornTotalLossesAndUpToRegen()
        {
            var f = Bronze();
            f.TotalMax = 115f;
            f.Miss = -5f;
            f.Power = 10f;
            f.HealthPerSec = 0f;
            f.StaminaPerSec = 1.5f;
            var s = AdrenalineText.Append("", f, true);
            Assert.Contains("<color=orange>50</color> ($runeskin_adr_worn 115)", s);
            Assert.Contains("$runeskin_adr_power <color=orange>+10</color>, $runeskin_adr_miss <color=orange>−5</color>", s);
            Assert.Contains("$runeskin_stats_stamina_regen: <color=orange>$runeskin_stats_upto +1,5$runeskin_stats_per_second</color>", s);
            Assert.DoesNotContain("health_regen", s);
        }

        [Fact]
        public void FlatGainCurveScalesGainsSlopedOneIsNamed()
        {
            var f = Bronze();
            f.GainMin = f.GainMax = 2f;
            Assert.Contains("$runeskin_adr_parry <color=orange>+10</color>", AdrenalineText.Append("", f, false));
            f.GainMin = 0.5f;
            var s = AdrenalineText.Append("", f, false);
            Assert.Contains("$runeskin_adr_parry <color=orange>+5</color>", s);
            Assert.Contains("($runeskin_adr_fillscale ×0.5–2)", s);
        }

        [Fact]
        public void NoPlayerKeepsItemLinesOnlyAndNonTrinketIsUntouched()
        {
            var f = new AdrenalineFacts { ItemMax = 50f, Duration = 30f, FullEffect = true };
            var s = AdrenalineText.Append("", f, false);
            Assert.DoesNotContain("adr_gain", s);
            Assert.DoesNotContain("adr_decay", s);
            Assert.Contains("$runeskin_adr_effect 30", s);
            Assert.Equal("tip", AdrenalineText.Append("tip", new AdrenalineFacts(), false));
            // Max without a full-adrenaline SE: vanilla keeps the bar full (Player.cs:4577).
            Assert.Contains("$runeskin_adr_full: <color=orange>$runeskin_adr_stays</color>", AdrenalineText.Append("", new AdrenalineFacts { ItemMax = 40f }, false));
        }
    }
}
