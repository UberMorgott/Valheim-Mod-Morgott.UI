using System;
using System.Text;
using RuneSkin.InfoScreens;

namespace RuneSkin.Screens
{
    // The numbers behind a trinket's adrenaline, read live by AdrenalineTooltip (vanilla ValheimDecompiled-1.0.21).
    // Gains are per event, already x Game.m_adrenalineRate (Player.AddAdrenaline, Player.cs:4549); 0 = no line.
    public sealed class AdrenalineFacts
    {
        public float Hit;        // melee hit: Attack.m_attackAdrenaline x enemy multiplier (Attack.cs:1084, :1446)
        public float Shot;       // projectile hit: Projectile.m_adrenaline (Projectile.cs:699)
        public float Swing;      // every attack: Attack.m_attackUseAdrenaline (Attack.cs:613, :881)
        public float Block;      // blocker's m_blockAdrenaline (Humanoid.cs:1823)
        public float Parry;      // blocker's m_perfectBlockAdrenaline (Humanoid.cs:1828)
        public float Dodge;      // Player.m_perfectDodgeAdrenaline (Player.cs:5900)
        public float Stagger;    // Player.m_staggerEnemyAdrenaline x enemy multiplier (Character.cs:2644)
        public float Power;      // Player.m_adrenalineGuardianPower on a guardian power use (Player.cs:6401)
        public bool FullEffect;  // the item has a full-adrenaline SE: the bar empties when full, else stays full (:4577)
        public float Miss;       // Player.m_attackMissAdrenaline (Attack.cs:1500), negative = loss
        public float Unblocked;  // Player.m_nonBlockDamageAdrenaline (Character.cs:2360), negative = loss
        public float GainMin = 1f, GainMax = 1f; // Player.m_adrenalineGainMultiplier over the fill 0..1 (Player.cs:4550)
        public float ItemMax;    // this item's m_maxAdrenaline
        public float TotalMax;   // Player.GetMaxAdrenaline (base + worn, Player.cs:4446), 0 = unknown
        public float Duration;   // full-adrenaline SE m_ttl, 0 = no timer
        public float HealthPerSec, StaminaPerSec, EitrPerSec; // SE regen bonus in units per second (0 = none)
        public float StaminaOverTimePerSec; // SE m_staminaOverTime spread over its duration (SE_Stats.cs:245-256)
        public float DelayMin, DelayMax;    // Player.m_adrenalineDegenDelay over the fill (Player.cs:4548)
        public float DecayMin, DecayMax;    // Player.m_adrenalineDegen per second over the fill (Player.cs:2130)
        public bool HasPlayer;
    }

