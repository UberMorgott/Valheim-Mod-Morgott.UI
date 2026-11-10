using System;
using System.IO;
using Morgott.Core;
using UnityEngine;

namespace Morgott.UI.Api
{
    // Morgott.Core IVfx implementation (plan 5a): the small "morgott_vfx" bundle next to the DLL (unity BuildVfx), loaded on
    // the first call and kept loaded (handed-out shaders / template live as long as consumer copies may).
    public sealed class VfxProvider : IVfx, IWebVfx
    {
        public const string BundleFile = "morgott_vfx";

        private readonly string _path;
        private bool _loaded;
        private Shader _shell, _telegraph, _bossFx, _bar;
        private Material _particle, _web;

        public VfxProvider(string pluginDir) => _path = Path.Combine(pluginDir, BundleFile);

        public static VfxProvider Instance { get; set; }

        public Shader Shell() => Load()._shell;
        public Shader Telegraph() => Load()._telegraph;
        public Shader UiBossFx() => Load()._bossFx;
        public Shader UiBar() => Load()._bar;
        public Material AdditiveParticle() => Load()._particle;
        public Material Web() => Load()._web;

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
                }
            }
            _particle = bundle.LoadAsset<Material>("AdditiveParticle");
            if (_particle && (!_particle.shader || !_particle.shader.isSupported)) _particle = null;
            _web = bundle.LoadAsset<Material>("Web");
            if (_web && (!_web.shader || !_web.shader.isSupported || !_web.mainTexture)) _web = null;
            Plugin.Log.LogInfo($"[Morgott.UI] {BundleFile}: shell {(bool)_shell}, telegraph {(bool)_telegraph}, boss fx {(bool)_bossFx}, bar {(bool)_bar}, particle {(bool)_particle}, web {(bool)_web}");
            return this;
        }
    }
}
