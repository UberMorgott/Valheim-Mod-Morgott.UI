using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Morgott.UI.Icons;
using Xunit;

namespace Morgott.UI.Tests
{
    // ModStatusIconMap (moved from MorgottTweaks ModStatusIconChecks, TASKS 4.5): which foreign status gets which atlas cell.
    public class ModStatusIconMapTests
    {
        [Theory]
        [InlineData("Warfare_Bleeding_Stacking", "bleed")]
        [InlineData("Warfare_BleedingSecondaryAttack", "bleed")]
        [InlineData("Warfare_Impale", "impale")]
        [InlineData("EL_Paralyze", "paralyze")]
        public void MappedStatusGetsItsCell(string status, string cell) =>
            Assert.Equal(Array.IndexOf(ModStatusIconMap.Cells, cell), ModStatusIconMap.CellOf(status));

        [Theory]
        [InlineData("Warfare_Bleeding")] // 4-hit counter, no damage over time
        [InlineData("Warfare_PiercingGreatbowPlains")]
        [InlineData("Burning")] // vanilla keeps its own
        [InlineData("")]
        [InlineData(null)]
        public void OtherStatusKeepsItsIcon(string status) => Assert.Equal(-1, ModStatusIconMap.CellOf(status));

        [Fact]
        public void EveryMappedStatusHasACell() => Assert.All(ModStatusIconMap.Statuses, s => Assert.True(ModStatusIconMap.CellOf(s) >= 0));

        [Fact]
        public void OnlyEpicLootParalyzeGoesIntoObjectDb()
        {
            Assert.True(ModStatusIconMap.RegistersInObjectDb("EL_Paralyze"));
            Assert.False(ModStatusIconMap.RegistersInObjectDb("Warfare_Bleeding_Stacking"));
            Assert.False(ModStatusIconMap.RegistersInObjectDb("Warfare_Impale"));
            Assert.False(ModStatusIconMap.RegistersInObjectDb("Paralyze"));
            Assert.False(ModStatusIconMap.RegistersInObjectDb(null));
        }

        // The atlas is packed in status_icons.json "items" order (pack_aura_icons.py --spec status_icons.json).
        [Fact]
        public void CellsFollowTheSpecOrder()
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "icons", "status_icons.json")));
            Assert.Equal(doc.RootElement.GetProperty("items").EnumerateObject().Select(p => p.Name), ModStatusIconMap.Cells);
        }
    }
}