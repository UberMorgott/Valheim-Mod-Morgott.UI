using System;
using System.Collections.Generic;
using Morgott.Core;
using UnityEngine;

namespace Morgott.UI.ItemBrowser
{
    // The vanilla scan (docs\item-browser-design.md section 4): owns everything a prefab component declares. Every
    // component instance (GetComponents), ObjectDB-only items, every quality level, every PieceTable reachable from an
    // item. No icon renders here (section 5).
    internal static class VanillaScan
    {
        private static BrowserIndex _ix;
        private static Func<string, string> _mod;

        public static void Run(BrowserIndex ix, Func<string, string> mod)
        {
            _ix = ix;
            _mod = mod;
            try
            {
                var prefabs = new List<GameObject>();
                var seen = new HashSet<GameObject>(); // m_namedPrefabs repeats m_prefabs and the ObjectDB items
                foreach (var go in ObjectDB.instance.m_items) if (go && seen.Add(go)) prefabs.Add(go);
                if (ZNetScene.instance)
                {
                    foreach (var go in ZNetScene.instance.m_prefabs) if (go && seen.Add(go)) prefabs.Add(go);
                    foreach (var go in ZNetScene.instance.m_namedPrefabs.Values) if (go && seen.Add(go)) prefabs.Add(go);
                }
                // Dev prefabs (DvergerTest, Rock_destructible_test) are no source of anything; test items / pieces (CapeTest)
                // stay, hidden by the unlisted rule.
                prefabs.RemoveAll(go => SourceNames.DevPrefab(go.name) && !go.GetComponent<ItemDrop>() && !go.GetComponent<Piece>());
                foreach (var go in prefabs) Entry(go);
                foreach (var go in prefabs) Sources(go);
                foreach (var go in prefabs) ResolveName(go);
                foreach (var r in ObjectDB.instance.m_recipes) if (r && r.m_enabled && r.m_item) Recipe(r);
                var tables = new HashSet<PieceTable>();
                foreach (var go in ObjectDB.instance.m_items)
                    if (go && go.TryGetComponent<ItemDrop>(out var tool) && tool.m_itemData.m_shared.m_buildPieces is PieceTable t && tables.Add(t))
                        foreach (var p in t.m_pieces)
                            if (p && p.TryGetComponent<Piece>(out var piece))
                                Add(RelationKind.Build, go.name, Req(piece.m_resources, 1), new[] { new Stack(p.name) }, Station(piece.m_craftingStation), 0, 0);
            }
            finally { _ix = null; _mod = null; }
        }

        private static void Entry(GameObject go)
        {
            var id = go.name;
            if (_ix.Entries.ContainsKey(id)) return;
            Entry e = null;
            if (go.TryGetComponent<ItemDrop>(out var drop) && drop.m_itemData?.m_shared is ItemDrop.ItemData.SharedData s)
            {
                e = new Entry
                {
                    Type = EntryType.Item,
                    Name = s.m_name,
                    Desc = s.m_description,
                    MaxQuality = Math.Max(1, s.m_maxQuality),
                    Category = PageModel.Categorize(s.m_itemType, s.m_skillType, s.m_buildPieces, s.m_food, s.m_foodStamina, s.m_foodEitr, s.m_consumeStatusEffect)
                };
                if (s.m_icons == null || s.m_icons.Length == 0) e.Hidden = "no icon";
            }
            else if (go.TryGetComponent<Piece>(out var piece))
                e = new Entry { Type = IsStation(go) ? EntryType.Station : EntryType.Piece, Name = piece.m_name, Desc = piece.m_description, Hidden = piece.m_icon ? null : "no icon" };
            else if (go.TryGetComponent<Character>(out var c) && !(c is Player))
                e = new Entry { Type = EntryType.Creature, Name = c.m_name };
            if (e == null) return; // other prefabs enter as relation sources only (BrowserIndex.Add)
            e.Id = id;
            e.Mod = _mod(id);
            if (string.IsNullOrEmpty(e.Name)) e.Hidden = "no name";
            else if (id.StartsWith("JVLmock_", StringComparison.Ordinal)) e.Hidden = "Jotunn mock";
            _ix.Add(e);
        }

