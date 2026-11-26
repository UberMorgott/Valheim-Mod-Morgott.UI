using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using Morgott.UI.Diagnostics;
using Morgott.UI.Ownership;
using Morgott.UI.PlayerHud;
using Morgott.UI.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Morgott.UI.WorldHud
{
    // M6 in-game HUD families (art\README.md "M6 sprites", mockups m6-build-a / m6-ship-b), restyled in place on the
    // live vanilla Hud objects (Hud.cs fields; hierarchy from the hudroot dump):
    // - Crosshair (CrosshairSkin): hudroot/crosshair: crosshair glyph, hover text, piece health bar, stealth eye + bar;
    // - ActionHud (ActionHudSkin): hudroot/action_progress, staggerpanel, EventBar, MountHud/MountPanel;
    // - ShipHud (ShipHudSkin): hudroot/ShipHud: own speed ladder + rudder socket, vanilla icons hidden;
    // - KeyHints (KeyHintsSkin): hudroot/KeyHints: key plates + labels, gamepad glyph art kept;
    // - BuildHud (BuildHudSkin): BuildUi (hudroot/BuildUIV2) + Hud.m_buildHud (selected-piece strip, author window)
    //   through the role pass (ScreenRegistry), as the M5 forms.
    public static class PlayHud
    {
        internal static readonly List<Func<ScreenReport>> Reports = new List<Func<ScreenReport>>();

        public static void Record()
        {
            foreach (var r in Reports)
            {
                try
                {
                    var report = r();
                    if (report != null) SkinDiagnostics.Record(report);
                }
                catch (Exception e) { Plugin.Log.LogWarning($"[Morgott.UI] play HUD diagnostics failed: {e.Message}"); }
            }
        }

        internal static bool Owned(string family) => OwnershipRegistry.IsOwned(family);

        internal static void SyncPpu(HudArt art, Component c)
        {
            var canvas = c ? c.GetComponentInParent<Canvas>(true) : null;
            if (canvas) art.SyncPpu(canvas.rootCanvas.referencePixelsPerUnit);
        }

        // Sprite size in reference px (sprite px x 100 / PPU).
        internal static Vector2 RefSize(string token, Vector2 fallback)
        {
            var s = ThemeTokens.GetSprite(token);
            return s ? s.rect.size * (100f / Mathf.Max(1f, s.pixelsPerUnit)) : fallback;
        }

        // Ref px per side of a sliced sprite's border (left, bottom, right, top).
        internal static Vector4 RefBorder(string token)
        {
            var s = ThemeTokens.GetSprite(token);
            return s ? s.border * (100f / Mathf.Max(1f, s.pixelsPerUnit)) : Vector4.zero;
        }

        // A GuiBar fill restyled: sprite + colour, and the colour GuiBar restores on ResetColor (GuiBar.m_originalColor,
        // cached in Awake).
        internal static void Fill(HudArt art, GuiBar bar, string spriteToken, string colorToken, string reason)
        {
            if (!bar || !bar.m_bar || !bar.m_bar.TryGetComponent<Image>(out var img)) return;
            if (!art.Reskin(img, spriteToken, colorToken, reason)) return;
            img.type = Image.Type.Sliced;
            if (colorToken != null) bar.m_originalColor = img.color;
        }

        // Crisp fill-end rim (bar_edge, M6 edge pass) as a child of a GuiBar fill, anchored on its right edge: it follows
        // GuiBar's width changes without per-frame code. Theme mode.hud.bar.edge 2 only.
        internal static void EdgeRim(HudArt art, GuiBar bar, string colorToken)
        {
            if (!bar || !bar.m_bar || ThemeTokens.GetFloat("mode.hud.bar.edge", 2f) < 2f || !ThemeTokens.GetSprite("sprite.hud.bar.edge")) return;
            var fill = bar.m_bar;
            if (fill.Find("RuneSkin_Edge")) return;
            var img = art.Image("RuneSkin_Edge", fill, "sprite.hud.bar.edge", colorToken, Image.Type.Simple);
            var rt = img.rectTransform;
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            var s = img.sprite;
            var h = fill.rect.height > 0f ? fill.rect.height : 20f;
            rt.sizeDelta = new Vector2(PlayerHud.HudMath.EdgeWidth(s ? s.rect.width : 0f, s ? s.rect.height : 0f, h), 0f);
        }

        // An Image named `name` directly under `parent`.
        internal static Image Child(Transform parent, string name) =>
            parent && parent.Find(name) is Transform t && t.TryGetComponent<Image>(out var img) ? img : null;

        internal static void Hide(HudArt art, Image img, string reason)
        {
            if (!img) return;
            img.enabled = false;
            art.Suppressed(img.transform, reason);
        }

        internal static void Text(HudArt art, TMP_Text t, string font, string look, string color, string role, string reason, float size = 0f)
        {
            if (!t) return;
            art.Note(t, role, TextLook.ApplyNameplate(t, font, look, 0f), reason); // outline + soft shadow (user: readable on white)
            if (color != null) t.color = ThemeTokens.GetColor(color, t.color);
            // Every themed HUD text reads at >= size.text.hud.min (px at 1080p, the HUD canvas scales with the screen).
            if (size > 0f)
            {
                t.enableAutoSizing = false;
                t.fontSize = MessageSkin.HudReadable(size);
            }
            else if (t.enableAutoSizing) t.fontSizeMax = MessageSkin.HudReadable(t.fontSizeMax);
            else t.fontSize = MessageSkin.HudReadable(t.fontSize);
        }

        // A frame image (sibling drawn after `after`) around `target` by the sprite's border insets.
        internal static Image FrameAround(HudArt art, string name, RectTransform target, Transform after, string token, float scale = 1f)
        {
            if (!target || !target.parent) return null;
            var frame = art.Image(name, target.parent, token, "color.frame", Image.Type.Sliced);
            frame.fillCenter = false; // the window shows the bar under it
            var rt = frame.rectTransform;
            rt.SetSiblingIndex((after ? after : target).GetSiblingIndex() + 1);
            var b = RefBorder(token) * scale;
            rt.anchorMin = target.anchorMin;
            rt.anchorMax = target.anchorMax;
            rt.pivot = target.pivot;
            rt.sizeDelta = target.sizeDelta + new Vector2(b.x + b.z, b.y + b.w);
            rt.anchoredPosition = new Vector2(
                PlayHudMath.GrowPosition(target.anchoredPosition.x, target.pivot.x, b.x, b.z),
                PlayHudMath.GrowPosition(target.anchoredPosition.y, target.pivot.y, b.y, b.w));
            art.Track(frame, 1f / Mathf.Max(0.1f, scale));
            return frame;
        }
    }

    // ---- Crosshair -------------------------------------------------------------------------------------------------

    // Crosshair: glyph_crosshair (tint text_body, gold over something interactable: vanilla writes Color.yellow /
    // half white every frame, Hud.UpdateCrosshair :839-843, re-tinted in its postfix); bow crosshair stays vanilla;
    // hover text in the outlined body sans, the "[<color=yellow>KEY</color>]" tags in text_active; piece health =
    // bar_skill trough / fill (text_ok); stealth = glyph_stealth_eye (hidden text_body, targeted text_active, alert
    // ember) over a slim bar_skill fill in the rune glow (Hud.UpdateStealth :947-980 toggles the objects).
    public sealed class CrosshairSkin : MonoBehaviour
    {
        public static CrosshairSkin Instance { get; private set; }
        private readonly HudArt _art = new HudArt(OwnershipRegistry.Crosshair);
        private Hud _hud;
        private Color _gold, _rest;
        private string _goldTag;

        public static void Attach(Hud hud)
        {
            if (!hud || !hud.m_crosshair || hud.GetComponent<CrosshairSkin>()) return;
            hud.gameObject.AddComponent<CrosshairSkin>().Init(hud);
        }

        private void Init(Hud hud)
        {
            Instance = this;
            _hud = hud;
            PlayHud.SyncPpu(_art, hud.m_crosshair);
            _gold = ThemeTokens.GetColor("color.text.active", new Color(0.91f, 0.71f, 0.29f));
            _rest = ThemeTokens.GetColor("color.text.body", Color.white);
            _rest.a = ThemeTokens.GetFloat("share.crosshair.alpha", 0.85f);
            _goldTag = "<color=#" + ColorUtility.ToHtmlStringRGB(_gold) + ">";
            var root = hud.m_crosshair.transform.parent;
            if (root && root.TryGetComponent<Image>(out var anchor)) _art.Content(anchor, "crosshair anchor (vanilla 5 px frame)");
            if (_art.Reskin(hud.m_crosshair, "sprite.glyph.crosshair", null, "crosshair glyph"))
            {
                hud.m_crosshair.type = Image.Type.Simple;
                hud.m_crosshair.preserveAspect = true;
                var s = ThemeTokens.GetFloat("size.crosshair", 22f);
                hud.m_crosshair.rectTransform.sizeDelta = new Vector2(s, s);
            }
            _art.Content(hud.m_crosshairBow, "bow draw crosshair (vanilla, art notes: stays)");
            PlayHud.Text(_art, hud.m_hoverName, "font.body", "hover", "color.text.body", "hover", "hover text (outlined body sans)", ThemeTokens.GetFloat("size.hover.font", 19f));
            if (hud.m_hoverName) hud.m_hoverName.richText = true;
            PieceHealth(hud);
            Stealth(hud);
            Plugin.Log.LogInfo("[Morgott.UI] crosshair attached");
            PlayHud.Reports.Add(() => Instance && root ? _art.Report(root) : null);
        }

        private void PieceHealth(Hud hud)
        {
            var root = hud.m_pieceHealthRoot;
            if (!root) return;
            var darken = PlayHud.Child(root, "darken");
            if (darken && _art.Reskin(darken, "sprite.bar.skill.trough", "color.frame", "piece health trough")) darken.type = Image.Type.Sliced;
            PlayHud.Hide(_art, PlayHud.Child(root, "bkg"), "vanilla bar background (the trough replaces it)");
            PlayHud.Fill(_art, hud.m_pieceHealthBar, "sprite.bar.skill.fill", "color.text.ok", "piece health fill");
        }

        private void Stealth(Hud hud)
        {
            Eye(hud.m_hidden, "color.text.body", "stealth: hidden");
            Eye(hud.m_targeted, "color.text.active", "stealth: targeted");
            Eye(hud.m_targetedAlert, "color.text.ember", "stealth: alerted");
            var bar = hud.m_stealthBar;
            if (!bar) return;
            PlayHud.Hide(_art, PlayHud.Child(bar.transform, "darken"), "vanilla bar shadow");
            var bkg = PlayHud.Child(bar.transform, "bkg");
            if (bkg && _art.Reskin(bkg, "sprite.bar.skill.trough", "color.frame", "stealth trough")) bkg.type = Image.Type.Sliced;
            PlayHud.Fill(_art, bar, "sprite.bar.skill.fill", "color.hud.glyph", "stealth fill (rune glow)");
        }

        private void Eye(GameObject go, string color, string reason)
        {
            if (!go || !go.TryGetComponent<Image>(out var img)) return;
            if (!_art.Reskin(img, "sprite.glyph.stealth.eye", color, reason)) return;
            img.type = Image.Type.Simple;
            img.preserveAspect = true;
            img.rectTransform.sizeDelta = PlayHud.RefSize("sprite.glyph.stealth.eye", new Vector2(48f, 28f)) * ThemeTokens.GetFloat("scale.stealth.eye", 0.9f);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // Hud.UpdateCrosshair postfix: vanilla colours -> theme colours, hover key tags gold.
        internal void AfterCrosshair()
        {
            var c = _hud.m_crosshair;
            if (c)
            {
                var want = c.color == Color.yellow ? _gold : _rest;
                if (c.color != want) c.color = want;
            }
            var t = _hud.m_hoverName;
            if (!t) return;
            // Vanilla writes the hover text every frame (Hud.cs:838): the gold copy is made once per distinct text.
            var raw = t.text;
            if (ReferenceEquals(raw, _hoverGold)) return;
            if (!string.Equals(raw, _hoverRaw, StringComparison.Ordinal))
            {
                _hoverRaw = raw;
                _hoverGold = raw != null && raw.IndexOf("<color=yellow>", StringComparison.Ordinal) >= 0 ? raw.Replace("<color=yellow>", _goldTag) : null;
            }
            if (_hoverGold != null) t.text = _hoverGold;
        }

        private string _hoverRaw, _hoverGold;
    }

    // ---- Action progress, stagger, event line, mount -----------------------------------------------------------------

    // Action progress / stagger (art notes): bar_frame_action around the bar (window inset by its border), bar_trough
    // + bar_fill (action in the rune glow, stagger keeps vanilla's colour), serif label. Event / raid line:
    // band_message behind the ember serif title + small mirrored banner_rule at both ends (UpdateEvent writes the
    // name). Mount panel: popup frame, portrait in a button_round socket, bar_skill trough / fill (health red, lag
    // ghost, stamina yellow), outlined texts.
    public sealed class ActionHudSkin : MonoBehaviour
    {
        public static ActionHudSkin Instance { get; private set; }

        // The equip / action progress bar is shown while the inventory is open (you equip from it), but the RuneSkin
        // inventory panels span the bottom centre where it sits and draw after the HUD on the same root canvas (and
        // CoverCull then gated it as covered): its own canvas sorts one above the GUI, under modals (+5) and tooltips (+10).
        // Added at Init, so a RenderGate never finds a canvas of its own there. Lifted only while the inventory is
        // shown: vanilla draws the bar inside hudroot, under LoadingBlack (Hud.m_loadingScreen, hudroot's later sibling),
        // so with the inventory closed the canvas sorts nested again and the loading / teleport screen covers it.
        private const int ActionBand = 1;
        private readonly HudArt _art = new HudArt(OwnershipRegistry.ActionHud);
        private Hud _hud;
        private Image _band;
        private RectTransform _ruleL, _ruleR;
        private string _eventText;

        public static void Attach(Hud hud)
        {
            if (!hud || hud.GetComponent<ActionHudSkin>()) return;
            hud.gameObject.AddComponent<ActionHudSkin>().Init(hud);
        }

        private void Init(Hud hud)
        {
            Instance = this;
            _hud = hud;
            PlayHud.SyncPpu(_art, hud.m_rootObject.transform);
            if (hud.m_actionBarRoot) Bar(hud.m_actionBarRoot.transform, hud.m_actionProgress, "color.hud.glyph", hud.m_actionName, "action progress");
            if (hud.m_actionBarRoot && !hud.m_actionBarRoot.GetComponent<Canvas>()) hud.m_actionBarRoot.AddComponent<Canvas>();
            if (hud.m_staggerProgress) Bar(hud.m_staggerProgress.transform, hud.m_staggerProgress, null, null, "stagger");
            if (hud.m_staggerProgress && PlayHud.Child(hud.m_staggerProgress.transform, "StaggerIcon") is Image icon) _art.Content(icon, "stagger icon (vanilla)");
            Event(hud);
            Mount(hud);
            Plugin.Log.LogInfo("[Morgott.UI] action HUD attached");
            var root = hud.m_rootObject.transform;
            PlayHud.Reports.Add(() => Instance && root ? _art.Report(root) : null);
        }

        private void Bar(Transform root, GuiBar bar, string fillColor, TMP_Text label, string what)
        {
            PlayHud.Hide(_art, PlayHud.Child(root, "darken"), $"{what}: vanilla shadow (frame replaces it)");
            var bkg = PlayHud.Child(root, "bkg");
            if (bkg && _art.Reskin(bkg, "sprite.hud.bar.trough", "color.hud.trough", $"{what} trough")) bkg.type = Image.Type.Sliced;
            PlayHud.Fill(_art, bar, "sprite.hud.bar.fill", fillColor, $"{what} fill");
            PlayHud.EdgeRim(_art, bar, "color.hud.edge");
            if (bkg)
            {
                var frame = PlayHud.FrameAround(_art, "RuneSkin_ActionFrame", bkg.rectTransform, bar && bar.m_bar ? bar.m_bar : null, "sprite.bar.frame.action",
                    ThemeTokens.GetFloat("scale.action.frame", 0.5f));
                if (frame && label) frame.rectTransform.SetSiblingIndex(label.transform.GetSiblingIndex());
            }
            PlayHud.Text(_art, label, "font.body", "action", "color.text.body", what + ".label", $"{what} label (outlined serif)", ThemeTokens.GetFloat("size.action.font", 19f));
            // Label above the framed bar (vanilla centres a 16 px auto-sized line inside the 20 px bar; the frame and the
            // serif would cut it): one line, overflowing sideways, its bottom on the frame top.
            if (label && bkg)
            {
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.overflowMode = TextOverflowModes.Overflow;
                label.alignment = TextAlignmentOptions.Bottom;
                var lr = label.rectTransform;
                var br = bkg.rectTransform;
                var lift = br.rect.height * 0.5f + PlayHud.RefBorder("sprite.bar.frame.action").w * ThemeTokens.GetFloat("scale.action.frame", 0.5f) + ThemeTokens.GetFloat("size.action.label.gap", 4f);
                lr.anchorMin = lr.anchorMax = br.anchorMin == br.anchorMax ? br.anchorMin : new Vector2(0.5f, 0.5f);
                lr.pivot = new Vector2(0.5f, 0f);
                lr.sizeDelta = new Vector2(Mathf.Max(br.rect.width, 200f), label.fontSize * 1.4f);
                lr.anchoredPosition = br.anchoredPosition + new Vector2((0.5f - br.pivot.x) * br.rect.width, (0.5f - br.pivot.y) * br.rect.height + lift);
            }
        }

        private void Event(Hud hud)
        {
            if (!hud.m_eventBar) return;
            var root = hud.m_eventBar.transform;
            _band = PlayHud.Child(root, "Image");
            if (_band && _art.Reskin(_band, "sprite.band.message", "color.frame", "event line band")) _band.type = Image.Type.Sliced;
            PlayHud.Text(_art, hud.m_eventName, "font.display", "event", "color.text.ember", "event.title", "event / raid title (ember serif)", ThemeTokens.GetFloat("size.event.font", 30f));
            if (!hud.m_eventName) return;
            var size = PlayHud.RefSize("sprite.banner.rule", new Vector2(156f, 29f)) * ThemeTokens.GetFloat("scale.event.rule", 0.6f);
            _ruleL = Rule("RuneSkin_RuleLeft", hud.m_eventName.rectTransform, size, true);
            _ruleR = Rule("RuneSkin_RuleRight", hud.m_eventName.rectTransform, size, false);
        }

        private RectTransform Rule(string name, RectTransform title, Vector2 size, bool mirrored)
        {
            var img = _art.Image(name, title.parent, "sprite.banner.rule", "color.frame", Image.Type.Simple);
            var rt = img.rectTransform;
            rt.SetSiblingIndex(title.GetSiblingIndex());
            rt.anchorMin = rt.anchorMax = title.anchorMin == title.anchorMax ? title.anchorMin : new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.sizeDelta = size;
            if (mirrored) rt.localScale = new Vector3(-1f, 1f, 1f);
            return rt;
        }

        private void Mount(Hud hud)
        {
            var panel = hud.m_mountPanel ? hud.m_mountPanel.transform : null;
            if (!panel) return;
            var darken = PlayHud.Child(panel, "darken");
            if (darken && _art.Reskin(darken, "sprite.frame.tooltip", "color.frame", "mount panel popup")) darken.type = Image.Type.Sliced;
            if (hud.m_mountIcon)
            {
                _art.Content(hud.m_mountIcon, "mount portrait (creature trophy icon)");
                var ir = hud.m_mountIcon.rectTransform;
                var socket = _art.Image("RuneSkin_PortraitSocket", ir.parent, "sprite.button.round", "color.frame", Image.Type.Simple);
                var sr = socket.rectTransform;
                sr.SetSiblingIndex(ir.GetSiblingIndex());
                sr.anchorMin = ir.anchorMin;
                sr.anchorMax = ir.anchorMax;
                sr.pivot = ir.pivot;
                sr.anchoredPosition = ir.anchoredPosition;
                sr.sizeDelta = ir.sizeDelta * ThemeTokens.GetFloat("scale.mount.socket", 1.6f);
                socket.preserveAspect = true;
            }
            foreach (var group in new[] { "Health", "Stamina" })
                if (panel.Find(group) is Transform g && PlayHud.Child(g, "bkg") is Image bkg && _art.Reskin(bkg, "sprite.bar.skill.trough", "color.frame", $"mount {group} trough"))
                    bkg.type = Image.Type.Sliced;
            PlayHud.Fill(_art, hud.m_mountHealthBarFast, "sprite.bar.skill.fill", "color.hud.health", "mount health");
            PlayHud.Fill(_art, hud.m_mountHealthBarSlow, "sprite.bar.skill.fill", "color.hud.lag", "mount health lag");
            PlayHud.Fill(_art, hud.m_mountStaminaBar, "sprite.bar.skill.fill", "color.hud.stamina", "mount stamina");
            PlayHud.Text(_art, hud.m_mountNameText, "font.display", "mount", "color.text.header", "mount.name", "mount name (serif)");
            PlayHud.Text(_art, hud.m_mountHealthText, "font.body", "hudvalue", "color.hud.text", "mount.value", "mount health value");
            PlayHud.Text(_art, hud.m_mountStaminaText, "font.body", "hudvalue", "color.hud.text", "mount.value", "mount stamina value");
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // The hidden first open (InventorySkin.GhostCursor: alpha 0, behind the spawn's loading screen) draws no panels.
        private static void SortBar(GameObject bar)
        {
            var skin = InventoryScreen.InventorySkin.Instance;
            if (InventoryGui.IsVisible() && !(skin && skin.GhostCursor))
            {
                if (bar.activeInHierarchy) Restyle.TopOverlay.Lift(bar, ActionBand); // base order changes as the inventory opens
            }
            else if (bar.TryGetComponent<Canvas>(out var c) && c.overrideSorting) c.overrideSorting = false;
        }

        private void LateUpdate()
        {
            var bar = _hud ? _hud.m_actionBarRoot : null;
            if (bar) SortBar(bar);
            var t = _hud ? _hud.m_eventName : null;
            if (!t || !_ruleL || !_ruleR || t.text == _eventText || !t.gameObject.activeInHierarchy) return;
            _eventText = t.text;
            t.ForceMeshUpdate();
            var b = t.textBounds;
            var gap = ThemeTokens.GetFloat("size.event.rule.gap", 16f);
            var x = PlayHudMath.RuleOffset(b.size.x, gap);
            var tr = t.rectTransform;
            var centre = tr.anchoredPosition + new Vector2(b.center.x, b.center.y);
            _ruleR.anchoredPosition = centre + new Vector2(x, 0f);
            _ruleL.anchoredPosition = centre - new Vector2(x, 0f);
            if (_band)
            {
                var br = _band.rectTransform;
                var pad = ThemeTokens.GetFloat("size.message.band.pad", 70f);
                br.sizeDelta = new Vector2(2f * (x + _ruleR.sizeDelta.x) + pad, Mathf.Max(br.sizeDelta.y, b.size.y + 2f * ThemeTokens.GetFloat("size.message.band.vpad", 8f)));
            }
        }
    }

    // ---- Ship HUD ------------------------------------------------------------------------------------------------

    // Ship HUD (mockup m6-ship-b): a vertical popup column, top-down sail full / sail half / oar / stop / reverse,
    // the active speed in slot_cell_selected with its glyph in the rune glow (no text labels, mockup 2026-11-06). Under it,
    // centred on the column's axis, the rudder row: arrow_left | rudder in a button_round socket inside a gp_ring angle
    // arc (vanilla m_shipRudderIndicator, PlayHudMath.RudderArc: slate top-half track, gold zero tick at 12 o'clock) |
    // arrow_right, the turned side lit (PlayHudMath.RudderSideAlpha). Vanilla's wind compass (WindIndicator) stays drawn,
    // reskinned beside the block (Compass). Vanilla's PowerIcon and Controls (following the tiller) stay alive for
    // Hud.UpdateShipHud but are drawn at alpha 0.
    // Placement (tester 2026-10-26: the ladder sat on the vanilla PowerIcon spot, under the RuneSkin minimap ring):
    // screen px from the minimap footprint (Minimap.m_smallRoot + its plaque / key tip children, reserved whether the
    // small map shows or not) and the HUD strip top, PlayHudMath.ShipPlacement; hidden while the large map is open.
    public static class ShipHudConfig
    {
        public const string Section = "ShipHUD";

        public static ConfigEntry<ShipSpeedUnit> SpeedUnit { get; private set; }
        public static ConfigEntry<bool> ShowWindEfficiencyArc { get; private set; }
        public static ConfigEntry<bool> ShowSpeedReadout { get; private set; }

        internal static void Bind(ConfigFile config)
        {
            SpeedUnit = config.Bind(Section, "SpeedUnit", ShipSpeedUnit.MetersPerSecond,
                "Unit for live ship speed readout below the ship HUD (MetersPerSecond or KilometersPerHour). Applied live.");
            ShowWindEfficiencyArc = config.Bind(Section, "ShowWindEfficiencyArc", true,
                "Show the color-coded sail efficiency arc around the ship wind compass dial.");
            ShowSpeedReadout = config.Bind(Section, "ShowSpeedReadout", true,
                "Show sail thrust percentage and live boat speed below the ship rudder indicator.");
        }
    }

    public sealed class ShipHudSkin : MonoBehaviour
    {
        public static ShipHudSkin Instance { get; private set; }
        private readonly HudArt _art = new HudArt(OwnershipRegistry.ShipHud);
        private static readonly string[] Glyphs = { "sprite.glyph.ship.sail.full", "sprite.glyph.ship.sail.half", "sprite.glyph.ship.oar", "sprite.glyph.stop", "sprite.glyph.chevron.down" };
        private Hud _hud;
        private RectTransform _column, _efficiencyRoot;
        private CanvasGroup _group;
        private readonly Image[] _cells = new Image[PlayHudMath.LadderCells];
        private readonly Image[] _glyphs = new Image[PlayHudMath.LadderCells];
        private Image _left, _right, _arc, _windIcon;
        private Image[] _arcSegments;
        private Color[] _arcColors;
        private TMP_Text _thrustText, _speedText;
        private int _shown = -1, _lastThrustPct = -1, _lastSpeedSetting = -1, _lastSpeedTenths = -1;
        private float _rudderShown = float.NaN, _smoothedSpeed, _baseBlockHeight;
        private bool _hiddenForMap, _arcShown = true, _readoutShown = true;
        private WindEfficiencyZone _activeZone = (WindEfficiencyZone)(-1), _lastThrustZone = (WindEfficiencyZone)(-1);
        private ShipSpeedUnit _lastSpeedUnit = (ShipSpeedUnit)(-1);
        private string _lastLang;
        private float _compassUnits; // compass left of the block: placed with it (ShipPlacement min-left)
        private Vector2 _blockUnits, _placedAt = new Vector2(float.NaN, float.NaN);
        private readonly Vector3[] _corners = new Vector3[4];
        private readonly RectTransform[] _round = new RectTransform[2]; // compass socket + ring: kept unrotated
        public int ActiveIndex => _shown;
        // Harness read side: the last placement (screen px top-right corner) and whether it went beside the minimap.
        public Vector2 PlacedTopRight => _placedAt;
        public bool PlacedBeside { get; private set; }

        public static void Attach(Hud hud)
        {
            if (!hud || !hud.m_shipHudRoot || hud.GetComponent<ShipHudSkin>()) return;
            hud.gameObject.AddComponent<ShipHudSkin>().Init(hud);
        }

        private void Init(Hud hud)
        {
            Instance = this;
            _hud = hud;
            var root = hud.m_shipHudRoot.transform;
            PlayHud.SyncPpu(_art, root);
            foreach (var t in new[] { hud.m_fullSail ? hud.m_fullSail.transform.parent?.parent : null, hud.m_shipControlsRoot ? hud.m_shipControlsRoot.transform : null })
            {
                if (!t) continue;
                var g = t.GetComponent<CanvasGroup>() ?? t.gameObject.AddComponent<CanvasGroup>();
                g.alpha = 0f;
                g.blocksRaycasts = false;
                Morgott.UI.Restyle.RenderGate.Set(t, "shiphud", true); // not drawn (vanilla code keeps writing it)
                _art.Suppressed(t, $"vanilla ship HUD '{t.name}' (RuneSkin ladder replaces it)");
            }
            var cell = ThemeTokens.GetFloat("size.ship.cell", 46f);
            var gap = ThemeTokens.GetFloat("size.ship.gap", 6f);
            var pad = ThemeTokens.GetFloat("size.ship.pad", 12f);
            var rs = ThemeTokens.GetFloat("size.ship.rudder", 64f);
            // Angle ring diameter: gp_ring's stroke (alpha > 100) spans 3..7.5 px of its 112, so at 1.34 x the 64-unit
            // socket it is ~4 units thick with its inner edge ~5 units outside the socket rim (1080p = 1 unit per px).
            var ring = rs * ThemeTokens.GetFloat("share.ship.arc", 1.34f);
            var arrow = ThemeTokens.GetFloat("size.ship.arrow", 28f);
            var ladderH = PlayHudMath.LadderHeight(cell, gap, pad);
            _column = HudArt.Rect("RuneSkin_ShipLadder", root);
            _column.anchorMin = _column.anchorMax = new Vector2(1f, 1f);
            _column.pivot = new Vector2(1f, 1f);
            _group = _column.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            var popup = _art.Image("Popup", _column, "sprite.frame.tooltip", "color.frame", Image.Type.Sliced);
            var block = PlayHudMath.ShipBlock(cell, gap, pad, ring, arrow);
            _baseBlockHeight = block.height;
            _readoutShown = ShipHudConfig.ShowSpeedReadout?.Value ?? true;
            _blockUnits = new Vector2(block.width, _baseBlockHeight + (_readoutShown ? ShipHudRules.ReadoutBlockHeight : 0f));
            _column.sizeDelta = _blockUnits;
            // Column, cells and rudder row share the block's vertical centre line (anchor / pivot x 0.5).
            Pin(popup, 0f, 0f, 2f * pad + cell, ladderH);

            var inset = cell * ThemeTokens.GetFloat("share.ship.glyph.inset", 0.18f);
            for (var i = 0; i < PlayHudMath.LadderCells; i++)
            {
                var top = PlayHudMath.LadderCellTop(i, cell, gap, pad);
                var c = Pin(_art.Image("Cell" + i, _column, "sprite.slot.cell.selected", "color.frame", Image.Type.Sliced), 0f, -top, cell, cell);
                c.gameObject.SetActive(false);
                _cells[i] = c;
                var g = Pin(_art.Image("Glyph" + i, _column, Glyphs[i], "color.text.body", Image.Type.Simple), 0f, -top - inset, cell - 2f * inset, cell - 2f * inset);
                g.preserveAspect = true;
                _glyphs[i] = g;
            }

            // Rudder row: arrow_left | socket + rudder glyph inside the angle ring | arrow_right.
            var rowY = -ladderH - gap;
            var socket = Pin(_art.Image("RudderSocket", _column, "sprite.button.round", "color.frame", Image.Type.Simple), 0f, rowY - (ring - rs) * 0.5f, rs, rs);
            socket.preserveAspect = true;
            var rudder = _art.Image("Rudder", socket.rectTransform, "sprite.glyph.rudder", "color.text.active", Image.Type.Simple);
            rudder.preserveAspect = true;
            var rr = rudder.rectTransform;
            rr.anchorMin = rr.anchorMax = rr.pivot = new Vector2(0.5f, 0.5f);
            rr.anchoredPosition = Vector2.zero;
            rr.sizeDelta = new Vector2(rs, rs) * ThemeTokens.GetFloat("share.ship.rudder.glyph", 0.72f);
            // Track over the top half (9 -> 3 o'clock): the arc's full range, |v| * 0.25 either way from 12 o'clock.
            Radial(Pin(_art.Image("RudderTrack", _column, "sprite.hud.gp.ring", "color.hud.ship.track", Image.Type.Simple), 0f, rowY, ring, ring), Image.Origin360.Left, true, 0.5f);
            _arc = Radial(Pin(_art.Image("RudderArc", _column, "sprite.hud.gp.ring", "color.hud.glyph", Image.Type.Simple), 0f, rowY, ring, ring), Image.Origin360.Top, true, 0f);
            _arc.gameObject.SetActive(false);
            var tickH = ThemeTokens.GetFloat("size.ship.tick.h", 10f);
            Pin(_art.Image("RudderTick", _column, null, "color.text.active", Image.Type.Simple), 0f, rowY + tickH * 0.3f, ThemeTokens.GetFloat("size.ship.tick.w", 3f), tickH);
            var arrowX = ring * 0.5f + gap + arrow * 0.5f;
            _left = Arrow("RudderLeft", "sprite.glyph.arrow.left", -arrowX, rowY - (ring - arrow) * 0.5f, arrow);
            _right = Arrow("RudderRight", "sprite.glyph.arrow.right", arrowX, rowY - (ring - arrow) * 0.5f, arrow);
            var readoutY = -_baseBlockHeight - ShipHudRules.ReadoutGap;
            var readoutSize = MessageSkin.HudReadable(ThemeTokens.GetFloat("size.ship.readout.font", 16f));
            _thrustText = Pin(_art.Text("RuneSkin_ShipThrust", _column, "font.body", "color.text.body", readoutSize, TextAlignmentOptions.Center),
                0f, readoutY, block.width, ShipHudRules.ReadoutLineHeight);
            TextLook.ApplyNameplate(_thrustText, "font.body", "ship.readout", 0f);
            _thrustText.fontSize = readoutSize;
            _art.Note(_thrustText, "ship.thrust", true, "sail thrust percentage readout");
            _speedText = Pin(_art.Text("RuneSkin_ShipSpeed", _column, "font.body", "color.text.body", readoutSize, TextAlignmentOptions.Center),
                0f, readoutY - ShipHudRules.ReadoutLineHeight - ShipHudRules.ReadoutLineGap, block.width, ShipHudRules.ReadoutLineHeight);
            TextLook.ApplyNameplate(_speedText, "font.body", "ship.readout", 0f);
            _speedText.fontSize = readoutSize;
            _art.Note(_speedText, "ship.speed", true, "live ship speed readout");
            _thrustText.gameObject.SetActive(_readoutShown);
            _speedText.gameObject.SetActive(_readoutShown);
            Compass(hud, rs, ring, arrow, gap);
            Plugin.Log.LogInfo($"[Morgott.UI] ship HUD attached (block {block.width:0}x{block.height:0} units)");
            PlayHud.Reports.Add(() => Instance && root ? _art.Report(root) : null);
        }

        // Vanilla wind compass (hudroot/ShipHud/WindIndicator: Ship, Circle, Wind/WindIcon) kept live for Hud.UpdateShipHud
        // (root z = ship yaw vs camera, Wind z = wind vs ship): moved left of the block's top edge (out of the minimap
        // footprint, follows Place + the large-map hide) and laid out as the rudder button: button_round socket (rs) inside
        // the gp_ring (ring), hull glyph in the middle, the wind arrow (rudder-arrow size) between hull and ring, on the
        // orbit side the prefab puts it. Sizes in ladder units, every part centred on the pivot vanilla rotates about.
        private void Compass(Hud hud, float socket, float ring, float glyph, float gap)
        {
            var w = hud.m_shipWindIndicatorRoot;
            var wind = hud.m_shipWindIconRoot;
            _windIcon = hud.m_shipWindIcon;
            if (!w || !wind || !_windIcon) return;
            var icon = _windIcon.rectTransform;
            var side = ((Vector2)wind.InverseTransformPoint(icon.TransformPoint(icon.rect.center))).normalized; // orbit side, Wind-local
            w.SetParent(_column, false);
            w.anchorMin = w.anchorMax = new Vector2(0f, 1f);
            w.localScale = Vector3.one;
            w.anchoredPosition = new Vector2(-gap - ring * 0.5f, -ring * 0.5f);
            // The prefab's root graphic (rect not tied to the pivot) drew no disc in game (run 20261122-211132): the socket is ours.
            var own = w.GetComponent<Image>();
            if (own) own.enabled = false;
            var disc = _art.Image("CompassSocket", w, "sprite.button.round", "color.frame", Image.Type.Simple);
            disc.preserveAspect = true;
            disc.transform.SetAsFirstSibling();
            Centre(disc.rectTransform, w, socket);
            _round[0] = disc.rectTransform;
            var circle = w.Find("Circle")?.GetComponent<Image>();
            if (_art.Reskin(circle, "sprite.hud.gp.ring", "color.hud.ship.track", "ship wind compass ring"))
            {
                Centre(circle.rectTransform, w, ring);
                _round[1] = circle.rectTransform;
            }
            _efficiencyRoot = HudArt.Rect("RuneSkin_WindEfficiency", w);
            if (circle) _efficiencyRoot.SetSiblingIndex(circle.transform.GetSiblingIndex() + 1);
            Centre(_efficiencyRoot, w, ring);
            var segs = ShipHudRules.ArcSegments;
            _arcSegments = new Image[segs.Length];
            _arcColors = new Color[segs.Length];
            for (var i = 0; i < segs.Length; i++)
            {
                var seg = segs[i];
                if (!HexColor.TryParse(seg.ColorHex, out var c)) c = Color.white;
                _arcColors[i] = c;
                var arcImg = _art.Image("Arc" + i, _efficiencyRoot, "sprite.hud.gp.ring", null, Image.Type.Simple);
                MapScreen.MinimapSkin.Stretch(arcImg.rectTransform);
                Radial(arcImg, Image.Origin360.Top, true, seg.SpanDeg / 360f);
                arcImg.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -seg.StartDeg);
                c.a = 0.62f;
                arcImg.color = c;
                _arcSegments[i] = arcImg;
            }
            _arcShown = ShipHudConfig.ShowWindEfficiencyArc?.Value ?? true;
            _efficiencyRoot.gameObject.SetActive(_arcShown);
            var hull = w.Find("Ship")?.GetComponent<Image>();
            Tint(hull, "color.text.active", "ship heading silhouette (vanilla art)");
            if (hull)
            {
                hull.preserveAspect = true;
                Centre(hull.rectTransform, w, ring - 2f * glyph); // clear of the arrow on any side
            }
            Centre(wind, w, ring);
            var sector = wind.GetComponent<Image>();
            if (sector) sector.enabled = false; // vanilla wedge art, no theme counterpart: the arrow carries the cue
            if (_art.Reskin(_windIcon, "sprite.glyph.wind", null, "wind arrow (tint per frame, AfterShipHud)"))
            {
                _windIcon.preserveAspect = true;
                Centre(icon, w, glyph);
                icon.localRotation = Quaternion.identity; // vanilla windicon art points right (+90 in the prefab); glyph_wind points up
                icon.position = wind.TransformPoint(side * ((ring - glyph) * 0.5f * w.lossyScale.x / wind.lossyScale.x));
            }
            // Keep every rect inside the ladder's top edge whatever the yaw: socket + ring stay unrotated (AfterShipHud), the
            // rotating rects (hull, arrow) sweep a circle through their farthest corner (rotation-invariant, w-local units).
            var reach = ring * 0.5f;
            foreach (var g in w.GetComponentsInChildren<Graphic>(true)) // ShipHud is inactive at Hud.Awake
            {
                if (!g.enabled || Array.IndexOf(_round, g.rectTransform) >= 0 || (_efficiencyRoot && g.transform.IsChildOf(_efficiencyRoot))) continue;
                g.rectTransform.GetWorldCorners(_corners);
                foreach (var c in _corners) reach = Mathf.Max(reach, ((Vector2)w.InverseTransformPoint(c)).magnitude);
            }
            w.anchoredPosition = new Vector2(-gap - ring * 0.5f, -reach);
            _compassUnits = gap + ring * 0.5f + reach;
        }

        // Square rect of `units` ladder units (w's scale) centred on w's pivot.
        private static void Centre(RectTransform rt, RectTransform w, float units)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.one * (units * w.lossyScale.x / rt.lossyScale.x);
            rt.position = w.position;
        }

        private void Tint(Image img, string colorToken, string reason)
        {
            if (!img) return;
            img.color = ThemeTokens.GetColor(colorToken, img.color);
            _art.Note(img, colorToken, true, reason);
        }

        // Postfix of Hud.UpdateShipHud: vanilla's tint Lerp(s_shipWindIconColor, white, GetWindAngleFactor) in theme
        // colours (idle glyph -> lit glyph, as the ladder), so a good sailing wind stays visibly brighter.
        internal void AfterShipHud(Player player)
        {
            var ship = player ? player.GetControlledShip() : null;
            if (!ship) return;
            // Round art does not turn with vanilla's yaw rotation: its square rect would stick out by up to sqrt 2 x radius.
            foreach (var r in _round) if (r) r.rotation = _column.rotation;
            SyncEfficiencyArc(ship);
            if (!_windIcon) return;
            var off = ThemeTokens.GetColor("color.text.body", Color.white);
            off.a = ThemeTokens.GetFloat("share.ship.idle.alpha", 0.55f);
            _windIcon.color = Color.Lerp(off, ThemeTokens.GetColor("color.hud.glyph", Color.cyan), ship.GetWindAngleFactor());
        }

        private void SyncEfficiencyArc(Ship ship)
        {
            if (!_efficiencyRoot || _arcSegments == null) return;
            var wantArc = ShipHudConfig.ShowWindEfficiencyArc?.Value ?? true;
            if (wantArc != _arcShown)
            {
                _arcShown = wantArc;
                _efficiencyRoot.gameObject.SetActive(wantArc);
            }
            if (!wantArc || !ship || !EnvMan.instance) return;
            var zone = ShipHudRules.ClassifyZone(ShipHudRules.AngleFromBowDeg(ship.GetWindAngle()));
            if (zone == _activeZone) return;
            _activeZone = zone;
            for (var i = 0; i < _arcSegments.Length; i++)
            {
                var segImg = _arcSegments[i];
                if (!segImg) continue;
                var c = _arcColors[i];
                c.a = ShipHudRules.ArcSegments[i].Zone == zone ? 0.96f : 0.56f;
                segImg.color = c;
            }
        }

        // Top-centre anchored rect in the block (x from the centre line, y down from the top).
        private static T Pin<T>(T g, float x, float y, float w, float h) where T : Graphic
        {
            var r = g.rectTransform;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(w, h);
            return g;
        }

        // Radial360 fill as HoldPromptSkin's ring (no sprite = a plain rect: left unfilled).
        private static Image Radial(Image img, Image.Origin360 origin, bool clockwise, float amount)
        {
            if (!img.sprite) return img;
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Radial360;
            img.fillOrigin = (int)origin;
            img.fillClockwise = clockwise;
            img.fillAmount = amount;
            return img;
        }

        private Image Arrow(string name, string sprite, float x, float y, float size)
        {
            var a = Pin(_art.Image(name, _column, sprite, "color.text.body", Image.Type.Simple), x, y, size, size);
            a.preserveAspect = true;
            return a;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void LateUpdate()
        {
            var player = Player.m_localPlayer;
            var ship = player ? player.GetControlledShip() : null;
            if (!ship || !_column) return;
            SyncConfigReactive(ship);
            HideForLargeMap();
            Place();
            var index = PlayHudMath.LadderIndex((int)ship.GetSpeedSetting());
            if (index != _shown)
            {
                _shown = index;
                var on = ThemeTokens.GetColor("color.hud.glyph", Color.cyan);
                var off = ThemeTokens.GetColor("color.text.body", Color.white);
                off.a = ThemeTokens.GetFloat("share.ship.idle.alpha", 0.55f);
                for (var i = 0; i < PlayHudMath.LadderCells; i++)
                {
                    if (_cells[i].gameObject.activeSelf != (i == index)) _cells[i].gameObject.SetActive(i == index);
                    _glyphs[i].color = i == index ? on : off;
                }
            }
            var value = ship.GetRudderValue();
            if (!(Mathf.Abs(value - _rudderShown) < 0.005f))
            {
                _rudderShown = value;
                var arc = PlayHudMath.RudderArc(value);
                if (_arc.gameObject.activeSelf != arc.shown) _arc.gameObject.SetActive(arc.shown);
                _arc.fillClockwise = arc.clockwise;
                _arc.fillAmount = arc.amount;
                var idle = ThemeTokens.GetFloat("share.ship.idle.alpha", 0.55f) * 0.6f;
                var minLit = ThemeTokens.GetFloat("share.ship.rudder.lit.min", 0.65f);
                Side(_left, PlayHudMath.RudderSideAlpha(value, -1, idle, minLit), idle);
                Side(_right, PlayHudMath.RudderSideAlpha(value, 1, idle, minLit), idle);
            }
            UpdateReadouts(ship, Time.deltaTime);
        }

        private void SyncConfigReactive(Ship ship)
        {
            SyncEfficiencyArc(ship);
            var wantReadout = ShipHudConfig.ShowSpeedReadout?.Value ?? true;
            if (wantReadout == _readoutShown) return;
            _readoutShown = wantReadout;
            if (_thrustText) _thrustText.gameObject.SetActive(wantReadout);
            if (_speedText) _speedText.gameObject.SetActive(wantReadout);
            _blockUnits.y = _baseBlockHeight + (wantReadout ? ShipHudRules.ReadoutBlockHeight : 0f);
            _column.sizeDelta = _blockUnits;
            _placedAt = new Vector2(float.NaN, float.NaN);
        }

        private void UpdateReadouts(Ship ship, float dt)
        {
            if (!_readoutShown || !_thrustText || !_speedText) return;
            var vel = ship.m_body ? ship.m_body.linearVelocity : Vector3.zero;
            var raw = ShipHudRules.HorizontalSpeed(vel.x, vel.z);
            _smoothedSpeed = ShipHudRules.StepSmoothedSpeed(_smoothedSpeed, raw, dt);

            var lang = Localization.instance != null ? Localization.instance.GetSelectedLanguage() : "";
            var langChanged = !string.Equals(lang, _lastLang, StringComparison.Ordinal);

            var unit = ShipHudConfig.SpeedUnit?.Value ?? ShipSpeedUnit.MetersPerSecond;
            var converted = ShipHudRules.ConvertSpeed(_smoothedSpeed < 0.05f ? 0f : _smoothedSpeed, unit);
            var tenths = Mathf.RoundToInt(converted * 10f);
            if (tenths != _lastSpeedTenths || unit != _lastSpeedUnit || langChanged)
            {
                _lastSpeedTenths = tenths;
                _lastSpeedUnit = unit;
                _speedText.text = ShipHudRules.FormatSpeed(_smoothedSpeed, unit, Words.Get("$ui_ship_unit_ms"), Words.Get("$ui_ship_unit_kmh"));
            }

            var speedSetting = (int)ship.GetSpeedSetting();
            var hasEnv = EnvMan.instance != null;
            var factor = hasEnv ? ship.GetWindAngleFactor() : 0f;
            var pct = ShipHudRules.ThrustPercent(factor);
            var zone = hasEnv ? ShipHudRules.ClassifyZone(ShipHudRules.AngleFromBowDeg(ship.GetWindAngle())) : WindEfficiencyZone.DeadZone;
            if (pct != _lastThrustPct || speedSetting != _lastSpeedSetting || zone != _lastThrustZone || langChanged)
            {
                _lastThrustPct = pct;
                _lastSpeedSetting = speedSetting;
                _lastThrustZone = zone;
                _thrustText.text = ShipHudRules.FormatThrust(speedSetting, factor, Words.Get("$ui_ship_thrust"), Words.Get("$ui_ship_oars"), Words.Get("$ui_ship_calm"));
                _thrustText.color = ThrustColor(speedSetting, zone);
            }
            _lastLang = lang;
        }

        private static Color ThrustColor(int speedSetting, WindEfficiencyZone zone)
        {
            if (speedSetting == 3 || speedSetting == 4)
            {
                switch (zone)
                {
                    case WindEfficiencyZone.Optimal: return new Color(0.45f, 0.90f, 0.56f, 1f);
                    case WindEfficiencyZone.Tailwind: return ThemeTokens.GetColor("color.text.active", new Color(0.91f, 0.71f, 0.29f, 1f));
                    case WindEfficiencyZone.CloseHauled: return ThemeTokens.GetColor("color.text.ember", new Color(0.94f, 0.54f, 0.23f, 1f));
                    default: return ThemeTokens.GetColor("color.text.missing", new Color(0.88f, 0.25f, 0.23f, 1f));
                }
            }
            if (speedSetting == 1 || speedSetting == 2) return ThemeTokens.GetColor("color.hud.glyph", Color.cyan);
            var c = ThemeTokens.GetColor("color.text.body", Color.white);
            c.a = 0.78f;
            return c;
        }

        private static void Side(Image arrow, float alpha, float idle)
        {
            if (!arrow) return;
            var c = alpha > idle ? ThemeTokens.GetColor("color.hud.glyph", Color.cyan) : ThemeTokens.GetColor("color.text.body", Color.white);
            c.a = alpha;
            arrow.color = c;
        }

        // The large map covers the screen (the HUD cluster hides with it, LargeMap.HideHudCluster): the ladder too.
        private void HideForLargeMap()
        {
            var map = Minimap.instance;
            var hide = map && map.m_largeRoot && map.m_largeRoot.activeInHierarchy;
            if (hide == _hiddenForMap) return;
            _hiddenForMap = hide;
            _group.alpha = hide ? 0f : 1f;
            Morgott.UI.Restyle.RenderGate.Set(_column, "shiphud-map", hide);
        }

        // Screen placement (PlayHudMath.ShipPlacement) from the live minimap footprint and strip top; moved only when
        // the result changes by half a pixel or more (StripClear re-homes an element its owner moves).
        private void Place()
        {
            var parent = _column.parent as RectTransform;
            var canvas = parent ? parent.GetComponentInParent<Canvas>() : null;
            if (!canvas) return;
            var cam = canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.rootCanvas.worldCamera;
            var origin = RectTransformUtility.WorldToScreenPoint(cam, parent.position);
            var unit = RectTransformUtility.WorldToScreenPoint(cam, parent.position + parent.TransformVector(Vector3.up)).y - origin.y;
            if (!(unit > 0f)) return;
            var safe = Responsive.ResponsiveContext.SafeArea;
            HudMath.HudBox(safe.x, safe.width, safe.height, Plugin.UltrawideBox?.Value ?? true, ThemeTokens.GetFloat("ratio.hud.ultrawide", 2.2f), out var boxX, out var boxW);
            var s = HudMath.FrameScale(Responsive.ResponsiveContext.GuiFactor, Plugin.HudScale?.Value ?? 1f); // = the strip's gaps
            var margin = ThemeTokens.GetFloat("size.hud.margin", 24f) * s;
            if (!Footprint(out var fp)) fp = new Rect(boxX + boxW - margin, safe.yMax - margin, 0f, 0f);
            var strip = HudCluster.Instance ? HudCluster.Instance.StripTopPx : 0f;
            var floor = strip > 0f ? strip + ThemeTokens.GetFloat("size.hud.strip.clear.gap", 12f) * s : safe.y + margin;
            var gapPx = ThemeTokens.GetFloat("size.ship.screen.gap", 12f) * s;
            var p = PlayHudMath.ShipPlacement(fp.xMin, fp.xMax, fp.yMin, fp.yMax, boxX + margin, floor, (_blockUnits.x + _compassUnits) * unit, _blockUnits.y * unit, gapPx);
            var want = new Vector2(p.right, p.top);
            if (Mathf.Abs(want.x - _placedAt.x) < 0.5f && Mathf.Abs(want.y - _placedAt.y) < 0.5f) return;
            if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, want, cam, out var world)) return;
            _column.position = world;
            _placedAt = want;
            PlacedBeside = p.beside;
            Plugin.Log.LogInfo($"[Morgott.UI] ship HUD placed top-right ({want.x:0},{want.y:0}) px {(p.beside ? "beside" : "under")} the minimap footprint [{fp.xMin:0},{fp.yMin:0}]-[{fp.xMax:0},{fp.yMax:0}] (floor {floor:0} px)");
        }

        // Minimap footprint, screen px: the small root plus its direct children (ring art, biome plaque, key tip; the
        // RuneSkin ones always, others while active), from their rects - valid while the small map is hidden too.
        private bool Footprint(out Rect r)
        {
            r = default;
            var map = Minimap.instance;
            var small = map && map.m_smallRoot ? map.m_smallRoot.transform as RectTransform : null;
            if (!small) return false;
            var canvas = small.GetComponentInParent<Canvas>(true);
            var cam = canvas && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            void Add(RectTransform rt)
            {
                if (rt.rect.width < 1f || rt.rect.height < 1f) return;
                rt.GetWorldCorners(_corners);
                for (var k = 0; k < 4; k++)
                {
                    var sp = RectTransformUtility.WorldToScreenPoint(cam, _corners[k]);
                    x0 = Mathf.Min(x0, sp.x); x1 = Mathf.Max(x1, sp.x);
                    y0 = Mathf.Min(y0, sp.y); y1 = Mathf.Max(y1, sp.y);
                }
            }
            Add(small);
            for (var i = 0; i < small.childCount; i++)
            {
                var c = small.GetChild(i) as RectTransform;
                if (!c) continue;
                if (c.gameObject.activeSelf || Morgott.UI.Restyle.NameCache.Of(c).StartsWith("RuneSkin_", StringComparison.Ordinal) || Morgott.UI.Restyle.NameCache.Of(c) == "Key tip") Add(c);
            }
            if (x0 == float.MaxValue) return false;
            r = Rect.MinMaxRect(x0, y0, x1, y1);
            return true;
        }
    }

    // ---- Key hints -------------------------------------------------------------------------------------------------

    // Key hints (hudroot/KeyHints: combat, build, inventory, fishing, barber, radial groups; KeyHints.UpdateHints
    // toggles them): keyboard key plates ("key_bkg") = plate_key, key letters + labels in the outlined body sans;
    // controller / mouse glyph art kept. Template-free: every group exists at Awake.
    public sealed class KeyHintsSkin : MonoBehaviour
    {
        public static KeyHintsSkin Instance { get; private set; }
        private readonly HudArt _art = new HudArt(OwnershipRegistry.KeyHints);

        public static void Attach(Hud hud)
        {
            var hints = hud ? hud.GetComponentInChildren<KeyHints>(true) : null;
            if (!hints || hints.GetComponent<KeyHintsSkin>()) return;
            hints.gameObject.AddComponent<KeyHintsSkin>().Init(hints);
        }

        private void Init(KeyHints hints)
        {
            Instance = this;
            var root = hints.transform;
            PlayHud.SyncPpu(_art, root);
            Restyle(root);
            Plugin.Log.LogInfo("[Morgott.UI] key hints attached");
            PlayHud.Reports.Add(() => Instance && root ? _art.Report(root) : null);
        }

        // Also used by BuildHud for the build menu's own keyboard hints.
        internal void Restyle(Transform root)
        {
            foreach (var img in root.GetComponentsInChildren<Image>(true))
            {
                if (img.name == "key_bkg")
                {
                    if (_art.Reskin(img, "sprite.hud.plate.key", "color.frame", "key plate")) img.type = Image.Type.Sliced;
                }
                else _art.Content(img, img.sprite ? $"key hint glyph art '{img.sprite.name}' (controller / mouse)" : "key hint layout image");
            }
            var keySize = MessageSkin.HudReadable(ThemeTokens.GetFloat("size.keyhint.key", 17f));
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
            {
                var key = t.transform.parent && t.transform.parent.name == "key_bkg";
                PlayHud.Text(_art, t, "font.body", key ? "key" : "hint", key ? "color.text.active" : "color.text.body", key ? "keyhint.key" : "keyhint.label",
                    key ? "key letter on its plate" : "key hint label (outlined body sans)");
                if (key && t.enableAutoSizing) t.fontSizeMax = Mathf.Max(t.fontSizeMax, keySize);
            }
            foreach (var s in root.GetComponentsInChildren<TMP_SubMeshUI>(true)) _art.Content(s, "TMP sub-mesh");
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }

    // ---- Harmony -------------------------------------------------------------------------------------------------

    [HarmonyPatch]
    public static class PlayHudAdapter
    {
        [HarmonyPatch(typeof(Hud), nameof(Hud.Awake))]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Awake_Postfix(Hud __instance)
        {
            PlayHud.Reports.Clear();
            Try("crosshair", OwnershipRegistry.Crosshair, () => CrosshairSkin.Attach(__instance));
            Try("action HUD", OwnershipRegistry.ActionHud, () => ActionHudSkin.Attach(__instance));
            Try("ship HUD", OwnershipRegistry.ShipHud, () => ShipHudSkin.Attach(__instance));
            Try("key hints", OwnershipRegistry.KeyHints, () => KeyHintsSkin.Attach(__instance));
            Try("build HUD", OwnershipRegistry.BuildHud, () => BuildHudSkin.Attach(__instance));
        }

        private static void Try(string what, string family, Action attach)
        {
            if (!PlayHud.Owned(family)) return;
            try { attach(); }
            catch (Exception e) { Plugin.Log.LogError($"[Morgott.UI] {what} restyle failed (vanilla look stays): {e}"); }
        }

        [HarmonyPatch(typeof(Hud), nameof(Hud.UpdateCrosshair))]
        [HarmonyPostfix]
        private static void UpdateCrosshair_Postfix()
        {
            var c = CrosshairSkin.Instance;
            if (c) c.AfterCrosshair();
        }

        [HarmonyPatch(typeof(Hud), nameof(Hud.UpdateShipHud))]
        [HarmonyPostfix]
        private static void UpdateShipHud_Postfix(Player player)
        {
            var s = ShipHudSkin.Instance;
            if (s) s.AfterShipHud(player);
        }

        // Hud.UpdateBuild calls it every frame in place mode (Hud.cs:1496/1500): the strip hugs what it just filled.
        [HarmonyPatch(typeof(Hud), nameof(Hud.SetupPieceInfo))]
        [HarmonyPostfix]
        private static void SetupPieceInfo_Postfix(Hud __instance)
        {
            if (PlayHud.Owned(OwnershipRegistry.BuildHud)) BuildHudSkin.FitStrip(__instance);
        }

        // Piece / tag button templates are cloned from BuildUi.Awake on (BuildUi.cs:154 the first tag button): styled
        // before it runs, every clone copies the look.
        [HarmonyPatch(typeof(BuildUi), nameof(BuildUi.Awake))]
        [HarmonyPrefix]
        private static void BuildUiAwake_Prefix(BuildUi __instance)
        {
            if (!PlayHud.Owned(OwnershipRegistry.BuildHud)) return;
            try { BuildHudSkin.Templates(__instance); }
            catch (Exception e) { Plugin.Log.LogError($"[Morgott.UI] build menu templates restyle failed: {e}"); }
        }

        // BuildUi lays its tag list / piece grid out on open (OpenBuildMenu); restyle again so pooled clones made after
        // Awake that missed the template (mod tags) are covered.
        [HarmonyPatch(typeof(BuildUi), nameof(BuildUi.OpenBuildMenu))]
        [HarmonyPostfix]
        private static void BuildUiOpen_Postfix(BuildUi __instance)
        {
            if (!PlayHud.Owned(OwnershipRegistry.BuildHud)) return;
            try { BuildHudSkin.Refresh(__instance); }
            catch (Exception e) { Plugin.Log.LogError($"[Morgott.UI] build menu refresh failed: {e}"); }
        }
    }
}
