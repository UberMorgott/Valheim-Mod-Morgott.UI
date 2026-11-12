using System;
using Morgott.Core;

namespace Morgott.UI.PlayerHud
{
    // Crossbow magazine data through the Core provider IMagazineInfo (docs\mod-contracts.md; owner Morgott.Power):
    // Capacity(ItemData) / Loaded(ItemData), 0 = not a magazine crossbow; Revision bumped on every loaded-count or
    // MagazineSize change. Without the provider (or with its magazine off) no item is a magazine: no dots, no plate readout.
    public static class MagazineSource
    {
        private static IMagazineInfo _failed; // the provider that threw: readout off for it (logged once)

        // Capacity asks Epic Loot (magazine affix; it rebuilds magic data per read): cached per item, re-read on a revision
        // change or after this many seconds (an enchant changes the affix without a revision bump).
        public const float CapacityTtl = 2f;

        private static IMagazineInfo Info => Services.TryGet(out IMagazineInfo m) && !ReferenceEquals(m, _failed) ? m : null;

        public static bool Available => Info != null;

        // The provider's change counter; 0 without it.
        public static int Revision
        {
            get
            {
                if (Info is not { } m) return 0;
                try { return m.Revision; }
                catch (Exception e) { return Fail(m, e); }
            }
        }

        public static bool Read(ItemDrop.ItemData item, out int loaded, out int capacity)
        {
            loaded = capacity = 0;
            if (item == null || Info is not { } m) return false;
            try
            {
                capacity = m.Capacity(item);
                if (capacity <= 0) return false;
                loaded = m.Loaded(item);
                return true;
            }
            catch (Exception e)
            {
                Fail(m, e);
                return false;
            }
        }

        private static int Fail(IMagazineInfo m, Exception e)
        {
            _failed = m;
            Plugin.Log.LogWarning($"[Morgott.UI] crossbow magazine read failed, magazine readout off: {e.GetBaseException().Message}");
            return 0;
        }
    }
}
