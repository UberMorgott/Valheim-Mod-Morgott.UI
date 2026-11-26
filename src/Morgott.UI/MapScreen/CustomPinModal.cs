using System;
using System.Collections.Generic;
using HarmonyLib;
using Morgott.UI.Ownership;
using Morgott.UI.Restyle;
using Morgott.UI.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Morgott.UI.MapScreen
{
    // Custom colored 25-icon map pins: double-click modal (New / Edit), 0.3s deferred single-click cross-out on own
    // editable pins, custom icon + color rendering preserving shared-pin alpha, and synchronous custom-icon filtering.
    public sealed partial class MinimapSkin
    {
        private const int PinModalCols = 5;

        private RectTransform _pinModal;
        private TMP_Text _pinModalTitle, _pinModalNameHint, _pinModalPreviewName;
        private TMP_InputField _pinModalName;
        private Image _pinModalPreviewIcon;
        private Button _pinModalConfirm, _pinModalDelete;
        private TMP_Text _pinModalConfirmLabel;
        private readonly List<Image> _pinModalIconCells = new List<Image>();
        private readonly List<Image> _pinModalIconGlyphs = new List<Image>();
        private readonly List<Image> _pinModalSwatchFrames = new List<Image>();
        private Minimap.PinData _pinModalEditPin;
        private Vector3 _pinModalPos;
        private int _pinModalIconIdx = 24; // "dot"
        private string _pinModalHex = CustomPinMath.DefaultHex;
        private readonly DeferredPinToggle _deferredPinToggle = new DeferredPinToggle();
        private readonly HashSet<string> _hiddenCustomIcons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _writingHiddenCustomPins;
        private int _customFilterVersion;

        public bool CustomPinModalShown => _pinModal && _pinModal.gameObject.activeSelf;
        internal static bool HasHiddenCustomIcons => Instance && Instance._hiddenCustomIcons.Count > 0;

        internal bool IsCustomIconVisible(string iconId) =>
            string.IsNullOrEmpty(iconId) || !_hiddenCustomIcons.Contains(iconId);

        internal bool IsPinVisibleByCustomFilter(Minimap.PinData pin) =>
            pin == null || CustomPinMath.IsCustomPinVisible(pin.m_name, _hiddenCustomIcons);

        internal void SyncHiddenCustomIconsFromConfig()
        {
            if (_writingHiddenCustomPins) return;
            var entry = MapConfig.HiddenCustomPins;
            if (entry != null && HookedCustomFilter != entry)
            {
                HookedCustomFilter = entry;
                entry.SettingChanged += (_, __) => Instance?.SyncHiddenCustomIconsFromConfig();
            }
            CustomPinMath.ParseHiddenIcons(entry?.Value, _hiddenCustomIcons);
            _customFilterVersion++;
            if (_map)
            {
                ApplyCustomPinVisuals(_map);
                _map.m_pinUpdateRequired = true;
            }
        }

        private static BepInEx.Configuration.ConfigEntry<string> HookedCustomFilter;

        internal void ToggleCustomIconFilter(string iconId)
        {
            var idx = CustomPinMath.IconIndex(iconId);
            if (idx < 0) return;
            var canonical = CustomPinMath.IconIds[idx];
            if (!_hiddenCustomIcons.Remove(canonical)) _hiddenCustomIcons.Add(canonical);
            _customFilterVersion++;
            SaveHiddenCustomIcons();
            if (_map)
            {
                ApplyCustomPinVisuals(_map);
                _map.m_pinUpdateRequired = true;
            }
        }

        private void SaveHiddenCustomIcons()
        {
            var entry = MapConfig.HiddenCustomPins;
            if (entry == null) return;
            var csv = CustomPinMath.FormatHiddenIcons(_hiddenCustomIcons);
            if (entry.Value == csv) return;
            _writingHiddenCustomPins = true;
            try { entry.Value = csv; }
            finally { _writingHiddenCustomPins = false; }
        }

        // Runs after Minimap.UpdatePins (and synchronously when custom icon filter toggles):
        // strips [mp1:...] from rendered pin labels, applies custom icon sprite and color (preserving shared alpha),
        // and enforces custom-icon filter state synchronously across icon, label, and GetClosestPin hit-testing.
        internal void ApplyCustomPinVisuals(Minimap map)
        {
            if (!map || map.m_pins == null) return;
            foreach (var pin in map.m_pins)
            {
                if (pin == null || !CustomPinMath.TryDecode(pin.m_name, out var d)) continue;
                var visible = IsCustomIconVisible(d.IconId);
                if (pin.m_uiElement)
                {
                    var go = pin.m_uiElement.gameObject;
                    if (!visible)
                    {
                        if (go.activeSelf) go.SetActive(false);
                        if (pin.m_NamePinData?.PinNameGameObject && pin.m_NamePinData.PinNameGameObject.activeSelf)
                            pin.m_NamePinData.PinNameGameObject.SetActive(false);
                        continue;
                    }
                    if (!go.activeSelf) go.SetActive(true);
                    if (d.IconIndex >= 0 && pin.m_iconElement)
                    {
                        var sprite = CustomPinIcons.Get(d.IconIndex);
                        if (sprite && pin.m_iconElement.sprite != sprite) pin.m_iconElement.sprite = sprite;
                    }
                    if (d.HasColor && pin.m_iconElement)
                        pin.m_iconElement.color = CustomPinMath.ApplyColorPreservingAlpha(pin.m_iconElement.color, d.R, d.G, d.B);
                }

                if (pin.m_NamePinData?.PinNameGameObject)
                {
                    var nameGo = pin.m_NamePinData.PinNameGameObject;
                    var text = pin.m_NamePinData.PinNameText;
                    if (!visible || string.IsNullOrEmpty(d.VisibleName))
                    {
                        if (nameGo.activeSelf) nameGo.SetActive(false);
                        if (text && text.text.Length > 0) text.text = "";
                    }
                    else if (text)
                    {
                        var stripped = CustomPinMath.StripTag(text.text);
                        if (text.text != stripped) text.text = stripped;
                    }
                }
            }
        }

        // ---------------------------------------------------------------- deferred single-click & double-click handlers

        internal bool HandleMapLeftClick(Minimap map)
        {
            var pos = map.ScreenToWorldPoint(ZInput.pointerPosition);
            var pin = map.GetClosestPin(pos, map.PinInteractRadius);
            if (pin != null && pin.m_ownerID != 0L)
            {
                if (!ZInput.IsTouchActive() || !ZInput.VirtualKeyboardOpen) map.HidePinTextInput();
                if (_deferredPinToggle.OnPinClick(pin, DeferredPinAction.ClaimForeign, Time.time, out var prev) && prev.Pin != null)
                    ApplyDeferredPinItem(map, prev);
                return false;
            }
            if (pin != null && CustomPinMath.IsEditablePin(pin.m_save, pin.m_ownerID, pin.m_type))
            {
                if (!ZInput.IsTouchActive() || !ZInput.VirtualKeyboardOpen) map.HidePinTextInput();
                if (_deferredPinToggle.OnPinClick(pin, DeferredPinAction.ToggleChecked, Time.time, out var prev) && prev.Pin != null)
                    ApplyDeferredPinItem(map, prev);
                return false;
            }
            if (_deferredPinToggle.HasPending)
            {
                var due = _deferredPinToggle.PollDueItem(float.PositiveInfinity);
                if (due.Pin != null) ApplyDeferredPinItem(map, due);
            }
            return true;
        }

        internal bool HandleMapDblClick(Minimap map)
        {
            _deferredPinToggle.CancelPendingOnly();
            var pos = map.ScreenToWorldPoint(ZInput.pointerPosition);
            var pin = map.GetClosestPin(pos, map.PinInteractRadius);
            if (pin != null)
            {
                // Own editable pins open the editor; foreign shared (including pre-click foreign) or system pins never do.
                if (!_deferredPinToggle.WasRecentlyForeign(pin, Time.time)
                    && CustomPinMath.IsEditablePin(pin.m_save, pin.m_ownerID, pin.m_type))
                    OpenEditCustomPinModal(pin);
                return false;
            }
            if (map.m_selectedType == Minimap.PinType.Ping) return true;
            OpenNewCustomPinModal(pos);
            return false;
        }

        internal void CancelDeferredPinClick() => _deferredPinToggle.Cancel();

        private void PollDeferredPinClick()
        {
            var due = _deferredPinToggle.PollDueItem(Time.time);
            if (due.Pin != null) ApplyDeferredPinItem(_map, due);
        }

        private static void ApplyDeferredPinItem(Minimap map, DeferredPinItem item)
        {
            if (!map || !(item.Pin is Minimap.PinData pin) || !map.m_pins.Contains(pin)) return;
            if (item.Action == DeferredPinAction.ClaimForeign)
            {
                if (pin.m_ownerID != 0L)
                {
                    pin.m_ownerID = 0L;
                    map.m_pinUpdateRequired = true;
                }
            }
            else if (item.Action == DeferredPinAction.ToggleChecked)
            {
                pin.m_checked = !pin.m_checked;
                map.m_pinUpdateRequired = true;
            }
        }

        // ---------------------------------------------------------------- modal UI

        private void BuildCustomPinModal()
        {
            _pinModal = HudArtRect("RuneSkin_CustomPinModal", _large);
            Stretch(_pinModal);
            var dim = _pinModal.gameObject.AddComponent<Image>();
            dim.color = ThemeTokens.GetColor("color.map.portal.editor.dim", new Color(0f, 0f, 0f, 0.55f));
            dim.raycastTarget = true;

            const float w = 540f, h = 580f, pad = 26f;
            var panel = HudArtRect("Panel", _pinModal);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(w, h);
            PanelDecor.Ensure(panel, "size.panel.glass.inset", glass: true, plaque: false);

            var font = ThemeTokens.GetFloat("size.map.legend.font", 17f);
            var contentW = w - 2f * pad;
            _pinModalTitle = _art.Text("Title", panel, "font.display", "color.text.header", ThemeTokens.GetFloat("size.map.legend.header", 21f) + 2f, TextAlignmentOptions.Center);
            Place(_pinModalTitle.rectTransform, pad, 18f, contentW, 34f);

            var y = 62f;
            var nameBack = _art.Image("Name", panel, "sprite.field.search", "color.frame", Image.Type.Sliced, raycast: true);
            Place(nameBack.rectTransform, pad, y, contentW, 38f);
            _pinModalName = Field(nameBack, CustomPinMath.MaxVisibleNameLength);
            _pinModalName.textViewport.offsetMin = new Vector2(12f, 0f);
            _pinModalName.textViewport.offsetMax = new Vector2(-12f, 0f);
            _pinModalName.onValueChanged.AddListener(_ => RefreshCustomPinModal());
            _pinModalNameHint = _art.Text("NameHint", nameBack.transform, "font.body", "color.state.disabled", font, TextAlignmentOptions.Left);
            Place(_pinModalNameHint.rectTransform, 12f, 0f, contentW - 24f, 38f);

            // 10 harmonious Valheim color swatches in one clean row.
            y += 48f;
            const float swGap = 8f;
            var swSize = Mathf.Floor((contentW - (CustomPinMath.SwatchHex.Length - 1) * swGap) / CustomPinMath.SwatchHex.Length);
            var swStartX = pad + (contentW - (CustomPinMath.SwatchHex.Length * swSize + (CustomPinMath.SwatchHex.Length - 1) * swGap)) * 0.5f;
            for (var i = 0; i < CustomPinMath.SwatchHex.Length; i++)
            {
                var hex = CustomPinMath.SwatchHex[i];
                var frame = _art.Image("Swatch" + i, panel, "sprite.slot.cell", "color.frame", Image.Type.Sliced, raycast: true);
                Place(frame.rectTransform, swStartX + i * (swSize + swGap), y, swSize, swSize);
                var fill = _art.Image("Fill", frame.transform, null, "color.frame", Image.Type.Simple);
                Stretch(fill.rectTransform);
                fill.rectTransform.offsetMin = new Vector2(5f, 5f);
                fill.rectTransform.offsetMax = new Vector2(-5f, -5f);
                fill.color = CustomPinMath.SwatchColor(hex);
                frame.gameObject.AddComponent<Button>().onClick.AddListener(() => { _pinModalHex = hex; RefreshCustomPinModal(); });
                _pinModalSwatchFrames.Add(frame);
            }

            // 5x5 grid of 25 custom icons.
            y += swSize + 14f;
            const float cell = 54f, gap = 8f;
            var gridW = PinModalCols * cell + (PinModalCols - 1) * gap;
            var gridX = pad + (contentW - gridW) * 0.5f;
            for (var i = 0; i < CustomPinMath.IconIds.Length; i++)
            {
                var idx = i;
                var col = i % PinModalCols;
                var row = i / PinModalCols;
                var c = _art.Image("Icon" + i, panel, "sprite.slot.cell", "color.frame", Image.Type.Sliced, raycast: true);
                Place(c.rectTransform, gridX + col * (cell + gap), y + row * (cell + gap), cell, cell);
                var glyph = _art.Image("Glyph", c.transform, null, "color.frame", Image.Type.Simple);
                Stretch(glyph.rectTransform);
                glyph.rectTransform.offsetMin = new Vector2(8f, 8f);
                glyph.rectTransform.offsetMax = new Vector2(-8f, -8f);
                glyph.sprite = CustomPinIcons.Get(i);
                glyph.preserveAspect = true;
                c.gameObject.AddComponent<Button>().onClick.AddListener(() => { _pinModalIconIdx = idx; RefreshCustomPinModal(); });
                if (_ppTooltip)
                {
                    var tip = c.gameObject.AddComponent<UITooltip>();
                    tip.m_tooltipPrefab = _ppTooltip;
                    tip.m_topic = "";
                    tip.m_text = Words.Get(CustomPinMath.IconNameToken(CustomPinMath.IconIds[i]));
                }
                _pinModalIconCells.Add(c);
                _pinModalIconGlyphs.Add(glyph);
            }

            // Preview + divider + buttons.
            var bottom = h - 72f;
            var line = _art.Image("Divider", panel, "sprite.divider", "color.frame", Image.Type.Sliced);
            Place(line.rectTransform, pad, bottom - 14f, contentW, 11f);
            _pinModalPreviewIcon = _art.Image("PreviewIcon", panel, null, "color.frame", Image.Type.Simple);
            _pinModalPreviewIcon.preserveAspect = true;
            Place(_pinModalPreviewIcon.rectTransform, pad, bottom + 6f, 38f, 38f);
            _pinModalPreviewName = _art.Text("PreviewName", panel, "font.body", "color.text.body", font + 2f, TextAlignmentOptions.Left);
            _pinModalPreviewName.overflowMode = TextOverflowModes.Ellipsis;
            Place(_pinModalPreviewName.rectTransform, pad + 46f, bottom + 6f, 120f, 38f);

            const float bw = 112f, bgap = 8f;
            _pinModalConfirm = EdButton(panel, "Confirm", "$ui_mpin_place", "sprite.button.selected", w - pad - bw, bottom + 2f, bw, font - 1f, ConfirmCustomPinModal);
            _pinModalConfirmLabel = _pinModalConfirm.GetComponentInChildren<TMP_Text>();
            EdButton(panel, "Cancel", "$ui_portal_edit_cancel", "sprite.button.normal", w - pad - 2f * bw - bgap, bottom + 2f, bw, font - 1f, CloseCustomPinModal);
            _pinModalDelete = EdButton(panel, "Delete", "$ui_mpin_delete", "sprite.button.danger.normal", w - pad - 3f * bw - 2f * bgap, bottom + 2f, bw, font - 1f, DeleteCustomPinModal);
            _pinModal.gameObject.SetActive(false);
        }

        private void OpenNewCustomPinModal(Vector3 worldPos)
        {
            if (!_pinModal) BuildCustomPinModal();
            _pinModalEditPin = null;
            _pinModalPos = worldPos;
            _pinModalTitle.text = Words.Get("$ui_mpin_title_new");
            _pinModalConfirmLabel.text = Words.Get("$ui_mpin_place");
            _pinModalDelete.gameObject.SetActive(false);
            _pinModalName.text = "";
            ShowCustomPinModal();
        }

        private void OpenEditCustomPinModal(Minimap.PinData pin)
        {
            if (pin == null) return;
            if (!_pinModal) BuildCustomPinModal();
            _pinModalEditPin = pin;
            _pinModalPos = pin.m_pos;
            _pinModalTitle.text = Words.Get("$ui_mpin_title_edit");
            _pinModalConfirmLabel.text = Words.Get("$ui_portal_edit_save");
            _pinModalDelete.gameObject.SetActive(true);
            if (CustomPinMath.TryDecode(pin.m_name, out var d))
            {
                _pinModalName.text = d.VisibleName;
                if (d.IconIndex >= 0) _pinModalIconIdx = d.IconIndex;
                _pinModalHex = d.HasColor ? d.Hex : CustomPinMath.DefaultHex;
            }
            else
            {
                _pinModalName.text = pin.m_name ?? "";
                _pinModalIconIdx = MapVanillaPinTypeToDefaultIcon(pin.m_type);
                _pinModalHex = CustomPinMath.DefaultHex;
            }
            ShowCustomPinModal();
        }

        private static int MapVanillaPinTypeToDefaultIcon(Minimap.PinType type)
        {
            switch (type)
            {
                case Minimap.PinType.Icon0: return CustomPinMath.IconIndex("camp");
                case Minimap.PinType.Icon1: return CustomPinMath.IconIndex("house");
                case Minimap.PinType.Icon2: return CustomPinMath.IconIndex("fort");
                case Minimap.PinType.Icon4: return CustomPinMath.IconIndex("cave");
                default: return CustomPinMath.IconIndex(CustomPinMath.DefaultIconId);
            }
        }

        private void ShowCustomPinModal()
        {
            _pinModalNameHint.text = Words.Get("$ui_mpin_name_hint");
            for (var i = 0; i < _pinModalIconCells.Count && i < CustomPinMath.IconIds.Length; i++)
                if (_pinModalIconCells[i].TryGetComponent<UITooltip>(out var tip))
                    tip.m_text = Words.Get(CustomPinMath.IconNameToken(CustomPinMath.IconIds[i]));
            _pinModal.gameObject.SetActive(true);
            _pinModal.SetAsLastSibling();
            RefreshCustomPinModal();
            _pinModalName.Select();
            _pinModalName.ActivateInputField();
        }

        internal void CloseCustomPinModal()
        {
            _pinModalEditPin = null;
            if (_pinModal && _pinModal.gameObject.activeSelf) _pinModal.gameObject.SetActive(false);
        }

        private void ConfirmCustomPinModal()
        {
            if (!CustomPinModalShown || !_map) return;
            var iconId = _pinModalIconIdx >= 0 && _pinModalIconIdx < CustomPinMath.IconIds.Length
                ? CustomPinMath.IconIds[_pinModalIconIdx]
                : CustomPinMath.DefaultIconId;
            var encoded = CustomPinMath.Encode(_pinModalName.text, iconId, _pinModalHex);

            var oldPin = _pinModalEditPin;
            var isEditing = oldPin != null;
            var stillPresent = isEditing && _map.m_pins.Contains(oldPin);
            var stillEditable = stillPresent && CustomPinMath.IsEditablePin(oldPin.m_save, oldPin.m_ownerID, oldPin.m_type);
            var type = isEditing ? oldPin.m_type : Minimap.PinType.Icon3;

            var action = CustomPinMath.PlanSave(isEditing, stillPresent, stillEditable,
                isEditing ? oldPin.m_type : type, isEditing ? oldPin.m_name : null, type, encoded);
            if (action == CustomPinMath.PinSaveAction.Cancel || action == CustomPinMath.PinSaveAction.NoOp)
            {
                CloseCustomPinModal();
                return;
            }

            var pos = isEditing ? oldPin.m_pos : _pinModalPos;
            var isChecked = isEditing && oldPin.m_checked;
            var author = isEditing ? oldPin.m_author : default;

            // Refuse duplicate V+ sync key against any other saved pin (including filter-hidden pins) so a local duplicate
            // cannot later delete the shared server V+ entry when removed.
            if (HasVPlusCollision(oldPin, type, pos, encoded))
            {
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "$msg_pin_exist");
                return;
            }

            if (_hiddenCustomIcons.Remove(iconId))
            {
                _customFilterVersion++;
                SaveHiddenCustomIcons();
            }

            // Ensure underlying vanilla PinType is visible so the newly saved/placed custom pin appears immediately.
            if (_map.m_visibleIconTypes != null && (int)type >= 0 && (int)type < _map.m_visibleIconTypes.Length && !_map.m_visibleIconTypes[(int)type])
                _map.ToggleIconFilter(type);

            // ValheimPlus syncPins & Cartography Table compatibility: AddPin FIRST (preserving author/checked), then RemovePin(oldPin).
            var added = _map.AddPin(pos, type, encoded, save: true, isChecked: isChecked, ownerID: 0L, author: author);
            if (action == CustomPinMath.PinSaveAction.Replace)
            {
                if (added != null)
                {
                    added.m_author = oldPin.m_author;
                    added.m_checked = oldPin.m_checked;
                }
                if (_map.m_pins.Contains(oldPin)) _map.RemovePin(oldPin);
            }

            _map.m_pinUpdateRequired = true;
            CloseCustomPinModal();
        }

        private bool HasVPlusCollision(Minimap.PinData editingPin, Minimap.PinType targetType, Vector3 targetPos, string targetRawName)
        {
            if (!_map || _map.m_pins == null) return false;
            var targetKey = CustomPinMath.VPlusSyncKey(targetType, targetPos, targetRawName);
            foreach (var p in _map.m_pins)
            {
                if (p == null || !p.m_save || ReferenceEquals(p, editingPin)) continue;
                if (string.Equals(CustomPinMath.VPlusSyncKey(p.m_type, p.m_pos, p.m_name), targetKey, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private void DeleteCustomPinModal()
        {
            if (!CustomPinModalShown || !_map) return;
            if (_pinModalEditPin != null && _map.m_pins.Contains(_pinModalEditPin))
                _map.RemovePin(_pinModalEditPin);
            CloseCustomPinModal();
        }

        private void CustomPinModalUpdate()
        {
            if (!CustomPinModalShown) return;
            var showHint = _pinModalName.text.Length == 0 && !_pinModalName.isFocused;
            if (_pinModalNameHint.gameObject.activeSelf != showHint) _pinModalNameHint.gameObject.SetActive(showHint);
            if (ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyButtonB")) CloseCustomPinModal();
            else if (ZInput.GetKeyDown(KeyCode.Return) || ZInput.GetKeyDown(KeyCode.KeypadEnter)) ConfirmCustomPinModal();
        }

        private void RefreshCustomPinModal()
        {
            if (!_pinModal) return;
            var color = CustomPinMath.SwatchColor(_pinModalHex);
            for (var i = 0; i < _pinModalSwatchFrames.Count; i++)
                EdSelect(_pinModalSwatchFrames[i], string.Equals(CustomPinMath.SwatchHex[i], _pinModalHex, StringComparison.OrdinalIgnoreCase));
            for (var i = 0; i < _pinModalIconCells.Count; i++)
            {
                EdSelect(_pinModalIconCells[i], i == _pinModalIconIdx);
                _pinModalIconGlyphs[i].color = color;
            }
            var sprite = CustomPinIcons.Get(_pinModalIconIdx);
            _pinModalPreviewIcon.sprite = sprite;
            _pinModalPreviewIcon.color = color;
            _pinModalPreviewIcon.enabled = sprite;
            var shown = CustomPinMath.NormalizeVisibleName(_pinModalName.text);
            var iconName = Words.Get(CustomPinMath.IconNameToken(_pinModalIconIdx >= 0 && _pinModalIconIdx < CustomPinMath.IconIds.Length ? CustomPinMath.IconIds[_pinModalIconIdx] : CustomPinMath.DefaultIconId));
            _pinModalPreviewName.text = shown.Length > 0 ? shown : iconName;
            _pinModalPreviewName.color = ThemeTokens.GetColor(shown.Length > 0 ? "color.text.body" : "color.state.disabled", Color.white);
        }
    }

    // Minimap input + hit-test hooks for custom pins and modal/sidebar text input (always active when Minimap is owned).
    [HarmonyPatch]
    public static class CustomPinInput
    {
        private readonly struct ModalPanGuard
        {
            public readonly bool Blocked;
            public readonly Vector3 SavedOffset;
            public readonly float SavedZoom, SavedLastZoom;

            public ModalPanGuard(bool blocked, Vector3 savedOffset, float savedZoom, float savedLastZoom)
            {
                Blocked = blocked;
                SavedOffset = savedOffset;
                SavedZoom = savedZoom;
                SavedLastZoom = savedLastZoom;
            }
        }

        private static bool Prepare() => OwnershipRegistry.IsOwned(OwnershipRegistry.Minimap);

        private static bool AnyModalOpen => MinimapSkin.Instance && (MinimapSkin.Instance.CustomPinModalShown || MinimapSkin.Instance.PortalEditorShown);

        // Blocks gamepad JoyTabRight delete, JoyTabLeft toggle, JoyButtonX ping, D-pad, left-stick pan, AND touch pinch-zoom while a modal is open.
        [HarmonyPatch(typeof(Minimap), nameof(Minimap.UpdateMap)), HarmonyPrefix]
        private static void UpdateMap_Prefix(Minimap __instance, ref bool takeInput, out ModalPanGuard __state)
        {
            if (AnyModalOpen && __instance.m_mode == Minimap.MapMode.Large)
            {
                takeInput = false;
                __instance.m_dragView = false;
                __instance.m_leftDownTime = 0f;
                __state = new ModalPanGuard(true, __instance.m_mapOffset, __instance.m_largeZoom, __instance.m_lastLargeZoom);
            }
            else
            {
                __state = default;
            }
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.UpdateMap)), HarmonyPostfix]
        private static void UpdateMap_Postfix(Minimap __instance, Player player, ModalPanGuard __state)
        {
            if (!__state.Blocked) return;
            __instance.m_mapOffset = __state.SavedOffset;
            __instance.m_largeZoom = __state.SavedZoom;
            __instance.m_lastLargeZoom = __state.SavedLastZoom;
            if (player) __instance.CenterMap(player.transform.position + __instance.m_mapOffset);
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnMapLeftDown)), HarmonyPrefix]
        private static bool BlockLeftDown() => !AnyModalOpen;

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnMapLeftUp)), HarmonyPrefix]
        private static bool BlockLeftUp() => !AnyModalOpen;

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnMapLeftClick)), HarmonyPrefix]
        private static bool OnLeftClick(Minimap __instance)
        {
            if (PortalPickerService.Active) return true; // handled by PortalPickerInput
            if (AnyModalOpen) return false;
            return !MinimapSkin.Instance || MinimapSkin.Instance.HandleMapLeftClick(__instance);
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnMapDblClick)), HarmonyPrefix]
        private static bool OnDblClick(Minimap __instance)
        {
            var skin = MinimapSkin.Instance;
            if (PortalPickerService.Active) return true; // handled by PortalPickerInput
            if (!skin) return true;
            if (AnyModalOpen || !MapFilterMath.AllowAutoPinAfterOpen(Time.unscaledTime))
            {
                skin.CancelDeferredPinClick();
                return false;
            }
            return skin.HandleMapDblClick(__instance);
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.OnMapMiddleClick)), HarmonyPrefix]
        private static bool OnMiddleClick()
        {
            MinimapSkin.Instance?.CancelDeferredPinClick();
            return !AnyModalOpen;
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.RemovePinUnderPointer)), HarmonyPrefix]
        private static bool OnRemoveUnderPointer()
        {
            MinimapSkin.Instance?.CancelDeferredPinClick();
            return !AnyModalOpen;
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.RemovePin), typeof(Vector3), typeof(float)), HarmonyPrefix]
        private static bool BlockRemovePinAt(ref bool __result)
        {
            if (!AnyModalOpen) return true;
            __result = false;
            return false;
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.ShowPinNameInput)), HarmonyPrefix]
        private static bool BlockVanillaPinInput() => !AnyModalOpen;

        // Synchronous hit-test filtering: pins hidden by custom-icon filter are never picked by GetClosestPin when mustBeVisible is true.
        [HarmonyPatch(typeof(Minimap), nameof(Minimap.GetClosestPin)), HarmonyPrefix]
        private static bool GetClosestPin_Prefix(Minimap __instance, Vector3 pos, float radius, bool mustBeVisible, ref Minimap.PinData __result)
        {
            if (!mustBeVisible || !MinimapSkin.HasHiddenCustomIcons) return true;
            var skin = MinimapSkin.Instance;
            Minimap.PinData best = null;
            var bestDist = 999999f;
            foreach (var pin in __instance.m_pins)
            {
                if (!pin.m_save || !pin.m_uiElement || !pin.m_uiElement.gameObject.activeInHierarchy) continue;
                if (!skin.IsPinVisibleByCustomFilter(pin)) continue;
                var d = Utils.DistanceXZ(pos, pin.m_pos);
                if (d < radius && (d < bestDist || best == null))
                {
                    best = pin;
                    bestDist = d;
                }
            }
            __result = best;
            return false;
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.InTextInput)), HarmonyPostfix]
        private static void Typing(ref bool __result) => __result |= MinimapSkin.PortalTyping;
    }
}
