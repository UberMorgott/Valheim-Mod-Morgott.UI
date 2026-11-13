using Morgott.Core;
using UnityEngine;

namespace Morgott.UI.MapScreen
{
    // Core IPortalIcons: the portal style icons (Core PortalStyle ids 1..32) from one embedded atlas page
    // (Assets\PortalIcons.png, tools\portalatlas: 8 x 128 px cells per row from the top-left, id N = cell N-1).
    // One texture = one shared material for the arch icons (PortalArchIcon).
    public sealed class PortalIcons : IPortalIcons
    {
        public static readonly PortalIcons Instance = new PortalIcons();
        private const int Columns = 8, Cell = 128;
        private Sprite[] _sprites;
        private bool _tried;

        public Texture2D Atlas { get; private set; }

        public Sprite Get(int id) => id > PortalStyle.NoIcon && PortalStyle.IsIcon(id) && Load() && id - 1 < _sprites.Length ? _sprites[id - 1] : null;

        /// <summary>Normalised atlas rect of an icon (texture origin bottom-left).</summary>
        public Rect Uv(int id)
        {
            var s = Get(id);
            if (!s) return default;
            var r = s.textureRect;
            return new Rect(r.x / Atlas.width, r.y / Atlas.height, r.width / Atlas.width, r.height / Atlas.height);
        }

        private bool Load()
        {
            if (_tried) return _sprites != null;
            _tried = true;
            Atlas = Icons.ModStatusIcons.LoadEmbedded("Morgott.UI.PortalIcons.png", "UI_PortalIcons");
            if (!Atlas) return false;
            var count = Mathf.Min(PortalStyle.Icons.Length - 1, Atlas.width / Cell * (Atlas.height / Cell));
            _sprites = new Sprite[count];
            for (var i = 0; i < count; i++)
            {
                var rect = new Rect(i % Columns * Cell, Atlas.height - (i / Columns + 1) * Cell, Cell, Cell);
                _sprites[i] = Sprite.Create(Atlas, rect, new Vector2(0.5f, 0.5f), 100f);
                _sprites[i].name = "UI_PortalIcon_" + PortalStyle.Icons[i + 1];
            }
            Plugin.Log.LogInfo($"[Morgott.UI] portal icons {Atlas.width}x{Atlas.height}, {count} sprites");
            return true;
        }
    }
}
