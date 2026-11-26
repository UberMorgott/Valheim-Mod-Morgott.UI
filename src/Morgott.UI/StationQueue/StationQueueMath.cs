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
        public const int TabMake = 0, TabKeep = 1, TabUntil = 2, TabInfinite = 3, TabCount = 4;
        public const string Separator = " · ";

        // One input = the single-input window (windmill, spinning wheel) unless multiOnly forces the recipe queue UI,
        // the station already has multiple queued entries, or the multi modal editor is currently open (preserve draft).
        public static bool IsSingle(int inputs, bool multiOnly = false, int entryCount = 0, bool editing = false) =>
            !editing && !multiOnly && inputs == 1 && entryCount <= 1;

        public static int TabOf(StationQueueEntry e)
        {
            if (e == null) return -1;
            switch (e.Mode)
            {
                case StationQueueMode.Count: return e.Keep ? TabKeep : TabMake;
                case StationQueueMode.Until: return TabUntil;
                case StationQueueMode.Infinite: return TabInfinite;
                default: return TabMake;
            }
        }

        public static void SwitchTab(StationQueueEntry e, int tab, string defaultCondition, bool untilKeep = true, bool infiniteKeep = true)
        {
            if (e == null) return;
            switch (tab)
            {
                case TabMake:
                    if (e.Mode != StationQueueMode.Count) e.Fed = 0;
                    e.Mode = StationQueueMode.Count;
                    e.Keep = false;
                    e.Target = ShownTarget(e.Target);
                    break;
                case TabKeep:
                    if (e.Mode != StationQueueMode.Count) e.Fed = 0;
                    e.Mode = StationQueueMode.Count;
                    e.Keep = true;
                    e.Target = ShownTarget(e.Target);
                    break;
                case TabUntil:
                    if (e.Mode != StationQueueMode.Until)
                    {
                        e.Mode = StationQueueMode.Until;
                        e.Keep = untilKeep;
                        e.Fed = 0;
                    }
                    if (string.IsNullOrEmpty(e.Condition)) e.Condition = defaultCondition;
                    if (e.Threshold <= 0) e.Threshold = DefaultThreshold;
                    break;
                case TabInfinite:
                    if (e.Mode != StationQueueMode.Infinite)
                    {
                        e.Mode = StationQueueMode.Infinite;
                        e.Keep = infiniteKeep;
                        e.Fed = 0;
                    }
                    break;
            }
        }

        public static StationQueueEntryStatus EntryStatus(int id, int index, int active, StationQueueStatus stationStatus,
            IReadOnlyDictionary<int, StationQueueEntryStatus> statuses)
        {
            if (statuses != null && statuses.Count > 0)
                return id != 0 && statuses.TryGetValue(id, out var st) ? st : StationQueueEntryStatus.Queued;
            return IsActive(index, active, stationStatus) ? StationQueueEntryStatus.Running : StationQueueEntryStatus.Queued;
        }

        // Single-input header status key: surfaces the live entry's specific reason when unpaused (and not plain
        // Queued), otherwise the station-level status word.
        public static string SingleStatusKey(StationQueueEntry live, StationQueueStatus stationStatus,
            IReadOnlyDictionary<int, StationQueueEntryStatus> statuses, out StationQueueEntryStatus? entryStatus)
        {
            if (stationStatus != StationQueueStatus.Paused && live != null && live.Id != 0 && statuses != null &&
                statuses.TryGetValue(live.Id, out var st) && st != StationQueueEntryStatus.Queued)
            {
                entryStatus = st;
                return StatusKey(st);
            }
            entryStatus = null;
            switch (stationStatus)
            {
                case StationQueueStatus.Working: return "status_working";
                case StationQueueStatus.Waiting: return "status_waiting";
                case StationQueueStatus.Paused: return "status_paused";
                default: return "status_idle";
            }
        }

        public static string StatusKey(StationQueueEntryStatus status)
        {
            switch (status)
            {
                case StationQueueEntryStatus.Running: return "st_running";
                case StationQueueEntryStatus.MissingInput: return "st_missing";
                case StationQueueEntryStatus.Stocked: return "st_stocked";
                case StationQueueEntryStatus.Pending: return "st_pending";
                case StationQueueEntryStatus.Blocked: return "st_blocked";
                default: return "st_queued";
            }
        }

        public static string DisplayName(string name, int outputAmount) =>
            outputAmount > 1 ? (name ?? "") + " ×" + Group(outputAmount) : name ?? "";

        // Multi-ingredient recipes (Cauldron / Mead Ketill) show the crafted output; single-input conversions
        // (Kiln, Smelter, Fermenter, CookingStation) show the input (e.g. Wood / FineWood / RoundLog, not 3 identical Coal rows).
        public static bool PrefersOutput(bool hasInputItem, int ingredientCount) => !hasInputItem || ingredientCount > 0;

        public static StationQueueInput FindInput(IReadOnlyList<StationQueueInput> inputs, string key)
        {
            if (inputs == null || string.IsNullOrEmpty(key)) return null;
            for (var i = 0; i < inputs.Count; i++)
                if (inputs[i] != null && string.Equals(inputs[i].Key, key, StringComparison.Ordinal)) return inputs[i];
            return null;
        }

        // Compact "Name net/req, ..." list for multi-ingredient recipes; highlights missing ingredients (net < req).
        public static string IngredientsText(IReadOnlyList<(string Name, int Amount, int Network)> ingredients,
            string okHex, string missingHex, string template = null)
        {
            if (ingredients == null || ingredients.Count == 0) return "";
            var parts = new List<string>(ingredients.Count);
            for (var i = 0; i < ingredients.Count; i++)
            {
                var (name, amount, network) = ingredients[i];
                var part = $"{name} {Group(network)}/{Group(amount)}";
                var hex = network < amount ? missingHex : okHex;
                if (!string.IsNullOrEmpty(hex)) part = $"<color=#{hex}>{part}</color>";
                parts.Add(part);
            }
            var joined = string.Join(", ", parts);
            return string.IsNullOrEmpty(template) ? joined : Format(template, joined);
        }

        // Steps back from the hover reach before the window closes.
        public const float ReachMargin = 1.5f;

        // Open while the eye is within the hover reach (+ margin) of the station's nearest surface point: anything the
        // hotkey could open (hover within m_maxInteractDistance) stays open, whatever the station's size.
        public static bool KeepsOpen(float eyeToSurface, float maxInteractDistance) => eyeToSurface <= maxInteractDistance + ReachMargin;

        public static string CompareText(StationQueueCompare c) => c == StationQueueCompare.Less ? "<" : "≥";

        // "1 240": thousands grouped by a space (mockup counts).
        public static string Group(int n) => Group((long)n);
        public static string Group(long n) => n.ToString("#,0", CultureInfo.InvariantCulture).Replace(',', ' ');

        public static string MakeYield(int crafts, int outputAmount, string yieldTemplate) =>
            outputAmount > 1 && !string.IsNullOrEmpty(yieldTemplate) ? Format(yieldTemplate, Group(crafts), Group((long)crafts * outputAmount)) : "";

        public static string KeepNowText(int chests, int pending, string nowTemplate, string pendingTemplate) =>
            pending > 0 && !string.IsNullOrEmpty(pendingTemplate)
                ? Format(pendingTemplate, Group(chests), Group(pending))
                : Format(nowTemplate, Group(chests));

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

        // Editor summary line: count {0} item {1} target (or with yield: {0} item {1} crafts {2} total); infinite {0} item;
        // until {0} item {1} condition {2} comparator {3} threshold {4} current; keep-in-stock count {0} item {1} its output
        // {2} target {3} current; keep until = the until line + keepTail.
        public static string Summary(StationQueueEntry e, string item, string cond, int current, string countTemplate, string infiniteTemplate, string untilTemplate,
            string keepTemplate = null, string keepTail = null, int outputAmount = 1, string yieldCountTemplate = null)
        {
            switch (e.Mode)
            {
                case StationQueueMode.Count:
                    if (e.Keep && keepTemplate != null) return Format(keepTemplate, item, cond, Group(e.Target), Group(current));
                    if (outputAmount > 1 && !string.IsNullOrEmpty(yieldCountTemplate))
                        return Format(yieldCountTemplate, item, Group(e.Target), Group((long)e.Target * outputAmount));
                    return Format(countTemplate, item, Group(e.Target));
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

        public static StationQueueEntry NewEntry(string item, StationQueueMode mode, string condition, bool keep = false) => new StationQueueEntry
        {
            Item = item,
            Mode = mode,
            Target = DefaultCount,
            Condition = condition,
            Compare = StationQueueCompare.Less,
            Threshold = DefaultThreshold,
            Keep = keep,
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

        // Header "loaded" word per station kind, from kind tag ("craft" -> ui_sq_crafting) or its piece name token
        // (lowercased): furnace "В печи", machine "В машине", barrel "В бочке", fire "На огне"; else generic "Загружено".
        public static string LoadedKey(string nameToken, string kind = "")
        {
            if (string.Equals(kind, "craft", StringComparison.OrdinalIgnoreCase)) return "ui_sq_crafting";
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

        // The edited entry in a list read since the editor opened: by its owner id (rows above it may have finished and gone
        // meanwhile), an unnumbered one by `index` while that is still the same input; -1 = gone.
        public static int Find(IList<StationQueueEntry> entries, int index, StationQueueEntry draft)
        {
            if (draft.Id != 0)
            {
                for (var i = 0; i < entries.Count; i++) if (entries[i].Id == draft.Id) return i;
                return -1;
            }
            // a new entry opened before the owner numbered it: still at its row
            return index >= 0 && index < entries.Count && entries[index].Item == draft.Item ? index : -1;
        }

        // Editor save: when index < 0 (new entry) appends draft; when index >= 0 (editing existing) replaces Find, or
        // leaves the list unchanged if that entry finished/vanished meanwhile (never creates a silent duplicate).
        public static List<StationQueueEntry> Save(IList<StationQueueEntry> entries, int index, StationQueueEntry draft)
        {
            var list = Clone(entries);
            var copy = draft.Clone();
            if (index < 0)
            {
                list.Add(copy);
                return list;
            }
            var at = Find(list, index, draft);
            if (at >= 0)
            {
                if (copy.Id == 0) copy.Id = list[at].Id; // numbered by the owner since: the same entry, its progress kept
                list[at] = copy;
            }
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

        // Single-input tab change: on an empty station (`live == null`) updates `draft` locally and returns false so no
        // default order is committed until Start; when `live != null` clones and switches it, returning true to commit.
        public static bool SingleTab(StationQueueEntry live, ref StationQueueEntry draft, int tab, string item,
            string defaultCondition, out StationQueueEntry next)
        {
            if (live != null)
            {
                if (TabOf(live) == tab) { next = null; return false; }
                next = live.Clone();
                SwitchTab(next, tab, defaultCondition, untilKeep: true, infiniteKeep: true);
                return true;
            }
            if (draft != null && TabOf(draft) == tab) { next = null; return false; }
            if (draft == null)
                draft = NewEntry(item, StationQueueMode.Count, defaultCondition == item ? null : defaultCondition, keep: false);
            SwitchTab(draft, tab, defaultCondition, untilKeep: true, infiniteKeep: true);
            next = draft;
            return false;
        }

        // Single-input control edit: edits `draft` in place when `live == null` (false = keep local draft); clones and
        // edits `live` when an active entry exists (true = commit immediately).
        public static bool SingleEdit(StationQueueEntry live, StationQueueEntry draft, Action<StationQueueEntry> edit,
            out StationQueueEntry next)
        {
            if (live != null)
            {
                next = live.Clone();
                edit?.Invoke(next);
                return true;
            }
            next = draft;
            if (next != null) edit?.Invoke(next);
            return false;
        }

        // Picker search: case-insensitive substring, empty query = all.
        public static bool Matches(string name, string query) =>
            string.IsNullOrEmpty(query) || (name ?? "").IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
