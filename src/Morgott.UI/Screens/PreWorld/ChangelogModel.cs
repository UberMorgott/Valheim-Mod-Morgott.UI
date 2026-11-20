using System;
using System.Collections.Generic;
using System.Globalization;
using Morgott.UI.Theme;

namespace Morgott.UI.PreWorld
{
    // Pack changelog data (BepInEx\config\Morgott.UI\changelog.json, written by the pack generator mod-updaters
    // cmd/changelog): {"schemaVersion":1,"generated":"..","entries":[{id,date,mod,version,type,text:{en,ru},src}]}, newest
    // first. Pure logic (unit tested): parse, filter by period / type / mod, group mod + version + date -> lines, mod list search / solo.
    public sealed class ChangelogEntry
    {
        public string Id, Mod, Version, En, Ru;
        public int Type; // index into ChangelogModel.Types
        public DateTime Date;
        public string Text(bool russian) => russian && !string.IsNullOrEmpty(Ru) ? Ru : En ?? Ru ?? "";
    }

    public enum ChangelogRowKind { Mod, Line }

    public readonly struct ChangelogRow
    {
        public readonly ChangelogRowKind Kind;
        public readonly ChangelogEntry Entry; // Mod rows (mod + version + date header): the group's first entry
        public ChangelogRow(ChangelogRowKind kind, ChangelogEntry entry) { Kind = kind; Entry = entry; }
    }

    public sealed class ChangelogFilter
    {
        public DateTime? From, To; // inclusive, date part only
        public readonly bool[] Types = { true, true, true, true, true, true };
        public readonly HashSet<string> HiddenMods = new HashSet<string>(StringComparer.Ordinal);
    }

    public sealed class ChangelogView
    {
        public readonly List<ChangelogRow> Rows = new List<ChangelogRow>();
        public readonly int[] TypeCounts = new int[ChangelogModel.Types.Length]; // date + mod filters, any type
        public readonly Dictionary<string, int> ModCounts = new Dictionary<string, int>(StringComparer.Ordinal); // date + type filters
        public int Shown, Total;
    }

    public static class ChangelogModel
    {
        public const int Schema = 1;
        public static readonly string[] Types = { "feature", "fix", "change", "balance", "perf", "mods" };

