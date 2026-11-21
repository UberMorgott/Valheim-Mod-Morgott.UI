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
            foreach (var name in new[] { "_MainTex", "_TintColor", "_ZTest", "_Dash", "_DashFill", "_DstBlend", "_Outline", "_OutlineColor" }) Assert.Contains($" {name} (\"", s);
        }

        [Fact]
        public void ThroughWallAdditiveByDefault()
        {
            var s = Shader();
            Assert.Contains("[Enum(UnityEngine.Rendering.CompareFunction)] _ZTest (\"ZTest\", Float) = 8", s);
            Assert.Contains("ZTest [_ZTest]", s);
            Assert.Contains("[Enum(UnityEngine.Rendering.BlendMode)] _DstBlend (\"Dst blend\", Float) = 1", s); // 1 One = additive
            Assert.Contains("Blend One [_DstBlend], Zero One", s);
            Assert.Contains("ZWrite Off", s);
            Assert.Contains("return fixed4(c.rgb * c.a, c.a);", s);
            Assert.Contains("if (_Dash > 0 && frac(i.raw.x / _Dash) > _DashFill) discard;", s);
        }

        [Fact]
        public void OutlineOffByDefault()
        {
            var s = Shader();
            Assert.Contains("_Outline (\"Outline\", Range(0, 1)) = 0", s);
            Assert.Contains("if (_Outline > 0)", s);
        }
    }
}
