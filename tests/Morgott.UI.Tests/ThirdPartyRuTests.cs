using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Morgott.UI.Tests
{
    // Russian for third-party texts that ship English only (Jotunn's version-compat dialog).
    public class ThirdPartyRuTests
    {
        private static Dictionary<string, string> Load(string language)
        {
            using var s = typeof(ThirdPartyRuTests).Assembly.GetManifestResourceStream($"Morgott.UI.Translations.{language}.json");
            Assert.NotNull(s);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(s);
        }

        // Jotunn 2.x Jotunn/Translations/English.json: token -> placeholders it uses. Jotunn adds every English word first,
        // then the selected language (LocalizationManager.LoadAndSetupModLanguages), so English stays Jotunn's own.
        private static readonly Dictionary<string, string[]> JotunnCompat = new Dictionary<string, string[]>
        {
            { "mod_compat_header_failed_connection", new string[0] },
            { "mod_compat_header_remote_version", new string[0] },
            { "mod_compat_header_local_version", new string[0] },
            { "mod_compat_header_valheim_version", new string[0] },
            { "mod_compat_header_missing_mods", new string[0] },
            { "mod_compat_header_update_needed", new string[0] },
            { "mod_compat_header_downgrade_needed", new string[0] },
            { "mod_compat_header_additional_mods", new string[0] },
            { "mod_compat_header_further_steps", new string[0] },
            { "mod_compat_valheim_version_error_description", new[] { "$1", "$2" } },
            { "mod_compat_valheim_version_upgrade", new string[0] },
            { "mod_compat_valheim_version_downgrade", new string[0] },
            { "mod_compat_further_steps_description", new[] { "BepInEx/LogOutput.log" } },
            { "mod_compat_mod_update", new[] { "$1", "$2" } },
            { "mod_compat_mod_downgrade", new[] { "$1", "$2" } },
            { "mod_compat_missing_mods_description", new string[0] },
            { "mod_compat_additional_mods_description", new string[0] },
            { "mod_compat_missing_mod", new[] { "$1", "$2" } },
            { "mod_compat_additional_mod", new[] { "$1", "$2" } },
            { "mod_compat_open_log_file", new string[0] },
            { "mod_compat_troubleshooting", new string[0] }
        };

        [Fact]
        public void JotunnCompatDialogHasRussianWithPlaceholders()
        {
            var ru = Load("Russian");
            var en = Load("English");
            foreach (var (key, keep) in JotunnCompat)
            {
                Assert.True(ru.TryGetValue(key, out var value) && value.Trim().Length > 0, $"Russian.json misses {key}");
                foreach (var k in keep) Assert.Contains(k, value);
                Assert.False(en.ContainsKey(key), $"English.json must not override Jotunn's own {key}");
            }
            Assert.Equal(21, ru.Keys.Count(k => k.StartsWith("mod_compat_")));
        }
    }
}
