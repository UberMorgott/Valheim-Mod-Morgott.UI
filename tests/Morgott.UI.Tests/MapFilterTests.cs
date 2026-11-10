using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BepInEx.Configuration;
using Morgott.UI.MapScreen;
using Morgott.UI.Theme;
using UnityEngine;
using Xunit;

namespace Morgott.UI.Tests
{
    // Large map redesign (docs\biome-overlay-design.md): filter rows, config persistence, biome grid / outline / fog
    // pixels, biome colour key, list scrolling.
    public class MapFilterTests
    {
        private static bool[] Vanilla() => Enumerable.Repeat(true, Enum.GetValues(typeof(Minimap.PinType)).Length).ToArray();

        [Fact]
        public void RowsCoverEveryDrawnVanillaTypeOnce()
        {
            var types = MapFilterMath.Rows.SelectMany(r => r.Types).ToList();
            Assert.Equal(types.Count, types.Distinct().Count());
            Assert.Equal(MapFilterMath.Rows.Length, MapFilterMath.Rows.Select(r => r.Key).Distinct().Count());
            foreach (Minimap.PinType t in Enum.GetValues(typeof(Minimap.PinType)))
                Assert.Equal(t != Minimap.PinType.None && t != Minimap.PinType.Player, types.Contains(t));
            Assert.Contains(MapFilterMath.Portal, types);
            Assert.Equal(Morgott.Core.Contracts.PortalPinType, (int)MapFilterMath.Portal);
        }

        [Fact]
        public void PortalRowNeedsTheGrownArray()
        {
            var portal = MapFilterMath.Rows.Single(r => r.Key == "Portal");
            Assert.False(MapFilterMath.Present(Vanilla(), portal));
            Assert.False(MapFilterMath.Apply(Vanilla(), portal, false));
            var grown = new bool[Morgott.Core.Contracts.PortalPinType + 1];
            Assert.True(MapFilterMath.Present(grown, portal));
        }

        [Fact]
        public void GroupRowShowsWhenAnyTypeShowsAndAppliesAll()
        {
            var hildir = MapFilterMath.Rows.Single(r => r.Key == "Hildir");
            var v = Vanilla();
            Assert.True(MapFilterMath.Apply(v, hildir, false));
            Assert.False(MapFilterMath.Shown(v, hildir));
            Assert.False(MapFilterMath.Apply(v, hildir, false));
            v[(int)Minimap.PinType.Hildir2] = true; // vanilla AddPin re-shows one type
            Assert.True(MapFilterMath.Shown(v, hildir));
            Assert.True(MapFilterMath.Apply(v, hildir, true));
            Assert.All(hildir.Types, t => Assert.True(v[(int)t]));
            Assert.True(v[(int)Minimap.PinType.Icon0]);
        }

