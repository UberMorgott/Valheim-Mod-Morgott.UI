using System.Linq;
using Morgott.UI.Screens;
using Xunit;

namespace Morgott.UI.Tests
{
    // Item tooltip sheet (Variant A) on raw GetTooltip shapes (ItemDrop.cs:859-1090) and Epic Loot 0.15.6 magic text.
    public class TooltipSheetTests
    {
        private static readonly TooltipSheet.Style S = new TooltipSheet.Style();

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
            var s = new TooltipSheet.Set { Name = "Jarl", Equipped = 2, Full = 4 };
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
