// Offline preview of Morgott/Web from the BUILT morgott_vfx bundle (BuildVfx first), for side-by-sides against
// docs/spider-webs-mockups (web-zone.png, cocoon.png) before any game launch. Stand-in scene: dark forest fog, trunks,
// sagging sheet cards, a ground carpet, one sheet mid-burn (sweep from its centre), a cocoon (_Mode 1) on a capsule.
// Unity.exe -batchmode -quit -projectPath unity -executeMethod RenderWeb.Render -logFile <log>   env: RSFX_OUT (dir),
// RSFX_VANILLA (dir with the game's fi_village_webs.png + tunnelweb_d.png, UnityPy dump: at runtime VfxProvider binds them).
// Sheets / carpet mirror Morgott.Creatures Webs\WebPiece.Sheet / CarpetCard (layered vanilla-art cards, tile 4).
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
            var van = Environment.GetEnvironmentVariable("RSFX_VANILLA") ?? throw new Exception("RSFX_VANILLA not set");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
            web.SetTexture("_WebTex", Png(Path.Combine(van, "fi_village_webs.png")));
            web.SetTexture("_WrapTex", Png(Path.Combine(van, "tunnelweb_d.png")));
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
            Shot(cam, new Vector3(1.5f, 1.7f, -4.5f), new Vector3(1.5f, 4f, 9f), Path.Combine(outDir, "web-zone.png")); // tall veils: frame up to ~10 m

            // Cocoon (= Morgott.Creatures WebCocoon: WebPiece.CocoonLook): player stand-in capsule, wrap feet..waist + 5 tethers
            // (two to trunks, three to the ground), seen from the side like the user ref.
            var feet = new Vector3(0f, 0f, 3f);
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.transform.position = feet + Vector3.up * 0.9f;
            body.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            body.GetComponent<Renderer>().sharedMaterial = Solid(new Color(0.24f, 0.17f, 0.12f));
            Trunk(feet + new Vector3(-2.6f, 0f, 1.2f));
            var ends = new List<Vector3> { new(-2.25f, 0.5f, 1.05f), new(2.2f, 0.03f, 0.9f), new(1.4f, 0.03f, -1.6f), new(-1.5f, 0.03f, -1.4f), new(0.3f, 0.03f, 2.3f), new(-2.1f, 0.03f, 0.6f), new(1.9f, 0.03f, 1.9f) };
            var look = new GameObject("cocoon");
            look.transform.position = feet;
            look.AddComponent<MeshFilter>().sharedMesh = CocoonLook(1.8f, 0.35f, ends);
            var lr = look.AddComponent<MeshRenderer>();
            lr.sharedMaterials = new[] { Copy(m => { m.SetFloat("_Mode", 1f); m.SetFloat("_Opacity", 1f); }), Copy(m => m.SetFloat("_WebGain", 5f)) };
            lr.shadowCastingMode = ShadowCastingMode.Off;
            Shot(cam, new Vector3(1.5f, 1.7f, 0.9f), new Vector3(0f, 0.6f, 3f), Path.Combine(outDir, "cocoon.png"));
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

    static Texture2D Png(string file)
    {
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
        if (!t.LoadImage(File.ReadAllBytes(file))) throw new Exception("bad png " + file);
        t.Apply(true); // rebuild the mip chain
        return t;
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

    // = Morgott.Creatures WebPiece rects / card sets (vanilla fi_village_webs pieces).
    static readonly Rect BigTri = Rect.MinMaxRect(0.038f, 0.005f, 0.645f, 0.999f), UpperTri = Rect.MinMaxRect(0.364f, 0.321f, 0.937f, 0.995f),
        HubTL = Rect.MinMaxRect(0.006f, 0.730f, 0.268f, 0.992f), HubR = Rect.MinMaxRect(0.768f, 0.546f, 0.988f, 0.801f), Threads = Rect.MinMaxRect(0.01f, 0.6f, 0.22f, 0.68f);
    static readonly (Rect Uv, bool Fan, bool Flip, float Z, float T0, float T1, float W0, float W1)[] SheetCards =
    {
        (BigTri, true, false, -0.12f, 0f, 0.8f, 0.25f, 2f), (BigTri, true, true, 0.12f, 0.2f, 1f, 0.25f, 2f), // start over the ground: in-game the low tier read as one white mass
        (UpperTri, false, false, 0f, 0f, 1f, 0.9f, 3.2f), (HubR, false, false, 0.05f, 0.25f, 0.75f, 0.3f, 1.4f),
                (Threads, false, false, 0.03f, 0f, 1f, 0.85f, 1.0f), (Threads, false, true, -0.05f, 0f, 1f, 1.55f, 1.7f), // long anchor-to-anchor guy threads tie the veils into one net
    };

    // Layered veils a..b, height h, sag (m); UV0 = (atlas u, v, 4), UV1 = (sheet 0, local x, y m), COLOR.a sway.
    static void Sheet(string name, Material mat, Vector3 a, Vector3 b, float h, int unused, float sag)
    {
        const int nx = 6, ny = 3;
        var v = new List<Vector3>(); var uv = new List<Vector4>(); var uv1 = new List<Vector3>(); var col = new List<Color>(); var tri = new List<int>();
        var len = Vector3.Distance(a, b);
        var n = Vector3.Cross((b - a).normalized, Vector3.up);
        var k = Mathf.Repeat(len * 7.31f + h * 3.17f + a.y * 1.7f, 1f); // = WebPiece.Sheet per-sheet variant
        foreach (var card in SheetCards)
        {
            var (r, fan, flip, z, t0, t1, w0, w1) = card;
            if (r == HubR && k > 0.55f) continue;
            if (w1 > 1f) w1 = 1f + (w1 - 1f) * (0.7f + 0.6f * k);
            if (fan) (t0, t1) = flip ? (0.35f - 0.3f * k, t1) : (t0, 0.65f + 0.3f * k);
            if (r == UpperTri && Mathf.Repeat(k * 7f, 1f) > 0.5f) flip = !flip;
            var k0 = v.Count;
            for (var i = 0; i <= nx; i++)
            {
                var t = i / (float)nx;
                var tt = Mathf.Lerp(t0, t1, t);
                var s = (0.3f + 0.05f * len) * Mathf.Sin(tt * Mathf.PI) * (1f + z);
                var endRim = (i == 0 && t0 > 0f) || (i == nx && t1 < 1f);
                for (var j = 0; j <= ny; j++)
                {
                    var w = j / (float)ny;
                    var y = Mathf.LerpUnclamped(0.2f + s * 0.5f, h - s, Mathf.Lerp(w0, w1, w));
                    if (r == Threads) y += (Mathf.Repeat(k * 3.3f + z * 10f, 1f) - 0.5f) * h * 1.2f * (tt - 0.5f) - s;
                    var p = Vector3.Lerp(a, b, tt) + Vector3.up * y + n * z;
                    var at = flip ? 1f - t : t;
                    var (u, vv) = fan ? (Mathf.Lerp(r.xMin, r.xMax, w), Mathf.Lerp(r.yMax, r.yMin, at)) : (Mathf.Lerp(r.xMin, r.xMax, at), Mathf.Lerp(r.yMin, r.yMax, w));
                    v.Add(p);
                    uv.Add(new Vector4(u, vv, 4f, j == 0 || j == ny || endRim ? 0f : 1f));
                    uv1.Add(new Vector3(0f, tt * len, y));
                    col.Add(new Color(1f, 1f, 1f, Mathf.Sin(tt * Mathf.PI)));
                }
                if (i == 0) continue;
                for (var j = 0; j < ny; j++)
                {
                    int p0 = k0 + (i - 1) * (ny + 1) + j, q = p0 + ny + 1;
                    tri.AddRange(new[] { p0, p0 + 1, q, q, p0 + 1, q + 1 });
                }
            }
        }
        Mesh(name, mat, v, uv, uv1, col, tri);
    }

    static void Carpet(string name, Material mat, Vector3 c, float sx, float sz)
    {
        var v = new List<Vector3>(); var uv = new List<Vector4>(); var uv1 = new List<Vector3>(); var col = new List<Color>(); var tri = new List<int>();
        foreach (var (r, turn, scale) in new[] { (HubTL, 0f, 1f) })
        {
            var rot = Quaternion.Euler(0f, turn, 0f);
            var k = v.Count;
            foreach (var (x, z) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
            {
                var p = rot * new Vector3(x * sx * scale / 2f, 0.02f + turn * 0.0004f, z * sz * scale / 2f);
                v.Add(c + p);
                uv.Add(new Vector4(Mathf.Lerp(r.xMin, r.xMax, (x + 1f) * 0.5f), Mathf.Lerp(r.yMin, r.yMax, (z + 1f) * 0.5f), 4f, 1f));
                uv1.Add(new Vector3(0f, p.x, p.z));
                col.Add(new Color(1f, 1f, 1f, 0.1f));
            }
            tri.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 });
        }
        Mesh(name, mat, v, uv, uv1, col, tri);
    }
    // = Morgott.Creatures Webs\WebPiece.CocoonLook (copy for the offline preview).
    private static readonly (float Y, float R, float Edge)[] WrapProfile =
        { (0.01f, 0.2f, 0f), (0.1f, 0.13f, 1f), (0.25f, 0.1f, 1f), (0.5f, 0.08f, 1f), (0.8f, 0.06f, 1f), (1f, 0.04f, 0f) }; // Y: 0..1 = ground (first two m-fixed) .. waist, R: m over the body radius; no ground skirt (tethers spread instead)

    static Mesh CocoonLook(float height, float radius, IList<Vector3> anchors)
    {
        var (verts, uv0, uv1, colors) = (new List<Vector3>(), new List<Vector4>(), new List<Vector3>(), new List<Color>());
        var (wrap, tether) = (new List<int>(), new List<int>());
        const int around = 16;
        var waist = 0.55f * height;
        for (var ring = 0; ring < WrapProfile.Length; ring++)
        {
        var (y, r, edge) = WrapProfile[ring];
        var h = ring < 2 ? y : y * waist;
        for (var k = 0; k <= around; k++)
        {
            var a = k * Mathf.PI * 2f / around;
            var bump = 1f + 0.1f * Mathf.Sin(a * 3f + ring * 1.7f) + 0.06f * Mathf.Sin(a * 5f - ring * 2.3f); // tangled, not a smooth shell
            verts.Add(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * ((radius + r) * bump) + Vector3.up * (h + (ring == WrapProfile.Length - 1 ? 0.12f * Mathf.Sin(k * 1.18f + 1f) + 0.07f * Mathf.Sin(k * 2.75f) : 0f))); // ragged waist line
            uv0.Add(new Vector4(0f, 0f, 0f, edge));
            uv1.Add(Vector3.zero);
            colors.Add(Color.white);
            if (ring == 0 || k == 0) continue;
            int p = verts.Count - 1, q = p - (around + 1);
            wrap.AddRange(new[] { q - 1, p - 1, q, q, p - 1, p });
        }
        }
        // Tethers: two crossed cards each, body (0.25..0.6 of the waist) to the anchor, sagging; the vanilla side piece's long
        // parallel threads (u 0.01..0.22, v 0.60..0.68) along it, 3 vertices across so the rims taper.
        const int segs = 8;
        for (var n = 0; n < anchors.Count * 2; n++) // per anchor: a main bundle + a thinner branch to a point beside it
        {
            var main = n % 2 == 0;
            var to0 = anchors[n / 2];
            var dir = new Vector3(to0.x, 0f, to0.z).normalized;
            var to = main ? to0 : to0 + Vector3.Cross(Vector3.up, dir) * (n % 4 == 1 ? 0.45f : -0.45f) + Vector3.up * 0.12f;
            var from = dir * (radius + 0.08f) + Vector3.up * (waist * (0.15f + 0.6f * ((n * 0.618f) % 1f)));
            var width = main ? 1f : 0.45f;
        var len = Vector3.Distance(from, to);
        var side = Vector3.Cross(Vector3.up, dir);
        foreach (var across in new[] { side, Vector3.Cross(dir, side).normalized })
        {
            var k0 = verts.Count;
            for (var i = 0; i <= segs; i++)
            {
            var t = (float)i / segs;
            var c = Vector3.Lerp(from, to, t) + Vector3.down * (0.12f * len * Mathf.Sin(t * Mathf.PI));
            var half = Mathf.Lerp(0.09f, 0.015f, t) * width; // tight bundle, thinning to the anchor
            for (var j = 0; j < 3; j++)
            {
                verts.Add(c + across * (half * (j - 1)));
                uv0.Add(new Vector4(Mathf.Lerp(0.01f, 0.22f, t), Mathf.Lerp(0.6f, 0.68f, j * 0.5f), 4f, j == 1 ? 1f : 0f));
                uv1.Add(new Vector3(0f, t * len, 0f)); // burn entry = the body end
                colors.Add(new Color(1f, 1f, 1f, 0.5f * Mathf.Sin(t * Mathf.PI)));
            }
            if (i == 0) continue;
            for (var j = 0; j < 2; j++)
            {
                int p = k0 + (i - 1) * 3 + j, q = p + 3;
                tether.AddRange(new[] { p, p + 1, q, q, p + 1, q + 1 });
            }
            }
        }
        }
        var mesh = new Mesh { name = "CR_WebCocoon", subMeshCount = 2 };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uv0);
        mesh.SetUVs(1, uv1);
        mesh.SetColors(colors);
        mesh.SetTriangles(wrap, 0);
        mesh.SetTriangles(tether, 1);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static void Mesh(string name, Material mat, List<Vector3> v, List<Vector4> uv, List<Vector3> uv1, List<Color> col, List<int> tri)
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
