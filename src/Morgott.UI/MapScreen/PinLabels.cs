using Morgott.UI.Theme;
using TMPro;
using UnityEngine;

namespace Morgott.UI.MapScreen
{
    // Pin name labels (both maps). Vanilla gives a pin icon and its name label the SAME anchoredPosition but parents them
    // to different roots (Minimap.UpdatePins, Minimap.cs:1620-1658: m_pinRoot* vs m_pinNameRoot*), so the label sits on
    // its pin only while both roots cover the same rect. The restyle resizes the map image that holds the pin roots
    // (large map: inset left of the legend and above the key band; small map: the ring hole) but not the name roots:
    // every label drifted from its pin by the difference of the two rects (screenshot 2026-10-22: "Day N" labels away
    // from their death pins). Fix, one rule for every pin (vanilla, death pins, Almanac / PotalMap / other mods' pins,
    // all in Minimap.m_pins): each name root is laid out as a copy of its pin root (same parent, anchors, offsets,
    // scale), the label is reparented to the current mode's name root (vanilla leaves a label created on the small map
    // in the small root, :1641), hung a fixed gap under its pin icon (never over it), set in the theme's readable font
    // with a minimum size, and colliding labels are decluttered (own pins before shared ones, then pin order).
    // Small map: labels also follow the pin cull (outside the ring hole = both hidden).
    public sealed partial class MinimapSkin
    {
        private PinLabelMath.Box[] _labelBoxes = new PinLabelMath.Box[64];
        private GameObject[] _labelObjects = new GameObject[64];
        private RectTransform[] _labelRects = new RectTransform[64];
        private Vector3[] _labelPositions = new Vector3[64];
        private int[] _labelOrder = new int[64];
        private bool[] _labelShown = new bool[64];
        private int _labelCount;
        private Minimap.MapMode _labelStateMode = (Minimap.MapMode)(-1);
        private int _labelStatePinsVer = -1, _labelStateFilterVer = -1, _labelStateThemeRev = -1, _labelStatePinCount = -1, _labelStateSig;
        private float _labelStateZoom = -1f;
        private Vector3 _labelStateOffset;
        private Rect _labelStateRect;

        // Name root = geometric copy of the pin root, drawn right above it (labels over pins, under later map children
        // such as the player marker or Jotunn's overlay panel).
        private static void MatchRoot(RectTransform names, RectTransform pins)
        {
            if (!names || !pins || names == pins) return;
            if (names.parent != pins.parent) names.SetParent(pins.parent, false);
            if (names.anchorMin != pins.anchorMin) names.anchorMin = pins.anchorMin;
            if (names.anchorMax != pins.anchorMax) names.anchorMax = pins.anchorMax;
            if (names.pivot != pins.pivot) names.pivot = pins.pivot;
            if (names.offsetMin != pins.offsetMin) names.offsetMin = pins.offsetMin;
            if (names.offsetMax != pins.offsetMax) names.offsetMax = pins.offsetMax;
            if (names.localScale != pins.localScale) names.localScale = pins.localScale;
            if (names.localRotation != pins.localRotation) names.localRotation = pins.localRotation;
            var target = pins.GetSiblingIndex();
            var at = names.GetSiblingIndex();
            if (at < target) names.SetSiblingIndex(target);          // pins shift down by one: lands right after them
            else if (at != target + 1) names.SetSiblingIndex(target + 1);
        }

        private void MatchNameRoots()
        {
            MatchRoot(_map.m_pinNameRootSmall, _map.m_pinRootSmall);
            MatchRoot(_map.m_pinNameRootLarge, _map.m_pinRootLarge);
        }

        private static bool Hidden(GameObject go) => go.TryGetComponent<CanvasGroup>(out var g) && g.alpha <= 0f;

