using System;
using System.IO;
using Xunit;

namespace Morgott.UI.Tests
{
    // Morgott/Shell _RimAlpha (Core IVfx.Shell contract; consumer: Power staff shield bubble): off by default, so every older
    // shell (rarity, gems, shield flash) keeps its additive rim.
    public class ShellShaderTests
    {
        private static string Shader()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "art", "vfx", "Shell.shader"))) dir = dir.Parent;
            Assert.NotNull(dir);
            return File.ReadAllText(Path.Combine(dir.FullName, "art", "vfx", "Shell.shader"));
        }

        [Fact]
        public void RimAlphaOffByDefaultAndAlphaOver()
        {
            var s = Shader();
            Assert.Contains("_RimAlpha (\"Rim opacity (0 additive rim)\", Range(0, 1)) = 0", s);
            Assert.Contains("float aR = _RimAlpha * fres * Pulse();", s);
            Assert.Contains("(s.a + (1.0 - s.a) * aR) * fade", s);
            Assert.Contains("Blend One OneMinusSrcAlpha", s);
        }
    }
}
