using System;
using HarmonyLib;
using Morgott.UI.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Morgott.UI.WorldHud
{
    // Server announcement (routed RPC Contracts.ServerAnnounceRpc, Morgott.Patcher [ServerCommands] `say`), user bug
    // 2026-11-22: a restart notice sent as vanilla "ShowMessage" Center was missed in the F6 build camera. That centre line is
    // one shared 4 s slot (MessageHud.ShowMessage, MessageHud.cs:179-192) every gameplay Center message replaces (build
    // refusals Player.cs:3047-3084, Build Camera's own DirectResourceReturn), dropped unlogged while the HUD is hidden
    // (MessageHud.cs:159) and drawn under the inventory / map / menus. Here: an own root overlay canvas above every game
    // canvas, top centre, held Hold s regardless of HUD hiding, the centre message look (rune face + soft dark plate); the
    // text also goes into the vanilla message log (MessageHud.AddLog, as ShowMessage does).
    public sealed class ServerAnnounce : MonoBehaviour
    {
        private const float Hold = 12f, Fade = 2f;
        private const int SortingOrder = short.MaxValue; // Unity's max order: above every canvas, incl. ValheimBuildCamera's F6 panel (32000)
        private static ServerAnnounce _instance;

        private MessageHud _hud;
        private Canvas _canvas;
        private CanvasGroup _group;
        private TextMeshProUGUI _text;
        private Image _plate;
        private float _shownAt = float.NegativeInfinity;

        internal static void Register(MessageHud hud)
        {
            // Same place and lifetime as vanilla's "ShowMessage" (MessageHud.Start, MessageHud.cs:111).
            ZRoutedRpc.instance.Register<string>(Morgott.Core.Contracts.ServerAnnounceRpc, (sender, text) => Receive(hud, sender, text));
        }

        private static void Receive(MessageHud hud, long sender, string text)
        {
            // Only the server announces (a client could route the RPC to everybody); GetServerPeerID = ZRoutedRpc's own notion.
            if (sender != ZRoutedRpc.instance.GetServerPeerID() || string.IsNullOrEmpty(text) || !hud) return;
            text = Localization.instance.Localize(text);
            hud.AddLog(text);
            if (!_instance) _instance = new GameObject("RuneSkin_ServerAnnounce").AddComponent<ServerAnnounce>().Init(hud);
            _instance.Show(text);
        }

        private ServerAnnounce Init(MessageHud hud)
        {
            _hud = hud;
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = SortingOrder;
            _group = gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;

            var root = (RectTransform)transform;
            _plate = new GameObject("Plate", typeof(RectTransform)).AddComponent<Image>();
            _plate.transform.SetParent(root, false);
            var refCanvas = hud.GetComponentInParent<Canvas>();
            _plate.sprite = MessageSkin.SoftPlate(refCanvas ? refCanvas.rootCanvas.referencePixelsPerUnit : 100f);
            _plate.type = Image.Type.Sliced;
            _plate.pixelsPerUnitMultiplier = MessageSkin.SoftRamp / Mathf.Max(1f, ThemeTokens.GetFloat("size.message.center.fade", 16f));
            _plate.color = ThemeTokens.GetColor("color.message.band", new Color(0.043f, 0.078f, 0.098f, 0.9f));
            _plate.raycastTarget = false;

            var center = hud.m_messageCenterText;
            _text = UiText.Add(new GameObject("Text", typeof(RectTransform)), center ? center.font : null);
            _text.transform.SetParent(root, false);
            if (!TextLook.ApplyShadowed(_text, "font.display", "message.rune") && center) _text.font = center.font;
            _text.color = ThemeTokens.GetColor("color.message.center", new Color(1f, 0.93f, 0.8f, 1f));
            _text.fontSize = ThemeTokens.GetFloat("size.message.center.font", 44f);
            _text.alignment = TextAlignmentOptions.Center;
            _text.textWrappingMode = TextWrappingModes.Normal;
            _text.raycastTarget = false;
            foreach (var rt in new[] { _plate.rectTransform, _text.rectTransform })
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
            return this;
        }

        private void Show(string text)
        {
            _text.text = text;
            _shownAt = Time.unscaledTime;
            Layout();
            LateUpdate();
        }

        // Screen-relative layout in the HUD's own scale (its root canvas scaleFactor), top centre, wrapped at 70 % width.
        private void Layout()
        {
            var refCanvas = _hud ? _hud.GetComponentInParent<Canvas>() : null;
            var scale = refCanvas ? refCanvas.rootCanvas.scaleFactor : 1f;
            _canvas.scaleFactor = scale;
            var width = Screen.width / Mathf.Max(0.01f, scale) * 0.7f;
            var size = _text.GetPreferredValues(_text.text, width, 0f);
            var w = Mathf.Min(width, size.x);
            var top = ThemeTokens.GetFloat("size.message.announce.top", 110f);
            _text.rectTransform.sizeDelta = new Vector2(w, size.y);
            _text.rectTransform.anchoredPosition = new Vector2(0f, -top);
            var pad = ThemeTokens.GetFloat("size.message.center.pad", 54f);
            var vpad = ThemeTokens.GetFloat("size.message.center.vpad", 12f) + ThemeTokens.GetFloat("size.message.center.fade", 16f);
            _plate.rectTransform.sizeDelta = new Vector2(w + 2f * pad, size.y + 2f * vpad);
            _plate.rectTransform.anchoredPosition = new Vector2(0f, -top + vpad);
        }

        private void LateUpdate()
        {
            var a = PlayHudMath.AnnounceAlpha(Time.unscaledTime - _shownAt, Hold, Fade);
            if (!Mathf.Approximately(_group.alpha, a)) _group.alpha = a;
            var on = a > 0f;
            if (_canvas.enabled != on) _canvas.enabled = on;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        // MessageHud dies with the game scene: the announce layer goes with it.
        internal static void Drop()
        {
            if (_instance) Destroy(_instance.gameObject);
        }
    }

    [HarmonyPatch]
    public static class ServerAnnounceAdapter
    {
        // Not gated on MessageHud ownership: the announcement layer is Morgott.UI's own, the vanilla centre line is untouched.
        [HarmonyPatch(typeof(MessageHud), "Start")]
        [HarmonyPostfix]
        private static void Start_Postfix(MessageHud __instance)
        {
            try { ServerAnnounce.Register(__instance); }
            catch (Exception e) { Plugin.Log.LogError($"[Morgott.UI] server announce RPC registration failed: {e}"); }
        }

        [HarmonyPatch(typeof(MessageHud), "OnDestroy")]
        [HarmonyPostfix]
        private static void OnDestroy_Postfix() => ServerAnnounce.Drop();
    }
}
