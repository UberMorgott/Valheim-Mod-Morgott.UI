using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Morgott.UI.Compat;
using Morgott.UI.WorldHud;
using Xunit;

namespace Morgott.UI.Tests
{
    // ValheimBuildCamera texts (BuildCameraWords) in both translation files, and the build menu comfort badge corner.
    public class BuildHudRuTests
    {
        private static Dictionary<string, string> Load(string language)
        {
            using var s = typeof(BuildHudRuTests).Assembly.GetManifestResourceStream($"Morgott.UI.Translations.{language}.json");
            Assert.NotNull(s);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(s);
        }

        private static IEnumerable<KeyValuePair<string, string>> AllWords() => BuildCameraWords.Texts.Concat(BuildCameraWords.Reasons);

        [Fact]
        public void EnglishValueIsTheModLiteral()
        {
            var en = Load("English");
            foreach (var (literal, key) in AllWords())
            {
                Assert.True(en.TryGetValue(key, out var value), $"English.json misses {key}");
                Assert.Equal(literal, value);
            }
        }

        // Fragments are concatenated with counts / key labels: the Russian keeps the leading / trailing spaces and line breaks.
        [Fact]
        public void RussianKeepsFragmentShape()
        {
            var ru = Load("Russian");
            foreach (var (literal, key) in AllWords())
            {
                Assert.True(ru.TryGetValue(key, out var value) && value.Trim().Length > 0, $"Russian.json misses {key}");
                Assert.Equal(literal.Length - literal.TrimStart(' ').Length, value.Length - value.TrimStart(' ').Length);
                Assert.Equal(literal.Length - literal.TrimEnd(' ').Length, value.Length - value.TrimEnd(' ').Length);
                Assert.Equal(literal.Count(c => c == '\n'), value.Count(c => c == '\n'));
                Assert.Equal(literal.StartsWith('\n'), value.StartsWith('\n'));
                Assert.Equal(literal.EndsWith('\n'), value.EndsWith('\n'));
            }
        }

        // Key labels stay Latin (memory key-hints-english): every Latin key token of the English survives in the Russian.
        [Fact]
        public void RussianKeepsKeyLabels()
        {
            var ru = Load("Russian");
            foreach (var (literal, key) in AllWords())
                foreach (var label in new[] { "LB", "+ Y", "F6", "F9", "Shift" })
                    if (literal.Contains(label)) Assert.Contains(label, ru[key]);
        }

        // StopCamera switches on its reason: no reason may be a generic text (that literal would be replaced in the switch).
        [Fact]
        public void ReasonsAreNotGenericTexts()
        {
            foreach (var reason in BuildCameraWords.Reasons.Keys) Assert.False(BuildCameraWords.Texts.ContainsKey(reason));
            Assert.False(BuildCameraWords.Texts.ContainsKey("Escape"));
            Assert.False(BuildCameraWords.Texts.ContainsKey("Configuration reload"));
        }

        [Fact]
        public void Badge_TakesBottomLeftWhenFree()
        {
            Assert.Equal(0, ComfortBadgeMath.Pick(new List<ComfortBadgeMath.Box>()));
            // star top-right, arrow top-left: bottom-left is free
            Assert.Equal(0, ComfortBadgeMath.Pick(new[] { new ComfortBadgeMath.Box(0.7f, 0.7f, 1f, 1f), new ComfortBadgeMath.Box(0f, 0.7f, 0.3f, 1f) }));
        }

        [Fact]
        public void Badge_AvoidsTakenCorners()
        {
            var bottomLeft = new ComfortBadgeMath.Box(0f, 0f, 0.3f, 0.3f);
            Assert.Equal(1, ComfortBadgeMath.Pick(new[] { bottomLeft }));
            var bottomRight = new ComfortBadgeMath.Box(0.7f, 0f, 1f, 0.3f);
            Assert.Equal(2, ComfortBadgeMath.Pick(new[] { bottomLeft, bottomRight }));
            for (var i = 0; i < 4; i++)
            {
                var c = ComfortBadgeMath.Corner(i);
                Assert.False(c.Overlaps(new ComfortBadgeMath.Box(0.45f, 0.45f, 0.55f, 0.55f)), "badge covers the icon centre");
            }
        }

        [Theory]
        [InlineData(0, "")]
        [InlineData(1, "+1")]
        [InlineData(3, "+3")]
        public void Badge_Label(int comfort, string expected) => Assert.Equal(expected, ComfortBadgeMath.Label(comfort));
    }
}
