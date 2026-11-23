using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Morgott.UI.WorldHud
{
    public static class TypedDamageConfig
    {
        public static ConfigEntry<bool> Enabled { get; private set; }

        internal static bool On => Enabled != null && Enabled.Value;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("DamageText", "TypedDamageNumbers", true,
                "Damage numbers carry the damage type and how the target resists it; damage over time and immune types get their own numbers.");
        }
    }

    // Carries a DamageDescriptor from the hit (computed where RPC_Damage / the DoT tick runs) to the client that draws the number,
    // inside the existing RPC_DamageText `int type` (DamageTypeMath.Pack). The receiver restores the plain TextType before vanilla,
    // FPSDoctor (DamageTextPool prefix) or our skin read it.
    public static class TypedDamageText
    {
        // Receive side: the descriptor of the number AddInworldText is adding (valid from our prefix through the postfixes).
        public static bool HasCurrent { get; private set; }
        public static DamageDescriptor Current { get; private set; }

        internal struct RpcFrame
        {
            public Character Target;
            public HitData Hit;
            public bool Resisted;
            public HitData.DamageModifiers Mods;
            public int ImmuneMask;
        }

        internal struct ApplyFrame
        {
            public Character Target;
            public HitData Hit;
            public bool Armed;
        }

        internal static RpcFrame Rpc;
        internal static ApplyFrame Apply;
        internal static bool HasOutgoing;
        internal static DamageDescriptor Outgoing;

        private static readonly DamageKind[] Kinds = new DamageKind[DamageTypeMath.KindCount];
        private static readonly float[] Amounts = new float[DamageTypeMath.KindCount];
        private static readonly Dictionary<Character, int> Slots = new Dictionary<Character, int>();
        private static int _slotFrame = -1;

        // 0, 1, ... per target within one frame (A, B, A in one frame -> A0, B0, A1).
        internal static int NextSlot(Character target)
        {
            var frame = Time.frameCount;
            if (frame != _slotFrame)
            {
                Slots.Clear();
                _slotFrame = frame;
            }
            Slots.TryGetValue(target, out var n);
            Slots[target] = n + 1;
            return n;
        }

        // One number with a descriptor through the vanilla send path (ShowText -> InvokeRoutedRPC, or NPS's own routing).
        internal static void Send(DamageDescriptor d, DamageText.TextType type, Vector3 pos, string text, bool player)
        {
            Outgoing = d;
            HasOutgoing = true;
            try { DamageText.instance.ShowText(type, pos, text, player); }
            finally { HasOutgoing = false; }
        }

        internal static bool IsDot(HitData.HitType t) =>
            t == HitData.HitType.Burning || t == HitData.HitType.Poisoned || t == HitData.HitType.Smoke;

        // Vanilla ApplyDamage's number (Character.cs:2451-2454). Returns false when the DoT split replaced it.
        // Typed numbers rise from the top of the target's collider (as vanilla heals do, Character.cs:2169) instead of the hit point
        // inside the body (ApplyDamage passes hit.m_point, Character.cs:2453), where they sank into fire / spark VFX.
        // Player / tame targets keep the hit point: their owner (the sender) draws them as its own plain red damage (mySelf).
        internal static Vector3 SpawnPoint(Character target, Vector3 hitPoint, bool player)
        {
            if (player) return hitPoint;
            var top = target.GetTopPoint();
            return hitPoint.y < top.y ? top : hitPoint;
        }

        internal static bool OnShowModifier(HitData.DamageModifier mod, ref Vector3 pos, float dmg, bool player)
        {
            var a = Apply;
            // A foreign number inside ApplyDamage (e.g. Morgott.Summons NecroSet body transfer, at the body's top point) stays plain.
            if (!a.Armed || a.Hit == null || pos != a.Hit.m_point) return true;
            Apply.Armed = false;
            if (!TypedDamageConfig.On || !DamageText.instance) return true;
            var hit = a.Hit;
            if (IsDot(hit.m_hitType))
            {
                pos = SpawnPoint(a.Target, pos, player);
                var n = DamageTypeMath.Split(hit.m_damage, dmg, Kinds, Amounts);
                if (n == 0) return true;
                // Burning/poison were resisted when the hit landed (Character.cs:2386 before AddFireDamage); smoke never is (SE_Smoke.cs:33).
                var resisted = hit.m_hitType != HitData.HitType.Smoke;
                var mods = resisted ? a.Target.GetDamageModifiers() : default;
                var type = DamageTypeMath.TextTypeOf(mod);
                for (var i = 0; i < n; i++)
                {
                    var grade = resisted ? DamageTypeMath.GradeOf(mods, Kinds[i]) : DamageGrade.Normal;
                    // shortcut: grade is the target's resistance now, not when the DoT landed; a nonzero tick after an immunity
                    // gain reads Normal. Upgrade by stamping the grade on the SE when the visual step needs it exact.
                    if (grade == DamageGrade.Immune) grade = DamageGrade.Normal;
                    Send(new DamageDescriptor(Kinds[i], grade, true, NextSlot(a.Target)), type, pos,
                        Amounts[i].ToString("0.#", CultureInfo.InvariantCulture), player);
                }
                return false;
            }
            var r = Rpc;
            if (r.Resisted && ReferenceEquals(r.Hit, hit))
            {
                // Dominant from the final hit (after armor, Character.cs:2387-2396), grade from the resistances it met.
                var kind = DamageTypeMath.Dominant(hit.m_damage);
                Outgoing = new DamageDescriptor(kind, DamageTypeMath.GradeOf(r.Mods, kind), false, NextSlot(a.Target));
                HasOutgoing = true;
                pos = SpawnPoint(a.Target, pos, player);
            }
            return true;
        }

        // One "0" per type the target is immune to and that carried damage (vanilla shows nothing for a fully immune hit:
        // ApplyDamage returns at total <= 0.1, Character.cs:2443).
        internal static void EmitImmune(Character target, HitData hit)
        {
            var r = Rpc;
            if (!r.Resisted || r.ImmuneMask == 0 || !ReferenceEquals(r.Hit, hit) || !TypedDamageConfig.On || !DamageText.instance) return;
            var player = target.IsPlayer() || target.IsTamed();
            var pos = SpawnPoint(target, hit.m_point, player);
            for (var k = DamageKind.Blunt; k <= DamageKind.Spirit; k++)
                if ((r.ImmuneMask & 1 << (int)k) != 0)
                    Send(new DamageDescriptor(k, DamageGrade.Immune, false, NextSlot(target)), DamageText.TextType.Immune, pos, "0", player);
        }

        internal static void OnResisted(HitData hit, HitData.DamageTypes pre, HitData.DamageModifiers mods)
        {
            Rpc.Mods = mods;
            Rpc.ImmuneMask = DamageTypeMath.ImmuneMask(pre, mods);
            Rpc.Resisted = true;
        }

        // ZRoutedRpc.InvokeRoutedRPC serializes the parameters (ZRoutedRpc.cs:128): swap in a package whose `int type` is packed.
        internal static void Repack(object[] parameters)
        {
            if (!(parameters[0] is ZPackage pkg)) return;
            var at = pkg.GetPos();
            pkg.SetPos(0);
            var type = pkg.ReadInt();
            var pos = pkg.ReadVector3();
            var text = pkg.ReadString();
            var player = pkg.ReadBool();
            pkg.SetPos(at);
            if (type < 0 || type > (int)DamageText.TextType.Immune) return;
            var packed = new ZPackage();
            packed.Write(DamageTypeMath.Pack(type, Outgoing));
            packed.Write(pos);
            packed.Write(text);
            packed.Write(player);
            parameters[0] = packed;
        }

        internal static void Decode(ref DamageText.TextType type)
        {
            HasCurrent = false;
            // Always strip: a sender with the option on packs regardless of this client's setting.
            if (DamageTypeMath.TryUnpack((int)type, out var plain, out var d))
            {
                type = (DamageText.TextType)plain;
                if (!TypedDamageConfig.On) return;
                Current = d;
                HasCurrent = true;
            }
            // NPS keeps a number local by calling RPC_DamageText with its own plain package (DamageNumbers.cs:175-176).
            else if (HasOutgoing && TypedDamageConfig.On)
            {
                Current = Outgoing;
                HasCurrent = true;
            }
        }

        internal static void EndDecode() => HasCurrent = false;
    }

    // Not behind OwnershipRegistry: the decode must strip packed values even when another skin owns DamageText.
    [HarmonyPatch]
    public static class TypedDamagePatches
    {
        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        [HarmonyPrefix]
        private static void RpcDamage_Prefix(Character __instance, HitData hit, out TypedDamageText.RpcFrame __state)
        {
            __state = TypedDamageText.Rpc;
            TypedDamageText.Rpc = new TypedDamageText.RpcFrame { Target = __instance, Hit = hit };
        }

        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        [HarmonyPostfix]
        private static void RpcDamage_Postfix(Character __instance, HitData hit)
        {
            try { TypedDamageText.EmitImmune(__instance, hit); }
            catch (Exception e) { Plugin.Log.LogError($"[Morgott.UI] immune damage numbers failed: {e}"); }
        }

        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        [HarmonyFinalizer]
        private static void RpcDamage_Finalizer(TypedDamageText.RpcFrame __state) => TypedDamageText.Rpc = __state;

        [HarmonyPatch(typeof(HitData), nameof(HitData.ApplyResistance))]
        [HarmonyPrefix]
        private static void ApplyResistance_Prefix(HitData __instance, out HitData.DamageTypes __state) => __state = __instance.m_damage;

        [HarmonyPatch(typeof(HitData), nameof(HitData.ApplyResistance))]
        [HarmonyPostfix]
        private static void ApplyResistance_Postfix(HitData __instance, HitData.DamageModifiers modifiers, HitData.DamageTypes __state)
        {
            // Only the RPC_Damage hit (Character.cs:2386); other callers (e.g. Morgott.Patcher FrozenWater) stay plain.
            if (!ReferenceEquals(TypedDamageText.Rpc.Hit, __instance) || TypedDamageText.Rpc.Resisted) return;
            try { TypedDamageText.OnResisted(__instance, __state, modifiers); }
            catch (Exception e) { Plugin.Log.LogError($"[Morgott.UI] damage type capture failed: {e}"); }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
        [HarmonyPrefix]
        private static void ApplyDamage_Prefix(Character __instance, HitData hit, bool showDamageText, out TypedDamageText.ApplyFrame __state)
        {
            __state = TypedDamageText.Apply;
            TypedDamageText.Apply = new TypedDamageText.ApplyFrame { Target = __instance, Hit = hit, Armed = showDamageText };
        }

        [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
        [HarmonyFinalizer]
        private static void ApplyDamage_Finalizer(TypedDamageText.ApplyFrame __state) => TypedDamageText.Apply = __state;

        [HarmonyPatch(typeof(DamageText), nameof(DamageText.ShowText), typeof(HitData.DamageModifier), typeof(Vector3), typeof(float), typeof(bool))]
        [HarmonyPrefix]
        private static bool ShowModifier_Prefix(HitData.DamageModifier type, ref Vector3 pos, float dmg, bool player)
        {
            try { return TypedDamageText.OnShowModifier(type, ref pos, dmg, player); }
            catch (Exception e)
            {
                TypedDamageText.HasOutgoing = false;
                Plugin.Log.LogError($"[Morgott.UI] typed damage number failed (plain number): {e}");
                return true;
            }
        }

        [HarmonyPatch(typeof(DamageText), nameof(DamageText.ShowText), typeof(HitData.DamageModifier), typeof(Vector3), typeof(float), typeof(bool))]
        [HarmonyFinalizer]
        private static void ShowModifier_Finalizer() => TypedDamageText.HasOutgoing = false;

        [HarmonyPatch(typeof(ZRoutedRpc), nameof(ZRoutedRpc.InvokeRoutedRPC), typeof(long), typeof(ZDOID), typeof(string), typeof(object[]))]
        [HarmonyPrefix]
        private static void InvokeRoutedRPC_Prefix(string methodName, object[] parameters)
        {
            if (!TypedDamageText.HasOutgoing || methodName != "RPC_DamageText" || parameters == null || parameters.Length != 1) return;
            try { TypedDamageText.Repack(parameters); }
            catch (Exception e) { Plugin.Log.LogError($"[Morgott.UI] typed damage number packing failed (plain number): {e}"); }
        }

        // First: before FPSDoctor's DamageTextPool prefix (default priority, returns false) and our skin's count prefix.
        [HarmonyPatch(typeof(DamageText), nameof(DamageText.AddInworldText))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void AddInworldText_Prefix(ref DamageText.TextType type) => TypedDamageText.Decode(ref type);

        // No finalizer on AddInworldText: Morgott.Performance FxPool keeps its text pool vanilla on any foreign finalizer there
        // (FxPoolModule.AnyForeign); Decode resets HasCurrent at the start of every number instead.

        [HarmonyPatch(typeof(DamageText), nameof(DamageText.RPC_DamageText))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool RpcDamageText_Prefix(DamageText __instance, long sender, ZPackage pkg)
        {
            try { return DamagePopupCap.Admit(__instance, sender, pkg); }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[Morgott.UI] damage number cap failed (number shown): {e}");
                return true;
            }
        }

        [HarmonyPatch(typeof(DamageText), nameof(DamageText.LateUpdate))]
        [HarmonyPrefix]
        private static void LateUpdate_Prefix(DamageText __instance)
        {
            try { DamagePopupCap.Flush(__instance); }
            catch (Exception e)
            {
                DamagePopupCap.Clear();
                Plugin.Log.LogError($"[Morgott.UI] damage number flush failed: {e}");
            }
        }
    }

    // Popup budget with TypedDamageNumbers on (DamagePopupMath: 8 new per frame, 24 alive, room reserved for own damage / heals /
    // block texts). Others' numbers wait in a fixed buffer until DamageText.LateUpdate of the frame, then the best of them up to the
    // budget are added (hits before ticks, larger before smaller); the rest are dropped. No allocation per number.
    public static class DamagePopupCap
    {
        private const int Capacity = 32;
        private static readonly int[] Types = new int[Capacity];
        private static readonly Vector3[] Positions = new Vector3[Capacity];
        private static readonly float[] Distances = new float[Capacity];
        private static readonly string[] Texts = new string[Capacity];
        private static readonly bool[] Ticks = new bool[Capacity];
        private static readonly float[] Amounts = new float[Capacity];
        private static readonly bool[] Keep = new bool[Capacity];
        private static int _count;
        private static int _frame = -1;
        private static int _priority;
        private static int _others;

        private static void Tick()
        {
            var f = Time.frameCount;
            if (f == _frame) return;
            _frame = f;
            _priority = 0;
            _others = 0;
        }

        // RPC_DamageText prefix (DamageText.cs:206-222): false = this number is buffered or dropped.
        internal static bool Admit(DamageText dt, long sender, ZPackage pkg)
        {
            if (!TypedDamageConfig.On) return true;
            var cam = Utils.GetMainCamera();
            if (!cam || Hud.IsUserHidden()) return true;
            var at = pkg.GetPos();
            int raw;
            Vector3 pos;
            string text;
            bool player;
            try
            {
                raw = pkg.ReadInt();
                pos = pkg.ReadVector3();
                text = pkg.ReadString();
                player = pkg.ReadBool();
            }
            finally { pkg.SetPos(at); }
            var distance = Vector3.Distance(cam.transform.position, pos);
            if (distance > dt.m_maxTextDistance) return true; // vanilla drops it
            var typed = DamageTypeMath.TryUnpack(raw, out var plain, out var d);
            if (!typed && TypedDamageText.HasOutgoing)
            {
                // NPS local path: the descriptor is in the outgoing static only now, so it goes into the buffered value.
                d = TypedDamageText.Outgoing;
                raw = DamageTypeMath.Pack(plain, d);
                typed = true;
            }
            Tick();
            var alive = dt.m_worldTexts.Count;
            if (DamagePopupMath.IsPriority((DamageText.TextType)plain, player && sender == ZNet.GetUID()))
            {
                if (!DamagePopupMath.AdmitPriority(_priority + _others, alive)) return false;
                _priority++;
                return true;
            }
            var tick = typed && d.Tick;
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount);
            var i = _count;
            if (i == Capacity)
            {
                i = DamagePopupMath.WorstIndex(Ticks, Amounts, Capacity);
                if (!DamagePopupMath.Worse(Ticks[i], Amounts[i], tick, amount)) return false;
            }
            else _count++;
            Types[i] = raw;
            Positions[i] = pos;
            Distances[i] = distance;
            Texts[i] = text;
            Ticks[i] = tick;
            Amounts[i] = amount;
            return false;
        }

        internal static void Flush(DamageText dt)
        {
            if (_count == 0) return;
            Tick();
            var n = _count;
            _count = 0; // before adding: a nested number (another mod's AddInworldText hook) starts a fresh buffer
            DamagePopupMath.SelectBest(Ticks, Amounts, n, DamagePopupMath.OthersBudget(_priority, _others, dt.m_worldTexts.Count), Keep);
            for (var i = 0; i < n; i++)
            {
                var text = Texts[i];
                Texts[i] = null;
                if (!Keep[i]) continue;
                _others++;
                dt.AddInworldText((DamageText.TextType)Types[i], Positions[i], Distances[i], text, false);
            }
        }

        internal static void Clear()
        {
            for (var i = 0; i < _count; i++) Texts[i] = null;
            _count = 0;
        }
    }
}
