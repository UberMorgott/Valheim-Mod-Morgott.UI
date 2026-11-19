using System.Collections.Generic;
using Morgott.Core;
using UnityEngine;

namespace Morgott.UI.PlayerHud
{
    // Ashen Crown (Morgott.Power boss gem): infusions stolen onto the local player (Core IInfusionStealLong: every infusion, Reflect too; provided by
    // Morgott.Creatures) shown as a buff tile with the time left. Display-only effect: never added to the SEMan, so it has
    // no gameplay side; the tile icon is the Ashen Crown T1 gem's (absent item = no tile).
    internal static class StolenInfusionStatus
    {
        private const string IconItem = "1071_Mythic_ShardStone"; // GemPool.Id(AshenCrown = 7, tier 1)
        private static StatusEffect _se;

        internal static void Append(List<StatusEffect> list)
        {
            var player = Player.m_localPlayer;
            if (!player || !Services.TryGet<IInfusionStealLong>(out var steal) || steal.Stolen(player, out var left) == 0) return;
            if (!_se)
            {
                var icon = ObjectDB.instance ? ObjectDB.instance.GetItemPrefab(IconItem)?.GetComponent<ItemDrop>()?.m_itemData.GetIcon() : null;
                if (!icon) return;
                _se = ScriptableObject.CreateInstance<StatusEffect>();
                _se.name = "MT_StolenInfusion";
                _se.m_name = "$ui_stolen_infusion";
                _se.m_icon = icon;
            }
            _se.m_ttl = left; // remaining = m_ttl - m_time (StatusSource.Remaining), m_time stays 0
            list.Add(_se);
        }
    }
}
