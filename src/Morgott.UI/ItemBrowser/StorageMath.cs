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
            string.Format(localize("$ib_store_in"), n, localize("$ib_store_in_chest_" + Form(n, language)));

        /// <summary>Word form of <paramref name="n"/> in the game language: Russian rules for Russian, else English ones
        /// (1 = _1, anything else = _5; languages without own words fall back to the English ones).</summary>
        public static int Form(int n, string language) => language == "Russian" ? Plural(n) : Math.Abs(n) == 1 ? 1 : 5;

        /// <summary>Grid rows: stock entries whose prefab <paramref name="shown"/> holds (BrowserIndex.Search hits for the
        /// query + category), by name, then quality high to low; equal ones keep the provider's order (stable sort).</summary>
        public static List<T> Rows<T>(IEnumerable<T> stock, Func<T, string> prefab, ICollection<string> shown, Func<T, string> name, Func<T, int> quality) =>
            stock.Where(e => shown.Contains(prefab(e))).OrderBy(name, StringComparer.OrdinalIgnoreCase).ThenByDescending(quality).ToList();

        /// <summary>Entry of <paramref name="stock"/> with identity <paramref name="key"/> (Equals), null when gone.</summary>
        public static T Pick<T>(IEnumerable<T> stock, object key, Func<T, object> keyOf) where T : class =>
            key == null ? null : stock.FirstOrDefault(e => Equals(keyOf(e), key));

        /// <summary>A single item (Count 1): the take bar shows only "Take", no stack / amount / all.</summary>
        public static bool Single(int count) => count <= 1;

        /// <summary>Cell count badge: several items, or a stackable (as the inventory shows n/max on stackables).</summary>
        public static bool Badge(int count, int maxStack) => count > 1 || maxStack > 1;

        /// <summary>Count with a space between thousands ("1 240", mockup network-storage-v2).</summary>
        public static string Count(int n) => n.ToString("#,0", Groups);

        private static readonly System.Globalization.NumberFormatInfo Groups = new System.Globalization.NumberFormatInfo { NumberGroupSeparator = " " };
    }
}