        private void PlaceLabels()
        {
            var pins = _map.m_pins;
            if (pins == null) return;
            var mode = _map.m_mode;
            var large = mode == Minimap.MapMode.Large;
            var mapImg = large ? _map.m_mapImageLarge : _map.m_mapImageSmall;
            var mapRect = mapImg ? mapImg.rectTransform.rect : default;
            var zoom = large ? _map.LargeZoom : _map.SmallZoom;
            var offset = _map.m_mapOffset;
            var pinsVer = PinsVersion;
            var filterVer = _customFilterVersion;
            var themeRev = ThemeTokens.Revision;
            var pinCount = pins.Count;
            var sig = 17;
            for (var i = 0; i < pinCount; i++)
            {
                var pin = pins[i];
                var data = pin.m_NamePinData;
                var go = data?.PinNameGameObject;
                var icon = pin.m_uiElement;
                if (!go || !icon) continue;
                var flags = (icon.gameObject.activeSelf ? 1 : 0) | (go.activeSelf ? 2 : 0) | (Hidden(icon.gameObject) ? 4 : 0);
                sig = sig * 31 + flags;
                if (go.activeSelf)
                {
                    var txt = data.PinNameText ? data.PinNameText.text : null;
                    sig = sig * 31 + (txt != null ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(txt) : 0);
                }
            }
            if (mode == _labelStateMode && pinsVer == _labelStatePinsVer && filterVer == _labelStateFilterVer && themeRev == _labelStateThemeRev
                && Mathf.Approximately(zoom, _labelStateZoom) && offset == _labelStateOffset && mapRect == _labelStateRect
                && pinCount == _labelStatePinCount && sig == _labelStateSig)
            {
                // Re-apply cached label positions if anything overwrote them since the last declutter pass.
                for (var i = 0; i < _labelCount; i++)
                {
                    var rt = _labelRects[i];
                    if (!rt) continue;
                    var want = _labelPositions[i];
                    if ((rt.localPosition - want).sqrMagnitude > 0.0001f) rt.localPosition = want;
                }
                return;
            }
            _labelStateMode = mode;
            _labelStatePinsVer = pinsVer;
            _labelStateFilterVer = filterVer;
            _labelStateThemeRev = themeRev;
            _labelStateZoom = zoom;
            _labelStateOffset = offset;
            _labelStateRect = mapRect;
            _labelStatePinCount = pinCount;
            _labelStateSig = sig;

            MatchNameRoots();
            var root = large ? _map.m_pinNameRootLarge : _map.m_pinNameRootSmall;
            var font = ThemeTokens.GetFont("font.body");
            var min = ThemeTokens.GetFloat("size.map.pin.label.min", 14f);
            var gap = ThemeTokens.GetFloat("size.map.pin.label.gap", 2f);
            var n = 0;
            for (var pi = 0; pi < pinCount; pi++)
            {
                var pin = pins[pi];
                var data = pin.m_NamePinData;
                var go = data?.PinNameGameObject;
                if (!go || !pin.m_uiElement) continue;
                var rt = data.PinNameRectTransform;
                if (root && rt.parent != root) rt.SetParent(root, false);
                var text = data.PinNameText;
                if (text)
                {
                    if (font && text.font != font)
                    {
                        text.font = font;
                        if (font.material) text.fontSharedMaterial = font.material;
                    }
                    if (text.enableAutoSizing) text.enableAutoSizing = false;
                    if (text.fontSize < min) text.fontSize = min;
                    // Defense-in-depth against DelayActivation(1s): never display raw [mp1:...] metadata suffix.
                    if (CustomPinMath.TryDecode(pin.m_name, out var decoded))
                    {
                        if (!IsCustomIconVisible(decoded.IconId) || string.IsNullOrEmpty(decoded.VisibleName))
                        {
                            if (go.activeSelf) go.SetActive(false);
                            continue;
                        }
                        var stripped = CustomPinMath.StripTag(text.text);
                        if (text.text != stripped) text.text = stripped;
                    }
                }
                if (!pin.m_uiElement.gameObject.activeSelf)
                {
                    if (go.activeSelf) go.SetActive(false);
                    continue;
                }
                if (!go.activeSelf) continue; // vanilla shows names on the large map only, zoomed in (Minimap.cs:1678)

                var icon = pin.m_uiElement;
                var p = icon.localPosition;
                var bottom = p.y + icon.rect.yMin * icon.localScale.y;
                Vector2 top;
                var tr = text ? text.rectTransform : rt;
                var bounds = text ? text.textBounds : default;
                if (text && bounds.size.x <= 0f && !string.IsNullOrEmpty(text.text))
                {
                    text.ForceMeshUpdate();
                    bounds = text.textBounds;
                }
                var topLocal = bounds.size.x > 0f
                    ? new Vector3(bounds.center.x, bounds.max.y, 0f)
                    : new Vector3(tr.rect.center.x, tr.rect.yMax, 0f);
                top = rt.InverseTransformPoint(tr.TransformPoint(topLocal));
                top = Vector2.Scale(top, rt.localScale);
                PinLabelMath.Hang(p.x, bottom, gap, top.x, top.y, out var x, out var y);
                var want = new Vector3(x, y, p.z);
                if ((rt.localPosition - want).sqrMagnitude > 0.0001f) rt.localPosition = want;

                if (n == _labelBoxes.Length) Grow();
                var w = bounds.size.x > 0f ? bounds.size.x * rt.localScale.x : tr.rect.width;
                var h = bounds.size.y > 0f ? bounds.size.y * rt.localScale.y : tr.rect.height;
                var y1 = bottom - gap;
                _labelBoxes[n] = new PinLabelMath.Box
                {
                    X0 = p.x - w * 0.5f,
                    X1 = p.x + w * 0.5f,
                    Y0 = y1 - h,
                    Y1 = y1,
                    // Culled pin (small map, outside the ring) = its label never claims space.
                    Priority = Hidden(icon.gameObject) ? -1 : pin.m_ownerID == 0L ? 2 : 1,
                    Order = n,
                };
                _labelObjects[n] = go;
                _labelRects[n] = rt;
                _labelPositions[n] = want;
                n++;
            }
            PinLabelMath.Declutter(_labelBoxes, n, _labelOrder, _labelShown, ThemeTokens.GetFloat("size.map.pin.label.pad", 1f));
            for (var i = 0; i < n; i++)
            {
                Show(_labelObjects[i], _labelShown[i] && _labelBoxes[i].Priority >= 0);
                _labelObjects[i] = null;
            }
            for (var i = n; i < _labelCount; i++) _labelRects[i] = null;
            _labelCount = n;
        }

        private void Grow()
        {
            var size = _labelBoxes.Length * 2;
            System.Array.Resize(ref _labelBoxes, size);
            System.Array.Resize(ref _labelObjects, size);
            System.Array.Resize(ref _labelRects, size);
            System.Array.Resize(ref _labelPositions, size);
            System.Array.Resize(ref _labelOrder, size);
            System.Array.Resize(ref _labelShown, size);
        }
    }
}
