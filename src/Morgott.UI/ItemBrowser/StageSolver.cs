using System;
using System.Collections.Generic;

namespace Morgott.UI.ItemBrowser
{
    /// <summary>Where an entry's stage came from (docs\mockups\item-browser-progression\DESIGN.md section 3).</summary>
    public enum StageVia : byte { Unknown, Derived, Incidental, Authored, Magic }

    /// <summary>Solved stage of one entry. Stage = 2 * biome + afterBoss, -1 = none.</summary>
    public struct StageInfo
    {
        public int Stage;
        public StageVia Via;
        public bool Ocean;  // reached only over open water (Karve stage + visible mark)
        public string Kind; // winning path's kind, for the dump

        public int Biome => Stage < 0 ? -1 : Stage / 2;
        public bool AfterBoss => Stage >= 0 && (Stage & 1) == 1;
        public static readonly StageInfo None = new StageInfo { Stage = -1 };
    }

    /// <summary>Requirement of a path: the K-th cheapest of its members (K = 1: any one of them).</summary>
    public sealed class StageGroup
    {
        public readonly string[] Ids;
        public readonly int K;
        internal StageSolver.Node[] Nodes;

        public StageGroup(string[] ids, int k = 1)
        {
            Ids = ids;
            K = Math.Max(1, k);
        }
    }

    /// <summary>One way to obtain an entry: MAX of its base and every requirement.</summary>
    public sealed class StagePath
    {
        public string Kind, Source; // Source: an ocean mark is inherited from it (a drop of a sea creature)
        public int Base;
        public bool Incidental, Ocean, AfterBoss;
        public readonly List<StageGroup> Groups = new List<StageGroup>();

        public StagePath Need(string id)
        {
            Groups.Add(new StageGroup(new[] { id }));
            return this;
        }

        public StagePath NeedAny(string[] ids, int k = 1)
        {
            Groups.Add(new StageGroup(ids, k));
            return this;
        }
    }

    /// <summary>
    /// Least fixed point from "unknown": entry = MIN over paths, path = MAX over requirements, so a cycle never starts
    /// itself. Passes, each freezing what it resolved: real paths; incidental ones (only for entries with no other path);
    /// authored fallbacks, which describe their own item only and never feed a dependent. A rarity item (Magic) has no
    /// biome and gives none. No Unity calls: <see cref="Step"/> is time-sliced by the caller.
    /// </summary>
    public sealed class StageSolver
    {
        private const int Inf = int.MaxValue;

        internal sealed class Node
        {
            public string Id;
            public readonly List<StagePath> Paths = new List<StagePath>();
            public readonly List<Node> Deps = new List<Node>();
            public int Value = Inf, Authored = -1;
            public StagePath Win;
            public StageVia Via;
            public bool Frozen, Queued, HasPlain, Magic;
        }

        private readonly Dictionary<string, Node> _nodes = new Dictionary<string, Node>(StringComparer.Ordinal);
        private readonly Queue<Node> _queue = new Queue<Node>();
        private int _phase = -1;
        private int[] _scratch = new int[8];

        public bool Done => _phase >= 3;

        private Node N(string id)
        {
            if (!_nodes.TryGetValue(id, out var n)) _nodes[id] = n = new Node { Id = id };
            return n;
        }

        public void Add(string id, StagePath path) => N(id).Paths.Add(path);

        /// <summary>Authored biome 0..7 (the fallback; a derived value always wins).</summary>
        public void SetAuthored(string id, int biome) => N(id).Authored = biome * 2;

        /// <summary>Rarity item: always shown as Magic, no biome (user decision 4).</summary>
        public void SetMagic(string id) => N(id).Magic = true;

        private void Compile()
        {
            foreach (var n in new List<Node>(_nodes.Values))
                foreach (var p in n.Paths)
                {
                    if (!p.Incidental) n.HasPlain = true;
                    foreach (var g in p.Groups)
                    {
                        if (g.Nodes == null)
                        {
                            g.Nodes = new Node[g.Ids.Length];
                            for (var i = 0; i < g.Ids.Length; i++) g.Nodes[i] = N(g.Ids[i]);
                        }
                        foreach (var m in g.Nodes)
                            if (m.Deps.Count == 0 || m.Deps[m.Deps.Count - 1] != n) m.Deps.Add(n);
                    }
                }
        }

