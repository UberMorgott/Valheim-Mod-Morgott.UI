using System;
using HarmonyLib;
using Morgott.UI.Diagnostics;
using Morgott.UI.PlayerHud;
using Morgott.UI.Theme;
using TMPro;
using UnityEngine;
using Morgott.UI.Ownership;

namespace Morgott.UI.WorldHud
{
    // Family "DamageText" (art\README.md "M4 sprites": damage text A, font only, no sprites). Vanilla clones
    // m_worldTextBase per number (AddInworldText, DamageText.cs:94) and writes a hard-coded colour per TextType
    // (:105-115), the size (m_largeFontSize / m_smallFontSize, :39-41, Bonus x1.5) and later only the alpha
    // (UpdateWorldTexts :77). RuneSkin: the template text gets the body font (the display serif has old-style digits),
    // a dark outline and a little dilate (bold read); sizes from tokens; each new number's colour from
    // color.damage.<type> (self damage stays its own red token). No prefab swap.
    public static class DamageTextSkin
    {
        public const string ScreenId = "DamageText";
        private static readonly HudArt Art = new HudArt(ScreenId);
        private static Transform _template;
        private static TMP_FontAsset _plainFont;
        private static Material _plainMaterial;
        private static FontStyles _plainStyle;
        private static int _jitterFrame = -1;
        private static Vector3 _jitter;

        internal static void Attach(DamageText dt)
        {
            if (!dt || !dt.m_worldTextBase) return;
            // A new DamageText per world load: the previous template's rows only pile up in the static table.
            Art.Reset();
            _template = dt.m_worldTextBase.transform;
            var text = dt.m_worldTextBase.GetComponentInChildren<TMP_Text>(true);
            var ok = TextLook.Apply(text, "font.body", "damage", ThemeTokens.GetFloat("size.damage.dilate", 0.15f));
            if (text)
            {
                text.fontStyle |= FontStyles.Bold;
                // The plain look, restored on every plain number (Morgott.Performance FxPool reuses clones as a typed number left them).
                _plainFont = text.font;
                _plainMaterial = text.fontSharedMaterial;
                _plainStyle = text.fontStyle;
                // Typed dimming multiplies vanilla's per-frame fade (UpdateWorldTexts writes only color.a, DamageText.cs:79-81):
                // a CanvasGroup alpha on every clone, set per number, no per-frame code.
                var cg = text.GetComponent<CanvasGroup>();
                if (!cg) cg = text.gameObject.AddComponent<CanvasGroup>();
                cg.interactable = false;
                cg.blocksRaycasts = false;
            }
            Art.Note(text, "damage.text", ok, "damage number template (body font, outline, bold)");
            dt.m_largeFontSize = Mathf.RoundToInt(ThemeTokens.GetFloat("size.damage.font", 24f));
            dt.m_smallFontSize = Mathf.RoundToInt(ThemeTokens.GetFloat("size.damage.font.small", 15f));
            Plugin.Log.LogInfo("[Morgott.UI] damage text restyled");
        }

        public static string ColorToken(DamageText.TextType type, bool mySelf)
        {
            if (mySelf && type <= DamageText.TextType.Immune) return "color.damage.self";
            switch (type)
            {
                case DamageText.TextType.Weak: return "color.damage.weak";
                case DamageText.TextType.Bonus: return "color.damage.bonus";
                case DamageText.TextType.Resistant: return "color.damage.resistant";
                case DamageText.TextType.Immune: return "color.damage.immune";
                case DamageText.TextType.Heal: return "color.damage.heal";
                case DamageText.TextType.TooHard: return "color.damage.toohard";
                case DamageText.TextType.Blocked: return "color.damage.blocked";
                default: return "color.damage.normal";
            }
        }

        // The number AddInworldText just appended (last of m_worldTexts): colour from the theme, alpha kept.
        internal static void Recolor(DamageText dt, DamageText.TextType type, Vector3 pos, float distance, bool mySelf, string text)
        {
            var list = dt.m_worldTexts;
            if (list == null || list.Count == 0) return;
            var inst = list[list.Count - 1];
            var tf = inst?.m_textField;
            if (!tf) return;
            var cg = tf.GetComponent<CanvasGroup>();
            if (TypedDamageText.HasCurrent && !mySelf && type <= DamageText.TextType.Immune)
            {
                Typed(inst, tf, cg, TypedDamageText.Current, type, pos, distance);
                return;
            }
            if (_plainFont)
            {
                tf.font = _plainFont;
                tf.fontSharedMaterial = _plainMaterial;
                tf.fontStyle = _plainStyle;
            }
            if (cg) cg.alpha = 1f;
            // "0" damage keeps vanilla's grey (DamageText.cs:105-108).
            if (mySelf && text == "0") return;
            var c = ThemeTokens.GetColor(ColorToken(type, mySelf), tf.color);
            c.a = tf.color.a;
            tf.color = c;
        }

