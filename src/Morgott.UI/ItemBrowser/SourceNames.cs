using System;
using System.Collections.Generic;
using System.Text;

namespace Morgott.UI.ItemBrowser
{
    // Readable name for a source prefab that carries no localized name (vanilla destructibles: pots, braziers, debris have no
    // m_name and no hover text). Never a raw ID on screen: "ashland_pot1_green" -> "Ashlands pot (green)".
    public static class SourceNames
    {
        // shortcut: curated words only for vanilla prefab name parts seen unnamed in the index; extend when a new one shows raw.
        private static readonly Dictionary<string, string> Words = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ashland"] = "Ashlands",
            ["kit"] = "",
            ["dvergrtown"] = "Dvergr",
            ["dvergrprops"] = "Dvergr",
            ["mistlands"] = "Mistlands",
            ["goblin"] = "Fuling",
            ["trader"] = "Trader",
            ["bonepile"] = "Bone pile",
            ["mudpile"] = "Mud pile",
            ["deadtree"] = "Dead tree",
            ["fx"] = "",
            ["sfx"] = "",
            ["vfx"] = "",
            ["clone"] = "",
        };

        private static readonly HashSet<string> Variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "green", "red", "blue", "yellow", "white", "black", "small", "large", "big", "old", "broken" };

        // Morgott.EpicLoot LootGraph pseudo sources: one per Epic Loot loot table (owner = creature / container prefab) and
        // per item set.
        public const string LootTablePrefix = "EL:loottable:", ItemSetPrefix = "EL:itemset:";

        /// <summary>Vanilla dev / test prefab ("DvergerTest", "Rock_destructible_test").</summary>
        public static bool DevPrefab(string id) => id != null && id.EndsWith("test", StringComparison.OrdinalIgnoreCase);

        /// <summary>The token has a translation: Localization answers "[key]" for a key it lacks (Localization.Translate).
        /// Plain text and an absent Localization (localize returns its input) count as translated.</summary>
        public static bool Translated(string token, Func<string, string> localize) =>
            !string.IsNullOrEmpty(token) && localize(token) is string s && s.Length > 0 && s[0] != '[';

        public static bool EpicLootPseudo(string id) =>
            id != null && (id.StartsWith(LootTablePrefix, StringComparison.Ordinal) || id.StartsWith(ItemSetPrefix, StringComparison.Ordinal));

        /// <summary>Loot table's owner prefab ("EL:loottable:Troll" -> "Troll"), else null.</summary>
        public static string LootTableOwner(string id) =>
            id != null && id.StartsWith(LootTablePrefix, StringComparison.Ordinal) ? id.Substring(LootTablePrefix.Length) : null;

        /// <summary>Name token(s) of an Epic Loot pseudo source, localized as a whole: "$ib_el_loottable $enemy_troll"
        /// ("Epic Loot drops: Troll"); ownerName = the owner entry's token, null = readable prefab. Null for other ids.</summary>
        public static string EpicLootName(string id, Func<string, string> ownerName)
        {
            var owner = LootTableOwner(id);
            if (owner != null) return $"$ib_el_loottable {ownerName(owner) ?? SetName(owner)}"; // "Tier3EliteMob": no prefab, set-style name
            return id != null && id.StartsWith(ItemSetPrefix, StringComparison.Ordinal) ? $"$ib_el_itemset {SetName(id.Substring(ItemSetPrefix.Length))}" : null;
        }

        // Epic Loot set kinds (baseconfig loottables.json ItemSets + our MT_ patch sets) with a $ib_el_set_<kind> token.
        private static readonly HashSet<string> SetKinds = new HashSet<string>(StringComparer.Ordinal)
        {
            "mats", "runestone", "weapons", "armor", "shields", "tools", "trinkets", "utility", "everything",
            "enchantingmats", "enchantingrunestones", "modutility", "shardunique", "shardslotchisel", "bossgems",
            "mob", "elitemob" // loot tables Tier<N>Mob / Tier<N>EliteMob (no owner prefab)
        };

        // "Tier1Mats" -> "$ib_el_set_mats, $ib_el_tier 1"; "MT_BossGems" -> "$ib_el_set_bossgems". A kind without a token
        // (another mod's set) falls back to the split words, digits kept (run 20261108-043049 flagged "Tier2weapons").
        public static string SetName(string name)
        {
            var m = System.Text.RegularExpressions.Regex.Match(name, "^(?:MT_)?(?:Tier(\\d+))?(.+)$");
            var kind = m.Groups[2].Value.Replace("_", "").ToLowerInvariant();
            if (SetKinds.Contains(kind))
                return m.Groups[1].Success ? $"$ib_el_set_{kind}, $ib_el_tier {m.Groups[1].Value}" : $"$ib_el_set_{kind}";
            var s = System.Text.RegularExpressions.Regex.Replace(name, "(?<=[a-z])(?=[A-Z0-9])|(?<=[0-9])(?=[A-Za-z])|_", " ").Trim();
            return s.Length == 0 ? name : char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();
        }

        public static string Readable(string id)
        {
            if (string.IsNullOrEmpty(id)) return id;
            var words = new List<string>();
            var variants = new List<string>();
            foreach (var raw in Split(id))
            {
                var w = raw.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
                if (w.Length == 0) continue;
                if (Variants.Contains(w)) { variants.Add(w.ToLowerInvariant()); continue; }
                if (Words.TryGetValue(w, out var mapped)) { if (mapped.Length > 0) words.Add(mapped); continue; }
                words.Add(w.ToLowerInvariant());
            }
            if (words.Count == 0) return id;
            var s = string.Join(" ", words);
            s = char.ToUpperInvariant(s[0]) + s.Substring(1);
            return variants.Count == 0 ? s : $"{s} ({string.Join(", ", variants)})";
        }

        // Splits on '_', '-', ' ', parentheses and lower->upper camel steps.
        private static IEnumerable<string> Split(string id)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < id.Length; i++)
            {
                var c = id[i];
                var cut = c == '_' || c == '-' || c == ' ' || c == '(' || c == ')';
                if (!cut && i > 0 && char.IsUpper(c) && char.IsLower(id[i - 1])) { if (sb.Length > 0) yield return sb.ToString(); sb.Clear(); }
                if (cut) { if (sb.Length > 0) yield return sb.ToString(); sb.Clear(); continue; }
                sb.Append(c);
            }
            if (sb.Length > 0) yield return sb.ToString();
        }
    }
}
