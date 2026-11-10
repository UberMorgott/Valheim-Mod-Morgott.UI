using System.IO;
using Xunit;

namespace Morgott.UI.Tests
{
    // Summons 0.5.0 stamps pets with Core Contracts.PetOwner ("su_pet_owner"); a local "mt_pet_owner" literal lost every pet frame.
    public class PetOwnerKeyTests
    {
        [Fact]
        public void PetFramesReadTheCorePetOwnerKey()
        {
            var d = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "Morgott.UI.sln"))) d = d.Parent;
            var src = Path.Combine(d!.FullName, "src", "Morgott.UI");
            foreach (var f in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
                Assert.DoesNotContain("\"mt_pet_owner\"", File.ReadAllText(f));
            Assert.Contains("Morgott.Core.Contracts.PetOwner.GetStableHashCode()", File.ReadAllText(Path.Combine(src, "Hud", "SummonFrames.cs")));
        }
    }
}
