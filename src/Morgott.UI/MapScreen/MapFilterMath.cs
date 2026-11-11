using System;
using System.Collections;
using UnityEngine;

namespace Morgott.UI.MapScreen
{
    // One row of the large map's МЕТКИ filter list (docs\biome-overlay-design.md "Pin types in the filter list"): its
    // [Map Filters] config key, name token and the Minimap.PinType values it shows / hides together.
    public sealed class FilterRow
    {
        public readonly string Key, NameToken;
        public readonly Minimap.PinType[] Types;

        public FilterRow(string key, string nameToken, params Minimap.PinType[] types)
        {
            Key = key;
            NameToken = nameToken;
            Types = types;
        }
    }

    // Unity-light map filter / biome overlay math (linked into Morgott.UI.Tests).
    public static class MapFilterMath
    {
        // Portal pins of Morgott.Patcher (Core contract): their own row, apart from caves (Icon4).
        public const Minimap.PinType Portal = (Minimap.PinType)Morgott.Core.Contracts.PortalPinType;

        // The companion pin of Morgott.Summons (Core contract, Core 0.12.0): its own row, gold paw (MinimapAdapter.PetPaw).
        public const Minimap.PinType Pet = (Minimap.PinType)Morgott.Core.Contracts.PetPinType;

        // Not rows: None (never drawn), Player (ИГРОКИ section). Mod types (Epic Loot) come from their own buttons.
        public static readonly FilterRow[] Rows =
        {
            new FilterRow("Fire", "$ui_pin_icon0", Minimap.PinType.Icon0),
            new FilterRow("Home", "$ui_pin_icon1", Minimap.PinType.Icon1),
            new FilterRow("Hammer", "$ui_pin_icon2", Minimap.PinType.Icon2),
            new FilterRow("Dot", "$ui_pin_icon3", Minimap.PinType.Icon3),
            new FilterRow("Cave", "$ui_pin_icon4", Minimap.PinType.Icon4),
            new FilterRow("Portal", "$ui_pin_portal", Portal),
            new FilterRow("Pet", "$ui_pin_pet", Pet),
            new FilterRow("Death", "$ui_pin_death", Minimap.PinType.Death),
            new FilterRow("Boss", "$ui_pin_boss", Minimap.PinType.Boss),
            new FilterRow("Bed", "$ui_pin_bed", Minimap.PinType.Bed),
            new FilterRow("Event", "$ui_pin_event", Minimap.PinType.RandomEvent, Minimap.PinType.EventArea),
            new FilterRow("Hildir", "$ui_pin_hildir", Minimap.PinType.Hildir1, Minimap.PinType.Hildir2, Minimap.PinType.Hildir3),
            new FilterRow("Memorial", "$ui_pin_memorial", Minimap.PinType.Memorial),
            new FilterRow("Shout", "$ui_pin_shout", Minimap.PinType.Shout),
            new FilterRow("Ping", "$ui_pin_ping", Minimap.PinType.Ping),
        };

        // The row exists on this map: one of its types is inside m_visibleIconTypes (portal type only with Morgott.Patcher).
        public static bool Present(bool[] visible, FilterRow row)
        {
            foreach (var t in row.Types)
                if ((int)t >= 0 && (int)t < visible.Length) return true;
            return false;
        }

        // Shown = any of its types visible (vanilla AddPin re-shows a single hidden type, Minimap.cs:2346).
        public static bool Shown(bool[] visible, FilterRow row)
        {
            foreach (var t in row.Types)
                if ((int)t >= 0 && (int)t < visible.Length && visible[(int)t]) return true;
            return false;
        }

        // Writes the row state into the array; true when anything changed.
        public static bool Apply(bool[] visible, FilterRow row, bool on)
        {
            var changed = false;
            foreach (var t in row.Types)
            {
                var i = (int)t;
                if (i < 0 || i >= visible.Length || visible[i] == on) continue;
                visible[i] = on;
                changed = true;
            }
            return changed;
        }

        // ---------------------------------------------------------------- biome overlay

        // Legend / colour order. Ocean has no entry (transparent).
        public static readonly Heightmap.Biome[] Biomes =
        {
            Heightmap.Biome.Meadows, Heightmap.Biome.BlackForest, Heightmap.Biome.Swamp, Heightmap.Biome.Mountain,
            Heightmap.Biome.Plains, Heightmap.Biome.Mistlands, Heightmap.Biome.AshLands, Heightmap.Biome.DeepNorth,
        };

