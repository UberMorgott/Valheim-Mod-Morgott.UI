using System;
using System.Collections.Generic;
using System.Globalization;
using Morgott.Core;
using UnityEngine;

namespace Morgott.UI.MapScreen
{
    public readonly struct QuestPinInput
    {
        public readonly Minimap.PinType Type;
        public readonly string Name;
        public readonly Vector3 Position;

        public QuestPinInput(Minimap.PinType type, string name, Vector3 position)
        {
            Type = type;
            Name = name ?? "";
            Position = position;
        }
    }

    public struct QuestRow
    {
        public string Id;
        public MapQuestKind Kind;
        public string Title;
        public string Subtitle;
        public Vector3 Position;
        public float Radius;
        public float Distance;
        public bool IsBossGroup;
    }

    public readonly struct QuestRowLayout
    {
        public readonly float RowHeight, CardHeight;
        public readonly float BadgeY, BadgeH, BadgeFont;
        public readonly float TitleY, TitleH, TitleFont;
        public readonly float SubY, SubH, SubFont;

        public QuestRowLayout(float rowHeight, float cardHeight, float badgeY, float badgeH, float badgeFont,
            float titleY, float titleH, float titleFont, float subY, float subH, float subFont)
        {
            RowHeight = rowHeight;
            CardHeight = cardHeight;
            BadgeY = badgeY;
            BadgeH = badgeH;
            BadgeFont = badgeFont;
            TitleY = titleY;
            TitleH = titleH;
            TitleFont = titleFont;
            SubY = subY;
            SubH = subH;
            SubFont = subFont;
        }
    }

    // Pure logic for the left map sidebar's ЗАДАНИЯ tab (linked into Morgott.UI.Tests):
    // merges Core MapQuests objectives with vanilla Hildir1/2/3 and Boss pins, deduplicating Hildir pins
    // within 30m of an active EpicLoot Hildir contract, skipping completed vanilla Hildir dungeons (global key
    // hildir1/2/3), computes live XZ distance from the player, filters by search query, and groups NPC quests above Known Bosses.
    public static class MapQuestSidebarMath
    {
        public const float DedupeRadius = 30f;

        // Computes quest card vertical geometry so TMP Ellipsis never hides title/subtitle lines at any theme font size.
        public static QuestRowLayout ComputeRowLayout(float legendFont, float biomeFont)
        {
            var badgeFont = Mathf.Clamp(biomeFont - 4f, 12f, 15f);
            var titleFont = Mathf.Clamp(legendFont - 2f, 15f, 20f);
            var subFont = Mathf.Clamp(biomeFont - 3f, 13f, 17f);
            var badgeH = Mathf.Max(20f, Mathf.Ceil(badgeFont * 1.45f));
            var titleH = Mathf.Max(26f, Mathf.Ceil(titleFont * 1.45f));
            var subH = Mathf.Max(22f, Mathf.Ceil(subFont * 1.45f));
            const float padTop = 4f, padBottom = 4f, rowGap = 4f;
            var badgeY = padTop;
            var titleY = badgeY + badgeH + 1f;
            var subY = titleY + titleH;
            var cardH = subY + subH + padBottom;
            return new QuestRowLayout(cardH + rowGap, cardH, badgeY, badgeH, badgeFont, titleY, titleH, titleFont, subY, subH, subFont);
        }

        public static string HildirGlobalKey(Minimap.PinType type)
        {
            switch (type)
            {
                case Minimap.PinType.Hildir1: return "hildir1";
                case Minimap.PinType.Hildir2: return "hildir2";
                case Minimap.PinType.Hildir3: return "hildir3";
                default: return null;
            }
        }

        public static string KindLabelToken(MapQuestKind kind)
        {
            switch (kind)
            {
                case MapQuestKind.Bounty: return "$ui_map_quest_bounty";
                case MapQuestKind.HildirBoss: return "$ui_map_quest_hildir";
                case MapQuestKind.Treasure: return "$ui_map_quest_treasure";
                default: return "$ui_map_quest_boss";
            }
        }

        // Harmonious Valheim category accent colors matching the custom pin swatch palette.
        public static Color KindAccentColor(MapQuestKind kind)
        {
            switch (kind)
            {
                case MapQuestKind.Bounty: return new Color(0.831f, 0.333f, 0.290f, 1f);     // #D4554A crimson
                case MapQuestKind.HildirBoss: return new Color(0.647f, 0.459f, 0.831f, 1f); // #A575D4 violet
                case MapQuestKind.Treasure: return new Color(0.902f, 0.722f, 0.298f, 1f);   // #E6B84C gold
                default: return new Color(0.831f, 0.510f, 0.267f, 1f);                      // #D48244 copper
            }
        }

