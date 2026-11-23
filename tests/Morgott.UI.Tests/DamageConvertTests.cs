using Morgott.Core;
using Morgott.UI.InfoScreens;
using Xunit;

namespace Morgott.UI.Tests
{
    public class DamageConvertTests
    {
        private static readonly DamageConversion.Source Avatar = new DamageConversion.Source
        {
            Name = "$mod_epicloot_me_avataroffire_name",
            From = DamageConversion.AllTypes,
            To = DamageConversion.Fire,
            Share = 1f,
            Bonus = 0.2f,
        };

        // Bone spear from the report: 54 pierce + 1 fire.
        private static HitData.DamageTypes Spear() => new HitData.DamageTypes { m_pierce = 54f, m_fire = 1f, m_chop = 3f };

        [Fact]
        public void Full_fire_conversion_leaves_no_pierce()
        {
            var d = DamageConvert.Apply(Spear(), new[] { Avatar });
            Assert.Equal(0f, d.m_pierce);
            Assert.Equal(55f * 1.2f, d.m_fire, 3);
            Assert.Equal(3f, d.m_chop); // tool damage never converts
        }

        [Fact]
        public void No_source_is_unchanged()
        {
            var d = DamageConvert.Apply(Spear(), new DamageConversion.Source[0]);
            Assert.Equal(54f, d.m_pierce);
            Assert.Equal(1f, d.m_fire);
        }

        [Fact]
        public void Row_names_moved_types_and_shares()
        {
            Assert.Equal("$inventory_pierce → $inventory_fire 100% +20%", DamageConvert.Describe(DamageConvert.Parts(Spear()), Avatar, w => w, false));
            var partial = new DamageConversion.Source { From = DamageConversion.Physical, To = DamageConversion.Lightning, Share = 0.255f };
            Assert.Equal("$inventory_pierce → $inventory_lightning 25,5%", DamageConvert.Describe(DamageConvert.Parts(Spear()), partial, w => w, true));
            Assert.Null(DamageConvert.Describe(DamageConvert.Parts(new HitData.DamageTypes { m_fire = 5f }), Avatar, w => w, false));
        }

        [Fact]
        public void Tooltip_row_goes_after_the_damage_rows_once()
        {
            const string tip = "$desc\n$inventory_pierce: <color=orange>54</color>\n$inventory_fire: <color=orange>1</color>\n$item_staminause: <color=orange>8</color>";
            var once = DamageConvert.TooltipRows(tip, Spear(), new[] { Avatar }, false);
            Assert.Contains("$inventory_fire: <color=orange>1</color>\n$ui_tip_conversion: <color=orange>$inventory_pierce → $inventory_fire 100% +20%</color>\n$item_staminause", once);
            Assert.Equal(once, DamageConvert.TooltipRows(once, Spear(), new[] { Avatar }, false));
            Assert.Equal(tip, DamageConvert.TooltipRows(tip, Spear(), new DamageConversion.Source[0], false));
        }
    }
}
