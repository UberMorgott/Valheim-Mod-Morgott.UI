using System;
using System.Collections.Generic;
using System.Globalization;
using Morgott.Core;

namespace Morgott.UI.StationQueue
{
    // Pure text / list math of the station queue window (no Unity): window kind, card / summary / header texts, Russian
    // plurals, list edits. Templates are the localized "$ui_sq_*" words ({0}.. placeholders), passed in by the window.
    internal static class StationQueueMath
    {
        public const int DefaultCount = 10, DefaultThreshold = 100, MaxValue = 99999;
        public const string Separator = " · ";

        // One input = the single-input window (eitr refinery, windmill, spinning wheel); more = the queue window.
        public static bool IsSingle(int inputs) => inputs == 1;

        // Steps back from the hover reach before the window closes.
        public const float ReachMargin = 1.5f;

        // Open while the eye is within the hover reach (+ margin) of the station's nearest surface point: anything the
        // hotkey could open (hover within m_maxInteractDistance) stays open, whatever the station's size.
        public static bool KeepsOpen(float eyeToSurface, float maxInteractDistance) => eyeToSurface <= maxInteractDistance + ReachMargin;

        public static string CompareText(StationQueueCompare c) => c == StationQueueCompare.Less ? "<" : "≥";

        // "1 240": thousands grouped by a space (mockup counts).
        public static string Group(int n) => n.ToString("#,0", CultureInfo.InvariantCulture).Replace(',', ' ');

        // Russian plural form index: 0 = one (1, 21), 1 = few (2-4, 22-24), 2 = many (0, 5-20, 11-14).
        public static int PluralForm(int n)
        {
            n = Math.Abs(n) % 100;
            var d = n % 10;
            if (n >= 11 && n <= 14) return 2;
            if (d == 1) return 0;
            return d >= 2 && d <= 4 ? 1 : 2;
        }

        public static string Plural(int n, string one, string few, string many)
        {
            switch (PluralForm(n))
            {
                case 0: return one;
                case 1: return few;
                default: return many;
            }
        }

        public static string Format(string template, params object[] args) =>
            string.Format(CultureInfo.InvariantCulture, template ?? "", args);

        // Non-empty parts joined by " · " (header / status lines).
        public static string Join(params string[] parts)
        {
            var list = new List<string>(parts.Length);
            foreach (var p in parts) if (!string.IsNullOrEmpty(p)) list.Add(p);
            return string.Join(Separator, list);
        }

        // Card text: "50 × Fine wood" / "Wood — ∞" / until template ({0} item, {1} condition item, {2} comparator, {3} threshold)
        // / keep-in-stock count ({0} item, {1} its output, {2} target).
        public static string CardText(StationQueueEntry e, string item, string cond, string untilTemplate, string keepTemplate = null, string output = null)
        {
            switch (e.Mode)
            {
                case StationQueueMode.Count: return e.Keep && keepTemplate != null ? Format(keepTemplate, item, output, Group(e.Target)) : Group(e.Target) + " × " + item;
                case StationQueueMode.Infinite: return item + " — ∞";
                default: return Format(untilTemplate, item, cond, CompareText(e.Compare), Group(e.Threshold));
            }
        }

        // Editor summary line: count {0} item {1} target; infinite {0} item; until {0} item {1} condition {2} comparator
        // {3} threshold {4} current count of the condition item (network + pending); keep-in-stock count {0} item {1} its
        // output (passed as cond) {2} target {3} current (passed as current); keep until = the until line + keepTail.
        public static string Summary(StationQueueEntry e, string item, string cond, int current, string countTemplate, string infiniteTemplate, string untilTemplate,
            string keepTemplate = null, string keepTail = null)
        {
            switch (e.Mode)
            {
                case StationQueueMode.Count:
                    return e.Keep && keepTemplate != null ? Format(keepTemplate, item, cond, Group(e.Target), Group(current)) : Format(countTemplate, item, Group(e.Target));
                case StationQueueMode.Infinite: return Format(infiniteTemplate, item);
                default:
                    return Format(untilTemplate, item, cond, CompareText(e.Compare), Group(e.Threshold), Group(current)) + (e.Keep && !string.IsNullOrEmpty(keepTail) ? " " + keepTail : "");
            }
        }

        // Keep in stock means something for count / until entries (infinite never ends anyway).
        public static bool CanKeep(StationQueueMode mode) => mode != StationQueueMode.Infinite;

        // The running entry (provider's Active: resting keep entries are skipped) shows the cyan rim + progress while the
        // station works or waits for input.
        public static bool IsActive(int index, int active, StationQueueStatus status) =>
            index == active && (status == StationQueueStatus.Working || status == StationQueueStatus.Waiting);

        // Progress bar share: count mode = fed / target (keep: stock / target), else the item being processed now.
        public static float Share(StationQueueEntry e, float progress, int stock = 0)
        {
            var share = e.Mode == StationQueueMode.Count && e.Target > 0 ? (float)(e.Keep ? stock : e.Fed) / e.Target : progress;
            return share < 0f ? 0f : share > 1f ? 1f : share;
        }

