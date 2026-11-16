using System;
using Morgott.UI.InventoryScreen;

namespace Morgott.UI.CraftingScreen
{
    // Pure geometry of the crafting column (M3, mockup-1 / layout A right column; no Unity types: unit-tested on
    // net8). Logical canvas units of the InventoryGui canvas, origin top-left, y down, inside the crafting box that
    // InventoryMath gives (same top / bottom / gutters as dock and centre). Top-down: header (station icon | name
    // plaque | level badge), mode tab row (craft / upgrade / mod tabs), category row (CraftingFilter), its sub-group row (when shown), then two
    // columns: search + recipe list well left, detail well right (title, item icon, description, quality row,
    // requirement grid) with the button row (repair | craft) under it. Every count is an input (tabs, categories,
    // requirements); tokens give only sizes and gaps.
    public sealed class CraftingTokens
    {
        public float Pad = 18f, Gap = 8f;
        public float HeaderH = 80f, TabH = 36f, TabW = 150f, TabMinW = 96f, ArrowW = 28f;
        public float CategoryH = 44f, SubCategoryH = 44f, SearchH = 38f, NetworkH = 28f, ControlsH = 32f;
        // ListMinW: the list column never narrower than this while the detail keeps half the body (0.24.0 list at the
        // 576 window; layout A docks crafting at the room right of the hotbar-locked centre column, 536 at 16:9).
        public float ListShare = 0.4f, ListMinW = 204f, RowH = 46f, ScrollbarW = 10f;
        public float TitleH = 32f, CraftTypeH = 24f, IconH = 112f, IconMinH = 56f, QualityH = 44f, DescriptionMinH = 48f;
        // ReqMinH: the lowest cell whose count band (AmountBand of the height) still holds one amount line (16 x LineEm):
        // run 20261116-163644 shrank the cells to 82 and every "0/1" vanished.
        public float ReqW = 68f, ReqH = 88f, ReqMinW = 52f, ReqMaxRows = 2f, ReqMinH = 84f;
        // StatRowH: one body line (16 x LineEm), mockup-v2 rows sit line on line.
        public float ButtonH = 64f, StatRowH = 21f, ChainMinH = 72f;
        public float WellPad = 10f;
    }

    public sealed class CraftingInput
    {
        public Box Panel;
        // Content inset of the panel stack (corner-cap keep-out, canvas units); 0 = Pad only.
        public float CapInset;
        public float Scale = 1f;
        public int Tabs, Categories, Requirements;
        // CraftingFilter sub-group tabs (second row under the categories, MT_CraftFilter/Subs); 0 = no row.
        public int SubCategories;
        public bool Search, Repair, CraftType, Quality;
        // Create tab view controls (sort / direction / new filter / availability) under the search, full width (Core ICraftingAvailability).
        public bool Controls;
        // Lines of the detail stat table (food stats, yield, count, bonus chance); 0 = none. Reserved like the quality row.
        public int StatLines;
        // Height the RecipeChains "missing" box needs (canvas units, already capped; 0 = not shown): reserved above the
        // requirements so the box keeps its header, lines and hint (run 20261116-152659 collapsed it to a 25 px strip).
        public float ChainH;
        // Station network checkbox row under the mode tabs (at a station while Core IStationNetwork is registered).
        public bool Network;
        // First visible tab / category of a scrolled row (row arrows).
        public int TabOffset, CategoryOffset, SubCategoryOffset;
        // The shared window header band height (RuneWindowHeader, canvas units): > 0 = the header row is that band at the
        // panel's top edge (full width, like every window), the content starts under it; 0 = the padded header row.
        public float WindowHeaderH;
    }

    // A row of equal cells that scrolls when the count does not fit: Cells[i] empty = scrolled out.
    public sealed class RowFit
    {
        public Box[] Cells = Array.Empty<Box>();
        public Box Prev, Next;
        public int First, Visible;
        public bool Scroll;
    }

