using HarmonyLib;
using Morgott.Core;
using Morgott.UI.Theme;
using UnityEngine;
using UnityEngine.Rendering;

namespace Morgott.UI.MapScreen
{
    // The portal's style icon (Core PortalStyle.IconKey on its ZDO) inside the arch, readable from both sides: one mesh,
    // two opposite back-culled faces, the back one with mirrored U so it does not read mirrored. Child of the vanilla
    // "TELEPORT" trigger of portal_wood / portal_stone (prefab dump: centre of the opening, its plane = local XY), no
    // collider. Shared: one mesh per icon id, one material per atlas page. Unlit (readable at night), no shadows.
    [HarmonyPatch]
    public static class PortalArchIcons
    {
        private static readonly int Wood = "portal_wood".GetStableHashCode(), Stone = "portal_stone".GetStableHashCode();
        private static readonly Mesh[] Meshes = new Mesh[PortalStyle.Icons.Length];
        private static Material _material;
        public static int Count; // live icons, harness

        private static bool Prepare() => !CoreApi.Headless && MapConfig.PortalArchIcons.Value && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

        // TeleportWorld.Awake (TeleportWorld.cs:41) sets up m_nview; a portal without a ZDO (ghost / placement) gets nothing.
        [HarmonyPatch(typeof(TeleportWorld), "Awake"), HarmonyPostfix]
        private static void Attach(TeleportWorld __instance)
        {
            var zdo = __instance.m_nview ? __instance.m_nview.GetZDO() : null;
            if (zdo == null) return;
            var prefab = zdo.GetPrefab();
            if (prefab != Wood && prefab != Stone) return; // other mods' portals: unknown arch shape
            var trigger = __instance.transform.Find("TELEPORT");
            if (!trigger) return;
            var go = new GameObject("RuneSkin_PortalIcon");
            go.transform.SetParent(trigger, false);
            go.AddComponent<MeshFilter>();
            var r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.enabled = false;
            var icon = go.AddComponent<PortalArchIcon>();
            icon.View = __instance.m_nview;
            // World quad size (art = 111/128 of it) / height above the trigger centre, divided by the trigger's scale (1.74 in both prefabs).
            var s = trigger.lossyScale.y > 0.01f ? trigger.lossyScale.y : 1f;
            var stone = prefab == Stone;
            var size = ThemeTokens.GetFloat(stone ? "size.portal.arch.stone" : "size.portal.arch.wood", stone ? 2.65f : 1.5f) / s; // icon art ~60 % of the opening (mockup_arch, Codex review of run 20261114-010013); was 1.3 / 2.3
            var up = ThemeTokens.GetFloat(stone ? "size.portal.arch.stone.up" : "size.portal.arch.wood.up", stone ? 0.2f : 0.3f) / s;
            go.transform.localPosition = new Vector3(0f, up, 0f);
            go.transform.localScale = new Vector3(size, size, 1f);
        }

        internal static Material Material()
        {
            if (_material) return _material;
            var atlas = PortalIcons.Instance.Atlas;
            // TMP/Sprite (in the game's shared assets): straight alpha, unlit, and its cull mode is a property (Sprites/Default
            // and UI/Default, the only built-ins, are Cull Off: the mirrored back face would show through).
            var shader = Shader.Find("TextMeshPro/Sprite");
            if (!atlas || !shader) return null;
            // Above the vanilla portal FX (teleport_suck / portal_sparcs: queue 3000, alpha blend, ZWrite off; portal_flame:
            // shader Transparent). At the same 3000 the camera-distance sort put the dark Black_suck billboard (z -1.04 of
            // the effect root, the back side) after the icon for a back-side viewer and blacked it out.
            _material = new Material(shader) { name = "RuneSkin PortalIcons", mainTexture = atlas, renderQueue = (int)RenderQueue.Transparent + 10 };
            _material.SetFloat("_CullMode", (float)CullMode.Back);
            _material.SetFloat("unity_GUIZTestMode", (float)CompareFunction.LessEqual); // world depth, not the UI's Always
            _material.SetColor("_Color", new Color(1f, 1f, 1f, 0.92f));
            return _material;
        }

        // Warm-gold outline glow, HDR (> 1, picked up by the game's bloom) at low opacity; the icon itself stays white.
        // ~30 % below the first cut (2.5, 1.9, 1, 0.22) and warmer: it read as a hard bright rim in the day shot.
        private static readonly Color Halo = new Color(1.75f, 1.2f, 0.55f, 0.18f);

