using System;
using System.Collections.Generic;
using System.Linq;
using Morgott.Core;
using Morgott.UI.Theme;
using UnityEngine;
using UnityEngine.UI;
using Xunit;

namespace Morgott.UI.Tests
{
    public class HexColorTests
    {
        [Theory]
        [InlineData("#FF0000", 1f, 0f, 0f, 1f)]
        [InlineData("#00FF0080", 0f, 1f, 0f, 128f / 255f)]
        public void Parses(string hex, float r, float g, float b, float a)
        {
            Assert.True(HexColor.TryParse(hex, out var c));
            Assert.Equal(r, c.r, 3);
            Assert.Equal(g, c.g, 3);
            Assert.Equal(b, c.b, 3);
            Assert.Equal(a, c.a, 3);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("FF0000")]
        [InlineData("#FF00")]
        [InlineData("#GG0000")]
        public void Rejects(string hex) => Assert.False(HexColor.TryParse(hex, out _));
    }

    public class ThemeValidationTests
    {
        private static ThemeFile Minimal() => new ThemeFile
        {
            schema = 1,
            name = "t",
            colors = new[] { new ColorEntry { token = "color.a", value = "#FFFFFF" } },
            sprites = new[] { new AssetEntry { token = "sprite.a", asset = "rs_a" } },
            fonts = new AssetEntry[0],
            floats = new FloatEntry[0],
            roles = new[]
            {
                new RoleRule { role = "panel", sprite = "sprite.a", color = "color.a", imageType = "Sliced" },
                new RoleRule { role = "content" },
                new RoleRule { role = "skip" }
            },
            spriteRoles = new[] { new SpriteRoleEntry { match = "woodpanel_*", role = "panel" } },
            families = new[] { new FamilyEntry { family = "Menu", requires = new[] { "sprite.a" } } }
        };

        [Fact]
        public void MinimalThemeIsValid() => Assert.Empty(ThemeSchema.Validate(Minimal()));

        // Fonts: empty source = our bundle, "loaded" = a TMP asset the game / another plugin loads (rune face).
        [Theory]
        [InlineData(null, true)]
        [InlineData("loaded", true)]
        [InlineData("disk", false)]
        public void FontSourceIsBundleOrLoaded(string source, bool valid)
        {
            var f = Minimal();
            f.fonts = new[] { new AssetEntry { token = "font.rune", asset = "Valheim-Norsebold", source = source } };
            Assert.Equal(valid, !ThemeSchema.Validate(f).Exists(e => e.Contains("source")));
        }

        // Every token sits in its own section (run 20261023-191817: sprite.hud.strip7.field / .rim were listed under
        // "floats", never registered as sprites - the bar field and channel rims were not drawn in game).
        [Fact]
        public void DefaultThemeTokensInTheirSection()
        {
            using var s = typeof(ThemeSchema).Assembly.GetManifestResourceStream("Morgott.UI.default-theme.json");
            using var r = new System.IO.StreamReader(s!);
            var f = ThemeJson.Parse(r.ReadToEnd());
            Assert.Empty(System.Array.FindAll(f.floats, x => x.token.StartsWith("sprite.", System.StringComparison.Ordinal)));
            Assert.Empty(System.Array.FindAll(f.sprites, x => !x.token.StartsWith("sprite.", System.StringComparison.Ordinal)));
            Assert.Contains(f.sprites, x => x.token == "sprite.hud.panel.hotbar");
        }

        // hud2 sharpness (batch201): the packed atlas pages ship mip bias 0 (the importer's -0.5 is lost in packing),
        // the runtime sets the theme's bias on every page; the stats Hi atlas uses the same value.
        [Fact]
        public void DefaultThemeAtlasMipBiasIsSharp()
        {
            using var s = typeof(ThemeSchema).Assembly.GetManifestResourceStream("Morgott.UI.default-theme.json");
            using var r = new System.IO.StreamReader(s!);
            var f = ThemeJson.Parse(r.ReadToEnd());
            Assert.Equal(-0.7f, System.Array.Find(f.floats, x => x.token == "bias.atlas.mip").value, 3);
            Assert.Equal(-0.7f, System.Array.Find(f.floats, x => x.token == "size.stats.mip.bias").value, 3);
        }