        [Fact]
        public void FiltersPersistThroughTheConfigFile()
        {
            var path = Path.Combine(Path.GetTempPath(), $"morgott-ui-mapfilters-{Guid.NewGuid():N}.cfg");
            try
            {
                var cfg = new ConfigFile(path, true);
                MapConfig.Bind(cfg);
                Assert.True(MapConfig.BiomeOverlay.Value);
                Assert.Equal(0.30f, MapConfig.FillAlpha.Value, 3);
                Assert.Equal(0.85f, MapConfig.OutlineAlpha.Value, 3);
                Assert.All(MapFilterMath.Rows, r => Assert.True(MapConfig.Shown(r)));
                MapConfig.Filters["Hammer"].Value = false;
                MapConfig.Filters["Portal"].Value = false;
                MapConfig.BiomeOverlay.Value = false;
                cfg.Save();

                var again = new ConfigFile(path, true);
                MapConfig.Bind(again);
                Assert.False(MapConfig.Shown(MapFilterMath.Rows.Single(r => r.Key == "Hammer")));
                Assert.False(MapConfig.Shown(MapFilterMath.Rows.Single(r => r.Key == "Portal")));
                Assert.True(MapConfig.Shown(MapFilterMath.Rows.Single(r => r.Key == "Fire")));
                Assert.False(MapConfig.BiomeOverlay.Value);
                var text = File.ReadAllText(path);
                Assert.Contains("[Map Filters]", text);
                Assert.Contains("Hammer = false", text);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void BiomeIdsAndKeys()
        {
            Assert.Equal(8, MapFilterMath.Biomes.Length);
            Assert.Equal(0, MapFilterMath.Id(Heightmap.Biome.Ocean));
            Assert.Equal(0, MapFilterMath.Id(Heightmap.Biome.None));
            for (var i = 0; i < MapFilterMath.Biomes.Length; i++) Assert.Equal(i + 1, MapFilterMath.Id(MapFilterMath.Biomes[i]));
            Assert.Equal("$biome_blackforest", MapFilterMath.NameKey(Heightmap.Biome.BlackForest));
            Assert.Equal("$biome_deepnorth", MapFilterMath.NameKey(Heightmap.Biome.DeepNorth));
            Assert.Equal("color.map.biome.ashlands", MapFilterMath.ColorToken(Heightmap.Biome.AshLands));
        }

        [Fact]
        public void ThemeHasEveryBiomeColourAndListSizes()
        {
            using var s = typeof(ThemeSchema).Assembly.GetManifestResourceStream("Morgott.UI.default-theme.json");
            using var r = new StreamReader(s!);
            var t = ThemeJson.Parse(r.ReadToEnd());
            Assert.Empty(ThemeSchema.Validate(t));
            foreach (var b in MapFilterMath.Biomes) Assert.Contains(t.colors, c => c.token == MapFilterMath.ColorToken(b));
            foreach (var token in new[] { "size.map.biome.row", "size.map.biome.font", "size.map.legend.scroll", "count.map.players.max" })
                Assert.Contains(t.floats, f => f.token == token);
            // Mountain and Deep North must not share a colour (both are white in the vanilla map texture).
            Assert.NotEqual(t.colors.Single(c => c.token == "color.map.biome.mountain").value, t.colors.Single(c => c.token == "color.map.biome.deepnorth").value);
        }

        [Theory]
        [InlineData("English")]
        [InlineData("Russian")]
        public void EveryRowAndLayerIsTranslated(string language)
        {
            using var s = typeof(MapFilterTests).Assembly.GetManifestResourceStream($"Morgott.UI.Translations.{language}.json");
            var words = JsonSerializer.Deserialize<Dictionary<string, string>>(s!)!;
            foreach (var key in MapFilterMath.Rows.Select(r => r.NameToken).Concat(new[] { "$ui_map_layers", "$ui_map_biomes", "$ui_map_players", "$ui_map_pins" }))
                Assert.True(words.TryGetValue(key.TrimStart('$'), out var w) && !string.IsNullOrWhiteSpace(w), $"{language} misses {key}");
        }

        // 4x3 grid: meadows left half, black forest right half, ocean bottom-right corner.
        private static readonly byte[] Grid =
        {
            1, 1, 2, 2,
            1, 1, 2, 2,
            1, 1, 2, 0,
        };

        private static readonly Color32[] Palette = MapFilterMath.Biomes.Select((b, i) => new Color32((byte)(10 * (i + 1)), 0, 0, 255)).ToArray();

        [Fact]
        public void BorderIsAnyDifferentFourNeighbour()
        {
            // Grid is 4 wide but Border takes a square size: use a 4x4 padded copy.
            var ids = Grid.Concat(new byte[] { 1, 1, 2, 2 }).ToArray();
            Assert.False(MapFilterMath.Border(ids, 4, 0, 0)); // grid edge is no border
            Assert.True(MapFilterMath.Border(ids, 4, 1, 0));
            Assert.True(MapFilterMath.Border(ids, 4, 2, 1));
            Assert.False(MapFilterMath.Border(ids, 4, 3, 0));
            Assert.True(MapFilterMath.Border(ids, 4, 3, 1)); // ocean below
            Assert.True(MapFilterMath.Border(ids, 4, 3, 2)); // the ocean pixel itself
        }

        [Fact]
        public void PixelsFollowFogFillAndOutline()
        {
            var ids = Grid.Concat(new byte[] { 1, 1, 2, 2 }).ToArray();
            var fill = MapFilterMath.Alpha(0.3f);
            var outline = MapFilterMath.Alpha(0.85f);
            Assert.Equal(76, fill); // 76.5 rounds to even
            Assert.Equal(217, outline);
            var inside = MapFilterMath.Pixel(ids, 4, 0, 1, true, Palette, fill, outline);
            Assert.Equal((10, 76), (inside.r, inside.a));
            var border = MapFilterMath.Pixel(ids, 4, 1, 1, true, Palette, fill, outline);
            Assert.Equal((10, 217), (border.r, border.a));
            var fog = MapFilterMath.Pixel(ids, 4, 1, 1, false, Palette, fill, outline);
            Assert.Equal((10, 0), (fog.r, fog.a)); // unexplored: transparent, colour kept for bilinear edges
            Assert.Equal(0, MapFilterMath.Pixel(ids, 4, 3, 2, true, Palette, fill, outline).a); // ocean

            var explored = new BitArray(16);
            var others = new BitArray(16);
            explored[0] = true;
            others[5] = true; // shared map data counts as explored
            var px = new Color32[16];
            MapFilterMath.Compose(ids, 4, explored, others, Palette, fill, outline, px);
            Assert.Equal(new[] { 0, 5 }, Enumerable.Range(0, 16).Where(i => px[i].a > 0).ToArray());
        }

        [Fact]
        public void GridSamplesTheVanillaPixelCentres()
        {
            // Minimap.GenerateWorldMap: wx = (j - size / 2) * pixelSize + pixelSize / 2.
            Assert.Equal(-1024 * 12f + 6f, MapFilterMath.WorldCoord(0, 2048, 12f));
            Assert.Equal(6f, MapFilterMath.WorldCoord(1024, 2048, 12f));
            Assert.Equal(1023 * 12f + 6f, MapFilterMath.WorldCoord(2047, 2048, 12f));
        }

        [Fact]
        public void ListWindowAndScrollClamp()
        {
            Assert.Equal(5, MapFilterMath.Window(200f, 40f));
            Assert.Equal(1, MapFilterMath.Window(10f, 40f));
            Assert.Equal(0, MapFilterMath.ClampScroll(-3, 16, 5));
            Assert.Equal(11, MapFilterMath.ClampScroll(40, 16, 5));
            Assert.Equal(0, MapFilterMath.ClampScroll(4, 3, 5)); // everything fits: no scroll
        }
    }
}
