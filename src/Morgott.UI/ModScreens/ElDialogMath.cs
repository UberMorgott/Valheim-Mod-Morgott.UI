using System;

namespace Morgott.UI.ModScreens
{
    // Pure math of the Epic Loot modal dialogs (ElDialogs; unit tested). Frame units, y up from the frame's bottom edge.
    public static class ElDialogMath
    {
        // Epic Loot's augment choice stack (AugmentHelper.CreateAugmentChoiceDialog, AugmentHelper.cs:88): 300 x 40
        // buttons, centres at 55 + k * 45 from the frame bottom -> lowest bottom edge 35, gap 5.
        public const float ElBase = 35f, ElGap = 5f, ElHeight = 40f;

        public struct ChoiceLayout
        {
            public float[] Bottoms;     // bottom edge of each button, index 0 = top button (Epic Loot's order)
            public float Top;           // top edge of the stack
            public float ScrollHeight;  // description scroll view height above it
        }

        // The stack sits above the bottom corner caps (capBand = their top edge over the frame bottom) and never lower
        // than Epic Loot's own base; the description view (top inset scrollTop) ends a gap above the stack, at least
        // minScroll tall.
        public static ChoiceLayout Stack(float frameH, float scrollTop, float[] heights, float capBand, float gap, float minScroll)
        {
            var n = heights?.Length ?? 0;
            var s = new ChoiceLayout { Bottoms = new float[n] };
            var y = Math.Max(ElBase, capBand + gap);
            for (var i = n - 1; i >= 0; i--)
            {
                s.Bottoms[i] = y;
                y += heights[i] + (i > 0 ? gap : 0f);
            }
            s.Top = n > 0 ? y : Math.Max(ElBase, capBand + gap);
            s.ScrollHeight = Math.Max(minScroll, frameH - scrollTop - s.Top - gap);
            return s;
        }

        // Epic Loot's choice button width (AugmentHelper.cs:88).
        public const float ElWidth = 300f;

        // Choice button width above the corner caps: the frame between its drawn side borders (left / right inset, frame
        // units, border + row pad), never narrower than Epic Loot's 300.
        public static float InnerWidth(float frameWidth, float left, float right) =>
            Math.Max(ElWidth, frameWidth - Math.Max(0f, left) - Math.Max(0f, right));

        // Epic Loot's dialog frame width (AugmentHelper.CreateDialog / CraftSuccessDialog.Create: 380 x 550).
        public const float ElFrameWidth = 380f;

        // Epic Loot's augment dialog base height, per-extra-choice step, and baseline description scroll height
        // (AugmentHelper.CreateAugmentChoiceDialog: 550 for <=3 choices, +45 per extra, 300 scroll height).
        public const float ElFrameBaseHeight = 550f, ElFrameRowStep = 45f, ElScrollHeight = 300f;

        // Least gap between the widened frame and the safe area's side edges (frame units).
        public const float ScreenMargin = 24f;

        // Right-pinned description scrollbar insets inside ScrollView (frame units).
        public const float ScrollbarPad = 2f, ScrollbarGap = 4f, DefaultScrollbarWidth = 14f;

        // Frame width that shows the widest content on one line: content width + the frame's side insets (left / right,
        // frame units), never narrower than Epic Loot's 380, never wider than the room on screen (maxWidth; at least 380:
        // a screen narrower than Epic Loot's own frame keeps its frame and the labels wrap).
        public static float FrameWidth(float contentWidth, float left, float right, float maxWidth)
        {
            var want = Math.Max(0f, contentWidth) + Math.Max(0f, left) + Math.Max(0f, right);
            return Math.Max(ElFrameWidth, Math.Min(want, Math.Min(maxWidth, MaxFrameWidth)));
        }

        // Readable line length: a sentence-long tooltip line in the craft result wraps instead of a screen-wide box
        // (twice Epic Loot's frame; the longest augment label needs far less).
        public const float MaxFrameWidth = 2f * ElFrameWidth;

        // Fixed stable width for AugmentChoiceDialog: wide enough for RU affix lines without jumping width between rolls,
        // capped to the safe area (never narrower than Epic Loot's 380).
        public const float AugmentFrameWidth = MaxFrameWidth;

        public static float StableFrameWidth(float maxWidth) =>
            Math.Max(ElFrameWidth, Math.Min(maxWidth, AugmentFrameWidth));

        public static float ElFrameHeight(int choiceCount) =>
            ElFrameBaseHeight + Math.Max(0, choiceCount - 3) * ElFrameRowStep;

