using System;
using System.IO;
using System.Threading;
using HarmonyLib;
using Morgott.UI.Ownership;
using Morgott.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace Morgott.UI.MapScreen
{
    // "Подсветка биомов" (docs\biome-overlay-design.md): large map only, explored pixels only. A biome id grid on the
    // vanilla map grid (Minimap.GenerateWorldMap samples WorldGenerator.GetBiome per pixel centre, Minimap.cs:1950-1966)
    // is built once per world on a worker thread - vanilla calls GetBiome off the main thread itself (HeightmapBuilder
    // BuildThread, HeightmapBuilder.cs:83/159) - and cached in BepInEx\cache\Morgott.UI. The overlay RawImage is the
    // first child of m_mapImageLarge (drawn over the map, under its pin / marker children; uvRect mirrored each frame).
    // Jotunn MinimapManager overlays were checked and not used: they draw on the small map too and replace the map
    // colour (MainTex at full alpha) instead of tinting it.
    public sealed class BiomeOverlay
    {
        private const int CacheMagic = 0x4D42494F; // "MBIO"
        public const string ImageName = "RuneSkin_BiomeOverlay";

        public static BiomeOverlay Instance { get; private set; }

        private readonly Minimap _map;
        private readonly int _size;
        private byte[] _ids;
        private volatile bool _gridReady, _composed;
        private Color32[] _px;
        private Texture2D _tex;
        private RawImage _image;
        private Color32[] _palette;
        private byte _fill, _outline;
        private bool _full = true, _dirty;
        private float _nextApply;
        private Thread _worker;
        private int _themeRev = -1;

        // Harness: overlay texture (read back by the driver), grid state.
        public Texture2D Texture => _tex;
        public bool Ready => _gridReady && _composed;
        public RawImage Image => _image;

        private BiomeOverlay(Minimap map)
        {
            _map = map;
            _size = map.m_textureSize;
        }

        private static bool _hooked;

        public static void Attach(Minimap map)
        {
            Instance = new BiomeOverlay(map);
            if (_hooked || MapConfig.BiomeOverlay == null) return;
            _hooked = true;
            MapConfig.BiomeOverlay.SettingChanged += (_, __) => Instance?.Restyle();
            MapConfig.FillAlpha.SettingChanged += (_, __) => Instance?.Restyle();
            MapConfig.OutlineAlpha.SettingChanged += (_, __) => Instance?.Restyle();
        }

        // Every frame (MinimapSkin.LateUpdate): the grid starts once the world generator exists (a joining client gets
        // its world after Minimap.Start).
        public void EnsureGrid()
        {
            if (_worker == null && WorldGenerator.instance != null) StartGrid();
        }

        public static void Detach(Minimap map)
        {
            if (Instance == null || Instance._map != map) return;
            if (Instance._tex) UnityEngine.Object.Destroy(Instance._tex);
            Instance = null;
        }

        private void Restyle()
        {
            _palette = null;
            _full = true;
        }

        // ---------------------------------------------------------------- grid

        private string CachePath()
        {
            var world = WorldGenerator.instance?.m_world;
            if (world == null) return null;
            return Path.Combine(BepInEx.Paths.CachePath, "Morgott.UI", $"biomes-{world.m_seed}-{world.m_worldGenVersion}-{_size}-{_map.m_pixelSize:0.###}.bin");
        }

        private void StartGrid()
        {
            var wg = WorldGenerator.instance;
            if (wg == null || _size <= 0 || _worker != null) return;
            var path = CachePath();
            var pixel = _map.m_pixelSize;
            var size = _size;
            _worker = new Thread(() =>
            {
                try
                {
                    var ids = ReadCache(path, size);
                    if (ids == null)
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        ids = new byte[size * size];
                        for (var y = 0; y < size; y++)
                        {
                            var wy = MapFilterMath.WorldCoord(y, size, pixel);
                            for (var x = 0; x < size; x++)
                                ids[y * size + x] = MapFilterMath.Id(wg.GetBiome(MapFilterMath.WorldCoord(x, size, pixel), wy));
                        }
                        WriteCache(path, size, ids);
                        Plugin.Log.LogInfo($"[Morgott.UI] biome overlay grid {size}x{size} built in {sw.ElapsedMilliseconds} ms, cached {path}");
                    }
                    _ids = ids;
                    _gridReady = true;
                }
                catch (Exception e) { Plugin.Log.LogError($"[Morgott.UI] biome overlay grid failed (overlay stays off): {e}"); }
            })
            { IsBackground = true, Name = "Morgott.UI biome grid" };
            _worker.Start();
        }

        private static byte[] ReadCache(string path, int size)
        {
            if (path == null || !File.Exists(path)) return null;
            try
            {
                using (var r = new BinaryReader(File.OpenRead(path)))
                {
                    if (r.ReadInt32() != CacheMagic || r.ReadInt32() != size) return null;
                    var ids = r.ReadBytes(size * size);
                    return ids.Length == size * size ? ids : null;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Morgott.UI] biome overlay cache {path} unreadable, rebuilt: {e.Message}");
                return null;
            }
        }

        private static void WriteCache(string path, int size, byte[] ids)
        {
            if (path == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var tmp = path + ".tmp";
                using (var w = new BinaryWriter(File.Create(tmp)))
                {
                    w.Write(CacheMagic);
                    w.Write(size);
                    w.Write(ids);
                }
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[Morgott.UI] biome overlay cache not written ({path}): {e.Message}"); }
        }

        // ---------------------------------------------------------------- texture

        private Color32[] Palette()
        {
            if (_palette != null) return _palette;
            var p = new Color32[MapFilterMath.Biomes.Length];
            for (var i = 0; i < p.Length; i++) p[i] = BiomeColor(MapFilterMath.Biomes[i]);
            _fill = MapFilterMath.Alpha(MapConfig.FillAlpha?.Value ?? 0.3f);
            _outline = MapFilterMath.Alpha(MapConfig.OutlineAlpha?.Value ?? 0.85f);
            return _palette = p;
        }

        public static Color BiomeColor(Heightmap.Biome biome) => ThemeTokens.GetColor(MapFilterMath.ColorToken(biome), Color.white);

        // Explore / ExploreOthers postfix: one newly explored pixel.
        internal void Explored(int x, int y)
        {
            if (!Ready || _full || x < 0 || y < 0 || x >= _size || y >= _size) return;
            _px[y * _size + x] = MapFilterMath.Pixel(_ids, _size, x, y, true, _palette, _fill, _outline);
            _dirty = true;
        }

        // The explored bits were replaced (SetMapData, Reset, ResetSharedMapData).
        internal void Invalidate() => _full = true;

        // Per frame while the large map is open (MinimapSkin.LargeLateUpdate).
        public void LateUpdate()
        {
            var on = (MapConfig.BiomeOverlay?.Value ?? true) && _gridReady;
            var img = _map.m_mapImageLarge;
            if (!img) return;
            if (!_image)
            {
                if (!on) return;
                _image = new GameObject(ImageName, typeof(RectTransform)).AddComponent<RawImage>();
                _image.rectTransform.SetParent(img.rectTransform, false);
                MinimapSkin.Stretch(_image.rectTransform);
                _image.raycastTarget = false;
            }
            if (_image.gameObject.activeSelf != on) _image.gameObject.SetActive(on);
            if (!on) return;
            if (_themeRev != ThemeTokens.Revision) { _themeRev = ThemeTokens.Revision; Restyle(); } // ui_reload: new biome colours
            if (_image.transform.GetSiblingIndex() != 0) _image.transform.SetAsFirstSibling(); // under pins / markers / mod layers
            if (_full)
            {
                _full = false;
                var palette = Palette();
                _px ??= new Color32[_size * _size];
                MapFilterMath.Compose(_ids, _size, _map.m_explored, _map.m_exploredOthers, palette, _fill, _outline, _px);
                _composed = true;
                _dirty = true;
            }
            if (_dirty && Time.unscaledTime >= _nextApply)
            {
                if (!_tex)
                {
                    _tex = new Texture2D(_size, _size, TextureFormat.RGBA32, false) { name = "Morgott.UI biome overlay", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                    _image.texture = _tex;
                }
                _tex.SetPixels32(_px);
                _tex.Apply(false);
                _dirty = false;
                _nextApply = Time.unscaledTime + 0.5f; // exploring with the map open: at most two uploads a second
            }
            if (_image.uvRect != img.uvRect) _image.uvRect = img.uvRect;
        }
    }

    [HarmonyPatch]
    public static class BiomeOverlayAdapter
    {
        private static bool Prepare() => OwnershipRegistry.IsOwned(OwnershipRegistry.Minimap);

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.Explore), new[] { typeof(int), typeof(int) })]
        [HarmonyPostfix]
        private static void Explore_Postfix(int x, int y, bool __result)
        {
            if (__result) BiomeOverlay.Instance?.Explored(x, y);
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.ExploreOthers))]
        [HarmonyPostfix]
        private static void ExploreOthers_Postfix(int x, int y, bool __result)
        {
            if (__result) BiomeOverlay.Instance?.Explored(x, y);
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.ResetAndExplore), new[] { typeof(System.Collections.BitArray), typeof(System.Collections.BitArray) })]
        [HarmonyPostfix]
        private static void ResetAndExplore_Postfix() => BiomeOverlay.Instance?.Invalidate();

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.ExploreAll))]
        [HarmonyPostfix]
        private static void ExploreAll_Postfix() => BiomeOverlay.Instance?.Invalidate();

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.Reset))]
        [HarmonyPostfix]
        private static void Reset_Postfix() => BiomeOverlay.Instance?.Invalidate();

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.ResetSharedMapData))]
        [HarmonyPostfix]
        private static void ResetShared_Postfix() => BiomeOverlay.Instance?.Invalidate();

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnDestroy))]
        [HarmonyPostfix]
        private static void OnDestroy_Postfix(Minimap __instance) => BiomeOverlay.Detach(__instance);
    }
}
