using System.Collections.Generic;
using BepInEx.Configuration;

namespace Morgott.UI.MapScreen
{
    // [Map] biome overlay + [Map Filters] one entry per МЕТКИ row (default on, ConfigurationManager-editable). The list and
    // the config stay in sync both ways: a checkbox writes its entry, SettingChanged (ConfigurationManager, file reload)
    // writes Minimap.m_visibleIconTypes (MinimapSkin.ApplyFilters, also after saved pins load), and a type vanilla toggles
    // itself (AddPin re-shows a hidden type, Minimap.cs:2346) is written back to its entry (MinimapSkin.OnVanillaToggle).
    public static class MapConfig
    {
        public const string MapSection = "Map", FilterSection = "Map Filters";

        public static ConfigEntry<bool> BiomeOverlay { get; private set; }
        public static ConfigEntry<float> FillAlpha { get; private set; }
        public static ConfigEntry<float> OutlineAlpha { get; private set; }
        public static ConfigEntry<bool> FogTint { get; private set; }
        public static readonly Dictionary<string, ConfigEntry<bool>> Filters = new Dictionary<string, ConfigEntry<bool>>();

        public static void Bind(ConfigFile config)
        {
            BiomeOverlay = config.Bind(MapSection, "BiomeOverlay", true,
                "Large map: tint explored land by biome with a biome border line (layer \"Biome highlight\" in the map's filter list).");
            FillAlpha = config.Bind(MapSection, "BiomeOverlayFillAlpha", 0.25f, new ConfigDescription(
                "Opacity of the biome tint inside a biome (0 = borders only).", new AcceptableValueRange<float>(0f, 1f)));
            OutlineAlpha = config.Bind(MapSection, "BiomeOverlayOutlineAlpha", 0.60f, new ConfigDescription(
                "Opacity of the biome border line.", new AcceptableValueRange<float>(0f, 1f)));
            FogTint = config.Bind(MapSection, "FogTint", true,
                "Large map: tint the unexplored fog toward a cold grey (theme colour color.map.fog).");
            Filters.Clear();
            foreach (var row in MapFilterMath.Rows)
                Filters[row.Key] = config.Bind(FilterSection, row.Key, true,
                    $"Large map: show {row.Key} pins (the checkbox of that row in the map's filter list).");
        }

        public static bool Shown(FilterRow row) => !Filters.TryGetValue(row.Key, out var e) || e.Value;
    }
}
