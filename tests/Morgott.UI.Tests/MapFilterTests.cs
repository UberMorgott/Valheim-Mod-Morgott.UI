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
        private static bool[] Vanilla() => Enumerable.Repeat(true, Enum.GetValues<Minimap.PinType>().Length).ToArray();

        [Fact]
        public void RowsCoverEveryDrawnVanillaTypeOnce()
        {
            var types = MapFilterMath.Rows.SelectMany(r => r.Types).ToList();
            Assert.Equal(types.Count, types.Distinct().Count());
            Assert.Equal(MapFilterMath.Rows.Length, MapFilterMath.Rows.Select(r => r.Key).Distinct().Count());
            foreach (var t in Enum.GetValues<Minimap.PinType>())
                Assert.Equal(t != Minimap.PinType.None && t != Minimap.PinType.Player, types.Contains(t));
            Assert.Contains(MapFilterMath.Portal, types);
            Assert.Contains(MapFilterMath.Pet, types);
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
        public void PetRowOnlyWithSummonsAndOnByDefault()
        {
            // Morgott.Summons grows the array to the companion pin type; without it the row is absent
            var pet = MapFilterMath.Rows.Single(r => r.Key == "Pet");
            Assert.Equal(Morgott.Core.Contracts.PetPinType, (int)MapFilterMath.Pet);
            Assert.False(MapFilterMath.Present(new bool[Morgott.Core.Contracts.PortalPinType + 1], pet));
            var grown = Enumerable.Repeat(true, Morgott.Core.Contracts.PetPinType + 1).ToArray();
            Assert.True(MapFilterMath.Present(grown, pet) && MapFilterMath.Shown(grown, pet));
            Assert.True(MapFilterMath.Apply(grown, pet, false));
            Assert.False(grown[Morgott.Core.Contracts.PetPinType]);
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
            for (var i = 0; i < hildir.Types.Length; i++) Assert.True(v[(int)hildir.Types[i]]);
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
                Assert.Equal(0.25f, MapConfig.FillAlpha.Value, 3);
                Assert.Equal(0.60f, MapConfig.OutlineAlpha.Value, 3);
                Assert.True(MapConfig.FogTint.Value);
                Assert.All(MapFilterMath.Rows, r => Assert.True(MapConfig.Shown(r)));
                MapConfig.Filters["Hammer"].Value = false;
                MapConfig.Filters["Portal"].Value = false;
                MapConfig.BindEpicLoot();
                MapConfig.ModFilter(MapConfig.BountiesKey).Value = false;
                MapConfig.ModFilter("CustomModFilter").Value = false;
                MapConfig.HiddenCustomPins.Value = "ore,cave";
                MapConfig.BiomeOverlay.Value = false;
                cfg.Save();

                var again = new ConfigFile(path, true);
                MapConfig.Bind(again);
                again.Save(); // startup save before late mod buttons appear must keep orphaned mod filter entries
                Assert.False(MapConfig.Shown(MapFilterMath.Rows.Single(r => r.Key == "Hammer")));
                Assert.False(MapConfig.Shown(MapFilterMath.Rows.Single(r => r.Key == "Portal")));
                Assert.True(MapConfig.Shown(MapFilterMath.Rows.Single(r => r.Key == "Fire")));
                Assert.Equal("ore,cave", MapConfig.HiddenCustomPins.Value);
                MapConfig.BindEpicLoot();
                Assert.False(MapConfig.ModFilter(MapConfig.BountiesKey).Value);
                Assert.True(MapConfig.ModFilter(MapConfig.TreasureMapsKey).Value);
                Assert.False(MapConfig.ModFilter("CustomModFilter").Value);
                Assert.Equal("Mod_Fire", MapConfig.ModFilter("Fire").Definition.Key);
                Assert.False(MapConfig.BiomeOverlay.Value);
                var text = File.ReadAllText(path);
                Assert.Contains("[Map Filters]", text);
                Assert.Contains("Hammer = false", text);
                Assert.Contains("Bounties = false", text);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void AddPinGateBlocksShowPinNameInputAndUnhidesOnlyConfirmedNamedPins()
        {
            Assert.True(MapFilterMath.AllowToggleFilter);
            MapFilterMath.EnterAddPin();
            Assert.False(MapFilterMath.AllowToggleFilter);
            MapFilterMath.EnterAddPin();
            MapFilterMath.ExitAddPin();
            Assert.False(MapFilterMath.AllowToggleFilter);
            MapFilterMath.ExitAddPin();
            Assert.True(MapFilterMath.AllowToggleFilter);

            var v = Vanilla();
            v[(int)Minimap.PinType.Icon0] = false;
            // Accidental double-click cancelled (Esc / click-away / empty submit / whitespace) never unhides Fire
            Assert.False(MapFilterMath.ShouldUnhideConfirmedPin(v, Minimap.PinType.Icon0, ""));
            Assert.False(MapFilterMath.ShouldUnhideConfirmedPin(v, Minimap.PinType.Icon0, null));
            Assert.False(MapFilterMath.ShouldUnhideConfirmedPin(v, Minimap.PinType.Icon0, "   "));
            // Already-visible pin type is never toggled off; out-of-range / null array are ignored
            Assert.False(MapFilterMath.ShouldUnhideConfirmedPin(v, Minimap.PinType.Icon1, "Home"));
            Assert.False(MapFilterMath.ShouldUnhideConfirmedPin(v, Minimap.PinType.None, "Camp"));
            Assert.False(MapFilterMath.ShouldUnhideConfirmedPin(null, Minimap.PinType.Icon0, "Camp"));
            // Confirming a real named pin unhides its hidden type
            Assert.True(MapFilterMath.ShouldUnhideConfirmedPin(v, Minimap.PinType.Icon0, "Camp"));

            Assert.Equal("AdventurePinFilter", MapFilterMath.ModFilterKey(" AdventurePinFilter(Clone)[\"=] "));
            Assert.Null(MapFilterMath.ModFilterKey(" [\"=] "));
        }

        [Fact]
        public void AutoPinGuardBlocksWithinGraceAndNeverWhenUnset()
        {
            Assert.True(MapFilterMath.AllowAutoPinAfterOpen(0f, float.NegativeInfinity));
            Assert.True(MapFilterMath.AllowAutoPinAfterOpen(10f, float.NegativeInfinity));
            Assert.False(MapFilterMath.AllowAutoPinAfterOpen(10f, 10f));
            Assert.False(MapFilterMath.AllowAutoPinAfterOpen(10.34f, 10f));
            Assert.True(MapFilterMath.AllowAutoPinAfterOpen(0.35f, 0f));
            Assert.True(MapFilterMath.AllowAutoPinAfterOpen(10.36f, 10f));
            Assert.False(MapFilterMath.AllowAutoPinAfterOpen(10.4f, 10f, 0.5f));
            Assert.True(MapFilterMath.AllowAutoPinAfterOpen(10.6f, 10f, 0.5f));

            MapFilterMath.ResetLargeMapOpened();
            try
            {
                Assert.True(MapFilterMath.AllowAutoPinAfterOpen(0.1f));
                MapFilterMath.NoteLargeMapOpened(10f);
                Assert.False(MapFilterMath.AllowAutoPinAfterOpen(10f));
                Assert.False(MapFilterMath.AllowAutoPinAfterOpen(10.34f));
                Assert.True(MapFilterMath.AllowAutoPinAfterOpen(10.4f));
                MapFilterMath.NoteLargeMapOpened(20f);
                Assert.False(MapFilterMath.AllowAutoPinAfterOpen(20.1f));
                MapFilterMath.ResetLargeMapOpened();
                Assert.True(MapFilterMath.AllowAutoPinAfterOpen(20.1f));
            }
            finally
            {
                MapFilterMath.ResetLargeMapOpened();
            }
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

        private static bool HasColor(ThemeFile t, string token)
        {
            for (var i = 0; i < t.colors.Length; i++)
                if (t.colors[i].token == token) return true;
            return false;
        }

        private static bool HasFloat(ThemeFile t, string token)
        {
            for (var i = 0; i < t.floats.Length; i++)
                if (t.floats[i].token == token) return true;
            return false;
        }

        [Fact]
        public void ThemeHasEveryBiomeColourAndListSizes()
        {
            using var s = typeof(ThemeSchema).Assembly.GetManifestResourceStream("Morgott.UI.default-theme.json");
            using var r = new StreamReader(s!);
            var t = ThemeJson.Parse(r.ReadToEnd());
            Assert.Empty(ThemeSchema.Validate(t));
            foreach (var b in MapFilterMath.Biomes) Assert.True(HasColor(t, MapFilterMath.ColorToken(b)), MapFilterMath.ColorToken(b));
            Assert.Contains(t.colors, c => c.token == "color.map.fog");
            Assert.Equal("Valheim-AveriaSerifLibre", t.fonts.Single(f => f.token == "font.map.header").asset);
            foreach (var token in new[] { "size.map.biome.row", "size.map.biome.font", "size.map.legend.scroll", "count.map.players.max", "size.map.biome.line", "size.map.biome.line.switch", "count.map.biome.lines.max" })
                Assert.True(HasFloat(t, token), token);
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
            for (var i = 0; i < MapFilterMath.Rows.Length; i++)
            {
                var key = MapFilterMath.Rows[i].NameToken;
                Assert.True(words.TryGetValue(key.TrimStart('$'), out var w) && !string.IsNullOrWhiteSpace(w), $"{language} misses {key}");
            }
            var extra = new[] { "$ui_map_layers", "$ui_map_biomes", "$ui_map_others_layer", "$ui_map_players", "$ui_map_pins" };
            for (var i = 0; i < extra.Length; i++)
            {
                var key = extra[i];
                Assert.True(words.TryGetValue(key.TrimStart('$'), out var w) && !string.IsNullOrWhiteSpace(w), $"{language} misses {key}");
            }
        }

        [Theory]
        [InlineData(false, false, false, 255)]
        [InlineData(false, false, true, 255)]
        [InlineData(true, false, false, 0)]
        [InlineData(true, true, false, 0)]
        [InlineData(false, true, false, 255)] // layered: others' texel keeps red fog, the shader's lighter layer
        [InlineData(false, true, true, 0)]    // merged: drawn like own exploration
        public void FogRedMergesOthersOnlyWhenAsked(bool explored, bool others, bool merged, int red) =>
            Assert.Equal((byte)red, MapFilterMath.FogRed(explored, others, merged));

        [Fact]
        public void BitReadsBitArrayWords()
        {
            var bits = new System.Collections.BitArray(100);
            foreach (var i in new[] { 0, 31, 32, 63, 64, 99 }) bits[i] = true;
            var words = new int[(bits.Length + 31) >> 5];
            bits.CopyTo(words, 0);
            for (var i = 0; i < bits.Length; i++) Assert.Equal(bits[i], MapFilterMath.Bit(words, i));
        }        // 4x3 grid: meadows left half, black forest right half, ocean bottom-right corner.
        private static readonly byte[] Grid =
        {
            1, 1, 2, 2,
            1, 1, 2, 2,
            1, 1, 2, 0,
        };

        private static readonly Color32[] Palette = MapFilterMath.Biomes.Select((b, i) => new Color32((byte)(10 * (i + 1)), 0, 0, 255)).ToArray();

        [Fact]
        public void BorderIsALowerLandNeighbourNeverACoast()
        {
            // Grid is 4 wide but Border takes a square size: use a 4x4 padded copy. One-sided: only the higher id side.
            var ids = Grid.Concat(new byte[] { 1, 1, 2, 2 }).ToArray();
            Assert.False(MapFilterMath.Border(ids, 4, 0, 0)); // grid edge is no border
            Assert.False(MapFilterMath.Border(ids, 4, 1, 0)); // meadows (1) beside black forest (2): the higher id draws the line
            Assert.True(MapFilterMath.Border(ids, 4, 2, 0));
            Assert.True(MapFilterMath.Border(ids, 4, 2, 1));
            Assert.False(MapFilterMath.Border(ids, 4, 3, 0));
            Assert.False(MapFilterMath.Border(ids, 4, 3, 1)); // ocean below: coasts are the fill ramp, no outline
            Assert.False(MapFilterMath.Border(ids, 4, 3, 2)); // the ocean pixel itself never
        }

        private static readonly Color32 Fog = new Color32(55, 78, 102, 166);

        private static MapFilterMath.Mix Full(byte id) => new MapFilterMath.Mix { W = 81, R = Palette[id - 1].r * 81, G = 0, B = 0 };

        [Fact]
        public void PixelsFollowFogFillAndOutline()
        {
            var ids = Grid.Concat(new byte[] { 1, 1, 2, 2 }).ToArray();
            var fill = MapFilterMath.Alpha(0.3f);
            var outline = MapFilterMath.Alpha(0.85f);
            Assert.Equal(76, fill); // 76.5 rounds to even
            Assert.Equal(217, outline);
            var inside = MapFilterMath.Pixel(ids, 4, 0, 1, true, Full(1), 0, Palette, fill, outline, Fog);
            Assert.Equal((10, 76), (inside.r, inside.a));
            var border = MapFilterMath.Pixel(ids, 4, 2, 1, true, Full(2), 0, Palette, fill, outline, Fog);
            Assert.Equal((20, 217), (border.r, border.a));
            Assert.Equal(76, MapFilterMath.Pixel(ids, 4, 2, 1, true, Full(2), 0, Palette, fill, 0, Fog).a); // fill texture: no outline
            var ramp = MapFilterMath.Pixel(ids, 4, 0, 1, true, new MapFilterMath.Mix { W = 40, R = 400 }, 0, Palette, fill, outline, Fog);
            Assert.Equal((10, 38), (ramp.r, ramp.a)); // coast / edge ramp: fill x 40/81
            Assert.Equal(Fog, MapFilterMath.Pixel(ids, 4, 1, 1, false, default, Fog.a, Palette, fill, outline, Fog)); // unexplored = fog tint
            var ocean = MapFilterMath.Pixel(ids, 4, 3, 2, true, default, 0, Palette, fill, outline, Fog);
            Assert.Equal((55, 0), (ocean.r, ocean.a)); // explored ocean, no land near: clear, fog colour kept for the bilinear edge
            Assert.Equal(0, MapFilterMath.Pixel(ids, 4, 1, 1, false, default, 0, Palette, fill, outline, default).a); // FogTint off
            // Explored land at the fog edge: tint over the biome colour, more opaque than either, between their colours.
            var edge = MapFilterMath.Pixel(ids, 4, 0, 1, true, Full(1), 80, Palette, fill, outline, Fog);
            Assert.True(edge.a > 80 && edge.a > fill && edge.r > 10 && edge.r < 55, edge.ToString());

            var explored = new BitArray(16);
            var others = new BitArray(16);
            explored[0] = true;
            others[5] = true; // shared map data counts as explored
            var po = new Color32[16];
            var pf = new Color32[16];
            var fogA = new byte[16];
            MapFilterMath.FogAlphas(explored, others, 4, Fog.a, fogA);
            MapFilterMath.Compose(ids, 4, explored, others, fogA, Palette, fill, outline, Fog, 0, po, pf);
            // Streamed in row bands (the overlay's worker path): same pixels as one full pass.
            var bo = new Color32[16];
            var bf = new Color32[16];
            for (var y0 = 0; y0 < 4; y0 += 3)
            {
                var n = Math.Min(3, 4 - y0) * 4;
                var (o, f) = (new Color32[n], new Color32[n]);
                MapFilterMath.Compose(ids, 4, explored, others, fogA, Palette, fill, outline, Fog, y0, o, f);
                Array.Copy(o, 0, bo, y0 * 4, n);
                Array.Copy(f, 0, bf, y0 * 4, n);
            }
            Assert.Equal(po, bo);
            Assert.Equal(pf, bf);
            // Biome colour only on explored texels; every other texel is the fog colour at its soft alpha.
            foreach (var px in new[] { po, pf })
            {
                var nonFog = new List<int>();
                for (var i = 0; i < 16; i++)
                {
                    if ((px[i].r, px[i].g, px[i].b) != (Fog.r, Fog.g, Fog.b)) nonFog.Add(i);
                    Assert.True(explored[i] || others[i] || px[i].a <= Fog.a);
                }
                Assert.Equal(new[] { 0, 5 }, nonFog);
            }
        }

        [Fact]
        public void LandMixBlendsCoastsEdgesAndSpecks()
        {
            const int n = 9;
            // Meadows (1) left of x = 5, ocean right; one black forest (2) speck at (2, 4); all explored.
            var land = Enumerable.Range(0, n * n).Select(i => (byte)(i % n < 5 ? 1 : 0)).ToArray();
            land[4 * n + 2] = 2;
            var all = new BitArray(n * n, true);
            var none = new BitArray(n * n);
            var w = new int[n];
            for (var x = 0; x < n; x++) w[x] = MapFilterMath.LandMix(land, all, none, n, Palette, x, 6).W;
            Assert.Equal(81, w[2]); // interior (x 0-1 see the grid edge: outside is no land)
            Assert.True(w.Skip(2).Zip(w.Skip(3), (a, b) => b <= a).All(t => t), string.Join(",", w)); // ramps down across the coast
            Assert.True(w[5] > 0 && w[5] < 81 && w[7] == 0, string.Join(",", w)); // first sea texel still carries some land
            // The speck: its own texel is mostly its neighbours' colour (1/9 weight of its own), so it no longer pops.
            var m = MapFilterMath.LandMix(land, all, none, n, Palette, 2, 4);
            Assert.Equal(81, m.W);
            var r = (m.R + m.W / 2) / m.W;
            Assert.InRange(r, 10, 12); // meadows 10, black forest 20: 10 + 10 x 9/81 = 11.1
            // Unexplored land is no land (fog of war): only the explored-by-others column 0 counts.
            var col0 = new BitArray(n * n);
            for (var y = 0; y < n; y++) col0[y * n] = true;
            Assert.Equal(3 * 9, MapFilterMath.LandMix(land, none, col0, n, Palette, 0, 4).W); // tent 3 (dx 0) x 9 (dy sum)
        }

        [Fact]
        public void FogAlphaIsSoftTentAndSeparableMatchesDirect()
        {
            const int n = 16;
            var explored = new BitArray(n * n);
            var others = new BitArray(n * n);
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                    explored[y * n + x] = (x - 7) * (x - 7) + (y - 8) * (y - 8) < 20 || x == 2 && y == 3;
            others[n * n - 1] = true;
            var all = new byte[n * n];
            MapFilterMath.FogAlphas(explored, others, n, 166, all);
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                    Assert.Equal(MapFilterMath.FogAlpha(explored, others, n, x, y, 166), all[y * n + x]);
            Assert.Equal(0, all[8 * n + 7]); // deep inside the explored disc: clear
            Assert.Equal(166, all[0]); // grid corner far from anything explored: full tint (outside counts as fog)
            // Across the disc edge the tint ramps instead of jumping 0 -> full in one texel.
            var ramp = new int[7];
            for (var i = 0; i < 7; i++) ramp[i] = all[8 * n + (7 + i)];
            Assert.True(ramp.Zip(ramp.Skip(1), (a, b) => b >= a).All(t => t), string.Join(",", ramp));
            Assert.True(ramp.Count(a => a > 0 && a < 166) >= 3, string.Join(",", ramp));
            Assert.All(all, a => Assert.InRange(a, 0, 166));
            var off = new byte[n * n];
            MapFilterMath.FogAlphas(explored, others, n, 0, off);
            Assert.All(off, a => Assert.Equal(0, a));
        }

        [Fact]
        public void UnderwaterTexelIsOcean()
        {
            Assert.Equal(0, MapFilterMath.GridId(Heightmap.Biome.Meadows, 29.9f, 30f));
            Assert.Equal(1, MapFilterMath.GridId(Heightmap.Biome.Meadows, 30f, 30f));
            Assert.Equal(0, MapFilterMath.GridId(Heightmap.Biome.Ocean, 40f, 30f));
        }

        private static byte[] GridOf(int n, Func<int, int, byte> f)
        {
            var a = new byte[n * n];
            for (var i = 0; i < a.Length; i++) a[i] = f(i % n, i / n);
            return a;
        }

        [Fact]
        public void ContourSegmentsAreSmoothLandOnlyAndRespectFog()
        {
            var segs = new List<MapFilterMath.Segment>();
            var all16 = new BitArray(16, true);
            // Straight vertical boundary between x = 1 and x = 2: one merged segment on x = 2 (between texel centres 1.5
            // and 2.5) from the first to the last texel centre, colour = the higher id.
            var ids = GridOf(4, (x, y) => (byte)(x < 2 ? 1 : 2));
            Assert.True(MapFilterMath.Segments(ids, 4, all16, new BitArray(16), 0, 0, 4, 4, 100, segs));
            var v = Assert.Single(segs);
            Assert.Equal((2f, 0.5f, 2f, 3.5f, (byte)2), (v.X0, v.Y0, v.X1, v.Y1, v.Id));
            // A 45-degree staircase boundary comes out as diagonals only (no stair steps).
            const int n = 8;
            var all = new BitArray(n * n, true);
            var none = new BitArray(n * n);
            var stair = GridOf(n, (x, y) => (byte)(x > y ? 2 : 1));
            Assert.True(MapFilterMath.Segments(stair, n, all, none, 0, 0, n, n, 1000, segs));
            Assert.NotEmpty(segs);
            Assert.All(segs, s => Assert.True(s.X0 != s.X1 && s.Y0 != s.Y1, $"{s.X0},{s.Y0}-{s.X1},{s.Y1}"));
            // A lone speck draws nothing; a coast (land / ocean) draws nothing.
            Assert.True(MapFilterMath.Segments(GridOf(n, (x, y) => (byte)(x == 4 && y == 4 ? 1 : 2)), n, all, none, 0, 0, n, n, 1000, segs));
            Assert.Empty(segs);
            Assert.True(MapFilterMath.Segments(GridOf(n, (x, y) => (byte)(x < 4 ? 3 : 0)), n, all, none, 0, 0, n, n, 1000, segs));
            Assert.Empty(segs);
            // Unexplored black forest: the border would reveal it (and trace the fog edge), so nothing is drawn.
            var half = new BitArray(n * n);
            for (var i = 0; i < n * n; i++) half[i] = i % n < 2;
            Assert.True(MapFilterMath.Segments(GridOf(n, (x, y) => (byte)(x < 2 ? 1 : 2)), n, half, none, 0, 0, n, n, 1000, segs));
            Assert.Empty(segs);
            // Horizontal runs merge along a row; over the cap: refused (caller falls back to the outline texture).
            Assert.True(MapFilterMath.Segments(GridOf(n, (x, y) => (byte)(y < 4 ? 1 : 2)), n, all, none, 0, 0, n, n, 1000, segs));
            Assert.Equal((0.5f, 3.5f, 7.5f, (byte)2), (Assert.Single(segs).X0, segs[0].Y0 - 0.5f, segs[0].X1, segs[0].Id));
            Assert.False(MapFilterMath.Segments(stair, n, all, none, 0, 0, n, n, 0, segs));
        }

        [Fact]
        public void ClipKeepsTheInsidePart()
        {
            var r = new Rect(0f, 0f, 10f, 10f);
            var a = new Vector2(-5f, 5f);
            var b = new Vector2(5f, 5f);
            Assert.True(MapFilterMath.Clip(r, ref a, ref b));
            Assert.Equal((0f, 5f), (a.x, b.x));
            a = new Vector2(-5f, -5f);
            b = new Vector2(-1f, 20f);
            Assert.False(MapFilterMath.Clip(r, ref a, ref b));
        }

        [Fact]
        public void TexelPixelsDecideLinesVersusOutline()
        {
            Assert.Equal(1.5f, MapFilterMath.TexelPixels(1536f, 0.5f, 2048), 3); // zoomed out: the texture outline is thin enough
            Assert.Equal(15f, MapFilterMath.TexelPixels(1536f, 0.05f, 2048), 3);
            Assert.Equal(0f, MapFilterMath.TexelPixels(1536f, 0f, 2048));
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

        [Fact]
        public void CustomPinsGridNeverOverflowsLegendAt720p150GuiWithAll25Icons()
        {
            // 1280x720 @ 150% GUI (legend height 642px), 25 icons (5 rows), 4 players, layers(227) + zoom/label(74) + gap(6) = 307px without Public toggle, 343px with Public toggle(36).
            const float legendH = 642f, pad = 16f, sectionH = 45f, playerRow = 36f, minPinsH = 28f;

            foreach (var toggles in new[] { 36f, 0f })
            {
                var fixedBelowWithoutPlayers = 74f + toggles + 227f + 6f;
                var tight = MapFilterMath.ComputeCustomSectionLayout(
                    legendH, pad, sectionH, fixedBelowWithoutPlayers, playerCount: 4, maxPlayersShown: 4, playerRow, minPinsH,
                    iconCount: 25, expanded: true, cols: 5, maxCell: 36f, minCell: 28f, gap: 6f);

                Assert.Equal(5, tight.TotalRows);
                Assert.True(tight.VisibleRows >= 1 && tight.VisibleRows < 5, $"toggles={toggles}: VisibleRows={tight.VisibleRows}");
                Assert.True(tight.Scrolling, $"toggles={toggles}: expected Scrolling=true");
                Assert.True(tight.PlayersShown >= 1, $"toggles={toggles}: PlayersShown={tight.PlayersShown}");
                var playersH = sectionH + tight.PlayersShown * playerRow + 6f;
                var total = pad + sectionH + tight.PinsAvailHeight + tight.SectionHeight + playersH + fixedBelowWithoutPlayers;
                Assert.True(total <= legendH + 0.5f, $"toggles={toggles}: overflow {total} > {legendH}");
            }

            // 1920x1080 @ 100% GUI (~960px legend height), 4 players, Public toggle on: all 5 rows (25 icons) + all 4 players fit without scrolling.
            var tall = MapFilterMath.ComputeCustomSectionLayout(
                960f, pad, sectionH, 74f + 36f + 227f + 6f, playerCount: 4, maxPlayersShown: 4, playerRow, minPinsH,
                iconCount: 25, expanded: true, cols: 5, maxCell: 36f, minCell: 28f, gap: 6f);
            Assert.Equal(5, tall.VisibleRows);
            Assert.Equal(4, tall.PlayersShown);
            Assert.False(tall.Scrolling);
            Assert.True(pad + sectionH + tall.PinsAvailHeight + tall.SectionHeight + (sectionH + 4f * playerRow + 6f) + (74f + 36f + 227f + 6f) <= 960f + 0.5f);

            // Collapsed keeps only header cost, 0 grid height.
            var collapsed = MapFilterMath.ComputeCustomSectionLayout(
                legendH, pad, sectionH, 74f + 36f + 227f + 6f, playerCount: 4, maxPlayersShown: 4, playerRow, minPinsH,
                iconCount: 25, expanded: false, cols: 5, maxCell: 36f, minCell: 28f, gap: 6f);
            Assert.Equal(0, collapsed.VisibleRows);
            Assert.Equal(0f, collapsed.GridHeight);
            Assert.False(collapsed.Scrolling);
        }
    }
}
