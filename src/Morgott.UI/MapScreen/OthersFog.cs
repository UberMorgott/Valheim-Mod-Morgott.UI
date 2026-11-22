using HarmonyLib;
using Morgott.UI.Ownership;
using UnityEngine.Experimental.Rendering;

namespace Morgott.UI.MapScreen
{
    // Areas explored by other players (Minimap.m_exploredOthers: map table AddSharedMapData, Minimap.cs:2744-2769; Morgott.Patcher
    // WorldShare) clear only the fog texture's green (ExploreOthers :1914-1929), which the map shaders draw as a lighter fog
    // layer (_SharedFade, :734-753). [Map] OthersLayer off (default): those texels also get red cleared like own exploration
    // (Explore :1857-1868), on the small and large map alike (one _FogTex, :540/:544). m_fogTexture is render state only (the
    // save writes the bit arrays, GetMapData :2129-2135): m_explored / m_exploredOthers are never touched. Vanilla's shared-map
    // toggle (m_showSharedMapData, OnToggleSharedMapData :2610) still hides them.
    [HarmonyPatch]
    public static class OthersFog
    {
        private static bool _hooked;

        private static bool Prepare() => OwnershipRegistry.IsOwned(OwnershipRegistry.Minimap);

        internal static bool Merged(Minimap map) => !(MapConfig.OthersLayer?.Value ?? false) && map.m_showSharedMapData;

        // MinimapSkin.Init: the checkbox / ConfigurationManager redraw the fog at once.
        public static void Attach()
        {
            if (_hooked || MapConfig.OthersLayer == null || !Prepare()) return;
            _hooked = true;
            MapConfig.OthersLayer.SettingChanged += (_, __) => { if (Minimap.instance) Rewrite(Minimap.instance, true); };
        }

        // Fog red of every texel from the explored bits (byte 0 of a texel in R8G8_UNorm or the RGBA32 fallback,
        // CreateMapTexture :584-594), in the texture's CPU copy.
        internal static void Rewrite(Minimap map, bool apply)
        {
            var tex = map.m_fogTexture;
            if (!tex) return;
            var merged = Merged(map);
            var stride = (int)GraphicsFormatUtility.GetBlockSize(tex.graphicsFormat);
            var fog = tex.GetPixelData<byte>(0);
            var explored = map.m_explored;
            var others = map.m_exploredOthers;
            for (var i = 0; i < explored.Length; i++) fog[i * stride] = MapFilterMath.FogRed(explored[i], others[i], merged);
            if (apply) tex.Apply();
        }

        // One texel, as Explore(x, y) writes it; the caller uploads (AddSharedMapData :2769, WorldShare).
        [HarmonyPatch(typeof(Minimap), nameof(Minimap.ExploreOthers))]
        [HarmonyPostfix]
        private static void ExploreOthers_Postfix(Minimap __instance, int x, int y, bool __result)
        {
            if (!__result || !Merged(__instance)) return;
            var c = __instance.m_fogTexture.GetPixel(x, y);
            c.r = 0f;
            __instance.m_fogTexture.SetPixel(x, y, c);
        }

        // World load (SetMapData uploads after it, :2178/:2213): every others' texel at once.
        [HarmonyPatch(typeof(Minimap), nameof(Minimap.ResetAndExplore), new[] { typeof(System.Collections.BitArray), typeof(System.Collections.BitArray) })]
        [HarmonyPostfix]
        private static void ResetAndExplore_Postfix(Minimap __instance)
        {
            if (Merged(__instance)) Rewrite(__instance, false);
        }

        // Shared data dropped (:610-629, uploaded before this postfix): merged texels that were others' only go back to fog.
        [HarmonyPatch(typeof(Minimap), nameof(Minimap.ResetSharedMapData))]
        [HarmonyPostfix]
        private static void ResetShared_Postfix(Minimap __instance)
        {
            if (!(MapConfig.OthersLayer?.Value ?? false)) Rewrite(__instance, true);
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnToggleSharedMapData))]
        [HarmonyPostfix]
        private static void ToggleShared_Postfix(Minimap __instance)
        {
            if (!(MapConfig.OthersLayer?.Value ?? false)) Rewrite(__instance, true);
        }
    }
}
