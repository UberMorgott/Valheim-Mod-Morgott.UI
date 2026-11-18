using System;
using System.Collections.Generic;
using System.Globalization;

namespace Morgott.UI.Screens
{
    // Base attacks per second of a weapon's primary attack, attack key held, no target hit: no skill, status effect or
    // Epic Loot speed. Unity exposes no trigger -> state mapping at runtime, so the timings are baked from the game's
    // Player_animator + clips (BalanceSim tools/anim_events.py Controller.attack, game 1.0.21, Therzie clip swaps included);
    // the caller shows nothing on another game version. An unknown trigger or looping attack has no value.
    public static class WeaponAttackRate
    {
        public const string Token = "$ui_tip_attack_rate";

        public struct Attack
        {
            public string Animation, ReloadAnimation;
            public int ChainLevels, RandomAnimations;
            public bool Looping, BowDraw, RequiresReload;
            public float DrawSeconds, ReloadSeconds;
        }

        // Full = trigger to the next possible attack: the attack-tagged span (Player.InAttack), plus the exit blend when
        // it cannot be interrupted. Chain = clip "Chain" event inside that span (CharacterAnimEvent.Chain): a held key
        // starts the next chain level there (Humanoid.StartAttack :282, Player.HaveQueuedChain, Attack.CanStartChainAttack).
        private readonly struct Timing
        {
            public readonly float Full, Chain;
            public Timing(float full, float chain) { Full = full; Chain = chain; }
        }

        private static readonly Dictionary<string, Timing> Triggers = new Dictionary<string, Timing>(StringComparer.Ordinal)
        {
            ["DualBladeMain0"] = new Timing(1.953f, 0.843f),
            ["DualBladeMain1"] = new Timing(1.606f, 0.632f),
            ["DualBladeMain2"] = new Timing(1.482f, 0f),
            ["ThrowAttackMain0"] = new Timing(1.2f, 0.765f),
            ["ThrowAttackMain1"] = new Timing(1.067f, 0.765f),
            ["ThrowAttackMain2"] = new Timing(1.876f, 0f),
            ["WarpikeMain0"] = new Timing(0.898f, 0.555f),
            ["WarpikeMain1"] = new Timing(0.998f, 0.743f),
            ["WarpikeMain2"] = new Timing(1.115f, 0f),
            ["atgeir_attack0"] = new Timing(0.667f, 0f),
            ["atgeir_attack1"] = new Timing(0.7f, 0f),
            ["atgeir_attack2"] = new Timing(1.148f, 0f),
            ["battleaxe_attack0"] = new Timing(1.423f, 0f),
            ["battleaxe_attack1"] = new Timing(0.746f, 0f),
            ["battleaxe_attack2"] = new Timing(1.097f, 0f),
            ["bow_fire"] = new Timing(0.76f, 0f),
            ["crossbow_fire"] = new Timing(0.7f, 0f),
            ["dual_knives0"] = new Timing(0.364f, 0.331f),
            ["dual_knives1"] = new Timing(0.364f, 0.336f),
            ["dual_knives2"] = new Timing(0.559f, 0f),
            ["dualaxes0"] = new Timing(0.95f, 0.257f),
            ["dualaxes1"] = new Timing(1.147f, 0.533f),
            ["dualaxes2"] = new Timing(1.125f, 0.706f),
            ["dualaxes3"] = new Timing(1.125f, 0f),
            ["emote_drink"] = new Timing(4.776f, 0f),
            ["fishingrod_throw"] = new Timing(2f, 0f),
            ["greatsword0"] = new Timing(1.167f, 0.783f),
            ["greatsword1"] = new Timing(1f, 0.641f),
            ["greatsword2"] = new Timing(1.5f, 0.833f),
            ["knife_stab0"] = new Timing(0.444f, 0.274f),
            ["knife_stab1"] = new Timing(0.395f, 0.299f),
            ["knife_stab2"] = new Timing(0.542f, 0f),
            ["scything"] = new Timing(1.076f, 0f),
            ["shovel0"] = new Timing(0.833f, 0.501f),
            ["shovel1"] = new Timing(0.833f, 0.502f),
            ["spear_poke"] = new Timing(0.511f, 0f),
            ["spear_throw"] = new Timing(1.034f, 0f),
            ["staff_fireball0"] = new Timing(0.833f, 0f),
            ["staff_fireball1"] = new Timing(0.75f, 0f),
            ["staff_frostorb"] = new Timing(2.05f, 0f),
            ["staff_lightningshot"] = new Timing(0.7f, 0f),
            ["staff_shield"] = new Timing(1.976f, 0f),
            ["staff_summon"] = new Timing(1.5f, 0f),
            ["staff_thrust"] = new Timing(1.083f, 0f),
            ["staff_thunder"] = new Timing(1.3f, 0f),
            ["staff_trollsummon"] = new Timing(2f, 0f),
            ["swing_axe0"] = new Timing(1.13f, 0.801f),
            ["swing_axe1"] = new Timing(0.549f, 0f),
            ["swing_axe2"] = new Timing(0.861f, 0f),
            ["swing_longsword0"] = new Timing(0.982f, 0.692f),
            ["swing_longsword1"] = new Timing(0.646f, 0.536f),
            ["swing_longsword2"] = new Timing(0.747f, 0f),
            ["swing_pickaxe"] = new Timing(1.234f, 0f),
            ["swing_sledge"] = new Timing(2.193f, 0f),
            ["throw_bomb"] = new Timing(0.633f, 0f),
            ["unarmed_attack0"] = new Timing(0.592f, 0f),
            ["unarmed_attack1"] = new Timing(0.54f, 0.496f),
        };

