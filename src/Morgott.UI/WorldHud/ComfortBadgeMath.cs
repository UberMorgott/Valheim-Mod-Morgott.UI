using System.Collections.Generic;

namespace Morgott.UI.WorldHud
{
    // Comfort badge on build menu piece buttons (pieces with Piece.m_comfort > 0): which corner of the cell it takes.
    // Boxes are in the cell's normalized space (0..1, y up). The badge never takes a corner that intersects an existing
    // badge (vanilla favourite star / upgrade arrow); the icon's key part is the centre, so only corners are candidates.
    internal static class ComfortBadgeMath
    {
        internal const float Size = 0.4f;

        internal readonly struct Box
        {
            public readonly float X0, Y0, X1, Y1;
            public Box(float x0, float y0, float x1, float y1) { X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; }
            public bool Overlaps(Box o) => X0 < o.X1 && o.X0 < X1 && Y0 < o.Y1 && o.Y0 < Y1;
        }

        // Preference: bottom-left, bottom-right, top-left, top-right.
        internal static Box Corner(int index)
        {
            var left = index == 0 || index == 2;
            var bottom = index < 2;
            return new Box(left ? 0f : 1f - Size, bottom ? 0f : 1f - Size, left ? Size : 1f, bottom ? Size : 1f);
        }

        // First free corner; every corner taken -> the first (the badge still shows, the overlap is the lesser loss).
        internal static int Pick(IReadOnlyList<Box> taken)
        {
            for (var i = 0; i < 4; i++)
            {
                var c = Corner(i);
                var free = true;
                foreach (var t in taken)
                    if (c.Overlaps(t)) { free = false; break; }
                if (free) return i;
            }
            return 0;
        }

        internal static string Label(int comfort) => comfort > 0 ? "+" + comfort : "";

        // A comfort piece in range of the player (Piece.m_comfortGroup as int, 0 = None; m_name; GetComfort()).
        internal readonly struct Source
        {
            public readonly int Group;
            public readonly string Name;
            public readonly int Comfort;
            public Source(int group, string name, int comfort) { Group = group; Name = name; Comfort = comfort; }
        }

        // True when one more such piece adds no comfort here. Vanilla SE_Rested.CalculateComfortLevel (SE_Rested.cs:68-91)
        // sorts the pieces in range by group, comfort descending, name, and counts only the first of a group (group != None)
        // and of a name: a piece in range with at least this comfort and the same group or name already holds the slot.
        internal static bool Counted(int group, string name, int comfort, IReadOnlyList<Source> nearby)
        {
            foreach (var n in nearby)
                if (n.Comfort >= comfort && (n.Name == name || (group != 0 && n.Group == group))) return true;
            return false;
        }
    }
}
