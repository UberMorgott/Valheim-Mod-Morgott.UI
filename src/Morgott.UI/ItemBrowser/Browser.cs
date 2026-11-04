using System;
using System.Collections.Generic;
using System.Diagnostics;
using BepInEx.Configuration;
using Morgott.Core;
using UnityEngine;

namespace Morgott.UI.ItemBrowser
{
    // Item browser facade (docs\item-browser-design.md sections 2, 5, 7): lazy index with a fingerprint rebuild, known
    // state, favorites, icons, vanilla tooltips, cheat take, and the typed probe the harness reads (EnsureReady / Query /
    // Take; public members only, the driver binds them by reflection).
    public static class Browser
    {
        public const string FavoritesKey = "ui_ib_favorites"; // Player.m_customData, ';'-joined prefab ids

        public static ConfigEntry<bool> Cheat, CheatCreatures, KnownOnly;
        public static ConfigEntry<KeyboardShortcut> Hotkey;

        public static BrowserIndex Index { get; private set; }
        public static int Generation { get; private set; }
        public static double BuildMs { get; private set; }
        public static IReadOnlyDictionary<string, string> ProviderErrors => ItemSources.Errors;

        private static int _fingerprint;
        private static Dictionary<string, string> _mods = new Dictionary<string, string>();

        public static void Bind(ConfigFile cfg)
        {
            Cheat = cfg.Bind("ItemBrowser", "EnableCheating", true, "Right-click take in the ITEMS tab (only for admins, devcommands or worlds with no build / craft cost).");
            CheatCreatures = cfg.Bind("ItemBrowser", "CheatCreatures", true, "Take on a creature spawns it in front of you (same gate as EnableCheating).");
            KnownOnly = cfg.Bind("ItemBrowser", "KnownOnly", true, "ITEMS tab shows only what your character knows (the tab's checkbox).");
            Hotkey = cfg.Bind("ItemBrowser", "Hotkey", new KeyboardShortcut(KeyCode.H, KeyCode.LeftAlt), "Opens the inventory on the ITEMS tab.");
        }

        // ---------------------------------------------------------------- index

        /// <summary>Builds or rebuilds the index when the fingerprint changed; returns the generation (0 = no world).</summary>
        public static int EnsureReady()
        {
            if (!ObjectDB.instance || !ZNetScene.instance) return Generation;
            var fp = Fingerprint();
            if (Index != null && fp == _fingerprint) return Generation;
            var sw = Stopwatch.StartNew();
            var mods = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                foreach (var p in Jotunn.Utils.ModQuery.GetPrefabs())
                    if (p?.Prefab && p.SourceMod != null && !mods.ContainsKey(p.Prefab.name)) mods[p.Prefab.name] = p.SourceMod.GUID;
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[Morgott.UI] item browser: ModQuery failed, mods unknown: {e.Message}"); }
            var ix = new BrowserIndex();
            VanillaScan.Run(ix, id => mods.TryGetValue(id, out var g) ? g : null);
            foreach (var r in ItemSources.Collect()) ix.Add(r);
            _mods = mods;
            Index = ix; // whole swap: no half-built snapshot visible
            _fingerprint = fp;
            Generation++;
            _tips.Clear();
            BuildMs = sw.Elapsed.TotalMilliseconds;
            Plugin.Log.LogInfo($"[Morgott.UI] item browser index #{Generation}: {ix.Entries.Count} entries, {ix.Relations.Count} relations, {BuildMs:F1} ms" +
                (ItemSources.Errors.Count > 0 ? $", provider errors: {string.Join("; ", ItemSources.Errors)}" : ""));
            return Generation;
        }

        // World session + prefab / item counts + recipe content (in-place edits keep counts) + provider revision.
        private static int Fingerprint()
        {
            var h = BrowserIndex.Mix(BrowserIndex.Seed, ZNet.instance ? ZNet.instance.GetWorldUID().GetHashCode() : 0);
            h = BrowserIndex.Mix(h, ZNetScene.instance.m_namedPrefabs.Count);
            h = BrowserIndex.Mix(h, ObjectDB.instance.m_items.Count);
            h = BrowserIndex.Mix(h, ItemSources.Revision);
            foreach (var r in ObjectDB.instance.m_recipes)
            {
                if (!r) continue;
                h = BrowserIndex.Mix(BrowserIndex.Mix(h, r.name), (r.m_enabled ? 1 : 0) | (r.m_requireOnlyOneIngredient ? 2 : 0) | (r.m_noCraftOnlyUpgrade ? 4 : 0));
                h = BrowserIndex.Mix(BrowserIndex.Mix(h, r.m_item ? r.m_item.name : null), r.m_amount * 31 + r.m_minStationLevel);
                h = BrowserIndex.Mix(BrowserIndex.Mix(h, r.m_craftingStation ? r.m_craftingStation.name : null), r.m_repairStation ? r.m_repairStation.name : null);
                if (r.m_resources == null) continue;
                foreach (var q in r.m_resources)
                    if (q?.m_resItem) h = BrowserIndex.Mix(BrowserIndex.Mix(BrowserIndex.Mix(h, q.m_resItem.name), q.m_amount), q.m_amountPerLevel);
            }
            return h;
        }

