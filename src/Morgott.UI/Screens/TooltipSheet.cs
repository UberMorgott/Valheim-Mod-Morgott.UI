using System;
using System.Collections.Generic;
using System.Text;

namespace Morgott.UI.Screens
{
    // Item tooltip "Variant A" (user-approved mockup docs/mockups/tooltip/v2-a.png, 2026-11-08): one narrow column split by
    // thin accent rules with small accent section labels: header (icon, rarity-coloured name, "rarity · type", numbered
    // quality pips) / main stats (label left, value right) + requirements / magic effects / set / description (dimmed) /
    // footer (durability bar, weight, value, key hints). Pure: the header, effects, set, footer come from structured data
    // (SheetData, filled from ItemData + Epic Loot's ItemTooltipData by TooltipSheetGather); only the stat rows come from
    // the raw GetTooltip text, because the game builds them inline as "$token: <value>" lines (ItemDrop.cs:859-1090,
    // HitData.GetTooltipString) and Epic Loot's magic renderer writes the same lines with its modified values.
    // Rules, the icon and the durability bar are TMP links (LinkRule, LinkIcon, LinkBar) that TooltipDecor draws as images.
    public static class TooltipSheet
    {
        public const string Marker = "<link=\"mui:sheet\"></link>";
        public const string LinkRule = "mui:rule";
        public const string LinkIcon = "mui:icon:";   // + cache id
        public const string LinkHeaderEnd = "mui:hdr";
        public const string LinkBar = "mui:bar:";     // + fill in 1/1000
        public const string LinkBarValue = "mui:barval";
        private const string Nbsp = " ";

        public sealed class Effect { public string Text; public string Color; }
        public sealed class Piece { public string Name; public bool On; }
        public sealed class Bonus { public int Count; public string Text; public bool Active; }
        public sealed class Set
        {
            public string Name; public int Equipped, Full;
            public List<Piece> Pieces = new List<Piece>();
            public List<Bonus> Bonuses = new List<Bonus>();
        }

        public sealed class SheetData
        {
            public string Name, NameColor, Subtitle, Description;
            public int IconId = -1;                 // TooltipDecor icon cache id, -1 = none
            public int Quality, MaxQuality;
            public float Durability = -1f, MaxDurability; // < 0 = no durability
            public bool Indestructible;
            public string Weight, Value;           // formatted numbers, null = hidden
            public string MagicColor;              // effect bullets / magic label colour (rarity), null = Style.Accent
            public List<Effect> Effects = new List<Effect>();
            public string Sockets;                 // "used/count", null = none
            public List<Set> Sets = new List<Set>();
            public List<string> Hints = new List<string>(); // "[RMB] $ui_tip_equip" (key labels already Latin)
            public List<string> DropLines = new List<string>(); // exact lines of the raw text shown elsewhere (Epic Loot set label)
            public string ElEffectsColor, ElSetColor; // Epic Loot drew its effects / set blocks into the raw text: stripped
        }

        // Colours = 8 hex digits (RGBA, no '#'); sizes = percent of the text size; IconIndent in text units.
        public sealed class Style
        {
            public string Accent = "4FE0E6FF", Value = "4FE0E6FF", Dim = "A3ADB3FF", Description = "A8AFB4FF", Body = "E3E6E8FF";
            public string PipOn = "4FE0E6FF", PipOff = "4FE0E633", PipDigit = "06090BFF", PipDigitOff = "A3ADB3FF", Key = "FFFFFF26";
            public int NameSize = 125, LabelSize = 82, SmallSize = 90, RuleSize = 45;
            public float IconIndent = 72f;
        }

