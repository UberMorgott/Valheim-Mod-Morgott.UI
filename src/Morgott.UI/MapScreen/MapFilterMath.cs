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

        // Pixel on a biome border: a 4-neighbour inside the grid has another id (the grid edge is no border).
        public static bool Border(byte[] ids, int size, int x, int y)
        {
            var id = ids[y * size + x];
            return x > 0 && ids[y * size + x - 1] != id
                || x < size - 1 && ids[y * size + x + 1] != id
                || y > 0 && ids[(y - 1) * size + x] != id
                || y < size - 1 && ids[(y + 1) * size + x] != id;
        }

        // Overlay pixel: biome colour at the fill alpha, the outline alpha on a border, alpha 0 on ocean and on pixels
        // nobody explored (fog of war: unexplored borders never show). Transparent pixels keep the colour so a bilinear
        // sample does not darken the edge.
        public static Color32 Pixel(byte[] ids, int size, int x, int y, bool explored, Color32[] palette, byte fill, byte outline)
        {
            var id = ids[y * size + x];
            var c = id == 0 || id > palette.Length ? new Color32(0, 0, 0, 0) : palette[id - 1];
            c.a = id == 0 || !explored ? (byte)0 : Border(ids, size, x, y) ? outline : fill;
            return c;
        }

        // Whole overlay from the vanilla explored bits (Minimap.m_explored / m_exploredOthers, index y * size + x).
        public static void Compose(byte[] ids, int size, BitArray explored, BitArray others, Color32[] palette, byte fill, byte outline, Color32[] into)
        {
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var i = y * size + x;
                    into[i] = Pixel(ids, size, x, y, explored[i] || others[i], palette, fill, outline);
                }
        }

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
