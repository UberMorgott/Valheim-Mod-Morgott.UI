using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Morgott.UI.MapScreen;
using UnityEngine;
using Xunit;

namespace Morgott.UI.Tests
{
    public class CustomPinTests
    {
        [Fact]
        public void IconIdsAndSwatchesMatchSpec()
        {
            Assert.Equal(25, CustomPinMath.IconIds.Length);
            Assert.Equal(new[]
            {
                "ore", "berry", "shroom", "cave", "skull",
                "boss", "house", "bed", "ship", "anchor",
                "coins", "chest", "portal", "farm", "hive",
                "hunt", "fish", "fight", "fort", "camp",
                "tree", "star", "alert", "quest", "dot",
            }, CustomPinMath.IconIds);
            Assert.Equal(10, CustomPinMath.SwatchHex.Length);
            Assert.Equal(new[]
            {
                "ECE5D8", "E6B84C", "D48244", "D4554A", "5FB86B",
                "5EB8D4", "A575D4", "A8B3BE", "B88B58", "88A858",
            }, CustomPinMath.SwatchHex);
            for (var i = 0; i < CustomPinMath.IconIds.Length; i++)
                Assert.Equal(i, CustomPinMath.IconIndex(CustomPinMath.IconIds[i]));
            Assert.Equal(-1, CustomPinMath.IconIndex("unknown_future_icon"));
        }

        [Fact]
        public void EncodeDecodeRoundtripNamedAndEmpty()
        {
            var encoded = CustomPinMath.Encode("Copper Vein", "ore", "E6B84C");
            Assert.Equal("Copper Vein [mp1:ore:E6B84C]", encoded);
            Assert.True(CustomPinMath.TryDecode(encoded, out var d));
            Assert.Equal("Copper Vein", d.VisibleName);
            Assert.Equal("ore", d.IconId);
            Assert.Equal(0, d.IconIndex);
            Assert.Equal("E6B84C", d.Hex);
            Assert.True(d.HasColor);
            Assert.Equal((230, 184, 76), (d.R, d.G, d.B));
            Assert.Equal("Copper Vein", CustomPinMath.StripTag(encoded));

            var empty = CustomPinMath.Encode("   ", "cave", "#d48244");
            Assert.Equal("[mp1:cave:D48244]", empty);
            Assert.True(CustomPinMath.TryDecode(empty, out var d2));
            Assert.Equal("", d2.VisibleName);
            Assert.Equal("cave", d2.IconId);
            Assert.Equal(3, d2.IconIndex);
            Assert.Equal("D48244", d2.Hex);
            Assert.Equal("", CustomPinMath.StripTag(empty));

            // Re-encoding already encoded text never double-encodes.
            var reencoded = CustomPinMath.Encode(encoded, "chest", "5FB86B");
            Assert.Equal("Copper Vein [mp1:chest:5FB86B]", reencoded);
        }

