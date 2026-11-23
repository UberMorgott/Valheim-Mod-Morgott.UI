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

    // Receiver-side lanes of typed numbers (mockup "·6 ·9 ·4"). Each candidate lane (DamagePopupMath.Side order: centre, right,
    // left ...) is tested against where the recent numbers are NOW: vanilla lifts every number 1 m/s (DamageText.UpdateWorldTexts:
    // m_worldPos.y += dt). The first lane whose spot overlaps none of them wins; all busy -> the lane whose blocker rises clear
    // first. Positions are checked, not targets, so a moving target or two close targets never stack. Fixed ring, no allocation.
    public sealed class DamageLanes
    {
        public const int Lanes = 5;
        public const float RiseSpeed = 1f;
        private const float SameSpot = 0.98f;
        private const int Ring = DamagePopupMath.MaxAlive;
        private readonly float[] _x = new float[Ring];
        private readonly float[] _y = new float[Ring];
        private readonly float[] _z = new float[Ring];
        private readonly float[] _t = new float[Ring];
        private readonly float[] _h = new float[Ring];
        private readonly float[] _end = new float[Ring]; // gone after (vanilla m_duration); 0 = empty slot
        private int _next;

        // Lane for a number spawned at (x, y, z) at `now`; (rx, rz) = camera right on the ground plane (unit), step = lane width,
        // height = the number's height, both world units at its distance; life = its vanilla m_duration. Records it where laid out.
        public int Assign(float x, float y, float z, float rx, float rz, float now, float step, float height, float life)
        {
            var lane = 0;
            var bestFree = float.PositiveInfinity;
            for (var l = 0; l < Lanes; l++)
            {
                var side = DamagePopupMath.Side(l) * step;
                var free = FreeAt(x + rx * side, y, z + rz * side, now, step, height);
                if (free <= now)
                {
                    lane = l;
                    bestFree = now;
                    break;
                }
                if (free < bestFree)
                {
                    bestFree = free;
                    lane = l;
                }
            }
            var s = DamagePopupMath.Side(lane) * step;
            _x[_next] = x + rx * s;
            _y[_next] = y;
            _z[_next] = z + rz * s;
            _t[_next] = now;
            _h[_next] = height;
            _end[_next] = now + life;
            _next = (_next + 1) % Ring;
            return lane;
        }

        // When the spot (x, y, z) stops overlapping every recorded number (<= now: free now).
        private float FreeAt(float x, float y, float z, float now, float width, float height)
        {
            var free = now;
            for (var i = 0; i < Ring; i++)
            {
                if (_end[i] <= now) continue;
                var h = _h[i];
                float dx = _x[i] - x, dz = _z[i] - z;
                // Neighbour lanes sit exactly one width apart: the tolerance keeps float error at large world coordinates out.
                if (dx * dx + dz * dz >= width * width * SameSpot) continue;
                var bottom = _y[i] + (now - _t[i]) * RiseSpeed;
                if (bottom >= y + height || bottom + h <= y) continue;
                var clear = now + (y + height - bottom) / RiseSpeed;
                if (clear > free) free = clear;
            }
            return free;
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
