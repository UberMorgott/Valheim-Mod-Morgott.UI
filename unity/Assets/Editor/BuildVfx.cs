// Shared gameplay / UI VFX bundle "morgott_vfx" (plan 5a, Morgott.Core IVfx): art/vfx/*.shader (Morgott/Shell,
// Morgott/Telegraph, Morgott/UI Boss FX, Morgott/UI Bar) + the AdditiveParticle template (white soft dot, Legacy
// Particles/Additive) -> unity/Build/vfx/morgott_vfx (StandaloneWindows64, LZ4), then loaded back and verified (shader
// compiled, supported, pass count) into unity/Build/vfx/morgott_vfx.contents.txt. Explicit AssetBundleBuild: asset
// bundle names stay untouched, so BuildBundles (the "runeskin" UI bundle) never picks these up.
// Batchmode: Unity.exe -batchmode -quit -projectPath unity -executeMethod BuildVfx.Build -logFile -
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class BuildVfx
{
    const string BundleName = "morgott_vfx";
    const string Dir = "Assets/Morgott/Vfx";
    const string OutDir = "Build/vfx";
    static readonly string[] Shaders = { "Morgott/Shell", "Morgott/Telegraph", "Morgott/UI Boss FX", "Morgott/UI Bar" };

    static string Src => Path.GetFullPath(Path.Combine(Application.dataPath, "../../art/vfx"));

    public static void Build()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var assets = new List<string>();
            foreach (var src in Directory.GetFiles(Src, "*.shader"))
            {
                var path = $"{Dir}/{Path.GetFileName(src)}";
                File.Copy(src, path, true);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                var sh = AssetDatabase.LoadAssetAtPath<Shader>(path) ?? throw new Exception("no shader at " + path);
                if (ShaderUtil.ShaderHasError(sh))
                    throw new Exception($"shader {sh.name} has errors: " + string.Join("; ", ShaderUtil.GetShaderMessages(sh).Select(m => m.message)));
                assets.Add(path);
            }
            assets.Add(Particle(Dot()));
            Directory.CreateDirectory(OutDir);
            var build = new[] { new AssetBundleBuild { assetBundleName = BundleName, assetNames = assets.ToArray() } };
            var res = BuildPipeline.BuildAssetBundles(OutDir, build, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,
                BuildTarget.StandaloneWindows64);
            var file = Path.Combine(OutDir, BundleName);
            if (res == null || !File.Exists(file)) throw new Exception("BuildAssetBundles failed");
            Verify(file);
        }
        catch (Exception e)
        {
            Debug.LogError("morgott_vfx: build FAILED: " + e);
            EditorApplication.Exit(1);
        }
    }

    // Same soft falloff as Vaettir's vt_dot (BossAwakening VaettirBuild.cs:79): alpha (1 - r)^2, white.
    static Texture2D Dot()
    {
        const int size = 64;
        var path = $"{Dir}/vfx_dot.png";
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
            var d = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v));
            t.SetPixel(x, y, new Color(1f, 1f, 1f, d * d));
        }
        File.WriteAllBytes(path, t.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.alphaIsTransparency = true;
        imp.mipmapEnabled = false;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // Template material: colour = 2 x _TintColor (default 0.5 grey = x1) x vertex colour x the dot.
    static string Particle(Texture2D dot)
    {
        var path = $"{Dir}/AdditiveParticle.mat";
        var shader = Shader.Find("Legacy Shaders/Particles/Additive") ?? throw new Exception("Legacy Shaders/Particles/Additive missing");
        AssetDatabase.DeleteAsset(path);
        var m = new Material(shader) { name = "AdditiveParticle", mainTexture = dot };
        AssetDatabase.CreateAsset(m, path);
        AssetDatabase.SaveAssets();
        return path;
    }

    static void Verify(string file)
    {
        AssetBundle.UnloadAllAssetBundles(true);
        var ab = AssetBundle.LoadFromFile(file) ?? throw new Exception("cannot load " + file);
        try
        {
            var sb = new StringBuilder($"{Path.GetFullPath(file)} {new FileInfo(file).Length} bytes, Unity {Application.unityVersion}, StandaloneWindows64\n");
            var all = ab.LoadAllAssets();
            var shaders = all.OfType<Shader>().ToList();
            foreach (var name in Shaders)
            {
                var sh = shaders.FirstOrDefault(s => s.name == name) ?? throw new Exception("bundle lacks " + name);
                if (!sh.isSupported) throw new Exception(name + " not supported");
                sb.Append($"Shader {sh.name}: supported {sh.isSupported}, passes {sh.passCount}\n");
            }
            var mat = all.OfType<Material>().FirstOrDefault(m => m.name == "AdditiveParticle") ?? throw new Exception("bundle lacks AdditiveParticle");
            sb.Append($"Material {mat.name}: shader {mat.shader?.name}, mainTexture {mat.mainTexture?.name} {mat.mainTexture?.width}x{mat.mainTexture?.height}\n");
            File.WriteAllText(file + ".contents.txt", sb.ToString());
            Debug.Log("morgott_vfx: " + sb);
        }
        finally
        {
            ab.Unload(true);
        }
    }
}