        [Fact]
        public void ToleratesUserBracketsUnknownFutureIdsColorsAndMalformedTags()
        {
            // User brackets before valid suffix are preserved.
            var withBrackets = CustomPinMath.Encode("Copper [NW]", "ore", "E6B84C");
            Assert.Equal("Copper [NW] [mp1:ore:E6B84C]", withBrackets);
            Assert.True(CustomPinMath.TryDecode(withBrackets, out var d));
            Assert.Equal("Copper [NW]", d.VisibleName);
            Assert.Equal("ore", d.IconId);

            // Unknown future icon ID still strips cleanly, IconIndex = -1.
            Assert.True(CustomPinMath.TryDecode("Mill [mp1:windmill:E6B84C]", out var futureIcon));
            Assert.Equal("Mill", futureIcon.VisibleName);
            Assert.Equal("windmill", futureIcon.IconId);
            Assert.Equal(-1, futureIcon.IconIndex);
            Assert.True(futureIcon.HasColor);
            Assert.Equal("Mill", CustomPinMath.StripTag("Mill [mp1:windmill:E6B84C]"));

            // Unknown future color token still strips cleanly, HasColor = false.
            Assert.True(CustomPinMath.TryDecode("Mill [mp1:ore:gold]", out var futureColor));
            Assert.Equal("Mill", futureColor.VisibleName);
            Assert.Equal("ore", futureColor.IconId);
            Assert.Equal(0, futureColor.IconIndex);
            Assert.False(futureColor.HasColor);

            // Plain user brackets / malformed / non-trailing tags are left untouched.
            foreach (var raw in new[]
            {
                "",
                "Home",
                "Mine [1]",
                "Base [mp2:house:ECE5D8]",
                "Broken [mp1:]",
                "Broken [mp1:ore]",
                "Broken [mp1:ore:E6B84C:extra]",
                "NotTrailing [mp1:ore:E6B84C] suffix",
                "NoSpace[mp1:ore:E6B84C]",
                "BadSpace [mp1:my ore:E6B84C]",
            })
            {
                Assert.False(CustomPinMath.TryDecode(raw, out _), raw);
                Assert.Equal(raw, CustomPinMath.StripTag(raw));
            }
        }

        [Fact]
        public void ApplyColorPreservesSharedPinAlpha()
        {
            var shared = new Color(0.7f, 0.7f, 0.7f, 0.64f);
            var tinted = CustomPinMath.ApplyColorPreservingAlpha(shared, 0xD4, 0x55, 0x4A);
            Assert.Equal(0xD4 / 255f, tinted.r, 4);
            Assert.Equal(0x55 / 255f, tinted.g, 4);
            Assert.Equal(0x4A / 255f, tinted.b, 4);
            Assert.Equal(0.64f, tinted.a, 4);
        }

        [Fact]
        public void EditablePinPredicateAllowsOnlyOwnSavedUserPins()
        {
            foreach (var t in new[] { Minimap.PinType.Icon0, Minimap.PinType.Icon1, Minimap.PinType.Icon2, Minimap.PinType.Icon3, Minimap.PinType.Icon4 })
            {
                Assert.True(CustomPinMath.IsEditablePin(true, 0L, t));
                Assert.False(CustomPinMath.IsEditablePin(false, 0L, t));
                Assert.False(CustomPinMath.IsEditablePin(true, 42L, t)); // foreign shared pin
            }
            foreach (var t in new[]
            {
                Minimap.PinType.Death, Minimap.PinType.Boss, Minimap.PinType.Bed, Minimap.PinType.Shout,
                Minimap.PinType.Ping, Minimap.PinType.RandomEvent, Minimap.PinType.EventArea,
                Minimap.PinType.Hildir1, Minimap.PinType.Hildir2, Minimap.PinType.Hildir3,
                Minimap.PinType.Memorial, Minimap.PinType.Player, Minimap.PinType.None,
                MapFilterMath.Portal, MapFilterMath.Pet,
            })
            {
                Assert.False(CustomPinMath.IsEditablePin(true, 0L, t), t.ToString());
            }
        }

        [Fact]
        public void DeferredSingleClickTogglesAfterDelayAndCancelsOnDoubleClick()
        {
            var defer = new DeferredPinToggle();
            object pinA = new object(), pinB = new object();

            // Single click defers toggle for 0.3s.
            Assert.True(defer.OnEditablePinClick(pinA, 10f, out var flushed));
            Assert.Null(flushed);
            Assert.Null(defer.PollDue(10.29f));
            Assert.Same(pinA, defer.PollDue(10.30f));
            Assert.False(defer.HasPending);

            // Second click on same pin within 0.3s cancels toggle so double-click opens editor without toggling X.
            Assert.True(defer.OnEditablePinClick(pinA, 20f, out _));
            Assert.True(defer.OnEditablePinClick(pinA, 20.15f, out flushed));
            Assert.Null(flushed);
            Assert.False(defer.HasPending);
            Assert.Null(defer.PollDue(25f));

            // Clicking a different pin flushes the previous pending toggle immediately.
            Assert.True(defer.OnEditablePinClick(pinA, 30f, out _));
            Assert.True(defer.OnEditablePinClick(pinB, 30.1f, out flushed));
            Assert.Same(pinA, flushed);
            Assert.Same(pinB, defer.PollDue(30.45f));
        }

