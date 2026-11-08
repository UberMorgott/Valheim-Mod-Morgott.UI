using System;
using System.Collections.Generic;
using Morgott.Core;

namespace Morgott.UI.ItemBrowser
{
    // Grid tabs (docs\item-browser-design.md section 2); Other = sources that are no item / piece / creature (rocks,
    // pickables, loot tables), shown under All only.
    public enum EntryType { Item, Piece, Creature, Station, Other }

    public sealed class Entry
    {
        public string Id, Name, Desc, Mod, Hidden; // Name / Desc = localization tokens; Mod = plugin GUID, null = Valheim
        public string Icon; // prefab whose icon stands for this entry (Relation.SourceIcon), null = its own / Browser.Icon fallback
        public EntryType Type;
        public ItemCategory Category = ItemCategory.Misc; // items only, set by the vanilla scan (PageModel.Categorize)
        public int MaxQuality = 1;
    }

    // Unity-free index model: entries by prefab id + relations with the design's dedupe key (section 3) and the two
    // lookups the item page shows. Built whole by Browser.Build and swapped in, never patched.
    public sealed class BrowserIndex
    {
        public readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        public readonly List<Relation> Relations = new List<Relation>();
        private readonly HashSet<string> _keys = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Relation>> _from = new Dictionary<string, List<Relation>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Relation>> _used = new Dictionary<string, List<Relation>>(StringComparer.Ordinal);
        private static readonly List<Relation> None = new List<Relation>();

        // Vanilla prefabs the player can never get or meet: test / dev objects, unused duplicates (CapeTest next to the
        // real capes, unused iron ore, NPC-only Dverger weapons, boss power items). Same ids VNEI hides (its
        // ItemBlacklist.json, vanilla only). Listed only with devcommands on (Search unlisted = true, user 2026-11-04).
        public const string UnlistedReason = "unlisted";
        private static readonly HashSet<string> Unlisted = new HashSet<string>(StringComparer.Ordinal)
        {
            "Rock_destructible_test", "TrainingDummy", "HealthUpgrade_Bonemass", "HealthUpgrade_GDKing",
            "StaminaUpgrade_Greydwarf", "StaminaUpgrade_Troll", "StaminaUpgrade_Wraith", "IronOre", "MineRock_Iron",
            "Pickable_BogIronOre", "Trailership", "FirTree_oldLog", "TrophyDraugrFem", "CapeTest", "DvergerArbalest",
            "DvergerArbalest_shoot", "DvergerStaffSupport", "DvergerTest", "EvilHeart_Forest", "EvilHeart_Swamp",
            "TorchMist", "FishAnglerRaw", "fallenvalkyrie_screech", "fallenvalkyrie_swoopattack", "fallenvalkyrie_wingspin",
        };

        /// <summary>Items a humanoid wears (default / random items and sets), filled by the vanilla scan.</summary>
        public readonly HashSet<string> NpcGear = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>After every relation is in (scan + providers): NPC gear nothing produces (FallenWarrior FW_ /
        /// ShadowPerson SP_ copies of player gear) is unlisted like the test prefabs.</summary>
        public void HideNpcOnlyGear()
        {
            foreach (var id in NpcGear)
                if (Entries.TryGetValue(id, out var e) && e.Type == EntryType.Item && e.Hidden == null && ObtainedFrom(id).Count == 0) e.Hidden = UnlistedReason;
        }

        // First writer wins: the vanilla scan adds its entries before any relation names a pseudo source.
        public Entry Add(Entry e)
        {
            if (Entries.TryGetValue(e.Id, out var had)) return had;
            if (e.Hidden == null && Unlisted.Contains(e.Id)) e.Hidden = UnlistedReason;
            Entries[e.Id] = e;
            return e;
        }

        public static string Key(Relation r)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append((int)r.Kind).Append('|').Append(r.Source).Append('|').Append(r.Station).Append('|')
              .Append(r.StationLevel).Append('|').Append(r.Quality).Append('|').Append(r.Conditions);
            foreach (var s in r.Outputs) Stack(sb.Append('>'), s);
            foreach (var s in r.Inputs) Stack(sb.Append('<'), s);
            return sb.ToString();
        }

        private static void Stack(System.Text.StringBuilder sb, Stack s) =>
            sb.Append(s.Prefab).Append(':').Append(s.Min).Append('-').Append(s.Max).Append('@').Append(s.Chance.ToString("R", System.Globalization.CultureInfo.InvariantCulture));

