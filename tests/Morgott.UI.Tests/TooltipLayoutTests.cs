using Morgott.UI.Screens;
using Xunit;

namespace Morgott.UI.Tests
{
    // Item tooltip grouping (TooltipLayout) on raw GetTooltip shapes (ItemDrop.cs:859-1090) of each item type.
    public class TooltipLayoutTests
    {
        private const string C = TooltipLayout.Column;
        private const string S = TooltipLayout.Small;
        private const string Gap = TooltipLayout.Spacer;

        // Width = char count; wide column pairs everything, zero column pairs nothing.
        private static string Wide(string raw) => TooltipLayout.Compose(raw, s => s.Length, 1000f, null);
        private static string Narrow(string raw) => TooltipLayout.Compose(raw, s => s.Length, 0f, null);

        // Crude Crossbow shape: handedness, meta, pierce damage, block lines, knockback, backstab, movement modifier.
        private const string Crossbow =
            "$item_crossbow_description\n" +
            "\n$item_twohanded" +
            "\n$item_weight: <color=orange>4.0</color>" +
            "\n$item_quality: <color=orange>1</color>" +
            "\n$item_durability: <color=orange>100%</color> <color=yellow>(100/100)</color>" +
            "\n$item_repairlevel: <color=orange>1</color>" +
            "\n$inventory_pierce: <color=orange>40</color> <color=yellow>(20-40) </color>" +
            "\n$item_blockarmor: <color=orange>4</color> <color=yellow>(4)</color>" +
            "\n$item_parrybonus: <color=orange>1.5x</color>" +
            "\n$item_parryadrenaline: <color=orange>1</color>" +
            "\n$item_knockback: <color=orange>80</color>" +
            "\n$item_backstab: <color=orange>3x</color>" +
            "\n$item_movement_modifier: <color=orange>-5%</color>";

        [Fact]
        public void WeaponGroupsArePairedWithSpacers()
        {
            var expected = string.Join("\n",
                "$item_crossbow_description",
                "",
                S + "$item_twohanded</size>" + C + S + "$item_weight: <color=orange>4.0</color></size>",
                S + "$item_quality: <color=orange>1</color></size>" + C + S + "$item_durability: <color=orange>100%</color> <color=yellow>(100/100)</color></size>",
                S + "$item_repairlevel: <color=orange>1</color></size>",
                Gap,
                "$inventory_pierce: <color=orange>40</color> <color=yellow>(20-40) </color>",
                Gap,
                "$item_blockarmor: <color=orange>4</color> <color=yellow>(4)</color>" + C + "$item_parrybonus: <color=orange>1.5x</color>",
                "$item_parryadrenaline: <color=orange>1</color>",
                Gap,
                S + "$item_knockback: <color=orange>80</color></size>" + C + S + "$item_backstab: <color=orange>3x</color></size>",
                S + "$item_movement_modifier: <color=orange>-5%</color></size>");
            Assert.Equal(expected, Wide(Crossbow));
        }

        [Fact]
        public void CellsThatDoNotFitStayOnTheirOwnRow()
        {
            var lines = Narrow(Crossbow).Split('\n');
            Assert.DoesNotContain(lines, l => l.Contains(C));
            // Every original line survives, nothing is dropped.
            foreach (var l in Crossbow.Split('\n')) Assert.Contains(lines, o => o.Contains(l));
        }

        [Fact]
        public void PairingIsDecidedPerCellWidth()
        {
            // Long label measured over the column: that pair is split, the next short pair still joins.
            var raw = "d\n\n$item_weight: <color=orange>1.0</color>\n$item_quality: <color=orange>2</color>" +
                      "\n$inventory_blunt: <color=orange>55</color> <color=yellow>(30-55) </color>\n$inventory_fire: <color=orange>10</color>";
            var r = TooltipLayout.Compose(raw, s => s.Contains("blunt") ? 999f : 10f, 50f, null).Split('\n');
            Assert.Contains(S + "$item_weight: <color=orange>1.0</color></size>" + C + S + "$item_quality: <color=orange>2</color></size>", r);
            Assert.Contains("$inventory_blunt: <color=orange>55</color> <color=yellow>(30-55) </color>", r);
            Assert.Contains("$inventory_fire: <color=orange>10</color>", r);
        }

        [Fact]
        public void ArmorResistLinesGoToDefense()
        {
            var raw = "d\n\n$item_weight: <color=orange>10.0</color>\n$item_quality: <color=orange>1</color>" +
                      "\n$item_durability: <color=orange>100%</color>\n$item_armor: <color=orange>14</color>" +
                      "\n$inventory_dmgmod: <color=orange>$inventory_resistant</color> VS $inventory_frost" +
                      "\n$item_movement_modifier: <color=orange>-5%</color>";
            var r = Wide(raw).Split('\n');
            Assert.Contains("$item_armor: <color=orange>14</color>" + C + "$inventory_dmgmod: <color=orange>$inventory_resistant</color> VS $inventory_frost", r);
            Assert.Equal(S + "$item_movement_modifier: <color=orange>-5%</color></size>", r[r.Length - 1]);
        }

