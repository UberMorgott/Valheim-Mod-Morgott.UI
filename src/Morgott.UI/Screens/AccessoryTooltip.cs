namespace Morgott.UI.Screens
{
    // Rings, belts, amulets, charms (ItemType Utility / Trinket): what the game does not print, and the status effect
    // blocks the sheet turns into sections. GetTooltip printed those blocks already (ItemDrop.cs:1121-1124, :1053-1055).
    public static class AccessoryTooltip
    {
        private static bool Is(ItemDrop.ItemData item) =>
            item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Utility ||
            item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trinket;

        // Raw lines, so the crafting panel (no sheet) shows them too.
        public static string Append(ItemDrop.ItemData item, string tooltip)
        {
            if (!Is(item)) return tooltip;
            var s = item.m_shared;
            var damage = SkillDamage(s.m_equipStatusEffect);
            if (damage != null) tooltip += "\n" + damage;
            // Humanoid.cs:423-429, Character.cs:1289 (shoes: no slipping), :1626-1629 (skates: skating glide).
            if (s.m_iceShoes) tooltip += "\n$ui_tip_ice: <color=orange>$ui_tip_ice_shoes</color>";
            if (s.m_iceSkates) tooltip += "\n$ui_tip_ice: <color=orange>$ui_tip_ice_skates</color>";
            return tooltip;
        }

        public static void Gather(ItemDrop.ItemData item, int quality, TooltipSheet.SheetData d)
        {
            if (!Is(item)) return;
            var s = item.m_shared;
            if (s.m_subtitle == "$item_utility" || s.m_subtitle == "$item_trinket") d.DropExact.Add(s.m_subtitle); // the header states the type
            var equip = s.m_equipStatusEffect;
            if (equip)
            {
                equip.SetLevel(quality, Player.m_localPlayer.GetSkillLevel(s.m_skillType)); // as GetStatusEffectTooltip
                var text = equip.GetTooltipString();
                d.Blocks.Add(new TooltipSheet.EffectBlock
                {
                    Label = TooltipSheet.EffectLabel,
                    Name = NameOf(equip, s),
                    Printed = equip.m_name + "\n" + text,
                    Text = text,
                });
            }
            var full = s.m_fullAdrenalineSE;
            if (full)
            {
                var text = full.GetTooltipString();
                d.Blocks.Add(new TooltipSheet.EffectBlock
                {
                    Label = TooltipSheet.FullAdrenalineLabel,
                    Name = NameOf(full, s),
                    Printed = "$item_fulladrenaline: " + text,
                    Text = text + "\n" + SkillDamage(full),
                });
            }
        }

        // Different tokens can read the same (Andvaranaut, Wishbone, every trinket): the header already says it.
        private static string NameOf(StatusEffect effect, ItemDrop.ItemData.SharedData s) =>
            Localization.instance.Localize(effect.m_name) == Localization.instance.Localize(s.m_name) ? null : effect.m_name;

        private static string SkillDamage(StatusEffect effect)
        {
            if (!(effect is SE_Stats se) || se.m_damageModifier == 1f || se.m_modifyAttackSkill == Skills.SkillType.None) return null;
            var skill = se.m_modifyAttackSkill == Skills.SkillType.All
                ? "$ui_tip_all_skills"
                : "$skill_" + TooltipSheetGather.SkillSlug(se.m_modifyAttackSkill);
            return TooltipSheet.SkillDamageLine(skill, se.m_damageModifier);
        }
    }
}
