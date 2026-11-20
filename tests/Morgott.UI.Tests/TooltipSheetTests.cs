using System.Linq;
using Morgott.UI.Screens;
using Xunit;

namespace Morgott.UI.Tests
{
    // Item tooltip sheet (Variant A) on raw GetTooltip shapes (ItemDrop.cs:859-1090) and Epic Loot 0.15.6 magic text.
    public class TooltipSheetTests
    {
        private static readonly TooltipSheet.Style S = new TooltipSheet.Style();

        [Fact]
        public void GemSubtitleIsFullRarityWordInItsColourWithoutTheName()
        {
            Assert.Equal("<color=#0070DDFF>$ui_tip_rarity_0</color> · $ui_tip_type_gem", TooltipSheet.GemSubtitle(0, "0070DDFF"));
            Assert.Equal("$ui_tip_rarity_3 · $ui_tip_type_gem", TooltipSheet.GemSubtitle(3, null));
            Assert.Equal("$ui_tip_type_gem", TooltipSheet.GemSubtitle(-1, "FFFFFFFF"));
        }

        [Fact]
        public void UniformGemDropsAllSlotsLabelPerSlotKeepsNames()
        {
            const string all = "$mod_epicloot_shard_ifsocketed:\n  <color=#A335EE>All slots: +5% armor</color>\n";
            Assert.Equal("$mod_epicloot_shard_ifsocketed:\n  <color=#A335EE>+5% armor</color>\n", TooltipSheet.DropAllSlots(all, "All slots"));
            const string per = "$mod_epicloot_shard_ifsocketed:\n  <color=#A335EE>Armor: +5% armor</color>\n  <color=#A335EE>Shield: +3 block</color>\n";
            Assert.Equal(per, TooltipSheet.DropAllSlots(per, "All slots"));
        }

        private const string Axe =
            "$item_axe_bronze_description\n" +
            "\n$item_onehanded" +
            "\n$item_weight: <color=orange>2.0</color>" +
            "\n$item_quality: <color=orange>3</color>" +
            "\n$item_durability: <color=orange>75%</color> <color=yellow>(150/200)</color>" +
            "\n$item_repairlevel: <color=orange>2</color>" +
            "\n$inventory_slash: <color=orange>40</color> <color=yellow>(30-40) </color>" +
            "\n$item_staminause: <color=orange>8</color>" +
            "\n$item_blockarmor: <color=orange>10</color> <color=yellow>(10)</color>";

        private static TooltipSheet.SheetData Data() => new TooltipSheet.SheetData
        {
            Name = "$item_axe_bronze",
            Description = "$item_axe_bronze_description",
            Subtitle = "$ui_tip_type_onehandedweapon",
            IconId = 0,
            Quality = 3,
            MaxQuality = 4,
            Durability = 150f,
            MaxDurability = 200f,
            Weight = "2.0",
        };

        private static int At(string text, string part)
        {
            var i = text.IndexOf(part, System.StringComparison.Ordinal);
            Assert.True(i >= 0, "missing: " + part);
            return i;
        }

        [Fact]
        public void BackpackContentsWeightIsARow()
        {
            var raw = "$d\n\n$item_weight: <color=orange>4.0</color>\n$adventurebackpacks_contents_weight: <color=orange>−50%</color>";
            var r = TooltipSheet.Compose(raw, Data(), S);
            Assert.Contains("<color=#" + S.Body + ">$adventurebackpacks_contents_weight</color><line-height=0>\n<align=right><color=#" + S.Value + ">−50%</color>", r);
        }

        [Fact]
        public void DamageModRowsLabelTheDamageType()
        {
            // SE_Stats.GetDamageModifiersTooltipString shape (ArmorRootLegs: weak fire, resistant pierce).
            var raw = "$d\n\n$item_armor: <color=orange>20</color>" +
                      "\n$inventory_dmgmod: <color=orange>$inventory_weak</color> VS <color=orange>$inventory_fire</color>" +
                      "\n$inventory_dmgmod: <color=orange>$inventory_resistant</color> VS <color=orange>$inventory_pierce</color>";
            var r = TooltipSheet.Compose(raw, Data(), S);
            string Pair(string label, string value) =>
                "<color=#" + S.Body + ">" + label + "</color><line-height=0>\n<align=right><color=#" + S.Value + ">" + value + "</color>";
            Assert.True(At(r, Pair("$inventory_fire", "$inventory_weak")) < At(r, Pair("$inventory_pierce", "$inventory_resistant")));
            Assert.DoesNotContain("$inventory_dmgmod", r);
            Assert.DoesNotContain(" VS ", r);
        }

