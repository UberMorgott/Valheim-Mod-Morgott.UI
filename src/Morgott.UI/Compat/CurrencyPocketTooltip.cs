using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Morgott.UI.Compat
{
    // CurrencyPocket (Azumatt, 1.0.13) hardcodes the English tooltip of its coin drop slot: PocketDrop.OnPointerEnter calls
    // uiTooltip.Set("Coin Drop", "Click here to store coins in your pocket.", ...). Vanilla UITooltip localizes topic and
    // text when it shows them, so swapping the two literals for $ui_cp_* tokens translates the tooltip. No reference to
    // CurrencyPocket.dll.
    internal static class CurrencyPocketTooltip
    {
        private static readonly Dictionary<string, string> Tokens = new Dictionary<string, string>
        {
            { "Coin Drop", "$ui_cp_coin_drop" },
            { "Click here to store coins in your pocket.", "$ui_cp_coin_drop_desc" }
        };

        // From Plugin.Start: CurrencyPocket may load after us.
        internal static void Register(ConfigFile config, Harmony harmony)
        {
            if (!config.Bind("Compat", "CurrencyPocketTooltip", true,
                    "Translate CurrencyPocket's hardcoded English \"Coin Drop\" tooltip into the game language. " +
                    "Does nothing without CurrencyPocket.").Value) return;
            if (!Chainloader.PluginInfos.TryGetValue(ModCompat.CurrencyPocketGuid, out var info) || !info.Instance) return;
            var target = info.Instance.GetType().Assembly.GetType("CurrencyPocket.PocketDrop")
                ?.GetMethod("OnPointerEnter", BindingFlags.Public | BindingFlags.Instance);
            if (target == null)
            {
                Plugin.Log.LogWarning($"[Compat] CurrencyPocket {info.Metadata.Version}: PocketDrop.OnPointerEnter not found; tooltip stays English.");
                return;
            }
            harmony.Patch(target, transpiler: new HarmonyMethod(typeof(CurrencyPocketTooltip), nameof(Transpiler)),
                postfix: new HarmonyMethod(typeof(CurrencyPocketTooltip), nameof(PointerEnterPostfix)));
            var update = target.DeclaringType.GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (update != null) harmony.Patch(update, postfix: new HarmonyMethod(typeof(CurrencyPocketTooltip), nameof(UpdatePostfix)));
            else Plugin.Log.LogWarning($"[Compat] CurrencyPocket {info.Metadata.Version}: PocketDrop.Update not found; the coin plate icon does not fade when empty.");
        }

        // CurrencyPocket Constants.CoinCountCustomData / ArmorIconName.
        private const string PurseKey = "CoinPocket_CoinCount";
        private const string IconName = "armor_icon";
        // Empty purse: the coin glyph dimmed like an empty slot (user 2026-11-08: the plate was not read as a coin slot).
        private const float EmptyAlpha = 0.35f;

        private static bool Dragging(InventoryGui gui) => gui && gui.m_dragGo && gui.m_dragItem != null;

        internal static int Purse(Player player) =>
            player && player.m_customData.TryGetValue(PurseKey, out var text) && int.TryParse(text, out var coins) && coins > 0 ? coins : 0;

        internal static float IconAlpha(int purse) => purse > 0 ? 1f : EmptyAlpha;

        // CurrencyPocket shows a tooltip only while an item is dragged ("put coins here"); hovering the plate empty-handed
        // now names it: the purse and how coins get there. Same tooltip object and call as its own (PocketDrop.OnPointerEnter).
        private static void PointerEnterPostfix(MonoBehaviour __instance)
        {
            var gui = InventoryGui.instance;
            if (!gui || Dragging(gui) || !gui.m_playerGrid || gui.m_playerGrid.m_elements.Count == 0) return;
            var tooltip = gui.m_playerGrid.m_elements[0].m_tooltip;
            if (!tooltip) return;
            tooltip.Set("$ui_cp_pocket", Localization.instance.Localize("$ui_cp_pocket_desc", Purse(Player.m_localPlayer).ToString()));
            tooltip.OnHoverStart(__instance.gameObject);
        }

        // After CurrencyPocket's own Update (it swaps the glyph sprite while coins are dragged): dim the coin glyph while
        // the purse is empty.
        private static void UpdatePostfix(MonoBehaviour __instance)
        {
            var icon = __instance.transform.Find(IconName);
            if (!icon || !icon.TryGetComponent<Image>(out var image)) return;
            var alpha = Dragging(InventoryGui.instance) ? 1f : IconAlpha(Purse(Player.m_localPlayer));
            var c = image.color;
            if (Mathf.Abs(c.a - alpha) > 0.001f) image.color = new Color(c.r, c.g, c.b, alpha);
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var replaced = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldstr && instruction.operand is string text && Tokens.TryGetValue(text, out var token))
                {
                    instruction.operand = token;
                    replaced++;
                }
                yield return instruction;
            }
            if (replaced != Tokens.Count)
                Plugin.Log.LogWarning($"[Compat] CurrencyPocket tooltip: replaced {replaced} of {Tokens.Count} strings; the mod's text changed.");
        }
    }
}
