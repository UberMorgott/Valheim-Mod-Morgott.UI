using System.Collections.Generic;
using System.Globalization;
using Morgott.Core;
using UnityEngine;

namespace Morgott.UI.PlayerHud
{
    // Ashen Crown (Morgott.Power boss gem): infusions stolen onto the local player (Core IInfusionStealLong: every
    // infusion, Reflect too; provided by Morgott.Creatures) shown individually with their localized name and icon
    // (Core IInfusionDisplay), falling back to a single Ashen Crown tile when display info is unavailable.
    // Display-only effects: never added to SEMan, so they have no gameplay side.
    internal static class StolenInfusionStatus
    {
        private const string IconItem = "1071_Mythic_ShardStone"; // GemPool.Id(AshenCrown = 7, tier 1)
        private static readonly Dictionary<int, StatusEffect> PerId = new Dictionary<int, StatusEffect>();
        private static readonly List<int> ActiveIds = new List<int>(8);
        private static StatusEffect _fallback;

        internal static void Append(List<StatusEffect> list)
        {
            var player = Player.m_localPlayer;
            if (!player || !Services.TryGet<IInfusionStealLong>(out var steal)) return;
            var mask = (ulong)steal.Stolen(player, out var left);
            if (mask == 0 || left <= 0f) return;

            if (Services.TryGet<IInfusionDisplay>(out var display))
            {
                var added = 0;
                ExtractIds(mask, ActiveIds);
                for (var i = 0; i < ActiveIds.Count; i++)
                {
                    var id = ActiveIds[i];
                    if (!display.TryGet(id, out var nameKey, out var icon) || string.IsNullOrEmpty(nameKey)) continue;
                    var sprite = icon ? icon : FallbackIcon();
                    if (!PerId.TryGetValue(id, out var se) || !se)
                    {
                        se = ScriptableObject.CreateInstance<StatusEffect>();
                        se.name = "MT_StolenInfusion_" + id.ToString(CultureInfo.InvariantCulture);
                        se.m_name = nameKey;
                        se.m_icon = sprite;
                        PerId[id] = se;
                    }
                    else
                    {
                        se.m_name = nameKey;
                        if (sprite) se.m_icon = sprite;
                    }
                    se.m_ttl = left; // remaining = m_ttl - m_time (StatusSource.Remaining), m_time stays 0
                    list.Add(se);
                    added++;
                }
                if (added > 0) return;
            }

            if (!_fallback)
            {
                var icon = FallbackIcon();
                if (!icon) return;
                _fallback = ScriptableObject.CreateInstance<StatusEffect>();
                _fallback.name = "MT_StolenInfusion";
                _fallback.m_name = "$ui_stolen_infusion";
                _fallback.m_icon = icon;
            }
            _fallback.m_ttl = left;
            list.Add(_fallback);
        }

        // Extracts set bit indices (0..63) in ascending order into dst without allocations.
        internal static int ExtractIds(ulong mask, List<int> dst)
        {
            dst.Clear();
            for (var id = 0; mask != 0; id++, mask >>= 1)
                if ((mask & 1UL) != 0) dst.Add(id);
            return dst.Count;
        }

        private static Sprite FallbackIcon() =>
            ObjectDB.instance ? ObjectDB.instance.GetItemPrefab(IconItem)?.GetComponent<ItemDrop>()?.m_itemData.GetIcon() : null;
    }
}
