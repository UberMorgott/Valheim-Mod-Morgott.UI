using Morgott.UI.InventoryScreen;
using Morgott.UI.Restyle;
using Morgott.UI.Theme;
using TMPro;
using UnityEngine;

namespace Morgott.UI.InfoScreens
{
    // Achievements (InventoryGui.m_achievementsPanel: a full-screen root - blur, darken, the AchFrame window, the AchDetails
    // popup) as a full-screen info page like skills / trophies (InfoPage: AchFrame fills the screen, tab row, Esc). The
    // header is the one RuneWindowHeader as the page title: title = the frame's "topic", the completion count
    // ("completionRate") in its aux slot; the list container fills the body above the close row (close button centred,
    // the cheated note right of it). Without the inventory layout (no InventorySkin) the vanilla placement stays.
    public sealed class AchievementsWindow : MonoBehaviour
    {
        private RectTransform _frame, _canvas;
        private InfoPage _page;

        public static void Ensure(InventoryGui gui)
        {
            if (!gui || !gui.m_achievementsPanel) return;
            var root = gui.m_achievementsPanel.transform;
            if (root.GetComponent<AchievementsWindow>()) return;
            if (!(root.Find("AchFrame") is RectTransform frame))
            {
                Plugin.Log.LogWarning("[Morgott.UI] achievements: no AchFrame under the panel (vanilla placement stays)");
                return;
            }
            var canvas = gui.GetComponentInParent<Canvas>();
            var w = root.gameObject.AddComponent<AchievementsWindow>();
            w._frame = frame;
            w._canvas = canvas ? (RectTransform)canvas.rootCanvas.transform : null;
            w._page = InfoPage.Ensure((RectTransform)root, frame, "OnOpenAchievements", gui.OnCloseAchievements);
        }

        private void LateUpdate() => Layout();

        private void Layout()
        {
            var inv = InventorySkin.Instance;
            if (!inv || !_canvas || !_frame) return;
            var k = inv.SkinScaleValue;
            var gap = 8f * k;
            var header = RuneWindowHeader.Ensure(_frame, WindowMove.Achievements);
            TMP_Text count = null;
            RectTransform container = null, close = null, cheated = null;
            foreach (var c in Children.Of(_frame))
            {
                if (!(c is RectTransform rt)) continue;
                switch (c.name)
                {
                    case "topic" when c.TryGetComponent<TMP_Text>(out var title):
                        if (header) header.Adopt(title, "$ui_win_achievements");
                        break;
                    case "completionRate" when c.TryGetComponent<TMP_Text>(out var rate):
                        count = rate;
                        break;
                    case "AchContainer": container = rt; break;
                    case "Closebutton": close = rt; break;
                    case "cheatedText": cheated = rt; break;
                }
            }
            var countW = count ? ThemeTokens.GetFloat("size.info.count.w", 150f) * k : 0f;
            var inner = _page.Layout(k, header, countW);
            if (inner.W <= 0f || inner.H <= 0f) return;
            if (header && count)
            {
                Geo.Place(_canvas, count.rectTransform, header.AuxBox);
                InfoArt.Label(count, TextAlignmentOptions.MidlineRight, header.AuxBox.H * 0.5f, 13f);
            }
            var rowH = close && close.gameObject.activeSelf ? 46f * k : 0f;
            var bodyH = Mathf.Max(0f, inner.H - (rowH > 0f ? rowH + gap : 0f));
            if (container) Geo.Place(_canvas, container, new Box(inner.X, inner.Y, inner.W, bodyH));
            if (rowH <= 0f) return;
            var y = inner.Y + bodyH + gap;
            var closeW = Mathf.Min(170f * k, inner.W);
            Geo.Place(_canvas, close, new Box(inner.X + (inner.W - closeW) / 2f, y, closeW, rowH));
            if (cheated && cheated.gameObject.activeSelf)
            {
                var x = inner.X + (inner.W + closeW) / 2f + gap;
                Geo.Place(_canvas, cheated, new Box(x, y, Mathf.Max(0f, inner.Right - x), rowH));
            }
        }
    }
}
