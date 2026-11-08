using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Morgott.UI.Compat
{
    // Epic Loot's structured item tooltip (no reference): EpicLoot.API.GetItemTooltipData(item, quality, includeDetails,
    // localize) returns its ItemTooltipData as JSON (0.15.6 decompile: API.cs:2527, ItemTooltipData.cs, the same per-frame
    // cached data its TooltipRenderer draws from). Only the fields the item tooltip sheet shows are mirrored; JsonUtility
    // ignores the rest. includeDetails follows Epic Loot's own MagicItem.ShowEffectDetails, like its renderer.
    internal static class EpicLootTooltipData
    {
        [Serializable] internal sealed class Stat { public string Id; public string Variant; }
        [Serializable] internal sealed class Effect { public string Text; }
        [Serializable] internal sealed class Sockets { public int Count; public int Used; }
        [Serializable] internal sealed class Piece { public string Name; public bool Equipped; }
        [Serializable] internal sealed class Bonus { public int Count; public string Text; public bool Active; }
        [Serializable] internal sealed class Set { public string Name; public int Equipped; public int Full; public List<Piece> Pieces; public List<Bonus> Bonuses; }

        [Serializable]
        internal sealed class Data
        {
            public string Kind, DisplayName, RarityName, RarityColor, ItemTypeName, SetLabel, SetColor, Description;
            public bool IsMagic;
            public int MaterialRarity = -1;
            public List<Stat> Stats;
            public List<Effect> Effects;
            public Sockets Sockets;
            public List<Set> Sets;
        }

        private static bool _tried;
        private static MethodInfo _get;
        private static PropertyInfo _details;

        public static bool Present { get { Resolve(); return _get != null; } }

        public static Data Get(ItemDrop.ItemData item, int quality)
        {
            Resolve();
            if (_get == null || item == null) return null;
            try
            {
                var details = _details != null && _details.GetValue(null) is bool b && b;
                var json = _get.Invoke(null, new object[] { item, quality, details, false }) as string;
                return string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<Data>(json);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Morgott.UI] Epic Loot tooltip data for {item.m_shared?.m_name}: {e.GetType().Name}: {e.Message} (sheet without magic sections)");
                return null;
            }
        }

        private static void Resolve()
        {
            if (_tried) return;
            _tried = true;
            var api = Morgott.Core.TypeLookup.ByFullName("EpicLoot.API");
            if (api == null) return;
            _get = AccessTools.Method(api, "GetItemTooltipData", new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(bool) });
            if (_get == null || _get.ReturnType != typeof(string))
            {
                _get = null;
                Plugin.Log.LogWarning("[Morgott.UI] EpicLoot.API.GetItemTooltipData(ItemData, int, bool, bool) not found: item tooltips show Epic Loot's text without magic / set sections");
                return;
            }
            _details = AccessTools.Property(Morgott.Core.TypeLookup.ByFullName("EpicLoot.MagicItem"), "ShowEffectDetails");
        }

        // "#RRGGBB" / "#RRGGBBAA" -> 8 hex digits; anything else (named colours) -> null.
        public static string Hex(string c)
        {
            if (string.IsNullOrEmpty(c) || c[0] != '#') return null;
            var h = c.Substring(1);
            return h.Length == 6 ? h + "FF" : h.Length == 8 ? h : null;
        }
    }
}
