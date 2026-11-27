using System;
using System.Collections.Generic;
using Morgott.UI.InventoryScreen;

namespace Morgott.UI.InfoScreens
{
    // Pure geometry / ordering of the info panels (skills-a / trophies-a), unit-tested on net8 (no Unity types).
    public static class InfoMath
    {
        // Tab row: n tabs `aspect` times wider than the row is tall, never past the row width, centred with `gap`.
        public static Box[] TabRow(Box row, int n, float aspect, float gap)
        {
            var cells = new Box[Math.Max(0, n)];
            if (n <= 0 || !(row.W > 0f) || !(row.H > 0f)) return cells;
            var w = Math.Min(row.H * aspect, (row.W - (n - 1) * gap) / n);
            if (w <= 0f) return cells;
            var x = row.X + (row.W - (n * w + (n - 1) * gap)) / 2f;
            for (var i = 0; i < n; i++) cells[i] = new Box(x + i * (w + gap), row.Y, w, row.H);
            return cells;
        }

        // One info tab strip on screen: the inventory header row (m_info buttons) shows only while no docked info panel
        // (skills / trophies) draws its own InfoTabs mirror of the same buttons. Texts / stats hide the whole inventory
        // (FullWindow); achievements and mod panels (Almanac) have no mirror, so the header row stays for them.
        public static bool HeaderTabsShown(bool skillsOpen, bool trophiesOpen) => !skillsOpen && !trophiesOpen;

        private sealed class LevelComparer : IComparer<int>
        {
            private readonly IReadOnlyList<float> _levels;
            private readonly IReadOnlyList<string> _names;
            private readonly StringComparer _comparer;

            public LevelComparer(IReadOnlyList<float> levels, IReadOnlyList<string> names, StringComparer comparer)
            {
                _levels = levels;
                _names = names;
                _comparer = comparer;
            }

            public int Compare(int a, int b)
            {
                var c = _levels[b].CompareTo(_levels[a]);
                if (c == 0) c = _comparer.Compare(_names[a], _names[b]);
                return c != 0 ? c : a.CompareTo(b);
            }
        }

        private sealed class NameComparer : IComparer<int>
        {
            private readonly IReadOnlyList<string> _names;
            private readonly StringComparer _comparer;

            public NameComparer(IReadOnlyList<string> names, StringComparer comparer)
            {
                _names = names;
                _comparer = comparer;
            }

            public int Compare(int a, int b)
            {
                var c = _comparer.Compare(_names[a], _names[b]);
                return c != 0 ? c : a.CompareTo(b);
            }
        }

        // Skill row order: mode 0 = as given, 1 = level high first (ties by name), 2 = name. Returns source indices.
        public static int[] SkillOrder(IReadOnlyList<float> levels, IReadOnlyList<string> names, int mode, StringComparer comparer)
        {
            var n = Math.Min(levels.Count, names.Count);
            var order = new int[n];
            for (var i = 0; i < n; i++) order[i] = i;
            if (mode == 1)
                Array.Sort(order, new LevelComparer(levels, names, comparer));
            else if (mode == 2)
                Array.Sort(order, new NameComparer(names, comparer));
            return order;
        }
    }
}