        [Fact]
        public void VanillaWeaponSectionsInOrder()
        {
            var r = TooltipSheet.Compose(Axe, Data(), S);
            Assert.True(TooltipSheet.IsSheet(r));
            var order = new[]
            {
                "mui:icon:0", "$item_axe_bronze", "$ui_tip_type_onehandedweapon", "$ui_tip_quality 3", "mui:hdr",
                "$inventory_slash", "$item_staminause", "$item_blockarmor", "$item_repairlevel",
                "em><i><color=#" + S.Description + ">$item_axe_bronze_description", "mui:bar:750", "150/200", "$ui_tip_weight 2.0", "$ui_tip_upgrade 3/4",
            }.Select(p => At(r, p)).ToList();
            Assert.Equal(order.OrderBy(i => i), order);
            // Meta lines shown from data, not twice; values without the game's orange / yellow.
            foreach (var gone in new[] { "$item_weight:", "$item_quality:", "$item_durability:", "$item_onehanded", "orange", "yellow" })
                Assert.DoesNotContain(gone, r);
            Assert.Contains("<align=right><color=#" + S.Value + ">40 (30-40)</color>", r);
            // Numbered pips: 3 on, 1 off.
            Assert.Equal(3, r.Split("<mark=#" + S.PipOn + ">").Length - 1);
            Assert.Contains(" 4 ", r);
        }

        [Fact]
        public void ElMagicBlocksReplacedBySections()
        {
            const string rarity = "#00abff";
            const string set = "#26ffff";
            var raw = "<color=#26ffff>$mod_epicloot_legendaryset</color>\n$item_desc\n" +
                      "\n$item_twohanded\n$item_weight: <color=#add8e6ff>4.0</color>" +
                      "\n$inventory_slash: <color=#add8e6ff>92</color>" +
                      "\n<color=" + rarity + ">● +12% fire\n● Lifesteal 3%\n</color>$mod_epicloot_itemtooltip_rarity: <color=" + rarity + ">Rare</color><pos=75%>$mod_epicloot_itemtooltip_effects: <color=" + rarity + ">2</color>\n" +
                      "\n\n<color=" + set + "> $mod_epicloot_set: Jarl (2/4):</color>\n  <color=white>Helm</color>\n  <color=#808080ff>Legs</color>\n<color=#808080ff>(4) ‣ Rage</color>" +
                      "\n$item_fulladrenaline: <color=orange>x</color>";
            var d = new TooltipSheet.SheetData
            {
                Name = "Blood axe",
                NameColor = "00ABFFFF",
                Subtitle = "Rare · Axe",
                Description = "$item_desc",
                MagicColor = "00ABFFFF",
                ElEffectsColor = rarity,
                ElSetColor = set,
                Sockets = "1/2",
            };
            d.DropLines.Add("$mod_epicloot_legendaryset");
            d.Effects.Add(new TooltipSheet.Effect { Text = "+12% fire" });
            d.Effects.Add(new TooltipSheet.Effect { Text = "Lifesteal 3%" });
            var s = new TooltipSheet.SetSection { Name = "Jarl", Equipped = 2, Full = 4 };
            s.Pieces.Add(new TooltipSheet.Piece { Name = "Helm", On = true });
            s.Pieces.Add(new TooltipSheet.Piece { Name = "Legs" });
            s.Bonuses.Add(new TooltipSheet.Bonus { Count = 4, Text = "Rage" });
            d.Sets.Add(s);

            var r = TooltipSheet.Compose(raw, d, S);
            foreach (var gone in new[] { "$mod_epicloot_itemtooltip_rarity", "$mod_epicloot_set:", "●", "$mod_epicloot_legendaryset", "#808080ff" })
                Assert.DoesNotContain(gone, r);
            var order = new[]
            {
                "Blood axe", "$inventory_slash", "$item_fulladrenaline", "$ui_tip_magic", "+12% fire", "$ui_tip_sockets 1/2",
                "$ui_tip_set Jarl", "<b>2/4</b>", "Helm", "<color=#" + S.Dim + ">  Legs", "<color=#" + S.Dim + ">(4)  Rage", "$item_desc",
            }.Select(p => At(r, p)).ToList();
            Assert.Equal(order.OrderBy(i => i), order);
        }

