using System;
using System.Collections.Generic;
using Morgott.Core;

namespace Morgott.UI.GameScreens
{
    // Row state chip of Hildir's contract board (mockup 2026-11-24 hildir-bounty-board): ДОСТУПЕН / ЗАКРЫТ / ВЗЯТ / ВЫПОЛНЕН.
    public enum HildirChip { Open, Locked, Taken, Done }

    // One board row: a contract snapshot (Id set) or a locked boss of the catalog (Id null).
    public sealed class HildirRow
    {
        public string Id, Prefab, NameToken, UnlockKey;
        public int Biome, Coins, Gold;
        public HildirChip Chip;
    }

    // HildirBoard's provider-read gate: one Available()/Active() read per period, plus one immediate read owed by an action
    // result (Force; several before the next Take coalesce into one).
    public sealed class HildirRefreshGate
    {
        private readonly float _period;
        private float _next = float.NegativeInfinity;
        private bool _forced;

        public HildirRefreshGate(float period) => _period = period;

        public void Force() => _forced = true;

        // True = read now (re-arms the period).
        public bool Take(float now)
        {
            if (!_forced && now < _next) return false;
            _forced = false;
            _next = now + _period;
            return true;
        }
    }

    // Game-free logic of HildirBoard (unit-tested): tab partition, locked rows, chips, button states, text keys.
    public static class HildirBoardMath
    {
        public const int TabCount = 3; // 0 Доступные, 1 Активные, 2 Выполненные

        // Tab 0 = the current offers (Open) + catalog bosses whose unlock key is unset (Locked, after them); tab 1 = accepted
        // InProgress (Taken); tab 2 = Complete (Done). A boss with its key set but no offer (accepted this refresh) is not listed.
        public static void Partition(IReadOnlyList<HildirContract> available, IReadOnlyList<HildirContract> active,
            IReadOnlyList<UniqueBoss> bosses, Func<string, bool> keySet, List<HildirRow>[] tabs)
        {
            foreach (var t in tabs) t.Clear();
            if (available != null)
                foreach (var c in available)
                    if (c?.Prefab != null) tabs[0].Add(Row(c, HildirChip.Open, bosses));
            if (active != null)
                foreach (var c in active)
                {
                    if (c?.Prefab == null) continue;
                    if (c.State == HildirContractState.InProgress) tabs[1].Add(Row(c, HildirChip.Taken, bosses));
                    else if (c.State == HildirContractState.Complete) tabs[2].Add(Row(c, HildirChip.Done, bosses));
                }
            if (bosses == null) return;
            foreach (var b in bosses)
                if (b?.Prefab != null && !string.IsNullOrEmpty(b.UnlockKey) && (keySet == null || !keySet(b.UnlockKey)) && !Listed(tabs, b.Prefab))
                    tabs[0].Add(new HildirRow { Prefab = b.Prefab, NameToken = b.NameToken, UnlockKey = b.UnlockKey, Biome = b.Biome, Chip = HildirChip.Locked });
        }

        private static HildirRow Row(HildirContract c, HildirChip chip, IReadOnlyList<UniqueBoss> bosses) => new HildirRow
        {
            Id = c.Id,
            Prefab = c.Prefab,
            NameToken = c.NameToken,
            UnlockKey = Boss(bosses, c.Prefab)?.UnlockKey,
            Biome = c.Biome,
            Coins = c.RewardCoins,
            Gold = c.RewardGold,
            Chip = chip,
        };

        private static UniqueBoss Boss(IReadOnlyList<UniqueBoss> bosses, string prefab)
        {
            if (bosses == null) return null;
            foreach (var b in bosses)
                if (b != null && b.Prefab == prefab) return b;
            return null;
        }

        private static bool Listed(List<HildirRow>[] tabs, string prefab)
        {
            foreach (var t in tabs)
                foreach (var r in t)
                    if (r.Prefab == prefab) return true;
            return false;
        }

        // Взять заказ = an Open offer; Получить награду = a Done contract; Отказаться = a Taken one.
        public static void Buttons(HildirRow selected, out bool accept, out bool claim, out bool abandon)
        {
            var chip = selected?.Chip;
            accept = chip == HildirChip.Open;
            claim = chip == HildirChip.Done;
            abandon = chip == HildirChip.Taken;
        }

        // Index of the row of `prefab`, else the first row (-1 = empty list).
        public static int Select(List<HildirRow> rows, string prefab)
        {
            for (var i = 0; i < rows.Count; i++)
                if (rows[i].Prefab == prefab) return i;
            return rows.Count > 0 ? 0 : -1;
        }

        // Per-boss LOC key stem: the prefab lower-cased without the no-quest-chest suffix (Skeleton_Hildir_nochest ->
        // skeleton_hildir).
        public static string TextKey(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return "";
            var k = prefab.ToLowerInvariant();
            const string suffix = "_nochest";
            return k.EndsWith(suffix, StringComparison.Ordinal) ? k.Substring(0, k.Length - suffix.Length) : k;
        }

        // "Трофей: <item>"; vanilla Russian trophy names already start with the label ("Трофей: Бренна"): no second label.
        public static string LabelLine(string label, string value) =>
            value.StartsWith(label, StringComparison.OrdinalIgnoreCase) ? value : label + ": " + value;

        // Vanilla biome name token of one Heightmap.Biome flag ("$biome_" + name lower-cased, as vanilla builds it); "" unknown.
        public static string BiomeToken(int biome)
        {
            switch (biome)
            {
                case 1: return "$biome_meadows";
                case 2: return "$biome_swamp";
                case 4: return "$biome_mountain";
                case 8: return "$biome_blackforest";
                case 16: return "$biome_plains";
                case 32: return "$biome_ashlands";
                case 64: return "$biome_deepnorth";
                case 256: return "$biome_ocean";
                case 512: return "$biome_mistlands";
                default: return "";
            }
        }

        // Chip LOC token.
        public static string ChipToken(HildirChip chip) => chip switch
        {
            HildirChip.Locked => "$ui_hildir_chip_locked",
            HildirChip.Taken => "$ui_hildir_chip_taken",
            HildirChip.Done => "$ui_hildir_chip_done",
            _ => "$ui_hildir_chip_open",
        };

        // A vanilla Localize result for a missing token ("[token]", Localization.Translate).
        public static bool Missing(string localized) => string.IsNullOrEmpty(localized) || localized[0] == '[';

        // Card button label (mockup: "ВЗЯТЬ / ЗАКАЗ" on two lines in a tall plate): one line when it fits `area` at the
        // full size, else split at the most balanced space; the size drops only as far as the widest line needs (a single
        // word shrinks). width(i, j) = width of text[i, j) at the full size.
        public static string ButtonLabel(string text, Func<int, int, float> width, float area, float full, out float size)
        {
            size = full;
            if (string.IsNullOrEmpty(text) || width == null) return text ?? "";
            var w = width(0, text.Length);
            if (w <= area) return text;
            var cut = Restyle.LabelFit.BestSplit(text, width, out var widest);
            size = Math.Min(full, Restyle.LabelFit.OneLineSize(area, cut < 0 ? w : widest, full));
            return cut < 0 ? text : text.Remove(cut, 1).Insert(cut, "\n");
        }
    }
}