    public sealed class CraftingLayout
    {
        public Box Content, Header, StationIcon, Title, Level;
        public Box TabRow, NetworkRow, CategoryRow, SubCategoryRow, Search, Controls, List, ListView, ListScrollbar;
        public Box Detail, DetailTitle, CraftType, Icon, Description, Stats, Chain, Quality, Requirements, ButtonRow, Repair, Craft;
        public RowFit Tabs = new RowFit(), Categories = new RowFit(), SubCategories = new RowFit();
        public Box[] RequirementCells = Array.Empty<Box>();
        public int RequirementColumns, RequirementRows;
        public float RowPitch;

        public override string ToString() =>
            $"header {Header} tabs {TabRow}({Tabs.Visible}/{Tabs.Cells.Length}{(Tabs.Scroll ? " scroll" : "")}) cats {CategoryRow}({Categories.Visible}/{Categories.Cells.Length}) subs {SubCategoryRow}({SubCategories.Visible}/{SubCategories.Cells.Length}) " +
            $"list {List} detail {Detail} icon {Icon} desc {Description} stats {Stats} chain {Chain} req {Requirements} {RequirementColumns}x{RequirementRows} buttons {ButtonRow}";
    }

    // Craft row parts (canvas boxes): idle = Minus | Field | Plus | Button, progress = Bar | Cancel.
    public struct CraftRow
    {
        public Box Minus, Field, Plus, Button, Bar, Cancel;
    }

    // Multi-craft (stepper N / Shift x5) as a queue of N single vanilla crafts, each with its own timer, resources,
    // effects and skill raise. Stops on cancel and on a craft that made nothing (inventory full). Resources missing for
    // the next unit may be pending (Morgott.Patcher SafeTake counts another player's chest only once our ownership claim
    // lands, ~2 s): the unit waits up to PendingWait, then the queue ends as a real shortage.
    public sealed class CraftRun
    {
        public const float PendingWait = 3f;

        private float _waitUntil = float.NaN;

        public int Total { get; private set; }
        public int Done { get; private set; }
        public bool Running { get; private set; }
        // A unit waits for resources; the caller re-checks each tick.
        public bool Waiting { get; private set; }

        public void Start(int total)
        {
            Total = total;
            Done = 0;
            Running = total > 1;
            Waiting = false;
            _waitUntil = float.NaN;
        }

        // One craft of the queue ended; true = start the next one (or retry this one: refused = the craft made nothing
        // for want of resources, nothing was consumed, InventoryGui.DoCrafting :1805).
        public bool Finished(bool crafted, bool refused = false)
        {
            if (!Running) return false;
            if (crafted)
            {
                Done++;
                _waitUntil = float.NaN;
            }
            Running = (crafted || refused) && Done < Total;
            Waiting = false;
            return Running;
        }

        // The next unit lacks resources at `now`: true = keep waiting (bounded per unit), false = real shortage (caller stops).
        public bool Wait(float now)
        {
            if (!Running) return false;
            if (float.IsNaN(_waitUntil)) _waitUntil = now + PendingWait;
            Waiting = now < _waitUntil;
            return Waiting;
        }

        // The next unit was pressed; its wait budget lasts until a unit crafts.
        public void Pressed() => Waiting = false;

        public void Stop()
        {
            Running = false;
            Waiting = false;
        }

        // "2/5" while the second of five crafts runs.
        public string Step => Math.Min(Done + 1, Total) + "/" + Total;
    }

    public static class CraftingMath
    {
        // Recipe row roles (rows = clones of InventoryGui.m_recipeElementPrefab): the strip "bkg" follows canCraft, the
        // "selected" rim its own role, the row button's target graphic the row plate, everything else (icon, durability,
        // magic background) vanilla content; the row texts body sans.
        public const string RowTextRole = "text.body";

        public static string RowImageRole(string name, bool childOfRow, bool buttonTarget, bool canCraft)
        {
            if (childOfRow && name == "bkg") return canCraft ? "craft.row" : "craft.row.unavailable";
            if (childOfRow && name == "selected") return "craft.row.selected";
            return buttonTarget ? "craft.row" : "content";
        }

        public static string RowImageReason(string name, bool childOfRow, bool buttonTarget)
        {
            if (childOfRow && name == "bkg") return "recipe row strip (canCraft from m_availableRecipes)";
            if (childOfRow && name == "selected") return "recipe row selected rim (SetRecipe toggles it)";
            return buttonTarget ? "recipe row button" : "recipe row icon / durability / magic background";
        }

