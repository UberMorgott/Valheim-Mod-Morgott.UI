using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Morgott.UI.Restyle
{
    // UI drawn before an opaque window and lying fully inside its opaque area is never seen: it is gated (RenderGate,
    // owner "cover") while the window shows. Candidates are the subtrees drawn before the window: its earlier siblings at
    // every level up to the root canvas, and other root canvases with a lower sorting order. A candidate whose drawn
    // bounds lie inside the opaque rect is gated whole; one that only overlaps it is searched a few levels down. A
    // subtree holding a canvas that sorts above the window (e.g. the strip's raycast layer StripHits over an open inventory)
    // is never gated whole, only its other children. Recomputed while the window shows (HUD parts move,
    // appear); only differences are applied.
    public static class CoverCull
    {
        public const string Owner = "cover";
        // Levels searched below a partly covered candidate. 6: the small minimap's player marker sits at HUD (0) / hudroot /
        // MiniMap / small / RuneSkin_MapMask / map / player_marker (6) - with 4 it stayed drawn fully under the crafting
        // panel (run 20261027-184426 overdraw covered=1) while the minimap disc only partly overlaps it.
        private const int MaxDepth = 6;
        private const float Tol = 0.5f;

        private struct SubtreeBounds
        {
            public bool Any;
            public bool HasOverride;
            public float X0, Y0, X1, Y1;
            public int MaxOverrideOrder;
        }

        private static readonly HashSet<GameObject> Covered = new HashSet<GameObject>();
        private static readonly HashSet<GameObject> Next = new HashSet<GameObject>();
        private static readonly List<GameObject> Drop = new List<GameObject>();
        private static readonly List<Canvas> RootCanvases = new List<Canvas>(16);
        private static readonly Dictionary<Transform, SubtreeBounds> BoundsCache = new Dictionary<Transform, SubtreeBounds>(128);
        private static readonly Vector3[] Corners = new Vector3[4];

        public static int CoveredCount => Covered.Count;

        // windows[i] is drawn over opaque[i] (screen px).
        public static void Refresh(IReadOnlyList<Transform> windows, IReadOnlyList<Rect> opaque)
        {
            Next.Clear();
            var hasValid = false;
            for (var i = 0; i < windows.Count; i++)
                if (windows[i] && opaque[i].width > 1f && opaque[i].height > 1f) { hasValid = true; break; }
            if (hasValid)
            {
                RootCanvases.Clear();
                BoundsCache.Clear();
                try
                {
                    var all = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
                    for (var i = 0; i < all.Length; i++)
                    {
                        var c = all[i];
                        if (c && c.isRootCanvas && c.isActiveAndEnabled && c.renderMode != RenderMode.WorldSpace)
                            RootCanvases.Add(c);
                    }
                    for (var i = 0; i < windows.Count; i++)
                    {
                        var w = windows[i];
                        var r = opaque[i];
                        if (!w || r.width <= 1f || r.height <= 1f) continue;
                        Collect(w, r);
                    }
                }
                finally
                {
                    RootCanvases.Clear();
                    BoundsCache.Clear();
                }
            }
            Drop.Clear();
            foreach (var go in Covered)
                if (!go || !Next.Contains(go)) Drop.Add(go);
            foreach (var go in Drop)
            {
                Covered.Remove(go);
                if (go) RenderGate.Set(go, Owner, false);
            }
            foreach (var go in Next)
                if (Covered.Add(go)) RenderGate.Set(go, Owner, true);
            Next.Clear();
        }

        public static void Clear()
        {
            foreach (var go in Covered)
                if (go) RenderGate.Set(go, Owner, false);
            Covered.Clear();
        }

        private static void Collect(Transform window, Rect opaque)
        {
            var sortCanvas = SortCanvas(window);
            if (!sortCanvas) return;
            var order = sortCanvas.sortingOrder;
            var root = sortCanvas.rootCanvas ? sortCanvas.rootCanvas.transform : null;
            // Earlier siblings at every level, up to the canvas that sorts the window.
            for (var node = window; node && node.parent && node != sortCanvas.transform; node = node.parent)
            {
                var parent = node.parent;
                var index = node.GetSiblingIndex();
                for (var i = 0; i < index; i++) Consider(parent.GetChild(i), opaque, order, 0);
                if (parent == root) break;
            }
            // A window under its own override-sorting canvas above its root (the large map, LargeMap.cs:68-70) is also
            // drawn after every other branch of that root canvas, earlier or later siblings alike (Codex: the crosshair,
            // EnemyHud and DamageText under IngameGui stayed drawn under the map backing).
            if (root && sortCanvas.transform != root && sortCanvas.rootCanvas.sortingOrder < order)
                for (var node = sortCanvas.transform; node && node != root && node.parent; node = node.parent)
                {
                    var parent = node.parent;
                    for (var i = 0; i < parent.childCount; i++)
                    {
                        var sib = parent.GetChild(i);
                        if (sib != node) Consider(sib, opaque, order, 0, true);
                    }
                }
            // Root canvases drawn earlier (lower sorting order), hoisted once per Refresh.
            for (var i = 0; i < RootCanvases.Count; i++)
            {
                var c = RootCanvases[i];
                if (!c || c.sortingOrder >= order || c.transform == root) continue;
                var ct = c.transform;
                for (var j = 0; j < ct.childCount; j++) Consider(ct.GetChild(j), opaque, order, 0);
            }
        }

        // tieAbove: an equal sorting order counts as drawn after the window (a later sibling branch: order not proven).
        private static void Consider(Transform t, Rect opaque, int windowOrder, int depth, bool tieAbove = false)
        {
            if (!t || !t.gameObject.activeInHierarchy || !(t is RectTransform)) return;
            // Sorted above the window by its own (or an ancestor's) override-sorting canvas: drawn after it.
            if (depth == 0)
            {
                var sort = SortCanvas(t);
                if (sort && sort.overrideSorting && (sort.sortingOrder > windowOrder || tieAbove && sort.sortingOrder == windowOrder)) return;
            }
            else if (t.TryGetComponent<Canvas>(out var own) && own.enabled && own.overrideSorting &&
                (own.sortingOrder > windowOrder || tieAbove && own.sortingOrder == windowOrder)) return;
            var b = GetBounds(t);
            if (!b.Any) return;
            if (!RenderGateMath.Overlaps(opaque.xMin, opaque.yMin, opaque.xMax, opaque.yMax, b.X0, b.Y0, b.X1, b.Y1)) return;
            var aboveWindow = b.HasOverride && (b.MaxOverrideOrder > windowOrder || tieAbove && b.MaxOverrideOrder == windowOrder);
            if (!aboveWindow && RenderGateMath.Inside(opaque.xMin, opaque.yMin, opaque.xMax, opaque.yMax, b.X0, b.Y0, b.X1, b.Y1, Tol))
            {
                Next.Add(t.gameObject);
                return;
            }
            if (depth >= MaxDepth) return;
            for (var i = 0; i < t.childCount; i++) Consider(t.GetChild(i), opaque, windowOrder, depth + 1, tieAbove);
        }

        // Screen bounds + highest override-sorting canvas of the active subtree, memoized once per Transform per Refresh.
        private static SubtreeBounds GetBounds(Transform t)
        {
            if (BoundsCache.TryGetValue(t, out var cached)) return cached;
            var res = default(SubtreeBounds);
            if (t.TryGetComponent<Canvas>(out var c) && c.overrideSorting && c.enabled)
            {
                res.HasOverride = true;
                res.MaxOverrideOrder = c.sortingOrder;
            }
            if (t.TryGetComponent<Graphic>(out var g) && g.enabled && g.canvas)
            {
                var rt = g.rectTransform;
                if (rt.rect.width > 0f && rt.rect.height > 0f)
                {
                    rt.GetWorldCorners(Corners);
                    var cam = g.canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : g.canvas.rootCanvas.worldCamera;
                    for (var k = 0; k < 4; k++)
                    {
                        var p = RectTransformUtility.WorldToScreenPoint(cam, Corners[k]);
                        if (!res.Any) { res.X0 = res.X1 = p.x; res.Y0 = res.Y1 = p.y; res.Any = true; continue; }
                        if (p.x < res.X0) res.X0 = p.x;
                        if (p.x > res.X1) res.X1 = p.x;
                        if (p.y < res.Y0) res.Y0 = p.y;
                        if (p.y > res.Y1) res.Y1 = p.y;
                    }
                }
            }
            var count = t.childCount;
            for (var i = 0; i < count; i++)
            {
                var child = t.GetChild(i);
                if (!child.gameObject.activeSelf) continue;
                var cb = GetBounds(child);
                if (cb.HasOverride && (!res.HasOverride || cb.MaxOverrideOrder > res.MaxOverrideOrder))
                {
                    res.HasOverride = true;
                    res.MaxOverrideOrder = cb.MaxOverrideOrder;
                }
                if (cb.Any)
                {
                    if (!res.Any) { res.X0 = cb.X0; res.Y0 = cb.Y0; res.X1 = cb.X1; res.Y1 = cb.Y1; res.Any = true; }
                    else
                    {
                        if (cb.X0 < res.X0) res.X0 = cb.X0;
                        if (cb.X1 > res.X1) res.X1 = cb.X1;
                        if (cb.Y0 < res.Y0) res.Y0 = cb.Y0;
                        if (cb.Y1 > res.Y1) res.Y1 = cb.Y1;
                    }
                }
            }
            BoundsCache[t] = res;
            return res;
        }

        // The canvas whose sorting order places the window: nearest override-sorting canvas, else the root.
        private static Canvas SortCanvas(Transform t)
        {
            Canvas last = null;
            for (; t; t = t.parent)
            {
                if (!t.TryGetComponent<Canvas>(out var c) || !c.enabled) continue;
                last = c;
                if (c.overrideSorting || c.isRootCanvas) return c;
            }
            return last;
        }
    }
}
