using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Morgott.UI.Screens
{
    // Item tooltip text laid out in scannable groups instead of one line per stat (user 2026-11-08, design with Codex).
    // Works on the raw (pre-Localize) text of ItemDrop.ItemData.GetTooltip (ItemDrop.cs:859-1090): every stat line there
    // is "\n$token: <value>" with a fixed vanilla token. Recognized lines are grouped (meta, attack, costs, defense,
    // modifiers, food) and paired two per row with TMP <pos=50%> when both cells fit their column; small groups are
    // shrunk; the description is dimmed. Barriers keep everything else in place: any unknown line (mod / Epic Loot text,
    // warnings like $item_noteleport, DLC, subtitles) and the first blank line after the stats (status effect, chain,
    // projectile and set-effect paragraphs, which carry their own stat lines) are emitted verbatim in their order.
    public static class TooltipLayout
    {
        public enum Group { None = -1, Meta, Attack, Cost, Defense, Modifier, Food, FoodTime }

        public const string Column = "<pos=50%>";
        public const string Small = "<size=88%>";
        public const string Spacer = "<size=40%> </size>";
        private const string DescSize = "<size=92%>";

        // Vanilla always writes the weight line into a full item tooltip (ItemDrop.cs:897-907, !appending): the marker
        // that the text is one, so other tooltips that happen to contain stat tokens stay untouched.
        private const string Marker = "$item_weight:";

        // Localization.FindNextWord word ends (same set as DamageIconText).
        private static readonly char[] EndChars = " (){}[]+-!?/\\&%,.:-=<>\n".ToCharArray();

        private static readonly Dictionary<string, Group> Tokens = Build();

        private static Dictionary<string, Group> Build()
        {
            var d = new Dictionary<string, Group>(StringComparer.Ordinal);
            void Add(Group g, params string[] tokens) { foreach (var t in tokens) d[t] = g; }
            // ItemDrop.cs:884-933 (handedness, crafter, value, weight, quality, durability, repair station level).
            Add(Group.Meta, "item_onehanded", "item_twohanded", "item_weight", "item_quality", "item_durability", "item_repairlevel", "item_value", "item_crafter");
            // HitData.GetTooltipString (HitData.cs:508-603), ItemDrop.cs:949-956.
            Add(Group.Attack, "inventory_damage", "inventory_blunt", "inventory_slash", "inventory_pierce", "inventory_fire", "inventory_frost",
                "inventory_lightning", "inventory_poison", "inventory_spirit", "item_damagemultipliertotal", "item_damagemultiplierhp");
            // ItemDrop.cs:957-984.
            Add(Group.Cost, "item_staminause", "item_eitruse", "item_healthuse", "item_healthhitreturn", "item_eitrhitreturn", "item_staminahold");
            // ItemDrop.cs:1011 armor, AddBlockTooltip :832-856, SE_Stats.GetDamageModifiersTooltipString.
            Add(Group.Defense, "item_armor", "item_blockarmor", "item_blockforce", "item_parrybonus", "item_parryadrenaline", "inventory_dmgmod");
            // ItemDrop.cs:986-993, :1042; Player.s_equipmentModifierTooltips (Player.cs:142-145, :2604-2624).
            Add(Group.Modifier, "item_knockback", "item_backstab", "item_eitrregen_modifier", "item_movement_modifier", "base_item_modifier",
                "item_heat_modifier", "se_jumpstamina", "se_attackstamina", "se_blockstamina", "se_dodgestamina", "se_swimstamina",
                "se_sneakstamina", "se_runstamina", "item_maxadrenaline");
            // printConsumable (ItemDrop.cs:1062-1089).
            Add(Group.Food, "item_food_health", "item_food_stamina", "item_food_eitr");
            Add(Group.FoodTime, "item_food_duration", "item_food_regen");
            return d;
        }

        private static bool IsSmall(Group g) => g == Group.Meta || g == Group.Modifier;

        // The group of one raw line: "$token: ..." with a known token; handedness lines are the bare token.
        public static Group Classify(string line)
        {
            if (string.IsNullOrEmpty(line) || line[0] != '$') return Group.None;
            var end = line.IndexOfAny(EndChars, 1);
            var token = end < 0 ? line.Substring(1) : line.Substring(1, end - 1);
            if (!Tokens.TryGetValue(token, out var g)) return Group.None;
            if (end < 0) return token == "item_onehanded" || token == "item_twohanded" ? g : Group.None;
            return line[end] == ':' ? g : Group.None;
        }

        // measure(cell) = rendered width of one cell (localized, icons included); column = width a cell may take.
        // descColor = 8-digit hex for the description (null = not dimmed). Not an item tooltip -> the same instance.
        public static string Compose(string raw, Func<string, float> measure, float column, string descColor)
        {
            if (string.IsNullOrEmpty(raw) || !raw.Contains(Marker) || raw.Contains(Column))
                return raw;
            var lines = raw.Split('\n');
            var first = Array.FindIndex(lines, l => Classify(l) != Group.None);
            if (first < 0) return raw;
            var end = Array.FindIndex(lines, first, l => l.Length == 0);
            if (end < 0) end = lines.Length;

            var outLines = new List<string>(lines.Length);
            for (var i = 0; i < first; i++)
                outLines.Add(i == 0 && lines[0].Length > 0 && descColor != null
                    ? DescSize + "<color=#" + descColor + ">" + lines[0] + "</color></size>"
                    : lines[i]);

            var run = new List<string>();
            for (var i = first; i < end; i++)
            {
                if (Classify(lines[i]) != Group.None) { run.Add(lines[i]); continue; }
                Flush(run, outLines, measure, column);
                outLines.Add(lines[i]);
            }
            Flush(run, outLines, measure, column);
            for (var i = end; i < lines.Length; i++) outLines.Add(lines[i]);
            return string.Join("\n", outLines);
        }

        // One run of recognized lines: stable-ordered by group, a spacer between groups, cells paired when both fit.
        private static void Flush(List<string> run, List<string> outLines, Func<string, float> measure, float column)
        {
            if (run.Count == 0) return;
            var groups = run.GroupBy(Classify).OrderBy(g => (int)g.Key).ToList();
            for (var gi = 0; gi < groups.Count; gi++)
            {
                if (gi > 0) outLines.Add(Spacer);
                var small = IsSmall(groups[gi].Key);
                var cells = groups[gi].Select(l => small ? Small + l + "</size>" : l).ToList();
                for (var i = 0; i < cells.Count; i++)
                {
                    if (i + 1 < cells.Count && column > 0f && measure(cells[i]) <= column && measure(cells[i + 1]) <= column)
                    {
                        outLines.Add(new StringBuilder(cells[i]).Append(Column).Append(cells[i + 1]).ToString());
                        i++;
                    }
                    else outLines.Add(cells[i]);
                }
            }
            run.Clear();
        }
    }
}
