using Morgott.UI.WorldHud;
using Xunit;

namespace Morgott.UI.Tests
{
    // Typed damage numbers (DamageTypeMath): the RPC_DamageText int packing round trip, grade fold, the dominant
    // component of a direct hit (fire/poison/spirit leave as DoT, Character.cs:2397-2402), immune mask and the DoT split.
    public class DamageTypeMathTests
    {
        [Fact]
        public void PackRoundTripsEveryField()
        {
            for (var t = 0; t <= (int)DamageText.TextType.Immune; t++)
                for (var k = DamageKind.None; k <= DamageKind.Spirit; k++)
                    for (var g = DamageGrade.Normal; g <= DamageGrade.Immune; g++)
                        foreach (var tick in new[] { false, true })
                            foreach (var slot in new[] { 0, 1, 7, 15 })
                            {
                                var packed = DamageTypeMath.Pack(t, new DamageDescriptor(k, g, tick, slot));
                                Assert.True(DamageTypeMath.TryUnpack(packed, out var plain, out var d));
                                Assert.Equal(t, plain);
                                Assert.Equal(k, d.Kind);
                                Assert.Equal(g, d.Grade);
                                Assert.Equal(tick, d.Tick);
                                Assert.Equal(slot, d.Slot);
                            }
        }

        [Fact]
        public void PlainVanillaTypesAreNotPacked()
        {
            for (var t = 0; t <= (int)DamageText.TextType.Bonus; t++)
            {
                Assert.False(DamageTypeMath.TryUnpack(t, out var plain, out _));
                Assert.Equal(t, plain);
            }
        }

        [Fact]
        public void SlotClampsToFourBits() => Assert.Equal(DamageTypeMath.MaxSlot, new DamageDescriptor(DamageKind.Blunt, DamageGrade.Normal, false, 40).Slot);

        [Theory]
        [InlineData(HitData.DamageModifier.Normal, DamageGrade.Normal)]
        [InlineData(HitData.DamageModifier.Ignore, DamageGrade.Normal)]
        [InlineData(HitData.DamageModifier.SlightlyWeak, DamageGrade.Weak)]
        [InlineData(HitData.DamageModifier.Weak, DamageGrade.Weak)]
        [InlineData(HitData.DamageModifier.VeryWeak, DamageGrade.VeryWeak)]
        [InlineData(HitData.DamageModifier.SlightlyResistant, DamageGrade.Resistant)]
        [InlineData(HitData.DamageModifier.Resistant, DamageGrade.Resistant)]
        [InlineData(HitData.DamageModifier.VeryResistant, DamageGrade.VeryResistant)]
        [InlineData(HitData.DamageModifier.Immune, DamageGrade.Immune)]
        public void GradeFoldsSlightSteps(HitData.DamageModifier m, DamageGrade g) => Assert.Equal(g, DamageTypeMath.Grade(m));

        [Theory]
        [InlineData(HitData.DamageModifier.Normal, DamageText.TextType.Normal)]
        [InlineData(HitData.DamageModifier.Ignore, DamageText.TextType.Normal)]
        [InlineData(HitData.DamageModifier.SlightlyWeak, DamageText.TextType.Weak)]
        [InlineData(HitData.DamageModifier.VeryWeak, DamageText.TextType.Weak)]
        [InlineData(HitData.DamageModifier.SlightlyResistant, DamageText.TextType.Resistant)]
        [InlineData(HitData.DamageModifier.VeryResistant, DamageText.TextType.Resistant)]
        [InlineData(HitData.DamageModifier.Immune, DamageText.TextType.Immune)]
        public void TextTypeMirrorsVanilla(HitData.DamageModifier m, DamageText.TextType t) => Assert.Equal(t, DamageTypeMath.TextTypeOf(m));

        [Fact]
        public void KindMapsToVanillaFlag()
        {
            Assert.Equal(HitData.DamageType.Blunt, DamageTypeMath.ToVanilla(DamageKind.Blunt));
            Assert.Equal(HitData.DamageType.Pickaxe, DamageTypeMath.ToVanilla(DamageKind.Pickaxe));
            Assert.Equal(HitData.DamageType.Spirit, DamageTypeMath.ToVanilla(DamageKind.Spirit));
        }

        [Fact]
        public void DominantIsLargestDirectComponent()
        {
            var d = new HitData.DamageTypes { m_slash = 10f, m_frost = 12f, m_fire = 50f, m_spirit = 40f };
            Assert.Equal(DamageKind.Frost, DamageTypeMath.Dominant(d));
            Assert.Equal(DamageKind.None, DamageTypeMath.Dominant(new HitData.DamageTypes { m_damage = 5f, m_blunt = 3f }));
            Assert.Equal(DamageKind.Blunt, DamageTypeMath.Dominant(new HitData.DamageTypes { m_blunt = 3f, m_pierce = 3f }));
            Assert.Equal(DamageKind.None, DamageTypeMath.Dominant(new HitData.DamageTypes { m_nonPlayer = 9f, m_slash = 4f }));
        }

        [Fact]
        public void GradeOfReadsTheTypesModifier()
        {
            var mods = new HitData.DamageModifiers { m_pierce = HitData.DamageModifier.SlightlyResistant, m_fire = HitData.DamageModifier.VeryWeak };
            Assert.Equal(DamageGrade.Resistant, DamageTypeMath.GradeOf(mods, DamageKind.Pierce));
            Assert.Equal(DamageGrade.VeryWeak, DamageTypeMath.GradeOf(mods, DamageKind.Fire));
            Assert.Equal(DamageGrade.Normal, DamageTypeMath.GradeOf(mods, DamageKind.None));
        }

        [Fact]
        public void ImmuneMaskNeedsDamageAndImmunity()
        {
            var pre = new HitData.DamageTypes { m_blunt = 5f, m_poison = 3f, m_spirit = 0f };
            var mods = new HitData.DamageModifiers
            {
                m_blunt = HitData.DamageModifier.Normal,
                m_poison = HitData.DamageModifier.Immune,
                m_spirit = HitData.DamageModifier.Immune,
                m_slash = HitData.DamageModifier.Ignore,
            };
            Assert.Equal(1 << (int)DamageKind.Poison, DamageTypeMath.ImmuneMask(pre, mods));
        }

        [Fact]
        public void DotSplitKeepsEachComponent()
        {
            var kinds = new DamageKind[DamageTypeMath.KindCount];
            var amounts = new float[DamageTypeMath.KindCount];
            var n = DamageTypeMath.Split(new HitData.DamageTypes { m_fire = 3f, m_spirit = 1f }, 8f, kinds, amounts);
            Assert.Equal(2, n);
            Assert.Equal(DamageKind.Fire, kinds[0]);
            Assert.Equal(6f, amounts[0], 3);
            Assert.Equal(DamageKind.Spirit, kinds[1]);
            Assert.Equal(2f, amounts[1], 3);
            Assert.Equal(0, DamageTypeMath.Split(new HitData.DamageTypes { m_damage = 4f }, 4f, kinds, amounts));
        }
    }
}