        public static string Localize(string token) =>
            string.IsNullOrEmpty(token) ? token : Localization.instance != null ? Localization.instance.Localize(token) : token;

        public static string ModName(string modGuid)
        {
            if (modGuid == null) return "Valheim";
            return BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(modGuid, out var p) ? p.Metadata.Name : modGuid;
        }

        public static GameObject Prefab(string id)
        {
            var go = ZNetScene.instance ? ZNetScene.instance.GetPrefab(id) : null;
            return go ? go : ObjectDB.instance ? ObjectDB.instance.GetItemPrefab(id) : null;
        }

        // ---------------------------------------------------------------- known / favorites

        // Computed at query time from the local player's known sets (design 5); creatures / sources by what they give.
        public static bool Known(Entry e)
        {
            var p = Player.m_localPlayer;
            if (!p || e == null) return false;
            switch (e.Type)
            {
                case EntryType.Item: return p.IsMaterialKnown(e.Name) || p.IsRecipeKnown(e.Name);
                case EntryType.Piece: return p.IsRecipeKnown(e.Name);
                case EntryType.Station: return p.IsRecipeKnown(e.Name) || p.m_knownStations.ContainsKey(e.Name);
                default:
                    foreach (var r in Index.UsedIn(e.Id))
                        if (r.Source == e.Id)
                            foreach (var s in r.Outputs)
                                if (Index.Entries.TryGetValue(s.Prefab, out var o) && o.Type == EntryType.Item && (p.IsMaterialKnown(o.Name) || p.IsRecipeKnown(o.Name))) return true;
                    return false;
            }
        }

        public static HashSet<string> Favorites()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (Player.m_localPlayer && Player.m_localPlayer.m_customData.TryGetValue(FavoritesKey, out var v))
                foreach (var id in v.Split(';')) if (id.Length > 0) set.Add(id);
            return set;
        }

        public static void ToggleFavorite(string id)
        {
            if (!Player.m_localPlayer) return;
            var set = Favorites();
            if (!set.Remove(id)) set.Add(id);
            Player.m_localPlayer.m_customData[FavoritesKey] = string.Join(";", set);
        }

        // ---------------------------------------------------------------- icons (lazy renders, bounded per frame)

        public enum IconState { Ready, Pending, Missing }

        private static readonly Dictionary<string, Sprite> _rendered = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private static readonly List<string> _queue = new List<string>();
        private const int RendersPerFrame = 2;

        public static Sprite Icon(string id, out IconState state)
        {
            state = IconState.Missing;
            var go = Prefab(id);
            if (!go) return null;
            Sprite s = null;
            if (go.TryGetComponent<ItemDrop>(out var d)) s = d.m_itemData.GetIcon();
            else if (go.TryGetComponent<Piece>(out var p)) s = p.m_icon;
            if (!s && _rendered.TryGetValue(id, out s) && !s)
                return null; // render tried, nothing to draw
            if (s)
            {
                state = IconState.Ready;
                return s;
            }
            if (!_queue.Contains(id)) _queue.Add(id);
            state = IconState.Pending;
            return null;
        }

        // Called once a frame by the panel while it is up: at most RendersPerFrame Jotunn renders (synchronous each).
        public static void RenderQueued()
        {
            for (var n = 0; n < RendersPerFrame && _queue.Count > 0; n++)
            {
                var id = _queue[_queue.Count - 1];
                _queue.RemoveAt(_queue.Count - 1);
                Sprite s = null;
                try
                {
                    var go = Prefab(id);
                    if (go)
                        s = Jotunn.Managers.RenderManager.Instance.Render(new Jotunn.Managers.RenderManager.RenderRequest(go)
                        { Rotation = Jotunn.Managers.RenderManager.IsometricRotation, UseCache = true });
                }
                catch (Exception e) { Plugin.Log.LogDebug($"[Morgott.UI] item browser icon {id}: {e.Message}"); }
                _rendered[id] = s;
            }
        }

        public static void DropQueue() => _queue.Clear();

        // ---------------------------------------------------------------- tooltip (vanilla path on a copy)

        private static readonly Dictionary<string, ItemDrop.ItemData> _tips = new Dictionary<string, ItemDrop.ItemData>(StringComparer.Ordinal);

        // Topic + text: items through ItemDrop.ItemData.GetTooltip (ItemDrop.cs:859) on a cached clone, so Epic Loot and
        // our tooltip patches see a normal item; never the prefab's own ItemData.
        public static void Tooltip(Entry e, int quality, out string topic, out string text)
        {
            topic = e.Name;
            text = e.Desc ?? "";
            if (e.Type != EntryType.Item) return;
            if (!_tips.TryGetValue(e.Id, out var copy))
            {
                var go = Prefab(e.Id);
                if (!go || !go.TryGetComponent<ItemDrop>(out var d)) return;
                copy = d.m_itemData.Clone();
                copy.m_dropPrefab = go;
                _tips[e.Id] = copy;
            }
            copy.m_quality = Mathf.Clamp(quality, 1, e.MaxQuality);
            copy.m_durability = copy.GetMaxDurability();
            text = ItemDrop.ItemData.GetTooltip(copy, copy.m_quality, false, copy.m_worldLevel);
        }

