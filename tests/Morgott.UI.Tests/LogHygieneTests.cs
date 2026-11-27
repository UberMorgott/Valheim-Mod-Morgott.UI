using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Morgott.UI.Tests
{
    // Player log 2026-11-07: TMP "LiberationSans SDF Font Asset was not found" x280 (texts added to active objects before a
    // font was set) and development Info lines in every play session (strip clear x317, diag Tooltip x246).
    public class LogHygieneTests
    {
        private static string SrcRoot()
        {
            var d = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "Morgott.UI.sln"))) d = d.Parent;
            return Path.Combine(d!.FullName, "src", "Morgott.UI");
        }

        private static readonly Regex Creation = new Regex(@"AddComponent<(TMPro\.)?TextMeshProUGUI>|typeof\((TMPro\.)?TextMeshProUGUI\)");

        [Fact]
        public void TmpTextsAreAddedOnlyThroughUiText()
        {
            var bad = new System.Collections.Generic.List<string>();
            var files = Directory.GetFiles(SrcRoot(), "*.cs", SearchOption.AllDirectories);
            for (var fi = 0; fi < files.Length; fi++)
            {
                var fileName = Path.GetFileName(files[fi]);
                if (fileName == "UiText.cs") continue;
                var lines = File.ReadAllLines(files[fi]);
                for (var li = 0; li < lines.Length; li++)
                {
                    var line = lines[li].Trim();
                    // Harmony lookups typeof(TextMeshProUGUI), "OnEnable" (ForeignUi) / "OnDestroy" (TextCacheRelease) are not creations.
                    if (!line.StartsWith("//") && Creation.IsMatch(line) && !line.Contains("\"OnEnable\"") && !line.Contains("\"OnDestroy\""))
                        bad.Add(fileName + ": " + line);
                }
            }
            Assert.Empty(bad);
        }

        [Theory]
        [InlineData("Hud/StripClear.cs", "strip clear:")]
        [InlineData("Diagnostics/SkinDiagnostics.cs", "] diag {summary}")]
        [InlineData("ModScreens/ForeignUi.cs", "] mod unit ")]
        public void DiagnosticInfoLinesAreDevOnly(string file, string marker)
        {
            var all = File.ReadAllLines(Path.Combine(SrcRoot(), file));
            var count = 0;
            for (var i = 0; i < all.Length; i++)
            {
                var l = all[i];
                if (!l.Contains(marker) || !l.Contains("LogInfo")) continue;
                count++;
                Assert.Contains("Plugin.Verbose", l);
            }
            Assert.True(count > 0);
        }
    }
}