        // Shared name resolver: an entry whose token has no translation in the current language ("[enemy_hive]") takes the
        // prefab's next name that has one (Character / Piece / ItemDrop, then hover / MineRock / Container / Pickable names),
        // else its readable prefab name. Tokens vanilla never defines get a curated word in our Translations (enemy_deerwhite).
        private static void ResolveName(GameObject go)
        {
            if (!_ix.Entries.TryGetValue(go.name, out var e) || SourceNames.Translated(e.Name, Browser.Localize)) return;
            foreach (var c in go.GetComponents<Component>())
            {
                var token = c switch
                {
                    Character ch => ch.m_name,
                    Piece p => p.m_name,
                    ItemDrop d => d.m_itemData?.m_shared?.m_name,
                    HoverText h => h.m_text,
                    MineRock m => m.m_name,
                    MineRock5 m5 => m5.m_name,
                    Container ct => ct.m_name,
                    Pickable pk => pk.m_overrideName,
                    Plant pl => pl.m_name,
                    _ => null
                };
                if (SourceNames.Translated(token, Browser.Localize)) { e.Name = token; return; }
            }
            e.Name = SourceNames.Readable(go.name);
        }

        private static void Gear(GameObject[] items)
        {
            if (items != null) foreach (var g in items) if (g) _ix.NpcGear.Add(g.name);
        }

