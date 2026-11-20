using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Morgott.Core;
using UnityEngine;

namespace Morgott.UI.ItemBrowser
{
    /// <summary>Per-generation result: stage of every entry + its facet values.</summary>
    internal sealed class StageData
    {
        public readonly Dictionary<string, StageInfo> Info = new Dictionary<string, StageInfo>(StringComparer.Ordinal);
        public readonly FacetTable Facets = new FacetTable();
        public StageSolver Solver;
        public readonly HashSet<string> MagicFacts = new HashSet<string>(StringComparer.Ordinal); // ItemFact.Magic, for the dump
    }

    /// <summary>
    /// Runs after <see cref="FoundInScan"/> of the same index generation, at most <see cref="BudgetMs"/> of main thread per
    /// frame: prefab facts (bosses, keys, tools, extensions), <see cref="StageGraph"/>, <see cref="StageSolver"/>, facets.
    /// Until it publishes, entries read as unknown; the panel refreshes on <see cref="Revision"/>.
    /// </summary>
    internal static class StageScan
    {
        private const double BudgetMs = 2.0;
        private static readonly FacetTable Empty = new FacetTable();
        private static int _gen = -1;
        private static MonoBehaviour _host;
        private static readonly Stopwatch Slice = new Stopwatch();

        public static StageData Data { get; private set; }
        public static int Revision { get; private set; }
        public static bool Done { get; private set; }
        public static FacetTable Facets => Data != null ? Data.Facets : Empty;

        public static StageInfo Info(string id) => Data != null && Data.Info.TryGetValue(id, out var i) ? i : StageInfo.None;

        public static void Ensure(MonoBehaviour host, int generation)
        {
            if (generation == 0 || generation == _gen && _host || !FoundInScan.Done || FoundInScan.Generation != generation || FoundInScan.Data == null || Browser.Index == null) return;
            _gen = generation;
            _host = host;
            Done = false;
            host.StartCoroutine(Run(Browser.Index, FoundInScan.Data.World, generation));
        }

        private static bool Over => Slice.Elapsed.TotalMilliseconds > BudgetMs;

        private static IEnumerator Run(BrowserIndex ix, StageWorld w, int generation)
        {
            var wall = Stopwatch.StartNew();
            Slice.Restart();
            var seen = new HashSet<GameObject>();
            var spawned = new HashSet<GameObject>();
            var prefabs = new List<GameObject>(ObjectDB.instance.m_items);
            if (ZNetScene.instance) prefabs.AddRange(ZNetScene.instance.m_prefabs);
            foreach (var go in prefabs)
            {
                if (go && seen.Add(go))
                {
                    try { Read(go, w, spawned); }
                    catch (Exception e) { Warn(go.name, e); }
                }
                if (Over) { yield return null; Slice.Restart(); }
            }
            if (generation != _gen) yield break;

            if (!w.Complete) Plugin.Log.LogWarning($"[Morgott.UI] item browser stages: no placed objects for {string.Join(", ", w.Stale.Keys)}: stages above their lowest biome stay unknown; regenerate FoundInTable.txt");
            var facts = ItemFacts.Collect();
            var byPrefab = new Dictionary<string, ItemFact>(StringComparer.Ordinal);
            foreach (var f in facts) byPrefab[f.Prefab] = f;
            var solver = new StageSolver();
            // A failure here leaves every entry unknown; the browser itself keeps working.
            var build = StageGraph.Build(ix, w, facts, solver).GetEnumerator();
            while (true)
            {
                bool more, solved = false;
                try
                {
                    more = build.MoveNext();
                    if (!more) solved = solver.Step(128);
                }
                catch (Exception e)
                {
                    Warn("solver", e);
                    yield break;
                }
                if (!more && solved) break;
                if (Over) { yield return null; Slice.Restart(); }
            }
            if (generation != _gen) yield break;

            var data = new StageData { Solver = solver };
            foreach (var f in facts) if (f.Magic) data.MagicFacts.Add(f.Prefab);
            var members = StageRules.SetMembers(facts, ix.Entries.Keys, SetName);
            var setBiomes = StageRules.SetBiomes(members, solver.Result);
            foreach (var e in ix.Entries.Values)
            {
                try
                {
                    data.Info[e.Id] = solver.Result(e.Id);
                    if (e.Hidden == null || e.Hidden == BrowserIndex.UnlistedReason)
                    {
                        byPrefab.TryGetValue(e.Id, out var fact);
                        AddFacets(data.Facets, ix, e, fact, members, setBiomes);
                    }
                }
                catch (Exception ex) { Warn(e.Id, ex); }
                if (Over) { yield return null; Slice.Restart(); }
            }
            if (generation != _gen) yield break;
            Data = data;
            Revision++;
            Done = true;
            Plugin.Log.LogInfo(FormattableString.Invariant($"[Morgott.UI] item browser stages: {data.Info.Count} entries, {facts.Count} facts, {wall.Elapsed.TotalSeconds:F1} s"));
        }

