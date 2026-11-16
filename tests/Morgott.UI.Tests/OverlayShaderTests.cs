using System;
using System.IO;
using Xunit;

namespace Morgott.UI.Tests
{
    // Morgott/Overlay properties and blend are the Core IOverlayVfx.Overlay contract (consumer: Patcher StationLinks).
    public class OverlayShaderTests
    {
        private static string Shader()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "art", "vfx", "Overlay.shader"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return File.ReadAllText(Path.Combine(dir.FullName, "art", "vfx", "Overlay.shader"));
        }

        [Fact]
        public void DeclaresEveryContractProperty()
        {
            var s = Shader();
            Assert.Contains("Shader \"Morgott/Overlay\"", s);
            foreach (var name in new[] { "_MainTex", "_TintColor", "_ZTest", "_Dash", "_DashFill" }) Assert.Contains($" {name} (\"", s);
        }

        [Fact]
        public void ThroughWallAdditiveByDefault()
        {
            var s = Shader();
            Assert.Contains("[Enum(UnityEngine.Rendering.CompareFunction)] _ZTest (\"ZTest\", Float) = 8", s);
            Assert.Contains("ZTest [_ZTest]", s);
            Assert.Contains("Blend One One", s);
            Assert.Contains("ZWrite Off", s);
            Assert.Contains("return fixed4(c.rgb * c.a, 0);", s);
            Assert.Contains("if (_Dash > 0 && frac(i.dash / _Dash) > _DashFill) discard;", s);
        }
    }
}
