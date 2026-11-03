using Morgott.UI.PlayerHud;
using Morgott.UI.Screens;
using Morgott.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace Morgott.UI.WorldHud
{
    // Immunity row (user 2026-11-03, mockup immune-mockup-1 approved; bosses excluded): under the bar window of a regular enemy
    // bar, one badge per damage type the creature takes nothing from (EnemyHudMath.ImmuneMask over the effective modifiers,
    // Character.GetDamageModifiers: prefab + armor + status effects, after every mod's patches). Badge = the slot recess,
    // the stats panel's element icon tinted like the tooltip icons (sprite.stats.icon.<type>, color.stats.elem.<type>) and
    // a diagonal strike. Built once on the template (every clone inherits it); the mask is re-read every ImmuneRecheck
    // seconds per shown bar and the row relaid only when it changes.
    public static partial class EnemyHudSkin
    {
        public const string ImmuneName = "RuneSkin_Immune";
        private const float ImmuneRecheck = 0.5f;

        private static void NewImmuneRow(RectTransform health, float frameBottom)
        {
            var row = HudArt.Rect(ImmuneName, health);
            row.anchorMin = row.anchorMax = Vector2.zero;
            row.pivot = new Vector2(0f, 1f);
            var size = ThemeTokens.GetFloat("size.enemy.immune", 14f);
            row.anchoredPosition = new Vector2(0f, -(frameBottom + ThemeTokens.GetFloat("size.enemy.immune.gap.top", 2f)));
            row.sizeDelta = new Vector2(health.rect.width, size);
            var inset = ThemeTokens.GetFloat("size.enemy.immune.inset", 2f);
            var strikeColor = ThemeTokens.GetColor("color.enemy.immune.strike", new Color(0.88f, 0.33f, 0.23f, 0.95f));
            for (var i = 0; i < DamageIconText.Types.Length; i++)
            {
                var type = DamageIconText.Types[i];
                var badge = Art.Image(type, row, "sprite.slot.recess", "color.frame", Image.Type.Simple);
                var bt = badge.rectTransform;
                bt.anchorMin = bt.anchorMax = bt.pivot = new Vector2(0f, 1f);
                bt.sizeDelta = new Vector2(size, size);
                var icon = Art.Image("Icon", bt, "sprite.stats.icon." + type, "color.stats.elem." + type, Image.Type.Simple);
                icon.preserveAspect = true;
                Stretch(icon.rectTransform, inset);
                var strike = HudArt.Rect("Strike", bt).gameObject.AddComponent<Image>();
                strike.raycastTarget = false;
                strike.color = strikeColor;
                var st = strike.rectTransform;
                st.anchorMin = st.anchorMax = st.pivot = new Vector2(0.5f, 0.5f);
                st.sizeDelta = new Vector2((size - inset) * 1.3f, ThemeTokens.GetFloat("size.enemy.immune.strike", 1.5f));
                st.localEulerAngles = new Vector3(0f, 0f, -45f); // top-left to bottom-right
                Art.Note(strike, "enemy.immune.strike", true, "immunity strike (flat colour)");
                badge.gameObject.SetActive(false);
            }
            row.gameObject.SetActive(false);
        }

        private static void Stretch(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }

        // EnemyHud.LateUpdate prefix, before Vaettir's OneBossBar clears m_boss for the HUD pass (OneBossBar.cs Prefix).
        internal static void ImmuneRows(EnemyHud hud)
        {
            var now = Time.unscaledTime;
            foreach (var pair in hud.m_huds)
            {
                var data = pair.Value;
                if (data == null || !data.m_gui || !data.m_character || !data.m_gui.activeSelf || !States.TryGetValue(data, out var s)) continue;
                ImmuneRow(s, data.m_character, now);
            }
        }

        private static void ImmuneRow(State s, Character c, float now)
        {
            if (!s.Immune || now < s.ImmuneAt) return;
            s.ImmuneAt = now + ImmuneRecheck;
            // No row on bosses (user 2026-11-03), also when Vaettir draws one on a mob bar (OneBossBar answers IsBoss false there).
            var mask = c.m_boss ? 0 : EnemyHudMath.ImmuneMask(c.GetDamageModifiers());
            if (mask == s.ImmuneMask) return;
            s.ImmuneMask = mask;
            var row = s.Immune;
            var step = ThemeTokens.GetFloat("size.enemy.immune", 14f) + ThemeTokens.GetFloat("size.enemy.immune.gap", 2f);
            var n = 0;
            for (var i = 0; i < row.childCount && i < DamageIconText.Types.Length; i++)
            {
                var badge = (RectTransform)row.GetChild(i);
                var on = (mask & (1 << i)) != 0;
                if (on) badge.anchoredPosition = new Vector2(n++ * step, 0f);
                if (badge.gameObject.activeSelf != on) badge.gameObject.SetActive(on);
            }
            row.gameObject.SetActive(n > 0);
        }

        // DebugState field: "frost+poison" (DamageIconText.Types order) or "-".
        private static string ImmuneTypes(int mask)
        {
            if (mask <= 0) return "-";
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < DamageIconText.Types.Length; i++)
                if ((mask & (1 << i)) != 0) sb.Append(sb.Length > 0 ? "+" : "").Append(DamageIconText.Types[i]);
            return sb.ToString();
        }
    }
}
