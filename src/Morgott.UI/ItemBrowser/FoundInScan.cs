using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Morgott.Core;
using SoftReferenceableAssets;
using UnityEngine;

namespace Morgott.UI.ItemBrowser
{
    /// <summary>
    /// Fills <see cref="FoundIn"/> once per index generation, at most <see cref="BudgetMs"/> of main thread per frame:
    /// world spawn lists (live SpawnSystem lists include every mod's: Jotunn, Morgott.Creatures roster), random events,
    /// vegetation (nests), index relations (Spawn / Grow / Tree with a creature output, Core Relation.Biomes), item
    /// summons, and what each location / dungeon room prefab places. Those prefabs are soft references: vanilla ones come
    /// from the embedded table (FoundInTable.txt, this game build only); the rest are loaded async, walked time-sliced,
    /// released, and cached under BepInEx\cache\Morgott.UI keyed by prefab + owner mod DLL hash (game build for vanilla).
    /// </summary>
    internal static class FoundInScan
    {
        public static FoundIn Data { get; private set; }
        /// <summary>Bumped whenever <see cref="Data"/> grows; the page relayouts.</summary>
        public static int Revision { get; private set; }
        /// <summary>The current generation's scan finished (autotest waits on it).</summary>
        public static bool Done { get; private set; }
        /// <summary>Index generation of <see cref="Data"/>.</summary>
        public static int Generation => _gen;
        /// <summary>Last finished scan: wall time, worst main-thread slice, loads, memory (log + autotest).</summary>
        public static string Stats { get; private set; }
        public static double WorstMs { get; private set; }
        /// <summary>Prefabs the last scan loaded (0 = table + cache covered everything).</summary>
        public static int Loaded { get; private set; }
        /// <summary>Autotest: ignore table and cache (full lazy scan: table dump, perf / memory A/B).</summary>
        public static bool ForceLazy;
        /// <summary>The embedded table matches this game build.</summary>
        public static bool TableCurrent => Table() != null;

        private const double BudgetMs = 2.0; // + one engine call over it stays under the 5 ms gate (run 20261111-171917-2: 6.0 ms at 3)
        private static int _gen = -1;
        private static MonoBehaviour _host;
        private static readonly System.Diagnostics.Stopwatch _slice = new System.Diagnostics.Stopwatch();
        private static readonly Dictionary<string, Scanned> _vanilla = new Dictionary<string, Scanned>(StringComparer.Ordinal); // last lazy scans of vanilla prefabs (dump)

        /// <summary>Starts the scan of <paramref name="generation"/> on <paramref name="host"/> unless it ran or runs.</summary>
        public static void Ensure(MonoBehaviour host, int generation)
        {
            if (generation == _gen && _host || generation == 0 || !ZoneSystem.instance || Browser.Index == null) return;
            _gen = generation;
            _host = host;
            Done = false;
            host.StartCoroutine(Run(Browser.Index, generation));
        }

        private static bool _unloadPending;

        /// <summary>Panel tick: after a big fallback scan, one Resources.UnloadUnusedAssets (the game's own Game.cs:327 call)
        /// while the browser is closed and no enemy is within 30 m (BaseAI.HaveEnemyInRange) - the hitch never lands in a fight.</summary>
        public static void UnloadWhenSafe(bool browserOpen)
        {
            var p = Player.m_localPlayer;
            if (!_unloadPending || browserOpen || !Done || !p || BaseAI.HaveEnemyInRange(p, p.transform.position, 30f)) return;
            _unloadPending = false;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _ = Resources.UnloadUnusedAssets(); // completes on its own; nothing waits for it
            Plugin.Log.LogInfo(FormattableString.Invariant($"[Morgott.UI] item browser found-in: Resources.UnloadUnusedAssets after the fallback scan ({sw.Elapsed.TotalMilliseconds:F0} ms to start)"));
        }

        /// <summary>Autotest: the next <see cref="Ensure"/> scans again.</summary>
        public static void Rescan() => _gen = -1;

