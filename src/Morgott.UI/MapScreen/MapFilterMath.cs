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

        // Not rows: None (never drawn), Player (ИГРОКИ section). Mod types (Epic Loot) come from their own buttons.
        public static readonly FilterRow[] Rows =
        {
            new FilterRow("Fire", "$ui_pin_icon0", Minimap.PinType.Icon0),
            new FilterRow("Home", "$ui_pin_icon1", Minimap.PinType.Icon1),
            new FilterRow("Hammer", "$ui_pin_icon2", Minimap.PinType.Icon2),
            new FilterRow("Dot", "$ui_pin_icon3", Minimap.PinType.Icon3),
            new FilterRow("Cave", "$ui_pin_icon4", Minimap.PinType.Icon4),
            new FilterRow("Portal", "$ui_pin_portal", Portal),
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

        // Overlay pixel: biome colour at the fill alpha (the outline alpha on a border), alpha 0 on explored ocean; a pixel
        // nobody explored gets the fog tint instead (fog of war: unexplored borders never show; fog.a = 0 = no tint).
        public static Color32 Pixel(byte[] ids, int size, int x, int y, bool explored, Color32[] palette, byte fill, byte outline, Color32 fog)
        {
            if (!explored) return fog;
            var id = ids[y * size + x];
            if (id == 0 || id > palette.Length) return new Color32(fog.r, fog.g, fog.b, 0); // keeps the bilinear edge colour
            var c = palette[id - 1];
            c.a = Border(ids, size, x, y) ? outline : fill;
            return c;
        }

        // Whole overlay from the vanilla explored bits (Minimap.m_explored / m_exploredOthers, index y * size + x).
        public static void Compose(byte[] ids, int size, BitArray explored, BitArray others, Color32[] palette, byte fill, byte outline, Color32 fog, Color32[] into)
        {
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var i = y * size + x;
                    into[i] = Pixel(ids, size, x, y, explored[i] || others[i], palette, fill, outline, fog);
                }
        }

        // One biome border run on the texel-corner lattice: (X0, Y0) -> (X1, Y1), axis-aligned; Id = the colour's biome.
        public struct Segment
        {
            public int X0, Y0, X1, Y1;
            public byte Id;
        }

        private static bool Seen(BitArray explored, BitArray others, int i) => explored[i] || others[i];

        // Border runs inside the texel window [x0, x1) x [y0, y1): an edge between two texels whose ids differ, drawn when
        // every land side is explored (never reveals an unexplored biome); colour = the left / lower land side. Runs of one
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
            if (ia != 0 && !Seen(explored, others, a) || ib != 0 && !Seen(explored, others, b)) return 0;
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
