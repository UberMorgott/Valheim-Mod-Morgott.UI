using System;

namespace Morgott.UI.Restyle
{
    // Geometry of the thin chamfered outline (OutlineFrame; no Unity types: unit-tested).
    public static class OutlineMath
    {
        // tan(22.5°): how far a 45° stroke runs past each centre-line joint per unit of thickness, half at either end.
        private const float MiterShare = 0.41421356f;

        // Diagonal corner stroke joining the two edge strokes that stop `chamfer` from the corner (edges `inset` in from the
        // rect, `thick` wide): its centre's distance from the corner along both axes, and its length with mitred ends.
        public static void Corner(float chamfer, float inset, float thick, out float centre, out float length)
        {
            var line = inset + thick / 2f;
            centre = (line + chamfer) / 2f;
            length = (chamfer - line) * (float)Math.Sqrt(2d) + thick * MiterShare;
        }
    }
}
