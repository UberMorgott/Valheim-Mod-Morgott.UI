using System;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;

namespace Morgott.UI.Compat
{
    // Third-party texts that never reach the game language. PlantEverything (1.21.4) adds its "$pe*" words in English
    // only (PluginInitUtils.InitLocalization, from its ZNetScene.Awake patch), over the ones Jotunn already added from our
    // Russian.json, so ours are added again right after it; a later language setup re-adds them through Jotunn.
    // No reference to any of these DLLs.
    internal static class ModText
    {
        // From Plugin.Start: the mods may load after us.
        internal static void Register(ConfigFile config, Harmony harmony)
        {
            if (!config.Bind("Compat", "ModText", true,
                    "Translate the English-only texts of PlantEverything into the game language. Does nothing without it.").Value) return;
            if (Loaded(ModCompat.PlantEverythingGuid) is Assembly everything)
            {
                var init = everything.GetType("Advize_PlantEverything.PluginInitUtils")?.GetMethod("InitLocalization", BindingFlags.Public | BindingFlags.Static);
                if (init == null) Plugin.Log.LogWarning("[Compat] PlantEverything: PluginInitUtils.InitLocalization not found; its texts stay English.");
                else harmony.Patch(init, postfix: new HarmonyMethod(typeof(ModText), nameof(PlantEverythingWords)));
            }
        }

        private static Assembly Loaded(string guid) =>
            Chainloader.PluginInfos.TryGetValue(guid, out var info) && info.Instance ? info.Instance.GetType().Assembly : null;

        // Our "pe*" words of the selected language (none in English.json: English stays PlantEverything's own).
        private static void PlantEverythingWords()
        {
            var loc = Localization.instance;
            foreach (var w in Plugin.Texts.GetTranslations(loc.GetSelectedLanguage()))
                if (w.Key.StartsWith("pe", StringComparison.Ordinal)) loc.AddWord(w.Key, w.Value);
        }
    }
}
