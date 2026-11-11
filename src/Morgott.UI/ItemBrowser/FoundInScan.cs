using System;
using System.Collections;
using System.Collections.Generic;
using Morgott.Core;
using SoftReferenceableAssets;
using UnityEngine;

namespace Morgott.UI.ItemBrowser
{
    /// <summary>
    /// Fills <see cref="FoundIn"/> once per index generation: world spawn lists (live SpawnSystem lists include every
    /// mod's: Jotunn, Morgott.Creatures roster), vegetation (nests), random events, location + dungeon room prefabs
    /// (CreatureSpawner / SpawnArea / OfferingBowl), index relations (Spawn / Grow / Tree with a creature output, Core
    /// Relation.Biomes) and item summons. Location / room prefabs are soft references: each is loaded, read and released
    /// one per frame (ZoneSystem.cs:930 / DungeonGenerator.cs:202 load them the same way).
    /// </summary>
    internal static class FoundInScan
    {
        public static FoundIn Data { get; private set; }
        /// <summary>Bumped whenever <see cref="Data"/> grows; the page relayouts.</summary>
        public static int Revision { get; private set; }
        /// <summary>The current generation's scan finished (autotest waits on it).</summary>
        public static bool Done { get; private set; }
        /// <summary>Last finished scan: wall time, worst main-thread slice, memory delta (log + autotest).</summary>
        public static string Stats { get; private set; }
        public static double WorstMs { get; private set; }
        public static double NativeDeltaMb { get; private set; }
        private static int _gen = -1;
        private static MonoBehaviour _host;

        /// <summary>Starts the scan of <paramref name="generation"/> on <paramref name="host"/> unless it ran or runs.</summary>
        public static void Ensure(MonoBehaviour host, int generation)
        {
            if (generation == _gen && _host || generation == 0 || !ZoneSystem.instance || Browser.Index == null) return;
            _gen = generation;
            _host = host;
            Done = false;
            host.StartCoroutine(Run(Browser.Index, generation));
        }

        public static string Line(string prefab, Func<Heightmap.Biome, string> biome) =>
            Data != null && Data.ByPrefab.TryGetValue(prefab, out var o) ? FoundIn.Text(o, Browser.Localize, biome) : null;

        private static IEnumerator Run(BrowserIndex ix, int generation)
        {
            var d = new FoundIn();
            var zs = ZoneSystem.instance;
            var wall = System.Diagnostics.Stopwatch.StartNew();
            var slice = System.Diagnostics.Stopwatch.StartNew();
            var worst = 0.0;
            var worstAt = "quick";
            void Slice(string at)
            {
                if (slice.Elapsed.TotalMilliseconds > worst) { worst = slice.Elapsed.TotalMilliseconds; worstAt = at; }
            }
            long native0 = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong(), managed0 = GC.GetTotalMemory(false);
            try { Quick(ix, d, zs); }
            catch (Exception e) { Plugin.Log.LogWarning($"[Morgott.UI] item browser found-in: {e}"); }
            Publish(d);
            Slice("quick");

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
            foreach (var (loc, biome) in locs.Values)
            {
                if (generation != _gen) yield break;
                var r = loc.m_prefab;
                if (!BeginLoad(r)) continue;
                Slice("load " + loc.m_name);
                while (r.IsLoading) yield return null; // async: the bundle read never stalls a frame
                slice.Restart();
                var asset = r.IsLoaded ? r.Asset : null;
                if (asset == null) { r.Release(); continue; }
                try
                {
                    var interior = asset.TryGetComponent<Location>(out var l) && l.m_hasInterior;
                    Prefab(d, asset, biome, interior ? Origin.Dungeon : Origin.Location);
                    foreach (var g in asset.GetComponentsInChildren<DungeonGenerator>(true)) themes.Add((g.m_themes, biome, interior));
                }
                catch (Exception e) { Plugin.Log.LogWarning($"[Morgott.UI] item browser found-in: location {asset.name}: {e.Message}"); }
                finally { loc.m_prefab.Release(); }
                Slice(asset.name);
                yield return null;
                slice.Restart();
            }
            Publish(d);

            // Dungeon / camp rooms: the biomes of every generator whose themes take the room.
            foreach (var room in DungeonDB.GetRooms())
            {
                if (generation != _gen) yield break;
                if (room == null || !room.m_enabled || !room.m_prefab.IsValid) continue;
                Heightmap.Biome inside = 0, outside = 0;
                foreach (var t in themes)
                    if ((room.m_theme & t.Themes) != 0)
                    {
                        if (t.Interior) inside |= t.Biome;
                        else outside |= t.Biome;
                    }
                if (inside == 0 && outside == 0) continue;
                var r = room.m_prefab;
                if (!BeginLoad(r)) continue;
                Slice("load room");
                while (r.IsLoading) yield return null;
                slice.Restart();
                var asset = r.IsLoaded ? r.Asset : null;
                if (asset == null) { r.Release(); continue; }
                try
                {
                    if (inside != 0) Prefab(d, asset, inside, Origin.Dungeon);
                    if (outside != 0) Prefab(d, asset, outside, Origin.Location);
                }
                finally { room.m_prefab.Release(); }
                Slice(asset.name);
                yield return null;
                slice.Restart();
            }
            Publish(d);

            var none = new List<string>();
            foreach (var e in ix.Entries.Values)
                if (e.Type == EntryType.Creature && e.Hidden == null && !d.ByPrefab.ContainsKey(e.Id)) none.Add(e.Id);
            none.Sort(StringComparer.Ordinal);
            for (var i = 0; i < 120; i++) yield return null; // Release unloads async (AssetLoader.UnloadAsync): settle before the memory sample
            WorstMs = worst;
            NativeDeltaMb = (UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() - native0) / 1048576.0;
            Stats = FormattableString.Invariant($"{wall.Elapsed.TotalSeconds:F1} s, worst slice {worst:F1} ms ({worstAt}), memory delta native {NativeDeltaMb:F1} MB managed {(GC.GetTotalMemory(false) - managed0) / 1048576.0:F1} MB");
            Done = true;
            Plugin.Log.LogInfo($"[Morgott.UI] item browser found-in: {d.ByPrefab.Count} prefabs, {locs.Count} locations, {Stats}; " +
                               $"creatures without a source ({none.Count}): {string.Join(", ", none)}");
        }

