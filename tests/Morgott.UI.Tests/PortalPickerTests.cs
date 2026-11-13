using System.Collections.Generic;
using System.Linq;
using Morgott.Core;
using Morgott.UI.MapScreen;
using UnityEngine;
using Xunit;

namespace Morgott.UI.Tests
{
    // Portal picker list (PortalPickerMath): sections, sort, search, entry row, gamepad step, click vs double click.
    public class PortalPickerTests
    {
        private static PortalInfo P(uint id, string name, float x, bool fav = false, bool stone = false) =>
            new PortalInfo { Id = new ZDOID(1L, id), Name = name, Position = new Vector3(x, 30f, 0f), Favorite = fav, Stone = stone };

        private static readonly PortalInfo Here = new PortalInfo { Id = new ZDOID(1L, 99u), Name = "Лагерь", Position = Vector3.zero };

        private static List<PortalInfo> World() => new List<PortalInfo>
        {
            P(1, "Шахта", 3870f, fav: true), P(2, "Болото-1", 2150f), P(3, "Дом", 1240f, fav: true),
            P(4, "Равнины", 5920f), P(5, "Курганы", 2610f, stone: true),
        };

        [Fact]
        public void FavoritesFirstThenHereThenRestByDistance()
        {
            var rows = new List<PortalRow>();
            var fav = PortalPickerMath.Build(World(), Here, "", rows);
            Assert.Equal(2, fav);
            Assert.Equal(new[] { "Дом", "Шахта", "Лагерь", "Болото-1", "Курганы", "Равнины" }, rows.Select(r => r.Portal.Name));
            Assert.True(rows[2].Here);
            Assert.Equal(0f, rows[2].Distance);
            Assert.Single(rows, r => r.Here);
            Assert.Equal(1240f, rows[0].Distance, 1);
            // Favourites are not repeated in ВСЕ ПОРТАЛЫ (user 2026-11-11).
            Assert.All(rows.Skip(fav), r => Assert.False(r.Portal.Favorite));
        }

        [Fact]
        public void DistanceIsOnTheMapPlane()
        {
            Assert.Equal(5f, PortalPickerMath.DistanceXZ(new Vector3(0f, 100f, 0f), new Vector3(3f, -50f, 4f)), 3);
        }

        [Fact]
        public void SearchIsCaseInsensitiveOnNames()
        {
            var rows = new List<PortalRow>();
            var fav = PortalPickerMath.Build(World(), Here, "  ШАХ ", rows);
            Assert.Equal(1, fav);
            Assert.Equal(new[] { "Шахта" }, rows.Select(r => r.Portal.Name));
            fav = PortalPickerMath.Build(World(), Here, "лаг", rows);
            Assert.Equal(0, fav);
            Assert.True(Assert.Single(rows).Here);
            PortalPickerMath.Build(World(), null, "", rows);
            Assert.DoesNotContain(rows, r => r.Here);
            Assert.Equal(5, rows.Count);
        }

        [Fact]
        public void MetersGroupedByThousands()
        {
            Assert.Equal("0", PortalPickerMath.Meters(0.4f));
            Assert.Equal("999", PortalPickerMath.Meters(999f));
            Assert.Equal("1 240", PortalPickerMath.Meters(1240.2f));
            Assert.Equal("12 345 678", PortalPickerMath.Meters(12345678f));
        }

        [Fact]
        public void StepSkipsTheEntryRow()
        {
            var rows = new List<PortalRow>();
            PortalPickerMath.Build(World(), Here, "", rows); // Дом, Шахта, [Лагерь], Болото-1, ...
            Assert.Equal(0, PortalPickerMath.Step(rows, -1, 1));
            Assert.Equal(3, PortalPickerMath.Step(rows, 1, 1));
            Assert.Equal(1, PortalPickerMath.Step(rows, 3, -1));
            Assert.Equal(5, PortalPickerMath.Step(rows, 5, 1)); // end: stays
            Assert.Equal(5, PortalPickerMath.Step(rows, -1, -1));
            PortalPickerMath.Build(new List<PortalInfo>(), Here, "", rows);
            Assert.Equal(-1, PortalPickerMath.Step(rows, -1, 1));
        }

        [Fact]
        public void VisibleCenterIsBetweenTheListAndTheMapEdge()
        {
            // Run 20261111-152231: list right edge ~420, map image right edge ~1485 at 1920x1080 -> centre ~952.
            var c = PortalPickerMath.VisibleCenter(420f, 1485f, 33f, 1047f);
            Assert.Equal(952.5f, c.x, 2);
            Assert.Equal(540f, c.y, 2);
            Assert.Equal(500f, PortalPickerMath.VisibleCenter(500f, 400f, 0f, 0f).x, 2); // degenerate: never left of the list
        }

        [Fact]
        public void DoubleClickOnlyOnTheSamePortalInTime()
        {
            var c = new PortalClicks();
            ZDOID a = new ZDOID(1L, 1u), b = new ZDOID(1L, 2u);
            Assert.False(c.IsDouble(a, 10f));
            Assert.True(c.IsDouble(a, 10.2f));
            Assert.False(c.IsDouble(a, 10.3f)); // the pair was consumed
            Assert.False(c.IsDouble(b, 10.4f));
            Assert.False(c.IsDouble(a, 10.5f));
            Assert.False(c.IsDouble(a, 10.5f + PortalPickerMath.DoubleClick + 0.01f));
        }

        // Host save: the routed RPC to self acks inside send, so "saved" must be the last status (2026-11-13 run).
        [Fact]
        public void SynchronousAckLandsAfterSaving()
        {
            var status = "";
            var ok = PortalPickerMath.Send(() => status = "saving", () => { status = "saved"; return true; }, () => status = "failed");
            Assert.True(ok);
            Assert.Equal("saved", status);

            ok = PortalPickerMath.Send(() => status = "saving", () => false, () => status = "failed");
            Assert.False(ok);
            Assert.Equal("failed", status);
        }
    }
}
