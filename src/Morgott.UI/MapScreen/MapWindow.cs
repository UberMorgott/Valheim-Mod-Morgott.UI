using UnityEngine;

namespace Morgott.UI.MapScreen
{
    // The map window is always full screen (user 2026-11-10, mockup docs\mockups\2026-11-10\biome-overlay.png): the frame
    // sits on the screen's safe-area edges (re-fitted every frame: resolution / safe-area changes), the side panel stays
    // docked right and the map image fills the rest of the content rect (LargeLateUpdate). No maximize / restore button,
    // the window does not move.
    public sealed partial class MinimapSkin
    {
        private void FitSafeArea()
        {
            if (!_large || !(_large.parent is RectTransform parent)) return;
            var canvas = _large.GetComponentInParent<Canvas>();
            var root = canvas ? canvas.rootCanvas : null;
            var cam = root && root.renderMode != RenderMode.ScreenSpaceOverlay ? root.worldCamera : null;
            var safe = Screen.safeArea;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, safe.min, cam, out var a)) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, safe.max, cam, out var b)) return;
            var origin = parent.rect.min;
            // Anchored at the parent's bottom-left corner: offsets = the safe-area corners relative to the parent rect min.
            var offMin = Vector2.Min(a, b) - origin;
            var offMax = Vector2.Max(a, b) - origin;
            if (_large.anchorMin != Vector2.zero) _large.anchorMin = Vector2.zero;
            if (_large.anchorMax != Vector2.zero) _large.anchorMax = Vector2.zero;
            if ((_large.offsetMin - offMin).sqrMagnitude > 0.0001f) _large.offsetMin = offMin;
            if ((_large.offsetMax - offMax).sqrMagnitude > 0.0001f) _large.offsetMax = offMax;
            var drag = _header ? _header.Drag : null;
            if (drag && drag.enabled) drag.enabled = false;
        }

        private Vector2 _mapSize;

        // The map image changed size (resolution, content inset, hint band): vanilla re-fits the uv only in its next
        // UpdateMap and re-places pins only when the centre / zoom changed (Minimap.UpdatedMap, Minimap.cs:1290), so pins
        // would sit stale. Mirror UpdateMap's large-mode uv (Minimap.cs:1263-1270: width = LargeZoom x aspect, height =
        // LargeZoom, same centre) and re-place the pins now (UpdatePins).
        private void RefitMapView()
        {
            var img = _map.m_mapImageLarge;
            if (!img) return;
            var size = img.rectTransform.rect.size;
            if ((size - _mapSize).sqrMagnitude < 0.01f || !(size.x > 0f) || !(size.y > 0f)) return;
            _mapSize = size;
            var uv = img.uvRect;
            var centre = uv.center;
            uv.width = _map.LargeZoom * (size.x / size.y);
            uv.height = _map.LargeZoom;
            uv.center = centre;
            img.uvRect = uv;
            _map.UpdatePins();
        }
    }
}
