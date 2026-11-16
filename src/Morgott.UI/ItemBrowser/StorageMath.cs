using System;
using System.Collections.Generic;

namespace Morgott.UI.ItemBrowser
{
    // Pure rules of the STORAGE tab (network chest browser, BrowserPanel storage mode): take amount bounds, Russian
    // plural forms, the grid filter. No Unity, unit-tested.
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

        /// <summary>Russian plural form of <paramref name="n"/>: 1 (сундук), 2 (сундука), 5 (сундуков). English uses _1 / _5 words.</summary>
        public static int Plural(int n)
        {
            n = Math.Abs(n);
            int ones = n % 10, tens = n % 100;
            if (ones == 1 && tens != 11) return 1;
            return ones >= 2 && ones <= 4 && (tens < 12 || tens > 14) ? 2 : 5;
        }

        /// <summary>Grid filter: in stock, and in the category when one is picked (search is BrowserIndex.Search's).</summary>
        public static Func<Entry, bool> Filter(IReadOnlyDictionary<string, int> stock, int cat) =>
            e => stock.TryGetValue(e.Id, out var c) && c > 0 && (cat < 0 || (int)e.Category == cat);

        public static bool Same(IReadOnlyDictionary<string, int> a, IReadOnlyDictionary<string, int> b)
        {
            if (a.Count != b.Count) return false;
            foreach (var kv in a)
                if (!b.TryGetValue(kv.Key, out var v) || v != kv.Value) return false;
            return true;
        }

        /// <summary>Count with a space between thousands ("1 240", mockup network-storage-v2).</summary>
        public static string Count(int n) => n.ToString("#,0", Groups);

        private static readonly System.Globalization.NumberFormatInfo Groups = new System.Globalization.NumberFormatInfo { NumberGroupSeparator = " " };
    }
}