        // Requirement cell tooltip: the item's own tooltip, then the cell's previous text (Morgott.Patcher producer lines,
        // or vanilla's bare name) minus its first line, which only repeats the name (shown as the topic).
        public static string RequirementTip(string item, string chain)
        {
            var cut = chain == null ? -1 : chain.IndexOf('\n');
            var rest = cut < 0 ? "" : chain.Substring(cut + 1).Trim('\n', '\r');
            if (string.IsNullOrEmpty(item)) return rest;
            return rest.Length == 0 ? item : item.TrimEnd('\n', '\r') + "\n\n" + rest;
        }

        public static CraftingLayout Compute(CraftingInput i, CraftingTokens t)
        {
            var o = new CraftingLayout();
            var k = i.Scale > 0f ? i.Scale : 1f;
            float S(float v) => v * k;
            var gap = S(t.Gap);
            // Content: Pad from the edges, at least the window content inset (PanelDecor.KeepOut = CapInset) from the
            // left / right / bottom edges, so nothing runs under a corner cap (user 2026-10-26: the requirements well and
            // the craft button lay under the bottom corner straps with half the keep-out at the bottom only).
            var pad = S(t.Pad);
            var edge = Math.Max(pad, i.CapInset);
            var c = new Box(i.Panel.X + edge, i.Panel.Y + pad, Math.Max(0f, i.Panel.W - 2f * edge), Math.Max(0f, i.Panel.H - pad - edge));
            o.Content = c;
            var y = c.Y;

            // Header: station icon (square) | name plaque | level badge (square, a little smaller), clear of the caps.
            var hh = Math.Min(S(t.HeaderH), c.H * 0.14f);
            o.Header = new Box(c.X, y, c.W, hh); // c is clear of the caps sideways already
            if (i.WindowHeaderH > 0f)
            {
                // The one window header (RuneWindowHeader): the band at the panel top, content under it.
                hh = i.WindowHeaderH;
                y = i.Panel.Y;
                o.Header = new Box(i.Panel.X, y, i.Panel.W, hh);
                var contentTop = i.Panel.Y + Math.Max(hh + gap, edge);
                c = new Box(c.X, contentTop, c.W, Math.Max(0f, c.Bottom - contentTop));
                o.Content = c;
            }
            var badge = hh * 0.8f;
            o.StationIcon = new Box(o.Header.X, y, hh, hh);
            o.Level = new Box(o.Header.Right - badge, y + (hh - badge) / 2f, badge, badge);
            o.Title = new Box(o.StationIcon.Right + gap, y, Math.Max(0f, o.Level.X - gap - o.StationIcon.Right - gap), hh);
            y = i.WindowHeaderH > 0f ? c.Y : y + hh + gap;

            // Mode tabs (craft / upgrade / mod tabs): centred row, shrinks to TabMinW, then scrolls.
            if (i.Tabs > 0)
            {
                o.TabRow = new Box(c.X, y, c.W, S(t.TabH));
                o.Tabs = Row(o.TabRow, i.Tabs, S(t.TabW), S(t.TabMinW), gap, S(t.ArrowW), i.TabOffset, true);
                y += o.TabRow.H + gap;
            }
            if (i.Network)
            {
                o.NetworkRow = new Box(c.X, y, c.W, S(t.NetworkH));
                y += o.NetworkRow.H + gap;
            }
            // Category filter (CraftingFilter): square tabs, left-aligned, scrolls when they do not fit.
            if (i.Categories > 0)
            {
                var ch = S(t.CategoryH);
                o.CategoryRow = new Box(c.X, y, c.W, ch);
                o.Categories = Row(o.CategoryRow, i.Categories, ch, ch, gap * 0.5f, S(t.ArrowW), i.CategoryOffset, false);
                y += ch + gap;
            }
            // Sub-group row (weapon kinds, armor slots, ...): same square tabs and size as the categories, only while the filter
            // shows it (two or more groups); the body gives way.
            if (i.Categories > 0 && i.SubCategories > 0)
            {
                var sh = S(t.SubCategoryH);
                o.SubCategoryRow = new Box(c.X, y, c.W, sh);
                o.SubCategories = Row(o.SubCategoryRow, i.SubCategories, sh, sh, gap * 0.5f, S(t.ArrowW), i.SubCategoryOffset, false);
                y += sh + gap;
            }

            // Body: list column (search + list well) | detail column (detail well + button row). With the view controls
            // (approved mockup craftfilter mockup-v2): search at list width, then the controls row across both columns.
            var listW = (float)Math.Floor(Math.Max(c.W * Clamp(t.ListShare, 0.25f, 0.6f), Math.Min(S(t.ListMinW), c.W * 0.5f)));
            if (i.Search && i.Controls)
            {
                o.Search = new Box(c.X, y, listW, S(t.SearchH));
                o.Controls = new Box(c.X, o.Search.Bottom + gap, c.W, S(t.ControlsH));
                y = o.Controls.Bottom + gap;
            }
            var body = new Box(c.X, y, c.W, Math.Max(0f, c.Bottom - y));
            var lx = body.X;
            var ly = body.Y;
            if (i.Search && !i.Controls)
            {
                o.Search = new Box(lx, ly, listW, S(t.SearchH));
                ly += o.Search.H + gap;
            }
            o.List = new Box(lx, ly, listW, Math.Max(0f, body.Bottom - ly));
            var wp = S(t.WellPad) * 0.5f;
            var sb = S(t.ScrollbarW);
            o.ListScrollbar = new Box(o.List.Right - wp - sb, o.List.Y + wp, sb, Math.Max(0f, o.List.H - 2f * wp));
            o.ListView = new Box(o.List.X + wp, o.List.Y + wp, Math.Max(0f, o.List.W - 3f * wp - sb), Math.Max(0f, o.List.H - 2f * wp));
            o.RowPitch = S(t.RowH);

            var dx = o.List.Right + gap;
            var dw = Math.Max(0f, body.Right - dx);
            var bh = S(t.ButtonH);
            o.ButtonRow = new Box(dx, body.Bottom - bh, dw, bh);
            if (i.Repair)
            {
                o.Repair = new Box(dx, o.ButtonRow.Y, bh, bh);
                o.Craft = new Box(o.Repair.Right + gap, o.ButtonRow.Y, Math.Max(0f, dw - bh - gap), bh);
            }
            else o.Craft = o.ButtonRow;
            o.Detail = new Box(dx, body.Y, dw, Math.Max(0f, o.ButtonRow.Y - gap - body.Y));

            // Detail well, top-down: title, craft type line, icon; bottom-up: requirements, quality row; the
            // description takes what is left (never below its minimum: the icon shrinks first).
            var d = Inset(o.Detail, S(t.WellPad), S(t.WellPad));
            var top = d.Y;
            o.DetailTitle = new Box(d.X, top, d.W, S(t.TitleH));
            top += o.DetailTitle.H;
            if (i.CraftType)
            {
                o.CraftType = new Box(d.X, top, d.W, S(t.CraftTypeH));
                top += o.CraftType.H;
            }
            top += gap * 0.5f;
            var bottom = d.Bottom;
            // Height the grid may take while the title / type, the full icon, the description minimum and the quality row keep theirs.
            var statsH = i.StatLines > 0 ? S(t.StatRowH) * i.StatLines : 0f;
            // The grid may take what the title, a minimum icon, the description minimum, the quality row, the stat table and the
            // missing box leave (run 20261116-161831 at 1080p: two full rows of cells left the icon 0 units).
            var keep = (top - d.Y) + S(i.StatLines > 0 || i.ChainH > 0f ? t.IconMinH : t.IconH) + gap * 0.5f + S(t.DescriptionMinH) + gap + (i.Quality ? S(t.QualityH) + gap * 0.5f : 0f) +
                       (statsH > 0f ? statsH + gap * 0.5f : 0f) + (i.ChainH > 0f ? Math.Min(i.ChainH, S(t.ChainMinH)) + gap * 0.5f : 0f);
            Requirements(o, i.Requirements, new Box(d.X, d.Y, d.W, d.H), S(t.ReqW), S(t.ReqH), S(t.ReqMinW), (int)Math.Max(1f, t.ReqMaxRows), gap * 0.75f, Math.Max(0f, d.H - keep), S(t.ReqMinH));
            if (o.RequirementRows > 0)
            {
                var rh = o.Requirements.H;
                o.Requirements = new Box(d.X, bottom - rh, d.W, rh);
                for (var n = 0; n < o.RequirementCells.Length; n++)
                {
                    var cell = o.RequirementCells[n];
                    o.RequirementCells[n] = new Box(cell.X, cell.Y + o.Requirements.Y - d.Y, cell.W, cell.H);
                }
                bottom = o.Requirements.Y - gap;
            }
            if (i.Quality)
            {
                o.Quality = new Box(d.X, bottom - S(t.QualityH), d.W, S(t.QualityH));
                bottom = o.Quality.Y - gap * 0.5f;
            }
            if (i.ChainH > 0f)
            {
                // The missing box takes what the title, the description minimum and the stat table leave (the icon may go):
                // at least its header + a line + the hint (ChainMinH), its tree scrolls inside.
                // ... and the minimum icon (run 20261116-170414: the box took 88, the icon got 4).
                var free = bottom - top - S(t.IconMinH) - gap * 0.5f - S(t.DescriptionMinH) - gap - (statsH > 0f ? statsH + gap * 0.5f : 0f) - gap * 0.5f;
                var ch = Math.Min(i.ChainH, Math.Max(Math.Min(i.ChainH, S(t.ChainMinH)), free));
                o.Chain = new Box(d.X, bottom - ch, d.W, ch);
                bottom = o.Chain.Y - gap * 0.5f;
            }
            if (statsH > 0f)
            {
                o.Stats = new Box(d.X, bottom - statsH, d.W, statsH);
                bottom = o.Stats.Y - gap * 0.5f;
            }
            var room = Math.Max(0f, bottom - top);
            var icon = Math.Max(0f, Math.Min(S(t.IconH), room - S(t.DescriptionMinH) - gap * 0.5f));
            o.Icon = new Box(d.X + (d.W - icon) / 2f, top, icon, icon);
            var descTop = top + (icon > 0f ? icon + gap * 0.5f : 0f);
            o.Description = new Box(d.X, descTop, d.W, Math.Max(0f, bottom - descTop));
            return o;
        }