        // Grid id of a biome: 1 + its index in Biomes, 0 = ocean / none / unknown flag.
        public static byte Id(Heightmap.Biome biome)
        {
            var i = Array.IndexOf(Biomes, biome);
            return (byte)(i < 0 ? 0 : i + 1);
        }

        // Grid id of a map texel: underwater terrain (vanilla's map shows water there) is ocean whatever GetBiome says - its
        // ocean test is a base-height contour, so shallow sea came back as meadows and drew fill + outline rings over water.
        public static byte GridId(Heightmap.Biome biome, float height, float waterLevel) => height < waterLevel ? (byte)0 : Id(biome);

        // Theme token of a biome's overlay colour, its vanilla localisation key (EnvMan / MessageHud names).
        public static string ColorToken(Heightmap.Biome biome) => "color.map.biome." + biome.ToString().ToLowerInvariant();
        public static string NameKey(Heightmap.Biome biome) => "$biome_" + biome.ToString().ToLowerInvariant();

        // Pixel on a biome border, one side only so the texture line is one texel wide (Codex review 2): a 4-neighbour
        // inside the grid is ocean or a lower id. The grid edge is no border; ocean pixels never are.
        public static bool Border(byte[] ids, int size, int x, int y)
        {
            var id = ids[y * size + x];
            return id != 0 && (x > 0 && Lower(ids[y * size + x - 1], id)
                || x < size - 1 && Lower(ids[y * size + x + 1], id)
                || y > 0 && Lower(ids[(y - 1) * size + x], id)
                || y < size - 1 && Lower(ids[(y + 1) * size + x], id));
        }

        private static bool Lower(byte neighbour, byte id) => neighbour != id && (neighbour == 0 || neighbour < id);

        // Overlay pixel under the soft fog tint fogA (FogAlpha). Unexplored: the fog tint alone (fog of war: unexplored
        // biomes and borders never show; fog.a = 0 = no tint). Explored: biome fill at fill x cover / Tent2 (cover =
        // LandCover: explored land share of the 5x5 tent), so coasts and the explored edge ramp over ~4 texels instead of
        // stepping per 12 m texel; explored ocean beside land takes the tent-weighted land colour at that ramp. outline > 0:
        // a border texel is drawn at the outline alpha instead (the zoomed-out outline texture; the fill texture passes 0).
        public static Color32 Pixel(byte[] ids, int size, BitArray explored, BitArray others, int x, int y, byte fogA, int cover, Color32[] palette, byte fill, byte outline, Color32 fog)
        {
            var tint = new Color32(fog.r, fog.g, fog.b, fogA); // ocean / unexplored: fog colour kept for the bilinear edge
            if (!Seen(explored, others, y * size + x)) return tint;
            var id = ids[y * size + x];
            Color32 c;
            if (id != 0 && id <= palette.Length)
            {
                c = palette[id - 1];
                if (outline > 0 && Border(ids, size, x, y)) { c.a = outline; return Over(tint, c); }
            }
            else if (cover > 0) c = LandColour(ids, size, explored, others, x, y, palette);
            else return tint;
            c.a = (byte)((fill * cover + Tent2 / 2) / Tent2);
            return Over(tint, c);
        }

        // Tent-weighted colour of the explored land around an ocean texel (only called when there is some).
        private static Color32 LandColour(byte[] ids, int size, BitArray explored, BitArray others, int x, int y, Color32[] palette)
        {
            int r = 0, g = 0, b = 0, w = 0;
            for (var dy = -R; dy <= R; dy++)
                for (var dx = -R; dx <= R; dx++)
                {
                    int px = x + dx, py = y + dy;
                    if (!Land(ids, size, explored, others, px, py)) continue;
                    var c = palette[ids[py * size + px] - 1];
                    var k = Tent(dx) * Tent(dy);
                    r += c.r * k;
                    g += c.g * k;
                    b += c.b * k;
                    w += k;
                }
            return w == 0 ? default : new Color32((byte)((r + w / 2) / w), (byte)((g + w / 2) / w), (byte)((b + w / 2) / w), 255);
        }