        /// <returns>false when an equal relation is already indexed (dedupe, design section 3).</returns>
        public bool Add(Relation r)
        {
            if (!_keys.Add(Key(r))) return false;
            Relations.Add(r);
            // A recipe is no browsable thing (its station / inputs are); other bare sources (loot tables, rocks) are.
            if (!Entries.ContainsKey(r.Source))
                Add(new Entry { Id = r.Source, Icon = r.SourceIcon, Name = r.SourceName ?? SourceNames.EpicLootName(r.Source, o => Entries.TryGetValue(o, out var oe) ? oe.Name : null) ?? SourceNames.Readable(r.Source), Type = EntryType.Other, Hidden = r.Kind == RelationKind.Craft || r.Kind == RelationKind.Upgrade ? "recipe" : null });
            foreach (var s in r.Outputs) To(_from, s.Prefab, r);
            foreach (var s in r.Inputs) To(_used, s.Prefab, r);
            To(_used, r.Source, r); // a creature's drops, a tool's pieces, a nest's harvest
            if (r.Station != null && r.Station != r.Source) To(_used, r.Station, r);
            return true;
        }

        private static void To(Dictionary<string, List<Relation>> map, string id, Relation r)
        {
            if (!map.TryGetValue(id, out var list)) map[id] = list = new List<Relation>();
            if (list.Count == 0 || list[list.Count - 1] != r) list.Add(r);
        }

        public List<Relation> ObtainedFrom(string id) => _from.TryGetValue(id, out var l) ? l : None;
        public List<Relation> UsedIn(string id) => _used.TryGetValue(id, out var l) ? l : None;

        // Distinct outputs of the relations an entry is used in ("Used in (N)" tiles), entry itself excluded.
        public List<string> UsedInResults(string id)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { id };
            var list = new List<string>();
            foreach (var r in UsedIn(id))
                foreach (var s in r.Outputs)
                    if (seen.Add(s.Prefab)) list.Add(s.Prefab);
            return list;
        }

        /// <summary>Visible entries matching the query: words against the localized name or the prefab id, "@x" against
        /// the mod GUID / name; type null = All (Other included), Favorites passes its own filter.</summary>
        public List<Entry> Search(string query, EntryType? type, Func<Entry, bool> filter, Func<string, string> localize, Func<string, string> modName, bool unlisted = false)
        {
            var words = (query ?? "").ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var hits = new List<KeyValuePair<string, Entry>>();
            foreach (var e in Entries.Values)
            {
                if (e.Hidden != null && !(unlisted && e.Hidden == UnlistedReason) || type.HasValue && e.Type != type.Value || filter != null && !filter(e)) continue;
                var name = localize(e.Name) ?? e.Id;
                if (Matches(words, name.ToLowerInvariant(), e.Id.ToLowerInvariant(), (e.Mod ?? "valheim").ToLowerInvariant() + " " + (modName(e.Mod) ?? "").ToLowerInvariant()))
                    hits.Add(new KeyValuePair<string, Entry>(name, e));
            }
            hits.Sort((a, b) =>
            {
                var c = a.Value.Type.CompareTo(b.Value.Type);
                if (c == 0) c = string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
                return c != 0 ? c : string.CompareOrdinal(a.Value.Id, b.Value.Id);
            });
            var list = new List<Entry>(hits.Count);
            foreach (var h in hits) list.Add(h.Value);
            return list;
        }

        private static bool Matches(string[] words, string name, string id, string mod)
        {
            foreach (var w in words)
            {
                if (w[0] == '@') { if (w.Length > 1 && !mod.Contains(w.Substring(1))) return false; }
                else if (!name.Contains(w) && !id.Contains(w)) return false;
            }
            return true;
        }

        // FNV-1a over strings / ints: the rebuild fingerprint (design section 5).
        public static int Mix(int h, string s)
        {
            unchecked
            {
                if (s != null) foreach (var c in s) h = (h ^ c) * 16777619;
                return (h ^ 0x1F) * 16777619;
            }
        }

        public static int Mix(int h, int v) { unchecked { return (h ^ v) * 16777619; } }

        public const int Seed = unchecked((int)2166136261);
    }

    // Back / forward history of item pages (mouse buttons + arrow hotkeys).
    public sealed class History
    {
        private readonly List<string> _items = new List<string>();
        private int _at = -1;

        public string Current => _at >= 0 ? _items[_at] : null;

        public void Go(string id)
        {
            if (id == Current) return;
            _items.RemoveRange(_at + 1, _items.Count - _at - 1);
            _items.Add(id);
            _at = _items.Count - 1;
        }

        public string Back() => _at > 0 ? _items[--_at] : null;
        public string Forward() => _at < _items.Count - 1 ? _items[++_at] : null;
        public void Clear() { _items.Clear(); _at = -1; }
    }
}