        [Fact]
        public void SocketedGemEffectUnderSocketCount()
        {
            var d = new TooltipSheet.SheetData { Name = "Mail", MagicColor = "00ABFFFF", Sockets = "1/1" };
            d.Effects.Add(new TooltipSheet.Effect { Text = "+1% armor" });
            d.SocketEffects.Add(new TooltipSheet.Effect { Text = "Eitr shield 8%", Color = "FFB75EFF" });
            var r = TooltipSheet.Compose("", d, S);
            Assert.True(At(r, "$ui_tip_sockets 1/1") < At(r, "<color=#FFB75EFF>◇ Eitr shield 8%</color>"));
        }

        [Fact]
        public void VanillaSetParagraphWithoutEpicLoot()
        {
            var raw = "$d\n\n$item_armor: <color=orange>20</color>\n\n$item_seteffect (<color=orange>3</color> $item_parts):<color=orange>$se_troll</color>\n$se_sneak: +15";
            var r = TooltipSheet.Compose(raw, new TooltipSheet.SheetData { Name = "$n", Description = "$d" }, S);
            Assert.True(At(r, "$item_armor") < At(r, "$item_seteffect"));
            Assert.True(At(r, "$se_sneak: +15") < At(r, "<i>"));
        }

        [Fact]
        public void PlainKeepsMeaningfulColours()
        {
            Assert.Equal("40 (30-40)", TooltipSheet.Plain("<color=orange>40</color> <color=yellow>(30-40) </color>"));
            var food = "<color=#ff8080ff>20</color>  ($item_current:<color=yellow>25</color>)";
            Assert.Equal(food, TooltipSheet.Plain(food));
            var r = TooltipSheet.Compose("$item_movement_modifier: <color=orange>-5%</color> ($item_total:<color=yellow>+0%</color>)", new TooltipSheet.SheetData { Name = "$n" }, S);
            Assert.Contains(">-5%</color></align>", r);
            Assert.DoesNotContain("$item_total", r);
        }

        private static string RowOf(string label, string value) =>
            "<color=#" + S.Body + ">" + label + "</color><line-height=0>\n<align=right><color=#" + S.Value + ">" + value + "</color>";

        private static int Count(string text, string part) => text.Split(part).Length - 1;

        private static TooltipSheet.EffectBlock Equip(string name, string text) => new TooltipSheet.EffectBlock
        {
            Label = TooltipSheet.EffectLabel,
            Name = name,
            Printed = name + "\n" + text,
            Text = text,
        };

        [Theory]
        [InlineData("orange")]
        [InlineData("#add8e6ff")] // Epic Loot's Finish recolours every orange / yellow tag
        public void RingEffectBecomesRowsOnceInItsSection(string color)
        {
            const string se = "$se_eitrregen: <color=orange>+4%</color>\nStealth <color=orange>+5</color>\n$se_ttl: <color=orange>60</color>\n";
            var raw = ("$d\n\n<color=orange>$item_utility</color>\n$item_weight: <color=orange>0.0</color>\n\n\n<color=orange>$se_ring</color>\n" + se +
                       "\n" + TooltipSheet.SkillDamageLine("$skill_elementalmagic", 1.03f)).Replace("orange", color);
            var d = new TooltipSheet.SheetData { Name = "$n", Description = "$d" };
            d.DropExact.Add("$item_utility");
            d.Blocks.Add(Equip("$se_ring", se));
            var r = TooltipSheet.Compose(raw, d, S);

            Assert.True(At(r, "$ui_tip_effect: $se_ring") < At(r, "<i>"));
            Assert.Contains(RowOf("$se_eitrregen", "+4%"), r);
            Assert.Contains(RowOf("Stealth", "+5"), r);
            Assert.Contains(RowOf("$se_ttl", "60 $ui_stats_sec"), r);
            Assert.Contains(RowOf("$ui_tip_skill_damage", "$skill_elementalmagic +3%"), r);
            Assert.Equal(1, Count(r, "$se_ring"));
            Assert.Equal(1, Count(r, "$se_eitrregen"));
            Assert.DoesNotContain("$item_utility", r);
        }

