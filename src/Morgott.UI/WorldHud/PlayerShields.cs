using System.Runtime.CompilerServices;
using HarmonyLib;
using Morgott.UI.PlayerHud;
using Morgott.UI.Theme;
using UnityEngine;
using UnityEngine.UI;
using Morgott.UI.Ownership;

namespace Morgott.UI.WorldHud
{
    // Other players' shields as segments of their overhead health bar (family PlayerShields, batch P: was an AugaSkin
    // feature; segmented bar user 2026-11-16, docs\mockups\shield-segments): HP | energy shield (blue) | absorb shield
    // (orchid), each its max's share of the bar (HudMath.Segments), filled current / max over the dark track, thin
    // dividers, honeycomb material (BarView.SegmentMaterial) - the same bar as the local HP bar.
    // Vanilla shows another player's health only on the EnemyHud player HUD (m_baseHudPlayer, ShowHud EnemyHud.cs:126-157,
    // shown in range and not crouching :101-124, health set every frame :213-224). Its GuiBars health_fast / health_slow
    // draw m_width x value (GuiBar.SetBar), so the HP segment = GuiBar.SetWidth(full x share), full = m_width at first sight.
    // - energy shield: Morgott.Power gear pool, player ZDO floats mt_es / mt_es_max, written by the owner;
    // - absorb shield: vanilla SE_Shield (e.g. Staff of Protection) lives only in the owner's SEMan, so Morgott.Power
    //   ShieldShareModule publishes player ZDO mt_shield / mt_shield_max.
    // The floats arrive independently: each value is clamped to its own max.
    [HarmonyPatch]
    public static class PlayerShields
    {
        // Harmony asks before patching this class (Plugin: one PatchAll(Assembly), batched by FastStartup).
        private static bool Prepare() => OwnershipRegistry.IsOwned(OwnershipRegistry.PlayerShields);

        public const string GearName = "MorgottUI_ShieldGear", ExtName = "MorgottUI_ShieldExt";
        public const string GearDividerName = "MorgottUI_ShieldGearDivider", ExtDividerName = "MorgottUI_ShieldExtDivider";
        private static readonly int EsKey = "mt_es".GetStableHashCode(), EsMaxKey = "mt_es_max".GetStableHashCode();
        private static readonly int ShieldKey = "mt_shield".GetStableHashCode(), ShieldMaxKey = "mt_shield_max".GetStableHashCode();

        private sealed class Segs
        {
            public float Full;
            public RectTransform Bar;
            public Image Gear, Ext, GearDiv, ExtDiv;
        }

        private static readonly ConditionalWeakTable<EnemyHud.HudData, Segs> Huds = new ConditionalWeakTable<EnemyHud.HudData, Segs>();

        [HarmonyPatch(typeof(EnemyHud), nameof(EnemyHud.UpdateHuds))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        private static void UpdateHuds_Postfix(EnemyHud __instance)
        {
            foreach (var pair in __instance.m_huds)
            {
                var c = pair.Key;
                var hud = pair.Value;
                if (!c || hud.m_gui == null || !hud.m_gui.activeSelf || !c.IsPlayer() || hud.m_healthFast == null) continue;
                var zdo = c.m_nview ? c.m_nview.GetZDO() : null;
                if (zdo == null) continue;
                var s = Huds.GetValue(hud, h => new Segs { Full = h.m_healthFast.m_width, Bar = h.m_healthFast.m_bar });
                if (!s.Bar) continue;
                var esMax = Mathf.Max(0f, zdo.GetFloat(EsMaxKey));
                var shMax = Mathf.Max(0f, zdo.GetFloat(ShieldMaxKey));
                HudMath.Segments(c.GetMaxHealth(), esMax, shMax, s.Full, out var hpW, out var gw, out var ew);
                hud.m_healthFast.SetWidth(hpW);
                if (hud.m_healthSlow) hud.m_healthSlow.SetWidth(hpW);
                if (esMax + shMax > 0f && !s.Gear) Make(s);
                if (!s.Gear) continue;
                var div = ThemeTokens.GetFloat("size.enemy.segment.divider", 1f);
                Place(s, s.Gear, hpW, HudMath.SegmentFill(zdo.GetFloat(EsKey), esMax, gw));
                Place(s, s.Ext, hpW + gw, HudMath.SegmentFill(zdo.GetFloat(ShieldKey), shMax, ew));
                Place(s, s.GearDiv, hpW - div / 2f, gw > 0f ? div : 0f);
                Place(s, s.ExtDiv, hpW + gw - div / 2f, ew > 0f ? div : 0f);
            }
        }

        // Segment [x, x+w] of the bar's full length, the bar's height, in the bar's parent (health_fast).
        private static void Place(Segs s, Image img, float x, float w)
        {
            var on = w > 0.05f;
            if (img.gameObject.activeSelf != on) img.gameObject.SetActive(on);
            if (!on) return;
            var bar = s.Bar;
            var r = img.rectTransform;
            var size = new Vector2(w, bar.rect.height);
            if (r.sizeDelta != size) r.sizeDelta = size;
            // the bar grows from its left edge (GuiBar keeps its pivot), so that edge is fixed in the parent
            var pos = new Vector3(bar.localPosition.x + bar.rect.xMin + x, bar.localPosition.y + bar.rect.center.y, 0f);
            if (r.localPosition != pos) r.localPosition = pos;
        }

        private static void Make(Segs s)
        {
            var parent = s.Bar.parent;
            s.Gear = Segment(parent, GearName, "color.hud.shield", true);
            s.Ext = Segment(parent, ExtName, "color.hud.shield.ext", true);
            s.GearDiv = Segment(parent, GearDividerName, "color.hud.segment.divider", false);
            s.ExtDiv = Segment(parent, ExtDividerName, "color.hud.segment.divider", false);
        }

        private static Image Segment(Transform parent, string name, string color, bool hex)
        {
            var t = parent.Find(name); // a segment left by an earlier build of this HUD object is reused
            var img = t ? t.GetComponent<Image>() : new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            if (!t) img.transform.SetParent(parent, false);
            var r = img.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0f, 0.5f);
            img.sprite = null;
            img.type = Image.Type.Simple;
            img.material = hex ? BarView.SegmentMaterial() : null;
            img.color = ThemeTokens.GetColor(color, Color.cyan);
            img.raycastTarget = false;
            img.gameObject.SetActive(false);
            return img;
        }
    }
}