        // Vanilla stat tokens shown as label / value rows (ItemDrop.cs:884-1089, HitData.cs:508-603, Player.s_equipmentModifierTooltips).
        private static readonly HashSet<string> RowTokens = new HashSet<string>(StringComparer.Ordinal)
        {
            "inventory_damage", "inventory_blunt", "inventory_slash", "inventory_pierce", "inventory_fire", "inventory_frost",
            "inventory_lightning", "inventory_poison", "inventory_spirit", "item_damagemultipliertotal", "item_damagemultiplierhp",
            "item_staminause", "item_eitruse", "item_healthuse", "item_healthhitreturn", "item_eitrhitreturn", "item_staminahold",
            "item_armor", "item_blockarmor", "item_blockforce", "item_parrybonus", "item_parryadrenaline", "inventory_dmgmod",
            "item_knockback", "item_backstab", "item_eitrregen_modifier", "item_movement_modifier", "base_item_modifier",
            "item_heat_modifier", "se_jumpstamina", "se_attackstamina", "se_blockstamina", "se_dodgestamina", "se_swimstamina",
            "se_sneakstamina", "se_runstamina", "item_maxadrenaline", "item_food_health", "item_food_stamina", "item_food_eitr",
            "item_food_duration", "item_food_regen", "item_crafter",
        };

        // Shown from structured data (header / footer): their raw lines are dropped.
        private static readonly HashSet<string> MetaTokens = new HashSet<string>(StringComparer.Ordinal)
        {
            "item_onehanded", "item_twohanded", "item_quality", "item_durability", "item_weight", "item_value",
        };

        private const string Requirement = "item_repairlevel";
        private const string VanillaSet = "$item_seteffect";
        private const string ElRarityLine = "$mod_epicloot_itemtooltip_rarity:";
        private const string ElSetHeader = "> $mod_epicloot_set:";

        // Localization.FindNextWord word ends.
        private static readonly char[] EndChars = " (){}[]+-!?/\\&%,.:-=<>\n".ToCharArray();

        public static bool IsSheet(string text) => text != null && text.StartsWith(Marker, StringComparison.Ordinal);

        // The token of a raw line: "$token: ..." (colon right after the word) or a bare handedness line; null otherwise.
        public static string Token(string line)
        {
            if (string.IsNullOrEmpty(line) || line[0] != '$') return null;
            var end = line.IndexOfAny(EndChars, 1);
            var token = end < 0 ? line.Substring(1) : line.Substring(1, end - 1);
            if (end < 0) return token == "item_onehanded" || token == "item_twohanded" ? token : null;
            return line[end] == ':' ? token : null;
        }

        public static string Compose(string raw, SheetData d, Style s)
        {
            raw ??= "";
            var body = StripElBlocks(raw, d.ElEffectsColor, d.ElSetColor);
            if (!string.IsNullOrEmpty(d.Description))
            {
                var at = body.IndexOf(d.Description, StringComparison.Ordinal);
                if (at >= 0 && (at == 0 || body[at - 1] == '\n')) body = body.Remove(at, d.Description.Length);
            }

            var rows = new List<string>();
            var reqs = new List<string>();
            var setLines = new List<string>();
            var inSet = false;
            foreach (var line in body.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                {
                    inSet = false;
                    if (rows.Count > 0 && rows[rows.Count - 1] != Spacer(s)) rows.Add(Spacer(s));
                    continue;
                }
                if (d.DropLines.Contains(trimmed)) continue;
                if (inSet || trimmed.StartsWith(VanillaSet, StringComparison.Ordinal)) { inSet = true; setLines.Add(line); continue; }
                var token = Token(trimmed);
                if (token != null && MetaTokens.Contains(token)) continue;
                if (token == Requirement) reqs.Add(Row(trimmed, s, s.Accent, s.Accent));
                else if (token != null && RowTokens.Contains(token)) rows.Add(Row(trimmed, s, s.Body, s.Value));
                else rows.Add(line);
            }
            while (rows.Count > 0 && rows[rows.Count - 1] == Spacer(s)) rows.RemoveAt(rows.Count - 1);

            var sb = new StringBuilder(raw.Length + 1024).Append(Marker);
            Header(sb, d, s);
            if (rows.Count + reqs.Count > 0)
            {
                Rule(sb, s);
                foreach (var r in rows) sb.Append('\n').Append(r);
                foreach (var r in reqs) sb.Append('\n').Append(r);
            }
            if (d.Effects.Count > 0 || d.Sockets != null)
            {
                var magic = d.MagicColor ?? s.Accent;
                Section(sb, s, "$ui_tip_magic", magic);
                foreach (var e in d.Effects)
                    sb.Append("\n<color=#").Append(e.Color ?? magic).Append(">◆</color> ").Append(e.Text);
                if (d.Sockets != null) sb.Append("\n<color=#").Append(s.Dim).Append(">$ui_tip_sockets ").Append(d.Sockets).Append("</color>");
            }
            foreach (var set in d.Sets)
            {
                Section(sb, s, "$ui_tip_set " + set.Name + "  (" + set.Equipped + "/" + set.Full + ")", s.Accent);
                foreach (var p in set.Pieces)
                    sb.Append("\n<color=#").Append(p.On ? s.Body : s.Dim).Append(">  ").Append(p.Name).Append("</color>");
                foreach (var b in set.Bonuses)
                    sb.Append("\n<color=#").Append(b.Active ? s.Accent : s.Dim).Append(">(").Append(b.Count).Append(")  ").Append(b.Text).Append("</color>");
            }
            if (d.Sets.Count == 0 && setLines.Count > 0)
            {
                Section(sb, s, null, s.Accent);
                foreach (var l in setLines) sb.Append('\n').Append(l);
            }
            if (!string.IsNullOrEmpty(d.Description))
            {
                Rule(sb, s);
                sb.Append("\n<i><color=#").Append(s.Description).Append('>').Append(d.Description.Trim('\n')).Append("</color></i>");
            }
            Footer(sb, d, s);
            return sb.ToString();
        }