        [Fact]
        public void FoodStaysTogetherAndDurationIsItsOwnGroup()
        {
            var raw = "d\n\n$item_weight: <color=orange>0.5 (5.0 $item_total)</color>" +
                      "\n$item_food_health: <color=#ff8080ff>30</color>  ($item_current:<color=yellow>25</color>)" +
                      "\n$item_food_stamina: <color=#ffff80ff>10</color>  ($item_current:<color=yellow>50</color>)" +
                      "\n$item_food_duration: <color=orange>20m </color>\n$item_food_regen: <color=orange>2 hp/tick</color>";
            var r = Wide(raw).Split('\n');
            Assert.Contains("$item_food_health: <color=#ff8080ff>30</color>  ($item_current:<color=yellow>25</color>)" + C +
                            "$item_food_stamina: <color=#ffff80ff>10</color>  ($item_current:<color=yellow>50</color>)", r);
            Assert.Contains("$item_food_duration: <color=orange>20m </color>" + C + "$item_food_regen: <color=orange>2 hp/tick</color>", r);
        }

        [Fact]
        public void UnknownLineIsABarrier()
        {
            // $item_noteleport (coloured warning) and a mod line stay exactly where they were; runs around them group alone.
            var raw = "d\n\n$item_weight: <color=orange>1.0</color>\n<color=orange>$item_noteleport</color>" +
                      "\n$item_value: <color=orange>10 (10 $item_total)</color>\nMod line: 5\n$item_quality: <color=orange>1</color>";
            var r = Wide(raw).Split('\n');
            Assert.Equal(new[]
            {
                "d", "",
                S + "$item_weight: <color=orange>1.0</color></size>",
                "<color=orange>$item_noteleport</color>",
                S + "$item_value: <color=orange>10 (10 $item_total)</color></size>",
                "Mod line: 5",
                S + "$item_quality: <color=orange>1</color></size>",
            }, r);
        }

        [Fact]
        public void ParagraphsAfterTheStatsAreVerbatim()
        {
            // Mead / set effect / projectile: their own stat lines (SE_Stats tooltip) are never regrouped.
            const string tail = "\n\n<color=orange>$se_mead</color>\n$item_movement_modifier: <color=orange>+10%</color>\n$item_knockback: <color=orange>5</color>" +
                                "\n\n$item_seteffect (<color=orange>3</color> $item_parts):<color=orange>$se_set</color>\n$inventory_dmgmod: x";
            var raw = "d\n\n$item_weight: <color=orange>1.0</color>\n$item_quality: <color=orange>1</color>" + tail;
            var r = Wide(raw);
            Assert.EndsWith(tail, r);
            Assert.StartsWith("d\n\n" + S + "$item_weight", r);
        }

        [Fact]
        public void DescriptionIsDimmedOnly()
        {
            var r = TooltipLayout.Compose("Some text\n\n$item_weight: <color=orange>1.0</color>", s => s.Length, 0f, "A8AFB4FF");
            Assert.StartsWith("<size=92%><color=#A8AFB4FF>Some text</color></size>\n\n", r);
        }

        [Fact]
        public void NonItemTextIsUntouched()
        {
            // No weight line = not a GetTooltip text (skills, piece, status effect tooltips): same instance.
            const string raw = "$item_armor: <color=orange>5</color>\n$item_knockback: <color=orange>1</color>";
            Assert.Same(raw, Wide(raw));
            const string plain = "Hello\nWorld";
            Assert.Same(plain, Wide(plain));
            Assert.Null(Wide(null));
        }

        [Fact]
        public void ComposedTextIsNotComposedAgain()
        {
            var once = Wide(Crossbow);
            Assert.Same(once, Wide(once));
        }

        [Fact]
        public void MaterialAndTrophyHaveOnlyMeta()
        {
            var raw = "$item_trophy_boar_description\n\n$item_value: <color=orange>5 (5 $item_total)</color>\n$item_weight: <color=orange>2.0 (2.0 $item_total)</color>";
            Assert.Equal("$item_trophy_boar_description\n\n" + S + "$item_value: <color=orange>5 (5 $item_total)</color></size>" + C +
                         S + "$item_weight: <color=orange>2.0 (2.0 $item_total)</color></size>", Wide(raw));
        }

        [Fact]
        public void ClassifyNeedsTheWholeToken()
        {
            Assert.Equal(TooltipLayout.Group.Meta, TooltipLayout.Classify("$item_twohanded"));
            Assert.Equal(TooltipLayout.Group.None, TooltipLayout.Classify("$item_weight"));       // stat without colon
            Assert.Equal(TooltipLayout.Group.None, TooltipLayout.Classify("$item_weightx: 1"));
            Assert.Equal(TooltipLayout.Group.None, TooltipLayout.Classify("$item_twohanded extra"));
            Assert.Equal(TooltipLayout.Group.Attack, TooltipLayout.Classify("$inventory_spirit: <color=orange>5</color>"));
            Assert.Equal(TooltipLayout.Group.Modifier, TooltipLayout.Classify("$se_runstamina: <color=orange>-10%</color>"));
        }
    }
}
