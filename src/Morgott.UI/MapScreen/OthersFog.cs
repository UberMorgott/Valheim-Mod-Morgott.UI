using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Morgott.UI.Ownership;
using UnityEngine;
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
            // Bits as int words (BitArray.CopyTo): no two BitArray indexer calls per texel of a 4M-texel pass.
            var n = map.m_explored.Length;
            var explored = new int[(n + 31) >> 5];
            var others = new int[explored.Length];
            map.m_explored.CopyTo(explored, 0);
            map.m_exploredOthers.CopyTo(others, 0);
            for (var i = 0; i < n; i++) fog[i * stride] = MapFilterMath.FogRed(MapFilterMath.Bit(explored, i), MapFilterMath.Bit(others, i), merged);
            if (apply) tex.Apply();
        }

        // One texel, as Explore(x, y) writes it, inside ExploreOthers' own SetPixel: one GetPixel / SetPixel pair per texel as
        // in vanilla (a postfix with its own pair doubled a big shared map's cost); the caller uploads (AddSharedMapData :2769,
        // WorldShare).
        [HarmonyPatch(typeof(Minimap), nameof(Minimap.ExploreOthers))]
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> ExploreOthers_Transpiler(IEnumerable<CodeInstruction> code) =>
            new CodeMatcher(code)
                .MatchForward(false, new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(Texture2D), nameof(Texture2D.SetPixel), new[] { typeof(int), typeof(int), typeof(Color) })))
                .ThrowIfInvalid("Minimap.ExploreOthers: Texture2D.SetPixel(int, int, Color) not found")
                .SetInstruction(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(OthersFog), nameof(SetOthersPixel))))
                .InstructionEnumeration();

        private static void SetOthersPixel(Texture2D tex, int x, int y, Color c)
        {
            if (Minimap.instance && Merged(Minimap.instance)) c.r = 0f;
            tex.SetPixel(x, y, c);
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
