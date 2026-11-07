using System.Collections.Generic;
using HarmonyLib;
using Morgott.UI.Ownership;
using Morgott.UI.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;

namespace Morgott.UI.Screens
{
    // Damage-type icons in tooltips: the stats panel's own element icons (sprite.stats.icon.<type>, tinted
    // color.stats.elem.<type> like StatsReader.ElementColor) inline before each damage-type word (DamageIconText).
    // UITooltip.UpdateTextElements (UITooltip.cs:166-181) writes Text = Localize(m_text); the postfix rewrites it from
    // the decorated raw text and hands the "Text" element our runtime TMP_SpriteAsset. One sprite asset per atlas page
    // the icons live on, chained as fallbacks (TMP name lookup walks fallbacks, TMP_SpriteAsset.SearchForSpriteByHashCode).
    [HarmonyPatch]
    public static class DamageIcons
    {
        // Installed when RuneSkin owns either family; each patch then checks its own (independent config switches).
        private static bool _tips, _craft;
        private static bool Prepare()
        {
            _tips = OwnershipRegistry.IsOwned(OwnershipRegistry.Tooltips);
            _craft = OwnershipRegistry.IsOwned(OwnershipRegistry.Crafting);
            return _tips || _craft;
        }

        // Glyph placement in glyph px (TMP scales a sprite of an asset without pointSize to the font's ascent line,
        // TMP_Text.cs sprite branch): top sits on the ascent, ~10 % below the baseline, small gap after.
        private const float BearingShare = 0.9f;

        private static int _builtRevision = -1;
        private static TMP_SpriteAsset _asset;
        private static string[] _tags;
        private static readonly List<Object> Owned = new List<Object>(); // our assets + materials, released on rebuild

        [HarmonyPatch(typeof(UITooltip), "UpdateTextElements")]
        [HarmonyPostfix]
        private static void UpdateTextElements_Postfix(UITooltip __instance)
        {
            var tip = UITooltip.m_tooltip;
            if (!_tips || !tip || __instance == null) return;
            var t = Utils.FindChild(tip.transform, "Text");
            if (!t || !t.TryGetComponent<TMP_Text>(out var text)) return;
            _laidText = null;
            _refits = 0;
            Write(text, __instance.m_text, Available(text)); // layout runs even without icons (Ensure false -> no tags)
        }

        // Item tooltip layout (TooltipLayout) state: the live text, its raw source and the width it was composed for.
        // UpdateTextElements runs before the restyle and before Epic Loot's TooltipBox fits the width, so the placer's
        // pass (TooltipPlacer) recomposes from the raw text when the width changed; capped so a fit loop cannot spin.
        private static TMP_Text _laidText;
        private static string _laidRaw;
        private static float _laidWidth, _laidSize;
        private static TMP_FontAsset _laidFont;
        private static int _refits;
        private const int MaxRefits = 3;

        private static float Available(TMP_Text text) => text.rectTransform.rect.width - text.margin.x - text.margin.z;

        private static void Write(TMP_Text text, string raw, float width)
        {
            var icons = Ensure();
            var prior = text.spriteAsset;
            if (icons) text.spriteAsset = _asset; // before measuring: cells carry icons
            var tags = icons ? _tags : null;
            var gap = ThemeTokens.GetFloat("size.tooltip.column.gap", 10f);
            var desc = ColorUtility.ToHtmlStringRGBA(ThemeTokens.GetColor("color.tooltip.description", new Color(0.66f, 0.69f, 0.71f, 1f)));
            var composed = TooltipLayout.Compose(raw,
                cell => text.GetPreferredValues(Localization.instance.Localize(DamageIconText.Decorate(cell, tags))).x,
                width / 2f - gap, desc);
            var decorated = DamageIconText.Decorate(composed, tags);
            // No icon of ours in the text: the text keeps its own sprite asset (other mods' <sprite> tags).
            if (ReferenceEquals(decorated, composed)) text.spriteAsset = prior;
            if (ReferenceEquals(decorated, raw)) return; // vanilla text as UpdateTextElements wrote it
            text.text = Localization.instance.Localize(decorated);
            if (ReferenceEquals(composed, raw)) return; // icons only: width-independent
            _laidText = text;
            _laidRaw = raw;
            _laidWidth = width;
            _laidFont = text.font;
            _laidSize = text.fontSize;
        }

        // TooltipPlacer pass of the live tooltip: recompose when the text width settled to another value.
        internal static void Refit(Transform tip)
        {
            if (!_laidText || _refits >= MaxRefits || !_laidText.transform.IsChildOf(tip)) return;
            var width = Available(_laidText);
            // Measured before the restyle (font / size swap) or Epic Loot's width fit: either one re-lays the cells.
            if (Mathf.Abs(width - _laidWidth) < 1f && _laidText.font == _laidFont && Mathf.Abs(_laidText.fontSize - _laidSize) < 0.01f) return;
            _refits++;
            Write(_laidText, _laidRaw, width);
        }

        // Crafting panel item description: InventoryGui.UpdateRecipe (InventoryGui.cs:1517) writes
        // m_recipeDecription.text = Localize(ItemData.GetTooltip(item, quality, crafting: true, worldLevel, stack)) in one
        // expression, so the raw tokens exist only as GetTooltip's return value. While UpdateRecipe runs, the outermost
        // GetTooltip result (appending = false; Epic Loot's wrapped calls are harmless, Decorate is idempotent) gets the
        // icons, and the description text gets our sprite asset.
        private static bool _inRecipe;

