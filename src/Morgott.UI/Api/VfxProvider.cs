using System;
using System.IO;
using Morgott.Core;
using UnityEngine;

namespace Morgott.UI.Api
{
    // Morgott.Core IVfx implementation (plan 5a): the small "morgott_vfx" bundle next to the DLL (unity BuildVfx), loaded on
    // the first call and kept loaded (handed-out shaders / template live as long as consumer copies may).
    public sealed class VfxProvider : IVfx, IWebVfx, IOverlayVfx, IGemVfx, ISnowVfx
    {
        public const string BundleFile = "morgott_vfx";

        private readonly string _path;
        private bool _loaded;
        private Shader _shell, _telegraph, _bossFx, _bar, _grey, _snow, _snowMask;
        private Material _particle, _web, _overlay;

        public VfxProvider(string pluginDir) => _path = Path.Combine(pluginDir, BundleFile);

        public static VfxProvider Instance { get; set; }

        public Shader Shell() => Load()._shell;
        public Shader Telegraph() => Load()._telegraph;
        public Shader UiBossFx() => Load()._bossFx;
        public Shader UiBar() => Load()._bar;
        // Morgott.UI-internal (not an IVfx member): desaturating UI/Default for greyed icons.
        public Shader UiGrey() => Load()._grey;
        public Material AdditiveParticle() => Load()._particle;
        public Material Web() => BindVanillaWeb(Load()._web);
        public Material Overlay() => Load()._overlay;
        public Shader Snow() => Load()._snow;
        public Shader SnowMask() => Load()._snowMask;

        private readonly System.Collections.Generic.Dictionary<GemLook, Material> _gem = new();

        // Boss gem looks (Core IGemVfx): tinted copies of the existing shell / overlay / particle templates, built once.
        public Material Gem(GemLook look)
        {
            if (_gem.TryGetValue(look, out var m)) return m;
            m = look switch
            {
                GemLook.FreezeShell => ShellLook(new Color(0.65f, 0.9f, 1f), 6f, new Color(0.75f, 0.95f, 1f), 0.6f),
                GemLook.RootShell => ShellLook(new Color(0.35f, 0.6f, 0.2f), 2f, new Color(0.3f, 0.5f, 0.15f), 0.25f),
                GemLook.StolenInfusion => ShellLook(new Color(1f, 0.7f, 0.3f), 5f, new Color(1f, 0.55f, 0.2f), 0.5f),
                GemLook.ChainArc => Tinted(Overlay(), new Color(0.55f, 0.7f, 1f, 0.9f), zTest: 4f),
                GemLook.LightningBurst => Tinted(AdditiveParticle(), new Color(0.6f, 0.75f, 1f, 0.8f)),
                GemLook.PoisonBurst => Tinted(AdditiveParticle(), new Color(0.45f, 0.75f, 0.15f, 0.7f)),
                _ => null,
            };
            if (m) m.name = "MT_Gem" + look;
            return _gem[look] = m; // null cached too: Load() is one attempt, a missing asset stays missing
        }

        private Material ShellLook(Color line, float surface, Color surfaceColor, float rim)
        {
            var shader = Shell();
            if (!shader) return null;
            var m = new Material(shader);
            m.SetColor("_LineColor", line);
            m.SetFloat("_Line", 0.015f);
            m.SetFloat("_LineGlow", 1.2f);
            m.SetFloat("_RimGlow", rim);
            m.SetFloat("_Surface", surface);
            m.SetColor("_SurfaceColor", surfaceColor);
            m.SetFloat("_Flash", 0f);
            return m;
        }

        private static Material Tinted(Material template, Color tint, float zTest = -1f)
        {
            if (!template) return null;
            var m = new Material(template);
            m.SetColor("_TintColor", tint);
            if (zTest >= 0f) m.SetFloat("_ZTest", zTest);
            return m;
        }

        private static readonly int WebTexId = Shader.PropertyToID("_WebTex"), WrapTexId = Shader.PropertyToID("_WrapTex");
        private bool _vanillaBound;

        // Morgott/Web tile 4 and the cocoon draw the game's own Mistlands web art: the textures of the vanilla prefabs
        // vertical_web (fi_village_webs) and tunnel_web (tunnelweb_d), set on the template before consumers copy it.
        // Needs ZNetScene (webs are built in a world); before that the template is returned unbound and the bind retried.
        private Material BindVanillaWeb(Material web)
        {
            if (!web || _vanillaBound || !ZNetScene.instance) return web;
            _vanillaBound = true;
            web.SetTexture(WebTexId, VanillaTexture("vertical_web"));
            web.SetTexture(WrapTexId, VanillaTexture("tunnel_web"));
            return web;
        }

        private static Texture VanillaTexture(string prefab)
        {
            var tex = ZNetScene.instance.GetPrefab(prefab)?.GetComponentInChildren<MeshRenderer>(true)?.sharedMaterial?.mainTexture;
            if (tex) return tex;
            Plugin.Log.LogError($"[Morgott.UI] vanilla web texture of {prefab} not found: webs drawn as solid veils");
            return Texture2D.whiteTexture; // webs are gameplay-critical: visible beats invisible
        }

        private VfxProvider Load()
        {
            if (_loaded) return this;
            _loaded = true; // one attempt: a missing or broken bundle logs once, every member then returns null
            AssetBundle bundle = null;
            try
            {
                bundle = File.Exists(_path) ? AssetBundle.LoadFromFile(_path) : null;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[Morgott.UI] {BundleFile} failed to load: {e.Message}");
            }
            if (!bundle)
            {
                Plugin.Log.LogError($"[Morgott.UI] {BundleFile} missing or unreadable: {_path} (VFX consumers use their fallbacks)");
                return this;
            }
            foreach (var s in bundle.LoadAllAssets<Shader>())
            {
                if (!s || !s.isSupported) continue;
                switch (s.name)
                {
                    case "Morgott/Shell": _shell = s; break;
                    case "Morgott/Telegraph": _telegraph = s; break;
                    case "Morgott/UI Boss FX": _bossFx = s; break;
                    case "Morgott/UI Bar": _bar = s; break;
                    case "Morgott/UI Grey": _grey = s; break;
                    case "Morgott/Snow": _snow = s; break;
                    case "Morgott/Snow Mask": _snowMask = s; break;
                }
            }
            _particle = bundle.LoadAsset<Material>("AdditiveParticle");
            if (_particle && (!_particle.shader || !_particle.shader.isSupported)) _particle = null;
            _web = bundle.LoadAsset<Material>("Web");
            if (_web && (!_web.shader || !_web.shader.isSupported || !_web.mainTexture)) _web = null;
            _overlay = bundle.LoadAsset<Material>("Overlay");
            if (_overlay && (!_overlay.shader || !_overlay.shader.isSupported)) _overlay = null;
            Plugin.Log.LogInfo($"[Morgott.UI] {BundleFile}: shell {((bool)_shell).ToString()}, telegraph {((bool)_telegraph).ToString()}, boss fx {((bool)_bossFx).ToString()}, bar {((bool)_bar).ToString()}, grey {((bool)_grey).ToString()}, particle {((bool)_particle).ToString()}, web {((bool)_web).ToString()}, overlay {((bool)_overlay).ToString()}, snow {((bool)_snow).ToString()}, snow mask {((bool)_snowMask).ToString()}");
            return this;
        }
    }
}