        // null + error text when the file is not a schema-1 changelog; bad entries (unknown type, bad date, no text) are skipped.
        public static List<ChangelogEntry> Parse(string json, out string error)
        {
            error = null;
            Dictionary<string, object> root;
            try { root = ThemeJson.ParseValue(json) as Dictionary<string, object>; }
            catch (FormatException e) { error = "corrupt: " + e.Message; return null; }
            if (root == null) { error = "corrupt: root is not an object"; return null; }
            if (!(root.TryGetValue("schemaVersion", out var sv) && sv is double d && (int)d == Schema)) { error = "unknown schemaVersion"; return null; }
            var list = new List<ChangelogEntry>();
            if (!(root.TryGetValue("entries", out var ev) && ev is List<object> raw)) return list;
            foreach (var o in raw)
            {
                if (!(o is Dictionary<string, object> e)) continue;
                var type = Array.IndexOf(Types, Str(e, "type"));
                if (type < 0 || !DateTime.TryParseExact(Str(e, "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
                var text = e.TryGetValue("text", out var tv) ? tv as Dictionary<string, object> : null;
                var en = text != null ? Str(text, "en") : null;
                var ru = text != null ? Str(text, "ru") : null;
                if (string.IsNullOrEmpty(en) && string.IsNullOrEmpty(ru)) continue;
                list.Add(new ChangelogEntry { Id = Str(e, "id"), Date = date, Mod = Str(e, "mod") ?? "", Version = Str(e, "version") ?? "", Type = type, En = en, Ru = ru });
            }
            // Newest first; the file order is kept inside one day (stable sort).
            var idx = new Dictionary<ChangelogEntry, int>(list.Count);
            for (var i = 0; i < list.Count; i++) idx[list[i]] = i;
            list.Sort((a, b) => a.Date != b.Date ? b.Date.CompareTo(a.Date) : idx[a].CompareTo(idx[b]));
            return list;
        }

        private static string Str(Dictionary<string, object> o, string key) => o.TryGetValue(key, out var v) ? v as string : null;

        // Mods present in the data, by name.
        public static List<string> Mods(List<ChangelogEntry> all)
        {
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in all) set.Add(e.Mod);
            return new List<string>(set);
        }

        public static ChangelogView Build(List<ChangelogEntry> all, ChangelogFilter f)
        {
            var v = new ChangelogView { Total = all.Count };
            // Groups keyed by date + mod + version in first-appearance order (entries are newest first).
            var groups = new List<List<ChangelogEntry>>();
            var byKey = new Dictionary<string, List<ChangelogEntry>>(StringComparer.Ordinal);
            foreach (var e in all)
            {
                if (f.From.HasValue && e.Date < f.From.Value.Date || f.To.HasValue && e.Date > f.To.Value.Date) continue;
                var modOk = !f.HiddenMods.Contains(e.Mod);
                var typeOk = f.Types[e.Type];
                if (modOk) v.TypeCounts[e.Type]++;
                if (typeOk) v.ModCounts[e.Mod] = (v.ModCounts.TryGetValue(e.Mod, out var n) ? n : 0) + 1;
                if (!modOk || !typeOk) continue;
                v.Shown++;
                var key = e.Date.Ticks + "\n" + e.Mod + "\n" + e.Version;
                if (!byKey.TryGetValue(key, out var g)) { byKey[key] = g = new List<ChangelogEntry>(); groups.Add(g); }
                g.Add(e);
            }
            // Entries come newest first (Parse), so groups do too; each group's header carries its date.
            foreach (var g in groups)
            {
                v.Rows.Add(new ChangelogRow(ChangelogRowKind.Mod, g[0]));
                foreach (var e in g) v.Rows.Add(new ChangelogRow(ChangelogRowKind.Line, e));
            }
            return v;
        }

        // Mod list search: case-insensitive substring, empty query = every mod (order kept).
        public static List<string> MatchMods(List<string> mods, string query)
        {
            query = (query ?? "").Trim();
            return query.Length == 0 ? mods : mods.FindAll(m => m.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // "All" / "None" over the given (searched) mods.
        public static void ShowMods(ChangelogFilter f, List<string> mods, bool show)
        {
            foreach (var m in mods) { if (show) f.HiddenMods.Remove(m); else f.HiddenMods.Add(m); }
        }

        // "only": just this mod visible; again on the same solo mod = every mod back.
        public static void Solo(ChangelogFilter f, List<string> mods, string mod)
        {
            var alreadySolo = !f.HiddenMods.Contains(mod) && f.HiddenMods.Count == mods.Count - 1;
            f.HiddenMods.Clear();
            if (!alreadySolo) foreach (var m in mods) if (m != mod) f.HiddenMods.Add(m);
        }

        public static bool TryParseDay(string s, out DateTime d) =>
            DateTime.TryParseExact((s ?? "").Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out d);

        public static string Day(DateTime d) => d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

        // Image.pixelsPerUnitMultiplier that draws a sliced sprite's cap (capPx border) as half the rect height: Unity draws
        // a sprite px as 1 / (imagePpu x multiplier) units (imagePpu = Image.pixelsPerUnit = sprite PPU / canvas refPpu).
        // 0 when the inputs are not usable (rect not laid out yet).
        public static float CapMultiplier(float capPx, float rectHeight, float imagePpu) =>
            capPx > 0f && rectHeight > 0f && imagePpu > 0f ? capPx / (rectHeight / 2f) / imagePpu : 0f;

        // "2 341" (mockup footer).
        public static string Count(int n) => n.ToString("#,0", CultureInfo.InvariantCulture).Replace(",", " ");
    }
}
