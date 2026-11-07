using System.Collections.Generic;

namespace Morgott.UI.Compat
{
    // Hardcoded English texts of RossItemDrawers (ItemDrawers 1.3.0 + ItemDrawers.Core), PlantEasily (2.3.1) and
    // ValheimPlus (10.4.0):
    // English literal -> Morgott.UI token (Translations\*.json, the English value is the literal itself). Fragments are
    // listed as the mods concatenate them (markup, key labels and format placeholders stay). None of them is compared
    // by the mods. Pure data: unit-tested against both translation files.
    internal static class ModWords
    {
        internal static readonly Dictionary<string, string> Texts = new Dictionary<string, string>
        {
            // ItemDrawers: DrawerTiers.DisplayName (piece name + hover name), piece description
            { "Item Drawer", "ui_dr_wood" },
            { "Stone Item Drawer", "ui_dr_stone" },
            { "Black Marble Item Drawer", "ui_dr_blackmarble" },
            { "Holds a great many of a single item.", "ui_dr_description" },
            // DrawerComponent.GetHoverText
            { "\n[<color=yellow><b>1-8</b></color>] Store an item", "ui_dr_store_item" },
            { "[<color=yellow><b>E</b></color>] Take stack", "ui_dr_take_stack" },
            { "Take one", "ui_dr_take_one" },
            { "Unassign", "ui_dr_unassign" },
            { "+E</b></color>] Store all", "ui_dr_store_all" },
            // Player messages
            { "Stored {0} {1}", "ui_dr_stored" },
            { "Try again", "ui_dr_try_again" },
            { "Drawers only hold stackable items", "ui_dr_stackable_only" },
            { "Cannot identify this item", "ui_dr_unknown_item" },
            { "Could not remove item", "ui_dr_remove_failed" },
            { "This drawer has not been assigned", "ui_dr_not_assigned" },
            { "You have none of those", "ui_dr_none_carried" },
            // ItemDrawers.Core DrawerRules refusals (DrawerOutcome.Rejection, shown as a center message)
            { "This drawer holds a different item", "ui_dr_wrong_item" },
            { "This drawer is full", "ui_dr_full" },
            { "This drawer has been drained", "ui_dr_drained" },
            { "This drawer has an invalid capacity", "ui_dr_bad_capacity" },
            { "Empty the drawer before clearing it", "ui_dr_not_empty" },
            { "Nothing to deposit", "ui_dr_nothing" },
            // PlantEasily: build HUD key hints (keyboard label is 75 px wide), hover hints
            { "Grid Size", "ui_pz_grid" },
            { "Resize Grid ", "ui_pz_resize_grid" },
            { " (area)", "ui_pz_area" },
            { "\nReplant as: <color=green>", "ui_pz_replant_as" },
            // ValheimPlus 10.4.0: hover timers (beehive, fermenter, sap collector, egg, growth, procreation)
            { " seconds", "ui_vp_seconds" },
            { " minutes", "ui_vp_minutes" },
            { "{0} seconds", "ui_vp_n_seconds" },
            { "{0} minutes", "ui_vp_n_minutes" },
            { "\nTime left: ", "ui_vp_time_left" },
            { " ( Matures in ", "ui_vp_matures_in" },
            { " seconds )", "ui_vp_seconds_close" },
            { " minutes )", "ui_vp_minutes_close" },
            { " ( Matured )", "ui_vp_matured" },
            { "\n<color=#FFAEC9>Pregnant", "ui_vp_pregnant" },
            { " seconds left )", "ui_vp_seconds_left" },
            { " minutes left )", "ui_vp_minutes_left" },
            { " ( Overdue )", "ui_vp_overdue" },
            { " ( Due to give birth )", "ui_vp_due" },
            { "\nNot loved", "ui_vp_not_loved" },
            { "\nLoved ( {0} / {1} )", "ui_vp_loved" },
            { ", Stunned", "ui_vp_stunned" },
            // Advanced building / editing mode notices
            { "Starting ABM", "ui_vp_abm_start" },
            { "Exiting ABM", "ui_vp_abm_exit" },
            { "ABM is active", "ui_vp_abm_active" },
            { "Entering AEM", "ui_vp_aem_start" },
            { "Exiting AEM", "ui_vp_aem_exit" },
            { "AEM is active", "ui_vp_aem_active" },
            { "Modification Speed: ", "ui_vp_mod_speed" },
            // Grid alignment notices
            { "Default grid alignment set to ", "ui_vp_grid_default" },
            { "Grid alignment by default ", "ui_vp_grid_toggle" },
            // Ammo HUD, portals, audio settings toggle, connection-failed dialog
            { "No Ammo", "ui_vp_no_ammo" },
            { "Portals have been disabled on this Server.", "ui_vp_portals_disabled" },
            { "Mute game in background", "ui_vp_mute_background" },
            { "\nServer expects you to have ", "ui_vp_server_expects" },
            { " (Version: ", "ui_vp_version" },
            { ") installed.", "ui_vp_installed" }
        };

        // Literals ValheimPlus also uses as config keys / parsed values elsewhere ("enabled"): translated only in a
        // method that also loads the anchor literal (its grid alignment toggle notice).
        internal static readonly Dictionary<string, (string Token, string Anchor)> Scoped = new Dictionary<string, (string, string)>
        {
            { "enabled", ("ui_vp_enabled", "Grid alignment by default ") },
            { "disabled", ("ui_vp_disabled", "Grid alignment by default ") }
        };
    }
}
