using NUnit.Framework;
using UnityEngine;

namespace MapTool.Core.Tests
{
    public sealed class GridTests
    {
        [Test]
        public void Rotation_ClockwiseFourTimes_ReturnsToStart()
        {
            var r = GridRotation.R270;
            Assert.AreEqual(GridRotation.R0, r.Clockwise());
            Assert.AreEqual(GridRotation.R270, GridRotation.R0.CounterClockwise());
            Assert.AreEqual(GridRotation.R0, GridRotation.R0.Clockwise().Clockwise().Clockwise().Clockwise());
            Assert.AreEqual(270f, GridRotation.R270.ToDegrees());
        }

        [Test]
        public void RotateSize_QuarterTurns_SwapXZ_KeepY()
        {
            var size = new Vector3Int(2, 3, 5);
            Assert.AreEqual(size, GridRotation.R0.RotateSize(size));
            Assert.AreEqual(new Vector3Int(5, 3, 2), GridRotation.R90.RotateSize(size));
            Assert.AreEqual(size, GridRotation.R180.RotateSize(size));
            Assert.AreEqual(new Vector3Int(5, 3, 2), GridRotation.R270.RotateSize(size));
        }

        [Test]
        public void WorldToCell_FloorsNegativeAndUsesPerAxisCellSize()
        {
            var grid = new GridSpace(new Vector3(10f, 0f, 0f), new Vector3(1f, 0.5f, 2f));

            Assert.AreEqual(new Vector3Int(0, 0, 0), grid.WorldToCell(new Vector3(10.5f, 0.2f, 1.9f)));
            Assert.AreEqual(new Vector3Int(-1, 2, -1), grid.WorldToCell(new Vector3(9.5f, 1.0f, -0.1f)));
        }

        [Test]
        public void WorldToCell_ValueOnBoundary_IsNotPushedDownByFloatError()
        {
            var grid = new GridSpace(Vector3.zero, new Vector3(0.1f, 0.1f, 0.1f));
            // 0.3f / 0.1f 는 부동소수 오차로 2.9999...가 된다
            Assert.AreEqual(new Vector3Int(3, 0, 0), grid.WorldToCell(new Vector3(0.3f, 0f, 0f)));
        }

        [Test]
        public void CellToWorld_WorldToCell_RoundTrip()
        {
            var grid = new GridSpace(new Vector3(-3f, 1f, 7f), new Vector3(0.5f, 2f, 0.5f));
            var cell = new Vector3Int(-4, 3, 11);
            Assert.AreEqual(cell, grid.WorldToCell(grid.CellToWorld(cell)));
        }

        [Test]
        public void FootprintBaseCenter_IsCenterOfBottomFace()
        {
            var grid = new GridSpace(Vector3.zero, new Vector3(2f, 1f, 2f));
            var bounds = new BoundsInt(new Vector3Int(1, 2, 0), new Vector3Int(2, 3, 1));

            var center = grid.FootprintBaseCenter(bounds);

            Assert.AreEqual(new Vector3(4f, 2f, 1f), center);
        }

        [Test]
        public void GridSpace_NonPositiveCellSize_Throws()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new GridSpace(Vector3.zero, new Vector3(1f, 0f, 1f)));
        }
    }
}
