using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Morgott.UI.Compat
{
    // Epic Loot's structured item tooltip (no reference): EpicLoot.ItemTooltip.GetData(item, quality, includeDetails)
    // returns the per-frame cached ItemTooltipData its TooltipRenderer draws from (0.15.6 decompile: ItemTooltip.cs:68,
    // ItemTooltipData.cs, TooltipEffect / TooltipSet / TooltipSetPiece / TooltipSetBonus / TooltipSockets / TooltipStat).
    // Read field by field into the mirror below (its public API's JSON through JsonUtility came back with empty lists in
    // run 20261108-183456: no magic / set sections). includeDetails follows Epic Loot's MagicItem.ShowEffectDetails.
    internal static class EpicLootTooltipData
    {
        internal sealed class Stat { public string Id; public string Variant; }
        internal sealed class Effect { public string Text; }
        internal sealed class Sockets { public int Count; public int Used; }
        internal sealed class Piece { public string Name; public bool Equipped; }
        internal sealed class Bonus { public int Count; public string Text; public bool Active; }
        internal sealed class Set { public string Name; public int Equipped; public int Full; public List<Piece> Pieces = new List<Piece>(); public List<Bonus> Bonuses = new List<Bonus>(); }

        internal sealed class Data
        {
            public string Kind, DisplayName, RarityName, RarityColor, ItemTypeName, SetLabel, SetColor, Description;
            public bool IsMagic;
            public int MaterialRarity = -1;
            public List<Stat> Stats = new List<Stat>();
            public List<Effect> Effects = new List<Effect>();
            public Sockets Sockets;
            public List<Set> Sets = new List<Set>();
        }

        private static bool _tried;
        private static MethodInfo _get;
        private static PropertyInfo _details;

        public static Data Get(ItemDrop.ItemData item, int quality)
        {
            Resolve();
            if (_get == null || item == null) return null;
            try
            {
                var details = _details != null && _details.GetValue(null) is bool b && b;
                var src = _get.Invoke(null, new object[] { item, quality, details });
                return src == null ? null : Read(src);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Morgott.UI] Epic Loot tooltip data for {item.m_shared?.m_name}: {e.GetType().Name}: {e.Message} (sheet without magic sections)");
                return null;
            }
        }

        private static Data Read(object src)
        {
            var d = new Data
            {
                Kind = S(src, "Kind"),
                DisplayName = S(src, "DisplayName"),
                RarityName = S(src, "RarityName"),
                RarityColor = S(src, "RarityColor"),
                ItemTypeName = S(src, "ItemTypeName"),
                SetLabel = S(src, "SetLabel"),
                SetColor = S(src, "SetColor"),
                Description = S(src, "Description"),
                IsMagic = F(src, "IsMagic") is bool m && m,
                MaterialRarity = F(src, "MaterialRarity") is int r ? r : -1,
            };
            foreach (var x in L(src, "Stats")) d.Stats.Add(new Stat { Id = S(x, "Id"), Variant = S(x, "Variant") });
            foreach (var x in L(src, "Effects")) d.Effects.Add(new Effect { Text = S(x, "Text") });
            var sockets = F(src, "Sockets");
            if (sockets != null) d.Sockets = new Sockets { Count = I(sockets, "Count"), Used = I(sockets, "Used") };
            foreach (var x in L(src, "Sets"))
            {
                var set = new Set { Name = S(x, "Name"), Equipped = I(x, "Equipped"), Full = I(x, "Full") };
                foreach (var p in L(x, "Pieces")) set.Pieces.Add(new Piece { Name = S(p, "Name"), Equipped = F(p, "Equipped") is bool e && e });
                foreach (var b in L(x, "Bonuses")) set.Bonuses.Add(new Bonus { Count = I(b, "Count"), Text = S(b, "Text"), Active = F(b, "Active") is bool a && a });
                d.Sets.Add(set);
            }
            return d;
        }

        private static object F(object o, string field) => o == null ? null : AccessTools.Field(o.GetType(), field)?.GetValue(o);
        private static string S(object o, string field) => F(o, field) as string;
        private static int I(object o, string field) => F(o, field) is int i ? i : 0;
        private static IEnumerable<object> L(object o, string field)
        {
            if (F(o, field) is IEnumerable list)
                foreach (var x in list) if (x != null) yield return x;
        }

        private static void Resolve()
        {
            if (_tried) return;
            _tried = true;
            var type = Morgott.Core.TypeLookup.ByFullName("EpicLoot.ItemTooltip");
            if (type == null) return;
            _get = AccessTools.Method(type, "GetData", new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool) });
            if (_get == null)
            {
                Plugin.Log.LogWarning("[Morgott.UI] EpicLoot.ItemTooltip.GetData(ItemData, int, bool) not found: item tooltips show Epic Loot's text without magic / set sections");
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