        // Straight-alpha "a over b".
        public static Color32 Over(Color32 a, Color32 b)
        {
            if (a.a == 0) return b;
            if (a.a == 255 || b.a == 0) return a;
            var under = b.a * (255 - a.a) / 255f;
            var oa = a.a + under;
            return new Color32((byte)Mathf.RoundToInt((a.r * a.a + b.r * under) / oa), (byte)Mathf.RoundToInt((a.g * a.a + b.g * under) / oa),
                (byte)Mathf.RoundToInt((a.b * a.a + b.b * under) / oa), (byte)Mathf.RoundToInt(oa));
        }

        // Soft edges: a 5x5 tent (weights 1-2-3-2-1 per axis, Tent2 = 81 in all) over a texel mask. Fog = unexplored
        // (outside the grid counts as unexplored); land = explored and not ocean (outside = none). Vanilla's _FogTex edge
        // is soft in its shader; binary per-texel tint / fill drew 12 m stair steps once a texel spans several screen px.
        private const int R = 2, Tent1 = 9, Tent2 = Tent1 * Tent1;

        private static int Tent(int d) => R + 1 - Math.Abs(d);

        private static bool Unseen(int size, BitArray explored, BitArray others, int x, int y) =>
            x < 0 || y < 0 || x >= size || y >= size || !Seen(explored, others, y * size + x);

        private static bool Land(byte[] ids, int size, BitArray explored, BitArray others, int x, int y) =>
            x >= 0 && y >= 0 && x < size && y < size && ids[y * size + x] != 0 && Seen(explored, others, y * size + x);

        // Tent sum (0..81) of one texel, direct; ids == null = the fog mask, else the land mask.
        private static int TentAt(byte[] ids, int size, BitArray explored, BitArray others, int x, int y)
        {
            var s = 0;
            for (var dy = -R; dy <= R; dy++)
                for (var dx = -R; dx <= R; dx++)
                    if (ids == null ? Unseen(size, explored, others, x + dx, y + dy) : Land(ids, size, explored, others, x + dx, y + dy))
                        s += Tent(dx) * Tent(dy);
            return s;
        }

