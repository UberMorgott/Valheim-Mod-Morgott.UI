using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Morgott.UI.Tests
{
    /// <summary>
    /// Tooltip direction audit (2026-11-23): each key states whose damage / stagger it changes. Taken = what hits the
    /// wearer / creature, Dealt = what it hits with. RU and EN carry the side's words and never the opposite ones.
    /// </summary>
    public class TooltipDirectionChecks
    {
        private enum Side { Taken, Dealt }

        private static readonly (string Key, Side Side, string En, string Ru)[] Table =
        {
            // StatsReader.Combat: the row reads Epic Loot ModifyStaggerDamage, the stagger the player's hits deal.
            ("ui_stats_stagger", Side.Dealt, "Stagger dealt", "Наносимое ошеломление"),
        };

        internal static Dictionary<string, string> Load(string language)
        {
            using var s = typeof(TooltipDirectionChecks).Assembly.GetManifestResourceStream($"Morgott.UI.Translations.{language}.json");
            Assert.NotNull(s);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(s);
        }

        [Fact]
        public void WordingMatchesTheSide()
        {
            var en = Load("English");
            var ru = Load("Russian");
            foreach (var (key, side, enWord, ruWord) in Table)
            {
                Assert.True(en.TryGetValue(key, out var e), $"English.json misses {key}");
                Assert.True(ru.TryGetValue(key, out var r), $"Russian.json misses {key}");
                Assert.Contains(enWord, e, StringComparison.OrdinalIgnoreCase);
                Assert.Contains(ruWord, r, StringComparison.OrdinalIgnoreCase);
                foreach (var wrong in side == Side.Taken ? new[] { "dealt", "наносим" } : new[] { "taken", "you take", "получаем" })
                {
                    Assert.DoesNotContain(wrong, e, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain(wrong, r, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }
}
