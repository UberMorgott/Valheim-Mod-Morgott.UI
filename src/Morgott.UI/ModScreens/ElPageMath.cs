namespace Morgott.UI.ModScreens
{
    // Pure body layout of the enchanting page (unit-tested): one scale for column and panels that fits the prefab body
    // height into the content box, shrunk when column + gap + the prefab panel width would not fit its width; the panel
    // then spans the rest of the width.
    public static class ElPageMath
    {
        public struct Layout
        {
            public float Scale, TabsW, ContentX;
        }

        // Sizes in canvas units at scale 1: tabsW column width, bodyH prefab body height, contentW prefab panel width, gap
        // column-to-panel gap.
        public static Layout Body(float boxW, float boxH, float tabsW, float bodyH, float contentW, float gap)
        {
            if (boxW <= 0f || boxH <= 0f || bodyH <= 0f) return new Layout { Scale = 1f };
            var s = boxH / bodyH;
            var need = tabsW + gap + contentW;
            if (need > 0f && need * s > boxW) s = boxW / need;
            return new Layout { Scale = s, TabsW = tabsW * s, ContentX = (tabsW + gap) * s };
        }
    }
}
