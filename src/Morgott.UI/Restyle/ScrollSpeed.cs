using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Morgott.UI.Restyle
{
    // Every ScrollRect (vanilla, ours, other mods'): the wheel moves 1.5 rows / text lines of its own content per notch
    // (ScrollMath). Set right before OnScroll reads m_ScrollSensitivity (ScrollRect.OnScroll: anchoredPosition +=
    // scrollDelta * m_ScrollSensitivity), so content that changes row size or font is measured each time.
    [HarmonyPatch]
    public static class ScrollSpeed
    {
        [HarmonyPatch(typeof(ScrollRect), nameof(ScrollRect.OnScroll))]
        [HarmonyPrefix]
        private static void OnScroll_Prefix(ScrollRect __instance)
        {
            if (!__instance.content) return;
            var vertical = __instance.vertical || !__instance.horizontal;
            var view = __instance.viewport ? __instance.viewport.rect : ((RectTransform)__instance.transform).rect;
            var px = ScrollMath.NotchPx(Pitch(__instance.content, vertical, vertical ? view.height : view.width),
                vertical ? view.height : view.width, Plugin.ScrollSpeed?.Value ?? 1f);
            __instance.scrollSensitivity = ScrollMath.Sensitivity(px, DeltaPerNotch());
        }

        // scrollDelta of one wheel notch as the active input module reports it (Valheim: 0.05).
        internal static float DeltaPerNotch()
        {
            var module = EventSystem.current ? EventSystem.current.currentInputModule : null;
            if (!module) return 1f;
            var ticks = module.ConvertPointerEventScrollDeltaToTicks(Vector2.up).y;
            return ticks > 1e-5f ? 1f / ticks : 1f;
        }

        // Row pitch of a scroll content along the scroll axis: a text's line, a grid's cell + spacing, else the first
        // row + the layout spacing; a single block taller than half the view (a text panel) falls back to its text's line.
        internal static float Pitch(RectTransform content, bool vertical, float view)
        {
            var line = Line(content);
            if (line > 0f) return line;
            if (content.TryGetComponent<GridLayoutGroup>(out var grid))
                return vertical ? grid.cellSize.y + grid.spacing.y : grid.cellSize.x + grid.spacing.x;
            for (var i = 0; i < content.childCount; i++)
            {
                if (!(content.GetChild(i) is RectTransform row) || !row.gameObject.activeSelf) continue;
                var size = vertical ? row.rect.height : row.rect.width;
                if (size <= 0f) continue;
                if (size > view * 0.5f) return LineIn(row);
                var group = content.GetComponent<HorizontalOrVerticalLayoutGroup>();
                return size + (group && (group is VerticalLayoutGroup) == vertical ? group.spacing : 0f);
            }
            return 0f;
        }

        private static float LineIn(RectTransform block)
        {
            var tmp = block.GetComponentInChildren<TMP_Text>();
            if (tmp) return Line(tmp.rectTransform);
            var text = block.GetComponentInChildren<Text>();
            return text ? Line(text.rectTransform) : 0f;
        }

        private static float Line(RectTransform t)
        {
            if (t.TryGetComponent<TMP_Text>(out var tmp))
                return tmp.textInfo != null && tmp.textInfo.lineCount > 0 ? tmp.textInfo.lineInfo[0].lineHeight : tmp.fontSize;
            return t.TryGetComponent<Text>(out var text) ? text.fontSize * text.lineSpacing : 0f;
        }
    }
}
