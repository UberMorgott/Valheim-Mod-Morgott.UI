using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using Morgott.UI.Theme;

namespace Morgott.UI.Compat
{
    // ValheimBuildCamera's hardcoded English HUD texts in the game language (BuildCameraWords). Its IMGUI labels and TMP
    // texts are never localized by the game, so every listed literal in the display types is followed by a call to Tr
    // (English stays the literal: English.json holds it as the value). Methods are picked by scanning their IL for a listed
    // literal; no reference to ValheimBuildCamera.dll.
    internal static class BuildCameraText
    {
        private static readonly string[] DisplayTypes =
        {
            "ValheimBuildCamera.BuildCamera", "ValheimBuildCamera.ClearBuildingView", "ValheimBuildCamera.DismantlePreview",
            "ValheimBuildCamera.OcdCompatibility", "ValheimBuildCamera.DirectResourceReturn", "ValheimBuildCamera.GridOrientation"
        };

        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        // From Plugin.Start: the build camera may load after us.
        internal static void Register(ConfigFile config, Harmony harmony)
        {
            if (!config.Bind("Compat", "BuildCameraText", true,
                    "Translate ValheimBuildCamera's hardcoded English HUD texts into the game language. Does nothing without it.").Value) return;
            if (!Chainloader.PluginInfos.TryGetValue(ModCompat.BuildCameraGuid, out var info) || !info.Instance) return;
            var asm = info.Instance.GetType().Assembly;
            var transpiler = new HarmonyMethod(typeof(BuildCameraText), nameof(Transpiler));
            var patched = 0;
            foreach (var name in DisplayTypes)
            {
                var type = asm.GetType(name);
                if (type == null)
                {
                    Plugin.Log.LogWarning($"[Compat] ValheimBuildCamera {info.Metadata.Version}: {name} not found; its texts stay English.");
                    continue;
                }
                var types = new List<Type>();
                CollectNested(type, types);
                for (var i = 0; i < types.Count; i++)
                    foreach (var m in types[i].GetMethods(All))
                        if (!m.IsAbstract && !m.ContainsGenericParameters && HasText(m))
                        {
                            harmony.Patch(m, transpiler: transpiler);
                            patched++;
                        }
            }
            Plugin.Log.LogInfo($"[Compat] ValheimBuildCamera {info.Metadata.Version}: texts translated in {patched.ToString()} methods");
        }

        private static void CollectNested(Type type, List<Type> list)
        {
            list.Add(type);
            var nested = type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);
            for (var i = 0; i < nested.Length; i++) CollectNested(nested[i], list);
        }

        private static bool HasText(MethodBase m)
        {
            try
            {
                var body = PatchProcessor.ReadMethodBody(m);
                var ops = body as IList<KeyValuePair<OpCode, object>> ?? new List<KeyValuePair<OpCode, object>>(body);
                for (var i = 0; i < ops.Count; i++)
                {
                    var op = ops[i];
                    if (op.Key == OpCodes.Ldstr && op.Value is string s && BuildCameraWords.Texts.ContainsKey(s)) return true;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Compat] ValheimBuildCamera: cannot read {m.DeclaringType?.Name}.{m.Name}: {e.Message}");
            }
            return false;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var tr = AccessTools.Method(typeof(BuildCameraText), nameof(Tr));
            var reason = AccessTools.Method(typeof(BuildCameraText), nameof(Reason));
            var afterOff = false;
            var list = instructions as IList<CodeInstruction> ?? new List<CodeInstruction>(instructions);
            for (var i = 0; i < list.Count; i++)
            {
                var ins = list[i];
                yield return ins;
                // StopCamera: "Build camera OFF: " + reason + "." -> the reason argument right after the prefix.
                if (afterOff && ins.IsLdarg()) yield return new CodeInstruction(OpCodes.Call, reason);
                afterOff = false;
                if (ins.opcode == OpCodes.Ldstr && ins.operand is string s && BuildCameraWords.Texts.ContainsKey(s))
                {
                    yield return new CodeInstruction(OpCodes.Call, tr);
                    afterOff = s == BuildCameraWords.OffPrefix;
                }
            }
        }

        public static string Tr(string english) =>
            BuildCameraWords.Texts.TryGetValue(english, out var key) ? Localized(key, english) : english;

        public static string Reason(string english) =>
            english != null && BuildCameraWords.Reasons.TryGetValue(english, out var key) ? Localized(key, english) : english;

        // Words.Get answers "$key" without a Localization instance and "[key]" for a missing token: keep the English then.
        internal static string Localized(string key, string english)
        {
            var text = Words.Get("$" + key);
            return text.Length == 0 || text[0] == '$' || text[0] == '[' ? english : text;
        }
    }
}
