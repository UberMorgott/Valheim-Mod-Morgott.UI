using System.Collections.Generic;
using Morgott.UI.InventoryScreen;
using UnityEngine;

namespace Morgott.UI.ModScreens
{
    // A mod window's own widgets re-placed on a full-screen page (ElPage, TraderPage) without touching their wiring: each
    // part's canvas box is captured once in the mod's prefab layout; the page then puts it at a new top-left with one
    // uniform scale k per group (the part keeps its own rect size, its localScale absorbs k and the parent's scale), or
    // stretched into a box. Rows / lists inside a part keep the mod's own anchoring.
    public sealed class PagePart
    {
        public RectTransform Rt;
        public Box Box0;   // canvas box in the prefab layout
        public float Unit; // canvas units per local unit then

        public static PagePart Of(RectTransform canvas, RectTransform rt)
        {
            if (!canvas || !rt) return null;
            var b = Geo.BoxOf(canvas, rt);
            if (b.W <= 1f || b.H <= 1f || rt.rect.width <= 0f) return null;
            return new PagePart { Rt = rt, Box0 = b, Unit = b.W / rt.rect.width };
        }

        // Every RectTransform child of `parent` (active or not) as a part.
        public static List<PagePart> Children(RectTransform canvas, Transform parent)
        {
            var list = new List<PagePart>();
            if (!parent) return list;
            foreach (Transform c in parent)
                if (c is RectTransform rt && !c.name.StartsWith("RuneSkin", System.StringComparison.Ordinal) && Of(canvas, rt) is PagePart p) list.Add(p);
            return list;
        }

        // Scaled by k, top-left at (x, y).
        public void Put(RectTransform canvas, float k, float x, float y) => Put(canvas, k, new Box(x, y, Box0.W * k, Box0.H * k));

        // Scaled by k, stretched into box (its local size = box / scale: stretched children of the part follow).
        public void Put(RectTransform canvas, float k, Box box)
        {
            if (!Rt || !(Rt.parent is RectTransform parent) || k <= 0f) return;
            var pu = Mathf.Abs(parent.lossyScale.x / Mathf.Max(1e-6f, canvas.lossyScale.x));
            Geo.SetScale(Rt, Unit * k / Mathf.Max(1e-6f, pu));
            Geo.Place(canvas, Rt, box);
        }

        // A group mapped into area at one fitting scale (ElPageMath.Fit over the group's bounding box).
        public static void Fit(RectTransform canvas, List<PagePart> parts, Box area)
        {
            if (parts == null || parts.Count == 0 || area.IsEmpty) return;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var p in parts)
            {
                x0 = Mathf.Min(x0, p.Box0.X); y0 = Mathf.Min(y0, p.Box0.Y);
                x1 = Mathf.Max(x1, p.Box0.Right); y1 = Mathf.Max(y1, p.Box0.Bottom);
            }
            var k = ElPageMath.Fit(x1 - x0, y1 - y0, area, out var ax, out var ay);
            foreach (var p in parts) p.Put(canvas, k, ax + (p.Box0.X - x0) * k, ay + (p.Box0.Y - y0) * k);
        }
    }
}