        public static bool IsHildirPin(Minimap.PinType type) =>
            type == Minimap.PinType.Hildir1 || type == Minimap.PinType.Hildir2 || type == Minimap.PinType.Hildir3;

        // Returns the number of NPC quest rows ([0, npcCount)); rows [npcCount, into.Count) are Known Bosses.
        public static int Build(
            IReadOnlyList<MapQuest> coreQuests,
            IReadOnlyList<QuestPinInput> mapPins,
            Vector3 playerPos,
            string query,
            List<QuestRow> into,
            Func<MapQuestKind, string> kindLabel = null,
            Func<string, string> localize = null,
            Func<string, bool> hasGlobalKey = null)
        {
            into.Clear();
            var loc = localize ?? (s => s ?? "");
            var coreCount = coreQuests?.Count ?? 0;

            for (var i = 0; i < coreCount; i++)
            {
                var q = coreQuests[i];
                var title = loc(q.Title);
                var sub = loc(q.Subtitle);
                var cat = kindLabel != null ? kindLabel(q.Kind) : "";
                if (!Matches(title, sub, cat, query)) continue;
                into.Add(new QuestRow
                {
                    Id = string.IsNullOrEmpty(q.Id) ? $"core:{((int)q.Kind).ToString(CultureInfo.InvariantCulture)}:{i.ToString(CultureInfo.InvariantCulture)}" : q.Id,
                    Kind = q.Kind,
                    Title = title,
                    Subtitle = sub,
                    Position = q.Position,
                    Radius = q.Radius,
                    Distance = PortalPickerMath.DistanceXZ(playerPos, q.Position),
                    IsBossGroup = q.Kind == MapQuestKind.Boss,
                });
            }

            if (mapPins != null)
            {
                for (var i = 0; i < mapPins.Count; i++)
                {
                    var pin = mapPins[i];
                    MapQuestKind kind;
                    if (IsHildirPin(pin.Type))
                    {
                        var key = HildirGlobalKey(pin.Type);
                        if (key != null && hasGlobalKey != null && hasGlobalKey(key)) continue;
                        kind = MapQuestKind.HildirBoss;
                    }
                    else if (pin.Type == Minimap.PinType.Boss) kind = MapQuestKind.Boss;
                    else continue;

                    if (HasCoreNear(coreQuests, kind, pin.Position)) continue;

                    var rawName = CustomPinMath.StripTag(pin.Name);
                    var title = string.IsNullOrWhiteSpace(rawName)
                        ? loc(kind == MapQuestKind.HildirBoss ? "$ui_pin_hildir" : "$ui_pin_boss")
                        : loc(rawName);
                    var sub = kind == MapQuestKind.HildirBoss ? loc("$ui_pin_hildir") : "";
                    var cat = kindLabel != null ? kindLabel(kind) : "";
                    if (!Matches(title, sub, cat, query)) continue;

                    into.Add(new QuestRow
                    {
                        Id = $"pin:{((int)pin.Type).ToString(CultureInfo.InvariantCulture)}:{Math.Round(pin.Position.x).ToString(CultureInfo.InvariantCulture)}:{Math.Round(pin.Position.z).ToString(CultureInfo.InvariantCulture)}",
                        Kind = kind,
                        Title = title,
                        Subtitle = sub,
                        Position = pin.Position,
                        Radius = 0f,
                        Distance = PortalPickerMath.DistanceXZ(playerPos, pin.Position),
                        IsBossGroup = kind == MapQuestKind.Boss,
                    });
                }
            }

            into.Sort((a, b) =>
            {
                if (a.IsBossGroup != b.IsBossGroup) return a.IsBossGroup ? 1 : -1;
                if (!a.IsBossGroup && a.Kind != b.Kind) return ((int)a.Kind).CompareTo((int)b.Kind);
                var dc = a.Distance.CompareTo(b.Distance);
                return dc != 0 ? dc : string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
            });

            var npcCount = 0;
            while (npcCount < into.Count && !into[npcCount].IsBossGroup) npcCount++;
            return npcCount;
        }

        private static bool HasCoreNear(IReadOnlyList<MapQuest> coreQuests, MapQuestKind kind, Vector3 pos)
        {
            if (coreQuests == null) return false;
            for (var i = 0; i < coreQuests.Count; i++)
            {
                if (coreQuests[i].Kind != kind) continue;
                if (PortalPickerMath.DistanceXZ(coreQuests[i].Position, pos) <= DedupeRadius) return true;
            }
            return false;
        }

        public static bool Matches(string title, string subtitle, string category, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            var q = query.Trim();
            return (title ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                || (subtitle ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                || (category ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
