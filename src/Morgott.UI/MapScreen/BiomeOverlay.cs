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
    // Unexplored pixels carry the fog tint (color.map.fog, [Map] FogTint: the vanilla beige clouds pulled toward the
    // mockup's cold grey), soft-edged over a 5x5 tent (MapFilterMath.FogAlpha); underwater texels are ocean. Borders: zoomed in (a texel wider than size.map.biome.line.switch screen px) the texture is
    // fill only and BorderLines draws 1-2 px screen-space lines over it; zoomed out the texture's one-texel outline is
    // already that thin. Jotunn MinimapManager overlays were checked and not used: they draw on the small map too and
    // replace the map colour (MainTex at full alpha) instead of tinting it.
    public sealed class BiomeOverlay
    {
        private const int CacheMagic = 0x4D424932; // "MBI2": underwater texels are ocean (old "MBIO" grids are rebuilt)
        public const string ImageName = "RuneSkin_BiomeOverlay";
        public const string LinesName = "RuneSkin_BiomeLines";

        public static BiomeOverlay Instance { get; private set; }

        private readonly Minimap _map;
        private readonly int _size;
        private byte[] _ids;
        private volatile bool _gridReady, _composed;
        private Color32[] _pxOutline, _pxFill;
        private byte[] _land; // explored land ids (MapFilterMath.LandIds), kept for the incremental blend
        private Texture2D _texOutline, _texFill;
        private RawImage _image;
        private BorderLines _lines;
        private Color32[] _palette;
        private Color32 _fog;
        private byte _fill, _outline;
        private bool _full = true, _dirty, _linesDirty = true;
        private float _nextApply, _nextLines;
        private Thread _worker;
        private int _themeRev = -1;
        private RectInt _window;

        // Harness: overlay textures (read back by the driver), grid / line state.
        public Texture2D Texture => _texOutline;
        public Texture2D FillTexture => _texFill;
        public bool Ready => _gridReady && _composed;
        public RawImage Image => _image;
        public bool LinesShown => _lines && _lines.isActiveAndEnabled && _lines.Count > 0;
        public int LineSegments => _lines ? _lines.Count : 0;
        public float LineScreenPx => _lines ? _lines.MeasuredPx : 0f;
        public int LineQuads => _lines ? _lines.Quads : 0;
        public Color32 Fog => _fog;

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
            MapConfig.FogTint.SettingChanged += (_, __) => Instance?.Restyle();
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
            if (Instance._texOutline) UnityEngine.Object.Destroy(Instance._texOutline);
            if (Instance._texFill) UnityEngine.Object.Destroy(Instance._texFill);
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
            var water = ZoneSystem.instance ? ZoneSystem.instance.m_waterLevel : 30f; // ZoneSystem.cs:466 default
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
                            {
                                // Same samples as Minimap.GenerateWorldMap (Minimap.cs:1967-1968): biome, then its height.
                                var wx = MapFilterMath.WorldCoord(x, size, pixel);
                                var biome = wg.GetBiome(wx, wy);
                                ids[y * size + x] = MapFilterMath.GridId(biome, wg.GetBiomeHeight(biome, wx, wy, out _), water);
                            }
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
            var on = MapConfig.BiomeOverlay?.Value ?? true;
            _fill = on ? MapFilterMath.Alpha(MapConfig.FillAlpha?.Value ?? 0.25f) : (byte)0;
            _outline = on ? MapFilterMath.Alpha(MapConfig.OutlineAlpha?.Value ?? 0.6f) : (byte)0;
            _fog = (MapConfig.FogTint?.Value ?? true) ? (Color32)ThemeTokens.GetColor("color.map.fog", new Color(0.216f, 0.306f, 0.4f, 0.65f)) : new Color32(0, 0, 0, 0);
            _linesDirty = true;
            return _palette = p;
        }

        public static Color BiomeColor(Heightmap.Biome biome) => ThemeTokens.GetColor(MapFilterMath.ColorToken(biome), Color.white);

        // Explore / ExploreOthers postfix: one newly explored pixel.
        internal void Explored(int x, int y)
        {
            if (!Ready || _full || x < 0 || y < 0 || x >= _size || y >= _size) return;
            _land[y * _size + x] = _ids[y * _size + x];
            // The soft fog edge / land blend spread FogReach texels: the whole neighbourhood is redone.
            var r = MapFilterMath.FogReach;
            for (var py = Mathf.Max(0, y - r); py <= Mathf.Min(_size - 1, y + r); py++)
                for (var px = Mathf.Max(0, x - r); px <= Mathf.Min(_size - 1, x + r); px++)
                {
                    var i = py * _size + px;
                    var seen = _map.m_explored[i] || _map.m_exploredOthers[i];
                    var a = MapFilterMath.FogAlpha(_map.m_explored, _map.m_exploredOthers, _size, px, py, _fog.a);
                    var m = seen ? MapFilterMath.LandMix(_land, _size, _palette, px, py) : default;
                    _pxOutline[i] = MapFilterMath.Pixel(_ids, _size, px, py, seen, m, a, _palette, _fill, _outline, _fog);
                    _pxFill[i] = MapFilterMath.Pixel(_ids, _size, px, py, seen, m, a, _palette, _fill, 0, _fog);
                }
            _dirty = true;
            _linesDirty = true;
        }

        // The explored bits were replaced (SetMapData, Reset, ResetSharedMapData, ExploreAll).
        internal void Invalidate()
        {
            _full = true;
            _nextApply = _nextLines = 0f; // stale lit pixels / lines must not outlive a reset
        }

        private static Texture2D NewTexture(int size, string name) =>
            new Texture2D(size, size, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };

        // Per frame while the large map is open (MinimapSkin.LargeLateUpdate).
        public void LateUpdate()
        {
            var img = _map.m_mapImageLarge;
            if (!img || !_gridReady) return;
            var on = (MapConfig.BiomeOverlay?.Value ?? true) || (MapConfig.FogTint?.Value ?? true);
            if (!_image)
            {
                if (!on) return;
                _image = new GameObject(ImageName, typeof(RectTransform)).AddComponent<RawImage>();
                _image.rectTransform.SetParent(img.rectTransform, false);
                MinimapSkin.Stretch(_image.rectTransform);
                _image.raycastTarget = false;
                _lines = new GameObject(LinesName, typeof(RectTransform)).AddComponent<BorderLines>();
                _lines.rectTransform.SetParent(img.rectTransform, false);
                MinimapSkin.Stretch(_lines.rectTransform);
                _lines.raycastTarget = false;
            }
            if (_image.gameObject.activeSelf != on) _image.gameObject.SetActive(on);
            if (!on)
            {
                if (_lines.gameObject.activeSelf) _lines.gameObject.SetActive(false);
                return;
            }
            if (_themeRev != ThemeTokens.Revision) { _themeRev = ThemeTokens.Revision; Restyle(); } // ui_reload: new colours
            // Under pins / markers / mod layers: overlay first, its lines right after it.
            if (_image.transform.GetSiblingIndex() != 0) _image.transform.SetAsFirstSibling();
            if (_lines.transform.GetSiblingIndex() != 1) _lines.transform.SetSiblingIndex(1);
            if (_full)
            {
                _full = false;
                var palette = Palette();
                _pxOutline ??= new Color32[_size * _size];
                _pxFill ??= new Color32[_size * _size];
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var fogA = new byte[_size * _size];
                _land ??= new byte[_size * _size];
                MapFilterMath.LandIds(_ids, _map.m_explored, _map.m_exploredOthers, _land);
                MapFilterMath.FogAlphas(_map.m_explored, _map.m_exploredOthers, _size, _fog.a, fogA);
                MapFilterMath.Compose(_ids, _land, _size, _map.m_explored, _map.m_exploredOthers, fogA, palette, _fill, _outline, _fog, _pxOutline, _pxFill);
                Plugin.Log.LogInfo($"[Morgott.UI] biome overlay composed in {sw.ElapsedMilliseconds} ms");
                _composed = true;
                _dirty = true;
                _linesDirty = true;
            }
            if (_dirty && Time.unscaledTime >= _nextApply)
            {
                _texOutline ??= NewTexture(_size, "Morgott.UI biome overlay");
                _texFill ??= NewTexture(_size, "Morgott.UI biome overlay (fill)");
                _texOutline.SetPixels32(_pxOutline);
                _texOutline.Apply(false);
                _texFill.SetPixels32(_pxFill);
                _texFill.Apply(false);
                _dirty = false;
                _nextApply = Time.unscaledTime + 0.5f; // exploring with the map open: at most two uploads a second
            }
            var uv = img.uvRect;
            if (_image.uvRect != uv) _image.uvRect = uv;
            UpdateLines(img, uv);
        }

        // Screen-space borders while a texel is wider than the switch size; else the texture's own one-texel outline.
        private void UpdateLines(RawImage img, Rect uv)
        {
            var scale = img.rectTransform.lossyScale.x;
            var texelPx = MapFilterMath.TexelPixels(img.rectTransform.rect.width * scale, uv.width, _size);
            var useLines = (MapConfig.BiomeOverlay?.Value ?? true) && _outline > 0 && texelPx > ThemeTokens.GetFloat("size.map.biome.line.switch", 2f);
            if (useLines)
            {
                // Texel window = the view + half a view on each side; re-read when the view leaves it, the zoom moved
                // more than a third, or exploring / colours changed (at most twice a second).
                var vx = Mathf.FloorToInt(uv.xMin * _size);
                var vy = Mathf.FloorToInt(uv.yMin * _size);
                var vw = Mathf.CeilToInt(uv.width * _size) + 1;
                var vh = Mathf.CeilToInt(uv.height * _size) + 1;
                var inside = _window.width > 0 && vx >= _window.xMin && vy >= _window.yMin && vx + vw <= _window.xMax && vy + vh <= _window.yMax
                    && vw * 1.5f >= _window.width * 0.5f;
                if (!inside || _linesDirty && Time.unscaledTime >= _nextLines)
                {
                    _window = new RectInt(vx - vw / 2, vy - vh / 2, vw * 2, vh * 2);
                    // VertexHelper.FillMesh refuses more than 65000 vertices (4 per segment): hard cap under any theme value.
                    var max = Mathf.Min(Mathf.RoundToInt(ThemeTokens.GetFloat("count.map.biome.lines.max", 12000f)), 16000);
                    useLines = _lines.Read(_ids, _size, _map.m_explored, _map.m_exploredOthers, _window, max);
                    _linesDirty = false;
                    _nextLines = Time.unscaledTime + 0.5f;
                    if (!useLines) _window = new RectInt(vx - vw / 2, vy - vh / 2, vw * 2, vh * 2); // too dense: outline texture here
                }
                else useLines = _lines.Fits;
            }
            var tex = useLines ? _texFill : _texOutline;
            if (_image.texture != tex) _image.texture = tex;
            if (_lines.gameObject.activeSelf != useLines) _lines.gameObject.SetActive(useLines);
            if (useLines) _lines.Show(uv, _size, _palette, _outline, ThemeTokens.GetFloat("size.map.biome.line", 1.5f), scale);
        }
    }

    // Biome border lines on the map image in screen pixels (UI quads along the marching-squares segments of the texel
    // window, MapFilterMath.Segments: straight or 45-degree, land-land only).
    public sealed class BorderLines : MaskableGraphic
    {
        private readonly System.Collections.Generic.List<MapFilterMath.Segment> _segs = new System.Collections.Generic.List<MapFilterMath.Segment>();
        private Rect _uv;
        private int _size;
        private Color32[] _palette;
        private byte _alpha;
        private float _half;

        public int Count => _segs.Count;
        public bool Fits { get; private set; }
        public float ScreenPx { get; private set; }
        // Harness: quads in the last built mesh and the thinnest unclipped line across, in screen px.
        public int Quads { get; private set; }
        public float MeasuredPx { get; private set; }

        public bool Read(byte[] ids, int size, System.Collections.BitArray explored, System.Collections.BitArray others, RectInt window, int max)
        {
            Fits = MapFilterMath.Segments(ids, size, explored, others, window.xMin, window.yMin, window.xMax, window.yMax, max, _segs);
            if (!Fits) _segs.Clear();
            SetVerticesDirty();
            return Fits;
        }

        public void Show(Rect uv, int size, Color32[] palette, byte alpha, float px, float lossyScale)
        {
            var half = px * 0.5f / Mathf.Max(lossyScale, 1e-6f);
            if (uv == _uv && size == _size && palette == _palette && alpha == _alpha && Mathf.Approximately(half, _half)) return;
            _uv = uv;
            _size = size;
            _palette = palette;
            _alpha = alpha;
            _half = half;
            ScreenPx = px;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Quads = 0;
            MeasuredPx = 0f;
            if (_palette == null || _size <= 0 || _uv.width <= 0f || _uv.height <= 0f) return;
            var k = rectTransform.lossyScale.x;
            var thin = float.MaxValue;
            var r = rectTransform.rect;
            var sx = r.width / (_uv.width * _size);
            var sy = r.height / (_uv.height * _size);
            var ox = r.xMin - _uv.xMin * _size * sx;
            var oy = r.yMin - _uv.yMin * _size * sy;
            foreach (var s in _segs)
            {
                var c = _palette[s.Id - 1];
                c.a = _alpha;
                var p0 = new Vector2(ox + s.X0 * sx, oy + s.Y0 * sy);
                var p1 = new Vector2(ox + s.X1 * sx, oy + s.Y1 * sy);
                // Clipped to the map image shrunk by half the line (the quads are geometry: nothing else clips them).
                var inner = new Rect(r.xMin + _half, r.yMin + _half, r.width - 2f * _half, r.height - 2f * _half);
                var clipped = !inner.Contains(p0) || !inner.Contains(p1);
                if (!MapFilterMath.Clip(inner, ref p0, ref p1)) continue;
                // Quad along the segment, widened by half the line on every side (ends overlap: diagonal / straight
                // pieces of one contour join without gaps).
                var dir = p1 - p0;
                var len = dir.magnitude;
                if (len < 1e-4f) continue;
                dir *= _half / len;
                var nrm = new Vector2(-dir.y, dir.x);
                var i = vh.currentVertCount;
                vh.AddVert(p0 - dir - nrm, c, Vector4.zero);
                vh.AddVert(p0 - dir + nrm, c, Vector4.zero);
                vh.AddVert(p1 + dir + nrm, c, Vector4.zero);
                vh.AddVert(p1 + dir - nrm, c, Vector4.zero);
                vh.AddTriangle(i, i + 1, i + 2);
                vh.AddTriangle(i + 2, i + 3, i);
                Quads++;
                if (!clipped) thin = Mathf.Min(thin, 2f * _half * k);
            }
            MeasuredPx = thin < float.MaxValue ? thin : 0f;
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
