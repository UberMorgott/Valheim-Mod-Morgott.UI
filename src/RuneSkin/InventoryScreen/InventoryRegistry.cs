using Morgott.Core;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuneSkin.InventoryScreen
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
        }

        // Value = the registration object (identity), so disposing an older handle never removes a newer one.
        public static readonly Dictionary<OverlayKey, Overlay> Overlays = new Dictionary<OverlayKey, Overlay>();

        // Bumped on every change: the screen re-lays out / rebuilds cells only then.
        public static int Revision { get; private set; }
        public static int OverlayRevision { get; private set; }

        public static System.Action AddAction(string owner, PanelActionDescriptor d)
        {
            var key = owner + ":" + d.Id;
            Actions.RemoveAll(a => a.Key == key && a.D.Panel == d.Panel);
            var entry = new Action { Key = key, Owner = owner, D = d };
            Actions.Add(entry);
            Revision++;
            Plugin.Log.LogInfo($"[RuneSkin] panel action '{key}' on {d.Panel}");
            return () => { if (Actions.Remove(entry)) Revision++; };
        }

        public static System.Action SetOverlay(string owner, Inventory inventory, int x, int y, string token)
        {
            var key = new OverlayKey { Owner = owner, Inventory = inventory, X = x, Y = y };
            var entry = new Overlay { Token = token };
            Overlays[key] = entry;
            OverlayRevision++;
            return () =>
            {
                if (Overlays.TryGetValue(key, out var cur) && ReferenceEquals(cur, entry))
                {
                    Overlays.Remove(key);
                    OverlayRevision++;
                }
            };
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

        private static void Fault(ref int faults, string key, Exception e)
        {
            faults++;
            Plugin.Log.LogError($"[RuneSkin] callback of '{key}' failed ({faults}/{MaxFaults}){(faults >= MaxFaults ? ", disabled" : "")}: {e}");
            Revision++;
        }
    }
}