        [HarmonyPatch(typeof(InventoryGui), "UpdateRecipe")]
        [HarmonyPrefix]
        private static void UpdateRecipe_Prefix() => _inRecipe = _craft && Ensure(); // the panel is family "Crafting"

        [HarmonyPatch(typeof(InventoryGui), "UpdateRecipe")]
        [HarmonyFinalizer]
        private static void UpdateRecipe_Finalizer(InventoryGui __instance)
        {
            if (_inRecipe && __instance.m_recipeDecription && __instance.m_recipeDecription.spriteAsset != _asset)
                __instance.m_recipeDecription.spriteAsset = _asset;
            _inRecipe = false;
        }

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip),
            new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool) })]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void GetTooltip_Postfix(ItemDrop.ItemData item, bool crafting, bool appending, ref string __result)
        {
            // Every skinned item tooltip states food healing per second, not vanilla's "hp/tick" (Player.UpdateFood, 10 s).
            // The m_appendToolTip food's line is printed inline by the local printConsumable (ItemDrop.cs:1057), no nested call.
            if (item?.m_shared != null)
            {
                var comma = Localization.instance?.GetSelectedLanguage() == "Russian";
                __result = InfoScreens.StatsMath.FoodTooltipPerSecond(__result, item.m_shared.m_foodRegen, comma, "$ui_stats_per_second");
                var appended = item.m_shared.m_appendToolTip ? item.m_shared.m_appendToolTip.m_itemData?.m_shared : null;
                if (appended != null)
                    __result = InfoScreens.StatsMath.FoodTooltipPerSecond(__result, appended.m_foodRegen, comma, "$ui_stats_per_second");
                // Trinkets: what fills adrenaline, the bar size, the full-bar effect per second and the decay.
                if (!appending) __result = AdrenalineTooltip.Append(item, __result);
            }
            if (_inRecipe && crafting && !appending) __result = DamageIconText.Decorate(__result, _tags);
        }

        // Built once per theme revision (sprites / colours are theme tokens). False = no icon resolved (vanilla text).
        private static bool Ensure()
        {
            if (_builtRevision == ThemeTokens.Revision) return _asset;
            _builtRevision = ThemeTokens.Revision;
            // Superseded by a theme reload: only the one live tooltip can hold them, and the caller re-points it next.
            foreach (var o in Owned) if (o) Object.Destroy(o);
            Owned.Clear();
            _asset = null;
            _tags = new string[DamageIconText.Types.Length];
            var pages = new Dictionary<Texture2D, TMP_SpriteAsset>();
            TMP_SpriteAsset first = null;
            var shader = Shader.Find("TextMeshPro/Sprite");
            if (!shader)
            {
                Diagnostics.SkinDiagnostics.Theme("damage icons: shader TextMeshPro/Sprite not found (tooltips keep plain text)");
                return false;
            }
            for (var i = 0; i < DamageIconText.Types.Length; i++)
            {
                var type = DamageIconText.Types[i];
                var sprite = ThemeTokens.GetSprite("sprite.stats.icon." + type);
                if (!sprite || !sprite.texture) continue;
                // A tight-packed / rotated atlas sprite has no rectangle on its page (Sprite.textureRect throws).
                if (sprite.packed && (sprite.packingMode == SpritePackingMode.Tight || sprite.packingRotation != SpritePackingRotation.None)) continue;
                var r = sprite.textureRect;
                if (!pages.TryGetValue(sprite.texture, out var asset))
                {
                    asset = NewAsset(sprite.texture, shader, pages.Count);
                    pages.Add(sprite.texture, asset);
                    if (!first) first = asset;
                    else first.fallbackSpriteAssets.Add(asset);
                }
                var index = (uint)asset.spriteGlyphTable.Count;
                var glyph = new TMP_SpriteGlyph(index,
                    new GlyphMetrics(r.width, r.height, 0f, r.height * BearingShare, r.width),
                    new GlyphRect((int)r.x, (int)r.y, (int)r.width, (int)r.height), 1f, 0, sprite);
                asset.spriteGlyphTable.Add(glyph);
                asset.spriteCharacterTable.Add(new TMP_SpriteCharacter(0xFFFE, asset, glyph) { name = type, scale = 1f });
                _tags[i] = DamageIconText.Tag(type, ColorUtility.ToHtmlStringRGBA(ThemeTokens.GetColor("color.stats.elem." + type, Color.white)));
            }
            foreach (var a in pages.Values) a.UpdateLookupTables();
            _asset = first;
            if (!_asset) Diagnostics.SkinDiagnostics.Theme("damage icons: no sprite.stats.icon.* sprite usable (tooltips keep plain text)");
            return _asset;
        }

        private static TMP_SpriteAsset NewAsset(Texture2D page, Shader shader, int n)
        {
            var asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            // Unique per theme revision: TMP caches sub-mesh materials by material-name hash (TMP_Asset.materialHashCode).
            asset.name = "RuneSkinDamageIcons" + ThemeTokens.Revision + "_" + n;
            asset.hideFlags = HideFlags.DontUnloadUnusedAsset;
            // A non-empty version skips UpgradeSpriteAsset, which would clear our tables from the empty spriteInfoList
            // (TMP_SpriteAsset.UpdateLookupTables / UpgradeSpriteAsset).
            AccessTools.Field(typeof(TMP_Asset), "m_Version").SetValue(asset, "1.1.0");
            asset.spriteSheet = page;
            var material = new Material(shader) { name = asset.name };
            material.SetTexture("_MainTex", page);
            asset.material = material;
            Owned.Add(material);
            Owned.Add(asset);
            asset.fallbackSpriteAssets = new List<TMP_SpriteAsset>();
            return asset;
        }
    }
}
