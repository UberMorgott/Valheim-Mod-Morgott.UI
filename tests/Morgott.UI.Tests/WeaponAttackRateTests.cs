using Morgott.UI.Screens;
using Xunit;

namespace Morgott.UI.Tests
{
    public class WeaponAttackRateTests
    {
        private static float Rate(WeaponAttackRate.Attack a) => WeaponAttackRate.PerSecond(a);

        [Theory]
        [InlineData("swing_longsword", 3, 1.519f)] // chain cuts at levels 0 and 1, level 2 plays out
        [InlineData("knife_stab", 3, 2.691f)]
        [InlineData("staff_fireball", 2, 1.263f)]  // no Chain event: both levels play out
        [InlineData("swing_sledge", 0, 0.456f)]    // uninterruptible exit blend counted
        public void Melee_and_cast_cycles(string anim, int levels, float expected) =>
            Assert.Equal(expected, Rate(new WeaponAttackRate.Attack { Animation = anim, ChainLevels = levels }), 2f);

        [Fact]
        public void Bow_is_limited_by_the_full_draw() =>
            Assert.Equal(0.4f, Rate(new WeaponAttackRate.Attack { Animation = "bow_fire", BowDraw = true, DrawSeconds = 2.5f }), 3f);

        [Fact]
        public void Bow_without_draw_time_is_limited_by_the_release() =>
            Assert.Equal(1f / 0.76f, Rate(new WeaponAttackRate.Attack { Animation = "bow_fire", BowDraw = true }), 3f);

        [Theory]
        [InlineData("crossbow_fire", "reload_crossbow", 3.5f, 0.194f)]
        [InlineData("staff_lightningshot", "recharge_lightningstaff", 1.9f, 0.377f)]
        public void Reload_weapons_include_the_reload(string anim, string reload, float seconds, float expected) =>
            Assert.Equal(expected, Rate(new WeaponAttackRate.Attack { Animation = anim, RequiresReload = true, ReloadAnimation = reload, ReloadSeconds = seconds }), 3f);

        [Fact]
        public void Undeterminable_attacks_have_no_rate()
        {
            Assert.Equal(0f, Rate(default));
            Assert.Equal(0f, Rate(new WeaponAttackRate.Attack { Animation = "modded_swing" }));
            Assert.Equal(0f, Rate(new WeaponAttackRate.Attack { Animation = "swing_longsword", ChainLevels = 4 })); // no level 3
            Assert.Equal(0f, Rate(new WeaponAttackRate.Attack { Animation = "staff_fireball0", Looping = true }));
            Assert.Equal(0f, Rate(new WeaponAttackRate.Attack { Animation = "crossbow_fire", RequiresReload = true, ReloadAnimation = "modded", ReloadSeconds = 2f }));
        }

        private const string Tip = "$desc\n\n$item_onehanded\n$inventory_slash: <color=orange>40</color>\n$inventory_fire: <color=orange>5</color>\n$item_staminause: <color=orange>8</color>";

        [Fact]
        public void Row_goes_after_the_last_damage_row() =>
            Assert.Contains("$inventory_fire: <color=orange>5</color>\n$ui_tip_attack_rate: <color=orange>1,52</color>\n$item_staminause",
                WeaponAttackRate.InsertRow(Tip, 1.519f, true));

        [Fact]
        public void Row_is_inserted_once_and_uses_a_dot_outside_russian()
        {
            var once = WeaponAttackRate.InsertRow(Tip, 0.4f, false);
            Assert.Contains("<color=orange>0.40</color>", once);
            Assert.Same(once, WeaponAttackRate.InsertRow(once, 0.4f, false));
        }

        [Fact]
        public void No_rate_or_no_damage_row_leaves_the_text_alone()
        {
            Assert.Same(Tip, WeaponAttackRate.InsertRow(Tip, 0f, false));
            const string tool = "$desc\n$item_weight: <color=orange>2.0</color>";
            Assert.Same(tool, WeaponAttackRate.InsertRow(tool, 1f, false));
        }

        [Fact]
        public void Sheet_shows_the_rate_as_a_stat_row()
        {
            var sheet = TooltipSheet.Compose(WeaponAttackRate.InsertRow(Tip, 1.5f, false), new TooltipSheet.SheetData { Name = "$n" }, new TooltipSheet.Style());
            Assert.Contains("$ui_tip_attack_rate</color>", sheet);
            Assert.DoesNotContain("$ui_tip_attack_rate:", sheet);
        }
    }
}
