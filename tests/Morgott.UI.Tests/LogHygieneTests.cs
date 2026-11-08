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
            var bad = Directory.EnumerateFiles(SrcRoot(), "*.cs", SearchOption.AllDirectories)
                .Where(f => Path.GetFileName(f) != "UiText.cs")
                .SelectMany(f => File.ReadAllLines(f).Select(l => (File: Path.GetFileName(f), Line: l.Trim())))
                // Harmony lookups typeof(TextMeshProUGUI), "OnEnable" (ForeignUi) / "OnDestroy" (TextCacheRelease) are not creations.
                .Where(x => !x.Line.StartsWith("//") && Creation.IsMatch(x.Line) && !x.Line.Contains("\"OnEnable\"") && !x.Line.Contains("\"OnDestroy\""))
                .Select(x => x.File + ": " + x.Line)
                .ToList();
            Assert.Empty(bad);
        }

        [Theory]
        [InlineData("Hud/StripClear.cs", "strip clear:")]
        [InlineData("Diagnostics/SkinDiagnostics.cs", "] diag {summary}")]
        [InlineData("ModScreens/ForeignUi.cs", "] mod unit ")]
        public void DiagnosticInfoLinesAreDevOnly(string file, string marker)
        {
            var lines = File.ReadAllLines(Path.Combine(SrcRoot(), file)).Where(l => l.Contains(marker) && l.Contains("LogInfo")).ToList();
            Assert.NotEmpty(lines);
            Assert.All(lines, l => Assert.Contains("Plugin.Verbose", l));
        }
    }
}
