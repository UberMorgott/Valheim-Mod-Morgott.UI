using System;
using System.Collections.Generic;
using HarmonyLib;
using Morgott.UI.InfoScreens;
using Morgott.UI.Ownership;
using Morgott.UI.Restyle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Morgott.UI.WorldHud
{
    // Build menu piece buttons of pieces that add comfort (Piece.m_comfort > 0, vanilla and mod pieces alike) carry a
    // small badge: the vanilla "Rested" status icon (ObjectDB SE_Rested.m_icon) + "+N". Placed in a free corner of the
    // cell (ComfortBadgeMath, away from the favourite star / upgrade arrow). Pooled buttons are re-Setup per piece: the
    // badge is created once per button and switched with the piece.
    public static class ComfortBadge
    {
        public const string BadgeName = "RuneSkin_ComfortBadge";
        public const string ValueName = "RuneSkin_ComfortValue";
        private static Sprite _sprite;
        private static bool _warned;

        public static void Apply(BuildUiPieceButton button)
        {
            var root = button.transform as RectTransform;
            if (!root) return;
            var comfort = button.Piece ? button.Piece.m_comfort : 0;
            var badge = root.Find(BadgeName) as RectTransform;
            if (comfort <= 0)
            {
                if (badge) badge.gameObject.SetActive(false);
                return;
            }
            var sprite = RestedIcon();
            if (!sprite) return;
            if (!badge) badge = Create(root, sprite);
            var corner = ComfortBadgeMath.Corner(ComfortBadgeMath.Pick(Taken(root, button)));
            badge.anchorMin = new Vector2(corner.X0, corner.Y0);
            badge.anchorMax = new Vector2(corner.X1, corner.Y1);
            badge.offsetMin = badge.offsetMax = Vector2.zero;
            badge.SetAsLastSibling();
            badge.GetComponentInChildren<TMP_Text>(true).text = ComfortBadgeMath.Label(comfort);
            badge.gameObject.SetActive(true);
        }

        private static Sprite RestedIcon()
        {
            if (_sprite) return _sprite;
            var se = ObjectDB.instance ? ObjectDB.instance.GetStatusEffect(SEMan.s_statusEffectRested) : null;
            _sprite = se ? se.m_icon : null;
            if (!_sprite && !_warned && ObjectDB.instance)
            {
                _warned = true;
                Plugin.Log.LogWarning("[Morgott.UI] comfort badge: no Rested status icon in ObjectDB; badges off");
            }
            return _sprite;
        }

        private static RectTransform Create(RectTransform root, Sprite sprite)
        {
            var go = new GameObject(BadgeName, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(root, false);
            var icon = new GameObject("Icon", typeof(RectTransform)).AddComponent<Image>();
            icon.rectTransform.SetParent(rt, false);
            icon.rectTransform.anchorMin = Vector2.zero;
            icon.rectTransform.anchorMax = Vector2.one;
            icon.rectTransform.offsetMin = icon.rectTransform.offsetMax = Vector2.zero;
            icon.sprite = sprite;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            SkinMark.Own(icon.gameObject, "content", "piece comfort badge (vanilla Rested icon)");
            var value = InfoArt.RowText(rt, ValueName, "skill.level", TextAlignmentOptions.BottomRight);
            value.rectTransform.anchorMin = Vector2.zero;
            value.rectTransform.anchorMax = Vector2.one;
            value.rectTransform.offsetMin = value.rectTransform.offsetMax = Vector2.zero;
            value.textWrappingMode = TextWrappingModes.NoWrap;
            value.overflowMode = TextOverflowModes.Overflow;
            return rt;
        }

        // Existing badges (star / arrow, shown or not: either may switch on later) in the cell's normalized space.
        private static List<ComfortBadgeMath.Box> Taken(RectTransform root, BuildUiPieceButton button)
        {
            var list = new List<ComfortBadgeMath.Box>(2);
            Add(list, root, button.m_favoriteStar);
            Add(list, root, button.m_upgradeArrow);
            return list;
        }

        private static readonly Vector3[] Corners = new Vector3[4];

        private static void Add(List<ComfortBadgeMath.Box> list, RectTransform root, Graphic g)
        {
            var r = root.rect;
            if (!g || r.width <= 0f || r.height <= 0f) return;
            g.rectTransform.GetWorldCorners(Corners);
            var a = root.InverseTransformPoint(Corners[0]);
            var b = root.InverseTransformPoint(Corners[2]);
            list.Add(new ComfortBadgeMath.Box((a.x - r.xMin) / r.width, (a.y - r.yMin) / r.height, (b.x - r.xMin) / r.width, (b.y - r.yMin) / r.height));
        }
    }

    [HarmonyPatch]
    internal static class ComfortBadgePatches
    {
        [HarmonyPatch(typeof(BuildUiPieceButton), nameof(BuildUiPieceButton.Setup))]
        [HarmonyPostfix]
        private static void Setup_Postfix(BuildUiPieceButton __instance)
        {
            if (!OwnershipRegistry.IsOwned(OwnershipRegistry.BuildHud)) return;
            try { ComfortBadge.Apply(__instance); }
            catch (Exception e) { Plugin.Log.LogError($"[Morgott.UI] comfort badge failed: {e}"); }
        }
    }
}