        // Dialog frame height grown so the bottom cap band, taller wrapped choice buttons, and the item description
        // fit cleanly without shrinking the description below Epic Loot's 300u scroll area; capped to the safe area.
        public static float FrameHeight(float minFrameH, float scrollTop, float stackTop, float descNeeded, float gap, float minScroll, float maxFrameH)
        {
            var wantScroll = Math.Max(Math.Max(minScroll, ElScrollHeight), descNeeded);
            var want = Math.Max(minFrameH, scrollTop + wantScroll + gap + stackTop);
            var floor = scrollTop + Math.Max(0f, minScroll) + gap + stackTop;
            var ceiling = Math.Max(floor, maxFrameH);
            return Math.Min(want, ceiling);
        }

        // Snap a scrolling description viewport to whole text lines (CraftingMath.WholeLines) so the bottom edge never
        // cuts a line in half. Only snaps when the description overflows rawScrollH.
        public static float SnapScrollHeight(float rawScrollH, float descNeeded, float lineFirst, float linePitch, float minScroll)
        {
            if (descNeeded <= rawScrollH + 0.5f || lineFirst <= 0f || linePitch <= 0f) return rawScrollH;
            var snapped = CraftingScreen.CraftingMath.WholeLines(rawScrollH, lineFirst, linePitch);
            return snapped >= minScroll ? snapped : rawScrollH;
        }

        // Right-pinned scrollbar width inside ScrollView (clamped so it never dominates a narrow view).
        public static float ScrollbarWidth(float tokenWidth, float scrollWidth)
        {
            var w = tokenWidth > 1f ? tokenWidth : DefaultScrollbarWidth;
            return scrollWidth > 0f ? Math.Min(w, Math.Max(4f, scrollWidth * 0.2f)) : w;
        }

        // Description text width inside its viewport, ending clear of the right-pinned scrollbar (barLeft - ScrollbarGap).
        public static float DescriptionWidth(float textLeft, float viewRight, float barLeft, bool hasBar, float scaleX)
        {
            var right = viewRight;
            if (hasBar)
            {
                var limit = barLeft - ScrollbarGap;
                if (limit > textLeft && limit < right) right = limit;
            }
            var span = right - textLeft;
            if (span <= 0f) return 0f;
            return span / Math.Max(Math.Abs(scaleX), 1e-6f);
        }

        // Shift (x, frame units) that centres the drawn [xMin, xMax] on [lo, hi], like a vanilla modal: the frame Epic Loot
        // placed right of centre grew further right when widened (run 20261114-011832: x 1063..1923 at 1920).
        public static float CentreOn(float xMin, float xMax, float lo, float hi) => (lo + hi) * 0.5f - (xMin + xMax) * 0.5f;

        // A label stays on one line (no wrap) when its unwrapped width at the readable floor fits its text width.
        public static bool OneLine(float floorWidth, float textWidth) => floorWidth <= textWidth + 0.5f;

        // Epic Loot's description font size (CraftSuccessDialog.ConvertToScrollingDescription: fontSize 18): the dialog's
        // body text, and the choice labels' size - a label never reads bigger than the description it sits under.
        public const float ElBodySize = 18f;

        // A choice label's fixed size (no auto-size): the body size when its one-line width there (widthAtBody) fits the
        // text width; else shrunk on one line in proportion (TMP glyph advance is linear in the size) down to the readable
        // floor; below the floor it stays at the floor and wraps.
        public static float LabelSize(float body, float floor, float widthAtBody, float textWidth, out bool wrap)
        {
            wrap = false;
            if (widthAtBody <= 0f || widthAtBody <= textWidth + 0.5f) return body;
            var s = body * Math.Max(0f, textWidth) / widthAtBody;
            if (s >= floor) return (float)Math.Floor(s * 10f) / 10f;
            wrap = true;
            return floor;
        }

        // Button height for a label that needs textNeeded (at the readable floor, wrapped in the button's text width):
        // Epic Loot's height, or the label plus its vertical padding when it needs more (no clipping).
        public static float ButtonHeight(float baseHeight, float textNeeded, float vpad) =>
            Math.Max(baseHeight, textNeeded + 2f * Math.Max(0f, vpad));

        // A RootMark on an object that is no registered root came with Instantiate from a RuneSkin root (Epic Loot clones
        // the restyled vanilla variant dialog): it is no root, and as a "nested root" it would keep the owning unit's pass
        // off the whole dialog.
        public static bool IsStaleRootMark(bool hasMark, bool registeredRoot) => hasMark && !registeredRoot;
    }
}
