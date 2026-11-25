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
        internal const int MaxPitchScan = 16;
        internal const float MinBodyLinePx = 16f;
        internal const float MinDragThresholdPx = 6f;

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

        // Representative item pitch from scanned active children: nested grids (structured cells) beat section headers;
        // otherwise pick the 75th-percentile row pitch so a leading header does not shrink the whole list while a single
        // tall banner does not inflate it.
        internal static float SelectPitch(float[] gridPitches, int gridCount, float[] rowPitches, int rowCount)
        {
            var grid = Percentile(gridPitches, gridCount, 0.75f);
            return grid > 0f ? grid : Percentile(rowPitches, rowCount, 0.75f);
        }

        // Text-block fallback line height: ignore tiny badge text (< 16 pt) whenever body text exists.
        internal static float SelectTextLine(float[] lines, int count)
        {
            if (lines == null || count <= 0) return 0f;
            var n = Math.Min(count, lines.Length);
            var body = 0;
            var maxSmall = 0f;
            for (var i = 0; i < n; i++)
            {
                var v = lines[i];
                if (v >= MinBodyLinePx) body++;
                else if (v > maxSmall) maxSmall = v;
            }
            if (body == 0) return maxSmall;
            var buf = new float[body];
            var k = 0;
            for (var i = 0; i < n; i++)
                if (lines[i] >= MinBodyLinePx) buf[k++] = lines[i];
            Array.Sort(buf);
            return buf[(body - 1) / 2];
        }

        private static float Percentile(float[] values, int count, float share)
        {
            if (values == null || count <= 0) return 0f;
            var n = Math.Min(count, values.Length);
            var valid = 0;
            for (var i = 0; i < n; i++)
                if (values[i] > 0f) valid++;
            if (valid == 0) return 0f;
            var buf = new float[valid];
            var k = 0;
            for (var i = 0; i < n; i++)
                if (values[i] > 0f) buf[k++] = values[i];
            Array.Sort(buf);
            var idx = Math.Min(valid - 1, Math.Max(0, (int)Math.Floor((valid * share) - 0.25f)));
            return buf[idx];
        }

        internal static float DragThreshold(float eventSystemThreshold) =>
            Math.Max(MinDragThresholdPx, eventSystemThreshold);

        internal static bool PassedDragThreshold(float startX, float startY, float x, float y, float threshold)
        {
            var t = Math.Max(0f, threshold);
            var dx = x - startX;
            var dy = y - startY;
            return dx * dx + dy * dy >= t * t;
        }

        // Shared drag-to-scroll state for custom non-ScrollRect panels (continuous delta + discrete row accumulator).
        internal struct DragScrollDelta
        {
            private float _startScreenX, _startScreenY, _lastX, _lastY, _accumY;
            private bool _active, _dragging;

            public bool Active => _active;
            public bool Dragging => _dragging;

            public void Begin(float x, float y) => Begin(x, y, x, y);

            public void Begin(float screenX, float screenY, float localX, float localY)
            {
                _active = true;
                _dragging = false;
                _startScreenX = screenX;
                _startScreenY = screenY;
                _lastX = localX;
                _lastY = localY;
                _accumY = 0f;
            }

            public bool Move(float x, float y, float threshold, out float dx, out float dy) =>
                Move(x, y, x, y, threshold, out dx, out dy);

            public bool Move(float screenX, float screenY, float localX, float localY, float threshold, out float dx, out float dy)
            {
                if (!_active)
                {
                    dx = dy = 0f;
                    return false;
                }
                if (!_dragging)
                {
                    if (!PassedDragThreshold(_startScreenX, _startScreenY, screenX, screenY, threshold))
                    {
                        dx = dy = 0f;
                        return false;
                    }
                    _dragging = true;
                }
                dx = localX - _lastX;
                dy = localY - _lastY;
                _lastX = localX;
                _lastY = localY;
                return true;
            }

            // Accumulates vertical drag (dy > 0 = dragged upward = scroll down = +steps) into whole rows.
            public int StepY(float dy, float rowSize)
            {
                if (rowSize <= 1e-4f) return 0;
                _accumY += dy;
                var steps = (int)(_accumY / rowSize);
                if (steps != 0) _accumY -= steps * rowSize;
                return steps;
            }

            // Returns true when the gesture crossed the drag threshold and the release click should be suppressed.
            public bool End()
            {
                var wasDragging = _dragging;
                Reset();
                return wasDragging;
            }

            public void Reset()
            {
                _active = false;
                _dragging = false;
                _accumY = 0f;
            }
        }
    }
}
