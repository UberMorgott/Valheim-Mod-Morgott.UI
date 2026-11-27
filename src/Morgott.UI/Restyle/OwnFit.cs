using TMPro;
using UnityEngine;

namespace Morgott.UI.Restyle
{
    // Marker: this text's size and box are set by its owner (a layout that fits it itself, e.g. the inventory side column
    // values or Epic Loot dialog choice labels). Generic text fitters (TextInset, PlateLabelFit, TextSizeGroup) keep off
    // it - two fitters on one text alternate their writes and the text blinks (user 2026-10-26).
    [DisallowMultipleComponent]
    public sealed class OwnFit : MonoBehaviour
    {
        public static void Claim(TMP_Text text)
        {
            if (!text) return;
            if (text.TryGetComponent<TextInset>(out var inset)) inset.Revert();
            if (text.TryGetComponent<PlateLabelFit>(out var plate) && !plate.Reverted) plate.Revert();
            if (text.TryGetComponent<TextSizeGroup>(out var group)) group.Revert();
            if (text.TryGetComponent<OwnFit>(out _)) return;
            text.gameObject.AddComponent<OwnFit>().hideFlags = HideFlags.HideInInspector;
        }

        public static bool Owns(TMP_Text text) => text && text.TryGetComponent<OwnFit>(out _);
    }
}