        [Fact]
        public void UnmatchedBlockKeepsTheRawText()
        {
            var d = new TooltipSheet.SheetData { Name = "$n" };
            d.Blocks.Add(Equip("$se_x", "$se_healthregen: <color=orange>+9%</color>\n"));
            var r = TooltipSheet.Compose("$d\n\nSome mod's line\n<color=orange>$se_x</color>\n$se_healthregen: <color=orange>+2%</color>", d, S);
            Assert.Contains("Some mod's line", r);
            Assert.Contains("$se_healthregen: <color=orange>+2%</color>", r); // the stale block is not invented, the raw one stays
            Assert.DoesNotContain("$ui_tip_effect", r);
            Assert.DoesNotContain("+9%", r);
        }

        [Fact]
        public void EffectTextEqualToDescriptionShownOnce()
        {
            var d = new TooltipSheet.SheetData { Name = "$n", Description = "$d" };
            d.Blocks.Add(new TooltipSheet.EffectBlock { Label = TooltipSheet.EffectLabel, Printed = "$n\n$d\n\n", Text = "$d\n\n" });
            Assert.Equal(1, Count(TooltipSheet.Compose("$d\n\n<color=orange>$n</color>\n$d\n\n", d, S), "$d"));
        }

        [Fact]
        public void FullAdrenalineWrappedBlockAndFactsGetSections()
        {
            const string se = "$se_x_description\n\n$se_health_upfront: <color=orange>100</color>\n";
            var raw = "$d\n\n$item_maxadrenaline: <color=orange>50</color>\n$item_fulladrenaline: <color=orange>" + se + "</color>" +
                      "\n\n<color=orange>$ui_adr_title</color>\n$ui_adr_full: <color=orange>$ui_adr_reset</color>";
            var d = new TooltipSheet.SheetData { Name = "$n", Description = "$d" };
            d.Blocks.Add(new TooltipSheet.EffectBlock { Label = TooltipSheet.FullAdrenalineLabel, Printed = "$item_fulladrenaline: " + se, Text = se });
            var r = TooltipSheet.Compose(raw, d, S);
            var order = new[] { "$item_maxadrenaline", "$ui_tip_full_adrenaline", "$se_x_description", "$se_health_upfront", "$ui_adr_title", "$ui_adr_full" }
                .Select(p => At(r, p)).ToList();
            Assert.Equal(order.OrderBy(i => i), order);
            Assert.DoesNotContain("$item_fulladrenaline", r);
            Assert.DoesNotContain("orange", r);
        }

        [Fact]
        public void OwnerRowsMakeTheEffectSectionAndPetItemDropsHints()
        {
            var d = new TooltipSheet.SheetData { Name = "$n", Subtitle = "$ui_tip_type_utility" };
            d.Hints.Add("[RMB] $ui_tip_equip");
            d.Effects.Add(new TooltipSheet.Effect { Text = "<color=#808080>Fire $no</color>" });
            var r = TooltipSheet.Compose("$su_collar_pet_health: <color=orange>+25%</color>\n$ui_tip_pet_item: <color=orange>$kind</color>\nUnknown line", d, S);
            Assert.True(At(r, "$kind") < At(r, "$ui_tip_effect"));
            Assert.Contains(RowOf("$su_collar_pet_health", "+25%"), r);
            Assert.Contains("Unknown line", r);
            Assert.DoesNotContain("$ui_tip_equip", r);
            Assert.DoesNotContain("$ui_tip_type_utility", r);
            Assert.DoesNotContain("#808080", r);
            Assert.Contains("<color=#" + S.Dim + ">◆</color> <color=#" + S.Dim + ">Fire $no</color>", r);
        }

        [Fact]
        public void TokenRules()
        {
            Assert.Equal("item_twohanded", TooltipSheet.Token("$item_twohanded"));
            Assert.Null(TooltipSheet.Token("$item_weight"));
            Assert.Null(TooltipSheet.Token("<color=orange>$item_noteleport</color>"));
            Assert.Equal("inventory_spirit", TooltipSheet.Token("$inventory_spirit: <color=orange>5</color>"));
        }
    }
}
