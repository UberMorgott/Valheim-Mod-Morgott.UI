using System;
using System.Reflection;
using Morgott.Core;

namespace Morgott.UI.InfoScreens
{
    // Soft dependencies of the character stats window. Epic Loot (third party) is bound once to typed delegates (no
    // reflection per read, no allocation): its effect sum (EpicLoot.PlayerExtensions.GetTotalActiveMagicEffectValue(Player,
    // string, float, ItemData), decompiled EL 0.14.13 PlayerExtensions.cs:65) and its incoming-damage steps
    // (EpicLoot.MagicItemEffects.ModifyResistance.ModifyIncoming / Shards.DamageReductionAtNight.ModifyIncoming, run by its
    // Character.RPC_Damage prefix). Morgott.Power's reads (food / potion slots, energy shield, armor curve) come only through
    // its Core provider IPlayerStatsInfo (docs\mod-contracts.md): registered in Power's Awake, so a Power assembly that
    // BepInEx loaded but never started (missing dependency) is absent, never half-initialized. Null = fall back to vanilla.
    internal static class StatsSources
    {
        private static Func<Player, string, float, ItemDrop.ItemData, float> _elTotal;
        public static Action<Character, HitData> ElResistance { get; private set; }
        public static Action<Character, HitData> ElNight { get; private set; }
        public static bool EpicLoot => _elTotal != null;

        // Morgott.Power's stats provider, null without it (resolved per read: Core Services rule, never cache "absent").
        public static IPlayerStatsInfo Power => Services.TryGet(out IPlayerStatsInfo s) ? s : null;

        // Food slot cap: Power's (Alchemist's Gut gem, belt cells), else vanilla Player.m_maxFoods (Player.cs:330).
        public static int FoodSlots(Player p) => Power?.FoodSlots(p) ?? Morgott.UI.PlayerHud.StripMath.VanillaFoodSlots;

        // Power pays Epic Loot's flat AddStaminaRegen / AddEitrRegen per second.
        public static bool ResourceAffixes => EpicLoot && Power?.ResourceAffixes == true;

        private static bool _bound;

        public static void Bind()
        {
            if (_bound) return;
            _bound = true;
            _elTotal = Make<Func<Player, string, float, ItemDrop.ItemData, float>>("EpicLoot.PlayerExtensions", "GetTotalActiveMagicEffectValue");
            ElResistance = Make<Action<Character, HitData>>("EpicLoot.MagicItemEffects.ModifyResistance", "ModifyIncoming");
            ElNight = Make<Action<Character, HitData>>("EpicLoot.MagicItemEffects.Shards.DamageReductionAtNight", "ModifyIncoming");
            Plugin.Log.LogInfo($"[Morgott.UI] stats sources: EpicLoot={EpicLoot} resist={ElResistance != null} night={ElNight != null}");
        }

        // Epic Loot effect total of the local player (EffectValue sum x scale), 0 without Epic Loot.
        public static float El(Player player, string effect, float scale = 1f)
        {
            if (_elTotal == null || !player) return 0f;
            try { return _elTotal(player, effect, scale, null); }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Morgott.UI] stats: Epic Loot effect '{effect}' read failed, Epic Loot rows off: {e.Message}");
                _elTotal = null;
                return 0f;
            }
        }

        // Public static method `name` of `typeName` as delegate T (exact parameter list), else null.
        private static T Make<T>(string typeName, string name) where T : Delegate
        {
            var type = Morgott.Core.TypeLookup.ByFullName(typeName);
            if (type == null) return null;
            // The delegate's signature (its Invoke method), found by scanning: no literal-name lookup on the generic T.
            var invoke = Array.Find(typeof(T).GetMethods(), m => m.Name == nameof(Action.Invoke));
            var pars = invoke.GetParameters();
            var types = new Type[pars.Length];
            for (var i = 0; i < pars.Length; i++) types[i] = pars[i].ParameterType;
            var m = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, types, null);
            return m != null && m.ReturnType == invoke.ReturnType ? (T)Delegate.CreateDelegate(typeof(T), m, false) : null;
        }
    }
}
