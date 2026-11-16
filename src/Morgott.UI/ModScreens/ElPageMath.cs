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

        // Trader body (mockup 2026-11-16 trader-ui-mockup-v2, 1920x1080): store column x 48..620 of the 48..1872 body,
        // the mod board from 636 (16 gap); without a board the store column is centred. Board: Тайник across the top
        // (y 210..610 of 210..1040), Лотерея | Карты сокровищ | Задания in three equal cells below.
        public const float StoreShare = 572f / 1824f, BoardGap = 16f / 1824f, StashShare = 400f / 830f;

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

        // Board cell i (gap g): 0 = Тайник across the top, 1..3 = the bottom row left to right.
        public static Box BoardCell(Box area, int i, float g)
        {
            var top = new Box(area.X, area.Y, area.W, area.H * StashShare);
            if (i == 0) return top;
            var y = top.Bottom + g;
            var w = (area.W - 2f * g) / 3f;
            return new Box(area.X + (i - 1) * (w + g), y, w, Math.Max(0f, area.Bottom - y));
        }

        // A section column inside its cell (inner pad p): header strip on top, action row at the bottom, the list between
        // them (gap g).
        public static void Section(Box cell, float p, float header, float action, float g, out Box head, out Box list, out Box act)
        {
            head = new Box(cell.X + p, cell.Y + p, cell.W - 2f * p, header);
            act = new Box(head.X, cell.Bottom - p - action, head.W, action);
            var y = head.Bottom + g;
            list = new Box(head.X, y, head.W, Math.Max(0f, act.Y - g - y));
        }

        // A box w wide centred in row (never wider than it).
        public static Box Centre(Box row, float w)
        {
            w = Math.Min(w, row.W);
            return new Box(row.X + (row.W - w) / 2f, row.Y, w, row.H);
        }
    }
}