        // Requirement grid: N cells left-aligned, rows wrap; when more rows than maxRows would be needed the cells
        // shrink (aspect kept) down to minW so they fit maxRows; past that the rows grow (the description gives way).
        // roomH > 0: the height the grid may take without squeezing the icon / description minimum; full-size rows that
        // fit it come first (the cells shrink only past it: a 60-wide cell drops its two-line names under the 10 font
        // floor, run 20261026-222320 names-*).
        // Cell boxes relative to `area` (y from area.Y). Requirements.H = grid height.
        // minH: cells never get lower than this (their count band must hold the amount line); narrower cells keep it, so their
        // aspect may get taller (theme req 68 x 86: aspect-kept cells at 84 high were 66.4 wide and only 3 fit the 280-unit
        // detail of run 20261116-170414, 3 + 1 rows left the icon 4 units).
        public static void Requirements(CraftingLayout o, int n, Box area, float w, float h, float minW, int maxRows, float gap, float roomH = 0f, float minH = 0f)
        {
            o.RequirementCells = new Box[Math.Max(0, n)];
            o.RequirementColumns = o.RequirementRows = 0;
            o.Requirements = new Box(area.X, area.Y, area.W, 0f);
            if (n <= 0 || !(area.W > 0f)) return;
            var cols = Columns(area.W, w, gap);
            if (roomH > 0f) maxRows = Math.Max(maxRows, (int)Math.Floor((roomH + gap + 0.01f) / (h + gap)));
            if ((n + cols - 1) / cols > maxRows)
            {
                var want = (n + maxRows - 1) / maxRows;
                var fitW = (area.W - (want - 1) * gap) / want;
                var cw = Math.Max(minW, Math.Min(w, fitW));
                h = Math.Max(Math.Min(h, minH), h * cw / w);
                w = cw;
                cols = Columns(area.W, w, gap);
            }
            cols = Math.Min(cols, n);
            var rows = (n + cols - 1) / cols;
            // Still taller than the room: the cells shrink further (aspect kept, never under minW); narrower cells may also
            // take fewer rows.
            while (roomH > 0f && rows * h + (rows - 1) * gap > roomH + 0.01f && w > minW + 0.01f)
            {
                var nw = Math.Max(minW, w - 1f);
                h = Math.Max(Math.Min(h, minH), h * nw / w);
                w = nw;
                cols = Math.Min(Columns(area.W, w, gap), n);
                rows = (n + cols - 1) / cols;
            }
            o.RequirementColumns = cols;
            o.RequirementRows = rows;
            for (var i = 0; i < n; i++)
            {
                var col = i % cols;
                var row = i / cols;
                o.RequirementCells[i] = new Box(area.X + col * (w + gap), area.Y + row * (h + gap), w, h);
            }
            o.Requirements = new Box(area.X, area.Y, area.W, rows * h + (rows - 1) * gap);
        }