        private static void Sources(GameObject go)
        {
            var id = go.name;
            var named = false;
            void Name(string token)
            {
                if (named || string.IsNullOrEmpty(token)) return;
                named = true;
                var e = _ix.Add(new Entry { Id = id, Name = token, Type = EntryType.Other, Mod = _mod(id) });
                if (e.Type == EntryType.Other && (e.Name == id || e.Name == SourceNames.Readable(id))) e.Name = token; // entered earlier as a bare relation source
            }
            foreach (var c in go.GetComponents<Component>())
                switch (c)
                {
                    case CharacterDrop cd:
                        foreach (var d in cd.m_drops)
                            if (d.m_prefab)
                            {
                                // Base amount + chance only (user 2026-11-08: no per-star note).
                                var (min, max) = PageModel.DropRange(d.m_amountMin, d.m_amountMax);
                                Add(RelationKind.CreatureDrop, id, null, new[] { new Stack(d.m_prefab.name, min, max, Mathf.Clamp01(d.m_chance)) }, null, 0, 0,
                                    d.m_onePerPlayer ? "$ib_cond_oneperplayer" : null);
                            }
                        break;
                    case Humanoid h when !(h is Player):
                        Gear(h.m_defaultItems);
                        Gear(h.m_randomWeapon);
                        Gear(h.m_randomArmor);
                        Gear(h.m_randomShield);
                        if (h.m_randomSets != null) foreach (var set in h.m_randomSets) Gear(set?.m_items);
                        if (h.m_randomItems != null) foreach (var ri in h.m_randomItems) if (ri?.m_prefab) _ix.NpcGear.Add(ri.m_prefab.name);
                        break;
                    case DropOnDestroyed dd: Table(RelationKind.Destroy, id, dd.m_dropWhenDestroyed); break;
                    case MineRock mr: Name(mr.m_name); Table(RelationKind.Mine, id, mr.m_dropItems); break;
                    case MineRock5 mr5: Name(mr5.m_name); Table(RelationKind.Mine, id, mr5.m_dropItems); break;
                    case TreeBase tb:
                        Table(RelationKind.Tree, id, tb.m_dropWhenDestroyed);
                        Spawned(RelationKind.Tree, id, tb.m_logPrefab, tb.m_stubPrefab); // tree -> log -> wood chain
                        break;
                    case TreeLog tl:
                        Table(RelationKind.Tree, id, tl.m_dropWhenDestroyed);
                        Spawned(RelationKind.Tree, id, tl.m_subLogPrefab);
                        break;
                    case Destructible de when de.m_spawnWhenDestroyed:
                        Add(RelationKind.Destroy, id, null, new[] { new Stack(de.m_spawnWhenDestroyed.name) }, null, 0, 0);
                        break;
                    case Container ct: Name(ct.m_name); Table(RelationKind.Container, id, ct.m_defaultItems); break;
                    case Pickable pk:
                        if (pk.m_itemPrefab)
                        {
                            Name(string.IsNullOrEmpty(pk.m_overrideName) && pk.m_itemPrefab.TryGetComponent<ItemDrop>(out var pd) ? pd.m_itemData.m_shared.m_name : pk.m_overrideName);
                            Add(RelationKind.Pickable, id, null, new[] { new Stack(pk.m_itemPrefab.name, pk.m_amount, pk.m_amount) }, null, 0, 0);
                        }
                        Table(RelationKind.Pickable, id, pk.m_extraDrops);
                        break;
                    case SpawnArea sa:
                        foreach (var s in sa.m_prefabs) if (s?.m_prefab) Add(RelationKind.Spawn, id, null, new[] { new Stack(s.m_prefab.name) }, null, 0, 0);
                        break;
                    case Growup gu:
                        if (gu.m_grownPrefab) Add(RelationKind.Grow, id, null, new[] { new Stack(gu.m_grownPrefab.name) }, null, 0, 0);
                        if (gu.m_altGrownPrefabs != null)
                            foreach (var a in gu.m_altGrownPrefabs) if (a?.m_prefab) Add(RelationKind.Grow, id, null, new[] { new Stack(a.m_prefab.name, 1, 1, float.NaN) }, null, 0, 0);
                        break;
                    case Plant pl:
                        Name(pl.m_name);
                        foreach (var g in pl.m_grownPrefabs) if (g) Add(RelationKind.Grow, id, null, new[] { new Stack(g.name) }, null, 0, 0);
                        break;
                    case Trader tr:
                        foreach (var t in tr.m_items)
                            if (t?.m_prefab)
                                Add(RelationKind.Trader, id, new[] { new Stack("Coins", t.m_price, t.m_price) }, new[] { new Stack(t.m_prefab.name, t.m_stack, t.m_stack) }, null, 0, 0,
                                    string.IsNullOrEmpty(t.m_requiredGlobalKey) ? null : "$ib_cond_key " + t.m_requiredGlobalKey);
                        break;
                    case Smelter sm:
                        foreach (var cv in sm.m_conversion)
                            if (cv?.m_from && cv.m_to)
                                Add(RelationKind.Conversion, id, sm.m_fuelItem ? new[] { new Stack(cv.m_from.name), new Stack(sm.m_fuelItem.name, sm.m_fuelPerProduct, sm.m_fuelPerProduct) } : new[] { new Stack(cv.m_from.name) },
                                    new[] { new Stack(cv.m_to.name) }, id, 0, 0);
                        break;
                    case Fermenter fe:
                        foreach (var cv in fe.m_conversion)
                            if (cv?.m_from && cv.m_to) Add(RelationKind.Conversion, id, new[] { new Stack(cv.m_from.name) }, new[] { new Stack(cv.m_to.name, cv.m_producedItems, cv.m_producedItems) }, id, 0, 0);
                        break;
                    case CookingStation cs:
                        foreach (var cv in cs.m_conversion)
                            if (cv?.m_from && cv.m_to) Add(RelationKind.Conversion, id, new[] { new Stack(cv.m_from.name) }, new[] { new Stack(cv.m_to.name) }, id, 0, 0);
                        break;
                    case Incinerator inc when inc.m_conversions != null:
                        foreach (var cv in inc.m_conversions)
                        {
                            if (cv?.m_result == null || cv.m_requirements == null) continue;
                            var ins = new List<Stack>();
                            foreach (var rq in cv.m_requirements) if (rq?.m_resItem) ins.Add(new Stack(rq.m_resItem.name, rq.m_amount, rq.m_amount));
                            Add(RelationKind.Conversion, id, ins.ToArray(), new[] { new Stack(cv.m_result.name, cv.m_resultAmount, cv.m_resultAmount) }, id, 0, 0,
                                cv.m_requireOnlyOneIngredient ? "$ib_cond_anyone" : null);
                        }
                        break;
                }
            // No named component above: the hover text vanilla shows on it (HoverText.m_text, a $token), else BrowserIndex keeps
            // SourceNames.Readable(id).
            if (!named && go.TryGetComponent<HoverText>(out var hover)) Name(hover.m_text);
        }

