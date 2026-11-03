using System;
using System.Text;

namespace RuneSkin.Screens
{
    // Inline damage-type icons in tooltip text: a TMP <sprite> tag before every vanilla damage-type word token
    // ($inventory_blunt .. $inventory_spirit), written by HitData.GetTooltipString (HitData.cs:508-603),
    // SE_Stats.GetDamageModifiersTooltipString (SE_Stats.cs:702-760) / SE_Stats percent mods (:657-690) and Epic Loot's
    // TooltipRenderer.AddDamages. Runs on the raw text before Localization.Localize, so every language keeps its words.
    // Chop / pickaxe never reach an item tooltip (GetTooltipString skips them); generic $inventory_damage has no icon.
    public static class DamageIconText
    {
        // Same order and names as the stats panel's element icons (StatsReader.ResistIcons, sprite.stats.icon.<type>).
        public static readonly string[] Types = { "blunt", "slash", "pierce", "fire", "frost", "lightning", "poison", "spirit" };

        private const string Prefix = "$inventory_";

        // Localization.FindNextWord ends a $word at these chars (Localization.cs:169, :428-441): a token matches only
        // when it is the whole word the game will translate ($inventory_fire, never $inventory_fireresist).
        private static readonly char[] EndChars = " (){}[]+-!?/\\&%,.:-=<>\n".ToCharArray();

        // tags[i] = markup inserted before Types[i] (null / empty = no icon). Nothing matched -> the same instance.
        public static string Decorate(string raw, string[] tags)
        {
            if (string.IsNullOrEmpty(raw) || tags == null || raw.IndexOf(Prefix, StringComparison.Ordinal) < 0) return raw;
            StringBuilder sb = null;
            var copied = 0;
            var at = 0;
            while ((at = raw.IndexOf(Prefix, at, StringComparison.Ordinal)) >= 0)
            {
                var start = at + Prefix.Length;
                var end = raw.IndexOfAny(EndChars, start);
                if (end < 0) end = raw.Length;
                var type = Array.IndexOf(Types, raw.Substring(start, end - start));
                // Idempotent: a token already carrying its tag (text decorated by a nested GetTooltip) is left alone.
                if (type >= 0 && type < tags.Length && !string.IsNullOrEmpty(tags[type]) &&
                    !(at >= tags[type].Length && string.CompareOrdinal(raw, at - tags[type].Length, tags[type], 0, tags[type].Length) == 0))
                {
                    sb ??= new StringBuilder(raw.Length + 160);
                    sb.Append(raw, copied, at - copied).Append(tags[type]);
                    copied = at;
                }
                at = end;
            }
            return sb == null ? raw : sb.Append(raw, copied, raw.Length - copied).ToString();
        }

        // rgba = 8 hex digits (ColorUtility.ToHtmlStringRGBA). Trailing space = gap before the word.
        public static string Tag(string type, string rgba) => "<sprite name=\"" + type + "\" color=#" + rgba + "> ";
    }
}