        // LoadAsync holds a reference (AssetBundleLoader.LoadAsync -> HoldReference): true = the caller releases it.
        private static bool BeginLoad(SoftReference<GameObject> r)
        {
            try { r.LoadAsync(); }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Morgott.UI] item browser found-in: load failed: {e.Message}");
                return false;
            }
            return true;
        }

        private static void Publish(FoundIn d)
        {
            Data = d;
            Revision++;
        }

        // Everything already in memory: spawn lists, events, vegetation, index relations, summons.
        private static void Quick(BrowserIndex ix, FoundIn d, ZoneSystem zs)
        {
            var lists = new HashSet<SpawnSystemList>();
            foreach (var s in SpawnSystem.m_instances) if (s) lists.UnionWith(s.m_spawnLists);
            if (zs.m_zoneCtrlPrefab && zs.m_zoneCtrlPrefab.TryGetComponent<SpawnSystem>(out var ctrl)) lists.UnionWith(ctrl.m_spawnLists);
            foreach (var l in lists)
                if (l)
                    foreach (var sd in l.m_spawners)
                        if (sd != null && sd.m_enabled && !sd.m_devDisabled && sd.m_prefab) d.Add(sd.m_prefab.name, sd.m_biome, Origin.World);

            if (RandEventSystem.instance)
                foreach (var ev in RandEventSystem.instance.m_events)
                    if (ev != null && ev.m_enabled && !ev.m_devDisabled)
                        foreach (var sd in ev.m_spawn)
                            if (sd?.m_prefab) d.Add(sd.m_prefab.name, 0, Origin.Event); // tag only: m_biome = where the raided base may stand (run 20261111-161053: Skeleton read "anywhere")

            foreach (var v in zs.m_vegetation)
                if (v != null && v.m_enable && v.m_prefab) Prefab(d, v.m_prefab, v.m_biome, Origin.World);

            foreach (var r in ix.Relations)
            {
                if (r.Kind != RelationKind.Spawn && r.Kind != RelationKind.Grow && r.Kind != RelationKind.Tree) continue;
                ix.Entries.TryGetValue(r.Source, out var src);
                if (src?.Type == EntryType.Creature) continue; // a boss calling guards, a young animal growing up
                var kind = r.Kind == RelationKind.Tree ? Origin.Tree : src?.Type == EntryType.Item ? Origin.Egg : Origin.Nest;
                foreach (var o in r.Outputs)
                    if (ix.Entries.TryGetValue(o.Prefab, out var e) && e.Type == EntryType.Creature) d.Add(o.Prefab, (Heightmap.Biome)r.Biomes, kind);
            }

            foreach (var go in Browser.Summoned()) if (go) d.Add(go.name, 0, Origin.Summon);
        }

        // Creatures a prefab places: spawner points, nests, boss altars.
        private static void Prefab(FoundIn d, GameObject root, Heightmap.Biome biome, Origin plain)
        {
            foreach (var c in root.GetComponentsInChildren<Character>(true)) d.Add(Utils.GetPrefabName(c.gameObject), biome, plain); // placed as is (sleeping draugr, hives)
            foreach (var cs in root.GetComponentsInChildren<CreatureSpawner>(true))
                if (cs.m_creaturePrefab) d.Add(cs.m_creaturePrefab.name, biome, plain);
            foreach (var sa in root.GetComponentsInChildren<SpawnArea>(true))
                foreach (var s in sa.m_prefabs)
                    if (s?.m_prefab) d.Add(s.m_prefab.name, biome, Origin.Nest);
            foreach (var bowl in root.GetComponentsInChildren<OfferingBowl>(true))
                if (bowl.m_bossPrefab) d.Add(bowl.m_bossPrefab.name, biome, Origin.Boss);
        }
    }
}