        // One relation per table entry; a lone entry keeps the table's drop chance, a weighted table reports NaN (design 3).
        private static void Table(RelationKind kind, string source, DropTable t)
        {
            if (t?.m_drops == null) return;
            var alone = t.m_drops.Count == 1;
            var cond = alone && t.m_dropMin == 1 && t.m_dropMax == 1 ? null : t.m_oneOfEach ? $"$ib_cond_draws {t.m_dropMin}-{t.m_dropMax} $ib_cond_oneofeach" : $"$ib_cond_draws {t.m_dropMin}-{t.m_dropMax}";
            foreach (var d in t.m_drops)
                if (d.m_item)
                    Add(kind, source, null, new[] { new Stack(d.m_item.name, Math.Max(0, d.m_stackMin), Math.Max(Math.Max(0, d.m_stackMin), d.m_stackMax), alone ? Mathf.Clamp01(t.m_dropChance) : float.NaN) }, null, 0, 0, cond);
        }

        // Craft (quality 1, skipped for upgrade-only recipes) and one Upgrade per further quality level.
        private static void Recipe(Recipe r)
        {
            var max = Math.Max(1, r.m_item.m_itemData.m_shared.m_maxQuality);
            for (var q = 1; q <= max; q++)
            {
                var ins = Req(r.m_resources, q);
                if (q == 1 && r.m_noCraftOnlyUpgrade) continue; // upgrade-only recipe
                if (q > 1 && ins.Length == 0) continue;
                CraftingStation station;
                int level;
                // Warfare's ItemManager patch needs the local player here (design 5): fall back to the base fields.
                try { station = r.GetRequiredStation(q); level = r.GetRequiredStationLevel(q); }
                catch (Exception)
                {
                    // Vanilla bodies (Recipe.cs GetRequiredStation / GetRequiredStationLevel): an upgrade needs a level per quality.
                    station = r.m_craftingStation ? r.m_craftingStation : q > 1 ? r.m_repairStation : null;
                    level = PageModel.StationLevel(r.m_minStationLevel, q);
                }
                var amount = q == 1 ? Math.Max(1, r.m_amount) : 1;
                Add(q == 1 ? RelationKind.Craft : RelationKind.Upgrade, r.name, ins, new[] { new Stack(r.m_item.name, amount, amount) }, Station(station), station ? level : 0, q,
                    r.m_requireOnlyOneIngredient ? "$ib_cond_anyone" : null);
            }
        }

        private static Stack[] Req(Piece.Requirement[] req, int quality)
        {
            if (req == null) return Array.Empty<Stack>();
            var list = new List<Stack>(req.Length);
            foreach (var x in req)
            {
                var n = x?.m_resItem ? x.GetAmount(quality) : 0;
                if (n > 0) list.Add(new Stack(x.m_resItem.name, n, n));
            }
            return list.ToArray();
        }

        private static string Station(CraftingStation s) => s ? s.gameObject.name : null;

        // Stations tab: crafting stations and the converters (design 2: a station page lists what it crafts / converts).
        private static bool IsStation(GameObject go) =>
            go.GetComponent<CraftingStation>() || go.GetComponent<Smelter>() || go.GetComponent<Fermenter>() || go.GetComponent<CookingStation>() || go.GetComponent<Incinerator>();

        private static void Spawned(RelationKind kind, string source, params GameObject[] spawns)
        {
            foreach (var s in spawns) if (s) Add(kind, source, null, new[] { new Stack(s.name) }, null, 0, 0);
        }

        private static void Add(RelationKind kind, string source, Stack[] ins, Stack[] outs, string station, int level, int quality, string cond = null) =>
            _ix.Add(new Relation { Kind = kind, Source = source, Inputs = ins ?? Array.Empty<Stack>(), Outputs = outs, Station = station, StationLevel = level, Quality = quality, Conditions = cond });
    }
}