        // Approved mockup v3: hue = damage type (color.damage.type.*), weak = real PT Sans Bold + bigger, resisted / immune = dimmed
        // and desaturated, DoT tick = "·" prefix x0.9; numbers of one target in one frame side by side by slot.
        private static void Typed(DamageText.WorldTextInstance inst, TMP_Text tf, CanvasGroup cg, DamageDescriptor d,
            DamageText.TextType type, Vector3 pos, float distance)
        {
            var s = DamagePopupMath.Style(d.Grade, d.Tick);
            var c = ThemeTokens.GetColor(DamagePopupMath.ColorToken(d.Kind) ?? ColorToken(type, false), tf.color);
            DamagePopupMath.Desaturate(ref c.r, ref c.g, ref c.b, s.Desaturate);
            c.a = tf.color.a;
            tf.color = c;
            var font = ThemeTokens.GetFont(s.Bold ? "font.body.bold" : "font.body");
            if (!font) font = _plainFont;
            if (font)
            {
                tf.font = font;
                // One look for both weights: same dark outline, no dilate (regular vs bold must differ).
                var m = TextLook.Outlined(font, "damage.typed", ThemeTokens.GetFloat("size.text.outline", 0.22f), 0f);
                if (m) tf.fontSharedMaterial = m;
            }
            tf.fontStyle = _plainStyle & ~FontStyles.Bold;
            tf.fontSize *= s.Scale;
            if (d.Tick) tf.text = DamagePopupMath.TickPrefix + tf.text;
            if (cg) cg.alpha = s.Alpha;
            // Vanilla scatters each number by insideUnitSphere * 0.5 (DamageText.cs:104), which piles same-frame numbers of one
            // target on each other: they share one scatter per frame and step sideways by a screen-constant width per slot.
            var frame = Time.frameCount;
            if (frame != _jitterFrame)
            {
                _jitterFrame = frame;
                _jitter = UnityEngine.Random.insideUnitSphere * 0.5f;
            }
            var cam = Utils.GetMainCamera();
            var side = cam ? cam.transform.right * (DamagePopupMath.Side(d.Slot) * distance * ThemeTokens.GetFloat("size.damage.stagger", 0.08f)) : Vector3.zero;
            inst.m_worldPos = pos + _jitter + side;
        }

        public static void Record()
        {
            if (_template) SkinDiagnostics.Record(Art.Report(_template));
        }
    }

    [HarmonyPatch]
    public static class DamageTextAdapter
    {
        // Harmony asks before patching this class (Plugin: one PatchAll(Assembly), batched by FastStartup).
        private static bool Prepare() => OwnershipRegistry.IsOwned(OwnershipRegistry.DamageText);

        [HarmonyPatch(typeof(DamageText), nameof(DamageText.Awake))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Awake_Postfix(DamageText __instance)
        {
            try { DamageTextSkin.Attach(__instance); }
            catch (Exception e) { Plugin.Log.LogError($"[Morgott.UI] damage text restyle failed (vanilla numbers stay): {e}"); }
        }

        // Count before: AddInworldText returns early past m_maxTextDistance (nothing appended, nothing to recolour).
        [HarmonyPatch(typeof(DamageText), nameof(DamageText.AddInworldText))]
        [HarmonyPrefix]
        private static void AddInworldText_Prefix(DamageText __instance, out int __state) =>
            __state = __instance.m_worldTexts != null ? __instance.m_worldTexts.Count : 0;

        [HarmonyPatch(typeof(DamageText), nameof(DamageText.AddInworldText))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void AddInworldText_Postfix(DamageText __instance, DamageText.TextType type, Vector3 pos, float distance, string text,
            bool mySelf, int __state)
        {
            try
            {
                if (__instance.m_worldTexts == null || __instance.m_worldTexts.Count <= __state) return;
                DamageTextSkin.Recolor(__instance, type, pos, distance, mySelf, text);
            }
            catch (Exception e) { Plugin.Log.LogError($"[Morgott.UI] damage text colour failed: {e}"); }
            finally { TypedDamageText.EndDecode(); }
        }
    }
}
