using System.Collections.Generic;

namespace Morgott.UI.Icons
{
    // Which icon-less status effect of another mod gets which cell of Assets\StatusIcons.png (no Unity, unit tested).
    // Found by scanning every pack plugin for StatusEffect subclasses that deal damage over time or slow and never get an
    // icon (2026-10-20):
    // - Warfare 1.9.4 Warfare_Bleeding_Stacking (FistSilver_TW), Warfare_BleedingSecondaryAttack (FistQueen_TW /
    //   FistChitin_TW) and Warfare_Impale (LanceBlackmetal_TW / LanceDvergr_TW): their OnEnable sets m_icon from a static
    //   Warfare never fills (WarfarePlugin calls UseIcon only for Haste, Ghost, Vampirism, LightningBurstStacking).
    // - Epic Loot EL_Paralyze (Paralyze.GetOrCreatePrototype, speed 0 for its duration): the prototype gets no icon.
    // Left out on purpose: Warfare's 4-hit counters (Warfare_Bleeding, Warfare_PiercingGreatbow*: no damage over time)
    // and its effects no item applies (BurningSecondaryAttack, EikthyrCrossbow, Pinned, PinnedFrozen, Shatter).
    internal static class ModStatusIconMap
    {
        // Atlas cells in order = Assets\status_icons.json "items" order.
        internal static readonly string[] Cells = { "bleed", "impale", "paralyze" };

        private static readonly Dictionary<string, string> ByStatus = new Dictionary<string, string>
        {
            { "Warfare_Bleeding_Stacking", "bleed" },
            { "Warfare_BleedingSecondaryAttack", "bleed" },
            { "Warfare_Impale", "impale" },
            { "EL_Paralyze", "paralyze" }
        };

        // Status effect names (ScriptableObject name) that get an icon.
        internal static IEnumerable<string> Statuses => ByStatus.Keys;

        // Mapped statuses their mod never puts into ObjectDB.m_StatusEffects, so a name hash of them (ZDO rows such as
        // Creatures' status rows, SEMan.RPC_AddStatusEffect) resolves to nothing: EL_Paralyze lives only in
        // Paralyze._prototype. Warfare adds its effects to ObjectDB itself.
        internal static bool RegistersInObjectDb(string name) => name == "EL_Paralyze";

        // Atlas cell of status effect name, or -1 when it keeps its own icon.
        internal static int CellOf(string name) =>
            name != null && ByStatus.TryGetValue(name, out var key) ? System.Array.IndexOf(Cells, key) : -1;
    }
}