        private static string SetName(string prefab)
        {
            var go = Browser.Prefab(prefab);
            return go && go.TryGetComponent<ItemDrop>(out var d) ? d.m_itemData?.m_shared?.m_setName : null;
        }

        private static void Warn(string what, Exception e) => Plugin.Log.LogWarning($"[Morgott.UI] item browser stages: {what}: {e.Message}");

        private static void Read(GameObject go, StageWorld w, HashSet<GameObject> spawned)
        {
            if (go.TryGetComponent<Character>(out var c))
            {
                if (c.m_boss) w.Bosses.Add(go.name);
                if (!string.IsNullOrEmpty(c.m_defeatSetGlobalKey)) StageWorld.Multi(w.Defeats, c.m_defeatSetGlobalKey, go.name);
            }
            if (go.TryGetComponent<StationExtension>(out var ext) && ext.m_craftingStation) StageWorld.Multi(w.Extensions, ext.m_craftingStation.gameObject.name, go.name);
            if (go.TryGetComponent<SapCollector>(out var sap) && sap.m_spawnItem && sap.m_mustConnectTo) w.Tapped.Add((sap.m_spawnItem.gameObject.name, go.name, sap.m_mustConnectTo.gameObject.name));
            if (!go.TryGetComponent<ItemDrop>(out var d) || d.m_itemData?.m_shared is not ItemDrop.ItemData.SharedData s) return;
            bool pick = s.m_damages.m_pickaxe > 0f, chop = s.m_damages.m_chop > 0f;
            if (pick || chop) w.Tools.Add(new StageTool(go.name, pick, chop, s.m_toolTier));
            spawned.Clear();
            Morgott.UI.PlayerHud.SummonFrames.Summoned(s, spawned);
            foreach (var g in spawned) if (g) w.ItemSpawns.Add((go.name, g.name));
        }

        // ---------------------------------------------------------------- facets (DESIGN.md section 4)

        private static string StationOf(BrowserIndex ix, string id, out string name)
        {
            name = null;
            foreach (var r in ix.ObtainedFrom(id))
                if ((r.Kind == RelationKind.Craft || r.Kind == RelationKind.Conversion) && r.Station != null && ix.Entries.TryGetValue(r.Station, out var st))
                {
                    name = st.Name;
                    return st.Id;
                }
            return null;
        }

        private static void AddFacets(FacetTable t, BrowserIndex ix, Entry e, ItemFact fact, Dictionary<string, ItemFact> members, Dictionary<string, int> setBiomes)
        {
            t.AddIndexFacets(ix, e, Browser.ModName);
            var go = Browser.Prefab(e.Id);
            if (!go) return;
            switch (e.Type)
            {
                case EntryType.Item when go.TryGetComponent<ItemDrop>(out var d) && d.m_itemData?.m_shared != null: Item(t, ix, e, d.m_itemData.m_shared, fact, members, setBiomes); break;
                case EntryType.Piece when go.TryGetComponent<Piece>(out var p): PieceFacets(t, e, p); break;
                case EntryType.Station: StationFacets(t, e, go); break;
                case EntryType.Creature when go.TryGetComponent<Character>(out var c): Creature(t, e, c, fact); break;
            }
        }

