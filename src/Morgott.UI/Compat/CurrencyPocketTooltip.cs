using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;

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
            harmony.Patch(target, transpiler: new HarmonyMethod(typeof(CurrencyPocketTooltip), nameof(Transpiler)));
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