        // Tent sum of every texel (separable, same integers as TentAt).
        private static void TentAll(byte[] ids, int size, BitArray explored, BitArray others, int[] into)
        {
            var row = new int[size * size];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var s = 0;
                    for (var d = -R; d <= R; d++)
                        if (ids == null ? Unseen(size, explored, others, x + d, y) : Land(ids, size, explored, others, x + d, y)) s += Tent(d);
                    row[y * size + x] = s;
                }
            var edge = ids == null ? Tent1 : 0; // a row outside the grid: all fog, no land
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var s = 0;
                    for (var d = -R; d <= R; d++)
                    {
                        var py = y + d;
                        s += Tent(d) * (py < 0 || py >= size ? edge : row[py * size + x]);
                    }
                    into[y * size + x] = s;
                }
        }

        private static byte Scale(byte a, int s) => (byte)((a * s + Tent2 / 2) / Tent2);

        // Fog alpha of a texel = a x unexplored tent share.
        public static byte FogAlpha(BitArray explored, BitArray others, int size, int x, int y, byte a) =>
            a == 0 ? (byte)0 : Scale(a, TentAt(null, size, explored, others, x, y));

        // Explored land tent share of a texel, 0..81.
        public static int LandCover(byte[] ids, int size, BitArray explored, BitArray others, int x, int y) => TentAt(ids, size, explored, others, x, y);

        // FogAlpha of every texel.
        public static void FogAlphas(BitArray explored, BitArray others, int size, byte a, byte[] into)
        {
            if (a == 0) { Array.Clear(into, 0, size * size); return; }
            var s = new int[size * size];
            TentAll(null, size, explored, others, s);
            for (var i = 0; i < s.Length; i++) into[i] = Scale(a, s[i]);
        }

        // LandCover of every texel.
        public static void LandCovers(byte[] ids, int size, BitArray explored, BitArray others, int[] into) => TentAll(ids, size, explored, others, into);

        // Whole overlay from the vanilla explored bits (Minimap.m_explored / m_exploredOthers, index y * size + x), their
        // FogAlphas and LandCovers.
        public static void Compose(byte[] ids, int size, BitArray explored, BitArray others, byte[] fogA, int[] cover, Color32[] palette, byte fill, byte outline, Color32 fog, Color32[] into)
        {
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var i = y * size + x;
                    into[i] = Pixel(ids, size, explored, others, x, y, fogA[i], cover[i], palette, fill, outline, fog);
                }
        }

        // Texels whose FogAlpha / LandCover / Pixel change when texel (x, y) gets explored: its 5x5 neighbourhood.
        public const int FogReach = R;

        // One biome border run on the texel-corner lattice: (X0, Y0) -> (X1, Y1), axis-aligned; Id = the colour's biome.
        public struct Segment
        {
            public int X0, Y0, X1, Y1;
            public byte Id;
        }

        private static bool Seen(BitArray explored, BitArray others, int i) => explored[i] || others[i];

        // Border runs inside the texel window [x0, x1) x [y0, y1): an edge between two texels whose ids differ, drawn when
        // both sides are explored (never reveals an unexplored biome, never traces the fog edge); colour = the left / lower land side. Runs of one
        // colour along a line merge. Stops at max segments (returns false: too many, caller falls back).
        public static bool Segments(byte[] ids, int size, BitArray explored, BitArray others, int x0, int y0, int x1, int y1, int max, System.Collections.Generic.List<Segment> into)
        {
            into.Clear();
            x0 = Mathf.Clamp(x0, 1, size - 1);
            y0 = Mathf.Clamp(y0, 1, size - 1);
            x1 = Mathf.Clamp(x1, x0, size - 1);
            y1 = Mathf.Clamp(y1, y0, size - 1);
            // Vertical edges at corner x (between texel x - 1 and x), merged along y.
            for (var x = x0; x <= x1; x++)
            {
                byte run = 0;
                var start = 0;
                for (var y = y0; y <= y1; y++)
                {
                    var c = y < y1 ? EdgeColour(ids, size, explored, others, y * size + x - 1, y * size + x) : (byte)0;
                    if (c == run) continue;
                    if (run != 0 && !Add(into, max, x, start, x, y, run)) return false;
                    run = c;
                    start = y;
                }
            }
            // Horizontal edges at corner y (between texel y - 1 and y), merged along x.
            for (var y = y0; y <= y1; y++)
            {
                byte run = 0;
                var start = 0;
                for (var x = x0; x <= x1; x++)
                {
                    var c = x < x1 ? EdgeColour(ids, size, explored, others, (y - 1) * size + x, y * size + x) : (byte)0;
                    if (c == run) continue;
                    if (run != 0 && !Add(into, max, start, y, x, y, run)) return false;
                    run = c;
                    start = x;
                }
            }
            return true;
        }

        private static byte EdgeColour(byte[] ids, int size, BitArray explored, BitArray others, int a, int b)
        {
            var ia = ids[a];
            var ib = ids[b];
            if (ia == ib) return 0;
            if (!Seen(explored, others, a) || !Seen(explored, others, b)) return 0; // never along the fog edge (coast included)
            return ia != 0 ? ia : ib;
        }

        private static bool Add(System.Collections.Generic.List<Segment> into, int max, int x0, int y0, int x1, int y1, byte id)
        {
            if (into.Count >= max) return false;
            into.Add(new Segment { X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Id = id });
            return true;
        }

        // Screen pixels one map texel spans: map image width on screen / texels the uv window shows.
        public static float TexelPixels(float imageScreenWidth, float uvWidth, int size) => uvWidth > 0f && size > 0 ? imageScreenWidth / (uvWidth * size) : 0f;
        // World position of a grid pixel centre, as Minimap.GenerateWorldMap samples it (Minimap.cs:1958-1964).
        public static float WorldCoord(int pixel, int size, float pixelSize) => (pixel - size / 2) * pixelSize + pixelSize / 2f;

        public static byte Alpha(float a) => (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f);

        // ---------------------------------------------------------------- filter list layout

        // First row of a scrolled list window: clamped so the window never runs past the last row.
        public static int ClampScroll(int first, int rows, int window) => Mathf.Clamp(first, 0, Mathf.Max(0, rows - window));

        // Rows that fit a list height (at least one).
        public static int Window(float height, float rowH) => Mathf.Max(1, Mathf.FloorToInt((height + 0.5f) / Mathf.Max(1f, rowH)));
    }
}
