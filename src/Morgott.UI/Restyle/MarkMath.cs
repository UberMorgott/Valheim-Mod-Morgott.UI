namespace Morgott.UI.Restyle
{
    // How SkinMark.Own treats an object (pure, unit-tested). SkinMark is [DisallowMultipleComponent]: a second
    // AddComponent<SkinMark> on an object that already carries one returns null (0.25.12 RC run 20261027-163702: the
    // inventory action bar re-marked its button backgrounds for the header plinth role -> NullReferenceException in
    // DollCell.Mark every LateUpdate). One mark per object, owned by SkinMark.Own: add it once, then reuse it.
    public enum MarkStep
    {
        Add,    // no mark yet
        Reuse,  // the object's own mark
        Rebind  // a mark Instantiate copied from a template (journal = the template's true originals): adopt it
    }

    public static class MarkMath
    {
        public static MarkStep Plan(bool hasMark, bool ownMark) => !hasMark ? MarkStep.Add : ownMark ? MarkStep.Reuse : MarkStep.Rebind;

        // A styled text's face was written by another mod after our pass: a different font, or - on a loaded face, whose
        // preset is the font asset's own material - the same font with another material (Epic Loot NorseBoldOutline).
        // Other faces keep the material they had (only the font is ours), so a material change there is not an overwrite.
        public static bool FaceOverwritten(bool fontIsOurs, bool loadedFace, bool materialIsOurs) =>
            !fontIsOurs || loadedFace && !materialIsOurs;

        // A button graphic vanilla never draws (no sprite, alpha 0: a dialog's full-screen click-outside closer) keeps
        // its vanilla look: button art on it paints the whole screen.
        public static bool InvisibleHitArea(bool hasSprite, float vanillaAlpha) => !hasSprite && vanillaAlpha <= 0.01f;
    }
}