        private static void Damage(FacetTable t, string id, string kind, float v)
        {
            if (v > 0f) t.Add("damage", id, kind, "$inventory_" + kind);
        }

        private static void Item(FacetTable t, BrowserIndex ix, Entry e, ItemDrop.ItemData.SharedData s, ItemFact fact, Dictionary<string, ItemFact> members, Dictionary<string, int> setBiomes)
        {
            var id = e.Id;
            var type = s.m_itemType.ToString().ToLowerInvariant();
            var station = StationOf(ix, id, out var stationName);
            switch (e.Category)
            {
                case ItemCategory.Weapons:
                    if (s.m_itemType == ItemDrop.ItemData.ItemType.Shield) t.Add("class", id, "shield", "$ib_f_shield");
                    else if (s.m_itemType == ItemDrop.ItemData.ItemType.Torch) t.Add("class", id, "torch", "$ib_f_torch");
                    else if (StageRules.HasWeaponClass(s.m_skillType, s.m_damages.GetTotalDamage() > 0f)) t.Add("class", id, s.m_skillType.ToString().ToLowerInvariant(), "$skill_" + s.m_skillType.ToString().ToLowerInvariant());
                    t.Add("grip", id, type, "$ib_itemtype_" + type);
                    var dmg = s.m_damages;
                    Damage(t, id, "blunt", dmg.m_blunt);
                    Damage(t, id, "slash", dmg.m_slash);
                    Damage(t, id, "pierce", dmg.m_pierce);
                    Damage(t, id, "fire", dmg.m_fire);
                    Damage(t, id, "frost", dmg.m_frost);
                    Damage(t, id, "lightning", dmg.m_lightning);
                    Damage(t, id, "poison", dmg.m_poison);
                    Damage(t, id, "spirit", dmg.m_spirit);
                    break;
                case ItemCategory.Armor:
                    t.Add("slot", id, type, "$ib_itemtype_" + type);
                    if (!members.TryGetValue(id, out var set)) break;
                    t.Add("line", id, set.LineId, set.LineNameToken ?? set.LineId);
                    if (setBiomes.TryGetValue(id, out var biome)) t.Add("tier", id, biome.ToString(), Morgott.UI.MapScreen.MapFilterMath.NameKey(FoundIn.Order[biome]));
                    break;
                case ItemCategory.Utility:
                    if (Contracts.RingPrefabs.Contains(id)) t.Add("type", id, "ring", "$ib_f_ring");
                    else t.Add("type", id, type, "$ib_itemtype_" + type);
                    break;
                case ItemCategory.Ammo:
                    if (!string.IsNullOrEmpty(s.m_ammoType)) t.Add("type", id, s.m_ammoType, s.m_ammoType);
                    break;
                case ItemCategory.Tools:
                    var tool = s.m_buildPieces ? "build" : s.m_skillType == Skills.SkillType.Pickaxes ? "pick" : s.m_skillType == Skills.SkillType.Fishing ? "fish" : "other";
                    t.Add("type", id, tool, "$ib_f_tool_" + tool);
                    t.Add("level", id, s.m_toolTier.ToString(), "$ib_f_level " + s.m_toolTier);
                    break;
                case ItemCategory.Food:
                    if (StageRules.FoodFocus(s.m_food, s.m_foodStamina, s.m_foodEitr) is string focus) t.Add("focus", id, focus, "$ib_f_focus_" + focus);
                    if (station != null) t.Add("station", id, station, stationName);
                    if (s.m_foodBurnTime > 0f)
                    {
                        var min = Mathf.RoundToInt(s.m_foodBurnTime / 60f);
                        t.Add("duration", id, min.ToString("D3"), min + " $ib_min");
                    }
                    break;
                case ItemCategory.Meads:
                    if (s.m_consumeStatusEffect) t.Add("effect", id, s.m_consumeStatusEffect.name, string.IsNullOrEmpty(s.m_consumeStatusEffect.m_name) ? s.m_consumeStatusEffect.name : s.m_consumeStatusEffect.m_name);
                    if (station != null) t.Add("station", id, station, stationName);
                    break;
            }
        }

