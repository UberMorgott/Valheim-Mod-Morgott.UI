using System.Collections.Generic;
using Morgott.Core;
using Morgott.UI.Restyle;
using Morgott.UI.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Morgott.UI.MapScreen
{
    // Portal style editor (mockup D:\tmp\cx-portal\out\mockup_editor.png): modal over the large map, opened by a row's
    // pencil. Name (vanilla tag limit 10), colour (10 presets, hex, hue slider), icon (Core PortalStyle ids, 8 per row,
    // first cell = none), preview, Reset / Cancel / Save. Save = IPortalTravel.TryStyle; "saved" only after the server's
    // ack, its reject / timeout reason otherwise. Esc / B closes it (Minimap.InTextInput is true while it is open).
    public sealed partial class MinimapSkin
    {
        // White, gold, orange, red, pink, purple, blue, cyan, green, grey (mockup order).
        private static readonly int[] EdPresets = { 0xF2F2F2, 0xE8B93D, 0xE8862E, 0xE04848, 0xE8607F, 0xA45CE0, 0x4C7BE8, 0x3FD0D8, 0x4CC85A, 0x8C8C8C };
        private const int EdColumns = 8;

        private RectTransform _ed;
        private TMP_Text _edCount, _edStatus, _edPreviewName;
        private TMP_InputField _edName, _edHex;
        private Slider _edHue;
        private Image _edColorPreview, _edPreviewIcon;
        private Button _edSave;
        private readonly List<Image> _edSwatchFrames = new List<Image>(), _edIconCells = new List<Image>();
        private ZDOID _edId = ZDOID.None;
        private string _edOriginal = "";
        private int _edColor = PortalStyle.DefaultColor, _edIcon, _edToken;
        private float _edCloseAt = -1f;
        private bool _edWrite;

        // Harness.
        public bool PortalEditorShown => _ed && _ed.gameObject.activeSelf;
        public ZDOID PortalEditing => _edId;
        public string PortalEditorStatus => _edStatus ? _edStatus.text : null;

        public void PortalEditorSet(string name, int color, int icon)
        {
            if (!PortalEditorShown) return;
            _edName.text = name;
            _edColor = color;
            _edIcon = icon;
            EdRefresh();
        }

        public void PortalEditorSave() => EdSave();

        private void BuildPortalEditor()
        {
            _ed = HudArtRect("RuneSkin_PortalEditor", _large);
            Stretch(_ed);
            var dim = _ed.gameObject.AddComponent<Image>();
            dim.color = ThemeTokens.GetColor("color.map.portal.editor.dim", new Color(0f, 0f, 0f, 0.55f));
            dim.raycastTarget = true; // nothing under the editor takes clicks
            var w = ThemeTokens.GetFloat("size.map.portal.editor.w", 760f);
            var h = ThemeTokens.GetFloat("size.map.portal.editor.h", 700f);
            var panel = HudArtRect("Panel", _ed);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(w, h);
            PanelDecor.Ensure(panel, "size.panel.glass.inset", glass: true, plaque: false);

            var pad = 28f;
            var label = 120f;
            var x = pad + label;
            var font = ThemeTokens.GetFloat("size.map.legend.font", 17f);
            var title = _art.Text("Title", panel, "font.display", "color.text.header", ThemeTokens.GetFloat("size.map.legend.header", 21f) + 4f, TextAlignmentOptions.Center);
            Place(title.rectTransform, pad, 20f, w - 2f * pad, 38f);
            title.text = Words.Get("$ui_portal_edit_title");

            var y = 78f;
            EdLabel(panel, "$ui_portal_edit_name", pad, y, label, 40f, font);
            var nameBack = _art.Image("Name", panel, "sprite.field.search", "color.frame", Image.Type.Sliced, raycast: true);
            Place(nameBack.rectTransform, x, y, w - x - pad, 40f);
            _edName = Field(nameBack, PortalStyle.MaxName); // vanilla tag limit (TeleportWorld.Interact, TeleportWorld.cs:84)
            _edName.textViewport.offsetMin = new Vector2(12f, 0f);
            _edName.textViewport.offsetMax = new Vector2(-70f, 0f);
            _edName.onValueChanged.AddListener(_ => EdRefresh());
            _edCount = _art.Text("Count", nameBack.transform, "font.body", "color.state.disabled", font, TextAlignmentOptions.Right);
            Place(_edCount.rectTransform, w - x - pad - 70f, 0f, 58f, 40f);

            y += 56f;
            EdLabel(panel, "$ui_portal_edit_color", pad, y, label, 44f, font);
            for (var i = 0; i < EdPresets.Length; i++)
            {
                var rgb = EdPresets[i];
                var frame = _art.Image("Swatch" + i, panel, "sprite.slot.cell", "color.frame", Image.Type.Sliced, raycast: true);
                Place(frame.rectTransform, x + i * 52f, y, 44f, 44f);
                var fill = _art.Image("Fill", frame.transform, null, "color.frame", Image.Type.Simple);
                Stretch(fill.rectTransform);
                fill.rectTransform.offsetMin = new Vector2(6f, 6f);
                fill.rectTransform.offsetMax = new Vector2(-6f, -6f);
                fill.color = ToColor(rgb);
                frame.gameObject.AddComponent<Button>().onClick.AddListener(() => { _edColor = rgb; EdRefresh(); });
                _edSwatchFrames.Add(frame);
            }

            y += 56f;
            var hash = _art.Text("Hash", panel, "font.body", "color.text.body", font, TextAlignmentOptions.Center);
            Place(hash.rectTransform, x, y, 24f, 36f);
            hash.text = "#";
            var hexBack = _art.Image("Hex", panel, "sprite.field.search", "color.frame", Image.Type.Sliced, raycast: true);
            Place(hexBack.rectTransform, x + 28f, y, 120f, 36f);
            _edHex = Field(hexBack, 6);
            _edHex.textViewport.offsetMin = new Vector2(10f, 0f);
            _edHex.onValueChanged.AddListener(text =>
            {
                if (_edWrite || !PortalStyle.TryParseHex(text, out var rgb)) return;
                _edColor = rgb;
                EdRefresh();
            });
            _edColorPreview = _art.Image("ColorPreview", panel, null, "color.frame", Image.Type.Simple);
            Place(_edColorPreview.rectTransform, x + 160f, y, 36f, 36f);
            _edHue = EdSlider(panel, x + 212f, y + 10f, w - x - 212f - pad, 16f);
            _edHue.onValueChanged.AddListener(v =>
            {
                if (_edWrite) return;
                Color.RGBToHSV(ToColor(EdShownRgb()), out _, out var s, out var b);
                if (s < 0.05f) { s = 0.75f; b = Mathf.Max(b, 0.6f); } // grey has no hue: start from a saturated one
                _edColor = ToRgb(Color.HSVToRGB(v, s, b));
                EdRefresh();
            });

            y += 54f;
            EdLabel(panel, "$ui_portal_edit_icon", pad, y, label, 40f, font);
            var cell = ThemeTokens.GetFloat("size.map.portal.editor.cell", 64f);
            for (var id = 0; id < PortalStyle.Icons.Length; id++)
            {
                var icon = id;
                var c = _art.Image("Icon" + id, panel, "sprite.slot.cell", "color.frame", Image.Type.Sliced, raycast: true);
                Place(c.rectTransform, x + id % EdColumns * (cell + 8f), y + id / EdColumns * (cell + 8f), cell, cell);
                if (id == PortalStyle.NoIcon)
                {
                    var none = _art.Text("None", c.transform, "font.body", "color.state.disabled", font + 6f, TextAlignmentOptions.Center);
                    Stretch(none.rectTransform);
                    none.text = "—";
                }
                else
                {
                    var g = _art.Image("Glyph", c.transform, null, "color.frame", Image.Type.Simple);
                    Stretch(g.rectTransform);
                    g.rectTransform.offsetMin = new Vector2(5f, 5f);
                    g.rectTransform.offsetMax = new Vector2(-5f, -5f);
                    g.sprite = PortalIcons.Instance.Get(id);
                    g.preserveAspect = true;
                }
                c.gameObject.AddComponent<Button>().onClick.AddListener(() => { _edIcon = icon; EdRefresh(); });
                _edIconCells.Add(c);
            }

            var bottom = h - 84f;
            var line = _art.Image("Divider", panel, "sprite.divider", "color.frame", Image.Type.Sliced);
            Place(line.rectTransform, pad, bottom - 14f, w - 2f * pad, 11f);
            _edPreviewIcon = _art.Image("PreviewIcon", panel, null, "color.frame", Image.Type.Simple);
            _edPreviewIcon.preserveAspect = true;
            Place(_edPreviewIcon.rectTransform, pad, bottom + 4f, 48f, 48f);
            _edPreviewName = _art.Text("PreviewName", panel, "font.body", "color.text.body", font + 6f, TextAlignmentOptions.Left);
            Place(_edPreviewName.rectTransform, pad + 58f, bottom, 170f, 34f);
            _edStatus = _art.Text("Status", panel, "font.body", "color.state.disabled", font - 2f, TextAlignmentOptions.Left);
            _edStatus.textWrappingMode = TextWrappingModes.Normal;
            Place(_edStatus.rectTransform, pad + 58f, bottom + 32f, 190f, 40f);
            var bw = 140f;
            _edSave = EdButton(panel, "Save", "$ui_portal_edit_save", "sprite.button.selected", w - pad - bw, bottom + 6f, bw, font, EdSave);
            EdButton(panel, "Cancel", "$ui_portal_edit_cancel", "sprite.button.normal", w - pad - 2f * bw - 12f, bottom + 6f, bw, font, ClosePortalEditor);
            EdButton(panel, "Reset", "$ui_portal_edit_reset", "sprite.button.normal", w - pad - 3f * bw - 24f, bottom + 6f, bw, font, () =>
            {
                _edName.text = _edOriginal;
                _edColor = PortalStyle.DefaultColor;
                _edIcon = PortalStyle.NoIcon;
                EdRefresh();
            });
            _ed.gameObject.SetActive(false);
        }

        private void EdLabel(Transform parent, string token, float x, float y, float w, float h, float size)
        {
            var t = _art.Text("Label", parent, "font.body", "color.text.body", size + 2f, TextAlignmentOptions.Left);
            Place(t.rectTransform, x, y, w, h);
            t.text = Words.Get(token);
        }

        private Button EdButton(Transform parent, string name, string token, string sprite, float x, float y, float w, float size, UnityEngine.Events.UnityAction click)
        {
            var back = _art.Image(name, parent, sprite, "color.frame", Image.Type.Sliced, raycast: true);
            Place(back.rectTransform, x, y, w, 46f);
            var text = _art.Text("Label", back.transform, "font.body", "color.text.body", size + 1f, TextAlignmentOptions.Center);
            Stretch(text.rectTransform);
            text.text = Words.Get(token);
            var b = back.gameObject.AddComponent<Button>();
            b.targetGraphic = back;
            var hover = ThemeTokens.GetSprite("sprite.button.hover");
            if (hover)
            {
                b.transition = Selectable.Transition.SpriteSwap;
                b.spriteState = new SpriteState { highlightedSprite = hover, pressedSprite = ThemeTokens.GetSprite("sprite.button.pressed") ?? hover, disabledSprite = ThemeTokens.GetSprite("sprite.button.disabled") };
            }
            b.onClick.AddListener(click);
            return b;
        }

        // Built like the zoom slider (MapLegend.BuildZoom).
        private Slider EdSlider(Transform parent, float x, float y, float w, float h)
        {
            var root = HudArtRect("Hue", parent);
            Place(root, x, y, w, h);
            var s = root.gameObject.AddComponent<Slider>();
            var track = _art.Image("Track", root, "sprite.bar.skill.trough", "color.frame", Image.Type.Sliced, raycast: true);
            Stretch(track.rectTransform);
            var fillArea = HudArtRect("FillArea", root);
            Stretch(fillArea);
            var fill = _art.Image("Fill", fillArea, "sprite.bar.skill.fill", "color.text.accent", Image.Type.Sliced);
            Stretch(fill.rectTransform);
            var handleArea = HudArtRect("HandleArea", root);
            Stretch(handleArea);
            var handle = _art.Image("Handle", handleArea, "sprite.slider.handle.normal", "color.frame", Image.Type.Simple, raycast: true);
            handle.preserveAspect = true;
            handle.rectTransform.sizeDelta = new Vector2(h * 1.6f, h * 1.6f);
            s.fillRect = fill.rectTransform;
            s.handleRect = handle.rectTransform;
            s.targetGraphic = handle;
            s.direction = Slider.Direction.LeftToRight;
            s.minValue = 0f;
            s.maxValue = 1f;
            return s;
        }

        // Pencil on a row (and the entry row): open for that portal, unless the ward rule denies it.
        private void OpenPortalEditor(ZDOID id)
        {
            var travel = PortalPickerService.Travel();
            if (travel == null || id == ZDOID.None) return;
            if (!travel.CanEdit(id, out var denied))
            {
                if (!string.IsNullOrEmpty(denied)) Player.m_localPlayer?.Message(MessageHud.MessageType.Center, denied);
                return;
            }
            var zdo = ZDOMan.instance?.GetZDO(id);
            if (zdo == null) return;
            if (!_ed) BuildPortalEditor();
            _edId = id;
            _edToken++;
            _edCloseAt = -1f;
            _edOriginal = zdo.GetString(ZDOVars.s_tag);
            _edName.text = _edOriginal;
            _edColor = zdo.GetInt(ColorKey, PortalStyle.DefaultColor);
            _edIcon = zdo.GetInt(IconKey, PortalStyle.NoIcon);
            if (!PortalStyle.IsColor(_edColor)) _edColor = PortalStyle.DefaultColor;
            if (!PortalStyle.IsIcon(_edIcon)) _edIcon = PortalStyle.NoIcon;
            _edStatus.text = "";
            _edSave.interactable = true;
            _ed.gameObject.SetActive(true);
            _ed.SetAsLastSibling();
            EdRefresh();
            _edName.Select();
            _edName.ActivateInputField();
        }

        private void ClosePortalEditor()
        {
            _edId = ZDOID.None;
            _edToken++; // a late ack no longer touches the editor
            if (_ed && _ed.gameObject.activeSelf) _ed.gameObject.SetActive(false);
        }

        private void EdSave()
        {
            var travel = PortalPickerService.Travel();
            if (travel == null || _edId == ZDOID.None) return;
            var token = ++_edToken;
            if (!travel.TryStyle(_edId, _edName.text, _edColor, _edIcon, reason => EdDone(token, reason), out var failure))
            {
                EdStatus(failure, "color.text.missing");
                return;
            }
            _edSave.interactable = false;
            EdStatus(Words.Get("$ui_portal_edit_saving"), "color.state.disabled");
        }

        private void EdDone(int token, string reason)
        {
            if (token != _edToken || !PortalEditorShown) return;
            _edSave.interactable = true;
            if (reason != null)
            {
                EdStatus(reason, "color.text.missing");
                return;
            }
            EdStatus(Words.Get("$ui_portal_edit_saved"), "color.text.ok");
            _edCloseAt = Time.unscaledTime + 0.8f;
        }

        private void EdStatus(string text, string color)
        {
            _edStatus.text = text ?? "";
            _edStatus.color = ThemeTokens.GetColor(color, Color.white);
        }

        // Per frame while open (PortalLateUpdate).
        private void PortalEditorUpdate()
        {
            if (!PortalEditorShown) return;
            if (_edCloseAt > 0f && Time.unscaledTime >= _edCloseAt) ClosePortalEditor();
            else if (ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyButtonB")) ClosePortalEditor();
        }

        private int EdShownRgb() => _edColor != PortalStyle.DefaultColor ? _edColor : ToRgb(ThemeTokens.GetColor("color.text.body", Color.white));

        private void EdRefresh()
        {
            if (!_ed) return;
            _edCount.text = $"{_edName.text.Length}/{PortalStyle.MaxName}";
            for (var i = 0; i < _edSwatchFrames.Count; i++)
                EdSelect(_edSwatchFrames[i], EdPresets[i] == _edColor);
            for (var i = 0; i < _edIconCells.Count; i++)
                EdSelect(_edIconCells[i], i == _edIcon);
            var rgb = EdShownRgb();
            var color = ToColor(rgb);
            _edColorPreview.color = color;
            _edPreviewName.color = color;
            var name = PortalStyle.NormalizeName(_edName.text);
            _edPreviewName.text = string.IsNullOrEmpty(name) ? Words.Get("$piece_portal") : name;
            var sprite = PortalIcons.Instance.Get(_edIcon);
            _edPreviewIcon.sprite = sprite;
            _edPreviewIcon.enabled = sprite;
            _edWrite = true; // our own writes do not feed back into _edColor
            if (!_edHex.isFocused) _edHex.text = PortalStyle.ToHex(rgb);
            Color.RGBToHSV(color, out var hue, out _, out _);
            _edHue.value = hue;
            _edWrite = false;
        }

        private void EdSelect(Image frame, bool on)
        {
            var sprite = on ? "sprite.slot.cell.selected" : "sprite.slot.cell";
            if (frame.sprite != ThemeTokens.GetSprite(sprite)) _art.Restyle(frame, sprite, "color.frame", Image.Type.Sliced);
        }

        private static readonly int ColorKey = PortalStyle.ColorKey.GetStableHashCode(), IconKey = PortalStyle.IconKey.GetStableHashCode();

        internal static Color ToColor(int rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);

        internal static int ToRgb(Color c)
        {
            Color32 c32 = c;
            return c32.r << 16 | c32.g << 8 | c32.b;
        }
    }
}
