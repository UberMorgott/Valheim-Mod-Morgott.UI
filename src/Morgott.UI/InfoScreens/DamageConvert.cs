using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Morgott.Core;

namespace Morgott.UI.InfoScreens
{
    // Damage-type conversion in the previews (stats panel, weapon tooltip): the sources come from Core IDamageConversion
    // (Power's gems), the math is Core DamageConversion.Apply, the same call the combat hook makes. Without a provider
    // nothing converts.
    public static class DamageConvert
    {
        // $inventory_* token per DamageConversion index (Damage .. Spirit).
        public static readonly string[] Words =
        {
            "$inventory_damage", "$inventory_blunt", "$inventory_slash", "$inventory_pierce", "$inventory_fire",
            "$inventory_frost", "$inventory_lightning", "$inventory_poison", "$inventory_spirit",
        };

        private static readonly List<DamageConversion.Source> Live = new List<DamageConversion.Source>(4);

        // The conversions the player's weapon hits get now (shared list, refilled per call; main thread).
        public static List<DamageConversion.Source> Sources(Player p)
        {
            Live.Clear();
            if (p && Services.TryGet<IDamageConversion>(out var conv)) conv.Sources(p, Live);
            return Live;
        }

        public static float[] Parts(HitData.DamageTypes d) =>
            new[] { d.m_damage, d.m_blunt, d.m_slash, d.m_pierce, d.m_fire, d.m_frost, d.m_lightning, d.m_poison, d.m_spirit };

        // d converted by sources; chop / pickaxe stay.
        public static HitData.DamageTypes Apply(HitData.DamageTypes d, IList<DamageConversion.Source> sources)
        {
            if (sources == null || sources.Count == 0) return d;
            var c = DamageConversion.Apply(Parts(d), sources);
            d.m_damage = c[0];
            d.m_blunt = c[1];
            d.m_slash = c[2];
            d.m_pierce = c[3];
            d.m_fire = c[4];
            d.m_frost = c[5];
            d.m_lightning = c[6];
            d.m_poison = c[7];
            d.m_spirit = c[8];
            return d;
        }

        // "pierce, slash → fire 100%" (+ " +20%" bonus) for the types of parts the source moves; null when it moves none.
        public static string Describe(float[] parts, DamageConversion.Source s, Func<string, string> loc, bool comma)
        {
            if (s.To < 0 || s.To >= DamageConversion.Count || s.Share <= 0f) return null;
            var sb = new StringBuilder(48);
            for (var t = 0; t < DamageConversion.Count; t++)
            {
                if (t == s.To || (s.From & (1 << t)) == 0 || parts[t] < 0.05f) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(loc(Words[t]));
            }
            if (sb.Length == 0) return null;
            sb.Append(" → ").Append(loc(Words[s.To])).Append(' ').Append(Pct(s.Share, comma)).Append('%');
            if (s.Bonus >= 0.005f) sb.Append(" +").Append(Pct(s.Bonus, comma)).Append('%');
            return sb.ToString();
        }

        public const string TipToken = "$ui_tip_conversion";

        // Weapon tooltip: one "$ui_tip_conversion: <color=orange>pierce → fire 100%</color>" row per source after the damage
        // rows (tokens left for the tooltip's localization); unchanged without a source that moves one of d's types.
        public static string TooltipRows(string tooltip, HitData.DamageTypes d, IList<DamageConversion.Source> sources, bool comma)
        {
            if (sources == null || sources.Count == 0 || string.IsNullOrEmpty(tooltip) || tooltip.Contains(TipToken)) return tooltip;
            var parts = Parts(d);
            for (var i = sources.Count - 1; i >= 0; i--) // each goes right after the damage rows: insert last first
            {
                var text = Describe(parts, sources[i], w => w, comma);
                if (text != null) tooltip = Screens.WeaponAttackRate.InsertLine(tooltip, TipToken + ": <color=orange>" + text + "</color>");
            }
            return tooltip;
        }

        private static string Pct(float share, bool comma)
        {
            var v = (share * 100f).ToString("0.#", CultureInfo.InvariantCulture);
            return comma ? v.Replace('.', ',') : v;
        }
    }
}
