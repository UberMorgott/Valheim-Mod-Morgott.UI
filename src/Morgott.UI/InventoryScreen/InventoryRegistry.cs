using Morgott.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Morgott.UI.InventoryScreen
{
    // Theme registrations the inventory screen renders: panel actions (ITheme.RegisterPanelAction) and slot overlays (ITheme.SetSlotOverlay). Keys are owner-qualified; a later registration of the same key replaces the earlier one.
    // Foreign callbacks run through Guard: MaxFaults exceptions disable that registration for the session.
    public static class InventoryRegistry
    {
        public const int MaxFaults = 3;

        public sealed class Action
        {
            public string Key, Owner;
            public PanelActionDescriptor D;
            public int Faults;
            public bool Dead => Faults >= MaxFaults;

            internal void Remove()
            {
                if (Actions.Remove(this)) Revision++;
            }
        }

        public struct OverlayKey : IEquatable<OverlayKey>
        {
            public string Owner;
            public Inventory Inventory;
            public int X, Y;

            public bool Equals(OverlayKey other) => string.Equals(Owner, other.Owner, StringComparison.Ordinal) && ReferenceEquals(Inventory, other.Inventory) && X == other.X && Y == other.Y;
            public override bool Equals(object obj) => obj is OverlayKey o && Equals(o);
            public override int GetHashCode() => ((Owner == null ? 0 : StringComparer.Ordinal.GetHashCode(Owner)) * 397) ^ (Inventory?.GetHashCode() ?? 0) ^ (X * 31 + Y);
            public static bool operator ==(OverlayKey a, OverlayKey b) => a.Equals(b);
            public static bool operator !=(OverlayKey a, OverlayKey b) => !a.Equals(b);
        }

        public static readonly List<Action> Actions = new List<Action>();
        public sealed class Overlay
        {
            public string Token;
            internal OverlayKey Key;

            internal void Remove()
            {
                if (Overlays.TryGetValue(Key, out var cur) && ReferenceEquals(cur, this))
                {
                    Overlays.Remove(Key);
                    OverlayRevision++;
                }
            }
        }

        // Value = the registration object (identity), so disposing an older handle never removes a newer one.
        public static readonly Dictionary<OverlayKey, Overlay> Overlays = new Dictionary<OverlayKey, Overlay>();

        // Bumped on every change: the screen re-lays out / rebuilds cells only then.
        public static int Revision { get; private set; }
        public static int OverlayRevision { get; private set; }

        private static string PanelKindName(PanelKind panel) => panel switch
        {
            PanelKind.Player => nameof(PanelKind.Player),
            PanelKind.Container => nameof(PanelKind.Container),
            PanelKind.Backpack => nameof(PanelKind.Backpack),
            _ => ((int)panel).ToString(CultureInfo.InvariantCulture)
        };

        public static System.Action AddAction(string owner, PanelActionDescriptor d)
        {
            var key = owner + ":" + d.Id;
            for (var i = Actions.Count - 1; i >= 0; i--)
                if (Actions[i].Key == key && Actions[i].D.Panel == d.Panel) Actions.RemoveAt(i);
            var entry = new Action { Key = key, Owner = owner, D = d };
            Actions.Add(entry);
            Revision++;
            Plugin.Log.LogInfo($"[Morgott.UI] panel action '{key}' on {PanelKindName(d.Panel)}");
            return entry.Remove;
        }

        public static System.Action SetOverlay(string owner, Inventory inventory, int x, int y, string token)
        {
            var key = new OverlayKey { Owner = owner, Inventory = inventory, X = x, Y = y };
            var entry = new Overlay { Token = token, Key = key };
            Overlays[key] = entry;
            OverlayRevision++;
            return entry.Remove;
        }

        // Overlay token of a cell for any owner (last one wins when two owners mark the same cell).
        public static string OverlayAt(Inventory inventory, int x, int y)
        {
            if (Overlays.Count == 0 || inventory == null) return null;
            string token = null;
            foreach (var pair in Overlays)
                if (ReferenceEquals(pair.Key.Inventory, inventory) && pair.Key.X == x && pair.Key.Y == y)
                    token = pair.Value.Token;
            return token;
        }

        public static T Guard<T>(Action a, Func<T> call, T fallback)
        {
            if (a.Dead) return fallback;
            try { return call(); }
            catch (Exception e) { Fault(ref a.Faults, a.Key, e); return fallback; }
        }

        // Per-frame callers pass the registered delegate and its argument (a capturing lambda per call allocated a
        // closure + delegate every frame per action / slot, perf audit B2).
        public static T Guard<TArg, T>(Action a, Func<TArg, T> call, TArg arg, T fallback)
        {
            if (a.Dead) return fallback;
            try { return call(arg); }
            catch (Exception e) { Fault(ref a.Faults, a.Key, e); return fallback; }
        }

        internal static void GuardInvoke(Action a, Inventory inv)
        {
            if (a.Dead) return;
            try
            {
                if (a.D.OnInvokeFor != null) a.D.OnInvokeFor(inv);
                else a.D.OnInvoke?.Invoke();
            }
            catch (Exception e) { Fault(ref a.Faults, a.Key, e); }
        }

        private static void Fault(ref int faults, string key, Exception e)
        {
            faults++;
            Plugin.Log.LogError($"[Morgott.UI] callback of '{key}' failed ({faults.ToString(CultureInfo.InvariantCulture)}/{MaxFaults.ToString(CultureInfo.InvariantCulture)}){(faults >= MaxFaults ? ", disabled" : "")}: {e}");
            Revision++;
        }
    }
}
