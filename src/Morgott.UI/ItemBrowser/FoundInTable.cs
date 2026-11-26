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
        /// <summary>Source prefabs the prefab places (pickables, chests, rocks, traders...); null = scanned before this
        /// column existed (its objects stay unknown; the prefab is not loaded for them).</summary>
        public List<string> Objects;
        /// <summary>Boss altars: boss prefab + the item offered + how many (OfferingBowl.m_bossItems, item stands: their count;
        /// 0 = unknown, a table dumped before the count existed).</summary>
        public readonly List<(string Boss, string Item, int Count)> Summons = new List<(string, string, int)>();

        public void AddObject(string prefab)
        {
            Objects ??= new List<string>();
            if (!string.IsNullOrEmpty(prefab) && !Objects.Contains(prefab)) Objects.Add(prefab);
        }

        public void Add(string creature, Origin kind)
        {
            if (!string.IsNullOrEmpty(creature) && !Creatures.Contains((creature, kind))) Creatures.Add((creature, kind));
        }
    }

    /// <summary>
    /// Text form of a <see cref="Scanned"/> set: the embedded vanilla table (FoundInTable.txt, dumped by the autotest
    /// check item-browser-found-in from a forced full scan) and the lazy-scan cache under BepInEx\cache. Line 1
    /// "# game &lt;version&gt;\t&lt;count&gt;", then "&lt;key&gt;\t&lt;themes&gt;\t&lt;interior 0|1&gt;\t&lt;creature&gt;:&lt;w|n|b&gt;,...\t&lt;object&gt;|&lt;boss&gt;&lt;&lt;item&gt;[*&lt;count&gt;],...".
    /// Keys: "L:&lt;location prefab&gt;", "R:&lt;room prefab&gt;" (cache keys add "@&lt;owner&gt;"). No Unity calls: unit-tested.
    /// </summary>
    public static class FoundInTable
    {
        private static char Code(Origin k) => k == Origin.Nest ? 'n' : k == Origin.Boss ? 'b' : 'w';
        private static Origin Kind(char c) => c == 'n' ? Origin.Nest : c == 'b' ? Origin.Boss : Origin.World;

        /// <summary>Collapses CR/LF/tab/space runs to single spaces and trims, so multiline game versions stay one TSV token.</summary>
        public static string SanitizeGameVersion(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var sb = new StringBuilder(raw.Length);
            var space = false;
            foreach (var c in raw)
            {
                if (c == '\r' || c == '\n' || c == '\t' || c == ' ')
                {
                    if (sb.Length > 0) space = true;
                }
                else
                {
                    if (space) { sb.Append(' '); space = false; }
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        public static string Write(string version, IEnumerable<KeyValuePair<string, Scanned>> entries)
        {
            var list = new List<KeyValuePair<string, Scanned>>(entries);
            list.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            var sb = new StringBuilder();
            sb.Append("# game ").Append(SanitizeGameVersion(version)).Append('\t').Append(list.Count).Append('\n');
            foreach (var kv in list)
            {
                var s = kv.Value;
                sb.Append(SanitizeGameVersion(kv.Key)).Append('\t').Append(s.Themes).Append('\t').Append(s.Interior ? '1' : '0').Append('\t');
                for (var i = 0; i < s.Creatures.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(s.Creatures[i].Creature).Append(':').Append(Code(s.Creatures[i].Kind));
                }
                if (s.Objects != null)
                {
                    sb.Append('\t').Append(string.Join(",", s.Objects));
                    foreach (var (boss, item, n) in s.Summons) sb.Append(sb[sb.Length - 1] == '\t' ? "" : ",").Append(boss).Append('<').Append(item).Append(n > 0 ? "*" + n : "");
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
                    version = SanitizeGameVersion(head[0]);
                    if (head.Length > 1 && int.TryParse(head[1], out var n)) count = n;
                    continue;
                }
                var f = line.Split('\t');
                if (f.Length < 4 || f.Length > 5 || f[0].Length == 0 || !int.TryParse(f[1], out var themes)) continue;
                var s = new Scanned { Themes = themes, Interior = f[2] == "1" };
                foreach (var c in f[3].Split(','))
                {
                    var at = c.LastIndexOf(':');
                    if (at > 0 && at == c.Length - 2) s.Add(c.Substring(0, at), Kind(c[at + 1]));
                }
                if (f.Length == 5)
                {
                    s.Objects = new List<string>();
                    foreach (var o in f[4].Split(','))
                    {
                        var lt = o.IndexOf('<');
                        if (lt > 0) s.Summons.Add(Summon(o.Substring(0, lt), o.Substring(lt + 1)));
                        else s.AddObject(o);
                    }
                }
                map[f[0]] = s;
            }
            return map;
        }

        // "item*3" -> (item, 3); "item" (older dump) -> count 0 = unknown; a multi-group spec "a|b*2+c*1" stays whole (count 0).
        private static (string, string, int) Summon(string boss, string item)
        {
            var star = item.LastIndexOf('*');
            return item.IndexOf('+') < 0 && star > 0 && int.TryParse(item.Substring(star + 1), out var n) ? (boss, item.Substring(0, star), n) : (boss, item, 0);
        }

        /// <summary>Stand group of a stand that takes any attachable item (ItemStand with no m_supportedItems): needed, nothing listed.</summary>
        public const string AnyItem = "*";

        /// <summary>One altar's stand groups (items '|'-joined alternatives, count = stands) as a summon item + count: one group =
        /// (items, count); several = ("a|b*2+c*1", 0); none = ("", 0).</summary>
        public static (string Item, int Count) Spec(List<(string Item, int Count)> groups)
        {
            if (groups.Count == 0) return ("", 0);
            if (groups.Count == 1) return groups[0];
            return (string.Join("+", groups.ConvertAll(g => g.Item + "*" + g.Count)), 0);
        }

        /// <summary>Inverse of <see cref="Spec"/>.</summary>
        public static List<(string Item, int Count)> Groups(string item, int count)
        {
            var list = new List<(string, int)>();
            if (item.IndexOf('+') < 0) { list.Add((item, count)); return list; }
            foreach (var part in item.Split('+'))
            {
                var star = part.LastIndexOf('*');
                list.Add(star > 0 && int.TryParse(part.Substring(star + 1), out var n) ? (part.Substring(0, star), n) : (part, 0));
            }
            return list;
        }
    }
}
