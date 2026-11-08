using Morgott.UI.PlayerHud;
using Xunit;

namespace Morgott.UI.Tests
{
    // KeyLabels: mouse / modifier key tokens Latin at any language ("Разместить ЛКМ" build hint, 2026-11-08).
    public class KeyLabelsTests
    {
        [Theory]
        [InlineData("$button_mouse0", "Russian", "LMB")]
        [InlineData("$button_mouse1", "German", "RMB")]
        [InlineData("$button_lshift", "Russian", "Shift")]
        [InlineData("$button_space", "Russian", "Space")]
        public void Token_IsLatin(string key, string language, string expected) => Assert.Equal(expected, KeyLabels.For(key, language));

        [Theory]
        [InlineData("$button_mouse0", "English")] // English keeps the game's own text
        [InlineData("F", "Russian")]               // letters / glyphs untouched
        [InlineData("<sprite=\"xbox\" name=\"a\">", "Russian")]
        [InlineData(null, "Russian")]
        public void Other_KeepsGameText(string key, string language) => Assert.Null(KeyLabels.For(key, language));

        [Theory]
        [InlineData("Й", true)]
        [InlineData("У", true)]
        [InlineData("Q", false)]
        [InlineData("Shift", false)]
        [InlineData(null, false)]
        public void LayoutGlyph(string s, bool expected) => Assert.Equal(expected, KeyLabels.IsLayoutGlyph(s));

        [Fact]
        public void Map_OnlyButtonTokens()
        {
            foreach (var (token, latin) in KeyLabels.Latin)
            {
                Assert.StartsWith("$button_", token);
                Assert.Matches("^[A-Za-z ]+$", latin);
            }
        }
    }
}
