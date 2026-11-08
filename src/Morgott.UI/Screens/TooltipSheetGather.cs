using System.Collections.Generic;
using HarmonyLib;
using Morgott.UI.Compat;
using Morgott.UI.Ownership;
using Morgott.UI.Theme;
using UnityEngine;

namespace Morgott.UI.Screens
{
    // Structured data of one item tooltip sheet (TooltipSheet): ItemData (name, type, quality, durability, weight, value,
    // icon) + Epic Loot's ItemTooltipData (rarity, magic effects, sockets, sets). Called from the outermost non-crafting
    // GetTooltip postfix (DamageIcons), after Epic Loot's own postfix, so the raw text is the one Epic Loot finished.
    [HarmonyPatch]
    public static class TooltipSheetGather
    {
        private static bool Prepare() => OwnershipRegistry.IsOwned(OwnershipRegistry.Tooltips);

        // Key hints only for the inventory grids (their RMB / Ctrl actions): InventoryGrid.CreateItemTooltip
        // (InventoryGrid.cs:409) is the only caller there; Epic Loot's prefix (TooltipPatches, runs GetTooltip itself)
        // comes after ours.
        private static bool _inGrid;

        [HarmonyPatch(typeof(InventoryGrid), "CreateItemTooltip")]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void CreateItemTooltip_Prefix() => _inGrid = true;

        [HarmonyPatch(typeof(InventoryGrid), "CreateItemTooltip")]
        [HarmonyFinalizer]
        private static void CreateItemTooltip_Finalizer() => _inGrid = false;

        // Item icons for TooltipDecor, addressed by the id in the sheet's icon link (a tooltip shows at most two sheets:
        // the item and Epic Loot's comparison).
        private static readonly Sprite[] Icons = new Sprite[16];
        private static int _nextIcon;

        internal static Sprite Icon(int id) => id >= 0 && id < Icons.Length ? Icons[id] : null;

        public static TooltipSheet.Style Style()
        {
            string C(string token, string fallback) => ColorUtility.ToHtmlStringRGBA(ThemeTokens.GetColor(token, Hex(fallback)));
            int P(string token, int fallback) => Mathf.RoundToInt(ThemeTokens.GetFloat(token, fallback));
            return new TooltipSheet.Style
            {
                Accent = C("color.tooltip.section", "4FE0E6FF"),
                Value = C("color.tooltip.value", "4FE0E6FF"),
                Dim = C("color.tooltip.inactive", "A3ADB3FF"),
                Description = C("color.tooltip.description", "A8AFB4FF"),
                Body = C("color.text.body", "E3E6E8FF"),
                PipOn = C("color.tooltip.pip.on", "4FE0E6FF"),
                PipOff = C("color.tooltip.pip.off", "4FE0E633"),
                PipDigit = C("color.tooltip.pip.digit", "06090BFF"),
                PipDigitOff = C("color.tooltip.pip.digit.off", "A3ADB3FF"),
                Key = C("color.tooltip.key", "FFFFFF26"),
                NameSize = P("size.tooltip.name", 125),
                LabelSize = P("size.tooltip.label", 82),
                SmallSize = P("size.tooltip.small", 90),
                RuleSize = P("size.tooltip.rule.line", 45),
                IconIndent = ThemeTokens.GetFloat("size.tooltip.icon", 60f) + ThemeTokens.GetFloat("size.tooltip.icon.gap", 12f),
            };
        }

        private static Color Hex(string rgba) => ColorUtility.TryParseHtmlString("#" + rgba, out var c) ? c : Color.white;

        // The sheet text, or the raw text when it cannot be one (Epic Loot set runes keep their own layout).
        public static string Build(ItemDrop.ItemData item, int quality, string raw)
        {
            if (item?.m_shared == null || TooltipSheet.IsSheet(raw)) return raw;
            var el = EpicLootTooltipData.Get(item, quality);
            if (el != null && el.Kind == "SetRune") return raw;
            var s = item.m_shared;
            var d = new TooltipSheet.SheetData
            {
                Name = s.m_name,
                Description = s.m_description,
                Quality = quality,
                MaxQuality = s.m_maxQuality,
                Subtitle = Subtitle(item),
            };

            var icon = item.GetIcon();
            if (icon)
            {
                d.IconId = _nextIcon;
                Icons[_nextIcon] = icon;
                _nextIcon = (_nextIcon + 1) % Icons.Length; // ring: ids of live sheets stay valid
            }
            if (s.m_useDurability)
            {
                d.Durability = item.m_durability;
                d.MaxDurability = item.GetMaxDurability(quality);
            }
            d.Weight = item.GetWeight().ToString("0.0");
            if (s.m_value > 0) d.Value = item.GetValue().ToString();

            if (el != null) Apply(el, d, ref raw);
            if (_inGrid) Hints(item, d, el != null);
            return TooltipSheet.Compose(raw, d, Style());
        }

