using Morgott.UI.Restyle;
using Xunit;

namespace Morgott.UI.Tests
{
    // SkinMark.Own plan (MarkMath): one mark per object. 0.25.12 RC run 20261027-163702: ActionBar.Own marked a button
    // background with "action.button", ButtonRole("action.button.header") marked it again -> AddComponent<SkinMark>
    // returned null ([DisallowMultipleComponent]) -> NullReferenceException in DollCell.Mark.
    public class MarkMathTests
    {
        [Fact]
        public void FirstMarkAdds() => Assert.Equal(MarkStep.Add, MarkMath.Plan(hasMark: false, ownMark: false));

        [Fact]
        public void SecondMarkOfTheSameObjectReusesNeverAdds()
        {
            // ActionBar.Own -> DollCell.Mark(bg, DefaultRole): mark added.
            var hasMark = false;
            Assert.Equal(MarkStep.Add, MarkMath.Plan(hasMark, ownMark: false));
            hasMark = true;
            // ActionBar.ButtonRole(HeaderRole) -> DollCell.Mark(bg, HeaderRole): the object's own mark comes back.
            Assert.Equal(MarkStep.Reuse, MarkMath.Plan(hasMark, ownMark: true));
            // Back to the default role (theme without plinth art), and every later relayout: still the one mark.
            Assert.Equal(MarkStep.Reuse, MarkMath.Plan(hasMark, ownMark: true));
        }

        [Fact]
        public void MarkCopiedByInstantiateIsAdoptedNotDuplicated() =>
            Assert.Equal(MarkStep.Rebind, MarkMath.Plan(hasMark: true, ownMark: false));

        // Run 20261029-033559-2: Epic Loot's VanillaFont (NorseBoldOutline) kept our rune face on the enchanting
        // TabContent/*Content/Title and wrote its "- Outline" material -> off the rune preset; that is an overwrite.
        [Theory]
        [InlineData(true, true, true, false)]   // ours, untouched
        [InlineData(false, false, true, true)]  // another font
        [InlineData(false, true, true, true)]   // another font on a loaded face
        [InlineData(true, true, false, true)]   // rune face kept, material replaced (Epic Loot Outline)
        [InlineData(true, false, false, false)] // body face: the material is the text's own, not ours
        public void FaceOverwrittenSeesTheMaterialOnLoadedFaces(bool fontIsOurs, bool loadedFace, bool materialIsOurs, bool want) =>
            Assert.Equal(want, MarkMath.FaceOverwritten(fontIsOurs, loadedFace, materialIsOurs));

        // Player screenshot 2026-11-08: SplitDialog/CloseButton (sprite-less, white alpha 0, full screen) took button_normal
        // + color.frame and painted an opaque full-screen plate under the split dialog.
        [Theory]
        [InlineData(false, 0f, true)]    // vanilla click-outside closer: keep it invisible
        [InlineData(false, 1f, false)]   // sprite-less but drawn: a real (flat) button
        [InlineData(true, 0f, false)]    // has art: button (alpha comes from its own transition)
        [InlineData(true, 1f, false)]
        public void InvisibleHitAreaKeepsVanillaClosersTransparent(bool hasSprite, float alpha, bool want) =>
            Assert.Equal(want, MarkMath.InvisibleHitArea(hasSprite, alpha));
    }
}