        // The shipped minimap (0.9.3, user ask): ring 320 ref (was 220) capped at 26 % of the safe height (was 18 %).
        [Fact]
        public void DefaultThemeMinimapIsLarge()
        {
            using var s = typeof(ThemeSchema).Assembly.GetManifestResourceStream("Morgott.UI.default-theme.json");
            using var r = new System.IO.StreamReader(s!);
            var f = ThemeJson.Parse(r.ReadToEnd());
            Assert.Equal(320f, System.Array.Find(f.floats, x => x.token == "size.minimap.ring").value);
            Assert.Equal(0.26f, System.Array.Find(f.floats, x => x.token == "share.minimap.ring.h").value, 3);
            // 1080p: the cap (0.26 x 1080 = 280.8 px at scale 1) wins over the 320 ref size.
            Assert.Equal(280.8f, Morgott.UI.MapScreen.MinimapMath.RingSize(320f, 1080f, 1f, 0.26f), 1);
        }

        // The shipped theme: two faces (user 2026-10-22), the rune face as display bound late from a loaded asset (menu
        // entries, headers), PT Sans for everything else (buttons, rows, tooltips, item names) with its real bold cut.
        [Fact]
        public void DefaultThemeHasTwoFaces()
        {
            using var s = typeof(ThemeSchema).Assembly.GetManifestResourceStream("Morgott.UI.default-theme.json");
            using var r = new System.IO.StreamReader(s!);
            var f = ThemeJson.Parse(r.ReadToEnd());
            // + font.map.header (2026-11-10 map mockup: the game's Averia serif for the map list section titles only, no role uses it).
            Assert.Equal(new[] { "font.display", "font.map.header", "font.body", "font.body.bold" }, System.Array.ConvertAll(f.fonts, x => x.token));
            Assert.DoesNotContain(f.roles, x => x.font == "font.map.header");
            Assert.Equal("loaded", System.Array.Find(f.fonts, x => x.token == "font.display").source);
            Assert.Equal("Valheim-Norsebold", System.Array.Find(f.fonts, x => x.token == "font.display").asset);
            foreach (var role in new[] { "menu.entry", "button.label.menu", "text.header", "info.title", "text.display" })
                Assert.Equal("font.display", System.Array.Find(f.roles, x => x.role == role).font);
            foreach (var role in new[] { "button.label", "row.label" })
                Assert.Equal("font.body", System.Array.Find(f.roles, x => x.role == role).font);
            foreach (var role in new[] { "tooltip.topic", "craft.title", "dialog.subject", "button.label.primary" })
                Assert.Equal("font.body.bold", System.Array.Find(f.roles, x => x.role == role).font);
            Assert.All(f.roles, x => Assert.True(x.font == null || x.font == "font.body" || x.font == "font.body.bold" || x.font == "font.display", x.role + ": " + x.font));
        }

        [Fact]
        public void UndefinedTokenReferenceIsAnError()
        {
            var f = Minimal();
            f.roles[0].color = "color.missing";
            Assert.Contains(ThemeSchema.Validate(f), e => e.Contains("color.missing"));
        }

        [Fact]
        public void DuplicateTokenBadColourAndTypeAreErrors()
        {
            var f = Minimal();
            f.colors = new[] { new ColorEntry { token = "color.a", value = "#FFFFFF" }, new ColorEntry { token = "color.a", value = "red" } };
            f.roles[0].imageType = "Wobbly";
            var errors = ThemeSchema.Validate(f);
            Assert.Contains(errors, e => e.Contains("duplicate token"));
            Assert.Contains(errors, e => e.Contains("bad value"));
            Assert.Contains(errors, e => e.Contains("imageType"));
        }

        [Fact]
        public void ClassifierRolesAreRequired()
        {
            var f = Minimal();
            f.roles = new[] { f.roles[0] };
            Assert.Contains(ThemeSchema.Validate(f), e => e.Contains("'content' missing"));
        }

        [Fact]
        public void EmbeddedDefaultThemeIsValid()
        {
            using var stream = typeof(ThemeSchema).Assembly.GetManifestResourceStream("Morgott.UI.default-theme.json");
            Assert.NotNull(stream);
            using var reader = new System.IO.StreamReader(stream);
            var theme = ThemeJson.Parse(reader.ReadToEnd());
            Assert.Empty(ThemeSchema.Validate(theme));
            var roles = theme.roles.Select(r => r.role).ToList();
            foreach (var role in new[] { "panel", "button", "menu.entry", "tooltip.box", "tooltip.text", "tooltip.topic", "content", "skip" })
                Assert.Contains(role, roles);
            Assert.Equal("button_hover", theme.sprites.Single(s => s.token == theme.roles.Single(r => r.role == "button").spriteHover).asset);
        }

        [Theory]
        [InlineData("{\"schema\": 1,}")]
        [InlineData("{\"schema\": 1} x")]
        [InlineData("{\"a\": 1, \"a\": 2}")]
        [InlineData("[1]")]
        public void ParserRejectsBadJson(string json) => Assert.Throws<FormatException>(() => ThemeJson.Parse(json));
    }

}
