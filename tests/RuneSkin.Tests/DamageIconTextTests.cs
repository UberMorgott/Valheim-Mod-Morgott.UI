using RuneSkin.Screens;
using Xunit;

namespace RuneSkin.Tests
{
    // Damage-type icon insertion into raw (pre-Localize) tooltip text (DamageIconText).
    public class DamageIconTextTests
    {
        private static readonly string[] Tags = { "[B]", "[S]", "[P]", "[F]", "[C]", "[L]", "[T]", "[X]" };

        [Fact]
        public void WeaponLinesGetTheIconBeforeTheWord()
        {
            // HitData.GetTooltipString shape (HitData.cs:522, :534).
            var raw = "\n$inventory_blunt: <color=orange>55</color>\n$inventory_fire: <color=orange>10</color>";
            Assert.Equal("\n[B]$inventory_blunt: <color=orange>55</color>\n[F]$inventory_fire: <color=orange>10</color>", DamageIconText.Decorate(raw, Tags));
        }

        [Fact]
        public void ModifierLineEndingInTheTokenIsDecorated()
        {
            // SE_Stats.GetDamageModifiersTooltipString: "... VS $inventory_frost" at the end of the text.
            var raw = "\n$inventory_dmgmod: <color=orange>$inventory_resistant</color> VS $inventory_frost";
            Assert.Equal("\n$inventory_dmgmod: <color=orange>$inventory_resistant</color> VS [C]$inventory_frost", DamageIconText.Decorate(raw, Tags));
        }

        [Fact]
        public void LongerWordsAndOtherTokensStayUntouched()
        {
            var raw = "$inventory_fireresist $inventory_damage: 5 $inventory_spirit_x $inventory_chop";
            Assert.Same(raw, DamageIconText.Decorate(raw, Tags));
        }

        [Fact]
        public void AllEightTypesAndMissingTags()
        {
            var raw = "$inventory_blunt $inventory_slash $inventory_pierce $inventory_fire $inventory_frost $inventory_lightning $inventory_poison $inventory_spirit";
            Assert.Equal("[B]$inventory_blunt [S]$inventory_slash [P]$inventory_pierce [F]$inventory_fire [C]$inventory_frost [L]$inventory_lightning [T]$inventory_poison [X]$inventory_spirit",
                DamageIconText.Decorate(raw, Tags));
            var some = new string[8];
            some[3] = "[F]";
            Assert.Equal("$inventory_blunt [F]$inventory_fire", DamageIconText.Decorate("$inventory_blunt $inventory_fire", some));
        }

        [Fact]
        public void EmptyAndNullAreSafe()
        {
            Assert.Null(DamageIconText.Decorate(null, Tags));
            Assert.Equal("", DamageIconText.Decorate("", Tags));
            Assert.Equal("$inventory_fire", DamageIconText.Decorate("$inventory_fire", null));
        }

        [Fact]
        public void TagCarriesNameAndColour()
        {
            Assert.Equal("<sprite name=\"fire\" color=#FF8A3DFF> ", DamageIconText.Tag("fire", "FF8A3DFF"));
        }
    }
}
