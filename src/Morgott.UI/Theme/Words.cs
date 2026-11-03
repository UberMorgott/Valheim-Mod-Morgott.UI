using System;
using System.Collections.Generic;

namespace Morgott.UI.Theme
{
    // Own texts are "$ui_*" localization keys from Translations\<Language>.json (Jotunn LocalizationManager via
    // Morgott.Core Translations.Load in Plugin.Awake; languages without a file fall back to English).
    public static class Words
    {
        // Localization.Localize builds its result in a new StringBuilder per call (Localization.cs): per-frame callers
        // (map legend labels) read a cache instead, dropped when the language changes. Bounded (texts of mods'
        // buttons are keys too).
        private static readonly Dictionary<string, string> Localized = new Dictionary<string, string>();
        private static string _language;
        public const int MaxLocalized = 1024;

        public static string Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            var loc = Localization.instance;
            if (loc == null) return key;
            var language = loc.GetSelectedLanguage();
            if (!ReferenceEquals(language, _language) && language != _language)
            {
                Localized.Clear();
                _language = language;
            }
            if (Localized.TryGetValue(key, out var text)) return text;
            text = loc.Localize(key);
            // "[key]" = not translated yet (a mod may add its words later): asked again next time.
            if (text.StartsWith("[", StringComparison.Ordinal)) return text;
            if (Localized.Count >= MaxLocalized) Localized.Clear();
            Localized[key] = text;
            return text;
        }
    }
}