        // ---------------------------------------------------------------- cheat take

        public enum TakeMode { One, Stack, Materials }

        // Admin (Jotunn), devcommands, or a world without build / craft cost (VNEI DisplayItem.WorldAllowsCheating keys
        // 24 / 25 = GlobalKeys.NoBuildCost / NoCraftCost); never plain single player.
        public static bool CanTake =>
            Cheat != null && Cheat.Value && Player.m_localPlayer &&
            // devcommands as vanilla reads it (Terminal.IsCheatsEnabled: m_cheat only counts on the server / single player).
            // Jotunn's PlayerIsAdmin is true for the local server too (single player / host): admin counts on a remote
            // server only, else plain single player could take (47i item-coverage-take-gate, 2026-11-04).
            (Console.instance && Console.instance.IsCheatsEnabled() ||
             ZNet.instance && !ZNet.instance.IsServer() && Jotunn.Managers.SynchronizationManager.Instance.PlayerIsAdmin ||
             ZoneSystem.instance && (ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoBuildCost) || ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoCraftCost)));

        /// <returns>null when taken, else why not. The gate is checked here, not only in the UI (design 7).</returns>
        public static string Take(string prefab, int quality, TakeMode mode)
        {
            if (!CanTake) return "not allowed (admin / devcommands / no-cost world only)";
            var go = Prefab(prefab);
            if (!go) return "unknown prefab " + prefab;
            var player = Player.m_localPlayer;
            if (go.TryGetComponent<ItemDrop>(out var item))
            {
                var q = Mathf.Clamp(quality, 1, Math.Max(1, item.m_itemData.m_shared.m_maxQuality));
                var recipe = ObjectDB.instance.GetRecipe(item.m_itemData);
                if (mode != TakeMode.Materials) return Give(item, 1, q, mode == TakeMode.Stack);
                if (!recipe) return "no recipe for " + prefab;
                foreach (var r in recipe.m_resources) Give(r.m_resItem, r.GetAmount(q), 1, false);
                return q > 1 ? Give(item, 1, q - 1, false) : null; // VNEI DisplayItem.CheatItem: the upgrade base comes too
            }
            if (go.TryGetComponent<Piece>(out var piece))
            {
                foreach (var r in piece.m_resources) Give(r.m_resItem, r.GetAmount(1), 1, mode == TakeMode.Stack);
                return null;
            }
            if (go.GetComponent<Character>())
            {
                if (!CheatCreatures.Value) return "creature spawn off (CheatCreatures)";
                var t = player.transform;
                UnityEngine.Object.Instantiate(go, t.position + t.forward * 2f + Vector3.up, Quaternion.identity);
                return null;
            }
            return "nothing to take for " + prefab;
        }

        private static string Give(ItemDrop item, int amount, int quality, bool stack)
        {
            if (!item) return "no item";
            var n = stack ? item.m_itemData.m_shared.m_maxStackSize : amount;
            if (n <= 0) return null;
            // Quality set at insertion (Inventory.AddItem by name), so it never merges into a stack of another quality.
            var got = Player.m_localPlayer.GetInventory().AddItem(item.gameObject.name, n, quality, 0, 0L, "", true);
            return got == null ? "inventory full" : null;
        }

        // ---------------------------------------------------------------- harness probe

        public sealed class Probe
        {
            public bool Indexed, Active, Known, Favorite;
            public string Hidden, Type, Name, Desc, Mod, Icon;
            public int MaxQuality;
            public string[] Sources; // per relation: "Kind|source|station|input,input"
        }

        public static Probe Query(string prefab)
        {
            EnsureReady();
            if (Index == null || !Index.Entries.TryGetValue(prefab, out var e)) return new Probe { Indexed = false };
            Icon(prefab, out var icon);
            var src = new List<string>();
            foreach (var r in Index.ObtainedFrom(prefab))
            {
                var ins = new List<string>();
                foreach (var s in r.Inputs) ins.Add(s.Prefab);
                src.Add($"{r.Kind}|{r.Source}|{r.Station}|{string.Join(",", ins)}");
            }
            return new Probe
            {
                Indexed = true,
                Active = e.Hidden == null,
                Hidden = e.Hidden,
                Type = e.Type.ToString(),
                Name = Localize(e.Name),
                Desc = Localize(e.Desc),
                Mod = e.Mod,
                Icon = icon.ToString(),
                MaxQuality = e.MaxQuality,
                Known = Known(e),
                Favorite = Favorites().Contains(prefab),
                Sources = src.ToArray()
            };
        }

        public static string Take(string prefab, int quality, string mode) =>
            Enum.TryParse(mode, true, out TakeMode m) ? Take(prefab, quality, m) : "mode One|Stack|Materials";
    }
}
