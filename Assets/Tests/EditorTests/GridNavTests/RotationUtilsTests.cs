using FluentAssertions;
using GridNav;
using NUnit.Framework;
using Unity.Mathematics;

namespace Tests.EditorTests.GridNavTests
{
    public class RotationUtilsTests
    {
        private static readonly GridRotation[] AllRotations =
        {
            GridRotation.None,
            GridRotation.Clockwise90,
            GridRotation.Clockwise180,
            GridRotation.CounterClockwise90,
        };

        [Test]
        public void Clockwise90_TurnsNorthIntoEast()
        {
            RotationUtils.Rotate(new int2(0, 1), GridRotation.Clockwise90).Should().Be(new int2(1, 0));
            RotationUtils.Rotate(new int2(1, 0), GridRotation.Clockwise90).Should().Be(new int2(0, -1));
        }

        [Test]
        public void CounterClockwise90_IsTheOtherWayRound()
        {
            RotationUtils.Rotate(new int2(0, 1), GridRotation.CounterClockwise90).Should().Be(new int2(-1, 0));
        }

        [Test]
        public void Clockwise180_IsTheNegatedOffset()
        {
            RotationUtils.Rotate(new int2(2, -3), GridRotation.Clockwise180).Should().Be(new int2(-2, 3));
        }

        [Test]
        public void FourQuarterTurns_ReturnTheOffsetItStartedAs()
        {
            int2 offset = new(3, -7);
            int2 turned = offset;

            for (int i = 0; i < 4; i++)
            {
                turned = RotationUtils.Rotate(turned, GridRotation.Clockwise90);
            }

            turned.Should().Be(offset, "integer rotation has to be exact, or footprints would drift");
        }

        [Test]
        public void RotatingADirection_AgreesWithRotatingItsOffset()
        {
            // If these two disagreed, a rotated building's entrances would face away from the cells they open onto.
            foreach (GridRotation rotation in AllRotations)
            {
                for (int i = 0; i < DirectionUtils.DIRECTION_COUNT; i++)
                {
                    var direction = (Direction)i;

                    int2 rotatedOffset = RotationUtils.Rotate(DirectionUtils.Offset(direction), rotation);
                    Direction rotatedDirection = RotationUtils.Rotate(direction, rotation);

                    DirectionUtils.Offset(rotatedDirection).Should().Be(rotatedOffset,
                        $"{direction} rotated by {rotation}");
                }
            }
        }

        [Test]
        public void Inverse_UndoesTheRotation()
        {
            int2 offset = new(1, 4);

            foreach (GridRotation rotation in AllRotations)
            {
                int2 there = RotationUtils.Rotate(offset, rotation);
                RotationUtils.Rotate(there, RotationUtils.Inverse(rotation)).Should().Be(offset);
            }
        }
    }
}
