namespace Morgott.UI.WorldHud
{
    // Look of one typed number (approved mockup v3): hue = damage type, weight / size / dimming = resistance grade.
    public readonly struct DamageStyle
    {
        public readonly bool Bold;
        public readonly float Scale;
        public readonly float Alpha;
        public readonly float Desaturate;

        public DamageStyle(bool bold, float scale, float alpha, float desaturate)
        {
            Bold = bold;
            Scale = scale;
            Alpha = alpha;
            Desaturate = desaturate;
        }
    }

    // Pure rules of the typed damage numbers: style per grade, side offset per slot, and the popup budget.
    public static class DamagePopupMath
    {
        public const float TickScale = 0.9f;
        public const string TickPrefix = "·";

        // Budget (cap only with TypedDamageNumbers on): at most MaxPerFrame new numbers per frame and MaxAlive on screen; others'
        // numbers leave ReserveFrame / ReserveAlive of that free for own damage taken, heals and block / shield texts.
        public const int MaxPerFrame = 8;
        public const int MaxAlive = 24;
        public const int ReserveFrame = 2;
        public const int ReserveAlive = 4;

        public static string ColorToken(DamageKind k)
        {
            switch (k)
            {
                case DamageKind.Blunt: return "color.damage.type.blunt";
                case DamageKind.Slash: return "color.damage.type.slash";
                case DamageKind.Pierce: return "color.damage.type.pierce";
                case DamageKind.Chop: return "color.damage.type.chop";
                case DamageKind.Pickaxe: return "color.damage.type.pickaxe";
                case DamageKind.Fire: return "color.damage.type.fire";
                case DamageKind.Frost: return "color.damage.type.frost";
                case DamageKind.Lightning: return "color.damage.type.lightning";
                case DamageKind.Poison: return "color.damage.type.poison";
                case DamageKind.Spirit: return "color.damage.type.spirit";
                default: return null;
            }
        }

        public static DamageStyle Style(DamageGrade g, bool tick)
        {
            var t = tick ? TickScale : 1f;
            switch (g)
            {
                case DamageGrade.VeryWeak: return new DamageStyle(true, 1.20f * t, 1f, 0f);
                case DamageGrade.Weak: return new DamageStyle(true, 1.12f * t, 1f, 0f);
                case DamageGrade.Resistant:
                case DamageGrade.Immune: return new DamageStyle(false, t, 0.55f, 0.20f);
                case DamageGrade.VeryResistant: return new DamageStyle(false, t, 0.45f, 0.30f);
                default: return new DamageStyle(false, t, 1f, 0f);
            }
        }

        // Toward the Rec.709 grey of the same luma by `amount` (0..1).
        public static void Desaturate(ref float r, ref float g, ref float b, float amount)
        {
            if (amount <= 0f) return;
            var y = 0.2126f * r + 0.7152f * g + 0.0722f * b;
            r += (y - r) * amount;
            g += (y - g) * amount;
            b += (y - b) * amount;
        }

        // Side step of a slot in number widths: 0 centre, 1 right, 2 left, 3 two right, 4 two left ... (mockup "·6 ·9 ·4").
        public static int Side(int slot) => slot <= 0 ? 0 : (slot & 1) == 1 ? (slot + 1) / 2 : -(slot / 2);

        // Own damage taken, heals and block / too-hard / bonus texts always get their reserved room.
        public static bool IsPriority(DamageText.TextType type, bool mySelf) => mySelf || type >= DamageText.TextType.Heal;

        public static bool AdmitPriority(int shownThisFrame, int alive) => shownThisFrame < MaxPerFrame && alive < MaxAlive;

        // How many of others' buffered numbers may still appear this frame: priority numbers use the reserve first, then the rest.
        public static int OthersBudget(int priorityThisFrame, int othersThisFrame, int alive)
        {
            var frame = MaxPerFrame - (priorityThisFrame > ReserveFrame ? priorityThisFrame : ReserveFrame) - othersThisFrame;
            var live = MaxAlive - ReserveAlive - alive;
            var n = frame < live ? frame : live;
            return n < 0 ? 0 : n;
        }

        // Drop order of others' numbers: ticks before hits, smaller before larger.
        public static bool Worse(bool tickA, float amountA, bool tickB, float amountB) =>
            tickA != tickB ? tickA : amountA < amountB;

        // Index of the entry dropped first among the first n (n > 0).
        public static int WorstIndex(bool[] tick, float[] amount, int n)
        {
            var w = 0;
            for (var i = 1; i < n; i++)
                if (Worse(tick[i], amount[i], tick[w], amount[w])) w = i;
            return w;
        }

        // Marks the `budget` best of the first n entries in keep[]; returns how many were kept. No allocation.
        public static int SelectBest(bool[] tick, float[] amount, int n, int budget, bool[] keep)
        {
            for (var i = 0; i < n; i++) keep[i] = false;
            if (budget >= n)
            {
                for (var i = 0; i < n; i++) keep[i] = true;
                return n;
            }
            var kept = 0;
            for (; kept < budget; kept++)
            {
                var best = -1;
                for (var i = 0; i < n; i++)
                    if (!keep[i] && (best < 0 || Worse(tick[best], amount[best], tick[i], amount[i]))) best = i;
                keep[best] = true;
            }
            return kept;
        }
    }
}
