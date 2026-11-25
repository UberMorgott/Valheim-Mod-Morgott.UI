using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Morgott.UI.Restyle
{
    // Unified right-click drag-to-scroll on every Unity ScrollRect (vanilla, ours, other mods'):
    // - skips ScrollRects that already have a custom IDragHandler relay on the same object (e.g. GemVaultTab ScrollDragRelay),
    //   preventing double delivery;
    // - skips presses starting on a Slider, Scrollbar, text input, or UIInputHandler with m_onRightDown (InventoryGrid cells
    //   fire UseItem synchronously on pointer-down and mods like EAQS / MultiUserChest hook that exact lifecycle);
    // - waits for max(EventSystem.pixelDragThreshold, 6px) and suppresses OnPointerClick once dragging begins.
    [HarmonyPatch]
    public static class ScrollDrag
    {
        private static readonly List<IDragHandler> DragScratch = new List<IDragHandler>(4);
        private static ScrollRect _rmbScroll;
        private static ScrollRect _lmbScroll;
        private static bool _rmbDragging;
        private static bool _forwarding;

        internal static float Threshold() =>
            ScrollMath.DragThreshold(EventSystem.current ? EventSystem.current.pixelDragThreshold : 0f);

        internal static void SuppressClick(PointerEventData e)
        {
            if (e == null) return;
            e.eligibleForClick = false;
            e.pointerClick = null;
        }

        private static bool CanScroll(ScrollRect sr)
        {
            if (!sr || !sr.content) return false;
            var view = sr.viewport ? sr.viewport.rect : ((RectTransform)sr.transform).rect;
            var cr = sr.content.rect;
            var s = sr.content.localScale;
            return (sr.vertical && cr.height * Mathf.Abs(s.y) > view.height + 1f) ||
                (sr.horizontal && cr.width * Mathf.Abs(s.x) > view.width + 1f);
        }

        private static bool HasCustomDragRelay(ScrollRect sr)
        {
            sr.gameObject.GetComponents(DragScratch);
            var custom = false;
            for (var i = 0; i < DragScratch.Count; i++)
                if (!ReferenceEquals(DragScratch[i], sr)) { custom = true; break; }
            DragScratch.Clear();
            return custom;
        }

        internal static bool StartsOnBlockedControl(PointerEventData e, Transform stopAt, bool skipRightDown = true)
        {
            var go = e.pointerPressRaycast.gameObject ? e.pointerPressRaycast.gameObject : e.rawPointerPress;
            for (var t = go ? go.transform : null; t && t != stopAt; t = t.parent)
            {
                if (t.GetComponent<Slider>() || t.GetComponent<Scrollbar>() ||
                    t.GetComponent<TMP_InputField>() || t.GetComponent<InputField>())
                    return true;
                if (skipRightDown && t.TryGetComponent<UIInputHandler>(out var h) && h.m_onRightDown != null)
                    return true;
            }
            return false;
        }

        private static bool Allowed(ScrollRect sr, PointerEventData e) =>
            sr && e != null && sr.IsActive() && sr != _lmbScroll && CanScroll(sr) &&
            !HasCustomDragRelay(sr) && !StartsOnBlockedControl(e, sr.transform);

        [HarmonyPatch(typeof(ScrollRect), nameof(ScrollRect.OnInitializePotentialDrag))]
        [HarmonyPrefix]
        private static void OnInitializePotentialDrag_Prefix(ScrollRect __instance, PointerEventData eventData, out bool __state)
        {
            __state = false;
            if (eventData == null || eventData.button != PointerEventData.InputButton.Right || !Allowed(__instance, eventData)) return;
            _rmbScroll = __instance;
            _rmbDragging = false;
            eventData.button = PointerEventData.InputButton.Left;
            __state = true;
        }

        [HarmonyPatch(typeof(ScrollRect), nameof(ScrollRect.OnInitializePotentialDrag))]
        [HarmonyFinalizer]
        private static void OnInitializePotentialDrag_Finalizer(PointerEventData eventData, bool __state)
        {
            if (__state && eventData != null) eventData.button = PointerEventData.InputButton.Right;
        }

        [HarmonyPatch(typeof(ScrollRect), nameof(ScrollRect.OnBeginDrag))]
        [HarmonyPrefix]
        private static void OnBeginDrag_Prefix(ScrollRect __instance, PointerEventData eventData, out bool __state)
        {
            __state = false;
            if (_forwarding || eventData == null) return;
            if (eventData.button == PointerEventData.InputButton.Left) { _lmbScroll = __instance; return; }
            if (eventData.button != PointerEventData.InputButton.Right || !Allowed(__instance, eventData)) return;
            _rmbScroll = __instance;
            _rmbDragging = false;
            if (!ScrollMath.PassedDragThreshold(eventData.pressPosition.x, eventData.pressPosition.y, eventData.position.x, eventData.position.y, Threshold()))
                return;
            _rmbDragging = true;
            SuppressClick(eventData);
            eventData.button = PointerEventData.InputButton.Left;
            __state = true;
        }

        [HarmonyPatch(typeof(ScrollRect), nameof(ScrollRect.OnBeginDrag))]
        [HarmonyFinalizer]
        private static void OnBeginDrag_Finalizer(PointerEventData eventData, bool __state)
        {
            if (__state && eventData != null) eventData.button = PointerEventData.InputButton.Right;
        }

        [HarmonyPatch(typeof(ScrollRect), nameof(ScrollRect.OnDrag))]
        [HarmonyPrefix]
        private static void OnDrag_Prefix(ScrollRect __instance, PointerEventData eventData, out bool __state)
        {
            __state = false;
            if (eventData == null || eventData.button != PointerEventData.InputButton.Right || __instance != _rmbScroll) return;
            if (!_rmbDragging)
            {
                if (!ScrollMath.PassedDragThreshold(eventData.pressPosition.x, eventData.pressPosition.y, eventData.position.x, eventData.position.y, Threshold()))
                    return;
                _rmbDragging = true;
                SuppressClick(eventData);
                _forwarding = true;
                eventData.button = PointerEventData.InputButton.Left;
                try { __instance.OnBeginDrag(eventData); }
                finally { eventData.button = PointerEventData.InputButton.Right; _forwarding = false; }
            }
            SuppressClick(eventData);
            eventData.button = PointerEventData.InputButton.Left;
            __state = true;
        }

        [HarmonyPatch(typeof(ScrollRect), nameof(ScrollRect.OnDrag))]
        [HarmonyFinalizer]
        private static void OnDrag_Finalizer(PointerEventData eventData, bool __state)
        {
            if (__state && eventData != null) eventData.button = PointerEventData.InputButton.Right;
        }

        [HarmonyPatch(typeof(ScrollRect), nameof(ScrollRect.OnEndDrag))]
        [HarmonyPrefix]
        private static void OnEndDrag_Prefix(ScrollRect __instance, PointerEventData eventData, out bool __state)
        {
            __state = false;
            if (eventData == null) return;
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                if (__instance == _lmbScroll) _lmbScroll = null;
                return;
            }
            if (eventData.button != PointerEventData.InputButton.Right || __instance != _rmbScroll) return;
            var wasDragging = _rmbDragging;
            _rmbScroll = null;
            _rmbDragging = false;
            if (!wasDragging) return;
            SuppressClick(eventData);
            eventData.button = PointerEventData.InputButton.Left;
            __state = true;
        }

        [HarmonyPatch(typeof(ScrollRect), nameof(ScrollRect.OnEndDrag))]
        [HarmonyFinalizer]
        private static void OnEndDrag_Finalizer(PointerEventData eventData, bool __state)
        {
            if (__state && eventData != null) eventData.button = PointerEventData.InputButton.Right;
        }

        [HarmonyPatch(typeof(ScrollRect), "OnDisable")]
        [HarmonyPostfix]
        private static void OnDisable_Postfix(ScrollRect __instance)
        {
            if (__instance == _rmbScroll) { _rmbScroll = null; _rmbDragging = false; }
            if (__instance == _lmbScroll) _lmbScroll = null;
        }
    }

    // Reusable mouse drag-to-scroll behaviour for custom non-ScrollRect panels (continuous or discrete rows).
    internal sealed class DragScroll : MonoBehaviour,
        IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public bool AllowLeft = true;
        public bool AllowRight = true;
        public RectTransform Space;
        public Func<PointerEventData, bool> CanStart;
        public Func<float> StepSize;
        public Action<int, PointerEventData> OnStep;
        public Action<float, PointerEventData> OnDeltaY;

        private ScrollMath.DragScrollDelta _state;
        private PointerEventData.InputButton _button;

        private bool Accepts(PointerEventData.InputButton b) =>
            (AllowLeft && b == PointerEventData.InputButton.Left) ||
            (AllowRight && b == PointerEventData.InputButton.Right);

        private RectTransform CoordSpace => Space ? Space : transform as RectTransform;

        private bool LocalPoint(PointerEventData e, Vector2 screen, out Vector2 local)
        {
            var rt = CoordSpace;
            if (rt && RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screen, e.pressEventCamera, out local))
                return true;
            local = screen;
            return false;
        }

        public void OnInitializePotentialDrag(PointerEventData e) { }

        public void OnBeginDrag(PointerEventData e)
        {
            if (e == null || !Accepts(e.button)) return;
            if (ScrollDrag.StartsOnBlockedControl(e, transform, skipRightDown: false)) return;
            if (CanStart != null && !CanStart(e)) return;
            if (!LocalPoint(e, e.pressPosition, out var start)) return;
            _button = e.button;
            _state.Begin(e.pressPosition.x, e.pressPosition.y, start.x, start.y);
            ApplyMove(e);
        }

        public void OnDrag(PointerEventData e)
        {
            if (e == null || !_state.Active || e.button != _button) return;
            ApplyMove(e);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (e == null || !_state.Active || e.button != _button) return;
            if (_state.End()) ScrollDrag.SuppressClick(e);
        }

        private void OnDisable() => _state.Reset();

        private void ApplyMove(PointerEventData e)
        {
            if (!LocalPoint(e, e.position, out var cur)) return;
            if (!_state.Move(e.position.x, e.position.y, cur.x, cur.y, ScrollDrag.Threshold(), out _, out var dy)) return;
            ScrollDrag.SuppressClick(e);
            if (OnStep != null && StepSize != null)
            {
                var steps = _state.StepY(dy, StepSize());
                if (steps != 0) OnStep(steps, e);
            }
            else if (Mathf.Abs(dy) > 1e-4f)
            {
                OnDeltaY?.Invoke(dy, e);
            }
        }
    }
}
