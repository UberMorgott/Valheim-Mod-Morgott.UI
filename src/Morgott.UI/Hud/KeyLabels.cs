using System.Collections.Generic;

namespace Morgott.UI.PlayerHud
{
    // Key hints stay Latin at any game language (user rule, memory key-hints-english): ZInput.GetBoundKeyString answers
    // the mouse / modifier keys as "$button_*" tokens (ZInput.s_keyLocalizationMap) that Localization.GetBoundKeyString
    // then translates ("$button_mouse0" -> "ЛКМ" in Russian, the build key hint "Разместить ЛКМ", 2026-11-08). The binding
    // itself is still read live from the input config; only the token's text is Latin. Pure data, unit-tested.
    internal static class KeyLabels
    {
        // Every token of ZInput.s_keyLocalizationMap (game 1.0.21) with a "$" value.
        internal static readonly Dictionary<string, string> Latin = new Dictionary<string, string>
        {
            { "$button_mouse0", "LMB" },
            { "$button_mouse1", "RMB" },
            { "$button_mouse2", "MMB" },
            { "$button_space", "Space" },
            { "$button_lshift", "Shift" },
            { "$button_rshift", "Right Shift" },
            { "$button_lalt", "Alt" },
            { "$button_ralt", "Right Alt" },
            { "$button_lctrl", "Ctrl" },
            { "$button_rctrl", "Right Ctrl" },
            { "$button_return", "Enter" }
        };

        // A letter key printed in the active keyboard layout (Input System ToDisplayString with Keyboard.current: Q -> "Й"
        // on a Russian layout, run 20261108-051942 snap hint): the label is then taken from the binding path instead.
        internal static bool IsLayoutGlyph(string s) => s != null && s.Length == 1 && s[0] > 127;

        // Latin label for a ZInput key string, or null to keep the game's text (letters, glyphs, English).
        internal static string For(string zinputKey, string language) =>
            language != "English" && zinputKey != null && Latin.TryGetValue(zinputKey, out var latin) ? latin : null;
    }
}
