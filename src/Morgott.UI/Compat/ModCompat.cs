namespace Morgott.UI.Compat
{
    // Every other mod RuneSkin adapts to, by name (perf audit B5: these names were spread over a dozen files). All soft:
    // no reference, nothing breaks when a mod is absent or renames a type / object (the hook finds nothing, logs, and
    // the vanilla look stays). Third-party mods are matched here; own mods register through Morgott.UI.API where the API
    // has the capability (AdventureBackpacks fork: ABAPI.GetBackpackPanel + Skin.RegisterPanel("inventory.backpack"),
    // MorgottTweaks: Skin.RegisterPanelAction / RegisterResourceProvider) and are listed here only for what the API does
    // not carry yet (object names their UI is placed by, ZDO keys they publish).
    public static class ModCompat
    {
        // ---- skins and frameworks
        public const string ValheimPlusGuid = "org.bepinex.plugins.valheim_plus";
        public const string JotunnAssembly = "Jotunn";
        public const string JotunnGuiManager = "Jotunn.Managers.GUIManager";

        // ---- Equipment and Quick Slots (randyknapp): doll cells read from its slot table
        public const string EaqsGuid = "randyknapp.mods.equipmentandquickslots";
        public const string EaqsSlotsType = "EquipmentAndQuickSlots.Slots";
        public const string EaqsContainerDragType = "EquipmentAndQuickSlots.ContainerWindow+ContainerDragHandle";
        // Its pool of unused cells (EquipmentPanel.InventoryGrid_UpdateGui_RelocateSlotElements.EnsureHiddenRoot: an inactive RectTransform under
        // m_player, kept by its static reference, cells parked into it by ParkHiddenCell).
        public const string EaqsHiddenRoot = "EaqsHiddenSlotRoot";


        // ---- Epic Loot (RandyKnapp)
        public const string EpicLootGuid = "randyknapp.mods.epicloot";
        public const string EpicLootAssembly = "EpicLoot";
        public const string EpicLootEnchantingUi = "EpicLoot_UnityLib.EnchantingTableUI";
        public const string EpicLootMerchantPanel = "EpicLoot.Adventure.MerchantPanel";
        public const string EpicLootTemperPanel = "EpicLoot.TemperPanel";
        public const string EpicLootAbilityBar = "EpicLoot.Abilities.AbilityBar";
        public const string EpicLootDebugText = "EpicLoot.DebugText";
        // Epic Loot's config-update prompt (ConfigUpdatePrompt: ConfigMessagePrefab under StartGui on the main menu, shown
        // once after Epic Loot's config version changes; 0.15.6 run 20261103-095927).
        public const string EpicLootConfigMessage = "EpicLoot.ConfigMessage";
        // 0.15 TraderPanelDrag (EpicLoot\TraderPanelDrag.cs) on MerchantPanel / TemperPanel: a clear full-panel hit
        // surface shown while the drag key is held, and a corner move handle whose colour it writes every frame.
        public const string EpicLootDragSurface = "ModifierDragSurface";
        public const string EpicLootDragHandle = "MoveHandle";

        // ---- CurrencyPocket: coin plate in the inventory footer
        public const string CoinPocketPlate = "CoinPocketUI";

        // ---- own mods: what the API does not carry yet
        // AdventureBackpacks fork 1.9.15+: docking API (AbApi); no name fallback.
        public const string AbApiType = "AdventureBackpacks.API.ABAPI";
        // Morgott.Creatures (was Vaettir): rarity frame child per enemy HUD (its tier sprite), its plugin id.
        public const string CreaturesFrame = "CR_Frame";
        public const string CreaturesGuid = Morgott.Core.Contracts.CreaturesGuid;
        public const string StarLevelSystemGuid = "MidnightsFX.StarLevelSystem";

        // ---- Icons\ModStatusIcons, Compat\CurrencyPocketTooltip (read from the installed plugins' [BepInPlugin])
        public const string WarfareGuid = "Therzie.Warfare";
        public const string CurrencyPocketGuid = "Azumatt.CurrencyPocket";
    }
}