        private static void Apply(EpicLootTooltipData.Data el, TooltipSheet.SheetData d, ref string raw)
        {
            if (el.Stats != null && el.Stats.Exists(st => st.Id == "$item_durability" && st.Variant == "Indestructible"))
            {
                d.Durability = -1f;
                d.Indestructible = true;
            }
            // Epic Loot's Finish prepends "<color=X>Rarity $mod_epicloot_craftingmaterial\n</color>" to materials.
            const string material = "$mod_epicloot_craftingmaterial\n</color>";
            var m = el.MaterialRarity >= 0 && raw.StartsWith("<color=") ? raw.IndexOf(material, System.StringComparison.Ordinal) : -1;
            if (m >= 0)
            {
                d.Subtitle = raw.Substring(0, m + material.Length).Replace("\n", "");
                raw = raw.Substring(m + material.Length);
            }
            if (el.IsMagic)
            {
                var rarity = EpicLootTooltipData.Hex(el.RarityColor);
                d.Name = string.IsNullOrEmpty(el.DisplayName) ? d.Name : el.DisplayName;
                d.NameColor = rarity;
                d.MagicColor = rarity;
                d.Subtitle = string.IsNullOrEmpty(el.ItemTypeName) ? el.RarityName : el.RarityName + " · " + el.ItemTypeName;
                if (!string.IsNullOrEmpty(el.Description)) d.Description = el.Description;
                if (!string.IsNullOrEmpty(el.SetLabel)) d.DropLines.Add(el.SetLabel);
                if (el.Kind != "Shard") d.ElEffectsColor = el.RarityColor;
                foreach (var e in el.Effects ?? new List<EpicLootTooltipData.Effect>()) d.Effects.Add(new TooltipSheet.Effect { Text = e.Text });
                if (el.Sockets != null && el.Sockets.Count > 0) d.Sockets = el.Sockets.Used + "/" + el.Sockets.Count;
            }
            if (el.Sets != null && el.Sets.Count > 0)
            {
                d.ElSetColor = el.SetColor;
                foreach (var set in el.Sets)
                {
                    var t = new TooltipSheet.Set { Name = set.Name, Equipped = set.Equipped, Full = set.Full };
                    foreach (var p in set.Pieces ?? new List<EpicLootTooltipData.Piece>()) t.Pieces.Add(new TooltipSheet.Piece { Name = p.Name, On = p.Equipped });
                    foreach (var b in set.Bonuses ?? new List<EpicLootTooltipData.Bonus>()) t.Bonuses.Add(new TooltipSheet.Bonus { Count = b.Count, Text = b.Text, Active = b.Active });
                    d.Sets.Add(t);
                }
            }
        }

        // "$item_twohanded · $skill_axes", "$ui_tip_type_helmet": vanilla words for handedness and skill.
        private static string Subtitle(ItemDrop.ItemData item)
        {
            var s = item.m_shared;
            var type = "$ui_tip_type_" + s.m_itemType.ToString().ToLowerInvariant();
            if (s.m_skillType == Skills.SkillType.None || !(item.IsWeapon() || s.m_itemType == ItemDrop.ItemData.ItemType.Ammo)) return type;
            return type + " · $skill_" + s.m_skillType.ToString().ToLowerInvariant();
        }

        // RMB = InventoryGui.OnRightClickItem -> UseItem (equip / unequip / consume, InventoryGui.cs); Ctrl = Epic Loot's
        // comparison while LeftControl is held (TooltipPatches, KeyCode 306, its HasEquipmentOfType = an equipped item of the
        // same type). Mouse buttons / fixed modifier: no binding to read, labels Latin (memory key-hints-english).
        private static void Hints(ItemDrop.ItemData item, TooltipSheet.SheetData d, bool epicLoot)
        {
            var player = Player.m_localPlayer;
            if (item.IsEquipable()) d.Hints.Add("[RMB] " + (item.m_equipped ? "$ui_tip_unequip" : "$ui_tip_equip"));
            else if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable) d.Hints.Add("[RMB] $ui_tip_use");
            if (epicLoot && item.IsEquipable() && !item.m_equipped && player &&
                player.GetInventory().GetEquippedItems().Exists(i => i.m_shared.m_itemType == item.m_shared.m_itemType))
                d.Hints.Add("[Ctrl] $ui_tip_compare");
        }
    }
}
