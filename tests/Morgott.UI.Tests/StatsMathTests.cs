using System.IO;
using System.Linq;
using System.Text;
using Morgott.Core;
using Morgott.UI.InfoScreens;
using Morgott.UI.Theme;
using Xunit;

namespace Morgott.UI.Tests
{
    // Character stats window (batch P, stats-full-b): regeneration per second, formatting without allocation helpers,
    // design-frame mapping, stacking / scroll, theme tokens of the family.
    public class StatsMathTests
    {
        private static ThemeFile Default()
        {
            using var s = typeof(ThemeSchema).Assembly.GetManifestResourceStream("Morgott.UI.default-theme.json");
            using var r = new StreamReader(s!);
            return ThemeJson.Parse(r.ReadToEnd());
        }

        [Fact]
        public void FoodTooltipStatesHealingPerSecond()
        {
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
                // Vanilla ItemDrop.cs:1084 line for a 3 hp food tick -> 0.3 hp per second.
                var raw = "$item_food_health: <color=orange>30</color>\n$item_food_regen: <color=orange>3 hp/tick</color>\n$item_food_duration: x";
                Assert.Equal("$item_food_health: <color=orange>30</color>\n$item_food_regen: <color=orange>0.3 hp/s</color>\n$item_food_duration: x",
                    StatsMath.FoodTooltipPerSecond(raw, 3f, false, "/s"));
                Assert.Equal("<color=orange>0,15 hp/с</color>", StatsMath.FoodTooltipPerSecond("<color=orange>1.5 hp/tick</color>", 1.5f, true, "/с"));
                // Already rewritten / other item: untouched (idempotent across nested GetTooltip calls).
                Assert.Equal("<color=orange>0.3 hp/s</color>", StatsMath.FoodTooltipPerSecond("<color=orange>0.3 hp/s</color>", 3f, false, "/s"));
                Assert.Equal("no food", StatsMath.FoodTooltipPerSecond("no food", 0f, false, "/s"));
                Assert.Equal(0.25f, StatsMath.FoodRegenPerSecond(2.5f), 4);
                // Item + its m_appendToolTip food with the same regen: one call per line, both rewritten.
                var two = "<color=orange>2 hp/tick</color>|<color=orange>2 hp/tick</color>";
                Assert.Equal("<color=orange>0.2 hp/s</color>|<color=orange>0.2 hp/s</color>",
                    StatsMath.FoodTooltipPerSecond(StatsMath.FoodTooltipPerSecond(two, 2f, false, "/s"), 2f, false, "/s"));
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = culture; }
        }

        [Fact]
        public void HealthRegenIsTickHealOverTenSeconds()
        {
            // Three dishes regen 2 + 2 + 3 = 7 per 10 s tick (Player.UpdateFood), no multiplier -> 0.7 / s.
            Assert.Equal(0.7f, StatsMath.HealthRegen(7f, 1f), 3);
            // Status-effect multiplier (SEMan.ModifyHealthRegen) scales the tick.
            Assert.Equal(1.05f, StatsMath.HealthRegen(7f, 1.5f), 3);
            Assert.Equal(0f, StatsMath.HealthRegen(0f, 2f));
            Assert.Equal(6f, StatsMath.HealthTickIn(4f), 3);
            Assert.Equal(10f, StatsMath.HealthTickIn(-1f), 3);
            Assert.Equal(0f, StatsMath.HealthTickIn(12f), 3);
        }

        [Fact]
        public void StaminaRegenDoublesWhenEmpty()
        {
            // Defaults m_staminaRegen 5, time multiplier 1, world rate 1: full = 5 / s, empty = 10 / s.
            Assert.Equal(5f, StatsMath.StaminaRegen(5f, 1f, 1f, 1f, 1f, 0f, 1f), 3);
            Assert.Equal(10f, StatsMath.StaminaRegen(5f, 1f, 0f, 1f, 1f, 0f, 1f), 3);
            // Blocking 0.8, multiplier 1.5, flat +2 (same factor, not multiplied), world rate 2.
            Assert.Equal((5f * 0.8f * 1.5f + 2f * 0.8f) * 2f, StatsMath.StaminaRegen(5f, 1f, 1f, 0.8f, 1.5f, 2f, 2f), 3);
            Assert.Equal(0f, StatsMath.StaminaRegen(5f, 1f, 0.5f, 0f, 1f, 2f, 1f));
        }

        [Fact]
        public void EitrRegenHasNoWorldRate()
        {
            Assert.Equal(5f, StatsMath.EitrRegen(5f, 1f, 1f, 1f, 0f), 3);
            Assert.Equal(7.5f, StatsMath.EitrRegen(5f, 0.5f, 1f, 1f, 0f), 3);
            Assert.Equal(5f * 1.2f + 1f, StatsMath.EitrRegen(5f, 1f, 1f, 1.2f, 1f), 3);
        }

        [Fact]
        public void FormattingUsesDecimalCommaAndTypographicMinus()
        {
            var sb = new StringBuilder();
            Assert.Equal("+2,4", StatsMath.AppendDecimal(sb, 2.44f, true, true).ToString());
            sb.Clear();
            Assert.Equal("15.0", StatsMath.AppendDecimal(sb, 15f, false, false).ToString());
            sb.Clear();
            Assert.Equal("−10", StatsMath.AppendInt(sb, -10.2f, true).ToString());
            sb.Clear();
            Assert.Equal("+12", StatsMath.AppendInt(sb, 12f, true).ToString());
            sb.Clear();
            Assert.Equal("0", StatsMath.AppendInt(sb, 0.2f, true).ToString());
            sb.Clear();
            Assert.Equal("1,5", StatsMath.AppendCompact(sb, 1.5f, true).ToString());
            sb.Clear();
            Assert.Equal("3", StatsMath.AppendCompact(sb, 3f, true).ToString());
            sb.Clear();
            Assert.Equal("16:15", StatsMath.AppendClock(sb, 975f).ToString());
            sb.Clear();
            Assert.Equal("0:05", StatsMath.AppendClock(sb, 4.2f).ToString());
            sb.Clear();
            Assert.Equal("1:02:03", StatsMath.AppendClock(sb, 3723f).ToString());
            sb.Clear();
            Assert.Equal("1209", StatsMath.Digits(sb, 1209).ToString());
        }