        // Seconds a reload keeps the minor-action tag (Player.InMinorAction) after its timer, counted from the end of the
        // fire animation's Full. crossbow: "reload done" state 0.9 + blend 0.25, minus fire's 0.2 exit blend; staff: bool
        // exit blend 0.25 minus the same 0.2.
        // shortcut: assumes a fire animation with crossbow_fire's 0.2 s blend, re-bake if a reload weapon uses another.
        private static readonly Dictionary<string, float> ReloadTail = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            ["recharge_lightningstaff"] = 0.05f,
            ["reload_crossbow"] = 0.95f,
        };

        // Attacks per second, 0 = cannot be determined.
        public static float PerSecond(in Attack a)
        {
            if (string.IsNullOrEmpty(a.Animation) || a.Looping) return 0f;
            var count = a.ChainLevels > 1 ? a.ChainLevels : a.RandomAnimations >= 2 ? a.RandomAnimations : 1; // Attack.Start :411-431
            var numbered = a.ChainLevels > 1 || a.RandomAnimations >= 2;
            var total = 0f;
            for (var i = 0; i < count; i++)
            {
                if (!Triggers.TryGetValue(numbered ? a.Animation + i : a.Animation, out var t)) return 0f;
                var chains = a.ChainLevels > 1 && i < count - 1 && t.Chain > 0f; // the last level has no next one (Attack.Update :543-547)
                total += chains ? t.Chain : t.Full;
            }
            var cycle = total / count;
            if (a.RequiresReload)
            {
                if (a.ReloadAnimation == null || !ReloadTail.TryGetValue(a.ReloadAnimation, out var tail)) return 0f;
                cycle += a.ReloadSeconds + tail; // Player.UpdateActionQueue runs the reload once the attack ended
            }
            // The draw timer runs while the release animation still plays (Player.UpdateAttackBowDraw has no attack check).
            if (a.BowDraw) cycle = Math.Max(cycle, a.DrawSeconds);
            return cycle > 0f ? 1f / cycle : 0f;
        }

        // Tooltip text with the rate row after its last damage row (HitData.GetTooltipString); unchanged without one.
        public static string InsertRow(string tooltip, float perSecond, bool comma)
        {
            if (perSecond <= 0f || string.IsNullOrEmpty(tooltip) || tooltip.Contains(Token)) return tooltip;
            var insertAt = -1;
            for (var start = 0; start < tooltip.Length;)
            {
                var end = tooltip.IndexOf('\n', start);
                if (end < 0) end = tooltip.Length;
                var token = TooltipSheet.Token(tooltip.Substring(start, end - start).Trim());
                if (token != null && DamageTokens.Contains(token)) insertAt = end;
                start = end + 1;
            }
            if (insertAt < 0) return tooltip;
            var value = perSecond.ToString("0.00", CultureInfo.InvariantCulture);
            if (comma) value = value.Replace('.', ',');
            return tooltip.Insert(insertAt, "\n" + Token + ": <color=orange>" + value + "</color>");
        }

        private static readonly HashSet<string> DamageTokens = new HashSet<string>(StringComparer.Ordinal)
        {
            "inventory_damage", "inventory_blunt", "inventory_slash", "inventory_pierce", "inventory_fire", "inventory_frost",
            "inventory_lightning", "inventory_poison", "inventory_spirit",
        };
    }
}
