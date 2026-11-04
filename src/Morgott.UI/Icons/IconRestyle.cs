using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Managers;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Morgott.UI.Icons
{
    // One icon style for every item, vanilla and modded (user 2026-10-19, Epic Loot item icons as the style reference):
    // replaces m_shared.m_icons[0] of each prefab listed in the paged BC7 atlas shipped next to the DLL (icons\ItemIcons.json
    // + icons\ItemIcons_NN.bc7.gz, format MorgottTweaks tools\iconstyle\ATLAS_FORMAT.md), made offline from
    // Morgott.DevTools.IconCapture renders. Independent of the theme (registered before it loads).
    internal static class IconRestyle
    {
        internal const string Folder = "icons";
        internal const string ManifestFile = "ItemIcons.json";

        // Read by the autotest (95j/95k) through reflection: prefab name -> sprite of every loaded page.
        private static readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>();

        // The icon each restyled prefab had before the swap (first one seen): Morgott.DevTools.IconCapture dumps it as
        // .orig.png, which the iconstyle alike rules read (InternalsVisibleTo).
        internal static readonly Dictionary<string, Sprite> Originals = new Dictionary<string, Sprite>();
        private static Atlas _atlas;
        private static string _dir;
        private static Texture2D[] _pages;
        private static bool[] _pageTried;

        // Pages gunzipped ahead on worker threads, index = page; null = read on the main thread when needed.
        private static Task<NativeArray<byte>>[] _prefetch;

        internal static void Register(ConfigFile config, Harmony harmony)
        {
            var enabled = config.Bind("IconRestyle", "Enabled", true,
                "Show the restyled item icons (one style for vanilla and modded items) for every item the shipped " +
                "icon atlas covers; other items keep their own icon. Takes effect on the next game launch.");
            if (!enabled.Value || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !Parse()) return;
            // Jotunn adds mod items (Consumables, Epic Loot gems, ...) in its ObjectDB postfixes, then raises this.
            ItemManager.OnItemsRegistered += () =>
            {
                Apply(ObjectDB.instance);
                // Every mod item of this ObjectDB is registered now: pages still untouched hold only absent items.
                DropPrefetched();
            };
            var postfix = new HarmonyMethod(typeof(IconRestyle), nameof(ObjectDbPostfix)) { priority = Priority.Last };
            harmony.Patch(AccessTools.Method(typeof(ObjectDB), nameof(ObjectDB.Awake)), postfix: postfix);
            harmony.Patch(AccessTools.Method(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB)), postfix: postfix);
            // Awake runs before the main menu's ObjectDB.Awake (the first Apply): gunzip every page (15.6 MB gzip ->
            // 46 MB BC7) on worker threads meanwhile, so the main thread only uploads.
            _prefetch = new Task<NativeArray<byte>>[_atlas.Pages.Count];
            for (var i = 0; i < _prefetch.Length; i++)
            {
                var path = Path.Combine(_dir, _atlas.Pages[i].File);
                var length = _atlas.Pages[i].Bytes;
                _prefetch[i] = Task.Run(() => ReadGzipNative(path, length));
            }
        }

        private static void ObjectDbPostfix(ObjectDB __instance) => Apply(__instance);

        // Releases prefetched pages no Apply took; a later Apply that needs one reads it on demand.
        private static void DropPrefetched()
        {
            if (_prefetch == null) return;
            foreach (var task in _prefetch)
                task?.ContinueWith(t =>
                {
                    if (t.Status == TaskStatus.RanToCompletion) t.Result.Dispose();
                }, TaskScheduler.Default);
            _prefetch = null;
        }

        // Loads every not yet loaded page that holds at least one prefab db (or ZNetScene) has, then sets the restyled
        // icon on every listed prefab; idempotent. Pages of absent mods stay on disk.
        private static void Apply(ObjectDB db)
        {
            if (!db) return;
            var watch = Stopwatch.StartNew();
            var loadedNow = 0;
            foreach (var kv in _atlas.Items)
            {
                var page = kv.Value.Page;
                if (_pageTried[page] || !Exists(db, kv.Key)) continue;
                if (LoadPage(page)) loadedNow++;
            }
            var loadMs = watch.Elapsed.TotalMilliseconds;
            var set = 0;
            foreach (var prefab in db.m_items)
                if (prefab && Set(prefab)) set++;
            // ZNetScene's prefab is normally the same GameObject; a mod may register a separate copy there.
            if (ZNetScene.instance)
                foreach (var name in _sprites.Keys)
                {
                    var other = ZNetScene.instance.GetPrefab(name);
                    if (other && db.GetItemPrefab(name) != other) Set(other);
                }
            var loaded = 0;
            long bytes = 0;
            for (var i = 0; i < _pages.Length; i++)
                if (_pages[i])
                {
                    loaded++;
                    bytes += _atlas.Pages[i].Bytes;
                }
            Plugin.Log.LogInfo($"[IconRestyle] {set} item icon(s) restyled ({_sprites.Count} loaded of " +
                               $"{_atlas.Items.Count} in atlas); pages {loaded}/{_atlas.Pages.Count} loaded (" +
                               $"{bytes / 1048576.0:0.0} MiB BC7 in VRAM), {loadedNow} now in {loadMs:0.0} ms, " +
                               $"apply {watch.Elapsed.TotalMilliseconds:0.0} ms");
        }

        private static bool Exists(ObjectDB db, string name) =>
            db.GetItemPrefab(name) || (ZNetScene.instance && ZNetScene.instance.GetPrefab(name));

        private static bool Set(GameObject prefab)
        {
            if (!_sprites.TryGetValue(prefab.name, out var sprite)) return false;
            var drop = prefab.GetComponent<ItemDrop>();
            if (!drop) return false;
            var shared = drop.m_itemData.m_shared;
            var icons = shared.m_icons;
            if (icons != null && icons.Length > 0 && icons[0] == sprite) return true;
            if (icons != null && icons.Length > 0 && icons[0] && !Originals.ContainsKey(prefab.name))
                Originals[prefab.name] = icons[0];
            // New array: an icon array may be shared between items; variants (m_icons[1..]) are kept.
            var copy = icons != null && icons.Length > 0 ? (Sprite[])icons.Clone() : new Sprite[1];
            copy[0] = sprite;
            shared.m_icons = copy;
            return true;
        }

        // true = a non-empty atlas the GPU can show.
        private static bool Parse()
        {
            _dir = Path.Combine(Path.GetDirectoryName(typeof(IconRestyle).Assembly.Location) ?? "", Folder);
            var path = Path.Combine(_dir, ManifestFile);
            if (!File.Exists(path))
            {
                Plugin.Log.LogWarning($"[IconRestyle] {path} missing, icons unchanged.");
                return false;
            }
            Atlas atlas;
            string error;
            try
            {
                // A bad number (FormatException / OverflowException) or an unreadable file must not stop Plugin.Awake.
                if (!Atlas.TryParse(File.ReadAllText(path), out atlas, out error)) error ??= "invalid";
            }
            catch (Exception e)
            {
                atlas = null;
                error = e.Message;
            }
            if (atlas == null)
            {
                Plugin.Log.LogError($"[IconRestyle] {ManifestFile}: {error}");
                return false;
            }
            if (atlas.Items.Count == 0) return false;
            if (!SystemInfo.SupportsTextureFormat(TextureFormat.BC7))
            {
                Plugin.Log.LogWarning("[IconRestyle] the GPU has no BC7 support, icons unchanged.");
                return false;
            }
            _atlas = atlas;
            _pages = new Texture2D[atlas.Pages.Count];
            _pageTried = new bool[atlas.Pages.Count];
            return true;
        }

        // One page: gzip -> raw BC7 with every mip level (Unity layout, level 0 first, bottom row first) -> a
        // non-readable texture (Apply(false, true) drops the CPU copy), then one sprite per cell of the page.
        private static bool LoadPage(int index)
        {
            _pageTried[index] = true;
            var page = _atlas.Pages[index];
            NativeArray<byte> raw;
            try
            {
                raw = TakePage(index, page);
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException)
            {
                Plugin.Log.LogError($"[IconRestyle] page {page.File}: {e.Message}");
                return false;
            }
            Texture2D tex;
            try
            {
                tex = new Texture2D(page.Width, page.Height, TextureFormat.BC7, _atlas.Mips, false)
                {
                    name = "UI_ItemIcons_" + index,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Trilinear,
                    anisoLevel = 0,
                    // The game's texture quality (global mipmap limit) must not drop level 0: the CPU copy is gone.
                    ignoreMipmapLimit = true
                };
                tex.LoadRawTextureData(raw);
            }
            finally
            {
                raw.Dispose();
            }
            tex.Apply(false, true);
            _pages[index] = tex;
            var stride = _atlas.CellSize + 2 * _atlas.Gutter;
            foreach (var kv in _atlas.Items)
            {
                if (kv.Value.Page != index) continue;
                var col = kv.Value.Slot % _atlas.Columns;
                var row = kv.Value.Slot / _atlas.Columns;
                // Manifest rows count from the top; Unity rects from the bottom.
                var rect = new Rect(col * stride + _atlas.Gutter, page.Height - row * stride - _atlas.Gutter - _atlas.CellSize,
                    _atlas.CellSize, _atlas.CellSize);
                var sprite = Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                sprite.name = "UI_ItemIcon_" + kv.Key;
                _sprites[kv.Key] = sprite;
            }
            return true;
        }

        // The page's raw BC7: the prefetched buffer when there is one (waits for it), else read now.
        private static NativeArray<byte> TakePage(int index, Atlas.Page page)
        {
            var task = _prefetch?[index];
            if (task == null) return ReadGzipNative(Path.Combine(_dir, page.File), page.Bytes);
            _prefetch[index] = null;
            // Rethrows the worker's own exception (not an AggregateException), as the synchronous read would.
            return task.GetAwaiter().GetResult();
        }

        // gunzip into a native buffer through one small managed chunk (no 0.4-5.4 MB managed array per page).
        // Thread-safe (file + GZipStream + Persistent allocation only); the caller disposes.
        private static NativeArray<byte> ReadGzipNative(string path, int length)
        {
            var raw = new NativeArray<byte>(length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            try
            {
                var chunk = new byte[81920];
                using (var file = File.OpenRead(path))
                using (var gz = new GZipStream(file, CompressionMode.Decompress))
                {
                    var read = 0;
                    while (read < length)
                    {
                        var n = gz.Read(chunk, 0, Math.Min(chunk.Length, length - read));
                        if (n <= 0) throw new InvalidDataException($"{read} of {length} bytes");
                        NativeArray<byte>.Copy(chunk, 0, raw, read, n);
                        read += n;
                    }
                    if (gz.ReadByte() >= 0) throw new InvalidDataException($"more than {length} bytes");
                }
                return raw;
            }
            catch
            {
                raw.Dispose();
                throw;
            }
        }
    }

    // The atlas manifest, version 2 (written by MorgottTweaks tools\iconstyle `iconstyle.py pack`):
    // {"format": "mt-item-icons", "version": 2, "cell", "gutter", "cols", "mips", "encoding": "bc7", "compression": "gzip",
    // "pages": [{"file", "width", "height", "bytes"}, ...], "items": {"Prefab": [page, slot]}}; slots row-major from the
    // top-left of their page, stride cell + 2 * gutter.
    internal sealed class Atlas
    {
        internal const string Format = "mt-item-icons";
        internal const int Version = 2;

        internal sealed class Page
        {
            internal string File;
            internal int Width, Height, Bytes;
        }

        internal struct Entry
        {
            internal int Page, Slot;
        }

        internal int CellSize, Columns, Gutter, Mips;
        internal readonly List<Page> Pages = new List<Page>();
        internal readonly Dictionary<string, Entry> Items = new Dictionary<string, Entry>();

        // BC7 bytes of a w x h texture with mips levels (4x4 blocks of 16 bytes).
        internal static long Bc7Size(int w, int h, int mips)
        {
            long total = 0;
            for (var level = 0; level < mips; level++)
                total += (long)((Math.Max(1, w >> level) + 3) / 4) * ((Math.Max(1, h >> level) + 3) / 4) * 16;
            return total;
        }

        internal static bool TryParse(string json, out Atlas atlas, out string error)
        {
            atlas = null;
            error = null;
            if (!SimpleJson.SimpleJson.TryDeserializeObject(json, out var parsed) || !(parsed is IDictionary<string, object> root))
                return Fail("not a JSON object", out error);
            if (!Format.Equals(Get(root, "format") as string)) return Fail($"format must be \"{Format}\"", out error);
            if (Int(root, "version") != Version) return Fail($"unsupported version (want {Version})", out error);
            if (!"bc7".Equals(Get(root, "encoding") as string) || !"gzip".Equals(Get(root, "compression") as string))
                return Fail("encoding must be bc7, compression gzip", out error);
            var a = new Atlas { CellSize = Int(root, "cell"), Columns = Int(root, "cols"), Gutter = Int(root, "gutter"), Mips = Int(root, "mips") };
            if (a.CellSize <= 0 || a.Columns <= 0 || a.Gutter < 0 || a.Mips < 1)
                return Fail("cell, cols, mips must be > 0, gutter >= 0", out error);
            if (!(Get(root, "pages") is IList<object> pages)) return Fail("\"pages\" array missing", out error);
            var stride = a.CellSize + 2 * a.Gutter;
            foreach (var entry in pages)
            {
                if (!(entry is IDictionary<string, object> p)) return Fail("page is not an object", out error);
                var page = new Page { File = Get(p, "file") as string, Width = Int(p, "width"), Height = Int(p, "height"), Bytes = Int(p, "bytes") };
                if (string.IsNullOrEmpty(page.File) || page.File.IndexOfAny(new[] { '/', '\\' }) >= 0 || page.File.Contains(".."))
                    return Fail($"page {a.Pages.Count}: bad file name", out error);
                if (page.Width <= 0 || page.Height <= 0 || page.Width % 4 != 0 || page.Height % 4 != 0 || page.Width < a.Columns * stride)
                    return Fail($"page {page.File}: bad size {page.Width}x{page.Height}", out error);
                var want = Bc7Size(page.Width, page.Height, a.Mips);
                if (page.Bytes != want)
                    return Fail($"page {page.File}: {page.Bytes} bytes, BC7 {page.Width}x{page.Height} x{a.Mips} needs {want}", out error);
                a.Pages.Add(page);
            }
            if (!(Get(root, "items") is IDictionary<string, object> items)) return Fail("\"items\" object missing", out error);
            foreach (var kv in items)
            {
                if (!(kv.Value is IList<object> pair) || pair.Count != 2)
                    return Fail($"{kv.Key}: want [page, slot]", out error);
                var cell = new Entry { Page = Convert.ToInt32(pair[0]), Slot = Convert.ToInt32(pair[1]) };
                if (cell.Page < 0 || cell.Page >= a.Pages.Count || cell.Slot < 0 ||
                    cell.Slot / a.Columns * stride + stride > a.Pages[cell.Page].Height)
                    return Fail($"{kv.Key}: cell [{cell.Page}, {cell.Slot}] outside the atlas", out error);
                a.Items[kv.Key] = cell;
            }
            atlas = a;
            return true;
        }

        private static object Get(IDictionary<string, object> d, string key) => d.TryGetValue(key, out var v) ? v : null;

        private static int Int(IDictionary<string, object> d, string key) => Get(d, key) is { } v ? Convert.ToInt32(v) : 0;

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }
    }
}