        /// <summary>Autotest: the vanilla prefabs the last lazy scan read, as an embeddable table.</summary>
        public static string TableText() => FoundInTable.Write(GameVersion(), _vanilla);

        public static string Line(string prefab, Func<Heightmap.Biome, string> biome) =>
            Data != null && Data.ByPrefab.TryGetValue(prefab, out var o) ? FoundIn.Text(o, Browser.Localize, biome) : null;

        private static readonly SortedSet<string> _staleTable = new SortedSet<string>(StringComparer.Ordinal), _failed = new SortedSet<string>(StringComparer.Ordinal);

        /// <summary>Autotest: why the table needs a rebuild (empty = current): vanilla prefabs it lacks + prefabs that failed to load.
        /// Rebuild = the forced scan of check item-browser-found-in, then its found-in-table.txt over ItemBrowser\FoundInTable.txt.</summary>
        public static string[] Stale()
        {
            var all = new List<string>();
            foreach (var k in _staleTable) all.Add("missing " + k);
            foreach (var k in _failed) all.Add("unread " + k);
            return all.ToArray();
        }

        private static readonly List<SoftReference<GameObject>> _loadedRefs = new List<SoftReference<GameObject>>();
        /// <summary>Prefabs this scan loaded that are still loaded 2 s after it (ours released; the game may hold some).</summary>
        public static int StillLoaded { get; private set; }

        private static bool Over => _slice.Elapsed.TotalMilliseconds > BudgetMs;

