using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Morgott.UI.Classify;
using Morgott.UI.Restyle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Morgott.UI.Diagnostics
{
    public sealed class ReportRow
    {
        internal Transform _root;
        private string _path;
        private string _component;
        private string _detail;

        public string Path
        {
            get => _path ?? (_path = Graphic ? SkinDiagnostics.PathOf(Graphic.transform, _root) : "(destroyed)");
            set => _path = value;
        }

        public string Component
        {
            get => _component ?? (_component = Graphic ? Graphic.GetType().Name : "");
            set => _component = value;
        }

        public string Detail
        {
            get => _detail ?? (_detail = Graphic ? SkinDiagnostics.Describe(Graphic) : "");
            set => _detail = value;
        }

        public Classification Result;
        public Graphic Graphic; // live reference (coverage scan)
    }

    // One classification pass over a root.
    public sealed class ScreenReport
    {
        public readonly string ScreenId;
        public readonly Transform Root;
        public readonly List<ReportRow> Rows = new List<ReportRow>();
        public readonly int[] Counts = new int[Enum.GetValues(typeof(Outcome)).Length];

        public ScreenReport(string screenId, Transform root)
        {
            ScreenId = screenId;
            Root = root;
        }

        public int Count(Outcome o) => Counts[(int)o];

        public void Add(Graphic g, Transform root, Classification c)
        {
            Counts[(int)c.Outcome]++;
            Rows.Add(Row(g, root, c));
        }

        private static ReportRow Row(Graphic g, Transform root, Classification c) =>
            new ReportRow { Graphic = g, _root = root, Result = c };

        private Dictionary<Graphic, int> _index;
        private int _prune = 256;

        // A row of a subtree the family restyled after this pass (Restyler.RestyleSubtree: cells / recipe rows vanilla
        // re-instantiated). Content rows carry no SkinMark, so without their row the coverage scan saw a rebuilt cell's
        // item icon as never classified (run 20261029-105944, InventoryElement(Clone)/icon). A graphic already in the
        // report has its row replaced (counts follow); rows of destroyed graphics leave once the list doubled (subtrees
        // rebuild on every open: no unbounded growth, no pinned dead graphics).
        public void Append(Graphic g, Transform root, Classification c)
        {
            if (_index == null)
            {
                _index = new Dictionary<Graphic, int>(Rows.Count);
                for (var i = 0; i < Rows.Count; i++) if (Rows[i].Graphic) _index[Rows[i].Graphic] = i;
            }
            if (_index.TryGetValue(g, out var at))
            {
                Counts[(int)Rows[at].Result.Outcome]--;
                Counts[(int)c.Outcome]++;
                Rows[at] = Row(g, root, c);
                return;
            }
            if (Rows.Count >= _prune)
            {
                Rows.RemoveAll(r => !r.Graphic);
                Array.Clear(Counts, 0, Counts.Length);
                _index.Clear();
                for (var i = 0; i < Rows.Count; i++)
                {
                    Counts[(int)Rows[i].Result.Outcome]++;
                    _index[Rows[i].Graphic] = i;
                }
                _prune = Math.Max(256, Rows.Count * 2);
            }
            _index[g] = Rows.Count;
            Add(g, root, c);
        }
        public string Summary =>
            $"{ScreenId}: graphics={Rows.Count.ToString()} styled={Count(Outcome.Styled).ToString()} degraded={Count(Outcome.Degraded).ToString()} content={Count(Outcome.Content).ToString()} skipped={Count(Outcome.Skipped).ToString()} unknown={Count(Outcome.Unknown).ToString()}";
    }

    // Diagnostics: per-screen classification counts, unexplained (Unknown) rows, theme/asset problems, and on dump the
    // live checks (text below the readable floor, rects off the safe area). Dumps go to BepInEx\RuneSkin\diag.
    public static class SkinDiagnostics
    {
        private static readonly Dictionary<string, ScreenReport> Last = new Dictionary<string, ScreenReport>();
        private static readonly List<string> ThemeIssues = new List<string>();
        private static readonly HashSet<string> WarnedUnknown = new HashSet<string>();
        private static readonly HashSet<string> NotedDegraded = new HashSet<string>();
        private static readonly Dictionary<string, string> LastSummary = new Dictionary<string, string>();

        public static string DumpDir => Path.Combine(BepInEx.Paths.BepInExRootPath, "Morgott.UI", "diag");
        public static IReadOnlyList<string> ThemeProblems => ThemeIssues;

        // Caps (perf audit B1): every theme reload re-reports its issues and every unknown path warns once; neither may
        // grow without bound over a long session.
        public const int MaxThemeIssues = 256, MaxWarnedUnknown = 2048;

        public static void Theme(string message)
        {
            Bounded.AddCapped(ThemeIssues, message, MaxThemeIssues);
            Plugin.Log?.LogWarning($"[Morgott.UI] theme: {message}");
        }

        public static void Note(string message) => Plugin.Log?.LogInfo($"[Morgott.UI] {message}");

        public static void Record(ScreenReport report)
        {
            Last[report.ScreenId] = report;
            // One line per change of the counts (re-shows of an unchanged screen stay quiet).
            if (Plugin.Verbose)
            {
                var summary = report.Summary;
                if (!LastSummary.TryGetValue(report.ScreenId, out var prev) || prev != summary)
                {
                    LastSummary[report.ScreenId] = summary;
                    if (Plugin.Verbose) Plugin.Log?.LogInfo($"[Morgott.UI] diag {summary}");
                }
            }
            var checkDegraded = Plugin.Verbose && report.Count(Outcome.Degraded) > 0;
            var checkUnknown = report.Count(Outcome.Unknown) > 0;
            if (!checkDegraded && !checkUnknown) return;
            foreach (var row in report.Rows)
                // A degraded row of an early pass is gone from the dump once a later pass styles it (UnifiedPopup Awake).
                if (checkDegraded && row.Result.Outcome == Outcome.Degraded && Bounded.AddCapped(NotedDegraded, report.ScreenId + "|" + row.Path, MaxWarnedUnknown))
                    Plugin.Log?.LogInfo($"[Morgott.UI] degraded {report.ScreenId}: {row.Path} [{row.Component} {row.Detail}] {row.Result.Reason}");
                else if (checkUnknown && row.Result.Outcome == Outcome.Unknown && Bounded.AddCapped(WarnedUnknown, report.ScreenId + "|" + row.Path, MaxWarnedUnknown))
                    Plugin.Log?.LogWarning($"[Morgott.UI] unknown {report.ScreenId}: {row.Path} [{row.Component} {row.Detail}] {row.Result.Reason}");
        }

        public static ScreenReport Get(string screenId) => Last.TryGetValue(screenId, out var r) ? r : null;

        public static IEnumerable<string> Screens => Last.Keys;

        public static IEnumerable<ScreenReport> Reports => Last.Values;

        internal static Dictionary<string, ScreenReport>.ValueCollection ReportValues => Last.Values;

        internal static string OutcomeName(Outcome o) => o switch
        {
            Outcome.Styled => "Styled",
            Outcome.Degraded => "Degraded",
            Outcome.Content => "Content",
            Outcome.Skipped => "Skipped",
            Outcome.Unknown => "Unknown",
            _ => ((int)o).ToString(),
        };

        private static string ImageTypeName(Image.Type t) => t switch
        {
            Image.Type.Simple => "Simple",
            Image.Type.Sliced => "Sliced",
            Image.Type.Tiled => "Tiled",
            Image.Type.Filled => "Filled",
            _ => ((int)t).ToString(),
        };

        // Writes one file per screen (or all); returns the paths. Also logs the summary with live checks.
        public static List<string> Dump(string screenId = null)
        {
            var written = new List<string>();
            Directory.CreateDirectory(DumpDir);
            var targets = new List<ScreenReport>(Last.Count);
            foreach (var r in Last.Values)
                if (screenId == null || r.ScreenId == screenId) targets.Add(r);
            for (var i = 0; i < targets.Count; i++)
            {
                var report = targets[i];
                var sb = new StringBuilder();
                sb.AppendLine($"# {report.Summary}");
                sb.AppendLine($"# theme '{Theme_Name()}' rev {Morgott.UI.Theme.ThemeTokens.Revision.ToString()} bundle={(Morgott.UI.Theme.ThemeTokens.BundleLoaded ? "loaded" : "missing")}");
                foreach (var issue in ThemeIssues) sb.AppendLine($"# theme-issue {issue}");
                var floor = Morgott.UI.Theme.ThemeTokens.GetFloat("floor.text.body.px", 16f);
                int belowFloor = 0, offSafe = 0;
                var safe = Screen.safeArea;
                foreach (var row in report.Rows)
                    sb.AppendLine($"{OutcomeName(row.Result.Outcome),-8} {row.Result.Role ?? "-",-14} {row.Path} | {row.Component} {row.Detail} | {row.Result.Reason}");
                if (report.Root)
                {
                    foreach (var text in report.Root.GetComponentsInChildren<TMP_Text>(false))
                    {
                        var px = PhysicalSize(text);
                        var min = text.TryGetComponent<Morgott.UI.PlayerHud.TextFloor>(out var own) ? own.Px : floor;
                        if (px > 0f && px < min - 0.05f && !string.IsNullOrEmpty(text.text))
                        {
                            belowFloor++;
                            sb.AppendLine($"FLOOR    {PathOf(text.transform, report.Root)} {px.ToString("0.0")}px < {min.ToString()}px");
                        }
                    }
                    var corners = new Vector3[4];
                    foreach (var g in report.Root.GetComponentsInChildren<Graphic>(false))
                    {
                        if (!g.canvas || g.canvas.renderMode != RenderMode.ScreenSpaceOverlay || !g.enabled || g.color.a <= 0f) continue;
                        g.rectTransform.GetWorldCorners(corners);
                        if (corners[0].x < safe.xMin - 1 || corners[0].y < safe.yMin - 1 || corners[2].x > safe.xMax + 1 || corners[2].y > safe.yMax + 1)
                        {
                            // Full-screen scrims/blur may cover the whole screen; only report content that leaves it.
                            if (g.rectTransform.rect.width * g.canvas.scaleFactor >= Screen.width * 0.98f) continue;
                            offSafe++;
                            sb.AppendLine($"SAFEAREA {PathOf(g.transform, report.Root)} ({corners[0].x.ToString("0")},{corners[0].y.ToString("0")})-({corners[2].x.ToString("0")},{corners[2].y.ToString("0")}) safe (x:{safe.x.ToString("F2")}, y:{safe.y.ToString("F2")}, width:{safe.width.ToString("F2")}, height:{safe.height.ToString("F2")})");
                        }
                    }
                }
                sb.AppendLine($"# checks belowFloor={belowFloor.ToString()} offSafeArea={offSafe.ToString()}");
                var path = Path.Combine(DumpDir, Sanitize(report.ScreenId) + ".txt");
                File.WriteAllText(path, sb.ToString());
                written.Add(path);
                Plugin.Log?.LogInfo($"[Morgott.UI] diag dump {report.Summary} belowFloor={belowFloor.ToString()} offSafeArea={offSafe.ToString()} -> {path}");
            }
            return written;
        }

        private static string Theme_Name() => Morgott.UI.Theme.ThemeTokens.ThemeName;

        // Rendered glyph size in physical pixels: font size x canvas scale x accumulated local scale.
        public static float PhysicalSize(TMP_Text text)
        {
            var canvas = text.canvas;
            if (!canvas) return 0f;
            var size = text.fontSize; // the auto-sized result when auto sizing is on
            var root = canvas.rootCanvas.transform;
            var scale = text.transform.lossyScale.y / Mathf.Max(1e-6f, root.lossyScale.y);
            return size * scale * canvas.rootCanvas.scaleFactor;
        }

        private static readonly List<string> PathParts = new List<string>(16);

        public static string PathOf(Transform t, Transform root)
        {
            if (!t) return "";
            PathParts.Clear();
            for (var p = t; p; p = p.parent)
            {
                PathParts.Add(p.name);
                if (p == root) break;
            }
            PathParts.Reverse();
            var joined = string.Join("/", PathParts);
            PathParts.Clear();
            return joined;
        }

        public static string Describe(Graphic g)
        {
            switch (g)
            {
                case Image i:
                    var mark = g.GetComponent<SkinMark>();
                    var sprite = mark && mark.OriginalSprite != null ? mark.OriginalSprite : i.sprite ? i.sprite.name : "-";
                    var geo = "";
                    if (i.sprite && (i.type == Image.Type.Sliced || i.type == Image.Type.Tiled))
                    {
                        // Live 9-slice data: border in sprite px, drawn border = border x refPPU / (PPU x multiplier).
                        var b = i.sprite.border;
                        var c = i.canvas;
                        var r = i.rectTransform.rect;
                        geo = $" type={ImageTypeName(i.type)} border={b.x.ToString("0")}/{b.y.ToString("0")}/{b.z.ToString("0")}/{b.w.ToString("0")} ppu={i.sprite.pixelsPerUnit.ToString("0.#")} mult={i.pixelsPerUnitMultiplier.ToString("0.###")}" +
                              $" refPpu={(c ? c.referencePixelsPerUnit : 0f).ToString("0.#")} rootRefPpu={(c ? c.rootCanvas.referencePixelsPerUnit : 0f).ToString("0.#")}" +
                              $" scaleFactor={(c ? c.scaleFactor : 0f).ToString("0.###")} rect={r.width.ToString("0.#")}x{r.height.ToString("0.#")}";
                    }
                    return $"spr={sprite} active={g.gameObject.activeInHierarchy.ToString()}{geo}";
                case TMP_Text t:
                    return $"font={(t.font ? t.font.name : "-")} size={t.fontSize.ToString("0.#")} active={g.gameObject.activeInHierarchy.ToString()}";
                default:
                    return $"active={g.gameObject.activeInHierarchy.ToString()}";
            }
        }

        private static string Sanitize(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }
    }
}
