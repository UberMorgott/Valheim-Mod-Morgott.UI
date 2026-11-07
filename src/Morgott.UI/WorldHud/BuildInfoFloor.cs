using System;
using HarmonyLib;
using Morgott.UI.Ownership;
using Morgott.UI.Restyle;
using TMPro;
using UnityEngine;

namespace Morgott.UI.WorldHud
{
    // Selected-piece strip requirement names (Hud.m_requirementItems res_name, auto-sized by the prefab): the auto-size
    // stops at the readable floor (Readable.ShrinkFloor, 16 px at 1080 at the current scale), longer names ellipsize.
    // Run 20261107-235729 m6-shrink-min build-1280x720: res_name 15.7 px (max 20). SetupPieceInfo runs per frame in place
    // mode; the values are written only when they differ.
    [HarmonyPatch]
    internal static class BuildInfoFloor
    {
        [HarmonyPatch(typeof(Hud), "SetupPieceInfo")]
        [HarmonyPostfix]
        private static void SetupPieceInfo_Postfix(Hud __instance)
        {
            if (!OwnershipRegistry.IsOwned(OwnershipRegistry.BuildHud)) return;
            try
            {
                foreach (var item in __instance.m_requirementItems)
                {
                    if (!item || !item.activeSelf) continue;
                    var t = item.transform.Find("res_name");
                    if (!t || !t.TryGetComponent<TMP_Text>(out var name) || !name.enableAutoSizing) continue;
                    var min = Readable.ShrinkFloor(name, name.fontSizeMax);
                    if (Mathf.Abs(name.fontSizeMin - min) > 0.05f) name.fontSizeMin = min;
                    if (name.overflowMode != TextOverflowModes.Ellipsis) name.overflowMode = TextOverflowModes.Ellipsis;
                }
            }
            catch (Exception e) { Plugin.Log.LogError($"[Morgott.UI] build requirement name floor failed: {e}"); }
        }
    }
}