        private static IEnumerator Run(BrowserIndex ix, int generation)
        {
            var d = new FoundIn();
            var zs = ZoneSystem.instance;
            var wall = System.Diagnostics.Stopwatch.StartNew();
            var worst = 0.0;
            var worstAt = "";
            var loads = 0;
            var at = "quick";
            _slice.Restart();
            // Called right before every yield of this scan: the slice that ends there counts (restarted after it).
            void Tick()
            {
                if (_slice.Elapsed.TotalMilliseconds > worst) { worst = _slice.Elapsed.TotalMilliseconds; worstAt = at; }
            }

            foreach (var _ in Quick(ix, d, zs)) { Tick(); yield return null; _slice.Restart(); }
            Publish(d);

            var table = ForceLazy ? null : Table();
            var cache = ForceLazy ? new Dictionary<string, Scanned>(StringComparer.Ordinal) : Cache();
            var cacheDirty = false;
            var newAltars = false;
            if (ForceLazy) _vanilla.Clear();
            _staleTable.Clear();
            _failed.Clear();

            // One scan per prefab key: from the table (vanilla, this build), the cache, or a lazy load.
            IEnumerable<object> Lookup(string key, SoftReference<GameObject> r, string owner, Action<Scanned> got)
            {
                Scanned s = null;
                if (owner == null) table?.TryGetValue(key, out s);
                var ckey = CacheKey(key, owner);
                if (s == null && table != null && owner == null) _staleTable.Add(key); // a vanilla prefab the table lacks: rebuild it
                if (s == null && cache.TryGetValue(ckey, out var c) && c.Objects != null) s = c; // a line without objects (old cache) is reread once
                if (s == null)
                {
                    at = key;
                    loads++;
                    foreach (var y in Lazy(r, x => s = x)) yield return y;
                    if (s == null)
                    {
                        _failed.Add(key);
                        got(new Scanned()); // no objects column: Place marks it stale, only stages above its biome stay unknown
                        yield break;
                    }
                    cache[ckey] = s;
                    cacheDirty = true;
                    newAltars |= s.Summons.Count > 0 && !ForceLazy; // the index built before this line existed
                    if (owner == null) _vanilla[key] = s;
                }
                got(s);
            }

            // Locations, one prefab each (duplicate entries of one prefab merged, biomes OR-ed).
            var locs = new Dictionary<string, (ZoneSystem.ZoneLocation Loc, Heightmap.Biome Biome)>(StringComparer.Ordinal);
            foreach (var loc in zs.m_locations)
            {
                if (loc == null || !loc.m_enable || !loc.m_prefab.IsValid) continue;
                string name;
                try { name = loc.m_prefab.Name; } catch (Exception) { continue; } // empty asset id has no name
                locs[name] = (locs.TryGetValue(name, out var had) ? had.Loc : loc, had.Biome | loc.m_biome);
            }
            var themes = new List<(Room.Theme Themes, Heightmap.Biome Biome, bool Interior)>();
            foreach (var pair in locs)
            {
                if (generation != _gen) yield break;
                var (loc, biome) = pair.Value;
                foreach (var _ in Lookup("L:" + pair.Key, loc.m_prefab, Owner(pair.Key), s =>
                         {
                             var plain = s.Interior ? Origin.Dungeon : Origin.Location;
                             foreach (var (c, k) in s.Creatures) d.Add(c, biome, k == Origin.World ? plain : k);
                             d.Place(s, biome, "L:" + pair.Key);
                             if (s.Themes != 0) themes.Add(((Room.Theme)s.Themes, biome, s.Interior));
                         }))
                { Tick(); yield return null; _slice.Restart(); }
                if (Over) { Tick(); yield return null; _slice.Restart(); }
            }
            Publish(d);

            // Dungeon / camp rooms: the biomes of every generator whose themes take the room.
            foreach (var room in DungeonDB.GetRooms())
            {
                if (generation != _gen) yield break;
                if (room == null || !room.m_enabled || !room.m_prefab.IsValid) continue;
                Heightmap.Biome inside = Heightmap.Biome.None, outside = Heightmap.Biome.None;
                foreach (var t in themes)
                    if ((room.m_theme & t.Themes) != Room.Theme.None)
                    {
                        if (t.Interior) inside |= t.Biome;
                        else outside |= t.Biome;
                    }
                if (inside == Heightmap.Biome.None && outside == Heightmap.Biome.None) continue;
                string name;
                try { name = room.m_prefab.Name; } catch (Exception) { continue; }
                // shortcut: rooms are keyed by game build only (no owner lookup); a mod changing its rooms keeps the old cache entry until the game updates.
                foreach (var _ in Lookup("R:" + name, room.m_prefab, null, s =>
                         {
                             foreach (var (c, k) in s.Creatures)
                             {
                                 if (inside != Heightmap.Biome.None) d.Add(c, inside, k == Origin.World ? Origin.Dungeon : k);
                                 if (outside != Heightmap.Biome.None) d.Add(c, outside, k == Origin.World ? Origin.Location : k);
                             }
                             d.Place(s, inside | outside, "R:" + name);
                         }))
                { Tick(); yield return null; _slice.Restart(); }
                if (Over) { Tick(); yield return null; _slice.Restart(); }
            }
            Publish(d);
            if (cacheDirty) SaveCache(cache); // a forced scan rewrites the cache whole
            if (newAltars) OfferingRevision++;

            var none = new List<string>();
            foreach (var e in ix.Entries.Values)
                if (e.Type == EntryType.Creature && e.Hidden == null && !d.ByPrefab.ContainsKey(e.Id)) none.Add(e.Id);
            none.Sort(StringComparer.Ordinal);
            if (loads > 50) _unloadPending = true; // a fallback scan (stale table): free the orphaned prefab objects once, at a safe moment
            for (var t0 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t0 < 2f;) yield return null; // async unloads settle
            StillLoaded = 0;
            foreach (var lr in _loadedRefs) if (lr.IsLoaded) StillLoaded++;
            _loadedRefs.Clear();
            WorstMs = worst;
            Loaded = loads;
            Stats = FormattableString.Invariant($"{wall.Elapsed.TotalSeconds:F1} s, worst slice {worst:F1} ms ({worstAt}), loaded {loads} (still loaded {StillLoaded}), table {(table != null ? "current" : "off")}, stale {_staleTable.Count + _failed.Count}");
            Done = true;
            Plugin.Log.LogInfo($"[Morgott.UI] item browser found-in: {d.ByPrefab.Count} prefabs, {locs.Count} locations, {Stats}; " +
                               $"creatures without a source ({none.Count}): {string.Join(", ", none)}");
        }


