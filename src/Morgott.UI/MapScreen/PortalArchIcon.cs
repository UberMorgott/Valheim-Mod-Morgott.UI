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
            // World size / height above the trigger centre, divided by the trigger's scale (1.74 in both prefabs).
            var s = trigger.lossyScale.y > 0.01f ? trigger.lossyScale.y : 1f;
            var stone = prefab == Stone;
            var size = ThemeTokens.GetFloat(stone ? "size.portal.arch.stone" : "size.portal.arch.wood", stone ? 1.6f : 0.9f) / s;
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
            _material = new Material(shader) { name = "RuneSkin PortalIcons", mainTexture = atlas, renderQueue = (int)RenderQueue.Transparent };
            _material.SetFloat("_CullMode", (float)CullMode.Back);
            _material.SetFloat("unity_GUIZTestMode", (float)CompareFunction.LessEqual); // world depth, not the UI's Always
            _material.SetColor("_Color", new Color(1f, 1f, 1f, 0.92f));
            return _material;
        }

        internal static Mesh MeshFor(int id)
        {
            if (Meshes[id]) return Meshes[id];
            var uv = PortalIcons.Instance.Uv(id);
            float u0 = uv.xMin, u1 = uv.xMax, v0 = uv.yMin, v1 = uv.yMax;
            var m = new Mesh { name = "RuneSkin PortalIcon " + PortalStyle.Icons[id] };
            m.vertices = new[]
            {
                // Front, seen from -Z: clockwise BL, TL, TR.
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
                // Back, seen from +Z (its right = -X).
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
            };
            m.uv = new[]
            {
                new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1),
                new Vector2(u1, v0), new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), // U mirrored: reads right from behind
            };
            m.triangles = new[] { 0, 3, 2, 0, 2, 1, 5, 6, 7, 5, 7, 4 };
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
        }

        private static readonly int IconKey = PortalStyle.IconKey.GetStableHashCode();
    }
}
