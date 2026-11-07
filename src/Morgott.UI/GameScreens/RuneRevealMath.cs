using System;
using System.Text;

namespace Morgott.UI.GameScreens
{
    // Rune stone reveal (mockups docs\mockups\2026-11-07\runestone-*): the localized text first drawn letter by letter in
    // the vanilla rune face (TMP "Valheim-Rune"), then decoded left to right into the readable display face. Pure math,
    // linked into the tests.
    public static class RuneRevealMath
    {
        // Share of the text the decoding front blends over (calm front: a soft gradient, not a hard edge).
        public const float Window = 0.12f;

        // Each letter kept 1:1 (same index in both layers), drawn by the first candidate the rune face has a visible glyph
        // for. The vanilla rune TTF maps Latin + Cyrillic to runes but leaves C E Б Е Ё З К П Ф Ц Ч (both cases) empty
        // (0 contours, checked 2026-11-07 on resources.assets Font "rune").
        public static string Remap(string text, Func<char, bool> hasGlyph)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var sb = new StringBuilder(text.Length);
            var inTag = false;
            foreach (var c in text)
            {
                if (c == '<') inTag = true;
                if (inTag || char.IsWhiteSpace(c) || !char.IsLetter(c) || hasGlyph(c)) sb.Append(c);
                else sb.Append(Substitute(c, hasGlyph));
                if (c == '>') inTag = false;
            }
            return sb.ToString();
        }

        private static char Substitute(char c, Func<char, bool> hasGlyph)
        {
            var upper = char.IsUpper(c);
            foreach (var cand in Candidates(char.ToUpperInvariant(c)))
            {
                var x = upper ? cand : char.ToLowerInvariant(cand);
                if (hasGlyph(x)) return x;
            }
            return c; // no candidate drawn either: keep the letter (the fallback face shows it)
        }

        // Sound-alike letters first (Cyrillic -> Latin too, in case the rune face lacks a Cyrillic letter), then any rune.
        private static string Candidates(char u) => u switch
        {
            'C' => "KS",
            'E' or 'Е' or 'Ё' or 'Э' => "ЭIИ",
            'А' => "A",
            'Б' => "B",
            'В' => "V",
            'Г' => "G",
            'Д' => "D",
            'Ж' => "ZШ",
            'З' => "Z",
            'И' or 'Й' or 'І' or 'Ї' => "I",
            'К' => "K",
            'Л' => "L",
            'М' => "M",
            'Н' => "N",
            'О' => "O",
            'П' => "P",
            'Р' => "R",
            'С' => "S",
            'Т' => "T",
            'У' => "U",
            'Ф' => "F",
            'Х' => "H",
            'Ц' => "ZS",
            'Ч' => "QШ",
            'Ш' or 'Щ' => "SШ",
            'Ъ' or 'Ь' => "Ь",
            'Ы' => "Y",
            'Ю' => "U",
            'Я' => "A",
            'Є' => "Э",
            'Ґ' => "G",
            _ => "",
        } + "AIRT";

        // Decode progress of the letter at index i of n at t seconds after the window opened: 0 = rune, 1 = readable.
        // Holds the runes for hold s, then a front crosses the text in decode s (letters at i/n reached in order).
        public static float Progress(int i, int n, float t, float hold, float decode)
        {
            if (n <= 0 || t >= hold + decode) return 1f;
            if (t <= hold) return 0f;
            var front = (t - hold) / Math.Max(decode, 1e-4f) * (1f + Window);
            var p = (front - (float)i / n) / Window;
            return p <= 0f ? 0f : p >= 1f ? 1f : p * p * (3f - 2f * p);
        }

        public static bool Done(float t, float hold, float decode) => t >= hold + decode;

        // The vanilla English rune line (TextViewer.m_runeText): hidden while the reveal is on, vanilla when it is off.
        public static string VanillaRuneLine(bool revealOn, string vanilla) => revealOn ? "" : vanilla;
    }
}