        // Async load, time-sliced walk of every transform, release (also when the scan is abandoned: finally).
        private static IEnumerable<object> Lazy(SoftReference<GameObject> r, Action<Scanned> done)
        {
            _loadedRefs.Add(r);
            try { r.LoadAsync(); } // holds a reference (AssetBundleLoader.LoadAsync -> HoldReference)
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Morgott.UI] item browser found-in: load failed: {e.Message}");
                yield break;
            }
            try
            {
                yield return null; // LoadAsync's own cost ends this slice
                while (r.IsLoading) yield return null;
                var asset = r.IsLoaded ? r.Asset : null;
                if (asset == null) yield break;
                yield return null; // so does the Asset fetch
                var s = new Scanned { Interior = asset.TryGetComponent<Location>(out var l) && l.m_hasInterior, Objects = new List<string>() };
                var stack = new Stack<Transform>();
                stack.Push(asset.transform);
                while (stack.Count > 0)
                {
                    var t = stack.Pop();
                    if (t.TryGetComponent<OfferingBowl>(out _)) yield return null; // an altar reads its stands + ObjectDB: a slice of its own
                    Read(s, t.gameObject);
                    for (var i = 0; i < t.childCount; i++) stack.Push(t.GetChild(i));
                    if (Over) yield return null;
                }
                done(s);
            }
            finally { r.Release(); }
        }


        // What one object of a location / room places: a creature standing there (sleeping draugr, hives), a spawner
        // point, a nest, a boss altar; dungeon generators' themes.
        private static void Read(Scanned s, GameObject go)
        {
            if (go.TryGetComponent<Character>(out _)) s.Add(Utils.GetPrefabName(go), Origin.World);
            if (go.TryGetComponent<CreatureSpawner>(out var cs) && cs.m_creaturePrefab) s.Add(cs.m_creaturePrefab.name, Origin.World);
            if (go.TryGetComponent<SpawnArea>(out var sa))
                foreach (var p in sa.m_prefabs)
                    if (p?.m_prefab) s.Add(p.m_prefab.name, Origin.Nest);
            if (go.TryGetComponent<OfferingBowl>(out var bowl) && bowl.m_bossPrefab)
            {
                s.Add(bowl.m_bossPrefab.name, Origin.Boss);
                var o = Offering(bowl);
                if (!s.Summons.Contains(o)) s.Summons.Add(o);
            }
            if (go.TryGetComponent<DungeonGenerator>(out var g)) s.Themes |= (int)g.m_themes;
            if (go.GetComponent<Pickable>() || go.GetComponent<Container>() || go.GetComponent<MineRock>() || go.GetComponent<MineRock5>() || go.GetComponent<TreeBase>() ||
                go.GetComponent<ResourceRoot>() || go.GetComponent<TreeLog>() || go.GetComponent<Destructible>() || go.GetComponent<DropOnDestroyed>() || go.GetComponent<ItemDrop>() || go.GetComponent<Trader>() || sa)
                s.AddObject(Utils.GetPrefabName(go));
        }

        /// <summary>What one altar takes (OfferingBowl.UseItem / Interact): m_bossItems x m_bossItem, or with m_useItemStands one
        /// item on every stand FindItemStands finds (name starts with m_itemStandPrefix, within m_itemstandMaxRange; in the
        /// prefab: the location's stands). Stands taking the same items are one group (count = stands), alternatives
        /// '|'-joined; several groups = one spec (FoundInTable.Spec); no listable stand = (boss, "", 0).</summary>
        internal static (string Boss, string Item, int Count) Offering(OfferingBowl bowl)
        {
            var boss = bowl.m_bossPrefab.name;
            if (!bowl.m_useItemStands) return bowl.m_bossItem ? (boss, bowl.m_bossItem.gameObject.name, bowl.m_bossItems) : (boss, "", 0);
            var list = new List<(string Item, int Count)>();
            var at = bowl.transform.position;
            foreach (var st in bowl.transform.root.GetComponentsInChildren<ItemStand>(true))
            {
                if (Vector3.Distance(at, st.transform.position) > bowl.m_itemstandMaxRange || !st.gameObject.name.StartsWith(bowl.m_itemStandPrefix, StringComparison.Ordinal)) continue;
                var alts = string.Join("|", Attachable(st));
                if (alts.Length == 0) alts = FoundInTable.AnyItem; // takes any item: still a stand to fill
                var i = list.FindIndex(g => g.Item == alts);
                if (i < 0) list.Add((alts, 1));
                else list[i] = (alts, list[i].Count + 1);
            }
            var (item, count) = FoundInTable.Spec(list);
            return (boss, item, count);
        }

        /// <summary>Items ItemStand.CanAttach takes, over every ObjectDB item: an "attach" child, not in m_unsupportedItems, in
        /// m_supportedItems (by shared name, as vanilla) or of a m_supportedTypes type. Empty m_supportedItems = any item (empty list).</summary>
        // shortcut: resolved against this session's ObjectDB and cached per location owner / build; an item mod added later that
        // matches a stand by shared name or type joins after the next game build or "#aN" bump, upgrade if a modded altar needs it.
        internal static List<string> Attachable(ItemStand st)
        {
            var list = new List<string>();
            if (st.m_supportedItems == null || st.m_supportedItems.Count == 0 || !ObjectDB.instance) return list;
            var yes = new HashSet<string>(StringComparer.Ordinal);
            var no = new HashSet<string>(StringComparer.Ordinal);
            foreach (var i in st.m_supportedItems) if (i) yes.Add(i.m_itemData.m_shared.m_name);
            if (st.m_unsupportedItems != null) foreach (var i in st.m_unsupportedItems) if (i) no.Add(i.m_itemData.m_shared.m_name);
            foreach (var go in ObjectDB.instance.m_items)
            {
                if (!go || !go.TryGetComponent<ItemDrop>(out var d)) continue;
                var s = d.m_itemData.m_shared;
                if (!no.Contains(s.m_name) && (yes.Contains(s.m_name) || st.m_supportedTypes != null && st.m_supportedTypes.Contains(s.m_itemType)) && ItemStand.GetAttachPrefab(go) && !list.Contains(go.name))
                    list.Add(go.name);
            }
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        /// <summary>Bumped when a lazy scan found altars the index could not know (no table / cache line yet): Browser's
        /// fingerprint rebuilds the index with them.</summary>
        public static int OfferingRevision { get; private set; }

        /// <summary>Altar offerings of the locations / rooms this session has (enabled), each read like the scan reads it: the
        /// embedded table for vanilla, else the cache line of this owner / build. Per location and boss: its stand groups.</summary>
        internal static List<(string Boss, List<(string Item, int Count)> Groups)> Offerings()
        {
            var list = new List<(string, List<(string, int)>)>();
            var zs = ZoneSystem.instance;
            if (!zs) return list;
            var table = Table();
            var cache = Cache();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            void Of(string key, string owner)
            {
                if (!seen.Add(key)) return;
                Scanned s = null;
                if (owner == null) table?.TryGetValue(key, out s);
                if (s == null) cache.TryGetValue(CacheKey(key, owner), out s);
                if (s == null) return;
                foreach (var o in s.Summons) list.Add((o.Boss, FoundInTable.Groups(o.Item, o.Count)));
            }
            foreach (var loc in zs.m_locations)
            {
                if (loc == null || !loc.m_enable || !loc.m_prefab.IsValid) continue;
                string name;
                try { name = loc.m_prefab.Name; } catch (Exception) { continue; }
                Of("L:" + name, Owner(name));
            }
            if (DungeonDB.instance)
                foreach (var room in DungeonDB.GetRooms())
                {
                    if (room == null || !room.m_enabled || !room.m_prefab.IsValid) continue;
                    string name;
                    try { name = room.m_prefab.Name; } catch (Exception) { continue; }
                    Of("R:" + name, null);
                }
            return list;
        }

        private static void Publish(FoundIn d)
        {
            Data = d;
            Revision++;
        }

        // Everything already in memory: spawn lists, events, vegetation, index relations, summons. Yields when over budget.
        private static IEnumerable<object> Quick(BrowserIndex ix, FoundIn d, ZoneSystem zs)
        {
            var lists = new HashSet<SpawnSystemList>();
            foreach (var s in SpawnSystem.m_instances) if (s) lists.UnionWith(s.m_spawnLists);
            if (zs.m_zoneCtrlPrefab && zs.m_zoneCtrlPrefab.TryGetComponent<SpawnSystem>(out var ctrl)) lists.UnionWith(ctrl.m_spawnLists);
            foreach (var l in lists)
                if (l)
                    foreach (var sd in l.m_spawners)
                        if (sd != null && sd.m_enabled && !sd.m_devDisabled && sd.m_prefab)
                        {
                            d.Add(sd.m_prefab.name, sd.m_biome, Origin.World);
                            d.World.Anchor(sd.m_prefab.name, sd.m_biome, true, sd.m_requiredGlobalKey);
                        }
            // Alt-biome spawns (SpawnSystem.UpdateSpawn :178-186 runs AltBiome.m_spawn of the zone's corner alt biomes, each
            // entry still gated by its own m_biome, IsSpawnPointGood :335); their vegetation / locations ZoneSystem already
            // merged into m_vegetation / m_locations (SetupLocations :883-894).
            foreach (var alt in AltBiomeList.m_altBiomes)
                if (alt != null && alt.m_enabled)
                    foreach (var sd in alt.m_spawn)
                        if (sd != null && sd.m_enabled && !sd.m_devDisabled && sd.m_prefab && !alt.m_blockSpawnNames.Contains(sd.m_name) && (sd.m_biome & alt.m_biome) is var b && b != Heightmap.Biome.None)
                        {
                            d.Add(sd.m_prefab.name, b, Origin.World);
                            d.World.Anchor(sd.m_prefab.name, b, true, sd.m_requiredGlobalKey);
                        }

            if (RandEventSystem.instance)
                foreach (var ev in RandEventSystem.instance.m_events)
                    if (ev != null && ev.m_enabled && !ev.m_devDisabled)
                        foreach (var sd in ev.m_spawn)
                            if (sd?.m_prefab) d.Add(sd.m_prefab.name, Heightmap.Biome.None, Origin.Event); // tag only: m_biome = where the raided base may stand (run 20261111-161053: Skeleton read "anywhere")
            yield return null; // every section in its own frame: first-call costs (JIT, Summoned) spread (run 20261111-174554: 18.7 ms in one)

            yield return null;
            var veg = new Scanned { Objects = new List<string>() };
            foreach (var v in zs.m_vegetation)
            {
                if (v == null || !v.m_enable || !v.m_prefab) continue;
                veg.Creatures.Clear();
                veg.Objects.Clear();
                foreach (var t in v.m_prefab.GetComponentsInChildren<Transform>(true)) Read(veg, t.gameObject);
                foreach (var (c, k) in veg.Creatures)
                {
                    d.Add(c, v.m_biome, k);
                    d.World.Anchor(c, v.m_biome, true);
                }
                d.World.Anchor(v.m_prefab.name, v.m_biome, true);
                foreach (var o in veg.Objects) d.World.Anchor(o, v.m_biome, true);
                if (Over) yield return null;
            }

            foreach (var r in ix.Relations)
            {
                if (r.Kind != RelationKind.Spawn && r.Kind != RelationKind.Grow && r.Kind != RelationKind.Tree) continue;
                ix.Entries.TryGetValue(r.Source, out var src);
                if (src?.Type == EntryType.Creature) continue; // a boss calling guards, a young animal growing up
                var kind = r.Kind == RelationKind.Tree ? Origin.Tree : src?.Type == EntryType.Item ? Origin.Egg : Origin.Nest;
                foreach (var o in r.Outputs)
                    if (ix.Entries.TryGetValue(o.Prefab, out var e) && e.Type == EntryType.Creature) d.Add(o.Prefab, (Heightmap.Biome)r.Biomes, kind);
                if (Over) yield return null;
            }
            yield return null;

            foreach (var go in Browser.Summoned()) if (go) d.Add(go.name, Heightmap.Biome.None, Origin.Summon);
        }

        // ---------------------------------------------------------------- table, cache, owners

        // Game version + Steam build id: a patch that keeps the version string still misses the table. No Steam client
        // (crossplay / not initialised): version only, which never matches a table dumped with a build id (lazy fallback).
        private static string _gameVersion;
        private static string GameVersion()
        {
            if (_gameVersion != null) return _gameVersion;
            uint build = 0;
            try { build = (uint)Steamworks.SteamApps.GetAppBuildId(); } catch (Exception) { }
            return _gameVersion = global::Version.GetVersionString(true) + (build > 0 ? " b" + build : "");
        }

        private static bool _tableRead;
        private static Dictionary<string, Scanned> _table;

        // The embedded vanilla table, null (one Warning) when it was dumped from another game build.
        // Rebuild (game update, or Stale() not empty): one autotest run with check item-browser-found-in (forced scan) -> copy <shots>\found-in-table.txt over FoundInTable.txt.
        private static Dictionary<string, Scanned> Table()
        {
            if (_tableRead) return _table;
            _tableRead = true;
            string text = null;
            using (var stream = typeof(FoundInScan).Assembly.GetManifestResourceStream("Morgott.UI.FoundInTable.txt"))
                if (stream != null) using (var reader = new StreamReader(stream)) text = reader.ReadToEnd();
            var map = FoundInTable.Parse(text, out var dumped, out _);
            if (dumped == GameVersion()) _table = map;
            else Plugin.Log.LogWarning($"[Morgott.UI] item browser found-in: table is from game '{dumped}', this is '{GameVersion()}': every location is scanned (cached after the first time); regenerate FoundInTable.txt");
            return _table;
        }

        // Cache line key: prefab key @ owner mod (or game build) + the scan format ("#a2": altar stand groups via CanAttach; older
        // lines held the first stand's item only and are rescanned once).
        private const string ScanFormat = "#a2";
        private static string CacheKey(string key, string owner) => key + "@" + (owner ?? GameVersion()) + ScanFormat;

        private static string CachePath => Path.Combine(BepInEx.Paths.CachePath, "Morgott.UI", "found-in.txt");

        private static Dictionary<string, Scanned> _cache;

        // Read + parsed once per session; the scan adds to it in memory and SaveCache writes it.
        private static Dictionary<string, Scanned> Cache()
        {
            if (_cache != null) return _cache;
            try
            {
                _cache = File.Exists(CachePath) ? FoundInTable.Parse(File.ReadAllText(CachePath), out _, out _) : new Dictionary<string, Scanned>(StringComparer.Ordinal);
                foreach (var k in new List<string>(_cache.Keys)) if (!k.EndsWith(ScanFormat, StringComparison.Ordinal)) _cache.Remove(k); // older format: rescanned
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Morgott.UI] item browser found-in: cache unreadable, rescanning: {e.Message}");
                _cache = new Dictionary<string, Scanned>(StringComparer.Ordinal);
            }
            return _cache;
        }

        private static void SaveCache(Dictionary<string, Scanned> cache)
        {
            _cache = cache;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CachePath));
                File.WriteAllText(CachePath, FoundInTable.Write(GameVersion(), cache));
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[Morgott.UI] item browser found-in: cache not saved: {e.Message}"); }
        }

        private static readonly Dictionary<string, string> _dllHash = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Mod that added the location (Jotunn ZoneManager) as "guid#dllhash"; null = vanilla (or added outside Jotunn).
        private static string Owner(string location)
        {
            try
            {
                var guid = Jotunn.Managers.ZoneManager.Instance.GetCustomLocation(location)?.SourceMod?.GUID;
                if (guid == null || !BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(guid, out var info)) return null;
                if (!_dllHash.TryGetValue(info.Location, out var hash))
                    using (var sha = SHA256.Create())
                        _dllHash[info.Location] = hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(info.Location)), 0, 8).Replace("-", "");
                return guid + "#" + hash;
            }
            catch (Exception) { return null; } // shortcut: a location another mod adds without Jotunn counts as vanilla (cache keyed by game build)
        }
    }
}
