using System.IO;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Managers;
using Morgott.UI.Compat;
using UnityEngine;

namespace Morgott.UI.Icons
{
    // Icons for other mods' damage-over-time / slow status effects that ship without one (ModStatusIconMap). Vanilla shows
    // a status only with an icon (SEMan.GetHUDStatusEffects and every HUD that follows it, Creatures' enemy panel
    // included). The icon goes on the status effect itself when its mod builds it, only where m_icon is still null: SEMan
    // copies effects with MemberwiseClone (StatusEffect.Clone), so every running copy carries it. Art: Assets\StatusIcons.png
    // (64 px cells, 8 per row, made by MorgottTweaks Assets\icons\pack_aura_icons.py --spec status_icons.json). No reference
    // to Warfare.dll or EpicLoot.dll.
    internal static class ModStatusIcons
    {
        private const int Cell = 64, Columns = 8;

        private static Sprite[] _sprites;
        private static bool _tried;
        private static MethodInfo _elPrototype;

        // Effects given an icon / prototypes put into ObjectDB since start (autotest).
        internal static int Set { get; private set; }
        internal static int Registered { get; private set; }

        // From Plugin.Start: Warfare / Epic Loot may load after us; their effects are created later (ObjectDB.Awake / first hit).
        internal static void Register(ConfigFile config, Harmony harmony)
        {
            if (!config.Bind("Compat", "ModStatusIcons", true,
                    "Give other mods' icon-less damage-over-time and slow effects (Warfare bleeding / impale, Epic Loot " +
                    "paralyze) an icon, so they show on the HUD and on enemy status rows. Takes effect on the next game launch.").Value)
                return;
            var postfix = new HarmonyMethod(typeof(ModStatusIcons), nameof(Fill));
            var patched = 0;

            // Warfare: each effect sets m_icon from its class' static Icon in OnEnable (run by the CreateInstance in its
            // ObjectDB.Awake/CopyOtherDB postfixes); the postfix fills the ones Warfare left empty.
            if (Chainloader.PluginInfos.TryGetValue(ModCompat.WarfareGuid, out var warfare) && warfare.Instance)
                foreach (var type in AccessTools.GetTypesFromAssembly(warfare.Instance.GetType().Assembly))
                {
                    if (!typeof(StatusEffect).IsAssignableFrom(type)) continue;
                    var onEnable = type.GetMethod("OnEnable",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    if (onEnable == null) continue;
                    harmony.Patch(onEnable, postfix: postfix);
                    patched++;
                }

            // Epic Loot: Paralyze.GetOrCreatePrototype builds the EL_Paralyze prototype every paralyze is cloned from.
            if (Chainloader.PluginInfos.TryGetValue(ModCompat.EpicLootGuid, out var epicLoot) && epicLoot.Instance)
            {
                _elPrototype = AccessTools.Method(epicLoot.Instance.GetType().Assembly.GetType("EpicLoot.MagicItemEffects.Paralyze"),
                    "GetOrCreatePrototype");
                if (_elPrototype != null)
                {
                    harmony.Patch(_elPrototype, postfix: new HarmonyMethod(typeof(ModStatusIcons), nameof(PrototypePostfix)));
                    patched++;
                    // Every main-scene ObjectDB.Awake (Jotunn's postfix), the hook Epic Loot registers its own effects on.
                    ItemManager.OnItemsRegistered += RegisterElParalyze;
                }
                else
                {
                    Plugin.Log.LogWarning("[Compat] Epic Loot: Paralyze.GetOrCreatePrototype not found; paralyze stays icon-less.");
                }
            }

            if (patched > 0) Plugin.Log.LogInfo($"[Compat] mod status icons: {patched} effect builders patched.");
        }

        // Puts Epic Loot's EL_Paralyze prototype into ObjectDB.m_StatusEffects. Vanilla sends a status over the wire as its
        // name hash and resolves it through ObjectDB (SEMan.RPC_AddStatusEffect -> ObjectDB.GetStatusEffect); Epic Loot
        // keeps the prototype private, so a client reading another owner's status rows by hash found no effect and no icon.
        // Epic Loot itself only clones the prototype and never looks EL_Paralyze up in ObjectDB.
        private static void RegisterElParalyze()
        {
            var db = ObjectDB.instance;
            if (!db || !(_elPrototype.Invoke(null, null) is StatusEffect se) || !ModStatusIconMap.RegistersInObjectDb(se.name)) return;
            Fill(se); // the prototype postfix already ran inside Invoke; a no-op then
            if (db.GetStatusEffect(se.NameHash()) != null) return;
            db.m_StatusEffects.Add(se);
            Registered++;
        }

        private static void PrototypePostfix(StatusEffect __result) => Fill(__result);

        private static void Fill(StatusEffect __instance)
        {
            if (!__instance || __instance.m_icon) return;
            var cell = ModStatusIconMap.CellOf(__instance.name);
            if (cell < 0) return;
            if (!_tried)
            {
                _tried = true;
                _sprites = Load();
            }
            if (_sprites == null || cell >= _sprites.Length) return;
            __instance.m_icon = _sprites[cell];
            Set++;
        }

        private static Sprite[] Load()
        {
            byte[] png;
            using (var s = typeof(ModStatusIcons).Assembly.GetManifestResourceStream("Morgott.UI.StatusIcons.png"))
            {
                if (s == null)
                {
                    Plugin.Log.LogError("[Compat] embedded StatusIcons.png missing");
                    return null;
                }
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    png = ms.ToArray();
                }
            }
            // ImageConversion.LoadImage by name: UnityEngine.ImageConversionModule targets netstandard 2.1, which this
            // net472 plugin cannot reference at compile time.
            var load = System.Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule")
                ?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });
            if (load == null)
            {
                Plugin.Log.LogError("[Compat] ImageConversion.LoadImage not found");
                return null;
            }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "UI_StatusIcons" };
            load.Invoke(null, new object[] { tex, png, false });
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;
            tex.Apply(true, true);
            var count = System.Math.Min(ModStatusIconMap.Cells.Length, tex.height / Cell * Columns);
            var sprites = new Sprite[count];
            for (var i = 0; i < count; i++)
            {
                // Cells from the top-left, texture origin bottom-left.
                var rect = new Rect(i % Columns * Cell, tex.height - (i / Columns + 1) * Cell, Cell, Cell);
                sprites[i] = Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), 100f);
                sprites[i].name = "UI_StatusIcon_" + ModStatusIconMap.Cells[i];
            }
            Plugin.Log.LogInfo($"[Compat] status icons {tex.width}x{tex.height}, {count} sprites");
            return sprites;
        }
    }
}
