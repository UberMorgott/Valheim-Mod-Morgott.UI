using System.Runtime.CompilerServices;
using HarmonyLib;
using Morgott.UI.PlayerHud;
using Morgott.UI.Theme;
using UnityEngine;
using UnityEngine.UI;
using Morgott.UI.Ownership;

namespace Morgott.UI.WorldHud
{
    // Other players' shields under their overhead health bar (family PlayerShields, batch P: was an AugaSkin feature).
    // Vanilla shows another player's health only on the EnemyHud player HUD (m_baseHudPlayer, ShowHud EnemyHud.cs:126-157,
    // shown in range and not crouching :101-124, health set every frame :213-224); the bar is its "Health" child. One thin
    // rune rail goes under it with two contiguous fills over es max + shield max (user 2026-11-16):
    // - energy shield (Morgott.Power gear pool, blue; player ZDO floats mt_es / mt_es_max, written by the owner);
    // - absorb shield (vanilla SE_Shield, e.g. Staff of Protection, purple: its remaining absorb lives only in the owner's
    //   SEMan, so Morgott.Power ShieldShareModule publishes player ZDO mt_shield / mt_shield_max).
    // The rail hides while both maxes are 0; it is a child of the HUD object (its visibility). The floats arrive
    // independently: each value is clamped to its own max.
    [HarmonyPatch]
    public static class PlayerShields
    {
        // Harmony asks before patching this class (Plugin: one PatchAll(Assembly), batched by FastStartup).
        private static bool Prepare() => OwnershipRegistry.IsOwned(OwnershipRegistry.PlayerShields);

        public const string RailName = "RuneSkin_Shields"; // children: 0 = energy shield fill, 1 = external shield fill
        private static readonly int EsKey = "mt_es".GetStableHashCode(), EsMaxKey = "mt_es_max".GetStableHashCode();
        private static readonly int ShieldKey = "mt_shield".GetStableHashCode(), ShieldMaxKey = "mt_shield_max".GetStableHashCode();

        private sealed class Rail
        {
            public RectTransform Rect, Es, Ext;
            public float GearW = -1f, ExtW = -1f;
        }

        private static readonly ConditionalWeakTable<EnemyHud.HudData, Rail> Huds = new ConditionalWeakTable<EnemyHud.HudData, Rail>();

        [HarmonyPatch(typeof(EnemyHud), nameof(EnemyHud.UpdateHuds))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        private static void UpdateHuds_Postfix(EnemyHud __instance)
        {
            foreach (var pair in __instance.m_huds)
            {
                var c = pair.Key;
                var hud = pair.Value;
                if (!c || hud?.m_gui == null || !hud.m_gui.activeSelf || !c.IsPlayer()) continue;
                var zdo = c.m_nview ? c.m_nview.GetZDO() : null;
                if (zdo == null) continue;
                var esMax = Mathf.Max(0f, zdo.GetFloat(EsMaxKey));
                var shMax = Mathf.Max(0f, zdo.GetFloat(ShieldMaxKey));
                var rail = Huds.GetValue(hud, _ => new Rail());
                Show(rail, hud.m_gui.transform, Mathf.Min(zdo.GetFloat(EsKey), esMax), Mathf.Min(zdo.GetFloat(ShieldKey), shMax), esMax + shMax);
            }
        }

        private static void Show(Rail rail, Transform gui, float es, float shield, float max)
        {
            if (max <= 0f)
            {
                if (rail.Rect && rail.Rect.gameObject.activeSelf) rail.Rect.gameObject.SetActive(false);
                return;
            }
            if (!rail.Rect && !Make(rail, gui)) return;
            if (!rail.Rect.gameObject.activeSelf) rail.Rect.gameObject.SetActive(true);
            HudMath.ShieldSegments(es, shield, max, 1f, out var gw, out var ew);
            if (rail.GearW == gw && rail.ExtW == ew) return;
            rail.GearW = gw;
            rail.ExtW = ew;
            rail.Es.anchorMax = new Vector2(gw, 1f);
            rail.Ext.anchorMin = new Vector2(gw, 0f);
            rail.Ext.anchorMax = new Vector2(gw + ew, 1f);
        }
        private static float Height => ThemeTokens.GetFloat("size.enemy.shield.rail", 5f);
        private static float Gap => ThemeTokens.GetFloat("size.enemy.shield.gap", 2f);

        // Top edge Gap under the health bar's bottom edge.
        private static bool Make(Rail rail, Transform gui)
        {
            if (!(gui.Find("Health") is RectTransform health)) return false;
            var rect = gui.Find(RailName) as RectTransform; // a rail left by an earlier build of this HUD object is reused
            if (!rect)
            {
                rect = new GameObject(RailName, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
                rect.SetParent(gui, false);
                new GameObject("es", typeof(RectTransform), typeof(Image)).transform.SetParent(rect, false);
                new GameObject("ext", typeof(RectTransform), typeof(Image)).transform.SetParent(rect, false);
            }
            rect.anchorMin = health.anchorMin;
            rect.anchorMax = health.anchorMax;
            rect.pivot = new Vector2(health.pivot.x, 1f);
            rect.sizeDelta = new Vector2(health.rect.width, Height);
            var trough = rect.GetComponent<Image>();
            trough.sprite = ThemeTokens.GetSprite("sprite.hud.rail.trough");
            trough.type = trough.sprite ? Image.Type.Sliced : Image.Type.Simple;
            trough.color = trough.sprite ? Color.white : ThemeTokens.GetColor("color.hud.trough", Color.black);
            trough.raycastTarget = false;
            rect.anchoredPosition = health.anchoredPosition - new Vector2(0f, health.pivot.y * health.rect.height + Gap);
            rail.Rect = rect;
            rail.Es = Fill(rect, 0, "color.hud.es");
            rail.Ext = Fill(rect, 1, "color.hud.shield.ext");
            rail.GearW = rail.ExtW = -1f;
            return true;
        }

        private static RectTransform Fill(RectTransform rail, int index, string color)
        {
            var fill = (RectTransform)rail.GetChild(index);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.zero;
            fill.sizeDelta = Vector2.zero;
            var img = fill.GetComponent<Image>();
            img.sprite = ThemeTokens.GetSprite("sprite.hud.rail.fill");
            img.type = img.sprite ? Image.Type.Sliced : Image.Type.Simple;
            img.color = ThemeTokens.GetColor(color, Color.cyan);
            img.raycastTarget = false;
            return fill;
        }
    }
}