        // Until mode live bar: network count / threshold.
        public static float Share(int current, int threshold) =>
            threshold <= 0 ? 1f : Math.Min(1f, Math.Max(0f, (float)current / threshold));

        // "[Esc] Close" with the key in the accent colour (Latin key, localized word).
        public static string KeyHint(string key, string word, string hex) => $"<color=#{hex}>[{key}]</color> {word}";

        // Mode button text: radio mark + word, the selected mark in the accent colour.
        public static string ModeText(bool on, string word, string hex) => (on ? $"<color=#{hex}>◉</color>" : "○") + "  " + word;

        // Downward shift of the centred editor dialog so its bottom hangs `overhang` below the centred parent window's.
        public static float EditorDrop(float parentH, float dialogH, float overhang) => (parentH - dialogH) / 2f + overhang;

        public static int Clamp(int v) => v < 1 ? 1 : v > MaxValue ? MaxValue : v;

        // Count field of an entry stored without a count (infinite / until entries keep Target 0): the new-entry default.
        public static int ShownTarget(int target) => target > 0 ? target : DefaultCount;

        // Stepper: 1 per click, 10 with Shift.
        public static int Step(int v, int dir, bool big) => Clamp(v + dir * (big ? 10 : 1));

        public static StationQueueEntry NewEntry(string item, StationQueueMode mode, string condition) => new StationQueueEntry
        {
            Item = item,
            Mode = mode,
            Target = DefaultCount,
            Condition = condition,
            Compare = StationQueueCompare.Less,
            Threshold = DefaultThreshold,
        };

        public static List<StationQueueEntry> Clone(IList<StationQueueEntry> entries)
        {
            var list = new List<StationQueueEntry>(entries.Count + 1);
            foreach (var e in entries) list.Add(e.Clone());
            return list;
        }

        // Swaps entry i with its neighbour (delta -1 up, +1 down); false at the ends.
        public static bool Move(List<StationQueueEntry> list, int i, int delta)
        {
            var j = i + delta;
            if (i < 0 || j < 0 || i >= list.Count || j >= list.Count) return false;
            (list[i], list[j]) = (list[j], list[i]);
            return true;
        }

        // Drag-reorder: entry `from` taken out and put at `to` (clamped); false when nothing moves.
        public static bool MoveTo(List<StationQueueEntry> list, int from, int to)
        {
            if (from < 0 || from >= list.Count) return false;
            to = to < 0 ? 0 : to >= list.Count ? list.Count - 1 : to;
            if (to == from) return false;
            var e = list[from];
            list.RemoveAt(from);
            list.Insert(to, e);
            return true;
        }

        // Card slot under a pointer `y` units below the list top (rows `step` apart, first shown = scroll).
        public static int SlotAt(float y, float step, int scroll) => scroll + (int)Math.Floor(Math.Max(0f, y) / step);

        // Header "loaded" word per station kind, from its piece name token (lowercased): furnace "В печи", machine
        // "В машине", barrel "В бочке", fire "На огне"; anything else the generic "Загружено".
        public static string LoadedKey(string nameToken)
        {
            var n = (nameToken ?? "").ToLowerInvariant();
            if (Has(n, "kiln", "smelter", "blastfurnace", "furnace", "oven")) return "loaded_furnace";
            if (Has(n, "eitr", "refinery", "windmill", "spinning")) return "loaded_machine";
            if (n.Contains("fermenter")) return "loaded_barrel";
            if (n.Contains("cooking")) return "loaded_fire";
            return "loaded";
        }

        private static bool Has(string s, params string[] parts)
        {
            foreach (var p in parts) if (s.Contains(p)) return true;
            return false;
        }

        // "Loaded: 4/20" + the input unit when the language has one ("ткани").
        public static string Loaded(string template, int loaded, int capacity, string unit) =>
            Format(template, loaded, capacity) + (string.IsNullOrEmpty(unit) ? "" : " " + unit);

        // Editor save: replaces entry `index` (still there and the same input), else appends the draft.
        public static List<StationQueueEntry> Save(IList<StationQueueEntry> entries, int index, StationQueueEntry draft)
        {
            var list = Clone(entries);
            if (index >= 0 && index < list.Count && list[index].Item == draft.Item) list[index] = draft.Clone();
            else list.Add(draft.Clone());
            return list;
        }

        // Single-input window: its one entry (the first one for that input), or null = no queue.
        public static StationQueueEntry SingleEntry(IList<StationQueueEntry> entries, string input)
        {
            foreach (var e in entries) if (e.Item == input) return e;
            return null;
        }

        // Single-input window writes exactly one entry (or none).
        public static List<StationQueueEntry> Single(StationQueueEntry e) =>
            e == null ? new List<StationQueueEntry>() : new List<StationQueueEntry> { e.Clone() };

        // Picker search: case-insensitive substring, empty query = all.
        public static bool Matches(string name, string query) =>
            string.IsNullOrEmpty(query) || (name ?? "").IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
