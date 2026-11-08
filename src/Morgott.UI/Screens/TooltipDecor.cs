using System.Collections.Generic;
using System.Globalization;
using Morgott.UI.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Morgott.UI.Screens
{
    // Images of an item tooltip sheet (TooltipSheet links), children of its text so they scroll with it: the section rules
    // (full text width), the item icon (left of the header lines, as tall as them) and the durability bar (left of its
    // value). Placed from the laid-out text (TMP_TextInfo link / character / line info, text-local space) by the tooltip's
    // placer pass (TooltipPlacer.Pass, right before canvases render) whenever the text or its width changed.
    public sealed class TooltipDecor : MonoBehaviour
    {
        private TMP_Text _text;
        private string _laid;
        private float _width = -1f;
        private int _revision = -1;
        private readonly List<Image> _images = new List<Image>();
        private int _used;

        public static void Ensure(TMP_Text text)
        {
            if (!text) return;
            if (!text.TryGetComponent<TooltipDecor>(out var d)) d = text.gameObject.AddComponent<TooltipDecor>();
            d._text = text;
            d._laid = null;
        }

        // Every decor under the shown tooltip (the item text and Epic Loot's comparison copy).
        internal static void Sync(Transform tip)
        {
            foreach (var d in tip.GetComponentsInChildren<TooltipDecor>(true)) d.Place();
        }

        private void Place()
        {
            if (!_text) _text = GetComponent<TMP_Text>();
            if (!_text) return;
            var rect = _text.rectTransform.rect;
            if (ReferenceEquals(_text.text, _laid) && Mathf.Abs(rect.width - _width) < 0.5f && _revision == ThemeTokens.Revision) return;
            _laid = _text.text;
            _width = rect.width;
            _revision = ThemeTokens.Revision;
            _used = 0;
            if (TooltipSheet.IsSheet(_laid))
            {
                _text.ForceMeshUpdate();
                Lay(_text.textInfo, rect);
            }
            for (var i = _used; i < _images.Count; i++) if (_images[i]) _images[i].enabled = false;
        }

        private void Lay(TMP_TextInfo info, Rect rect)
        {
            var left = rect.xMin + _text.margin.x;
            var right = rect.xMax - _text.margin.z;
            var rule = ThemeTokens.GetFloat("size.tooltip.rule", 1.5f);
            var ruleColor = ThemeTokens.GetColor("color.tooltip.rule", new Color(0.31f, 0.88f, 0.9f, 0.45f));
            float iconTop = float.NaN;
            int iconId = -1;
            for (var i = 0; i < info.linkCount; i++)
            {
                var link = info.linkInfo[i];
                var id = link.GetLinkID();
                if (link.linkTextLength < 1 || link.linkTextfirstCharacterIndex >= info.characterCount) continue;
                var ch = info.characterInfo[link.linkTextfirstCharacterIndex];
                var line = info.lineInfo[ch.lineNumber];
                if (id == TooltipSheet.LinkRule)
                {
                    var img = Next(null, ruleColor);
                    Set(img.rectTransform, new Vector2(0f, 0.5f), new Vector2(left, (line.ascender + line.descender) / 2f), new Vector2(right - left, rule));
                }
                else if (id.StartsWith(TooltipSheet.LinkIcon))
                {
                    int.TryParse(id.Substring(TooltipSheet.LinkIcon.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out iconId);
                    iconTop = line.ascender;
                }
                else if (id == TooltipSheet.LinkHeaderEnd && iconId >= 0 && !float.IsNaN(iconTop))
                {
                    var sprite = TooltipSheetGather.Icon(iconId);
                    if (!sprite) continue;
                    // As tall as the header lines, at most the icon column (TooltipSheet.Style.IconIndent), centred in it.
                    var column = ThemeTokens.GetFloat("size.tooltip.icon", 60f);
                    var size = Mathf.Min(iconTop - line.descender, column);
                    // Framed tile like the mockup: the inventory cell plate, the icon inset in it.
                    var plate = Next(ThemeTokens.GetSprite("sprite.tooltip.icon.plate"), ThemeTokens.GetColor("color.tooltip.icon.plate", Color.white));
                    Set(plate.rectTransform, new Vector2(0f, 1f), new Vector2(left + (column - size) / 2f, iconTop), new Vector2(size, size));
                    var inset = size * ThemeTokens.GetFloat("share.tooltip.icon.inset", 0.12f);
                    var img = Next(sprite, Color.white);
                    img.preserveAspect = true;
                    Set(img.rectTransform, new Vector2(0f, 1f), new Vector2(left + (column - size) / 2f + inset, iconTop - inset), new Vector2(size - 2f * inset, size - 2f * inset));
                }
                else if (id.StartsWith(TooltipSheet.LinkBar))
                {
                    int.TryParse(id.Substring(TooltipSheet.LinkBar.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var fill);
                    var end = right;
                    for (var j = 0; j < info.linkCount; j++)
                        if (info.linkInfo[j].GetLinkID() == TooltipSheet.LinkBarValue && info.linkInfo[j].linkTextfirstCharacterIndex < info.characterCount)
                            end = info.characterInfo[info.linkInfo[j].linkTextfirstCharacterIndex].origin - ThemeTokens.GetFloat("size.tooltip.bar.gap", 12f);
                    var h = ThemeTokens.GetFloat("size.tooltip.bar", 8f);
                    var y = (line.ascender + line.descender) / 2f;
                    var w = Mathf.Max(0f, end - left);
                    var trough = Next(ThemeTokens.GetSprite("sprite.bar.skill.trough"), ThemeTokens.GetColor("color.tooltip.bar.trough", new Color(0.31f, 0.88f, 0.9f, 0.2f)));
                    Set(trough.rectTransform, new Vector2(0f, 0.5f), new Vector2(left, y), new Vector2(w, h));
                    var bar = Next(ThemeTokens.GetSprite("sprite.bar.skill.fill"), ThemeTokens.GetColor("color.tooltip.bar.fill", new Color(0.31f, 0.88f, 0.9f, 1f)));
                    Set(bar.rectTransform, new Vector2(0f, 0.5f), new Vector2(left, y), new Vector2(w * Mathf.Clamp01(fill / 1000f), h));
                }
            }
        }

        private Image Next(Sprite sprite, Color color)
        {
            if (_used == _images.Count)
            {
                var go = new GameObject("MorgottTipDecor", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(_text.transform, false);
                // Our graphic, drawn as set here: never classified by the restyle pass (run 20261108-183456 reported it unknown).
                Restyle.SkinMark.Own(go, "content", "tooltip sheet decor (TooltipDecor: rule / icon / durability bar)");
                _images.Add(go.GetComponent<Image>());
            }
            var img = _images[_used++];
            img.raycastTarget = false;
            img.sprite = sprite;
            img.type = sprite && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            img.preserveAspect = false;
            img.color = color;
            img.enabled = true;
            return img;
        }

        // Text-local position: anchors on the text's pivot, so anchoredPosition = the TMP_TextInfo coordinates.
        private void Set(RectTransform rt, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var p = _text.rectTransform.pivot;
            rt.anchorMin = rt.anchorMax = p;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
        }
    }
}
