using System;
using System.Collections.Generic;
using System.Text;
using Morgott.Core;
using UnityEngine;

namespace Morgott.UI.MapScreen
{
    // Portal picker list (managed, unit-tested): ИЗБРАННОЕ = favourites, ВСЕ ПОРТАЛЫ = the entry portal ("вы здесь")
    // then every other portal; favourites are not repeated there (user 2026-11-11). Each section sorted by distance from
    // the entry (XZ, map distance), ties by name; the search keeps names containing the query, case-insensitive.
    public struct PortalRow
    {
        public PortalInfo Portal;
        public float Distance;
        public bool Here;
    }

    public static class PortalPickerMath
    {
        // Same window as vanilla's map double click (Minimap.OnMapLeftUp, Minimap.cs:2412).
        public const float DoubleClick = 0.3f;

        // portals: names already localized. here: the entry portal row (Here = true), null when unknown. Returns the
        // number of favourite rows (rows [0, n) = ИЗБРАННОЕ, the rest = ВСЕ ПОРТАЛЫ).
        public static int Build(List<PortalInfo> portals, PortalInfo? here, string query, List<PortalRow> into)
        {
            into.Clear();
            var origin = here?.Position ?? Vector3.zero;
            foreach (var p in portals)
                if (Matches(p.Name, query)) into.Add(new PortalRow { Portal = p, Distance = DistanceXZ(origin, p.Position) });
            into.Sort((a, b) => a.Portal.Favorite != b.Portal.Favorite ? (a.Portal.Favorite ? -1 : 1)
                : a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance)
                : string.Compare(a.Portal.Name, b.Portal.Name, StringComparison.CurrentCultureIgnoreCase));
            var favorites = 0;
            while (favorites < into.Count && into[favorites].Portal.Favorite) favorites++;
            if (here is PortalInfo h && Matches(h.Name, query)) into.Insert(favorites, new PortalRow { Portal = h, Here = true });
            return favorites;
        }

        public static bool Matches(string name, string query) =>
            string.IsNullOrEmpty(query) || (name ?? "").IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;

        public static float DistanceXZ(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        // "1 240": whole metres grouped by three with a space (mockup), culture-independent.
        public static string Meters(float distance)
        {
            var digits = ((long)Math.Round(distance)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var sb = new StringBuilder(digits.Length + 4);
            for (var i = 0; i < digits.Length; i++)
            {
                if (i > 0 && (digits.Length - i) % 3 == 0) sb.Append(' ');
                sb.Append(digits[i]);
            }
            return sb.ToString();
        }

        // Gamepad / keyboard step from row current by dir (+1 down, -1 up), skipping the entry row; -1 when nothing to pick.
        public static int Step(List<PortalRow> rows, int current, int dir)
        {
            if (current < 0 || current >= rows.Count) current = dir > 0 ? -1 : rows.Count;
            for (var i = current + dir; i >= 0 && i < rows.Count; i += dir)
                if (!rows[i].Here) return i;
            return current >= 0 && current < rows.Count && !rows[current].Here ? current : -1;
        }

        public static int IndexOf(List<PortalRow> rows, ZDOID id)
        {
            for (var i = 0; i < rows.Count; i++)
                if (rows[i].Portal.Id == id) return i;
            return -1;
        }
    }

    // Single vs double click on one portal (a list row or its map pin): the second click on the same portal within
    // DoubleClick seconds is a double click; a double click consumes the pair.
    public sealed class PortalClicks
    {
        private ZDOID _last = ZDOID.None;
        private float _at = float.NegativeInfinity;

        public bool IsDouble(ZDOID id, float now)
        {
            var dbl = id == _last && now - _at <= PortalPickerMath.DoubleClick;
            _last = dbl ? ZDOID.None : id;
            _at = dbl ? float.NegativeInfinity : now;
            return dbl;
        }
    }
}
