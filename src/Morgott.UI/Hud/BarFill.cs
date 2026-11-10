using System;

namespace Morgott.UI.PlayerHud
{
    // Drawn fill of a HUD bar (user 2026-10-23: with a small max HP the fill jumped in whole-point steps). The drawn share
    // follows the live share as a critically damped spring (no overshoot, starts and stops softly - no big first-frame
    // step like a plain exponential): `seconds` = time to cover 95 % of a change, frame-rate independent, on the true
    // float value, so the fill edge moves through sub-pixel positions. Pure (unit-tested on net8).
    public static class BarFill
    {
        // shown < 0 = no previous frame (snap). seconds <= 0 = no smoothing (snap). Within 1e-5 of the target with ~no
        // velocity it snaps (no endless tail). vel = share per second, kept by the caller.
        public static float Smooth(float shown, ref float vel, float target, float dt, float seconds)
        {
            if (float.IsNaN(target)) target = 0f;
            target = Math.Max(0f, Math.Min(1f, target));
            if (shown < 0f || seconds <= 0f) { vel = 0f; return target; }
            if (dt <= 0f) return shown;
            // critically damped: x(t) = (1 + w t) e^(-w t) of the gap; 95 % covered at w t = 4.744
            var w = 4.744f / seconds;
            var x = shown - target;
            var e = (float)Math.Exp(-w * dt);
            var t = (vel + w * x) * dt;
            var next = target + (x + t) * e;
            vel = (vel - w * t) * e;
            // never past the target (float error at large dt)
            if ((x > 0f && next < target) || (x < 0f && next > target)) { next = target; vel = 0f; }
            if (Math.Abs(target - next) < 1e-5f && Math.Abs(vel) < 1e-3f) { vel = 0f; return target; }
            return next;
        }
    }

    // Drawn value of a HUD resource bar (user 2026-11-10: Morgott.Patcher smooth regen pays HP in 1-hp steps; at max 5 each
    // step was a 20 % glide-then-stop staircase). In units, so a max change (food eaten / decaying) rescales with no jump:
    // - drop: instant (damage reads at once, never fuller than real; the BarView lag ghost shows the loss);
    // - regular rise (a step at most 2x the last one, its interval within 0.5x..2x the last interval): constant speed =
    //   gap / interval, so the fill arrives as the next tick lands -> continuous motion at the real regen rate, from
    //   per-frame regen (lag <= 1 frame) to any slow tick period (no cap: two ticks agreeing define the period);
    // - irregular rise (heal, potion, two equal heals 30 s apart, first tick after a pause): BarFill.Smooth ease over `seconds`.
    // Pure (unit-tested); dt is the caller's frame time.
    public sealed class BarFollow
    {
        private double _shown = -1.0; // double: 1 hp / 10 s at max 100000, 144 fps adds steps float loses (Codex review)
        private float _last = -1f, _lastStep, _sinceRise = float.MaxValue, _interval = -1f, _speed, _vel;
        private bool _linear;

        public float Shown => (float)_shown;

        // Next frame shows the real value (respawn, teleport, player change, HUD shown again).
        public void Reset() => _shown = -1.0;

        public float Step(float cur, float max, float dt, float seconds)
        {
            if (!(max > 0f)) max = 1f;
            cur = Math.Max(0f, Math.Min(max, float.IsNaN(cur) ? 0f : cur));
            if (dt > 0f && _sinceRise < float.MaxValue) _sinceRise += dt;
            if (_shown < 0.0 || seconds <= 0f)
            {
                _last = cur;
                _shown = cur;
                _vel = 0f;
                _linear = false;
                _sinceRise = float.MaxValue;
                _interval = -1f;
                return cur;
            }
            var step = cur - _last;
            if (step > 1e-6f)
            {
                // regular = a tick like the last one (x2 slack: fractional regen, frame-time jitter) at the same pace
                var regular = _lastStep > 0f && step <= 2f * _lastStep + 1e-4f * max && _interval > 0f && _sinceRise < float.MaxValue
                    && _sinceRise <= 2f * _interval && _sinceRise >= 0.5f * _interval;
                if (regular) _speed = (float)((cur - _shown) / Math.Max(_sinceRise, 1e-4f));
                _linear = regular;
                _lastStep = step;
                _interval = _sinceRise < float.MaxValue ? _sinceRise : -1f;
                _sinceRise = 0f;
                _vel = 0f;
            }
            else if (step < 0f) _shown = Math.Max(0.0, _shown + step); // a hit while the fill trails a tick reads at once too (Codex)
            _last = cur;
            if (cur <= _shown) { _shown = cur; _vel = 0f; return cur; }
            if (_linear) _shown = Math.Min(cur, _shown + (double)_speed * Math.Max(0f, dt));
            else _shown = Math.Min(cur, BarFill.Smooth((float)(_shown / max), ref _vel, cur / max, dt, seconds) * (double)max);
            return (float)_shown;
        }
    }
}
