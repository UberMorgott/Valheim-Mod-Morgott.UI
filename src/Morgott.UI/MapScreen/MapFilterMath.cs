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
        // inside the grid is a lower land id. Coasts are no border (the fill ramps out instead); ocean pixels never are.
        public static bool Border(byte[] ids, int size, int x, int y)
        {
            var id = ids[y * size + x];
            return id != 0 && (x > 0 && Lower(ids[y * size + x - 1], id)
                || x < size - 1 && Lower(ids[y * size + x + 1], id)
                || y > 0 && Lower(ids[(y - 1) * size + x], id)
                || y < size - 1 && Lower(ids[(y + 1) * size + x], id));
        }

        private static bool Lower(byte neighbour, byte id) => neighbour != 0 && neighbour < id;

        // Explored land ids: ids where the texel is explored (by anyone), 0 elsewhere - the mask the fill blends over.
        public static void LandIds(byte[] ids, BitArray explored, BitArray others, byte[] into)
        {
            for (var i = 0; i < into.Length; i++) into[i] = Seen(explored, others, i) ? ids[i] : (byte)0;
        }

        // Tent-weighted explored land around a texel: W = weight (0..81), R/G/B = weighted palette colour sums.
        public struct Mix
        {
            public int W, R, G, B;
        }

        public static Mix LandMix(byte[] land, int size, Color32[] palette, int x, int y)
        {
            var m = new Mix();
            for (var dy = -Rad; dy <= Rad; dy++)
            {
                var py = y + dy;
                if (py < 0 || py >= size) continue;
                for (var dx = -Rad; dx <= Rad; dx++)
                {
                    var px = x + dx;
                    if (px < 0 || px >= size) continue;
                    var id = land[py * size + px];
                    if (id == 0 || id > palette.Length) continue;
                    var c = palette[id - 1];
                    var k = Tent(dx) * Tent(dy);
                    m.W += k;
                    m.R += c.r * k;
                    m.G += c.g * k;
                    m.B += c.b * k;
                }
            }
            return m;
        }

        // Overlay pixel under the soft fog tint fogA (FogAlpha). Unexplored: the fog tint alone (fog of war: unexplored
        // biomes never show; fog.a = 0 = no tint). Explored: the tent-weighted colour of the explored land around it at
        // fill x W / 81, so coasts, biome edges, single-texel specks and the explored edge blend over ~4 texels instead
        // of stepping per 12 m texel. outline > 0: a border texel is its own biome colour at the outline alpha (the
        // zoomed-out outline texture; the fill texture passes 0).
        public static Color32 Pixel(byte[] ids, int size, int x, int y, bool seen, Mix m, byte fogA, Color32[] palette, byte fill, byte outline, Color32 fog)
        {
            var tint = new Color32(fog.r, fog.g, fog.b, fogA); // ocean / unexplored: fog colour kept for the bilinear edge
            if (!seen) return tint;
            var id = ids[y * size + x];
            if (outline > 0 && id != 0 && id <= palette.Length && Border(ids, size, x, y))
            {
                var o = palette[id - 1];
                o.a = outline;
                return Over(tint, o);
            }
            if (m.W == 0) return tint;
            var c = new Color32((byte)((m.R + m.W / 2) / m.W), (byte)((m.G + m.W / 2) / m.W), (byte)((m.B + m.W / 2) / m.W), (byte)((fill * m.W + Tent2 / 2) / Tent2));
            return Over(tint, c);
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

        // Soft edges: a 5x5 tent (weights 1-2-3-2-1 per axis, Tent2 = 81 in all). Fog = unexplored share (outside the
        // grid counts as unexplored). Vanilla's _FogTex edge is soft in its shader; binary per-texel tint / fill drew
        // 12 m stair steps once a texel spans several screen pixels.
        private const int Rad = 2, Tent1 = 9, Tent2 = Tent1 * Tent1;

        private static int Tent(int d) => Rad + 1 - Math.Abs(d);

        private static bool Unseen(int size, BitArray explored, BitArray others, int x, int y) =>
            x < 0 || y < 0 || x >= size || y >= size || !Seen(explored, others, y * size + x);

        private static byte Scale(byte a, int s) => (byte)((a * s + Tent2 / 2) / Tent2);

        // Fog alpha of a texel = a x unexplored tent share.
        public static byte FogAlpha(BitArray explored, BitArray others, int size, int x, int y, byte a)
        {
            if (a == 0) return 0;
            var s = 0;
            for (var dy = -Rad; dy <= Rad; dy++)
                for (var dx = -Rad; dx <= Rad; dx++)
                    if (Unseen(size, explored, others, x + dx, y + dy)) s += Tent(dx) * Tent(dy);
            return Scale(a, s);
        }

        // FogAlpha of every texel (separable, same integers).
        public static void FogAlphas(BitArray explored, BitArray others, int size, byte a, byte[] into)
        {
            if (a == 0) { Array.Clear(into, 0, size * size); return; }
            var row = new int[size * size];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var s = 0;
                    for (var d = -Rad; d <= Rad; d++)
                        if (Unseen(size, explored, others, x + d, y)) s += Tent(d);
                    row[y * size + x] = s;
                }
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var s = 0;
                    for (var d = -Rad; d <= Rad; d++)
                    {
                        var py = y + d;
                        s += Tent(d) * (py < 0 || py >= size ? Tent1 : row[py * size + x]);
                    }
                    into[y * size + x] = Scale(a, s);
                }
        }

        // Both overlay textures from the explored land ids (LandIds), the explored bits (Minimap.m_explored /
        // m_exploredOthers, index y * size + x) and their FogAlphas, one land mix per texel.
        public static void Compose(byte[] ids, byte[] land, int size, BitArray explored, BitArray others, byte[] fogA, Color32[] palette, byte fill, byte outline, Color32 fog, Color32[] outlineTex, Color32[] fillTex)
        {
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var i = y * size + x;
                    var seen = Seen(explored, others, i);
                    var m = seen ? LandMix(land, size, palette, x, y) : default;
                    outlineTex[i] = Pixel(ids, size, x, y, seen, m, fogA[i], palette, fill, outline, fog);
                    fillTex[i] = Pixel(ids, size, x, y, seen, m, fogA[i], palette, fill, 0, fog);
                }
        }

        // Texels whose FogAlpha / LandMix / Pixel change when texel (x, y) gets explored: its 5x5 neighbourhood.
        public const int FogReach = Rad;

        // One biome border segment in texel-corner lattice units (texel (x, y) spans [x, x + 1) x [y, y + 1)); Id = the
        // colour's biome.
        public struct Segment
        {
            public float X0, Y0, X1, Y1;
            public byte Id;
        }

        private static bool Seen(BitArray explored, BitArray others, int i) => explored[i] || others[i];

        // Contour id of a texel: a land texel whose four neighbours are all one other land id takes that id (a lone
        // 12 m speck draws no tiny diamond).
        private static byte ContourId(byte[] ids, int size, int x, int y)
        {
            var i = y * size + x;
            var id = ids[i];
            if (id == 0 || x <= 0 || y <= 0 || x >= size - 1 || y >= size - 1) return id;
            var n = ids[i - 1];
            return n != 0 && n != id && ids[i + 1] == n && ids[i - size] == n && ids[i + size] == n ? n : id;
        }

        // Marching squares over the texel-centre grid inside the window [x0, x1) x [y0, y1): each 2x2 cell of texel
        // centres puts a line point on every cell edge whose two texels are explored land of different contour ids
        // (never against ocean - coasts are the fill ramp - and never against fog). Two points: one segment between them
        // (straight, or a 45-degree diagonal across the corner instead of a stair step); 1, 3 or 4: spokes to the cell
        // centre. Colour = the higher id of the first crossing. Straight segments continuing one another along a row
        // merge. Stops at max segments (returns false: too many, caller falls back to the outline texture).
        public static bool Segments(byte[] ids, int size, BitArray explored, BitArray others, int x0, int y0, int x1, int y1, int max, System.Collections.Generic.List<Segment> into)
        {
            into.Clear();
            x0 = Mathf.Clamp(x0, 0, size - 1);
            y0 = Mathf.Clamp(y0, 0, size - 1);
            x1 = Mathf.Clamp(x1, x0, size - 1);
            y1 = Mathf.Clamp(y1, y0, size - 1);
            var px = new float[4];
            var py = new float[4];
            for (var cy = y0; cy < y1; cy++)
                for (var cx = x0; cx < x1; cx++)
                {
                    // Corners: a = (cx, cy), b = (cx + 1, cy), c = (cx + 1, cy + 1), d = (cx, cy + 1).
                    var a = Contour(ids, size, explored, others, cx, cy);
                    var b = Contour(ids, size, explored, others, cx + 1, cy);
                    var c = Contour(ids, size, explored, others, cx + 1, cy + 1);
                    var d = Contour(ids, size, explored, others, cx, cy + 1);
                    var n = 0;
                    byte colour = 0;
                    // Edge midpoints in lattice units (texel centre = x + 0.5).
                    if (Cross(a, b, ref colour)) { px[n] = cx + 1f; py[n++] = cy + 0.5f; }
                    if (Cross(b, c, ref colour)) { px[n] = cx + 1.5f; py[n++] = cy + 1f; }
                    if (Cross(c, d, ref colour)) { px[n] = cx + 1f; py[n++] = cy + 1.5f; }
                    if (Cross(d, a, ref colour)) { px[n] = cx + 0.5f; py[n++] = cy + 1f; }
                    if (n == 2)
                    {
                        if (!Add(into, max, px[0], py[0], px[1], py[1], colour)) return false;
                    }
                    else
                        for (var k = 0; k < n; k++)
                            if (!Add(into, max, px[k], py[k], cx + 1f, cy + 1f, colour)) return false;
                }
            return true;
        }

        // Contour id of an explored texel, 0 for ocean or unexplored.
        private static byte Contour(byte[] ids, int size, BitArray explored, BitArray others, int x, int y) =>
            Seen(explored, others, y * size + x) ? ContourId(ids, size, x, y) : (byte)0;

        private static bool Cross(byte p, byte q, ref byte colour)
        {
            if (p == 0 || q == 0 || p == q) return false;
            if (colour == 0) colour = Math.Max(p, q);
            return true;
        }

        private static bool Add(System.Collections.Generic.List<Segment> into, int max, float x0, float y0, float x1, float y1, byte id)
        {
            if (x1 < x0 || x1 == x0 && y1 < y0) { (x0, x1) = (x1, x0); (y0, y1) = (y1, y0); } // left / lower end first
            var n = into.Count;
            if (n > 0)
            {
                // Merge a straight continuation of the last segment (same colour, same line, end = start).
                var l = into[n - 1];
                if (l.Id == id && l.X1 == x0 && l.Y1 == y0 && (y0 == y1 && l.Y0 == l.Y1 || x0 == x1 && l.X0 == l.X1))
                {
                    l.X1 = x1;
                    l.Y1 = y1;
                    into[n - 1] = l;
                    return true;
                }
            }
            if (n >= max) return false;
            into.Add(new Segment { X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Id = id });
            return true;
        }

        // Liang-Barsky: clips the segment p0-p1 to the rect; false when nothing is left.
        public static bool Clip(Rect r, ref Vector2 p0, ref Vector2 p1)
        {
            float t0 = 0f, t1 = 1f;
            var d = p1 - p0;
            if (!Edge(-d.x, p0.x - r.xMin, ref t0, ref t1) || !Edge(d.x, r.xMax - p0.x, ref t0, ref t1)
                || !Edge(-d.y, p0.y - r.yMin, ref t0, ref t1) || !Edge(d.y, r.yMax - p0.y, ref t0, ref t1)) return false;
            var a = p0;
            p0 = a + d * t0;
            p1 = a + d * t1;
            return t0 < t1 || d == Vector2.zero;
        }

        private static bool Edge(float p, float q, ref float t0, ref float t1)
        {
            if (p == 0f) return q >= 0f;
            var t = q / p;
            if (p < 0f) { if (t > t1) return false; if (t > t0) t0 = t; }
            else { if (t < t0) return false; if (t < t1) t1 = t; }
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
