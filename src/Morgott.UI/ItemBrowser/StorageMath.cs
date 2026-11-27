using System;
using System.Collections.Generic;
using System.Linq;

namespace Morgott.UI.ItemBrowser
{
    // Pure rules of the STORAGE tab (network chest browser, BrowserPanel storage mode): take amount bounds, Russian
    // plural forms, grid rows / selection by item identity, take bar mode. No Unity, unit-tested.
    public static class StorageMath
    {
        /// <summary>Most units one take can move: what the network holds and what the inventory can still fit.</summary>
        public static int Max(int stock, int room) => Math.Max(0, Math.Min(stock, room));

        /// <summary>Amount kept inside [1, Max]; 0 when nothing can be taken.</summary>
        public static int Clamp(int amount, int stock, int room)
        {
            var hi = Max(stock, room);
            return hi == 0 ? 0 : Math.Max(1, Math.Min(amount, hi));
        }

        /// <summary>Units a grid RMB takes (ITEMS tab scheme): RMB 1, Shift+RMB a full stack, Ctrl+RMB all; capped by
        /// stock and inventory room (partial take when the inventory fills), 0 = nothing fits.</summary>
        public static int ClickAmount(bool stack, bool all, int maxStack, int stock, int room) =>
            Max(stock, room) == 0 ? 0 : Clamp(all ? int.MaxValue : stack ? Math.Max(1, maxStack) : 1, stock, room);

        /// <summary>Russian plural form of <paramref name="n"/>: 1 (сундук), 2 (сундука), 5 (сундуков). English uses _1 / _5 words.</summary>
        public static int Plural(int n)
        {
            n = Math.Abs(n);
            int ones = n % 10, tens = n % 100;
            if (ones == 1 && tens != 11) return 1;
            return ones >= 2 && ones <= 4 && (tens < 12 || tens > 14) ? 2 : 5;
        }

        /// <summary>Take bar chest count, "в 5 сундуках" / "in 5 chests"; <paramref name="localize"/> resolves $tokens.</summary>
        public static string InChests(int n, string language, Func<string, string> localize) =>
            string.Format(localize("$ib_store_in"), n.ToString(System.Globalization.CultureInfo.InvariantCulture), localize("$ib_store_in_chest_" + Form(n, language)));

        /// <summary>Word form of <paramref name="n"/> in the game language: Russian rules for Russian, else English ones
        /// (1 = _1, anything else = _5; languages without own words fall back to the English ones).</summary>
        public static int Form(int n, string language) => language == "Russian" ? Plural(n) : Math.Abs(n) == 1 ? 1 : 5;

        /// <summary>Grid rows: stock entries <paramref name="keep"/> passes (search + category), by name, then quality high
        /// to low; equal ones keep the provider's order (stable sort).</summary>
        public static List<T> Rows<T>(IEnumerable<T> stock, Func<T, bool> keep, Func<T, string> name, Func<T, int> quality) =>
            stock.Where(keep).OrderBy(name, StringComparer.OrdinalIgnoreCase).ThenByDescending(quality).ToList();

        /// <summary>Same stock (order ignored): equal keys with equal <paramref name="sig"/> (count + chests). Unchanged
        /// stock keeps the grid as it is (no rebuild per tick).</summary>
        public static bool Same<T>(IReadOnlyCollection<T> a, IReadOnlyCollection<T> b, Func<T, object> key, Func<T, long> sig)
        {
            if (a.Count != b.Count) return false;
            var map = new Dictionary<object, long>(a.Count);
            var listA = a as IList<T> ?? new List<T>(a);
            for (var i = 0; i < listA.Count; i++)
            {
                var e = listA[i];
                map[key(e) ?? Nil] = sig(e);
            }
            var listB = b as IList<T> ?? new List<T>(b);
            for (var i = 0; i < listB.Count; i++)
            {
                var e = listB[i];
                if (!map.TryGetValue(key(e) ?? Nil, out var v) || v != sig(e)) return false;
            }
            return true;
        }

        private static readonly object Nil = new object();

        /// <summary>Stock signature of one entry: count and chest count.</summary>
        public static long Sig(int count, int chests) => ((long)count << 32) | (uint)chests;

        /// <summary>Units of an item (name, quality, world level) the inventory can take: free slots x max stack + the
        /// room left in partial stacks vanilla Inventory.AddItem would fill (FindFreeStackItem: same name, quality and
        /// world level, Inventory.cs:547).</summary>
        public static int Room(int freeSlots, int maxStack, string name, int quality, int worldLevel, IEnumerable<(string Name, int Quality, int WorldLevel, int Stack)> held)
        {
            maxStack = Math.Max(1, maxStack);
            var room = freeSlots * maxStack;
            var list = held as IList<(string Name, int Quality, int WorldLevel, int Stack)> ?? new List<(string Name, int Quality, int WorldLevel, int Stack)>(held);
            for (var i = 0; i < list.Count; i++)
            {
                var h = list[i];
                if (h.Name == name && h.Quality == quality && h.WorldLevel == worldLevel && h.Stack < maxStack) room += maxStack - h.Stack;
            }
            return room;
        }

        /// <summary>Entry of <paramref name="stock"/> with identity <paramref name="key"/> (Equals), null when gone.</summary>
        public static T Pick<T>(IEnumerable<T> stock, object key, Func<T, object> keyOf) where T : class
        {
            if (key == null) return null;
            var list = stock as IList<T> ?? new List<T>(stock);
            for (var i = 0; i < list.Count; i++)
                if (Equals(keyOf(list[i]), key)) return list[i];
            return null;
        }

        /// <summary>A single item (Count 1): the take bar shows only "Take", no stack / amount / all.</summary>
        public static bool IsSingleItem(int count) => count <= 1;

        /// <summary>Cell count badge: several items, or a stackable (as the inventory shows n/max on stackables).</summary>
        public static bool Badge(int count, int maxStack) => count > 1 || maxStack > 1;

        /// <summary>Count with a space between thousands ("1 240", mockup network-storage-v2).</summary>
        public static string Count(int n) => n.ToString("#,0", Groups);

        private static readonly System.Globalization.NumberFormatInfo Groups = new System.Globalization.NumberFormatInfo { NumberGroupSeparator = " " };
    }
}
