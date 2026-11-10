// Offline preview of Morgott/Web from the BUILT morgott_vfx bundle (BuildVfx first), for side-by-sides against
// docs/spider-webs-mockups (web-zone.png, cocoon.png) before any game launch. Stand-in scene: dark forest fog, trunks,
// sagging sheet cards, a ground carpet, one sheet mid-burn (sweep from its centre), a cocoon (_Mode 1) on a capsule.
// Unity.exe -batchmode -quit -projectPath unity -executeMethod RenderWeb.Render -logFile <log>   env: RSFX_OUT (dir)
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class RenderWeb
{
    const float Clock = 50f;

    public static void Render()
    {
        AssetBundle bundle = null;
        try
        {
            var outDir = Environment.GetEnvironmentVariable("RSFX_OUT") ?? throw new Exception("RSFX_OUT not set");
            Directory.CreateDirectory(outDir);
            AssetBundle.UnloadAllAssetBundles(true);
            bundle = AssetBundle.LoadFromFile(Path.GetFullPath("Build/vfx/morgott_vfx")) ?? throw new Exception("bundle missing (run BuildVfx first)");
            var web = bundle.LoadAsset<Material>("Web") ?? throw new Exception("Web material missing");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            Atmosphere(new Color(0.16f, 0.2f, 0.19f));
            var sun = new GameObject("sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.93f, 0.8f);
            sun.intensity = 0.9f;
            sun.transform.rotation = Quaternion.Euler(28f, 200f, 0f); // low, behind the webs: back-lit silk
            var cam = new GameObject("cam").AddComponent<Camera>();
            cam.renderingPath = RenderingPath.Forward;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = RenderSettings.fogColor;
            cam.fieldOfView = 55f;
            cam.farClipPlane = 200f;

            Ground();
            foreach (var x in new[] { -6f, -1.5f, 4.5f, 9f }) Trunk(new Vector3(x, 0f, 8f + (x % 3f)));
            var mats = new List<Material>();
            Material Copy(Action<Material> set) { var m = new Material(web); m.SetFloat("_Clock", Clock); set?.Invoke(m); mats.Add(m); return m; }
            var plain = Copy(null);
            Sheet("s1", plain, new Vector3(-6f, 0.3f, 8f), new Vector3(-1.5f, 0.3f, 9.5f), 4.2f, 0, 1.1f);
            Sheet("s2", plain, new Vector3(-1.5f, 1.2f, 9.5f), new Vector3(4.5f, 1.4f, 8f), 3.6f, 1, 0.8f);
            Sheet("s3", plain, new Vector3(-6f, 3.5f, 8f), new Vector3(-1.5f, 4.5f, 9.5f), 2.2f, 0, 0.6f);
            Sheet("s4", plain, new Vector3(4.5f, 2.6f, 8f), new Vector3(9f, 3.4f, 11f), 2.6f, 0, 0.7f);
            Carpet("c1", plain, new Vector3(1f, 0.04f, 6f), 9f, 6f);
            // Burning sheet: front 0.3 s from its centre at 20 m/s, sheet-local coords in metres.
            var burning = Copy(m =>
            {
                m.SetFloat("_BurnStart", Clock - 0.15f);
                var st = new Vector4[32];
                st[0] = new Vector4(0f, 0f, 2.7f, 1.6f);
                m.SetVectorArray("_SheetState", st);
            });
            Sheet("burn", burning, new Vector3(4.5f, 0.2f, 8f), new Vector3(9f, 0.2f, 11f), 3.2f, 0, 0.9f);
            Shot(cam, new Vector3(1.5f, 1.7f, -1.5f), new Vector3(1.5f, 1.4f, 9f), Path.Combine(outDir, "web-zone.png"));

            // Cocoon: player stand-in capsule + wrap capsule, close-up.
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.transform.position = new Vector3(0f, 1f, 3f);
            body.transform.localScale = new Vector3(0.75f, 1f, 0.75f);
            body.GetComponent<Renderer>().sharedMaterial = Solid(new Color(0.24f, 0.17f, 0.12f));
            var wrap = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            UnityEngine.Object.DestroyImmediate(wrap.GetComponent<Collider>());
            wrap.transform.position = body.transform.position;
            wrap.transform.localScale = new Vector3(0.9f, 1.06f, 0.9f);
            wrap.GetComponent<Renderer>().sharedMaterial = Copy(m => { m.SetFloat("_Mode", 1f); m.SetFloat("_Opacity", 1f); });
            Shot(cam, new Vector3(1.2f, 1.5f, 0.6f), new Vector3(0f, 1f, 3f), Path.Combine(outDir, "cocoon.png"));
            Debug.Log("RenderWeb: OK " + outDir);
        }
        catch (Exception e)
        {
            Debug.LogError("RenderWeb: FAILED " + e);
            EditorApplication.Exit(1);
        }
        finally
        {
            if (bundle != null) bundle.Unload(true);
        }
    }

    static void Atmosphere(Color fog)
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.035f;
        RenderSettings.fogColor = fog;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.22f, 0.26f, 0.25f);
        var sh = new SphericalHarmonicsL2();
        sh.AddAmbientLight(new Color(0.22f, 0.26f, 0.25f));
        RenderSettings.ambientProbe = sh;
    }

    static Material Solid(Color c)
    {
        var m = new Material(Shader.Find("Standard"));
        m.color = c;
        m.SetFloat("_Glossiness", 0.1f);
        return m;
    }

    static void Ground()
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Plane);
        g.transform.localScale = new Vector3(8f, 1f, 8f);
        g.GetComponent<Renderer>().sharedMaterial = Solid(new Color(0.12f, 0.15f, 0.08f));
    }

    static void Trunk(Vector3 at)
    {
        var t = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        t.transform.position = at + Vector3.up * 6f;
        t.transform.localScale = new Vector3(0.7f, 6f, 0.7f);
        t.GetComponent<Renderer>().sharedMaterial = Solid(new Color(0.22f, 0.13f, 0.08f));
    }

    // Card a..b, height h, sag (m) at the middle; UV0 = (u, v, tile), UV1 = (sheet 0, local x, y m), COLOR.a sway.
    static void Sheet(string name, Material mat, Vector3 a, Vector3 b, float h, int tile, float sag)
    {
        const int nx = 12, ny = 6;
        var v = new List<Vector3>(); var uv = new List<Vector3>(); var uv1 = new List<Vector3>(); var col = new List<Color>(); var tri = new List<int>();
        var len = Vector3.Distance(a, b);
        for (var j = 0; j <= ny; j++)
        for (var i = 0; i <= nx; i++)
        {
            float u = i / (float)nx, w = j / (float)ny;
            var edge = Mathf.Sin(u * Mathf.PI);
            var p = Vector3.Lerp(a, b, u) + Vector3.up * (h * w - sag * edge * (1f - 0.5f * w));
            v.Add(p);
            uv.Add(new Vector3(u, w, tile));
            uv1.Add(new Vector3(0f, u * len, w * h));
            col.Add(new Color(1f, 1f, 1f, edge * Mathf.Sin(w * Mathf.PI)));
        }
        for (var j = 0; j < ny; j++)
        for (var i = 0; i < nx; i++)
        {
            int k = j * (nx + 1) + i;
            tri.AddRange(new[] { k, k + nx + 1, k + 1, k + 1, k + nx + 1, k + nx + 2 });
        }
        Mesh(name, mat, v, uv, uv1, col, tri);
    }

    static void Carpet(string name, Material mat, Vector3 c, float sx, float sz)
    {
        var v = new List<Vector3> { c + new Vector3(-sx / 2, 0, -sz / 2), c + new Vector3(sx / 2, 0, -sz / 2), c + new Vector3(-sx / 2, 0, sz / 2), c + new Vector3(sx / 2, 0, sz / 2) };
        var uv = new List<Vector3> { new(0, 0, 2), new(1, 0, 2), new(0, 1, 2), new(1, 1, 2) };
        var uv1 = new List<Vector3> { new(0, 0, 0), new(0, sx, 0), new(0, 0, sz), new(0, sx, sz) };
        var col = new List<Color> { new(1, 1, 1, 0), new(1, 1, 1, 0), new(1, 1, 1, 0), new(1, 1, 1, 0) };
        Mesh(name, mat, v, uv, uv1, col, new List<int> { 0, 2, 1, 1, 2, 3 });
    }

    static void Mesh(string name, Material mat, List<Vector3> v, List<Vector3> uv, List<Vector3> uv1, List<Color> col, List<int> tri)
    {
        var m = new Mesh { name = name };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetUVs(1, uv1); m.SetColors(col); m.SetTriangles(tri, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        var go = new GameObject(name);
        go.AddComponent<MeshFilter>().sharedMesh = m;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
    }

    static void Shot(Camera cam, Vector3 pos, Vector3 look, string file)
    {
        const int w = 1600, h = 900;
        cam.transform.position = pos;
        cam.transform.LookAt(look);
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG());
        RenderTexture.active = null;
        cam.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(rt);
        UnityEngine.Object.DestroyImmediate(tex);
    }
}