        // One scrollable row of `count` cells of width w (shrinking to minW) in `row`. centred: the row is centred
        // when it fits. Scrolled: arrows at both ends, `offset` = first visible cell (clamped).
        public static RowFit Row(Box row, int count, float w, float minW, float gap, float arrowW, int offset, bool centred)
        {
            var f = new RowFit { Cells = new Box[Math.Max(0, count)] };
            if (count <= 0 || !(row.W > 0f)) return f;
            var need = count * w + (count - 1) * gap;
            if (need > row.W)
            {
                w = Math.Max(minW, (row.W - (count - 1) * gap) / count);
                need = count * w + (count - 1) * gap;
            }
            if (need <= row.W + 0.01f)
            {
                var x = centred ? row.X + (row.W - need) / 2f : row.X;
                for (var i = 0; i < count; i++) f.Cells[i] = new Box(x + i * (w + gap), row.Y, w, row.H);
                f.Visible = count;
                return f;
            }
            f.Scroll = true;
            f.Prev = new Box(row.X, row.Y, arrowW, row.H);
            f.Next = new Box(row.Right - arrowW, row.Y, arrowW, row.H);
            var inner = row.W - 2f * (arrowW + gap);
            var visible = Math.Max(1, (int)Math.Floor((inner + gap) / (w + gap)));
            f.Visible = Math.Min(visible, count);
            f.First = Math.Max(0, Math.Min(offset, count - f.Visible));
            var x0 = row.X + arrowW + gap + (inner - (f.Visible * w + (f.Visible - 1) * gap)) / 2f;
            for (var i = f.First; i < f.First + f.Visible; i++) f.Cells[i] = new Box(x0 + (i - f.First) * (w + gap), row.Y, w, row.H);
            return f;
        }

