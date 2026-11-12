using System;
using Morgott.Core;

namespace Morgott.UI.PlayerHud
{
    // Morgott.Power's belt through its Core provider IBeltApi (docs\mod-contracts.md), main thread / local player. Power
    // owns every rule and all state; Morgott.UI only reads cells and forwards presses (HandleClick and the gamepad
    // commands). No provider (Power absent, or loaded but never started) = belt off; the strip then keeps its old sources.
    // Kinds are BeltMath.Food / Potion as int (= BeltKind values).
    public static class BeltBridge
    {
        private static IBeltApi _failed; // the provider that threw: belt row off for it (logged once), not every frame

        private static IBeltApi Api => Services.TryGet(out IBeltApi b) && !ReferenceEquals(b, _failed) ? b : null;

        public static bool Bound => Api != null;

        // Belt hooked ([Belt] Enabled): the wings show belt cells. False = draw nothing belt-related.
        public static bool On => Guard("Enabled", b => b.Enabled);

        // Change counter: layout / content / binding / config changes (timers do not bump it).
        public static int Version => Api is { } b ? b.Version : 0;

        public static int Capacity(Player p, int kind) => Api is { } b && p ? BeltMath.Clamp(b.Capacity(p, (BeltKind)kind)) : 0;

        public static int Base(int kind) => Api is { } b ? b.Base((BeltKind)kind) : 0;

        public static bool TryGet(Player p, int kind, int index, out BeltSlotState cell)
        {
            cell = default;
            if (Api is not { } b || !p) return false;
            try { return b.TryGetSlot(p, (BeltKind)kind, index, out cell) && cell.Valid; }
            catch (Exception e) { return Fail(b, "TryGetSlot", e); }
        }

        public static string Tooltip(int kind, int index) => Api is { } b ? b.Tooltip((BeltKind)kind, index) ?? "" : "";

        // Mouse press on a cell: button 0 = LMB, 1 = RMB; Power reads the modifiers itself (Shift / Ctrl / LeftAlt).
        public static bool Click(int kind, int index, int button) => Guard("HandleClick", b => b.HandleClick((BeltKind)kind, index, button));

        public static bool ToggleAuto(int kind, int index) => Guard("ToggleAuto", b => b.ToggleAuto((BeltKind)kind, index));

        public static bool UseNow(int kind, int index) => Guard("UseNow", b => b.UseNow((BeltKind)kind, index));

        public static bool DropDragged(int kind, int index) => Guard("DropDragged", b => b.DropDragged((BeltKind)kind, index));

        public static bool MoveToInventory(int kind, int index) => Guard("MoveToInventory", b => b.MoveToInventory((BeltKind)kind, index));

        public static bool BeginDrag(int kind, int index, int amount) => Guard("BeginDrag", b => b.BeginDrag((BeltKind)kind, index, amount));

        private static bool Guard(string what, Func<IBeltApi, bool> call)
        {
            if (Api is not { } b) return false;
            try { return call(b); }
            catch (Exception e) { return Fail(b, what, e); }
        }

        // A throwing provider member switches the whole row off (logged once) instead of throwing every frame.
        private static bool Fail(IBeltApi b, string what, Exception e)
        {
            _failed = b;
            Plugin.Log.LogWarning($"[Morgott.UI] Morgott.Power IBeltApi.{what} threw, belt row off: {e.GetBaseException().Message}");
            return false;
        }
    }
}
