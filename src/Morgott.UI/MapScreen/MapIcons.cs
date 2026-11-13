using UnityEngine;
using UnityEngine.UI;

namespace Morgott.UI.MapScreen
{
    // Map icons of the 2026-11-10 mockup (Codex art, Assets\MapLayersIcon.png / MapBossIcon.png, 128 px, embedded):
    // the СЛОИ row glyph and the red boss skull. The boss skull replaces the vanilla Boss pin sprite (Minimap.m_icons,
    // read by AddPin / GetSprite, Minimap.cs:2336/2354), on existing boss pins and on the vanilla Boss filter button.
    public static class MapIcons
    {
        private static Sprite _layers, _boss, _pencil, _customColor;

        public static Sprite Layers => _layers ? _layers : _layers = Load("Morgott.UI.MapLayersIcon.png", "UI_MapLayersIcon");
        public static Sprite Boss => _boss ? _boss : _boss = Load("Morgott.UI.MapBossIcon.png", "UI_MapBossIcon");
        // Portal mockups: the picker row's edit pencil (white, tinted) and the editor's rainbow "+" custom-colour swatch.
        public static Sprite Pencil => _pencil ? _pencil : _pencil = Load("Morgott.UI.PortalPencil.png", "UI_PortalPencil");
        public static Sprite CustomColor => _customColor ? _customColor : _customColor = Load("Morgott.UI.PortalCustomColor.png", "UI_PortalCustomColor");

        private static Sprite Load(string resource, string name)
        {
            var tex = Icons.ModStatusIcons.LoadEmbedded(resource, name);
            if (!tex) return null;
            var s = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            s.name = name;
            return s;
        }

        public static void ApplyBoss(Minimap map)
        {
            var sprite = Boss;
            if (!map || !sprite) return;
            for (var i = 0; i < map.m_icons.Count; i++)
            {
                if (map.m_icons[i].m_name != Minimap.PinType.Boss) continue;
                var d = map.m_icons[i];
                d.m_icon = sprite;
                map.m_icons[i] = d;
            }
            foreach (var pin in map.m_pins)
            {
                if (pin.m_type != Minimap.PinType.Boss) continue;
                pin.m_icon = sprite;
                if (pin.m_iconElement) pin.m_iconElement.sprite = sprite;
            }
            if (map.m_selectedIcons.TryGetValue(Minimap.PinType.Boss, out var frame) && frame && frame.transform.parent
                && frame.transform.parent.TryGetComponent<Image>(out var button))
                button.sprite = sprite;
        }
    }
}