        [Fact]
        public void ForeignPinDoubleClickNeverClaimsOrOpensEditor()
        {
            var defer = new DeferredPinToggle();
            object foreignPin = new object();

            // Click #1 on foreign pin defers claim (does not claim immediately) and marks pin recently foreign for 0.3s.
            Assert.True(defer.OnPinClick(foreignPin, DeferredPinAction.ClaimForeign, 10f, out var prev));
            Assert.Null(prev.Pin);
            Assert.True(defer.WasRecentlyForeign(foreignPin, 10.15f));

            // Click #2 within 0.3s cancels the deferred claim, and CancelPendingOnly keeps WasRecentlyForeign true for OnMapDblClick!
            Assert.True(defer.OnPinClick(foreignPin, DeferredPinAction.ClaimForeign, 10.15f, out prev));
            Assert.Null(prev.Pin);
            defer.CancelPendingOnly();
            Assert.True(defer.WasRecentlyForeign(foreignPin, 10.15f));
            Assert.Null(defer.PollDueItem(20f).Pin); // never claims after double-click

            // Single click on foreign pin (no 2nd click within 0.3s) fires ClaimForeign after 0.3s.
            Assert.True(defer.OnPinClick(foreignPin, DeferredPinAction.ClaimForeign, 30f, out _));
            var due = defer.PollDueItem(30.31f);
            Assert.Same(foreignPin, due.Pin);
            Assert.Equal(DeferredPinAction.ClaimForeign, due.Action);
            Assert.False(defer.WasRecentlyForeign(foreignPin, 30.31f));
        }

        [Fact]
        public void PlanSavePreventsUnchangedDeleteAndConcurrentDeleteResurrection()
        {
            var encoded = CustomPinMath.Encode("Mine", "ore", "E6B84C");

            // New pin -> Create.
            Assert.Equal(CustomPinMath.PinSaveAction.Create,
                CustomPinMath.PlanSave(false, false, false, Minimap.PinType.Icon3, null, Minimap.PinType.Icon3, encoded));

            // Editing a concurrently deleted pin -> Cancel (must not resurrect!).
            Assert.Equal(CustomPinMath.PinSaveAction.Cancel,
                CustomPinMath.PlanSave(true, stillPresent: false, stillEditable: false, Minimap.PinType.Icon3, encoded, Minimap.PinType.Icon3, encoded));

            // Editing a pin that became non-editable -> Cancel.
            Assert.Equal(CustomPinMath.PinSaveAction.Cancel,
                CustomPinMath.PlanSave(true, stillPresent: true, stillEditable: false, Minimap.PinType.Icon3, encoded, Minimap.PinType.Icon3, encoded));

            // Unchanged (type, rawName) -> NoOp (prevents V+ server skip-Add + RemovePin deleting the pin!).
            Assert.Equal(CustomPinMath.PinSaveAction.NoOp,
                CustomPinMath.PlanSave(true, stillPresent: true, stillEditable: true, Minimap.PinType.Icon3, encoded, Minimap.PinType.Icon3, encoded));

            // Changed name/icon/color -> Replace.
            var modified = CustomPinMath.Encode("Copper", "ore", "D48244");
            Assert.Equal(CustomPinMath.PinSaveAction.Replace,
                CustomPinMath.PlanSave(true, stillPresent: true, stillEditable: true, Minimap.PinType.Icon3, encoded, Minimap.PinType.Icon3, modified));
        }

