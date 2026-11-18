using Morgott.UI.InfoScreens;
using UnityEngine;
using UnityEngine.UI;

namespace Morgott.UI.Restyle
{
    // Thin coloured outline inside a plate (changelog chips / tags, station queue frames): four edge strokes joined by 45°
    // corner strokes that follow the plate's chamfer, one closed line. Starts clear; the owner colours every stroke.
    public static class OutlineFrame
    {
        private const float Chamfer = 4f, Inset = 0.5f;

        public static Image[] Build(Transform host, float thick)
        {
            var strokes = new Image[8];
            for (var i = 0; i < 4; i++)
            {
                var rt = (strokes[i] = Stroke(host, "Rim" + i)).rectTransform;
                var horizontal = i < 2;
                var far = i == 0 || i == 3; // top / right edge
                var near = far ? -(Inset + thick) : Inset;
                var anchor = far ? 1f : 0f;
                rt.anchorMin = horizontal ? new Vector2(0f, anchor) : new Vector2(anchor, 0f);
                rt.anchorMax = horizontal ? new Vector2(1f, anchor) : new Vector2(anchor, 1f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.offsetMin = horizontal ? new Vector2(Chamfer, near) : new Vector2(near, Chamfer);
                rt.offsetMax = horizontal ? new Vector2(-Chamfer, near + thick) : new Vector2(near + thick, -Chamfer);
            }
            OutlineMath.Corner(Chamfer, Inset, thick, out var centre, out var length);
            for (var i = 0; i < 4; i++)
            {
                var rt = (strokes[4 + i] = Stroke(host, "RimCorner" + i)).rectTransform;
                var right = i == 1 || i == 2;
                var top = i < 2;
                rt.anchorMin = rt.anchorMax = new Vector2(right ? 1f : 0f, top ? 1f : 0f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(length, thick);
                rt.anchoredPosition = new Vector2(right ? -centre : centre, top ? -centre : centre);
                rt.localRotation = Quaternion.Euler(0f, 0f, right == top ? -45f : 45f);
            }
            return strokes;
        }

        private static Image Stroke(Transform host, string name)
        {
            var img = InfoArt.OwnImage(host, name, "ib.rule", "outline stroke " + name);
            img.raycastTarget = false;
            img.enabled = true;
            img.preserveAspect = false;
            img.color = Color.clear;
            return img;
        }
    }
}