        // RecipeChains box height cap: its own height, at most `max` and at most the room between `top` (description
        // minimum) and the box bottom, never under `min` (a header line + a line of tree).
        public static float ChainHeight(float height, float bottom, float top, float max, float min)
        {
            var cap = Math.Max(min, Math.Min(max, bottom - top));
            return Math.Min(height, cap);
        }

        // The craft row (o.Craft) split for its two states (approved mockup craft-progress.png). Idle: amount stepper
        // [-][N][+] at the left (step buttons stepShare * h wide, field fieldShare * h wide, all stepH * h tall, centred,
        // innerShare * gap apart;
        // the whole stepper at most maxShare of the row, shrunk evenly below that), the craft button takes the rest.
        // Progress: framed bar | cancel button (cancelShare of the row, clamped to [h * 1.2, h * 2.6]). Everything inside
        // `craft`; stepper == false -> the button gets the whole row.
        // maxShare 0.32 (0.4 -> 0.35 -> 0.32): the live craft row is 257 x 64 units, and RU "СОЗДАТЬ X 99" (6.46 em PT Sans
        // Bold) at the 16 px floor ellipsized in the label span (anvil-clear-*, runs 20261108-022445 / -044404).
        public static CraftRow ActionRow(Box craft, bool stepper, float gap, float stepShare = 0.68f, float fieldShare = 0.85f,
            float maxShare = 0.32f, float cancelShare = 0.27f, float stepH = 0.95f, float innerShare = 0.35f)
        {
            var r = new CraftRow { Button = craft };
            if (craft.IsEmpty) return r;
            var h = craft.H;
            var inner = gap * Clamp(innerShare, 0f, 2f);
            if (stepper)
            {
                var step = h * stepShare;
                var field = h * fieldShare;
                var sh = h * Clamp(stepH, 0.3f, 1f);
                var need = 2f * step + field + 2f * inner;
                var max = craft.W * Clamp(maxShare, 0.1f, 0.6f);
                if (need > max)
                {
                    var k = max / need;
                    step *= k;
                    field *= k;
                    inner *= k;
                }
                var y = craft.Y + (h - sh) / 2f;
                r.Minus = new Box(craft.X, y, step, sh);
                r.Field = new Box(r.Minus.Right + inner, y, field, sh);
                r.Plus = new Box(r.Field.Right + inner, y, step, sh);
                r.Button = new Box(r.Plus.Right + gap, craft.Y, Math.Max(0f, craft.Right - r.Plus.Right - gap), h);
            }
            var cw = Clamp(craft.W * cancelShare, h * 1.2f, h * 2.6f);
            cw = Math.Min(cw, craft.W * 0.45f);
            r.Cancel = new Box(craft.Right - cw, craft.Y, cw, h);
            r.Bar = new Box(craft.X, craft.Y, Math.Max(0f, r.Cancel.X - gap - craft.X), h);
            return r;
        }

