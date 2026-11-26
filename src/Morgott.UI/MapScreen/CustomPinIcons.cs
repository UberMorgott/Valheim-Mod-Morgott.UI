using UnityEngine;

namespace Morgott.UI.MapScreen
{
    // 25 custom Valheim map pin icons sliced 5x5 top-down row-major from embedded Assets\MapPinIcons.png (640x640, 128x128 cells).
    public static class CustomPinIcons
    {
        private const int Columns = 5, Cell = 128;
        private static Sprite[] _sprites;
        private static bool _tried;

        public static Sprite Get(int index) =>
            index >= 0 && index < CustomPinMath.IconIds.Length && Load() && index < _sprites.Length ? _sprites[index] : null;

        public static Sprite Get(string iconId) => Get(CustomPinMath.IconIndex(iconId));

        private static bool Load()
        {
            if (_tried) return _sprites != null;
            _tried = true;
            var tex = Icons.ModStatusIcons.LoadEmbedded("Morgott.UI.MapPinIcons.png", "UI_MapPinIcons");
            if (!tex) return false;
            var count = Mathf.Min(CustomPinMath.IconIds.Length, tex.width / Cell * (tex.height / Cell));
            _sprites = new Sprite[count];
            for (var i = 0; i < count; i++)
            {
                var rect = new Rect(i % Columns * Cell, tex.height - (i / Columns + 1) * Cell, Cell, Cell);
                _sprites[i] = Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), 100f);
                _sprites[i].name = "UI_MapPinIcon_" + CustomPinMath.IconIds[i];
            }
            Plugin.Log.LogInfo($"[Morgott.UI] custom map pin icons {tex.width}x{tex.height}, {count} sprites");
            return true;
        }
    }
}