        private static void PieceFacets(FacetTable t, Entry e, Piece p)
        {
            var cat = p.m_category.ToString().ToLowerInvariant();
            t.Add("section", e.Id, cat, "$ib_f_sec_" + cat);
            if (p.m_craftingStation) t.Add("station", e.Id, p.m_craftingStation.gameObject.name, p.m_craftingStation.m_name);
            if (p.m_comfort > 0) t.Add("comfort", e.Id, "yes", "$ib_f_comfort_yes");
        }

        private static void StationFacets(FacetTable t, Entry e, GameObject go)
        {
            var kind = go.GetComponent<Smelter>() ? "smelter" : go.GetComponent<Fermenter>() ? "fermenter" : go.GetComponent<CookingStation>() ? "cooking" :
                go.GetComponent<Incinerator>() ? "incinerator" : "crafting";
            t.Add("stationkind", e.Id, kind, "$ib_f_st_" + kind);
        }

        private static void Creature(FacetTable t, Entry e, Character c, ItemFact fact)
        {
            var id = e.Id;
            if (c.m_boss) t.Add("kind", id, "boss", "$ib_f_boss");
            if (fact != null && fact.Elite) t.Add("kind", id, "elite", "$ib_f_elite");
            var tame = Browser.Tameable(id, out _);
            if (tame == TameKind.Tame) t.Add("kind", id, "tame", "$ib_f_tame");
            else if (tame == TameKind.Summon) t.Add("kind", id, "summon", "$ib_from_summon");
            if (FoundInScan.Data != null && FoundInScan.Data.ByPrefab.TryGetValue(id, out var o))
            {
                foreach (var tag in FoundIn.Tags(o.Kinds)) t.Add("kind", id, tag, tag);
                foreach (var b in FoundIn.Biomes(o.Biomes)) t.Add("habitat", id, b.ToString().ToLowerInvariant(), Morgott.UI.MapScreen.MapFilterMath.NameKey(b));
            }
            var faction = c.m_faction.ToString().ToLowerInvariant();
            t.Add("faction", id, faction, "$ib_f_fac_" + faction);
        }

        // ---------------------------------------------------------------- harness dump

        private static string Json(string s) => s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        /// <summary>One JSON line per listed entry (<c>item-stage-dump</c>).</summary>
        public static List<string> Dump(BrowserIndex ix)
        {
            var lines = new List<string>();
            foreach (var e in ix.Entries.Values)
            {
                if (e.Hidden != null) continue;
                var i = Info(e.Id);
                var facets = new StringBuilder();
                foreach (var kv in Facets.Facets)
                    if (kv.Value.ByEntry.TryGetValue(e.Id, out var keys)) facets.Append(kv.Key).Append('=').Append(string.Join("|", keys)).Append(';');
                var missing = i.Stage < 0 && i.Via != StageVia.Magic ? Data?.Solver.Missing(e.Id) : null;
                lines.Add($"{{\"prefab\":{Json(e.Id)},\"type\":\"{e.Type}\",\"category\":\"{(e.Type == EntryType.Item ? e.Category.ToString() : "")}\",\"mod\":{Json(e.Mod)},\"stage\":{i.Stage},\"biome\":{i.Biome},\"afterBoss\":{(i.AfterBoss ? "true" : "false")},\"via\":\"{i.Via}\",\"kind\":{Json(i.Kind)},\"ocean\":{(i.Ocean ? "true" : "false")},\"magicFact\":{(Data != null && Data.MagicFacts.Contains(e.Id) ? "true" : "false")},\"missing\":{Json(missing)},\"facets\":{Json(facets.ToString())}}}");
            }
            return lines;
        }
    }
}