    // Plain-words block under a trinket's vanilla adrenaline lines ($item_maxadrenaline, $item_fulladrenaline,
    // ItemDrop.cs:1053-1055): what fills the bar and by how much, its size, what happens when full, how it drains.
    // Raw text with $runeskin_adr_* tokens (Words), localized with the rest of the tooltip.
    public static class AdrenalineText
    {
        public static string Append(string tooltip, AdrenalineFacts f, bool comma)
        {
            if (f == null || (f.ItemMax <= 0f && f.Duration <= 0f && !HasEffect(f))) return tooltip;
            var sb = new StringBuilder(tooltip ?? "", (tooltip?.Length ?? 0) + 512);
            sb.Append("\n\n<color=orange>$runeskin_adr_title</color>");
            if (f.HasPlayer)
            {
                sb.Append("\n$runeskin_adr_gain: ");
                // A flat gain curve scales every gain by its value; a sloped one is named as a range instead.
                var sloped = Math.Abs(f.GainMax - f.GainMin) > 0.005f;
                var k = sloped ? 1f : f.GainMin;
                var n = 0;
                n = Gain(sb, n, "$runeskin_adr_hit", f.Hit * k, comma);
                n = Gain(sb, n, "$runeskin_adr_shot", f.Shot * k, comma);
                n = Gain(sb, n, "$runeskin_adr_swing", f.Swing * k, comma);
                n = Gain(sb, n, "$runeskin_adr_block", f.Block * k, comma);
                n = Gain(sb, n, "$runeskin_adr_parry", f.Parry * k, comma);
                n = Gain(sb, n, "$runeskin_adr_dodge", f.Dodge * k, comma);
                n = Gain(sb, n, "$runeskin_adr_stagger", f.Stagger * k, comma);
                n = Gain(sb, n, "$runeskin_adr_power", f.Power * k, comma);
                n = Gain(sb, n, "$runeskin_adr_miss", f.Miss, comma);
                Gain(sb, n, "$runeskin_adr_unblocked", f.Unblocked, comma);
                if (sloped)
                {
                    sb.Append(" ($runeskin_adr_fillscale ×");
                    Range(sb, f.GainMin, f.GainMax, comma).Append(')');
                }
            }
            if (f.ItemMax > 0f)
            {
                sb.Append("\n$runeskin_adr_max: <color=orange>");
                StatsMath.AppendFactor(sb, f.ItemMax, comma).Append("</color>");
                if (f.TotalMax > 0f && Math.Abs(f.TotalMax - f.ItemMax) > 0.005f)
                    StatsMath.AppendFactor(sb.Append(" ($runeskin_adr_worn "), f.TotalMax, comma).Append(')');
            }
            sb.Append("\n$runeskin_adr_full: <color=orange>");
            if (!f.FullEffect) sb.Append("$runeskin_adr_stays");
            else if (f.Duration > 0f)
                StatsMath.AppendFactor(sb.Append("$runeskin_adr_effect "), f.Duration, comma).Append(" $runeskin_stats_sec, $runeskin_adr_reset");
            else sb.Append("$runeskin_adr_reset");
            sb.Append("</color>");
            PerSecond(sb, "$runeskin_stats_health_regen", f.HealthPerSec, false, comma);
            PerSecond(sb, "$runeskin_stats_stamina_regen", f.StaminaPerSec, true, comma);
            PerSecond(sb, "$runeskin_stats_eitr_regen", f.EitrPerSec, true, comma);
            PerSecond(sb, "$runeskin_stats_stamina", f.StaminaOverTimePerSec, false, comma);
            if (f.HasPlayer && f.DecayMax > 0f)
            {
                sb.Append("\n$runeskin_adr_decay: <color=orange>−");
                Range(sb, f.DecayMin, f.DecayMax, comma).Append("$runeskin_stats_per_second</color>");
                if (f.DelayMax > 0f)
                    Range(sb.Append(" $runeskin_adr_after "), f.DelayMin, f.DelayMax, comma).Append(" $runeskin_stats_sec");
            }
            return sb.ToString();
        }

        private static bool HasEffect(AdrenalineFacts f) =>
            f.HealthPerSec > 0f || f.StaminaPerSec > 0f || f.EitrPerSec > 0f || f.StaminaOverTimePerSec > 0f;

        private static int Gain(StringBuilder sb, int n, string what, float v, bool comma)
        {
            if (Math.Abs(v) < 0.005f) return n;
            if (n > 0) sb.Append(", ");
            sb.Append(what).Append(" <color=orange>");
            if (v > 0f) sb.Append('+');
            StatsMath.AppendFactor(sb, v, comma).Append("</color>");
            return n + 1;
        }

        // Health regen: food-paid, current foods; stamina / eitr: "up to" = the empty-pool rate (Player.cs:2099-2152).
        private static void PerSecond(StringBuilder sb, string what, float v, bool upTo, bool comma)
        {
            if (v <= 0.005f) return;
            sb.Append("\n  ").Append(what).Append(": <color=orange>");
            if (upTo) sb.Append("$runeskin_stats_upto ");
            StatsMath.AppendFactor(sb.Append('+'), v, comma).Append("$runeskin_stats_per_second</color>");
        }

        private static StringBuilder Range(StringBuilder sb, float a, float b, bool comma)
        {
            StatsMath.AppendFactor(sb, a, comma);
            return Math.Abs(b - a) > 0.005f ? StatsMath.AppendFactor(sb.Append('–'), b, comma) : sb;
        }
    }
}
