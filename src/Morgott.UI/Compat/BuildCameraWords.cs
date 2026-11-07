using System.Collections.Generic;

namespace Morgott.UI.Compat
{
    // ValheimBuildCamera (Stonaar, 1.15.0 / DLL 0.1.32) hardcodes its English HUD texts (IMGUI badges, the clear-view
    // button, the multi-build TMP panel, MessageHud notices). English literal -> Morgott.UI token (Translations\*.json,
    // the English value is the literal itself). Fragments of concatenations are listed as the mod writes them (counts and
    // key labels stay the mod's, Latin). Pure data: unit-tested against both translation files.
    internal static class BuildCameraWords
    {
        // Every ldstr with one of these literals in the patched types is followed by BuildCameraText.Tr. None of them is
        // compared by the mod (ValheimBuildCamera source: only StopCamera's reason switch compares literals, see Reasons).
        internal static readonly Dictionary<string, string> Texts = new Dictionary<string, string>
        {
            // BuildCamera notices
            { "Build camera settings reloaded; diagnostic report saved.", "ui_bc_reloaded" },
            { "Equip your hammer or another building tool first.", "ui_bc_need_tool" },
            { "Stand on solid ground to enter the build camera.", "ui_bc_need_ground" },
            { "Return to the normal game camera first.", "ui_bc_need_normal_camera" },
            { "The game camera is not ready yet.", "ui_bc_camera_not_ready" },
            { "Build camera ON. Jump: up; Crouch: down. F6 or press both stick clicks: exit.", "ui_bc_on" },
            { OffPrefix, "ui_bc_off" },
            // Multi-build hint / panel (UpdateGridUI, EnsureGridUI, GridLiveStatus)
            { "Place button", "ui_bc_place_button" },
            { "Rows", "ui_bc_rows" },
            { "Columns", "ui_bc_columns" },
            { "Select piece", "ui_bc_select_piece" },
            { "← / → Width    ↑ / ↓ ", "ui_bc_pad_width" },
            { "\nLB + ↑ / ↓  Change layout\nLB + Y  Leave multi-build mode", "ui_bc_pad_layout_leave" },
            { "← / →  Fewer / more columns\n↑ / ↓  More / fewer rows\nShift + F9  Change layout\n", "ui_bc_keys_resize" },
            { "  Build preview\n", "ui_bc_build_preview" },
            { "  Leave multi-build mode", "ui_bc_leave_multi" },
            { "  Enter multi-build mode", "ui_bc_enter_multi" },
            { "Rows (", "ui_bc_rows_open" },
            { "height", "ui_bc_height_lower" },
            { "Auto ✓", "ui_bc_auto_on" },
            { "Auto", "ui_bc_auto" },
            { "Upright ✓", "ui_bc_upright_on" },
            { "Upright", "ui_bc_upright" },
            { "Ground ✓", "ui_bc_ground_on" },
            { "Ground", "ui_bc_ground" },
            { "Building paused. Use normal controls.", "ui_bc_paused" },
            { "Click − / + to resize.\nUse normal controls. ", "ui_bc_panel_resize" },
            { " to build.\nAuto chooses the piece layout.", "ui_bc_panel_build" },
            { "Choose a piece in the Build Menu.", "ui_bc_choose_piece" },
            { "Aim at a surface to see the preview.", "ui_bc_aim_surface" },
            { " piece", "ui_bc_piece_count" },
            { " pieces", "ui_bc_pieces_count" },
            { " · Place to build", "ui_bc_place_to_build" },
            { "Set preview size", "ui_bc_set_size" },
            { "Resume", "ui_bc_resume" },
            { "Single piece", "ui_bc_single" },
            { "Choose a building piece, then aim to preview.", "ui_bc_choose_then_aim" },
            { "Aim at a valid starting position.", "ui_bc_aim_start" },
            { "Building from the bottom row…", "ui_bc_building" },
            { "Waiting for stamina…", "ui_bc_wait_stamina" },
            { "Built ", "ui_bc_built" },
            { "Stopped. Placed pieces remain.", "ui_bc_stopped" },
            { "Stopped: piece or tool changed.", "ui_bc_stopped_changed" },
            { "Stopped: repair your tool.", "ui_bc_stopped_repair" },
            { "Stopped: outside build reach.", "ui_bc_stopped_reach" },
            { "Stopped: a piece is already here.", "ui_bc_stopped_occupied" },
            { "Stopped: blocked, or missing materials/station.", "ui_bc_stopped_blocked" },
            { "Finished: ", "ui_bc_finished" },
            { " pieces built.", "ui_bc_finished_tail" },
            // GridOrientation basis labels (title + row label)
            { "Stairs", "ui_bc_stairs" },
            { "Roof slope", "ui_bc_roof_slope" },
            { "Incline", "ui_bc_incline" },
            { "Depth", "ui_bc_depth" },
            { "Height", "ui_bc_height" },
            { "Length", "ui_bc_length" },
            // OcdCompatibility / DirectResourceReturn
            { "Multi-build stopped after a placement error. Camera remains available.", "ui_bc_ocd_cancel" },
            { "Multi-build stopped after a placement error. You can keep using the camera.", "ui_bc_ocd_message" },
            { "Carry weight or inventory full: remaining materials are at the dismantled piece.", "ui_bc_return_full" },
            // ClearBuildingView
            { "Clear building view unavailable: clear weather preset was not found.", "ui_bc_clear_unavailable" },
            { "Clear building view ON: sunny noon, mist hidden (only you).", "ui_bc_clear_on_notice" },
            { "Clear building view OFF: normal weather and time restored.", "ui_bc_clear_off_notice" },
            { "Clear building view: ", "ui_bc_clear_button" },
            { "ON", "ui_bc_state_on" },
            { "OFF", "ui_bc_state_off" },
            // DismantlePreview badges
            { "Remove: checking…", "ui_bc_remove_checking" },
            { "Cannot calculate affected pieces", "ui_bc_cannot_calculate" },
            { "Dismantle preview unavailable — see log", "ui_bc_preview_unavailable" },
            { "Destroying will affect ", "ui_bc_affects" },
            { " other pieces", "ui_bc_affects_tail" },
            { "Destroying will affect 0 other pieces", "ui_bc_affects_none" },
            { "Build: blocked", "ui_bc_build_blocked" },
            { "Build: checking…", "ui_bc_build_checking" },
            { "Safe", "ui_bc_safe" },
            { "Not safe", "ui_bc_not_safe" },
            { "Build: support unknown", "ui_bc_support_unknown" }
        };

        // "Build camera OFF: " + reason + "." in StopCamera: the reason argument is translated after the prefix only, never
        // at the callers (StopCamera switches on "Toggled off" / "Escape" / "Configuration reload"). Only the reasons of
        // StopCamera(..., message: true) are shown; "Escape" is a key name and stays Latin.
        internal const string OffPrefix = "Build camera OFF: ";

        internal static readonly Dictionary<string, string> Reasons = new Dictionary<string, string>
        {
            { "Toggled off", "ui_bc_reason_toggled" },
            { "You took damage", "ui_bc_reason_damage" }
        };
    }
}
