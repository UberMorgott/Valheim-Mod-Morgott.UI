using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;

namespace Morgott.UI.Compat
{
    // Hardcoded English HUD / hover / message texts of RossItemDrawers, PlantEasily and ValheimPlus in the game language (ModWords),
    // the same way as BuildCameraText: every listed literal in the mod's assemblies is followed by a call to Tr. And
    // PlantEverything's own "$pe*" words: it adds them in English only (PluginInitUtils.InitLocalization, from its
    // ZNetScene.Awake patch), over the ones Jotunn already added from Russian.json, so they are added again after it.
    // No reference to any of these DLLs.
    internal static class ModText
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        // From Plugin.Start: the mods may load after us.
        internal static void Register(ConfigFile config, Harmony harmony)
        {
            if (!config.Bind("Compat", "ModText", true,
                    "Translate the hardcoded English texts of RossItemDrawers, PlantEasily, ValheimPlus and PlantEverything into the game language. " +
                    "Does nothing without them.").Value) return;
            if (Loaded(ModCompat.ItemDrawersGuid) is Assembly drawers)
            {
                Patch(harmony, drawers);
                // Refusal messages (DrawerOutcome.Refused reasons) live in its pure logic assembly.
                if (Type.GetType("ItemDrawers.Core.DrawerOutcome, ItemDrawers.Core") is Type outcome) Patch(harmony, outcome.Assembly);
                else Plugin.Log.LogWarning("[Compat] ItemDrawers.Core not found; drawer refusal messages stay English.");
            }
            if (Loaded(ModCompat.PlantEasilyGuid) is Assembly easily) Patch(harmony, easily);
            if (Loaded(ModCompat.ValheimPlusGuid) is Assembly plus) Patch(harmony, plus);
            if (Loaded(ModCompat.PlantEverythingGuid) is Assembly everything)
            {
                var init = everything.GetType("Advize_PlantEverything.PluginInitUtils")?.GetMethod("InitLocalization", BindingFlags.Public | BindingFlags.Static);
                if (init == null) Plugin.Log.LogWarning("[Compat] PlantEverything: PluginInitUtils.InitLocalization not found; its texts stay English.");
                else harmony.Patch(init, postfix: new HarmonyMethod(typeof(ModText), nameof(PlantEverythingWords)));
            }
        }

        private static Assembly Loaded(string guid) =>
            Chainloader.PluginInfos.TryGetValue(guid, out var info) && info.Instance ? info.Instance.GetType().Assembly : null;

        private static void Patch(Harmony harmony, Assembly asm)
        {
            var transpiler = new HarmonyMethod(typeof(ModText), nameof(Transpiler));
            var patched = 0;
            foreach (var t in AccessTools.GetTypesFromAssembly(asm))
                foreach (var m in t.GetMethods(All))
                    if (!m.IsAbstract && !m.ContainsGenericParameters && m.DeclaringType?.ContainsGenericParameters != true && HasText(m))
                    {
                        harmony.Patch(m, transpiler: transpiler);
                        patched++;
                    }
            Plugin.Log.LogInfo($"[Compat] {asm.GetName().Name}: texts translated in {patched} methods");
        }

        private static bool HasText(MethodBase m)
        {
            try
            {
                foreach (var op in PatchProcessor.ReadMethodBody(m))
                    if (op.Key == OpCodes.Ldstr && op.Value is string s && ModWords.Texts.ContainsKey(s)) return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Compat] cannot read {m.DeclaringType?.FullName}.{m.Name}: {e.Message}");
            }
            return false;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var tr = AccessTools.Method(typeof(ModText), nameof(Tr));
            var trScoped = AccessTools.Method(typeof(ModText), nameof(TrScoped));
            var list = instructions.ToList();
            var literals = new HashSet<string>(list.Where(i => i.opcode == OpCodes.Ldstr).Select(i => i.operand as string));
            foreach (var ins in list)
            {
                yield return ins;
                if (ins.opcode != OpCodes.Ldstr || !(ins.operand is string s)) continue;
                if (ModWords.Texts.ContainsKey(s)) yield return new CodeInstruction(OpCodes.Call, tr);
                else if (ModWords.Scoped.TryGetValue(s, out var scoped) && literals.Contains(scoped.Anchor))
                    yield return new CodeInstruction(OpCodes.Call, trScoped);
            }
        }

        public static string Tr(string english) =>
            ModWords.Texts.TryGetValue(english, out var key) ? BuildCameraText.Localized(key, english) : english;

        public static string TrScoped(string english) =>
            ModWords.Scoped.TryGetValue(english, out var scoped) ? BuildCameraText.Localized(scoped.Token, english) : english;

        // Our "pe*" words of the selected language (none in English.json: English stays PlantEverything's own).
        private static void PlantEverythingWords()
        {
            var loc = Localization.instance;
            foreach (var w in Plugin.Texts.GetTranslations(loc.GetSelectedLanguage()))
                if (w.Key.StartsWith("pe", StringComparison.Ordinal)) loc.AddWord(w.Key, w.Value);
        }
    }
}
