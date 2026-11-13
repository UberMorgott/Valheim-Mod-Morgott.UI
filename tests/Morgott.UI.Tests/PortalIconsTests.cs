using System.IO;
using Morgott.Core;
using Xunit;

namespace Morgott.UI.Tests
{
    // Portal style icons: one source per Core PortalStyle id (NN_name.png, the catalog's names) and the packed atlas the
    // plugin embeds (MapScreen\PortalIcons.cs: 8 x 128 px cells per row, id N = cell N-1).
    public class PortalIconsTests
    {
        private static string Assets()
        {
            var d = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "Morgott.UI.sln"))) d = d.Parent;
            return Path.Combine(d!.FullName, "src", "Morgott.UI", "Assets");
        }

        [Fact]
        public void EveryCatalogIdHasItsSource()
        {
            for (var id = 1; id < PortalStyle.Icons.Length; id++)
                Assert.True(File.Exists(Path.Combine(Assets(), "PortalIcons", $"{id:00}_{PortalStyle.Icons[id]}.png")), $"{id:00}_{PortalStyle.Icons[id]}.png");
            Assert.Equal(PortalStyle.Icons.Length - 1, Directory.GetFiles(Path.Combine(Assets(), "PortalIcons"), "*.png").Length);
        }

        [Fact]
        public void AtlasHoldsEveryIcon()
        {
            // PNG IHDR: width / height big-endian at bytes 16 / 20.
            var png = File.ReadAllBytes(Path.Combine(Assets(), "PortalIcons.png"));
            int Be(int at) => png[at] << 24 | png[at + 1] << 16 | png[at + 2] << 8 | png[at + 3];
            Assert.Equal(8 * 128, Be(16));
            // Icon rows, then the same rows again as halos (PortalArchIcons.MeshFor reads a halo at its icon's v - 0.5).
            Assert.Equal(2 * 128 * ((PortalStyle.Icons.Length - 1 + 7) / 8), Be(20));
        }
    }
}
