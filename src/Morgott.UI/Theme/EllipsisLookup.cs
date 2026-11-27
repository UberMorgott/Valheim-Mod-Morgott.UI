using HarmonyLib;
using TMPro;

namespace Morgott.UI.Theme
{
    // TMP's ellipsis lookup (TMP_Text.GetEllipsisSpecialCharacter) searches with the text's style and weight only. A bold,
    // italic or non-Regular-weight text on a face without a weight table (our PT Sans cuts, the game's Averia) never
    // finds U+2026 although the face has it, so TMP logs "The character used for Ellipsis is not available" and drops
    // the overflow to Truncate (player log 0.51.2: 71 warnings). Its glyph lookup (TMP_Text.GetTextElement) retries with
    // Normal / Regular after the styled miss; the same retry here.
    [HarmonyPatch(typeof(TMP_Text), nameof(TMP_Text.GetEllipsisSpecialCharacter))]
    internal static class EllipsisLookup
    {
        private const uint Ellipsis = 0x2026;

        [HarmonyPostfix]
        private static void Postfix(TMP_Text __instance, TMP_FontAsset fontAsset)
        {
            if (__instance.m_Ellipsis.character != null || !fontAsset) return;
            var c = TMP_FontAssetUtilities.GetCharacterFromFontAsset(Ellipsis, fontAsset, true, FontStyles.Normal, FontWeight.Regular, out _);
            if (c == null && TMP_Settings.fallbackFontAssets is { Count: > 0 } fallbacks)
                c = TMP_FontAssetUtilities.GetCharacterFromFontAssets(Ellipsis, fontAsset, fallbacks, true, FontStyles.Normal, FontWeight.Regular, out _);
            if (c == null && TMP_Settings.defaultFontAsset)
                c = TMP_FontAssetUtilities.GetCharacterFromFontAsset(Ellipsis, TMP_Settings.defaultFontAsset, true, FontStyles.Normal, FontWeight.Regular, out _);
            if (c != null) __instance.m_Ellipsis = new TMP_Text.SpecialCharacter(c, 0);
        }
    }
}
