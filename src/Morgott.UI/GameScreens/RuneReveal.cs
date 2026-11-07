using BepInEx.Configuration;
using Morgott.UI.Theme;
using TMPro;
using UnityEngine;

namespace Morgott.UI.GameScreens
{
    public static class RuneRevealConfig
    {
        public static ConfigEntry<bool> Enabled { get; private set; }
        public static ConfigEntry<float> HoldSec { get; private set; }
        public static ConfigEntry<float> DecodeSec { get; private set; }

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("RuneStone", "Reveal", true,
                "Reading a rune stone: the text first shows in the red carved runes of the stone, then decodes letter by letter into the readable runic face. A click (or gamepad A) skips to the readable text.");
            HoldSec = config.Bind("RuneStone", "HoldSec", 0.6f, new ConfigDescription(
                "Seconds the text stays in runes before decoding starts.", new AcceptableValueRange<float>(0f, 5f)));
            DecodeSec = config.Bind("RuneStone", "DecodeSec", 1.2f, new ConfigDescription(
                "Seconds the decoding wave takes to cross the whole text.", new AcceptableValueRange<float>(0.1f, 10f)));
        }
    }

    // Two layers on the TextViewer rune text box: the vanilla m_text (localized, display face) and a copy in the vanilla
    // rune face (m_runeText's font, TextViewer.cs:28) holding the same letters remapped 1:1. Per letter, TMP vertex
    // colours cross-fade rune (carved red) -> readable (its own colour); alpha stays the Animator's fade.
    public sealed class RuneReveal : MonoBehaviour
    {
        public const string LayerName = "MorgottUI_RuneLayer";

        private TMP_Text _text;
        private TMP_Text _runes;
        private Color32 _red;
        private float _t = float.MaxValue;
        private float _hold, _decode;

        public TMP_Text RuneLayer => _runes;
        public bool Running => _runes && !RuneRevealMath.Done(_t, _hold, _decode);
        public float Elapsed => _t;

        public static RuneReveal Attach(TextViewer v)
        {
            if (!v.m_text || !v.m_runeText) return null;
            if (!v.m_text.TryGetComponent<RuneReveal>(out var r)) r = v.m_text.gameObject.AddComponent<RuneReveal>();
            r.Init(v.m_text, v.m_runeText.font);
            return r;
        }

        private void Init(TMP_Text text, TMP_FontAsset runeFont)
        {
            _text = text;
            if (_runes) return;
            var go = new GameObject(LayerName, typeof(RectTransform));
            go.transform.SetParent(text.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _runes = go.AddComponent<TextMeshProUGUI>();
            _runes.font = runeFont;
            _runes.raycastTarget = false;
            _runes.richText = true;
            _runes.enabled = false;
            _text.OnPreRenderText += _ => Tint(_text, false);
            _runes.OnPreRenderText += _ => Tint(_runes, true);
        }

        // TextViewer.ShowText(Style.Rune) postfix: m_text holds the localized text (TextViewer.cs:104).
        public void Play()
        {
            if (!_text || !_runes) return;
            if (!RuneRevealConfig.Enabled.Value) { Finish(); return; }
            _red = ThemeTokens.GetColor("color.rune.carved", new Color(1f, 0.165f, 0.118f));
            _hold = RuneRevealConfig.HoldSec.Value;
            _decode = RuneRevealConfig.DecodeSec.Value;
            _runes.fontSize = _text.fontSize;
            _runes.enableAutoSizing = false;
            _runes.alignment = _text.alignment;
            _runes.textWrappingMode = _text.textWrappingMode;
            _runes.lineSpacing = _text.lineSpacing;
            _runes.text = RuneRevealMath.Remap(_text.text, HasGlyph);
            _runes.enabled = true;
            _t = 0f;
        }

        public void Skip() => _t = float.MaxValue;

        private void Finish()
        {
            _t = float.MaxValue;
            if (_runes) _runes.enabled = false;
            if (_text) _text.havePropertiesChanged = true;
        }

        private bool HasGlyph(char c) =>
            _runes.font && _runes.font.HasCharacter(c, false, true) &&
            _runes.font.characterLookupTable.TryGetValue(c, out var ch) && ch.glyph != null && ch.glyph.metrics.width > 0f;

        private void LateUpdate()
        {
            if (!_runes || !_runes.enabled) return;
            _t += Time.unscaledDeltaTime;
            if (_t > 0.05f && (ZInput.GetMouseButtonDown(0) || ZInput.GetButtonDown("JoyButtonA"))) Skip(); // Hud.cs:643 skip keys
            if (RuneRevealMath.Done(_t, _hold, _decode)) { Finish(); return; }
            var a = _text.color.a; // the viewer Animator's fade
            if (!Mathf.Approximately(_runes.color.a, a)) _runes.color = new Color(1f, 1f, 1f, a);
            _text.havePropertiesChanged = true; // regenerate both meshes: Tint runs in OnPreRenderText
            _runes.havePropertiesChanged = true;
        }

        // Rewrites the generated vertex colours of every visible letter by its decode progress.
        private void Tint(TMP_Text layer, bool rune)
        {
            if (!_runes || !_runes.enabled || RuneRevealMath.Done(_t, _hold, _decode)) return;
            var info = layer.textInfo;
            var n = info.characterCount;
            for (var i = 0; i < n; i++)
            {
                var ch = info.characterInfo[i];
                if (!ch.isVisible) continue;
                var p = RuneRevealMath.Progress(i, n, _t, _hold, _decode);
                var cols = info.meshInfo[ch.materialReferenceIndex].colors32;
                for (var k = 0; k < 4; k++)
                {
                    var src = cols[ch.vertexIndex + k];
                    Color32 c;
                    if (rune) c = new Color32(_red.r, _red.g, _red.b, (byte)(src.a * (1f - p)));
                    else
                    {
                        c = Color32.Lerp(new Color32(_red.r, _red.g, _red.b, src.a), src, p);
                        c.a = (byte)(src.a * p);
                    }
                    cols[ch.vertexIndex + k] = c;
                }
            }
        }
    }
}
