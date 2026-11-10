using System;
using System.IO;
using Morgott.Core;
using Xunit;

namespace Morgott.UI.Tests
{
    // Morgott/Web property names are the Core IVfx.Web contract (consumer: Morgott.Creatures spider webs).
    public class WebShaderTests
    {
        private static string Shader()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "art", "vfx", "Web.shader"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return File.ReadAllText(Path.Combine(dir.FullName, "art", "vfx", "Web.shader"));
        }

        private static readonly string[] Props =
        {
            "_Mode", "_MainTex", "_Color", "_Opacity", "_DewColor", "_DewDensity", "_DewSparkle", "_Rime", "_Glow", "_WindAmp",
            "_WindFreq", "_FadeNear", "_FadeFar", "_Tear", "_TriScale", "_BurnStart", "_Clock", "_SweepSpeed", "_BurnEdge",
            "_BurnColor", "_CharColor", "_BurnOrigin",
        };

        [Fact]
        public void DeclaresEveryContractProperty()
        {
            var s = Shader();
            foreach (var name in Props) Assert.Contains($"        {name} (\"", s);
        }

        [Fact]
        public void SheetArrayMatchesCore() => Assert.Contains($"float4 _SheetState[{WebBurn.MaxSheets}];", Shader());

        [Fact]
        public void OffSentinelMatchesCore()
        {
            Assert.Contains($"Float) = {WebBurn.Off.ToString("0", System.Globalization.CultureInfo.InvariantCulture)}", Shader());
            Assert.Contains($"_BurnStart > {(WebBurn.Off / 2f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}", Shader());
        }

        // GoneAt == shader clip: the noise threshold stays below progress 1 (cutAt = 0.1 + 0.8 * n, n in 0..1).
        [Fact]
        public void BurnThresholdBelowOne() => Assert.Contains("float cutAt = 0.1 + 0.8 * n;", Shader());

        [Fact]
        public void DefaultsMatchCore()
        {
            var s = Shader();
            Assert.Contains($"_SweepSpeed (\"Sweep speed (m/s)\", Float) = {WebBurn.DefaultSweepSpeed}", s);
            Assert.Contains($"_BurnEdge (\"Burn window (s)\", Float) = {WebBurn.DefaultEdge.ToString(System.Globalization.CultureInfo.InvariantCulture)}", s);
        }
    }
}