        private static string Spacer(Style s) => "<size=" + s.RuleSize + "%>" + Nbsp + "</size>";

        private static void Header(StringBuilder sb, SheetData d, Style s)
        {
            var indent = d.IconId >= 0 && s.IconIndent > 0f;
            if (indent) sb.Append("<indent=").Append(s.IconIndent.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)).Append('>');
            sb.Append("<link=\"").Append(LinkIcon).Append(d.IconId).Append("\">").Append(Nbsp).Append("</link>");
            sb.Append("<size=").Append(s.NameSize).Append("%><b>");
            if (d.NameColor != null) sb.Append("<color=#").Append(d.NameColor).Append('>').Append(d.Name).Append("</color>");
            else sb.Append(d.Name);
            sb.Append("</b></size>");
            if (!string.IsNullOrEmpty(d.Subtitle)) sb.Append('\n').Append(d.Subtitle);
            if (d.MaxQuality > 1)
            {
                sb.Append("\n$ui_tip_quality ").Append(d.Quality).Append("  ");
                for (var q = 1; q <= d.MaxQuality; q++)
                {
                    var on = q <= d.Quality;
                    sb.Append("<mark=#").Append(on ? s.PipOn : s.PipOff).Append("><color=#").Append(on ? s.PipDigit : s.PipDigitOff)
                      .Append('>').Append(Nbsp).Append(q).Append(Nbsp).Append("</color></mark> ");
                }
            }
            sb.Append("<link=\"").Append(LinkHeaderEnd).Append("\">").Append(Nbsp).Append("</link>");
            if (indent) sb.Append("</indent>");
        }

        private static void Rule(StringBuilder sb, Style s) =>
            sb.Append("\n<size=").Append(s.RuleSize).Append("%><link=\"").Append(LinkRule).Append("\">").Append(Nbsp).Append("</link></size>");

        private static void Section(StringBuilder sb, Style s, string label, string color)
        {
            Rule(sb, s);
            if (label == null) return;
            sb.Append("\n<size=").Append(s.LabelSize).Append("%><b><uppercase><color=#").Append(color).Append('>')
              .Append(label).Append("</color></uppercase></b></size>");
        }

