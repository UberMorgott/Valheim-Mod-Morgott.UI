using HarmonyLib;
using TMPro;

namespace Morgott.UI.Restyle
{
    // Vanilla Localization.Localize(Transform) (run by every `Localize` component's Start, Localization.cs:291-312,
    // Localize.cs:6-15) keeps each localized TMP text in textMeshStrings for good: its OnDestroy only unhooks the
    // language events, RemoveTextFromCache is called only before a re-localize (KeyHints). Every damage number /
    // hud / tooltip clone destroyed on a crypt reload stayed pinned with its whole TMP state (lab mod-memleak
    // 20261108-173720: textMeshStrings -> destroyed TextMeshProUGUI -> m_textInfo / m_internalCharacterInfo).
    // A text leaving the world releases its key there and in our own per-graphic tables (their prune stays as backstop).
    [HarmonyPatch(typeof(TextMeshProUGUI), "OnDestroy")]
    internal static class TextCacheRelease
    {
        [HarmonyPostfix]
        private static void Postfix(TextMeshProUGUI __instance)
        {
            Localization.m_instance?.RemoveTextFromCache(__instance); // m_instance: the getter would create one at shutdown
            ModScreens.ForeignUi.Forget(__instance);
            NameCache.Forget(__instance);
        }
    }
}
