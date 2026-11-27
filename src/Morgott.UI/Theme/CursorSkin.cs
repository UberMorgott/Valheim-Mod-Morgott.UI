using UnityEngine;

namespace Morgott.UI.Theme
{
    // Mouse cursor (family Cursor, batch P: was an AugaSkin feature): the bundle's readable cursor texture (theme
    // "texture.cursor", art/src/kit/textures.json, 32x32 hardware cursor) with its click point from the theme
    // (size.cursor.hot.x / .y, px from the top-left). Vanilla never sets a cursor, so it is set once and on theme reload.
    public static class CursorSkin
    {
        public const string Token = "texture.cursor";

        public static string Applied { get; private set; } = "none";

        public static void Apply()
        {
            var tex = ThemeTokens.GetTexture(Token);
            if (!tex)
            {
                Applied = "none (no texture.cursor in the bundle)";
                return;
            }
            var hot = new Vector2(ThemeTokens.GetFloat("size.cursor.hot.x", 0f), ThemeTokens.GetFloat("size.cursor.hot.y", 0f));
            Cursor.SetCursor(tex, hot, CursorMode.Auto);
            Applied = $"{tex.name} {tex.width.ToString()}x{tex.height.ToString()} {FormatName(tex.format)} hot {hot.ToString()}";
            Plugin.Log.LogInfo($"[Morgott.UI] cursor: {Applied}");
        }

        private static string FormatName(TextureFormat format) => format switch
        {
            TextureFormat.RGBA32 => "RGBA32",
            TextureFormat.ARGB32 => "ARGB32",
            TextureFormat.BGRA32 => "BGRA32",
            TextureFormat.RGB24 => "RGB24",
            TextureFormat.Alpha8 => "Alpha8",
            TextureFormat.DXT1 => "DXT1",
            TextureFormat.DXT5 => "DXT5",
            TextureFormat.BC7 => "BC7",
            _ => ((int)format).ToString(),
        };
    }
}
