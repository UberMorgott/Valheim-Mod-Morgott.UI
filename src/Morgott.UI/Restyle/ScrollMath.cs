using System;

namespace Morgott.UI.Restyle
{
    // Wheel step of every ScrollRect (ScrollSpeed). ScrollRect.OnScroll moves content by scrollDelta * scrollSensitivity,
    // and one notch is only InputSystemUIInputModule.scrollDeltaPerTick = 0.05 on Valheim's EventSystem (main scene
    // _GameMain/EventSystem), so a sensitivity set in "pixels per notch" (24 / 30 / 40, Epic Loot's MerchantPanel 40)
    // moved 1-2 px per notch.
    internal static class ScrollMath
    {
        internal const float RowsPerNotch = 1.5f;

        // Pixels one notch moves: 1.5 rows / text lines, at most half the view (a notch never skips unseen content),
        // then the [UI] Scroll Speed multiplier. No row found = a sixth of the view.
        internal static float NotchPx(float pitch, float view, float speed)
        {
            var px = pitch > 0f ? pitch * RowsPerNotch : view / 6f;
            if (view > 0f) px = Math.Min(px, view * 0.5f);
            return Math.Max(1f, px) * speed;
        }

        // The sensitivity that makes one notch (deltaPerNotch of scrollDelta) move notchPx.
        internal static float Sensitivity(float notchPx, float deltaPerNotch) =>
            deltaPerNotch > 1e-5f ? notchPx / deltaPerNotch : notchPx;
    }
}
