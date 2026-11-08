using HarmonyLib;
using Morgott.UI.Ownership;

namespace Morgott.UI.PlayerHud
{
    // Localization.GetBoundKeyString (every "$KEY_<button>" in texts, KeyHints rows, tooltips): mouse / modifier keys in
    // Latin at any language (KeyLabels). Same lookup as vanilla (Localization.cs GetBoundKeyString -> ZInput), only the
    // "$button_*" translation and layout-printed letters (Й -> Q) are replaced; gamepad glyphs and English stay the game's.
    [HarmonyPatch(typeof(Localization), nameof(Localization.GetBoundKeyString))]
    internal static class KeyLabelsPatch
    {
        private static void Postfix(Localization __instance, string bindingName, bool emptyStringOnMissing, ref string __result)
        {
            if (!OwnershipRegistry.IsOwned(OwnershipRegistry.KeyHints) || ZInput.instance == null) return;
            var raw = ZInput.instance.GetBoundKeyString(bindingName, emptyStringOnMissing);
            var latin = KeyLabels.For(raw, __instance.GetSelectedLanguage()) ?? (KeyLabels.IsLayoutGlyph(raw) ? GuardianAbility.KeyLabel(bindingName) : null);
            if (latin != null) __result = latin;
        }
    }
}
