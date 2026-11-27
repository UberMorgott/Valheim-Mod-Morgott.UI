using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Morgott.UI.MapScreen
{
    // Pure logic for custom colored 25-icon map pins (linked into Morgott.UI.Tests).
    // Metadata is stored as a versioned trailing suffix in PinData.m_name so pins survive vanilla save/load,
    // Cartography Table sharing, and ValheimPlus syncPins without custom ZDOs or non-vanilla PinTypes:
    //   "<VisibleName> [mp1:<iconId>:<RRGGBB>]" or "[mp1:<iconId>:<RRGGBB>]" when visible name is empty.
    public readonly struct DecodedPin
    {
        public readonly string VisibleName;
        public readonly string IconId;
        public readonly int IconIndex;
        public readonly string Hex;
        public readonly bool HasColor;
        public readonly byte R, G, B;

        public DecodedPin(string visibleName, string iconId, int iconIndex, string hex, bool hasColor, byte r, byte g, byte b)
        {
            VisibleName = visibleName;
            IconId = iconId;
            IconIndex = iconIndex;
            Hex = hex;
            HasColor = hasColor;
            R = r;
            G = g;
            B = b;
        }
    }

    public readonly struct CustomIconSummary
    {
        public readonly string IconId;
        public readonly int IconIndex;
        public readonly int Count;
        public readonly bool Hidden;
        public readonly bool HasColor;
        public readonly byte R, G, B;

        public CustomIconSummary(string iconId, int iconIndex, int count, bool hidden, bool hasColor, byte r, byte g, byte b)
        {
            IconId = iconId;
            IconIndex = iconIndex;
            Count = count;
            Hidden = hidden;
            HasColor = hasColor;
            R = r;
            G = g;
            B = b;
        }
    }

    public static class CustomPinMath
    {
        public const string DefaultIconId = "dot";
        public const string DefaultHex = "ECE5D8";
        public const float DeferClickSeconds = 0.3f;
        public const int MaxVisibleNameLength = 40;
        private const string TagPrefix = "[mp1:";

        // Row-major order matching Assets\MapPinIcons.png (5x5 grid of 128x128 cells, indices 0..24).
        public static readonly string[] IconIds =
        {
            "ore", "berry", "shroom", "cave", "skull",
            "boss", "house", "bed", "ship", "anchor",
            "coins", "chest", "portal", "farm", "hive",
            "hunt", "fish", "fight", "fort", "camp",
            "tree", "star", "alert", "quest", "dot",
        };

        // 10 harmonious Valheim map pin swatches (parchment, gold, copper, crimson, emerald, frost, violet, silver, bronze, moss).
        public static readonly string[] SwatchHex =
        {
            "ECE5D8", "E6B84C", "D48244", "D4554A", "5FB86B",
            "5EB8D4", "A575D4", "A8B3BE", "B88B58", "88A858",
        };

        private static readonly Dictionary<string, int> IconMap = BuildIconMap();

        private static Dictionary<string, int> BuildIconMap()
        {
            var map = new Dictionary<string, int>(IconIds.Length, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < IconIds.Length; i++) map[IconIds[i]] = i;
            return map;
        }

        public static int IconIndex(string iconId) =>
            !string.IsNullOrEmpty(iconId) && IconMap.TryGetValue(iconId, out var i) ? i : -1;

        public static bool IsKnownIcon(string iconId) => IconIndex(iconId) >= 0;

        public static string IconNameToken(string iconId) =>
            "$ui_mpin_icon_" + (IsKnownIcon(iconId) ? IconIds[IconIndex(iconId)] : DefaultIconId);

        public static string NormalizeVisibleName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var cleaned = StripTag(name);
            var sb = new StringBuilder(cleaned.Length);
            for (var i = 0; i < cleaned.Length; i++)
            {
                var ch = cleaned[i];
                if (char.IsHighSurrogate(ch))
                {
                    if (i + 1 < cleaned.Length && char.IsLowSurrogate(cleaned[i + 1]))
                    {
                        sb.Append(ch);
                        sb.Append(cleaned[++i]);
                    }
                    continue; // strip unpaired high surrogate so UTF-8 BinaryWriter never throws on map save
                }
                if (char.IsLowSurrogate(ch)) continue; // strip unpaired low surrogate
                // Mirror vanilla OnPinTextEntered sanitization ($ < >).
                sb.Append(ch == '$' || ch == '<' || ch == '>' ? ' ' : ch);
            }
            var trimmed = sb.ToString().Trim();
            if (trimmed.Length <= MaxVisibleNameLength) return trimmed;
            var cut = MaxVisibleNameLength;
            // Never split a UTF-16 surrogate pair at the truncation boundary.
            if (cut > 0 && char.IsHighSurrogate(trimmed[cut - 1])) cut--;
            return trimmed.Substring(0, cut).TrimEnd();
        }

        public static string Encode(string visibleName, string iconId, string hex)
        {
            var id = IsValidToken(iconId) ? iconId.ToLowerInvariant() : DefaultIconId;
            var color = TryParseHexRgb(hex, out var r, out var g, out var b)
                ? r.ToString("X2", CultureInfo.InvariantCulture) + g.ToString("X2", CultureInfo.InvariantCulture) + b.ToString("X2", CultureInfo.InvariantCulture)
                : DefaultHex;
            var clean = NormalizeVisibleName(visibleName);
            var tag = $"{TagPrefix}{id}:{color}]";
            return clean.Length > 0 ? $"{clean} {tag}" : tag;
        }

        public static bool TryDecode(string raw, out DecodedPin decoded)
        {
            decoded = default;
            if (string.IsNullOrEmpty(raw)) return false;

            var end = raw.Length - 1;
            while (end >= 0 && char.IsWhiteSpace(raw[end])) end--;
            if (end < 0 || raw[end] != ']') return false;

            var start = raw.LastIndexOf(TagPrefix, end, StringComparison.Ordinal);
            if (start < 0) return false;

            // Only accept a tag that sits cleanly at the start of the string or after whitespace.
            if (start > 0 && !char.IsWhiteSpace(raw[start - 1])) return false;

            var bodyStart = start + TagPrefix.Length;
            var bodyLen = end - bodyStart;
            if (bodyLen <= 0) return false;

            var colon = raw.IndexOf(':', bodyStart, bodyLen);
            if (colon <= bodyStart || colon >= end - 1) return false;
            // Reject extra colons or nested brackets inside the tag body.
            if (raw.IndexOf(':', colon + 1, end - colon - 1) >= 0) return false;
            if (raw.IndexOf('[', bodyStart, bodyLen) >= 0 || raw.IndexOf(']', bodyStart, bodyLen) >= 0) return false;

            var iconPart = raw.Substring(bodyStart, colon - bodyStart);
            var colorPart = raw.Substring(colon + 1, end - colon - 1);
            if (!IsValidToken(iconPart) || !IsValidToken(colorPart)) return false;

            var iconId = iconPart.ToLowerInvariant();
            var iconIndex = IconIndex(iconId);
            var hasColor = TryParseHexRgb(colorPart, out var r, out var g, out var b);
            var hex = hasColor
                ? r.ToString("X2", CultureInfo.InvariantCulture) + g.ToString("X2", CultureInfo.InvariantCulture) + b.ToString("X2", CultureInfo.InvariantCulture)
                : "";

            var nameEnd = start;
            if (nameEnd > 0 && raw[nameEnd - 1] == ' ') nameEnd--;
            var visible = nameEnd > 0 ? raw.Substring(0, nameEnd) : "";

            decoded = new DecodedPin(visible, iconId, iconIndex, hex, hasColor, r, g, b);
            return true;
        }

        public static string StripTag(string raw) =>
            string.IsNullOrEmpty(raw) ? "" : TryDecode(raw, out var d) ? d.VisibleName : raw;

        public static bool TryParseHexRgb(string hex, out byte r, out byte g, out byte b)
        {
            r = g = b = 0;
            if (string.IsNullOrEmpty(hex)) return false;
            var s = hex[0] == '#' ? hex.Substring(1) : hex;
            if (s.Length != 6) return false;
            if (!uint.TryParse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb)) return false;
            r = (byte)((rgb >> 16) & 0xFF);
            g = (byte)((rgb >> 8) & 0xFF);
            b = (byte)(rgb & 0xFF);
            return true;
        }

        public static Color ToColor(byte r, byte g, byte b, float alpha = 1f) =>
            new Color(r / 255f, g / 255f, b / 255f, alpha);

        public static Color SwatchColor(string hex) =>
            TryParseHexRgb(hex, out var r, out var g, out var b) ? ToColor(r, g, b) : Color.white;

        // Shared Cartography pins use translucent alpha (0.8 * m_sharedMapDataFade): never clobber original alpha.
        public static Color ApplyColorPreservingAlpha(Color original, byte r, byte g, byte b) =>
            new Color(r / 255f, g / 255f, b / 255f, original.a);

        // Only own saved user pins (Icon0..Icon4) may be edited: never claim/edit foreign shared pins or system pins.
        public static bool IsEditablePin(bool save, long ownerId, Minimap.PinType type) =>
            save && ownerId == 0L && IsUserPinType(type);

        public static bool IsUserPinType(Minimap.PinType type) =>
            type == Minimap.PinType.Icon0
            || type == Minimap.PinType.Icon1
            || type == Minimap.PinType.Icon2
            || type == Minimap.PinType.Icon3
            || type == Minimap.PinType.Icon4;

        // Guards map typing / modal state: once the large map is closed, InTextInput must never stay stuck true.
        public static bool IsTypingInMap(bool mapOpen, bool searchFocused, bool portalEditorOpen, bool customPinModalOpen) =>
            mapOpen && (searchFocused || portalEditorOpen || customPinModalOpen);

        public enum PinSaveAction
        {
            Cancel = 0,
            NoOp = 1,
            Create = 2,
            Replace = 3,
        }

        // Plans modal confirmation cleanly:
        // - Creating: Create
        // - Editing a deleted or no-longer-editable pin: Cancel (prevents resurrecting concurrently deleted pins)
        // - Editing with identical (type, rawName): NoOp (prevents V+ server key-skip + RemovePin deleting the pin!)
        // - Editing with modified key: Replace (AddPin preserving m_author first, then RemovePin(oldPin))
        public static PinSaveAction PlanSave(
            bool isEditing,
            bool stillPresent,
            bool stillEditable,
            Minimap.PinType oldType,
            string oldRawName,
            Minimap.PinType newType,
            string newRawName)
        {
            if (!isEditing) return PinSaveAction.Create;
            if (!stillPresent || !stillEditable) return PinSaveAction.Cancel;
            if (oldType == newType && string.Equals(oldRawName ?? "", newRawName ?? "", StringComparison.Ordinal))
                return PinSaveAction.NoOp;
            return PinSaveAction.Replace;
        }

        public readonly struct SavedPinIdentity
        {
            public readonly object Pin;
            public readonly bool Save;
            public readonly Minimap.PinType Type;
            public readonly Vector3 Pos;
            public readonly string RawName;

            public SavedPinIdentity(object pin, bool save, Minimap.PinType type, Vector3 pos, string rawName)
            {
                Pin = pin;
                Save = save;
                Type = type;
                Pos = pos;
                RawName = rawName ?? "";
            }
        }

        // ValheimPlus syncPins dedup key format: "<type>|<x:F1>|<z:F1>|<name>" (InvariantCulture).
        public static string VPlusSyncKey(Minimap.PinType type, Vector3 pos, string rawName) =>
            $"{((int)type).ToString(CultureInfo.InvariantCulture)}|{pos.x.ToString("F1", CultureInfo.InvariantCulture)}|{pos.z.ToString("F1", CultureInfo.InvariantCulture)}|{rawName ?? ""}";

        // True when any OTHER saved pin (including filter-hidden pins) already holds the same V+ sync key.
        public static bool HasVPlusKeyCollision(
            IEnumerable<SavedPinIdentity> existingPins,
            object editingPin,
            Minimap.PinType targetType,
            Vector3 targetPos,
            string targetRawName)
        {
            if (existingPins == null) return false;
            var targetKey = VPlusSyncKey(targetType, targetPos, targetRawName);
            var list = existingPins as IReadOnlyList<SavedPinIdentity> ?? new List<SavedPinIdentity>(existingPins);
            for (var i = 0; i < list.Count; i++)
                if (MatchesVPlusCollision(list[i], editingPin, targetKey)) return true;
            return false;
        }

        private static bool MatchesVPlusCollision(SavedPinIdentity p, object editingPin, string targetKey)
        {
            if (!p.Save || (editingPin != null && ReferenceEquals(p.Pin, editingPin))) return false;
            return string.Equals(VPlusSyncKey(p.Type, p.Pos, p.RawName), targetKey, StringComparison.Ordinal);
        }

        public static bool IsCustomPinVisible(string rawName, ISet<string> hiddenCustomIcons)
        {
            if (hiddenCustomIcons == null || hiddenCustomIcons.Count == 0) return true;
            return !TryDecode(rawName, out var d) || d.IconIndex < 0 || !hiddenCustomIcons.Contains(d.IconId);
        }

        // Collects ONLY known custom icon IDs currently placed on this map (INCLUDING hidden ones so players can re-enable them),
        // ordered canonically by IconIds (0..24) with count and representative color.
        public static void CollectPresentCustomIcons(IEnumerable<string> rawPinNames, ISet<string> hiddenCustomIcons, List<CustomIconSummary> into)
        {
            into.Clear();
            if (rawPinNames == null) return;
            var counts = new int[IconIds.Length];
            var hasColor = new bool[IconIds.Length];
            var r = new byte[IconIds.Length];
            var g = new byte[IconIds.Length];
            var b = new byte[IconIds.Length];

            var list = rawPinNames as IReadOnlyList<string> ?? new List<string>(rawPinNames);
            for (var i = 0; i < list.Count; i++)
                AccumulateCustomIcon(list[i], counts, hasColor, r, g, b);

            for (var i = 0; i < IconIds.Length; i++)
            {
                if (counts[i] <= 0) continue;
                var id = IconIds[i];
                var hidden = hiddenCustomIcons != null && hiddenCustomIcons.Contains(id);
                into.Add(new CustomIconSummary(id, i, counts[i], hidden, hasColor[i], r[i], g[i], b[i]));
            }
        }

        private static void AccumulateCustomIcon(string raw, int[] counts, bool[] hasColor, byte[] r, byte[] g, byte[] b)
        {
            if (!TryDecode(raw, out var d) || d.IconIndex < 0) return;
            var idx = d.IconIndex;
            counts[idx]++;
            if (!hasColor[idx] && d.HasColor)
            {
                hasColor[idx] = true;
                r[idx] = d.R;
                g[idx] = d.G;
                b[idx] = d.B;
            }
        }

        public static void ParseHiddenIcons(string csv, HashSet<string> into)
        {
            into.Clear();
            if (string.IsNullOrWhiteSpace(csv)) return;
            var parts = csv.Split(',');
            for (var i = 0; i < parts.Length; i++)
            {
                var p = parts[i].Trim();
                var idx = IconIndex(p);
                if (idx >= 0) into.Add(IconIds[idx]);
            }
        }

        public static string FormatHiddenIcons(IEnumerable<string> hidden)
        {
            if (hidden == null) return "";
            var present = new bool[IconIds.Length];
            if (hidden is HashSet<string> set)
            {
                foreach (var id in set)
                {
                    var idx = IconIndex(id);
                    if (idx >= 0) present[idx] = true;
                }
            }
            else
            {
                var list = hidden as IReadOnlyList<string> ?? new List<string>(hidden);
                for (var i = 0; i < list.Count; i++)
                {
                    var idx = IconIndex(list[i]);
                    if (idx >= 0) present[idx] = true;
                }
            }
            var sb = new StringBuilder();
            for (var i = 0; i < IconIds.Length; i++)
            {
                if (!present[i]) continue;
                if (sb.Length > 0) sb.Append(',');
                sb.Append(IconIds[i]);
            }
            return sb.ToString();
        }

        private static bool IsValidToken(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                var ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (!ok) return false;
            }
            return true;
        }
    }

    public enum DeferredPinAction
    {
        None = 0,
        ToggleChecked = 1,
        ClaimForeign = 2,
    }

    public readonly struct DeferredPinItem
    {
        public readonly object Pin;
        public readonly DeferredPinAction Action;

        public DeferredPinItem(object pin, DeferredPinAction action)
        {
            Pin = pin;
            Action = action;
        }
    }

    // Defers vanilla single-click actions on own editable pins (cross-out toggle) AND foreign shared pins (ownership claim)
    // by 0.3s so a double-click cancels the pending action instead of toggling X or claiming a foreign pin before opening the editor.
    public sealed class DeferredPinToggle
    {
        private object _pending;
        private DeferredPinAction _action;
        private float _dueAt = float.NegativeInfinity;
        private object _recentForeignPin;
        private float _recentForeignUntil = float.NegativeInfinity;

        public bool HasPending => _pending != null;

        public bool OnEditablePinClick(object pin, float now, out object flushPrevious)
        {
            var deferred = OnPinClick(pin, DeferredPinAction.ToggleChecked, now, out var prev);
            flushPrevious = prev.Pin;
            return deferred;
        }

        public bool OnPinClick(object pin, DeferredPinAction action, float now, out DeferredPinItem flushPrevious)
        {
            flushPrevious = default;
            if (pin == null || action == DeferredPinAction.None) return false;
            if (action == DeferredPinAction.ClaimForeign)
            {
                _recentForeignPin = pin;
                _recentForeignUntil = now + CustomPinMath.DeferClickSeconds;
            }
            if (ReferenceEquals(_pending, pin) && now < _dueAt)
            {
                CancelPendingOnly();
                return true;
            }
            if (_pending != null) flushPrevious = new DeferredPinItem(_pending, _action);
            _pending = pin;
            _action = action;
            _dueAt = now + CustomPinMath.DeferClickSeconds;
            return true;
        }

        // True if this pin was foreign BEFORE the first click of a double-click pair (within 0.3s),
        // blocking the editor even if another mod or vanilla claimed m_ownerID = 0 on click #1.
        public bool WasRecentlyForeign(object pin, float now) =>
            pin != null && ReferenceEquals(_recentForeignPin, pin) && now <= _recentForeignUntil;

        public DeferredPinItem PollDueItem(float now)
        {
            if (_pending == null || now < _dueAt) return default;
            var due = new DeferredPinItem(_pending, _action);
            CancelPendingOnly();
            return due;
        }

        public object PollDue(float now) => PollDueItem(now).Pin;

        public void Cancel()
        {
            CancelPendingOnly();
            _recentForeignPin = null;
            _recentForeignUntil = float.NegativeInfinity;
        }

        // Preserves _recentForeignPin across OnMapLeftClick -> OnMapDblClick on the same frame.
        public void CancelPendingOnly()
        {
            _pending = null;
            _action = DeferredPinAction.None;
            _dueAt = float.NegativeInfinity;
        }
    }
}
