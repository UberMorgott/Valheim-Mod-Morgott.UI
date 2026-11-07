using UnityEngine;

namespace Morgott.UI.PlayerHud
{
    // Refused-action feedback on RuneSkin's bars: vanilla's own "not enough" calls (HudAdapter prefixes on
    // Hud.StaminaBarEmptyFlash / EitrBarEmptyFlash / FlashHealthBar; the max-health upgrade flash from
    // Player.SetMaxHealth(float, bool), Player.cs:6166-6170, is not a refusal and is skipped) start a warning pulse on
    // the matching bar (theme color.hud.bar.deny, time.hud.bar.deny, count.hud.bar.deny.pulses). Stamina / eitr refusals
    // also play one soft sound (vanilla has none): the inventory-close sfx (user pick 2026-11-07, players found the old
    // forge-fail sting alarming), at most one per time.hud.bar.deny.sound. Health refusals (an HP-cost attack / blood
    // magic without the health, Attack.cs:401-403) stay silent (user 2026-11-07). No new audio asset.
    public static class DenyFeedback
    {
        public const string SoundPrefab = "sfx_gui_inventory_close";

        public static readonly DenyFlash Health = Make(), Stamina = Make(), Eitr = Make();

        // Diagnostics for the harness (reflection): sounds started, the last sound's ZSFX, the chosen prefab.
        public static int Sounds { get; private set; }
        public static ZSFX LastSound { get; private set; }
        public static string SoundName { get; private set; }

        private static float _soundAt = DenyFlash.NeverAt;
        private static GameObject _sound;
        private static bool _warned;

        // While Player.SetMaxHealth(float, true) runs: its FlashHealthBar is the upgrade flash.
        internal static bool InMaxHealthUpgrade;

        private static DenyFlash Make() => new DenyFlash(
            Theme.ThemeTokens.GetFloat("time.hud.bar.deny", 0.6f), (int)Theme.ThemeTokens.GetFloat("count.hud.bar.deny.pulses", 3f));

        public static float Now => Time.unscaledTime;

        public static void Deny(DenyFlash bar)
        {
            if (!HudCluster.Instance || !bar.Trigger(Now) || bar == Health) return;
            if (DenyFlash.Gate(ref _soundAt, Now, Theme.ThemeTokens.GetFloat("time.hud.bar.deny.sound", 0.6f))) PlaySound();
        }

        private static void PlaySound()
        {
            var player = Player.m_localPlayer;
            if (!player) return;
            if (!_sound) _sound = Find();
            if (!_sound) return;
            // local only: vanilla sfx prefabs carry a ZNetView (EffectList.Create spawns them networked); this cue is the
            // local player's own, so no ZDO is created (ZNetView.m_forceDisableInit, ZNetView.cs:45)
            GameObject go;
            ZNetView.m_forceDisableInit = true;
            try { go = Object.Instantiate(_sound, player.GetHeadPoint(), Quaternion.identity); }
            finally { ZNetView.m_forceDisableInit = false; }
            Object.Destroy(go, 5f);
            LastSound = go.GetComponentInChildren<ZSFX>();
            Sounds++;
        }

        // The sfx is no ZNetScene prefab: vanilla holds it in InventoryGui.m_closeInventoryEffects (InventoryGui.cs:237,
        // played at :1084). InventorySkin swaps that list for an empty one while its ghost window runs, so a miss is
        // retried on the next refusal (warned once).
        private static GameObject Find()
        {
            var fx = InventoryGui.instance ? InventoryGui.instance.m_closeInventoryEffects?.m_effectPrefabs : null;
            if (fx != null)
                foreach (var e in fx)
                    if (e?.m_prefab && e.m_prefab.name == SoundPrefab)
                    {
                        SoundName = e.m_prefab.name;
                        return e.m_prefab;
                    }
            if (!_warned) Plugin.Log.LogInfo($"[Morgott.UI] deny sound: {SoundPrefab} not in InventoryGui close effects yet (flash only)");
            _warned = true;
            return null;
        }
    }
}
