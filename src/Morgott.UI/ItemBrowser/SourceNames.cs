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
