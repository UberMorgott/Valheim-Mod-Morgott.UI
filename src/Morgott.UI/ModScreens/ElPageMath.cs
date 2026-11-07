using System;
using Morgott.UI.InventoryScreen;

namespace Morgott.UI.ModScreens
{
    // Pure layout of the full-screen mod pages (unit-tested), canvas units, top-left origin:
    // - Enchanting table (ElPage, mockup 2026-11-04/02): top-bar tab row; body = list column + one framed pane.
    // - Trader (TraderPage, mockup 2026-11-07/04): vanilla store column left, Epic Loot board / temper panel right.
    public static class ElPageMath
    {
        // Mockup 02 at 1920x1080 (u = canvas height / 1080): tab caps 85 x 66 from x 109, pitch 104; list column 61..543 of
        // the 1799-wide content box (its scrollbar at 574), pane from 612.
        public const float TabW = 85f, TabH = 66f, TabGap = 19f, TabX = 109f, TabY = 27f;
        public const float ListShare = 513f / 1799f, PaneGap = 38f / 1799f;

        // Tab cap i of the top-bar row.
        public static Box Tab(int i, float u) => new Box((TabX + i * (TabW + TabGap)) * u, TabY * u, TabW * u, TabH * u);

        // The row of n caps.
        public static Box TabRow(int n, float u) => new Box(TabX * u, TabY * u, Math.Max(0, n) * TabW * u + Math.Max(0, n - 1) * TabGap * u, TabH * u);

        // Body split: list column + pane; without a list the pane takes the whole box.
        public static void Split(Box body, bool list, out Box column, out Box pane)
        {
            if (!list)
            {
                column = default;
                pane = body;
                return;
            }
            var w = body.W * ListShare;
            column = new Box(body.X, body.Y, w, body.H);
            var x = body.X + w + body.W * PaneGap;
            pane = new Box(x, body.Y, Math.Max(0f, body.Right - x), body.H);
        }

        // One scale k that fits a group's bounding box (bw x bh) into area, top-aligned, centred horizontally (or left-aligned:
        // mockup 02 pane content starts at the pane's left edge); (x0, y0) =
        // where the bbox's top-left lands. A part at (px, py) relative to the bbox goes to (x0 + px * k, y0 + py * k).
        public static float Fit(float bw, float bh, Box area, out float x0, out float y0, bool left = false)
        {
            var k = bw > 0f && bh > 0f ? Math.Min(area.W / bw, area.H / bh) : 1f;
            if (k <= 0f) k = 1f;
            x0 = left ? area.X : area.X + (area.W - bw * k) / 2f;
            y0 = area.Y;
            return k;
        }

        // A mod panel placed by its visible content (content0 = union of its widgets, whole0 = the panel's own box, both in
        // the prefab layout): the content fits area (Fit, top-aligned, centred), the panel goes where that puts it. Hildir's
        // temper panel box runs ~95 px above its first widget (run 20261107-154027).
        public static float AlignContent(Box whole0, Box content0, Box area, out float x, out float y)
        {
            var k = Fit(content0.W, content0.H, area, out var cx, out var cy);
            x = cx - (content0.X - whole0.X) * k;
            y = cy - (content0.Y - whole0.Y) * k;
            return k;
        }

        // Trader body: store column ~30 % (mockup 04 x 40..617 of 1840) left, the mod board right; without a board the
        // store column is centred.
        public const float StoreShare = 0.3f, BoardGap = 0.016f;

        public static void Trader(Box body, bool board, out Box store, out Box right)
        {
            var w = body.W * StoreShare;
            if (!board)
            {
                store = new Box(body.X + (body.W - w) / 2f, body.Y, w, body.H);
                right = default;
                return;
            }
            store = new Box(body.X, body.Y, w, body.H);
            var x = store.Right + body.W * BoardGap;
            right = new Box(x, body.Y, Math.Max(0f, body.Right - x), body.H);
        }

        // Cell (col, row) of a 2 x 2 grid in area with gap g.
        public static Box Cell(Box area, int col, int row, float g)
        {
            var w = (area.W - g) / 2f;
            var h = (area.H - g) / 2f;
            return new Box(area.X + col * (w + g), area.Y + row * (h + g), w, h);
        }
    }
}
