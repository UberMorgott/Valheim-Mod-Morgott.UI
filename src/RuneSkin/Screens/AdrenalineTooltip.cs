using UnityEngine;
using RuneSkin.InfoScreens;

namespace RuneSkin.Screens
{
    // Live numbers for AdrenalineText, read from the hovered trinket, its full-adrenaline SE and the local player's
    // current loadout / prefab fields (vanilla ValheimDecompiled-1.0.21). Called by DamageIcons.GetTooltip_Postfix.
    public static class AdrenalineTooltip
    {
        public static string Append(ItemDrop.ItemData item, string tooltip)
        {
            var s = item?.m_shared;
            if (s == null || (s.m_maxAdrenaline <= 0f && s.m_fullAdrenalineSE == null)) return tooltip;
            var f = new AdrenalineFacts { ItemMax = s.m_maxAdrenaline, FullEffect = s.m_fullAdrenalineSE };
            var p = Player.m_localPlayer;
            if (s.m_fullAdrenalineSE is SE_Stats se) Effect(f, se, p);
            else if (s.m_fullAdrenalineSE) f.Duration = s.m_fullAdrenalineSE.m_ttl;
            if (p) Gains(f, p);
            var comma = Localization.instance?.GetSelectedLanguage() == "Russian";
            return AdrenalineText.Append(tooltip, f, comma);
        }

        private static void Effect(AdrenalineFacts f, SE_Stats se, Player p)
        {
            f.Duration = se.m_ttl;
            // SE_Stats.cs:245-256: m_staminaOverTime (x max stamina when a fraction) is paid evenly over its duration.
            if (se.m_staminaOverTime > 0f && se.m_staminaOverTimeDuration > 0f)
                f.StaminaOverTimePerSec = (se.m_staminaOverTimeIsFraction ? (p ? p.GetMaxStamina() : 0f) : 1f) *
                                          se.m_staminaOverTime / se.m_staminaOverTimeDuration;
            if (!p) return;
            // SE_Stats.cs:266-300: a multiplier m > 1 adds m - 1 to the regen multiplier, so the bonus is base x (m - 1).
            if (se.m_healthRegenMultiplier > 1f)
            {
                var food = 0f;
                foreach (var fd in p.m_foods) food += fd.m_item.m_shared.m_foodRegen;
                f.HealthPerSec = StatsMath.HealthRegen(food, se.m_healthRegenMultiplier - 1f);
            }
            if (se.m_staminaRegenMultiplier > 1f)
                f.StaminaPerSec = StatsMath.StaminaRegen(p.m_staminaRegen, p.m_staminaRegenTimeMultiplier, 0f, 1f,
                    se.m_staminaRegenMultiplier - 1f, 0f, Game.m_staminaRegenRate);
            if (se.m_eitrRegenMultiplier > 1f && p.GetMaxEitr() > 0f)
                f.EitrPerSec = StatsMath.EitrRegen(p.m_eiterRegen, 0f, 1f, se.m_eitrRegenMultiplier - 1f, 0f);
        }

        private static void Gains(AdrenalineFacts f, Player p)
        {
            f.HasPlayer = true;
            // Player.cs:4549-4551: world rate, then active SEs' m_adrenalineModifier (SE_Stats.cs:397-400, additive).
            var rate = Game.m_adrenalineRate;
            p.GetSEMan().ModifyAdrenaline(rate, ref rate);
            var weapon = p.GetCurrentWeapon();
            var attack = weapon?.m_shared.m_attack;
            if (attack != null)
            {
                // Attack.cs:887-900: the weapon's projectile, replaced by its ammo's (Attack.UseAmmo: the selected ammo
                // when it is in the bag and of the weapon's m_ammoType, else the first matching one).
                var shot = attack.m_attackProjectile;
                var type = weapon.m_shared.m_ammoType;
                if (!string.IsNullOrWhiteSpace(type))
                {
                    var ammo = p.GetAmmoItem();
                    if (ammo != null && (!p.GetInventory().ContainsItem(ammo) || ammo.m_shared.m_ammoType != type)) ammo = null;
                    ammo ??= p.GetInventory().GetAmmoItem(type);
                    if (ammo != null && ammo.m_shared.m_attack.m_attackProjectile) shot = ammo.m_shared.m_attack.m_attackProjectile;
                }
                var proj = shot ? shot.GetComponent<Projectile>() : null;
                if (proj) f.Shot = proj.m_adrenaline * rate;
                else f.Hit = attack.m_attackAdrenaline * rate;
                f.Swing = attack.m_attackUseAdrenaline * rate;
            }
            var blocker = p.m_leftItem ?? weapon; // Humanoid.GetCurrentBlocker
            if (blocker != null)
            {
                f.Block = blocker.m_shared.m_blockAdrenaline * rate;
                f.Parry = blocker.m_shared.m_perfectBlockAdrenaline * rate;
            }
            f.Dodge = p.m_perfectDodgeAdrenaline * rate;
            f.Stagger = p.m_staggerEnemyAdrenaline * rate;
            f.Power = p.m_adrenalineGuardianPower * rate; // Player.cs:6399-6401
            f.Miss = p.m_attackMissAdrenaline;        // losses skip the gain multipliers (Player.cs:4545)
            f.Unblocked = p.m_nonBlockDamageAdrenaline;
            f.TotalMax = p.GetMaxAdrenaline();
            Sample(p.m_adrenalineGainMultiplier, out f.GainMin, out f.GainMax);
            Sample(p.m_adrenalineDegenDelay, out f.DelayMin, out f.DelayMax);
            Sample(p.m_adrenalineDegen, out f.DecayMin, out f.DecayMax);
        }

        // A prefab curve over the bar fill 0..1 as its lowest / highest value (11 samples).
        private static void Sample(AnimationCurve c, out float min, out float max)
        {
            min = max = c != null ? c.Evaluate(0f) : 0f;
            if (c == null) return;
            for (var i = 1; i <= 10; i++)
            {
                var v = c.Evaluate(i / 10f);
                if (v < min) min = v;
                if (v > max) max = v;
            }
        }
    }
}