        // Craft button lead glyph (anvil / upgrade): a square of size x h, pad x h after the button's left edge, centred
        // vertically. Depends on the button only, never on the label (the label's span is laid out after it).
        public static Box LeadGlyph(Box button, float pad, float size)
        {
            var g = button.H * Clamp(size, 0.1f, 1f);
            return new Box(button.X + button.H * Clamp(pad, 0f, 1f), button.Y + (button.H - g) / 2f, g, g);
        }

        // Craft amount: clamp to [1, max] (max < 1 -> 1); typed text parsed as digits only (empty / junk -> fallback).
        public static int ClampAmount(int n, int max) => Math.Max(1, Math.Min(n, Math.Max(1, max)));

        public static int ParseAmount(string s, int fallback, int max)
        {
            var i = 0;
            if (string.IsNullOrEmpty(s) || !Number(s, ref i, out var n) || !End(s, i)) return ClampAmount(fallback, max);
            return ClampAmount(n, max);
        }

        // Controls row split (mockup-v2): sort dropdown at the list column width | direction square | filter button
        // (1.4 squares) | availability segments take the rest.
        public static (Box Sort, Box Direction, Box Filter, Box Show) ControlRow(Box row, float sortW, float gap)
        {
            var h = row.H;
            var sort = new Box(row.X, row.Y, Math.Max(0f, Math.Min(sortW, row.W - 2.4f * h - 3f * gap)), h);
            var dir = new Box(sort.Right + gap, row.Y, h, h);
            var filter = new Box(dir.Right + gap, row.Y, h * 1.4f, h);
            return (sort, dir, filter, new Box(filter.Right + gap, row.Y, Math.Max(0f, row.Right - filter.Right - gap), h));
        }

        // Row / detail count: "x6", "x99+" at the cap (the count search stops there).
        // Line height of the body face in em (PT Sans: ascender + descender + gap ~1.3): a text box shorter than size x
        // LineEm holds no line.
        public const float LineEm = 1.3f;

        // Share of a requirement cell's height its count band takes (CraftingSkin.StyleSocket res_amount 0.02-0.27).
        public const float AmountBand = 0.25f;

        public static string CountLabel(int count, int cap) => "\u00D7" + Math.Max(0, count) + (count >= cap ? "+" : "");

        // Active filters on the funnel badge: availability other than All, plus "new only".
        public static int ActiveFilters(int show, bool newOnly) => (show != 0 ? 1 : 0) + (newOnly ? 1 : 0);