        [Fact]
        public void ClosedMapNeverLeavesInTextInputStuckTrue()
        {
            Assert.False(CustomPinMath.IsTypingInMap(mapOpen: false, searchFocused: true, portalEditorOpen: true, customPinModalOpen: true));
            Assert.True(CustomPinMath.IsTypingInMap(mapOpen: true, searchFocused: true, portalEditorOpen: false, customPinModalOpen: false));
            Assert.True(CustomPinMath.IsTypingInMap(mapOpen: true, searchFocused: false, portalEditorOpen: true, customPinModalOpen: false));
            Assert.True(CustomPinMath.IsTypingInMap(mapOpen: true, searchFocused: false, portalEditorOpen: false, customPinModalOpen: true));
            Assert.False(CustomPinMath.IsTypingInMap(mapOpen: true, searchFocused: false, portalEditorOpen: false, customPinModalOpen: false));
        }

        [Fact]
        public void SurrogateSafeTruncationAndStrictUtf8EncodingNeverThrow()
        {
            var strictUtf8 = new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true);

            // 39 ASCII chars + 2-unit emoji (41 UTF-16 code units): must drop whole surrogate pair cleanly, not leave lone high surrogate.
            var edge = new string('a', 39) + "🦅";
            var encodedEdge = CustomPinMath.Encode(edge, "hunt", "E6B84C");
            Assert.Equal(new string('a', 39) + " [mp1:hunt:E6B84C]", encodedEdge);
            Assert.NotEmpty(strictUtf8.GetBytes(encodedEdge));

            // 38 ASCII chars + emoji (40 UTF-16 code units): keeps whole pair intact.
            var fits = new string('b', 38) + "🦅";
            var encodedFits = CustomPinMath.Encode(fits, "hunt", "E6B84C");
            Assert.Equal(fits + " [mp1:hunt:E6B84C]", encodedFits);
            Assert.NotEmpty(strictUtf8.GetBytes(encodedFits));

            // Stray unpaired high/low surrogates are stripped cleanly so BinaryWriter never throws EncoderFallbackException.
            var broken = "A\uD83EB\uDD85C\uDD85\uD83ED";
            var encodedBroken = CustomPinMath.Encode(broken, "dot", "ECE5D8");
            Assert.Equal("ABCD [mp1:dot:ECE5D8]", encodedBroken);
            Assert.NotEmpty(strictUtf8.GetBytes(encodedBroken));
        }

        [Fact]
        public void HasVPlusKeyCollisionDetectsOtherSavedPinsIncludingHiddenOnes()
        {
            object pinA = new object(), pinB = new object(), unsaved = new object();
            var encoded = CustomPinMath.Encode("Copper", "ore", "E6B84C");
            var existing = new[]
            {
                new CustomPinMath.SavedPinIdentity(pinA, save: true, Minimap.PinType.Icon3, new Vector3(100.04f, 5f, -42.12f), encoded),
                new CustomPinMath.SavedPinIdentity(pinB, save: true, Minimap.PinType.Icon1, new Vector3(200f, 0f, 300f), "Base"),
                new CustomPinMath.SavedPinIdentity(unsaved, save: false, Minimap.PinType.Icon3, new Vector3(500f, 0f, 500f), encoded),
            };

            // Same (type, x:F1, z:F1, name) on another saved pin -> collision (both create and edit-another).
            Assert.True(CustomPinMath.HasVPlusKeyCollision(existing, editingPin: null, Minimap.PinType.Icon3, new Vector3(100.01f, 99f, -42.14f), encoded));
            Assert.True(CustomPinMath.HasVPlusKeyCollision(existing, editingPin: pinB, Minimap.PinType.Icon3, new Vector3(100.04f, 0f, -42.12f), encoded));

            // Editing the same pin itself is NOT a collision; different type, position, or unsaved pin is NOT a collision.
            Assert.False(CustomPinMath.HasVPlusKeyCollision(existing, editingPin: pinA, Minimap.PinType.Icon3, new Vector3(100.04f, 0f, -42.12f), encoded));
            Assert.False(CustomPinMath.HasVPlusKeyCollision(existing, editingPin: null, Minimap.PinType.Icon0, new Vector3(100.04f, 0f, -42.12f), encoded));
            Assert.False(CustomPinMath.HasVPlusKeyCollision(existing, editingPin: null, Minimap.PinType.Icon3, new Vector3(500f, 0f, 500f), encoded));
            Assert.Equal("3|100.0|-42.1|" + encoded, CustomPinMath.VPlusSyncKey(Minimap.PinType.Icon3, new Vector3(100.04f, 5f, -42.12f), encoded));
        }

