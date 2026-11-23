using Morgott.UI.WorldHud;
using Xunit;

namespace Morgott.UI.Tests
{
    public class DamageLanesTests
    {
        // Lane width 0.8 m (at 10 m), number height 0.35 m -> a spot frees 0.35 s after a number spawned there (rise 1 m/s).
        private const float R = 0.8f;
        private const float H = 0.35f;
        private const float L = 1.5f; // vanilla m_textDuration

        [Fact]
        public void Overlapping_numbers_across_frames_take_the_next_free_lane()
        {
            var b = new DamageLanes();
            Assert.Equal(0, b.Assign(0, 2, 0, 1, 0, 10f, R, H, L));
            Assert.Equal(1, b.Assign(0, 2, 0, 1, 0, 10.1f, R, H, L)); // lightning 25, then a fire tick next frames
            Assert.Equal(2, b.Assign(0, 2, 0, 1, 0, 10.2f, R, H, L));
        }

        [Fact]
        public void A_lane_frees_once_its_number_has_risen_clear()
        {
            var b = new DamageLanes();
            Assert.Equal(0, b.Assign(0, 2, 0, 1, 0, 10f, R, H, L));
            Assert.Equal(1, b.Assign(0, 2, 0, 1, 0, 10.2f, R, H, L));
            Assert.Equal(0, b.Assign(0, 2, 0, 1, 0, 10.4f, R, H, L)); // centre cleared at 10.35, lane 1 still busy
        }

        [Fact]
        public void Numbers_on_far_apart_targets_share_the_centre_lane()
        {
            var b = new DamageLanes();
            Assert.Equal(0, b.Assign(0, 2, 0, 1, 0, 10f, R, H, L));
            Assert.Equal(0, b.Assign(5, 2, 0, 1, 0, 10f, R, H, L));
        }

        [Fact]
        public void A_target_moving_by_almost_a_lane_never_lands_on_a_rising_number()
        {
            var b = new DamageLanes();
            Assert.Equal(0, b.Assign(0, 2, 0, 1, 0, 10f, R, H, L));
            // centre (-0.79) and right (+0.01) both still touch the number at 0: left lane (-1.59)
            Assert.Equal(2, b.Assign(-0.79f, 2, 0, 1, 0, 10.1f, R, H, L));
        }

        [Fact]
        public void All_lanes_busy_wraps_to_the_lane_that_frees_first()
        {
            var b = new DamageLanes();
            for (var i = 0; i < DamageLanes.Lanes; i++)
                Assert.Equal(i, b.Assign(0, 2, 0, 1, 0, 10f + i * 0.01f, R, H, L));
            Assert.Equal(0, b.Assign(0, 2, 0, 1, 0, 10.1f, R, H, L));
            Assert.Equal(1, b.Assign(0, 2, 0, 1, 0, 10.1f, R, H, L)); // lane 0 now busy until 10.45
        }

        [Fact]
        public void Lanes_hold_at_large_world_coordinates()
        {
            var b = new DamageLanes();
            for (var i = 0; i < DamageLanes.Lanes; i++)
                Assert.Equal(i, b.Assign(1000f, 30f, -2000f, 1, 0, 10f, R, H, L));
        }

        [Fact]
        public void Faded_numbers_free_their_lane()
        {
            var b = new DamageLanes();
            Assert.Equal(0, b.Assign(0, 2, 0, 1, 0, 10f, R, H, L));
            Assert.Equal(0, b.Assign(0, 5, 0, 1, 0, 13f, R, H, L)); // would have risen to y=5, but gone after 1.5 s
        }

        [Fact]
        public void Lane_order_is_centre_right_left()
        {
            Assert.Equal(0, DamagePopupMath.Side(0));
            Assert.Equal(1, DamagePopupMath.Side(1));
            Assert.Equal(-1, DamagePopupMath.Side(2));
        }
    }
}