        // Bonus portion chance per craft in whole percent (InventoryGui.DoCrafting :1810-1818: skill factor x
        // m_craftBonusChance, stackable output at a station with a crafting skill only); 0 = no bonus line.
        public static int BonusPercent(float skillFactor, float bonusChance, int maxStack, bool stationSkill) =>
            stationSkill && maxStack > 1 ? (int)Math.Round(100f * Clamp(skillFactor * bonusChance, 0f, 1f)) : 0;
        private static int Columns(float width, float cell, float gap) => cell <= 0f ? 1 : Math.Max(1, (int)Math.Floor((width + gap) / (cell + gap)));

        private static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;

        public static Box Inset(Box b, float x, float y) => new Box(b.X + x, b.Y + y, Math.Max(0f, b.W - 2f * x), Math.Max(0f, b.H - 2f * y));

        // Station level digit size as a share of the star badge side: the digit (cap height ~0.7 em) sits in the star's
        // round recess (badge_level_star: ~0.47 of the side, centred).
        public const float LevelFontShare = 0.55f;

        // Station level star in the header aux slot: `slotH` tall by default, grown so its digit is at least `floorUnits`
        // (the readable number floor in canvas units) - the shared window header (size.win.header) made the slot so small
        // at 720p that no digit line fitted. Square, centred on `slot`; `side` is also the aux width to reserve.
        public static Box LevelBadge(Box slot, float floorUnits, out float fontSize)
        {
            var side = LevelSide(slot.H, floorUnits);
            fontSize = side * LevelFontShare;
            return new Box(slot.X + (slot.W - side) / 2f, slot.Y + (slot.H - side) / 2f, side, side);
        }

        // Side of the level star for a `slotH` slot and a `floorUnits` digit floor (the aux width the header reserves).
        public static float LevelSide(float slotH, float floorUnits) => Math.Max(Math.Max(0f, slotH), Math.Max(0f, floorUnits) / LevelFontShare);

        // Requirement amount "have/need" (ValheimPlus CraftFromChest rewrites the label so; vanilla writes only "need"),
        // read without substrings (this runs per requirement cell per frame). Spaces around the numbers allowed.
        public static bool TryParseHaveNeed(string s, out int have, out int need)
        {
            have = need = 0;
            if (string.IsNullOrEmpty(s)) return false;
            var i = 0;
            return Number(s, ref i, out have) && Skip(s, ref i, '/') && Number(s, ref i, out need) && End(s, i);
        }

        private static bool Number(string s, ref int i, out int value)
        {
            value = 0;
            while (i < s.Length && s[i] == ' ') i++;
            var start = i;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9')
            {
                if (value > 99999999) return false;
                value = value * 10 + (s[i] - '0');
                i++;
            }
            while (i < s.Length && s[i] == ' ') i++;
            return i > start && s[start] >= '0' && s[start] <= '9';
        }

        private static bool Skip(string s, ref int i, char c)
        {
            if (i >= s.Length || s[i] != c) return false;
            i++;
            return true;
        }

        private static bool End(string s, int i) => i == s.Length;

        // Craftability, the one rule for recipe rows, requirement cells and the Create button: Have = enough stock now
        // (vanilla HaveRequirements / the cell's have >= need, free crafting included), Chain = Create crafts it through its
        // missing intermediates (Core IChainCraft, Morgott.Patcher RecipeChains), Missing = neither.
        public enum Stock { Have, Chain, Missing }

        public static Stock StockOf(bool enough, bool chain) => enough ? Stock.Have : chain ? Stock.Chain : Stock.Missing;

        // Requirement cell: green count = held, white = craftable through the chain, grey = neither (socket greyed too).
        public static string CellTextColor(Stock s) =>
            s == Stock.Have ? "color.text.ok" : s == Stock.Chain ? "color.text.chain" : "color.text.unavailable";

        public static string SocketRole(Stock s) => s == Stock.Missing ? "craft.socket.missing" : "craft.socket";

        // Crafts of a producer yielding `perCraft` each that cover `shortBy` missing units.
        public static int CraftsFor(int shortBy, int perCraft) => shortBy <= 0 ? 0 : (shortBy + Math.Max(1, perCraft) - 1) / Math.Max(1, perCraft);
    }
}