        private int Kth(StageGroup g)
        {
            if (g.K == 1)
            {
                var min = Inf;
                foreach (var m in g.Nodes) if (m.Value < min) min = m.Value;
                return min;
            }
            if (_scratch.Length < g.K) _scratch = new int[g.K];
            var have = 0;
            foreach (var m in g.Nodes)
            {
                if (m.Value == Inf || have == g.K && m.Value >= _scratch[g.K - 1]) continue;
                var at = have < g.K ? have++ : g.K - 1;
                _scratch[at] = m.Value;
                for (; at > 0 && _scratch[at - 1] > _scratch[at]; at--) (_scratch[at - 1], _scratch[at]) = (_scratch[at], _scratch[at - 1]);
            }
            return have < g.K ? Inf : _scratch[g.K - 1];
        }

        private int Eval(StagePath p)
        {
            var v = p.Base;
            foreach (var g in p.Groups)
            {
                var gv = Kth(g);
                if (gv == Inf) return Inf;
                if (gv > v) v = gv;
            }
            return p.AfterBoss ? v | 1 : v;
        }

        private bool Allowed(Node n, StagePath p) => !p.Incidental || _phase > 0 && !n.HasPlain;

        private void Push(Node n)
        {
            if (n.Frozen || n.Queued || n.Magic || n.Paths.Count == 0) return;
            n.Queued = true;
            _queue.Enqueue(n);
        }

        private void NextPhase()
        {
            var via = _phase == 0 ? StageVia.Derived : _phase == 1 ? StageVia.Incidental : StageVia.Authored;
            foreach (var n in _nodes.Values)
                if (_phase >= 0 && !n.Frozen && n.Value != Inf)
                {
                    n.Via = via;
                    n.Frozen = true;
                }
            _phase++;
            if (_phase >= 3) return;
            if (_phase == 2)
                foreach (var n in _nodes.Values)
                    if (!n.Frozen && n.Authored >= 0)
                    {
                        n.Value = n.Authored;
                        n.Via = StageVia.Authored;
                        n.Frozen = true;
                    }
            if (_phase < 2) foreach (var n in _nodes.Values) Push(n);
        }

        /// <summary>Processes up to <paramref name="budget"/> nodes; true when solved.</summary>
        public bool Step(int budget)
        {
            if (_phase < 0) Compile();
            while (budget-- > 0)
            {
                if (_phase >= 3) return true;
                if (_queue.Count == 0)
                {
                    NextPhase();
                    continue;
                }
                var n = _queue.Dequeue();
                n.Queued = false;
                if (n.Frozen) continue;
                var best = n.Value;
                StagePath win = null;
                foreach (var p in n.Paths)
                {
                    if (!Allowed(n, p)) continue;
                    var v = Eval(p);
                    if (v < best)
                    {
                        best = v;
                        win = p;
                    }
                }
                if (win == null) continue;
                n.Value = best;
                n.Win = win;
                foreach (var d in n.Deps) Push(d);
            }
            return _phase >= 3;
        }

        public void Solve()
        {
            while (!Step(4096)) { }
        }

        private static bool OceanOf(StageSolver s, Node n, int depth)
        {
            if (n.Win == null || depth > 32) return false;
            if (n.Win.Ocean) return true;
            return n.Win.Source != null && s._nodes.TryGetValue(n.Win.Source, out var src) && src != n && OceanOf(s, src, depth + 1);
        }

        public StageInfo Result(string id)
        {
            if (!_nodes.TryGetValue(id, out var n)) return StageInfo.None;
            if (n.Magic) return new StageInfo { Stage = -1, Via = StageVia.Magic };
            if (n.Value == Inf) return StageInfo.None;
            return new StageInfo { Stage = n.Value, Via = n.Via, Ocean = OceanOf(this, n, 0), Kind = n.Win?.Kind ?? "authored" };
        }

        /// <summary>Why an entry is unknown: the first real prefab in its chain nothing provides; null when it is known
        /// or only a cycle blocks it. Internal requirement nodes ("@...") are never named.</summary>
        public string Missing(string id)
        {
            if (!_nodes.TryGetValue(id, out var n)) return id.Length > 0 && id[0] == '@' ? null : id;
            return Missing(n, new HashSet<Node>());
        }

        private string Missing(Node n, HashSet<Node> seen)
        {
            if (n.Value != Inf || n.Magic || seen.Count > 512 || !seen.Add(n)) return null;
            var real = n.Id[0] != '@';
            if (n.Paths.Count == 0) return real ? n.Id : null;
            foreach (var p in n.Paths)
                foreach (var g in p.Groups)
                {
                    if (Kth(g) != Inf) continue;
                    // Members with a source first: any-of tool groups also list dev items nothing provides.
                    for (var sourced = 1; sourced >= 0; sourced--)
                        foreach (var m in g.Nodes)
                            if (m.Paths.Count > 0 == (sourced == 1) && Missing(m, seen) is string why) return why;
                }
            return null;
        }
    }
}
