using System;
using System.Collections.Generic;
using System.Text;

namespace Morgott.UI.ItemBrowser
{
    /// <summary>What one location / dungeon room prefab places. <see cref="Origin.World"/> = a plain spawner point or a
    /// creature standing in the prefab (the caller maps it to Location / Dungeon); Nest / Boss as found.</summary>
    public sealed class Scanned
    {
        public readonly List<(string Creature, Origin Kind)> Creatures = new List<(string, Origin)>();
        /// <summary>Room.Theme flags of the prefab's DungeonGenerators (locations only).</summary>
        public int Themes;
        /// <summary>The location's Location.m_hasInterior.</summary>
        public bool Interior;

        public void Add(string creature, Origin kind)
        {
            if (!string.IsNullOrEmpty(creature) && !Creatures.Contains((creature, kind))) Creatures.Add((creature, kind));
        }
    }

    /// <summary>
    /// Text form of a <see cref="Scanned"/> set: the embedded vanilla table (FoundInTable.txt, dumped by the autotest
    /// check item-browser-found-in from a forced full scan) and the lazy-scan cache under BepInEx\cache. Line 1
    /// "# game &lt;version&gt;\t&lt;count&gt;", then "&lt;key&gt;\t&lt;themes&gt;\t&lt;interior 0|1&gt;\t&lt;creature&gt;:&lt;w|n|b&gt;,...".
    /// Keys: "L:&lt;location prefab&gt;", "R:&lt;room prefab&gt;" (cache keys add "@&lt;owner&gt;"). No Unity calls: unit-tested.
    /// </summary>
    public static class FoundInTable
    {
        private static char Code(Origin k) => k == Origin.Nest ? 'n' : k == Origin.Boss ? 'b' : 'w';
        private static Origin Kind(char c) => c == 'n' ? Origin.Nest : c == 'b' ? Origin.Boss : Origin.World;

        public static string Write(string version, IEnumerable<KeyValuePair<string, Scanned>> entries)
        {
            var list = new List<KeyValuePair<string, Scanned>>(entries);
            list.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            var sb = new StringBuilder();
            sb.Append("# game ").Append(version).Append('\t').Append(list.Count).Append('\n');
            foreach (var kv in list)
            {
                var s = kv.Value;
                sb.Append(kv.Key).Append('\t').Append(s.Themes).Append('\t').Append(s.Interior ? '1' : '0').Append('\t');
                for (var i = 0; i < s.Creatures.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(s.Creatures[i].Creature).Append(':').Append(Code(s.Creatures[i].Kind));
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <returns>key -> scan; <paramref name="version"/> = the header's game version (null without header);
        /// <paramref name="count"/> = the header's entry count (-1 without header). Malformed lines are skipped.</returns>
        public static Dictionary<string, Scanned> Parse(string text, out string version, out int count)
        {
            version = null;
            count = -1;
            var map = new Dictionary<string, Scanned>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(text)) return map;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.StartsWith("# game ", StringComparison.Ordinal))
                {
                    var head = line.Substring(7).Split('\t');
                    version = head[0];
                    if (head.Length > 1 && int.TryParse(head[1], out var n)) count = n;
                    continue;
                }
                var f = line.Split('\t');
                if (f.Length != 4 || f[0].Length == 0 || !int.TryParse(f[1], out var themes)) continue;
                var s = new Scanned { Themes = themes, Interior = f[2] == "1" };
                foreach (var c in f[3].Split(','))
                {
                    var at = c.LastIndexOf(':');
                    if (at > 0 && at == c.Length - 2) s.Add(c.Substring(0, at), Kind(c[at + 1]));
                }
                map[f[0]] = s;
            }
            return map;
        }
    }
}