        [Fact]
        public void SemanticFlipsForCosts()
        {
            Assert.Equal(1, StatsMath.Semantic(12f, true));
            Assert.Equal(-1, StatsMath.Semantic(-5f, true));
            Assert.Equal(1, StatsMath.Semantic(-10f, false));
            Assert.Equal(0, StatsMath.Semantic(0.3f, true));
            Assert.Equal(1, StatsMath.ReductionSemantic(0.69f, 0.39f));
            Assert.Equal(-1, StatsMath.ReductionSemantic(0.2f, 0.39f));
            Assert.Equal(0, StatsMath.ReductionSemantic(0.39f, 0.39f));
            var sb = new StringBuilder();
            StatsMath.Open(sb, 1, "7FD36B", "F0963A").Append("+8%");
            Assert.Equal("<color=#7FD36B>+8%</color>", StatsMath.Close(sb, 1).ToString());
        }

        [Fact]
        public void DesignFrameMapsCentredAndUniform()
        {
            Assert.Equal(1f, StatsMath.FitScale(1920f, 1080f), 4);
            Assert.Equal(4f / 3f, StatsMath.FitScale(2560f, 1440f), 4);
            // Ultrawide 2560x1080: height-bound, frame centred horizontally.
            var s = StatsMath.FitScale(2560f, 1080f);
            Assert.Equal(1f, s, 4);
            var b = StatsMath.Map(2560f, 1080f, s, 0f, 0f, 1920f, 1080f);
            Assert.Equal(320f, b.X, 3);
            Assert.Equal(0f, b.Y, 3);
            var card = StatsMath.Card(3, 1, 68f, 134f, 439f, 420f, 448.3f, 434.5f);
            Assert.True(card.Right <= 1852f + 1f && card.Bottom <= 990f, card.ToString());
        }

        [Fact]
        public void StackAndScroll()
        {
            var heights = new[] { 32f, 28f, 50f };
            var tops = new float[3];
            Assert.Equal(110f, StatsMath.Stack(heights, 3, 10f, tops), 3);
            Assert.Equal(new[] { 10f, 42f, 70f }, tops);
            Assert.Equal(0f, StatsMath.ClampScroll(50f, 100f, 120f));
            Assert.Equal(80f, StatsMath.ClampScroll(500f, 400f, 320f));
            Assert.Equal(0f, StatsMath.ClampScroll(-5f, 400f, 320f));
        }

        // Combat card (0.23.0): 10 rows of 31.5 in a 250 view; at rest the view starts on a row top, never mid-row.
        [Fact]
        public void ScrollRestsOnRowTops()
        {
            var heights = new float[10];
            for (var i = 0; i < 10; i++) heights[i] = 31.5f;
            var tops = new float[10];
            var content = StatsMath.Stack(heights, 10, 0f, tops); // 315
            Assert.Equal(0f, StatsMath.SnapScroll(0f, tops, 10, content, 250f), 3);
            Assert.Equal(31.5f, StatsMath.SnapScroll(40f, tops, 10, content, 250f), 3);      // nearest top
            Assert.Equal(94.5f, StatsMath.SnapScroll(65f, tops, 10, content, 250f), 3);      // end: first top >= 65 shows row 10 whole
            Assert.Equal(94.5f, StatsMath.SnapScroll(80f, tops, 10, content, 250f), 3);
            Assert.Equal(0f, StatsMath.SnapScroll(80f, tops, 10, 200f, 250f), 3);            // fits: no scroll
        }

        [Fact]
        public void ThemeShipsTheStatsFamily()
        {
            var t = Default();
            Assert.Empty(ThemeSchema.Validate(t));
            Assert.Contains(t.families, f => f.family == "CharacterStats");
            foreach (var token in t.families.Single(f => f.family == "CharacterStats").requires.Where(r => r.StartsWith("sprite.", System.StringComparison.Ordinal)))
                Assert.Contains(t.sprites, s => s.token == token);
            foreach (var role in new[] { "stats.chrome.tl", "stats.chrome.br", "stats.divider", "stats.hit", "stats.icon", "stats.title", "stats.header", "stats.label", "stats.value", "stats.sub" })
                Assert.Contains(t.roles, r => r.role == role);
            // Every card / row icon the reader names has a sprite token.
            foreach (var icon in new[] { "res", "resist", "move", "defense", "food", "rest", "combat", "mt" })
                Assert.Contains(t.sprites, s => s.token == "sprite.stats.head." + icon);
            foreach (var icon in new[] { "heart", "pulse", "bolt", "bolt2", "drop", "pulse2", "blunt", "slash", "pierce", "fire", "frost", "lightning", "poison", "spirit", "chop", "pickaxe",
                         "walk", "run", "swim", "weight", "rundrain", "dodge", "jump", "armor", "block", "parry", "fork", "flask", "hourglass", "bed", "clock",
                         "sword", "flame", "attackspeed", "backstab", "double", "lifesteal", "stagger", "boots", "ring", "es", "recharge", "breakdelay", "potion", "food", "potency" })
                Assert.Contains(t.sprites, s => s.token == "sprite.stats.icon." + icon);
            Assert.Contains(t.floats, f => f.token == "size.stats.card.px");
        }
    }
}