        private static void Footer(StringBuilder sb, SheetData d, Style s)
        {
            var bar = d.Durability >= 0f && d.MaxDurability > 0f;
            if (!bar && !d.Indestructible && d.Weight == null && d.Value == null && d.Hints.Count == 0) return;
            Rule(sb, s);
            if (bar)
            {
                var fill = (int)Math.Round(Math.Max(0f, Math.Min(1f, d.Durability / d.MaxDurability)) * 1000f);
                sb.Append("\n<link=\"").Append(LinkBar).Append(fill).Append("\">").Append(Nbsp).Append("</link>")
                  .Append(RightCell("<link=\"" + LinkBarValue + "\">" + Math.Round(d.Durability) + "/" + Math.Round(d.MaxDurability) + "</link>", s.Body));
            }
            else if (d.Indestructible) sb.Append("\n<color=#").Append(s.Accent).Append(">$ui_tip_indestructible</color>");
            var parts = new List<string>();
            if (d.Weight != null) parts.Add("$ui_tip_weight " + d.Weight);
            if (d.Value != null) parts.Add("$ui_tip_value " + d.Value);
            if (d.MaxQuality > 1) parts.Add("$ui_tip_upgrade " + d.Quality + "/" + d.MaxQuality);
            if (parts.Count > 0)
                sb.Append("\n<size=").Append(s.SmallSize).Append("%>").Append(string.Join("<color=#" + s.Dim + ">  |  </color>", parts)).Append("</size>");
            if (d.Hints.Count > 0)
            {
                sb.Append("\n<size=").Append(s.SmallSize).Append("%>");
                for (var i = 0; i < d.Hints.Count; i++)
                    sb.Append(i > 0 ? "    " : "").Append(d.Hints[i].Replace("[", "<mark=#" + s.Key + ">[").Replace("]", "]</mark>"));
                sb.Append("</size>");
            }
        }

        // "$label: value": label left, value right-aligned on the same line (line-height 0 before the break).
        private static string Row(string line, Style s, string labelColor, string valueColor)
        {
            var colon = line.IndexOf(':');
            var label = line.Substring(0, colon);
            var value = Plain(line.Substring(colon + 1).Trim());
            return "<color=#" + labelColor + ">" + label + "</color>" + RightCell(value, valueColor);
        }

        private static string RightCell(string value, string color) =>
            "<line-height=0>\n<align=right><color=#" + color + ">" + value + "</color></align></line-height>";

        // Drops the game's / Epic Loot's value colours (orange / yellow / light blue) so the theme's value colour shows;
        // any other colour (food health / stamina / eitr) carries meaning and stays.
        public static string Plain(string value)
        {
            foreach (var keep in new[] { "<color=#ff", "<color=#9090" })
                if (value.IndexOf(keep, StringComparison.OrdinalIgnoreCase) >= 0) return value;
            var sb = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] == '<' && (string.CompareOrdinal(value, i, "<color=", 0, 7) == 0 || string.CompareOrdinal(value, i, "</color>", 0, 8) == 0))
                {
                    var close = value.IndexOf('>', i);
                    if (close > i) { i = close; continue; }
                }
                sb.Append(value[i]);
            }
            return sb.ToString().Trim();
        }

        // Epic Loot's own effect and set blocks (TooltipRenderer.RenderEffects / RenderSets, 0.15.6) inside its text:
        // replaced by the structured sections. Effects block = "\n<color=RARITY>" ... the "$mod_epicloot_itemtooltip_rarity:"
        // line it always ends with. Set block = "\n\n<color=SETCOLOR> $mod_epicloot_set: ..." and every following line
        // that starts with a colour tag (pieces "  <color=", bonuses / details "<color="). Not found -> text unchanged.
        public static string StripElBlocks(string text, string effectsColor, string setColor)
        {
            if (effectsColor != null)
            {
                var end = text.IndexOf(ElRarityLine, StringComparison.Ordinal);
                if (end >= 0)
                {
                    var start = text.LastIndexOf("\n<color=" + effectsColor + ">", end, StringComparison.Ordinal);
                    var stop = text.IndexOf('\n', end);
                    if (start >= 0) text = text.Remove(start, (stop < 0 ? text.Length : stop) - start);
                }
            }
            if (setColor != null)
            {
                var head = text.IndexOf("\n\n<color=" + setColor + ElSetHeader, StringComparison.Ordinal);
                while (head >= 0)
                {
                    var stop = text.IndexOf('\n', head + 2);
                    while (stop >= 0 && stop + 1 < text.Length && text.Substring(stop + 1).TrimStart(' ').StartsWith("<color=", StringComparison.Ordinal))
                        stop = text.IndexOf('\n', stop + 1);
                    text = text.Remove(head, (stop < 0 ? text.Length : stop) - head);
                    head = text.IndexOf("\n\n<color=" + setColor + ElSetHeader, StringComparison.Ordinal);
                }
            }
            return text;
        }
    }
}
