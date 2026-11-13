using System.Collections.Generic;
using Morgott.UI.Responsive;
using Morgott.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace Morgott.UI.PlayerHud
{
    // Strip keep-out for the bottom-anchored HUD elements (bug 2026-10-23: the build piece info strip "SelectedInfo" with
    // its requirement row sat under the strip). Vanilla anchors Hud.m_buildHud/SelectedInfo bottom-centre at a fixed
    // offset made for the vanilla HUD (hotbar top-left, nothing at the bottom), and the RuneSkin strip, added last under
    // Hud.m_rootObject, drew over it. Here each element keeps its home position and rises by StripClearMath.Lift (drawn
    // bottom vs the strip's live top + size.hud.strip.clear.gap), and the strip moves below them in sibling order.
    // Measured on the element's drawn graphics (the panel stack reaches past its rect), re-measured when the strip
    // re-lays out, when an element shows and every size.hud.strip.clear.poll s while shown (its content may change
    // size). Another writer moving an element (vanilla / a skin) re-homes it.
    internal sealed class StripClear
    {
        private sealed class Entry
        {
            public string Name;
            public RectTransform Rt;
            public Vector2 Home, Set;
            public bool WasActive, Pending, Boxed;
            public float LiftPx, Top, Left, Right; // drawn box (screen px) at the applied lift, Boxed = measured once
            public bool StacksOnHints;             // build piece info / author: also clear of the key hint row
        }

        private readonly Dictionary<RectTransform, Entry> _entries = new Dictionary<RectTransform, Entry>(16);
        private readonly List<RectTransform> _targets = new List<RectTransform>(16);
        private readonly List<string> _names = new List<string>(16);
        private readonly List<Graphic> _graphics = new List<Graphic>(64);
        private readonly List<int> _indices = new List<int>(16);
        private readonly Vector3[] _corners = new Vector3[4];
        private float _stripTop, _stripLeft, _stripRight, _gap, _nextPoll;
        private Entry _hints;
        private bool _all, _statusMoved;
        // Status groups above the strip (HudCluster.Status, screen px; empty = no row): the key hints clear them too
        // (bug 2026-11-13: lifted only to the strip top, the hint row ran through the debuff group's "Cold" cell).
        private Rect _statusA, _statusB;

        // Strip geometry in screen px (bottom-up): top edge, horizontal extent and the keep-out gap. Every element
        // re-measures.
        public void SetStrip(float top, float left, float right, float gapPx)
        {
            _gap = gapPx;
            _stripTop = top;
            _stripLeft = left;
            _stripRight = right;
            _all = true;
        }

        // Status group boxes, every frame; a change >= 0.5 px re-measures the key hints.
        public void SetStatus(Rect a, Rect b)
        {
            if (Near(a, _statusA) && Near(b, _statusB)) return;
            _statusA = a;
            _statusB = b;
            _statusMoved = true;
        }

        private static bool Near(Rect a, Rect b) =>
            Mathf.Abs(a.xMin - b.xMin) < 0.5f && Mathf.Abs(a.xMax - b.xMax) < 0.5f && Mathf.Abs(a.yMin - b.yMin) < 0.5f && Mathf.Abs(a.yMax - b.yMax) < 0.5f;

        // The element list (Hud fields and RuneSkin's own bottom widgets); resolved every tick, cheap field reads.
        private void Collect(Hud hud)
        {
            _targets.Clear();
            _names.Clear();
            // Key hints first: the build piece info stacks on their drawn top (both rise above the strip, run 20261108-051942:
            // the build hint row y 145..198 px ran through the requirement cells y 169..249 px).
            var hints = KeyHints.instance;
            if (hints) Add(hints.transform, "key hints");
            if (hud.m_buildHud)
            {
                Add(hud.m_buildHud.transform.Find("SelectedInfo"), "build piece info");
                if (hud.m_hoveredPieceAuthorWindow) Add(hud.m_hoveredPieceAuthorWindow.transform, "piece author");
            }
            if (hud.m_actionBarRoot) Add(hud.m_actionBarRoot.transform, "action bar");
            if (hud.m_shipHudRoot) Add(hud.m_shipHudRoot.transform.Find("RuneSkin_ShipLadder"), "ship ladder");
            if (hud.m_mountPanel) Add(hud.m_mountPanel.transform, "mount panel");
            if (hud.m_eventBar) Add(hud.m_eventBar.transform, "event bar");
            if (WorldHud.HoldPromptSkin.Instance) Add(WorldHud.HoldPromptSkin.Instance.Root, "hold prompt");
            var msg = MessageHud.instance;
            if (msg && msg.m_messageCenterText) Add(msg.m_messageCenterText.rectTransform, "centre message");
        }

        private void Add(Transform t, string name)
        {
            if (!(t is RectTransform rt)) return;
            _targets.Add(rt);
            _names.Add(name);
        }

        public void Tick(Hud hud, RectTransform strip)
        {
            if (!hud || !(_stripTop > 0f)) return;
            var rootCanvas = strip ? strip.GetComponentInParent<Canvas>() : null;
            rootCanvas = rootCanvas ? rootCanvas.rootCanvas : null;
            if (rootCanvas && Mathf.Abs(rootCanvas.scaleFactor - ResponsiveContext.GuiFactor) > 0.001f) return; // CanvasScaler not caught up
            Collect(hud);
            var poll = Time.unscaledTime >= _nextPoll;
            var hintsTop = _hints != null ? _hints.Top : 0f;
            var hintsMoved = false;
            if (poll) _nextPoll = Time.unscaledTime + ThemeTokens.GetFloat("size.hud.strip.clear.poll", 0.5f);

            for (var i = 0; i < _targets.Count; i++)
            {
                var rt = _targets[i];
                if (!_entries.TryGetValue(rt, out var e))
                {
                    e = new Entry
                    {
                        Name = _names[i],
                        Rt = rt,
                        Home = rt.anchoredPosition,
                        Set = rt.anchoredPosition,
                        Pending = true,
                        StacksOnHints = _names[i] == "build piece info" || _names[i] == "piece author"
                    };
                    _entries[rt] = e;
                }
                var active = rt.gameObject.activeInHierarchy;
                if (active && !e.WasActive) e.Pending = true;
                e.WasActive = active;
                if (!active) continue;
                if (rt.anchoredPosition != e.Set)
                {
                    // moved by its owner: that is the new home
                    e.Home = rt.anchoredPosition;
                    e.Set = e.Home;
                    e.LiftPx = 0f;
                    e.Pending = true;
                }
                var under = e.StacksOnHints && _hints != null && _hints.WasActive && _hints.Boxed ? _hints : null;
                var isHints = _names[i] == "key hints";
                if (e.Pending || _all || poll || (under != null && hintsMoved) || (isHints && _statusMoved)) Measure(e, _gap, under, isHints);
                if (isHints)
                {
                    _hints = e;
                    hintsMoved = e.Boxed && Mathf.Abs(e.Top - hintsTop) >= 0.5f;
                }
            }
            _all = false;
            _statusMoved = false;
            Order(strip);
        }


        private void Measure(Entry e, float gap, Entry under = null, bool clearStatus = false)
        {
            var rt = e.Rt;
            var canvas = rt.GetComponentInParent<Canvas>();
            if (!canvas || !rt.parent) return;
            var cam = canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.rootCanvas.worldCamera;
            if (!Drawn(rt, cam, out var bottom, out var top, out var left, out var right)) return; // laid out next frame: still pending
            e.Pending = false;
            var home = StripClearMath.BottomAtHome(bottom, e.LiftPx);
            var want = StripClearMath.Lift(home, left, right, _stripTop, _stripLeft, _stripRight, gap);
            if (under != null) want = Mathf.Max(want, StripClearMath.Lift(home, left, right, under.Top, under.Left, under.Right, gap));
            if (clearStatus)
                foreach (var s in new[] { _statusA, _statusB })
                    if (s.height >= 1f) want = Mathf.Max(want, StripClearMath.Lift(home, left, right, s.yMax, s.xMin, s.xMax, gap));
            e.Top = StripClearMath.BottomAtHome(top, e.LiftPx) + want;
            e.Left = left;
            e.Right = right;
            e.Boxed = true;
            if (Mathf.Abs(want - e.LiftPx) < 0.5f) return;
            // screen px -> the element's anchored units (its parent's scale)
            var origin = rt.parent.position;
            var p0 = RectTransformUtility.WorldToScreenPoint(cam, origin).y;
            var up = RectTransformUtility.WorldToScreenPoint(cam, origin + rt.parent.TransformVector(Vector3.up)).y - p0;
            if (!(up > 0f)) return;
            e.Set = e.Home + new Vector2(0f, StripClearMath.Units(want, up));
            rt.anchoredPosition = e.Set;
            if (Plugin.Verbose) Plugin.Log.LogInfo($"[Morgott.UI] strip clear: {e.Name} lift {e.LiftPx:0} -> {want:0} px (bottom at home {bottom - e.LiftPx:0} px, strip top {_stripTop:0} px + gap {gap:0})");
            e.LiftPx = want;
        }

        // Drawn box (screen px) of the visible graphics under rt: enabled, sized, not faded out by a CanvasGroup.
        private bool Drawn(RectTransform rt, Camera cam, out float bottom, out float top, out float left, out float right)
        {
            bottom = float.MaxValue;
            top = float.MinValue;
            left = float.MaxValue;
            right = float.MinValue;
            rt.GetComponentsInChildren(false, _graphics);
            foreach (var g in _graphics)
            {
                if (!g.enabled || g.canvasRenderer.cull || g.color.a <= 0.01f || g.canvasRenderer.GetInheritedAlpha() <= 0.01f) continue;
                var r = g.rectTransform.rect;
                if (r.width < 1f || r.height < 1f) continue;
                g.rectTransform.GetWorldCorners(_corners);
                for (var k = 0; k < 4; k++)
                {
                    var s = RectTransformUtility.WorldToScreenPoint(cam, _corners[k]);
                    if (s.y < bottom) bottom = s.y;
                    if (s.y > top) top = s.y;
                    if (s.x < left) left = s.x;
                    if (s.x > right) right = s.x;
                }
            }
            _graphics.Clear(); // scratch: pins no Graphic past this measure
            return bottom != float.MaxValue;
        }

        // The strip draws below every tracked element that shares its parent chain (Hud.m_rootObject children).
        private void Order(RectTransform strip)
        {
            var parent = strip ? strip.parent : null;
            if (!parent) return;
            _indices.Clear();
            foreach (var rt in _targets)
            {
                Transform t = rt;
                while (t && t.parent != parent) t = t.parent;
                if (t && t != strip) _indices.Add(t.GetSiblingIndex());
            }
            var to = StripClearMath.SiblingBelow(strip.GetSiblingIndex(), _indices);
            if (to < 0) return;
            strip.SetSiblingIndex(to);
            if (Plugin.Verbose) Plugin.Log.LogInfo($"[Morgott.UI] strip clear: strip sibling -> {to} (below {_indices.Count} bottom HUD element(s))");
        }

        // Harness read side: name -> applied lift (screen px).
        public IEnumerable<KeyValuePair<string, float>> Lifts()
        {
            foreach (var e in _entries.Values)
                if (e.Rt) yield return new KeyValuePair<string, float>(e.Name, e.LiftPx);
        }
    }
}
