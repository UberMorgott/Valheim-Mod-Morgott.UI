using TMPro;
using UnityEngine;

namespace Morgott.UI.Theme
{
    // The one way RuneSkin adds a TextMeshProUGUI. TMP's Awake (TextMeshProUGUI.LoadFontAsset) falls back to
    // TMP_Settings.defaultFontAsset when no font is set; Valheim ships none, so a text added to an active GameObject
    // logged "The LiberationSans SDF Font Asset was not found" (player log: 280). Added while inactive, the font (and
    // material) is set before Awake runs; the GameObject's own active state is restored.
    public static class UiText
    {
        public static TextMeshProUGUI Add(GameObject go, TMP_FontAsset font = null, Material material = null)
        {
            var was = go.activeSelf;
            if (was) go.SetActive(false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (!font) font = ThemeTokens.GetFont("font.body");
            if (font) t.font = font;
            if (material) t.fontSharedMaterial = material;
            if (was) go.SetActive(true);
            return t;
        }
    }
}