        [Fact]
        public void CustomLegendSummaryVisibilityAndConfigPersistence()
        {
            var pins = new[]
            {
                "Silver [mp1:ore:A8B3BE]",
                "Copper [mp1:ore:D48244]",
                "Raspberries [mp1:berry:D4554A]",
                "[mp1:cave:ECE5D8]",
                "Plain vanilla pin",
                "Future [mp1:windmill:E6B84C]",
            };
            var hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "berry" };
            var summary = new List<CustomIconSummary>();
            CustomPinMath.CollectPresentCustomIcons(pins, hidden, summary);

            // Includes ONLY present known icons, INCLUDING hidden ones ("berry"), in canonical IconIds order.
            Assert.Equal(new[] { "ore", "berry", "cave" }, summary.Select(s => s.IconId));
            Assert.Equal(2, summary[0].Count);
            Assert.False(summary[0].Hidden);
            Assert.Equal((0xA8, 0xB3, 0xBE), (summary[0].R, summary[0].G, summary[0].B));
            Assert.Equal(1, summary[1].Count);
            Assert.True(summary[1].Hidden);
            Assert.Equal(1, summary[2].Count);
            Assert.False(summary[2].Hidden);

            // Synchronous visibility & hit-test filtering predicate.
            Assert.True(CustomPinMath.IsCustomPinVisible(pins[0], hidden));
            Assert.False(CustomPinMath.IsCustomPinVisible(pins[2], hidden));
            Assert.True(CustomPinMath.IsCustomPinVisible(pins[4], hidden));
            Assert.True(CustomPinMath.IsCustomPinVisible(pins[5], hidden));

            // CSV formatting & parsing roundtrip.
            var csv = CustomPinMath.FormatHiddenIcons(new[] { "cave", "ore", "unknown", "ore" });
            Assert.Equal("ore,cave", csv);
            var parsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CustomPinMath.ParseHiddenIcons(" CAVE, bad , ore ", parsed);
            Assert.Equal(new[] { "cave", "ore" }, parsed.OrderBy(x => x));
        }

        [Theory]
        [InlineData("English")]
        [InlineData("Russian")]
        public void AllCustomPinAndSidebarKeysAreTranslated(string language)
        {
            using var s = typeof(CustomPinTests).Assembly.GetManifestResourceStream($"Morgott.UI.Translations.{language}.json");
            var words = JsonSerializer.Deserialize<Dictionary<string, string>>(s!)!;
            var keys = CustomPinMath.IconIds.Select(id => CustomPinMath.IconNameToken(id).TrimStart('$'))
                .Concat(new[]
                {
                    "ui_map_sidebar_open", "ui_map_quests", "ui_map_quests_active", "ui_map_quests_bosses",
                    "ui_map_quests_empty", "ui_map_quests_hint", "ui_map_quest_bounty", "ui_map_quest_hildir",
                    "ui_map_quest_treasure", "ui_map_quest_boss", "ui_portal_hint_browse", "ui_portal_empty",
                    "ui_map_custom_pins", "ui_mpin_title_new", "ui_mpin_title_edit", "ui_mpin_name_hint",
                    "ui_mpin_place", "ui_mpin_delete", "ui_mpin_legend_count", "ui_mpin_legend_count_hidden",
                });
            foreach (var key in keys)
                Assert.True(words.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v), $"{language} misses {key}");
        }
    }
}
