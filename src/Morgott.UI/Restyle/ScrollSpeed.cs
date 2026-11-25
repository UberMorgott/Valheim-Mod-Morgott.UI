using System.Collections.Generic;
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
        private static readonly float[] GridScratch = new float[ScrollMath.MaxPitchScan];
        private static readonly float[] RowScratch = new float[ScrollMath.MaxPitchScan];
        private static readonly float[] LineScratch = new float[ScrollMath.MaxPitchScan];
        private static readonly List<TMP_Text> TmpScratch = new List<TMP_Text>(16);
        private static readonly List<Text> TextScratch = new List<Text>(16);

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

        // Row pitch of a scroll content along the scroll axis: own text line, own grid cell + spacing, else a bounded scan
        // of active children (preferring nested GridLayoutGroups and representative item rows over leading section headers);
        // only tall non-grid blocks (text panels) fall back to their body text's line height.
        internal static float Pitch(RectTransform content, bool vertical, float view)
        {
            var line = Line(content);
            if (line > 0f) return line;
            if (content.TryGetComponent<GridLayoutGroup>(out var grid))
                return GridPitch(grid, vertical);
            var group = content.GetComponent<HorizontalOrVerticalLayoutGroup>();
            var spacing = group && (group is VerticalLayoutGroup) == vertical ? group.spacing : 0f;
            int scanned = 0, gridCount = 0, rowCount = 0;
            RectTransform tallBlock = null;
            for (var i = 0; i < content.childCount && scanned < ScrollMath.MaxPitchScan; i++)
            {
                if (!(content.GetChild(i) is RectTransform row) || !row.gameObject.activeSelf) continue;
                scanned++;
                if (row.TryGetComponent<GridLayoutGroup>(out var childGrid))
                {
                    var gp = GridPitch(childGrid, vertical);
                    if (gp > 0f && gridCount < GridScratch.Length) GridScratch[gridCount++] = gp;
                    continue;
                }
                var size = vertical ? row.rect.height : row.rect.width;
                if (size <= 0f) continue;
                if (view > 0f && size > view * 0.5f)
                {
                    var nestedGrid = row.GetComponentInChildren<GridLayoutGroup>(false);
                    if (nestedGrid)
                    {
                        var gp = GridPitch(nestedGrid, vertical);
                        if (gp > 0f && gridCount < GridScratch.Length) GridScratch[gridCount++] = gp;
                    }
                    else if (!tallBlock) tallBlock = row;
                    continue;
                }
                if (rowCount < RowScratch.Length) RowScratch[rowCount++] = size + spacing;
            }
            var selected = ScrollMath.SelectPitch(GridScratch, gridCount, RowScratch, rowCount);
            if (selected > 0f) return selected;
            return tallBlock ? LineIn(tallBlock) : 0f;
        }

        private static float GridPitch(GridLayoutGroup grid, bool vertical) =>
            vertical ? grid.cellSize.y + grid.spacing.y : grid.cellSize.x + grid.spacing.x;

        private static float LineIn(RectTransform block)
        {
            block.GetComponentsInChildren(false, TmpScratch);
            var n = 0;
            for (var i = 0; i < TmpScratch.Count && n < LineScratch.Length; i++)
            {
                var h = Line(TmpScratch[i].rectTransform);
                if (h > 0f) LineScratch[n++] = h;
            }
            TmpScratch.Clear();
            var line = ScrollMath.SelectTextLine(LineScratch, n);
            if (line > 0f) return line;

            block.GetComponentsInChildren(false, TextScratch);
            n = 0;
            for (var i = 0; i < TextScratch.Count && n < LineScratch.Length; i++)
            {
                var h = Line(TextScratch[i].rectTransform);
                if (h > 0f) LineScratch[n++] = h;
            }
            TextScratch.Clear();
            return ScrollMath.SelectTextLine(LineScratch, n);
        }

        private static float Line(RectTransform t)
        {
            if (t.TryGetComponent<TMP_Text>(out var tmp))
                return tmp.textInfo != null && tmp.textInfo.lineCount > 0 ? tmp.textInfo.lineInfo[0].lineHeight : tmp.fontSize;
            return t.TryGetComponent<Text>(out var text) ? text.fontSize * text.lineSpacing : 0f;
        }
    }
}