        // Two face pairs in one submesh, drawn in index order: the halo quads (atlas bottom half, tools\portalatlas -halo:
        // the icon's alpha dilated + blurred, cut out under the icon) first, then the icon on top. Same quad, same material.
        internal static Mesh MeshFor(int id)
        {
            if (Meshes[id]) return Meshes[id];
            var icon = PortalIcons.Instance.Uv(id);
            var halo = new Rect(icon.x, icon.y - 0.5f, icon.width, icon.height);
            var vertices = new Vector3[16];
            var uv = new Vector2[16];
            var colors = new Color[16];
            var normals = new Vector3[16];
            for (var q = 0; q < 4; q++)
            {
                var r = q < 2 ? halo : icon;
                float u0 = r.xMin, u1 = r.xMax, v0 = r.yMin, v1 = r.yMax;
                if ((q & 1) == 1) (u0, u1) = (u1, u0); // back face, seen from +Z (its right = -X): U mirrored, reads right from behind
                // Each face sits ~3.5 cm (0.02 x trigger scale 1.74) out of the swirl plane toward its viewer, normal outward.
                var z = (q & 1) == 0 ? -0.02f : 0.02f;
                var b = q * 4;
                vertices[b] = new Vector3(-0.5f, -0.5f, z);
                vertices[b + 1] = new Vector3(0.5f, -0.5f, z);
                vertices[b + 2] = new Vector3(0.5f, 0.5f, z);
                vertices[b + 3] = new Vector3(-0.5f, 0.5f, z);
                for (var k = 0; k < 4; k++) normals[b + k] = new Vector3(0f, 0f, z < 0f ? -1f : 1f);
                uv[b] = new Vector2(u0, v0);
                uv[b + 1] = new Vector2(u1, v0);
                uv[b + 2] = new Vector2(u1, v1);
                uv[b + 3] = new Vector2(u0, v1);
                for (var k = 0; k < 4; k++) colors[b + k] = q < 2 ? Halo : Color.white;
            }
            var triangles = new int[24];
            for (var q = 0; q < 4; q++)
            {
                var b = q * 4;
                // Front (seen from -Z): clockwise BL, TL, TR. Back: the opposite winding.
                var t = (q & 1) == 0 ? new[] { b, b + 3, b + 2, b, b + 2, b + 1 } : new[] { b, b + 1, b + 2, b, b + 2, b + 3 };
                t.CopyTo(triangles, q * 6);
            }
            var m = new Mesh { name = "RuneSkin PortalIcon " + PortalStyle.Icons[id], vertices = vertices, normals = normals, uv = uv, colors = colors, triangles = triangles };
            m.RecalculateBounds();
            m.UploadMeshData(true);
            return Meshes[id] = m;
        }
    }

    // Re-reads the icon id when the portal ZDO's data revision changes (style write, also from other peers).
    public sealed class PortalArchIcon : MonoBehaviour
    {
        public ZNetView View;
        public int Icon;
        private uint _revision = uint.MaxValue;
        private MeshFilter _filter;
        private MeshRenderer _renderer;

        private void Awake()
        {
            _filter = GetComponent<MeshFilter>();
            _renderer = GetComponent<MeshRenderer>();
            PortalArchIcons.Count++;
        }

        private void OnDestroy() => PortalArchIcons.Count--;

        private void Update()
        {
            var zdo = View ? View.GetZDO() : null;
            if (zdo == null || zdo.DataRevision == _revision) return;
            _revision = zdo.DataRevision;
            var id = zdo.GetInt(IconKey, PortalStyle.NoIcon);
            if (id == Icon && _renderer.enabled == (id != PortalStyle.NoIcon)) return;
            Icon = id;
            var material = PortalArchIcons.Material();
            var on = id != PortalStyle.NoIcon && PortalStyle.IsIcon(id) && PortalIcons.Instance.Get(id) && material;
            if (on)
            {
                _filter.sharedMesh = PortalArchIcons.MeshFor(id);
                _renderer.sharedMaterial = material;
            }
            _renderer.enabled = on;
            // A styled portal whose icon could not be built yet (shader/atlas not ready) retries next frame instead of
            // latching on the unchanged revision; once on, no further work until the ZDO changes.
            if (!on && id != PortalStyle.NoIcon && PortalStyle.IsIcon(id)) _revision = uint.MaxValue;
        }

        private static readonly int IconKey = PortalStyle.IconKey.GetStableHashCode();
    }
}
