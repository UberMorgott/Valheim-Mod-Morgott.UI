namespace Morgott.UI.WorldHud
{
    // Damage type of one number; 1..10 in HitData.DamageTypes field order (HitData.cs:266-288), kind k <-> HitData.DamageType 1 << (k-1)
    // (HitData.cs:49-60). None = untyped m_damage or no type known.
    public enum DamageKind : byte { None, Blunt, Slash, Pierce, Chop, Pickaxe, Fire, Frost, Lightning, Poison, Spirit }

    // Resistance grade of that type on the target; the Slightly* steps fold into their neighbour.
    public enum DamageGrade : byte { Normal, VeryWeak, Weak, Resistant, VeryResistant, Immune }

    // What the visual step gets per number (TypedDamageText.Current): type, grade, DoT tick, and a per-target-per-frame slot
    // (0.. in send order) so numbers of one target in one frame can be staggered.
    public readonly struct DamageDescriptor
    {
        public readonly DamageKind Kind;
        public readonly DamageGrade Grade;
        public readonly bool Tick;
        public readonly byte Slot;

        public DamageDescriptor(DamageKind kind, DamageGrade grade, bool tick, int slot)
        {
            Kind = kind;
            Grade = grade;
            Tick = tick;
            Slot = (byte)(slot < 0 ? 0 : slot > DamageTypeMath.MaxSlot ? DamageTypeMath.MaxSlot : slot);
        }
    }

    public static class DamageTypeMath
    {
        public const int KindCount = 10;
        public const int MaxSlot = 15;

        // RPC_DamageText `int type` (DamageText.cs:199/211): bits 0-7 vanilla TextType, 8 marker, 9-12 kind, 13-15 grade, 16 tick,
        // 17-20 slot. Vanilla values are 0..7, so the marker bit never collides with a plain TextType.
        private const int TypeMask = 0xFF;
        private const int Marker = 1 << 8;
        private const int KindShift = 9;
        private const int GradeShift = 13;
        private const int TickBit = 1 << 16;
        private const int SlotShift = 17;

        public static int Pack(int textType, DamageDescriptor d) =>
            (textType & TypeMask) | Marker | ((int)d.Kind & 0xF) << KindShift | ((int)d.Grade & 0x7) << GradeShift
            | (d.Tick ? TickBit : 0) | (d.Slot & 0xF) << SlotShift;

        public static bool TryUnpack(int packed, out int textType, out DamageDescriptor d)
        {
            if ((packed & Marker) == 0)
            {
                textType = packed;
                d = default;
                return false;
            }
            textType = packed & TypeMask;
            d = new DamageDescriptor((DamageKind)(packed >> KindShift & 0xF), (DamageGrade)(packed >> GradeShift & 0x7),
                (packed & TickBit) != 0, packed >> SlotShift & 0xF);
            return true;
        }

        public static HitData.DamageType ToVanilla(DamageKind k) => (HitData.DamageType)(1 << ((int)k - 1));

        public static DamageGrade Grade(HitData.DamageModifier m)
        {
            switch (m)
            {
                case HitData.DamageModifier.VeryWeak: return DamageGrade.VeryWeak;
                case HitData.DamageModifier.Weak:
                case HitData.DamageModifier.SlightlyWeak: return DamageGrade.Weak;
                case HitData.DamageModifier.Resistant:
                case HitData.DamageModifier.SlightlyResistant: return DamageGrade.Resistant;
                case HitData.DamageModifier.VeryResistant: return DamageGrade.VeryResistant;
                case HitData.DamageModifier.Immune: return DamageGrade.Immune;
                default: return DamageGrade.Normal;
            }
        }

        public static DamageGrade GradeOf(HitData.DamageModifiers mods, DamageKind k) =>
            k == DamageKind.None ? DamageGrade.Normal : Grade(mods.GetModifier(ToVanilla(k)));

        // Vanilla ShowText(DamageModifier, ...) mapping (DamageText.cs:158-188).
        public static DamageText.TextType TextTypeOf(HitData.DamageModifier m)
        {
            switch (Grade(m))
            {
                case DamageGrade.Immune: return DamageText.TextType.Immune;
                case DamageGrade.Resistant:
                case DamageGrade.VeryResistant: return DamageText.TextType.Resistant;
                case DamageGrade.Weak:
                case DamageGrade.VeryWeak: return DamageText.TextType.Weak;
                default: return DamageText.TextType.Normal;
            }
        }

        public static float Amount(HitData.DamageTypes d, DamageKind k)
        {
            switch (k)
            {
                case DamageKind.Blunt: return d.m_blunt;
                case DamageKind.Slash: return d.m_slash;
                case DamageKind.Pierce: return d.m_pierce;
                case DamageKind.Chop: return d.m_chop;
                case DamageKind.Pickaxe: return d.m_pickaxe;
                case DamageKind.Fire: return d.m_fire;
                case DamageKind.Frost: return d.m_frost;
                case DamageKind.Lightning: return d.m_lightning;
                case DamageKind.Poison: return d.m_poison;
                case DamageKind.Spirit: return d.m_spirit;
                default: return d.m_damage;
            }
        }

        // Fire, poison and spirit leave a direct hit before its number is shown (zeroed at Character.cs:2397-2402, applied
        // later as DoT ticks), so they never make up the direct number.
        public static bool InDirectNumber(DamageKind k) => k != DamageKind.Fire && k != DamageKind.Poison && k != DamageKind.Spirit;

        // Largest component of a direct hit's number; untyped m_damage / m_nonPlayer count as None. Ties: first in order.
        public static DamageKind Dominant(HitData.DamageTypes post)
        {
            var best = DamageKind.None;
            var max = post.m_damage > post.m_nonPlayer ? post.m_damage : post.m_nonPlayer;
            for (var k = DamageKind.Blunt; k <= DamageKind.Spirit; k++)
            {
                if (!InDirectNumber(k)) continue;
                var a = Amount(post, k);
                if (a > max) { max = a; best = k; }
            }
            return best;
        }

        // Bit (1 << kind) per type that had damage before resistance and an Immune modifier (ApplyModifier, HitData.cs:958-961).
        public static int ImmuneMask(HitData.DamageTypes pre, HitData.DamageModifiers mods)
        {
            var mask = 0;
            for (var k = DamageKind.Blunt; k <= DamageKind.Spirit; k++)
                if (Amount(pre, k) > 0f && mods.GetModifier(ToVanilla(k)) == HitData.DamageModifier.Immune) mask |= 1 << (int)k;
            return mask;
        }

        // A DoT tick's shown total split per nonzero component (SE_Burning ticks fire + spirit in one HitData, SE_Burning.cs:43-48).
        // The hit is already scaled by ApplyModifier at that point, so the shares are taken from it and sized to `shown`.
        public static int Split(HitData.DamageTypes d, float shown, DamageKind[] kinds, float[] amounts)
        {
            var sum = 0f;
            for (var k = DamageKind.Blunt; k <= DamageKind.Spirit; k++) sum += Positive(Amount(d, k));
            if (sum <= 0f) return 0;
            var n = 0;
            for (var k = DamageKind.Blunt; k <= DamageKind.Spirit && n < kinds.Length; k++)
            {
                var a = Positive(Amount(d, k));
                if (a <= 0f) continue;
                kinds[n] = k;
                amounts[n] = shown * a / sum;
                n++;
            }
            return n;
        }

        private static float Positive(float v) => v > 0f ? v : 0f;
    }
}
