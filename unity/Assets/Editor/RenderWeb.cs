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

            // Cocoon (= Morgott.Creatures WebCocoon.Skin + WebPiece.CocoonLook): player stand-in = two leg capsules + torso, 7 tethers
            // (two to a trunk, the rest to the ground), seen from behind / above like the user ref; noon-ish light (in-game is brighter).
            sun.intensity = 1.5f;
            sun.transform.rotation = Quaternion.Euler(50f, 200f, 0f);
            var feet = new Vector3(-3f, 0f, -3f);
            var skin = Solid(new Color(0.3f, 0.21f, 0.15f));
            void Limb(Vector3 a, Vector3 b, float r)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                c.transform.position = (a + b) * 0.5f;
                c.transform.up = (b - a).normalized;
                c.transform.localScale = new Vector3(r * 2f, (b - a).magnitude * 0.5f + r, r * 2f);
                c.GetComponent<Renderer>().sharedMaterial = skin;
            }
            var legs = new[]
            {
                new CocoonLeg { Hip = feet + new Vector3(-0.11f, 0.95f, 0f), Knee = feet + new Vector3(-0.13f, 0.5f, 0.04f), Ankle = feet + new Vector3(-0.14f, 0.09f, 0f), Thigh = 1, Shin = 2, Foot = 3 },
                new CocoonLeg { Hip = feet + new Vector3(0.11f, 0.95f, 0f), Knee = feet + new Vector3(0.14f, 0.5f, -0.03f), Ankle = feet + new Vector3(0.16f, 0.09f, 0.02f), Thigh = 4, Shin = 5, Foot = 6 },
            };
            foreach (var l in legs) { Limb(l.Hip, l.Knee, 0.09f); Limb(l.Knee, l.Ankle, 0.065f); }
            Limb(feet + Vector3.up * 1.0f, feet + Vector3.up * 1.5f, 0.2f);
            Trunk(feet + new Vector3(-2.6f, 0f, 1.2f));
            var ends = new List<Vector3> { new(-2.25f, 0.5f, 1.05f), new(2.2f, 0.03f, 0.9f), new(1.4f, 0.03f, -1.6f), new(-1.5f, 0.03f, -1.4f), new(0.3f, 0.03f, 2.3f), new(-2.1f, 0.03f, 0.6f), new(1.9f, 0.03f, 1.9f) };
            var look = new GameObject("cocoon");
            look.AddComponent<MeshFilter>().sharedMesh = CocoonLook(legs, 0, 0f, ends.ConvertAll(e => feet + e), 7, 12345);
            var lr = look.AddComponent<MeshRenderer>();
            lr.sharedMaterial = Copy(m => { m.SetFloat("_WebGain", 3f); m.SetFloat("_Opacity", 0.6f); m.SetFloat("_DewSparkle", 0f); });
            lr.shadowCastingMode = ShadowCastingMode.Off;
            Shot(cam, feet + new Vector3(0.6f, 2.3f, -2.6f), feet + new Vector3(0f, 0.5f, 0.3f), Path.Combine(outDir, "cocoon.png"));
            Shot(cam, feet + new Vector3(2.2f, 1.2f, -0.6f), feet + new Vector3(0f, 0.55f, 0f), Path.Combine(outDir, "cocoon-side.png"));

            // Skirt (= WebPiece.Skirt): trunk -> ground veils + base mat, foot 2.8 m out.
            var trunkAt = new Vector3(-6f, 0f, 14f);
            Trunk(trunkAt);
            var sk = new GameObject("skirt");
            sk.transform.position = trunkAt;
            sk.transform.rotation = Quaternion.LookRotation(new Vector3(0.6f, 0f, -1f).normalized);
            sk.AddComponent<MeshFilter>().sharedMesh = Skirt(2.2f, new Vector3(0f, 0f, 2.8f), 1.1f, 7, (x, z) => 0f);
            var sr = sk.AddComponent<MeshRenderer>();
            sr.sharedMaterial = plain;
            sr.shadowCastingMode = ShadowCastingMode.Off;
            Shot(cam, trunkAt + new Vector3(3.5f, 2.2f, -5f), trunkAt + new Vector3(0.3f, 0.8f, -1.2f), Path.Combine(outDir, "skirt.png"));
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
        HubTL = Rect.MinMaxRect(0.006f, 0.730f, 0.268f, 0.992f), HubR = Rect.MinMaxRect(0.768f, 0.546f, 0.988f, 0.801f), Threads = Rect.MinMaxRect(0.01f, 0.6f, 0.22f, 0.68f), Net = Rect.MinMaxRect(0.725f, 0.018f, 0.974f, 0.232f);
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
    // = Morgott.Creatures Webs\WebPiece Card / CarpetCard / Skirt / CocoonLook (copy for the offline preview).
    const float TileVanilla = 4f;
        private static Mesh Card(string name, List<Vector3> verts, List<Vector4> uv0, List<Vector3> uv1, List<Color> colors, List<int> tris)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0); // double-sided shader: one face
            mesh.RecalculateBounds();
            return mesh;
        }

        internal static Mesh CarpetMesh(float size)
        {
            var (verts, uv0, uv1, colors, tris) = (new List<Vector3>(), new List<Vector4>(), new List<Vector3>(), new List<Color>(), new List<int>());
            // The vanilla hub (horizontal_web's rect) alone: a radial ground mat (a second hub made the low tier one white mass in-game).
            foreach (var (uv, turn, scale) in new[] { (HubTL, 0f, 1f) })
            {
                var rot = Quaternion.Euler(0f, turn, 0f);
                var k = verts.Count;
                foreach (var (x, z) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
                {
                    var p = rot * new Vector3(x * size * scale / 2f, 0.06f + turn * 0.0004f, z * size * scale / 2f);
                    verts.Add(p);
                    uv0.Add(new Vector4(Mathf.Lerp(uv.xMin, uv.xMax, (x + 1f) * 0.5f), Mathf.Lerp(uv.yMin, uv.yMax, (z + 1f) * 0.5f), TileVanilla, 1f)); // hubs fade out by themselves
                    uv1.Add(new Vector3(0f, p.x, p.z));
                    colors.Add(new Color(1f, 1f, 1f, 0.1f)); // a carpet barely sways
                }
                tris.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 });
            }
            return Card("CR_WebCarpet", verts, uv0, uv1, colors, tris);
        }

        // Skirt (trunk → ground, user 2026-11-11): two vanilla triangles fanning from the trunk at Height down to the splayed ground feet
        // + one long guy thread, over a vanilla hub mat around the trunk base. Yaw = splay of each card around the foot direction,
        // Top = its attach height x Height.
        internal const float SkirtMat = 1.7f; // bound of the mats around the trunk base (hit box, trigger, burn extent)
        internal static readonly (float Yaw, float Top, Rect Uv)[] SkirtVeils = { (-22f, 1f, BigTri), (24f, 0.75f, BigTri), (3f, 1.2f, Threads) };

        private static Vector3 SkirtTop(float height, float yaw) => Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, height, 0.25f); // the trunk's surface

        /// <summary>The skirt's ground mats (Codex: no repeated radial wreath): a main hub off-centre around the trunk base, a smaller
        /// hub beside it, a net patch toward the foot; frame-local centre (x, z), radius, turn, vanilla piece. Same on every peer (id).</summary>
        internal static (Vector2 At, float R, float Turn, Rect Uv)[] SkirtMats(int id)
        {
            var rng = new System.Random(id * 7919 + 17);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var side = R(-2.2f, 2.2f);
            return new[]
            {
                (new Vector2(R(-0.2f, 0.2f), R(0.1f, 0.5f)), R(0.9f, 1.25f), R(0f, 360f), HubTL),
                (new Vector2(Mathf.Sin(side), Mathf.Cos(side)) * R(0.7f, 1.1f), R(0.5f, 0.8f), R(0f, 360f), HubR),
                (new Vector2(R(-0.4f, 0.4f), R(1.2f, 1.8f)), R(0.4f, 0.7f), R(0f, 360f), Net),
            };
        }

        /// <summary>Skirt card mesh in its Frame (origin = trunk base, z = toward <paramref name="foot"/>); UV1 = (0, x, z) like a carpet.
        /// <paramref name="ground"/>(x, z) = frame-local ground height: mats and veil feet lie on the real terrain.</summary>
        internal static Mesh Skirt(float height, Vector3 foot, float halfWidth, int id, System.Func<float, float, float> ground)
        {
            var (verts, uv0, uv1, colors, tris) = (new List<Vector3>(), new List<Vector4>(), new List<Vector3>(), new List<Color>(), new List<int>());
            const int grid = 4;
            var layer = 0;
            foreach (var (at, r, turnDeg, uv) in SkirtMats(id))
            {
                var turn = Quaternion.Euler(0f, turnDeg, 0f);
                var k0 = verts.Count;
                for (var i = 0; i <= grid; i++)
                    for (var j = 0; j <= grid; j++)
                    {
                        var (a, b) = ((float)i / grid, (float)j / grid);
                        var p = turn * new Vector3((a * 2f - 1f) * r, 0f, (b * 2f - 1f) * r) + new Vector3(at.x, 0f, at.y);
                        p.y = ground(p.x, p.z) + 0.04f + 0.004f * layer; // hugs the terrain; layers apart (no z-fight)
                        verts.Add(p);
                        var rim = i == 0 || j == 0 || i == grid || j == grid;
                        uv0.Add(new Vector4(Mathf.Lerp(uv.xMin, uv.xMax, a), Mathf.Lerp(uv.yMin, uv.yMax, b), TileVanilla, uv == Net && rim ? 0f : 1f)); // hubs fade by themselves, the net tapers
                        uv1.Add(new Vector3(0f, p.x, p.z));
                        colors.Add(new Color(1f, 1f, 1f, 0.1f));
                        if (i == 0 || j == 0) continue;
                        int q = k0 + i * (grid + 1) + j, pp = q - (grid + 1);
                        tris.AddRange(new[] { pp - 1, q - 1, pp, pp, q - 1, q });
                    }
                layer++;
            }
            const int segs = 6, rows = 3;
            foreach (var (yaw, top, uv) in SkirtVeils)
            {
                var (from, to) = (SkirtTop(height * top, yaw), Quaternion.Euler(0f, yaw, 0f) * foot);
                to.y = ground(to.x, to.z) + 0.04f;
                var side = Vector3.Cross(Vector3.up, to - from).normalized;
                var thread = uv == Threads;
                var sag = 0.12f * (to - from).magnitude;
                var k0 = verts.Count;
                for (var i = 0; i <= segs; i++)
                {
                    var s = (float)i / segs; // 0 at the trunk, 1 at the foot
                    var spine = Vector3.Lerp(from, to, s) + Vector3.down * (sag * Mathf.Sin(s * Mathf.PI));
                    var half = thread ? 0.12f : Mathf.Lerp(0.15f, halfWidth, s); // fans out toward the ground
                    for (var j = 0; j <= rows; j++)
                    {
                        var w = (float)j / rows;
                        var p = spine + side * (half * (w * 2f - 1f));
                        p.y = Mathf.Max(p.y, ground(p.x, p.z) + 0.04f); // never under the ground; the foot rests on it
                        verts.Add(p);
                        var (u, v) = thread ? (Mathf.Lerp(uv.xMin, uv.xMax, s), Mathf.Lerp(uv.yMin, uv.yMax, w)) : (Mathf.Lerp(uv.xMin, uv.xMax, w), Mathf.Lerp(uv.yMax, uv.yMin, s));
                        uv0.Add(new Vector4(u, v, TileVanilla, j == 0 || j == rows || i == segs ? 0f : 1f)); // rims + the ground end taper, the trunk end stays
                        uv1.Add(new Vector3(0f, p.x, p.z));
                        colors.Add(new Color(1f, 1f, 1f, 0.6f * Mathf.Sin(s * Mathf.PI)));
                    }
                    if (i == 0) continue;
                    for (var j = 0; j < rows; j++)
                    {
                        int p0 = k0 + (i - 1) * (rows + 1) + j, q = p0 + rows + 1;
                        tris.AddRange(new[] { p0, p0 + 1, q, q, p0 + 1, q + 1 });
                    }
                }
            }
            return Card("CR_WebSkirt", verts, uv0, uv1, colors, tris);
        }

        // ---- caught-victim cocoon look (user ref docs\spider-webs-mockups\user-ref\cocoon-ref.webp, Codex 2026-11-11): the legs to the
        // waist wrapped in many separate fibrous ribbons (bundles of 1-3 narrow strips, vanilla Threads art) with dark gaps between
        // them, ~60 % around both legs, ~40 % around one, a ragged top, fibres flaring from the ankles onto the ground, + tether cords
        // (3 strips each, splayed at the end) to anchors around. Skinned: wrap vertices weigh the two nearest leg bones, tether strips
        // blend from the leg bone to a static anchor bone, so the wrap follows the attack animations and the cords keep their ends.

        /// <summary>One leg at its current pose (cocoon-mesh space) + its bone indices; a creature without feet = one axis, all = root.</summary>
        internal struct CocoonLeg
        {
            internal Vector3 Hip, Knee, Ankle;
            internal int Thigh, Shin, Foot;
        }

        private sealed class RibbonSink
        {
            internal readonly List<Vector3> Verts = new List<Vector3>();
            internal readonly List<Vector4> Uv0 = new List<Vector4>();
            internal readonly List<BoneWeight> Weights = new List<BoneWeight>();
            internal readonly List<int> Tris = new List<int>();
        }

        /// <param name="anchorBone">bone index of the static tether-end transform; <paramref name="anchors"/> are in mesh space.</param>
        internal static Mesh CocoonLook(CocoonLeg[] legs, int hipsBone, float groundY, IList<Vector3> anchors, int anchorBone, int seed)
        {
            var rng = new System.Random(seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var sink = new RibbonSink();
            var scale = Mathf.Clamp(Vector3.Distance(legs[0].Hip, legs[0].Ankle) / 0.85f, 0.4f, 3f);
            var top = groundY;
            foreach (var l in legs) top = Mathf.Max(top, l.Hip.y + 0.1f * scale);
            var hipMid = Vector3.zero;
            foreach (var l in legs) hipMid += l.Hip / legs.Length;

            // Leg axis + flesh radius (thigh 0.095, shin 0.065, ankle 0.055 m at player size) at height y.
            (Vector3 P, float Rad) Axis(CocoonLeg l, float y)
            {
                if (y >= l.Hip.y) return (new Vector3(l.Hip.x, y, l.Hip.z), 0.1f * scale);
                if (y >= l.Knee.y) { var t = Mathf.InverseLerp(l.Knee.y, l.Hip.y, y); return (Vector3.Lerp(l.Knee, l.Hip, t), Mathf.Lerp(0.07f, 0.095f, t) * scale); }
                if (y >= l.Ankle.y) { var t = Mathf.InverseLerp(l.Ankle.y, l.Knee.y, y); return (Vector3.Lerp(l.Ankle, l.Knee, t), Mathf.Lerp(0.055f, 0.065f, t) * scale); }
                return (new Vector3(l.Ankle.x, y, l.Ankle.z), 0.06f * scale);
            }
            const float off = 0.03f; // strands sit 2-5 cm off the flesh (Codex), not hanging around the legs

            // Weights: the two nearest bone segments (smooth at the knees), inverse square distance.
            var segs = new List<(Vector3 A, Vector3 B, int Bone)> { (hipMid, hipMid + Vector3.up * 0.15f, hipsBone) };
            foreach (var l in legs) segs.AddRange(new[] { (l.Hip, l.Knee, l.Thigh), (l.Knee, l.Ankle, l.Shin), (l.Ankle, l.Ankle + Vector3.down * 0.05f, l.Foot) });
            var acc = new float[Mathf.Max(hipsBone, anchorBone) + 1];
            foreach (var l in legs) acc = new float[Mathf.Max(acc.Length, Mathf.Max(l.Thigh, Mathf.Max(l.Shin, l.Foot)) + 1)];
            BoneWeight Weigh(Vector3 p, float toAnchor)
            {
                System.Array.Clear(acc, 0, acc.Length);
                foreach (var (a, b, bone) in segs)
                {
                    var ab = b - a;
                    var t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                    acc[bone] = Mathf.Max(acc[bone], 1f / ((p - (a + ab * t)).sqrMagnitude + 0.0025f));
                }
                int b0 = 0, b1 = -1;
                for (var i = 1; i < acc.Length; i++)
                    if (acc[i] > acc[b0]) b0 = i;
                for (var i = 0; i < acc.Length; i++)
                    if (i != b0 && acc[i] > 0f && (b1 < 0 || acc[i] > acc[b1])) b1 = i;
                if (toAnchor > 0f) return new BoneWeight { boneIndex0 = b0, weight0 = 1f - toAnchor, boneIndex1 = anchorBone, weight1 = toAnchor }; // nearest leg bone -> the static end
                var (w0, w1) = (acc[b0], b1 < 0 ? 0f : acc[b1]);
                return new BoneWeight { boneIndex0 = b0, weight0 = w0 / (w0 + w1), boneIndex1 = Mathf.Max(0, b1), weight1 = w1 / (w0 + w1) };
            }

            // One strip along curve(t) lying on the surface with outward normal n: 3 vertices across (rims 0, centre 1: ragged taper).
            void Strip(System.Func<float, (Vector3 P, Vector3 N)> curve, int n, float width, int bind) // bind: 0 body, 1 body -> anchor, 2 anchor
            {
                var k0 = sink.Verts.Count;
                var wob = R(0f, 6.3f);
                var band = R(Threads.yMin, Threads.yMax - 0.03f); // a thin slice of the vanilla threads: 2-5 fibres along the strip, a bundle
                for (var i = 0; i <= n; i++)
                {
                    var t = (float)i / n;
                    var (p, nrm) = curve(t);
                    var tan = curve(Mathf.Min(1f, t + 0.02f)).P - curve(Mathf.Max(0f, t - 0.02f)).P;
                    var side = Vector3.Cross(nrm, tan).normalized;
                    var w = width * (0.55f + 0.45f * Mathf.Sin(Mathf.PI * Mathf.Lerp(0.15f, 0.85f, t))) * (0.8f + 0.35f * Mathf.Sin(t * 9f + wob)); // uneven thickness
                    var weight = Weigh(p, bind == 2 ? 1f : bind == 1 ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.05f, 0.4f, t)) : 0f);
                    for (var j = 0; j < 3; j++)
                    {
                        sink.Verts.Add(p + side * (w * (j - 1)));
                        sink.Uv0.Add(new Vector4(Mathf.Lerp(Threads.xMin, Threads.xMax, t), band + 0.03f * j * 0.5f, TileVanilla,
                            j == 1 && i > 0 && i < n ? 1f : 0f));
                        sink.Weights.Add(weight);
                    }
                    if (i == 0) continue;
                    for (var j = 0; j < 2; j++)
                    {
                        int p0 = k0 + (i - 1) * 3 + j, q = p0 + 3;
                        sink.Tris.AddRange(new[] { p0, p0 + 1, q, q, p0 + 1, q + 1 });
                    }
                }
            }

            // A bundle: 1-3 strips with the same path, a few cm apart, slightly diverging.
            void Bundle(System.Func<float, float, float, (Vector3 P, Vector3 N)> wrap, float y0, float y1, float a0, float turns, int n)
            {
                var count = rng.NextDouble() < 0.15 ? 1 : rng.NextDouble() < 0.5 ? 2 : 3;
                var wide = rng.NextDouble() < 0.15; // a rare thick cord
                var ph = R(0f, 6.3f);
                for (var k = 0; k < count; k++)
                {
                    var (da, dy, lift) = (R(-0.07f, 0.07f), R(-0.02f, 0.02f), R(0f, 0.012f));
                    Strip(t =>
                    {
                        var y = Mathf.Lerp(y0, y1, t) + dy * (0.5f + t) + 0.03f * Mathf.Sin(t * 7f + ph);
                        var (p, nrm) = wrap(y, a0 + da + turns * Mathf.PI * 2f * t, lift);
                        return (p, nrm);
                    }, n, wide ? R(0.035f, 0.05f) : R(0.015f, 0.038f), 0);
                }
            }

            (Vector3 P, Vector3 N) OneLeg(CocoonLeg l, float y, float a, float lift)
            {
                var (c, rad) = Axis(l, y);
                var nrm = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                return (c + nrm * (rad + off + lift), nrm);
            }
            (Vector3 P, Vector3 N) BothLegs(float y, float a, float lift)
            {
                if (legs.Length < 2) return OneLeg(legs[0], y, a, lift + 0.02f);
                var (c0, r0) = Axis(legs[0], y);
                var (c1, r1) = Axis(legs[1], y);
                var ux = c1 - c0;
                ux.y = 0f;
                var half = ux.magnitude * 0.5f;
                ux = half > 1e-4f ? ux / (half * 2f) : Vector3.right;
                var uz = Vector3.Cross(ux, Vector3.up);
                var (ex, ez) = (half + Mathf.Max(r0, r1) + off + lift, Mathf.Max(r0, r1) + off + 0.025f + lift);
                var (ca, sa) = (Mathf.Cos(a), Mathf.Sin(a));
                return ((c0 + c1) * 0.5f + ux * (ca * ex) + uz * (sa * ez), (ux * (ca / ex) + uz * (sa / ez)).normalized);
            }

            var knee = 0f;
            foreach (var l in legs) knee += l.Knee.y / legs.Length;
            var ankle = 0f;
            foreach (var l in legs) ankle += l.Ankle.y / legs.Length;
            var dir = rng.NextDouble() < 0.5 ? 1f : -1f;
            // ~60 %: around both legs, most of them in the thigh / hip zone, the top ends ragged around the waist.
            for (var b = 0; b < 26; b++)
            {
                var high = b < 15;
                var y0 = high ? R(knee - 0.1f, top - 0.35f) : R(groundY + 0.06f, knee);
                var y1 = high ? top + R(-0.1f, 0.06f) : R(knee, top - 0.1f);
                Bundle((y, a, lift) => BothLegs(y, a, lift), y0, y1, R(0f, 6.3f), (b % 3 == 0 ? -dir : dir) * R(0.6f, 1.5f), 16);
            }
            // ~40 %: each leg alone (dark gaps between the legs show), ankle to thigh.
            foreach (var l in legs)
                for (var b = 0; b < 20 / legs.Length; b++)
                    Bundle((y, a, lift) => OneLeg(l, y, a, lift), R(groundY + 0.04f, ankle + 0.15f), R(knee - 0.1f, l.Hip.y), R(0f, 6.3f), (b % 2 == 0 ? dir : -dir) * R(0.7f, 1.6f), 12);
            // Crossing bundles on each shin (ankle .. knee, both directions: the lower legs read wrapped, Codex 2026-11-11 score 6).
            foreach (var l in legs)
                for (var b = 0; b < 12 / legs.Length; b++)
                    Bundle((y, a, lift) => OneLeg(l, y, a, lift), R(groundY + 0.04f, l.Ankle.y + 0.12f), R(l.Knee.y - 0.05f, l.Knee.y + 0.15f), R(0f, 6.3f), (b % 2 == 0 ? dir : -dir) * R(0.8f, 1.6f), 12);
            // Long diagonals around both legs, shin to thigh, alternating hand: the criss-cross of the ref.
            for (var b = 0; b < 8; b++)
                Bundle((y, a, lift) => BothLegs(y, a, lift), R(ankle + 0.1f, knee), R(knee + 0.1f, top - 0.1f), R(0f, 6.3f), (b % 2 == 0 ? dir : -dir) * R(0.45f, 0.9f), 14);
            // Loose fibres hanging from the waist line.
            for (var b = 0; b < 4; b++)
            {
                var (a0, y0, y1) = (R(0f, 6.3f), top + R(-0.05f, 0.05f), top - R(0.15f, 0.35f));
                Strip(t => BothLegs(Mathf.Lerp(y0, y1, t), a0 + 0.3f * t, 0.01f + 0.04f * t), 6, R(0.006f, 0.012f), 0);
            }
            // Fibres from the ankles flaring onto the ground.
            foreach (var l in legs)
                for (var b = 0; b < 6 / legs.Length; b++)
                {
                    var a = R(0f, 6.3f);
                    var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    var (from, _) = OneLeg(l, l.Ankle.y + R(0.05f, 0.2f), a, 0f);
                    var to = new Vector3(l.Ankle.x, groundY + 0.02f, l.Ankle.z) + outward * R(0.35f, 0.6f) * scale;
                    Strip(t => (Vector3.Lerp(from, to, t) + Vector3.up * (0.05f * Mathf.Sin(t * Mathf.PI)), Vector3.Lerp(outward, Vector3.up, t).normalized), 6, R(0.01f, 0.025f), 0);
                }
            // Tethers: per anchor 3 strips converging at both ends (spread mid-span), sagging 3-6 % of the length, from the near leg at
            // knee..hip height; at the anchor 3 short fibres splay over the ground.
            foreach (var e in anchors)
            {
                var flat = new Vector3(e.x - hipMid.x, 0f, e.z - hipMid.z);
                var outDir = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
                var leg = legs[0];
                foreach (var l in legs) if (Vector3.Dot(l.Hip - hipMid, outDir) > Vector3.Dot(leg.Hip - hipMid, outDir)) leg = l;
                var a = Mathf.Atan2(outDir.z, outDir.x);
                var (from, _) = OneLeg(leg, Mathf.Lerp(leg.Knee.y, leg.Hip.y, R(0.05f, 0.8f)), a, 0.005f);
                var len = Vector3.Distance(from, e);
                var sag = R(0.03f, 0.06f) * len;
                var side = Vector3.Cross(Vector3.up, outDir);
                var width = R(0.018f, 0.03f);
                var cuffY = from.y;
                Bundle((y, a2, lift) => OneLeg(leg, y, a2, lift), cuffY - 0.06f, cuffY + 0.06f, a - 1.2f, 1.1f * dir, 10); // the cord's cuff on the leg: a readable attachment
                for (var k = 0; k < 3; k++)
                {
                    var (spread, rise) = ((k - 1) * R(0.03f, 0.06f), R(-0.03f, 0.03f));
                    Strip(t =>
                    {
                        var s = Mathf.Sin(t * Mathf.PI);
                        var p = Vector3.Lerp(from, e, t) + Vector3.down * (sag * s) + side * (spread * (s + 2.5f * Mathf.Max(0f, t - 0.75f))) + Vector3.up * (rise * s); // fans out at the anchor
                        return (p, Vector3.Cross(side, (e - from).normalized).normalized);
                    }, 10, width * (k == 1 ? 1f : 0.7f), 1);
                }
                for (var k = 0; k < 5; k++)
                {
                    var d = Quaternion.Euler(0f, R(-50f, 50f), 0f) * outDir;
                    var end = e + d * R(0.4f, 0.8f);
                    end.y = e.y - 0.02f;
                    Strip(t => (Vector3.Lerp(e, end, t), Vector3.up), 4, R(0.008f, 0.016f), 2);
                }
            }

            var mesh = new Mesh { name = "CR_WebCocoon" };
            mesh.SetVertices(sink.Verts);
            mesh.SetUVs(0, sink.Uv0);
            var (uv1, colors) = (new List<Vector3>(), new List<Color>());
            foreach (var _ in sink.Verts)
            {
                uv1.Add(Vector3.zero);
                colors.Add(Color.white); // alpha 1: the additive fallback reads it; Morgott/Web sway is off by _WindAmp 0 (WebCocoon)
            }
            mesh.SetUVs(1, uv1);
            mesh.SetColors(colors);
            mesh.SetTriangles(sink.Tris, 0);
            mesh.boneWeights = sink.Weights.ToArray();
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
