using Morgott.Core;
using Morgott.UI.Restyle;
using Morgott.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace Morgott.UI.InventoryScreen
{
    // Store / Release buttons beside the pet collar doll cell (user 2026-11-11, Morgott.Summons "pokeball", Core IPetCollar):
    // two small action buttons stacked on the cell's inner side, children of the EAQS element so they move, scale and hide
    // with it. Greyed and not clickable when the owner says no; the tooltip then names why. Absent without Summons.
    public sealed class PetCollarButtons : MonoBehaviour
    {
        private Button _store, _release;
        private Image _storeGlyph, _releaseGlyph;
        private UITooltip _storeTip, _releaseTip;
        private float _next;

        // From PlaceEaqsCells for the collar cell; side 1 = right doll column (buttons on its left).
        public static void Sync(InventoryElement e, int side, GameObject tooltipPrefab)
        {
            var have = Services.TryGet<IPetCollar>(out _);
            var bar = e.GetComponent<PetCollarButtons>();
            if (!have)
            {
                if (bar) bar.Show(false);
                return;
            }
            if (!bar)
            {
                bar = e.gameObject.AddComponent<PetCollarButtons>();
                bar.Build((RectTransform)e.transform, side, tooltipPrefab);
            }
            bar.Show(true);
        }

        private void Show(bool on)
        {
            if (_store && _store.gameObject.activeSelf != on) _store.gameObject.SetActive(on);
            if (_release && _release.gameObject.activeSelf != on) _release.gameObject.SetActive(on);
        }

        private void Build(RectTransform cell, int side, GameObject tooltipPrefab)
        {
            _store = Make(cell, "RuneSkinPetStore", "sprite.action.take_all", tooltipPrefab, 0, side, out _storeGlyph, out _storeTip);
            _release = Make(cell, "RuneSkinPetRelease", "sprite.glyph.act.upgrade", tooltipPrefab, 1, side, out _releaseGlyph, out _releaseTip);
            _store.onClick.AddListener(() => { if (Services.TryGet<IPetCollar>(out var p)) p.Store(); _next = 0f; });
            _release.onClick.AddListener(() => { if (Services.TryGet<IPetCollar>(out var p)) p.Release(); _next = 0f; });
        }

        // Square, 44 % of the cell, outside its inner edge: Store on top, Release below.
        private static Button Make(RectTransform cell, string name, string glyphToken, GameObject tooltipPrefab, int index, int side,
            out Image glyph, out UITooltip tip)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(cell, false);
            const float share = 0.44f, gap = 0.04f;
            var x = side == 1 ? -share - gap : 1f + gap;
            var top = 0.5f + (index == 0 ? share + gap / 2f : -gap / 2f);
            rt.anchorMin = new Vector2(x, top - share);
            rt.anchorMax = new Vector2(x + share, top);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var bg = go.AddComponent<Image>();
            var button = go.AddComponent<Button>(); // first: the role's sprite states bind to a Selectable targeting this image
            button.targetGraphic = bg;
            var mark = DollCell.Mark(bg, ActionBar.DefaultRole, "pet collar store / release button");
            mark.Outcome = Restyler.Apply(bg, mark, ThemeTokens.Role(ActionBar.DefaultRole)) ? Outcome.Styled : Outcome.Degraded;
            glyph = new GameObject("RuneSkinGlyph", typeof(RectTransform)).AddComponent<Image>();
            var g = glyph.rectTransform;
            g.SetParent(rt, false);
            g.anchorMin = new Vector2(0.18f, 0.18f);
            g.anchorMax = new Vector2(0.82f, 0.82f);
            g.offsetMin = g.offsetMax = Vector2.zero;
            glyph.raycastTarget = false;
            glyph.preserveAspect = true;
            glyph.sprite = ThemeTokens.GetSprite(glyphToken);
            glyph.enabled = glyph.sprite;
            DollCell.Mark(glyph, "content", "pet collar button glyph").Outcome = Outcome.Styled;
            tip = null;
            if (tooltipPrefab)
            {
                tip = go.AddComponent<UITooltip>();
                tip.m_tooltipPrefab = tooltipPrefab;
            }
            return button;
        }

        // 4 Hz: CanRelease looks for ground (capsule checks), cheap enough at this rate.
        private void Update()
        {
            if (Time.unscaledTime < _next || !_store || !_store.gameObject.activeSelf) return;
            _next = Time.unscaledTime + 0.25f;
            if (!Services.TryGet<IPetCollar>(out var pet)) return;
            Apply(_store, _storeGlyph, _storeTip, pet.CanStore(out var whyStore), "$su_pet_store_button", whyStore);
            Apply(_release, _releaseGlyph, _releaseTip, pet.CanRelease(out var whyRelease), "$su_pet_release_button", whyRelease);
        }

        private static void Apply(Button b, Image glyph, UITooltip tip, bool can, string topic, string why)
        {
            if (b.interactable != can) b.interactable = can;
            var c = glyph.color;
            var a = can ? 1f : 0.35f;
            if (!Mathf.Approximately(c.a, a)) glyph.color = new Color(c.r, c.g, c.b, a);
            if (!tip) return;
            var t = Localization.instance.Localize(topic);
            var text = can || string.IsNullOrEmpty(why) ? "" : Localization.instance.Localize(why);
            if (tip.m_topic != t) tip.m_topic = t;
            if (tip.m_text != text) tip.m_text = text;
        }
    }
}
