using System;
using BepInEx.Logging;
using Morgott.Core;
using Morgott.UI.InfoScreens;
using Morgott.UI.PlayerHud;
using Xunit;

namespace Morgott.UI
{
    // Stand-in for the BepInEx plugin class: the linked bridges log through Plugin.Log.
    internal static class Plugin
    {
        public static ManualLogSource Log { get; } = new ManualLogSource("Morgott.UI.Tests");
    }
}

namespace Morgott.UI.Tests
{
    // Morgott.Power's reads go only through Core providers (run 20261112-110112: Power's assembly loaded but never
    // started, the old name lookups called into it and threw every frame). No provider = belt off, vanilla food cap, no
    // potion / magazine counts, nothing thrown; a throwing provider is switched off after one log.
    [Collection(nameof(Services))]
    public class PowerProviderTests
    {
        [Fact]
        public void WithoutPowerEveryReadFallsBackWithoutThrowing()
        {
            Assert.False(Services.TryGet(out IBeltApi _));
            Assert.False(BeltBridge.On);
            Assert.False(BeltBridge.Bound);
            Assert.Equal(0, BeltBridge.Capacity(null, BeltMath.Food));
            Assert.Equal(0, BeltBridge.Capacity(null, BeltMath.Potion));
            Assert.False(BeltBridge.TryGet(null, BeltMath.Food, 0, out _));
            Assert.False(BeltBridge.Click(BeltMath.Food, 0, 0));
            Assert.Equal("", BeltBridge.Tooltip(BeltMath.Potion, 0));
            Assert.Null(StatsSources.Power);
            Assert.Equal(StripMath.VanillaFoodSlots, StatsSources.FoodSlots(null));
            Assert.False(StatsSources.ResourceAffixes);
            Assert.False(MagazineSource.Available);
            Assert.Equal(0, MagazineSource.Revision);
            Assert.False(MagazineSource.Read(new ItemDrop.ItemData(), out _, out _));
        }

        [Fact]
        public void PowerStatsProviderSetsTheFoodCap()
        {
            var stats = new FakeStats();
            Assert.True(Services.Register<IPlayerStatsInfo>(stats));
            try
            {
                Assert.Same(stats, StatsSources.Power);
                Assert.Equal(4, StatsSources.FoodSlots(null));
            }
            finally { Services.Unregister<IPlayerStatsInfo>(stats); }
            Assert.Equal(StripMath.VanillaFoodSlots, StatsSources.FoodSlots(null));
        }

        [Fact]
        public void ThrowingBeltProviderIsSwitchedOffOnce()
        {
            var belt = new ThrowingBelt();
            Assert.True(Services.Register<IBeltApi>(belt));
            try
            {
                Assert.False(BeltBridge.On);
                Assert.False(BeltBridge.On);
                Assert.Equal(1, belt.Calls);
                Assert.Equal(0, BeltBridge.Capacity(null, BeltMath.Food));
            }
            finally { Services.Unregister<IBeltApi>(belt); }
        }

        private sealed class FakeStats : IPlayerStatsInfo
        {
            public int FoodSlots(Player player) => 4;
            public int PotionSlots(Player player) => 2;
            public int PotionsActive(Player player) => 1;
            public bool PotionSlotsEnabled => true;

            public bool TryGetEnergyShield(Player player, out float current, out float max, out float rate, out float delay, out float breakDelay)
            {
                current = max = rate = delay = breakDelay = 0f;
                return false;
            }

            public int EnergyShieldParts(Player player, int kind, string[] keys, float[] values) => -1;

            public void ApplyArmorResistance(HitData hit, HitData.DamageModifiers modifiers, out HitData.DamageModifier significant, Character character) =>
                significant = HitData.DamageModifier.Normal;

            public bool ResourceAffixes => false;
        }

        private sealed class ThrowingBelt : IBeltApi
        {
            public int Calls;

            public bool Enabled
            {
                get
                {
                    Calls++;
                    throw new InvalidOperationException("config not bound");
                }
            }

            public int Version => throw new NotSupportedException();
            public int Capacity(Player player, BeltKind kind) => throw new NotSupportedException();
            public int Base(BeltKind kind) => throw new NotSupportedException();
            public string Tooltip(BeltKind kind, int index) => throw new NotSupportedException();
            public bool HandleClick(BeltKind kind, int index, int button) => throw new NotSupportedException();
            public bool ToggleAuto(BeltKind kind, int index) => throw new NotSupportedException();
            public bool UseNow(BeltKind kind, int index) => throw new NotSupportedException();
            public bool DropDragged(BeltKind kind, int index) => throw new NotSupportedException();
            public bool MoveToInventory(BeltKind kind, int index) => throw new NotSupportedException();
            public bool BeginDrag(BeltKind kind, int index, int amount) => throw new NotSupportedException();

            public bool TryGetSlot(Player player, BeltKind kind, int index, out BeltSlotState state) =>
                throw new NotSupportedException();
        }
    }
}
