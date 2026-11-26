using NUnit.Framework;
using UnityEngine;

namespace MapTool.Core.Tests
{
    public sealed class GroupCommandTests
    {
        // crate 1x1x1, table 2x1x1 (X로 긴 것)
        private MapData map;
        private PlacementService service;

        [SetUp]
        public void SetUp()
        {
            var catalog = new ObjectCatalog(new[]
            {
                new ObjectDefinition("crate", new Vector3Int(1, 1, 1)),
                new ObjectDefinition("table", new Vector3Int(2, 1, 1)),
            });
            map = new MapData(new Vector3Int(10, 1, 10), GridSpace.Unit);
            service = new PlacementService(map, catalog);
        }

        private PlacedObject Place(string id, int x, int z)
        {
            var cmd = new PlaceCommand(id, new Vector3Int(x, 0, z));
            var result = service.Execute(cmd);
            Assert.IsTrue(result.IsValid, result.ToString());
            return cmd.Placed;
        }

        private PlacedObject Get(int id)
        {
            Assert.IsTrue(map.TryGet(id, out var placed), $"#{id} 없음");
            return placed;
        }

        private static Vector3Int C(int x, int z) => new(x, 0, z);

        [Test]
        public void MoveMany_KeepsSpacing_AndCanEnterGroupMembersOldCells()
        {
            // a(0,0) b(1,0)을 X+1로 옮기면 a가 b의 옛 자리로 들어간다. 하나씩 옮기면 실패하는 경우.
            var a = Place("crate", 0, 0);
            var b = Place("crate", 1, 0);

            var result = service.Execute(new MoveManyCommand(new[] { a.InstanceId, b.InstanceId }, C(1, 0)));

            Assert.IsTrue(result.IsValid, result.ToString());
            Assert.AreEqual(C(1, 0), Get(a.InstanceId).Cell);
            Assert.AreEqual(C(2, 0), Get(b.InstanceId).Cell);
            Assert.IsTrue(service.Occupancy.TryGetOccupant(C(1, 0), out var at1) && at1 == a.InstanceId);
            Assert.IsFalse(service.Occupancy.IsOccupied(C(0, 0)));
            Assert.AreEqual(2, service.Occupancy.OccupiedCellCount);
        }

        [Test]
        public void MoveMany_BlockedByOutsider_MovesNothing()
        {
            var a = Place("crate", 0, 0);
            var b = Place("crate", 0, 1);
            Place("crate", 3, 1); // b의 목적지를 막는다

            var result = service.Execute(new MoveManyCommand(new[] { a.InstanceId, b.InstanceId }, C(3, 0)));

            Assert.AreEqual(PlacementCodes.Overlap, result.Code);
            Assert.AreEqual(a, Get(a.InstanceId));
            Assert.AreEqual(b, Get(b.InstanceId));
        }

        [Test]
        public void MoveMany_OutOfBounds_MovesNothing()
        {
            var a = Place("crate", 0, 0);
            var b = Place("crate", 9, 0);

            var result = service.Execute(new MoveManyCommand(new[] { a.InstanceId, b.InstanceId }, C(1, 0)));

            Assert.AreEqual(PlacementCodes.OutOfBounds, result.Code);
            Assert.AreEqual(a, Get(a.InstanceId));
            Assert.AreEqual(b, Get(b.InstanceId));
        }

        [Test]
        public void MoveMany_UndoRedo_RestoresPosesAndOccupancy()
        {
            var a = Place("table", 0, 0); // (0,0)(1,0)
            var b = Place("crate", 2, 0);

            Assert.IsTrue(service.Execute(new MoveManyCommand(new[] { a.InstanceId, b.InstanceId }, C(1, 0))).IsValid);
            Assert.IsTrue(service.Undo());

            Assert.AreEqual(a, Get(a.InstanceId));
            Assert.AreEqual(b, Get(b.InstanceId));
            Assert.IsTrue(service.Occupancy.TryGetOccupant(C(1, 0), out var at1) && at1 == a.InstanceId);
            Assert.IsTrue(service.Occupancy.TryGetOccupant(C(2, 0), out var at2) && at2 == b.InstanceId);
            Assert.IsFalse(service.Occupancy.IsOccupied(C(3, 0)));

            Assert.IsTrue(service.Redo());
            Assert.AreEqual(C(1, 0), Get(a.InstanceId).Cell);
            Assert.AreEqual(C(3, 0), Get(b.InstanceId).Cell);
            Assert.IsFalse(service.Occupancy.IsOccupied(C(0, 0)));
            Assert.AreEqual(3, service.Occupancy.OccupiedCellCount);
        }

        [Test]
        public void PlaceMany_PlacesAll_WithOneUndo_AndRedoKeepsIds()
        {
            var cmd = new PlaceManyCommand(new[]
            {
                new PlacedObject(0, "crate", C(0, 0), GridRotation.R0),
                new PlacedObject(0, "table", C(1, 0), GridRotation.R0),
            });

            Assert.IsTrue(service.Execute(cmd).IsValid);
            Assert.AreEqual(2, map.Count);
            Assert.AreEqual(2, cmd.Placed.Count);
            int firstId = cmd.Placed[0].InstanceId;

            Assert.IsTrue(service.Undo());
            Assert.AreEqual(0, map.Count);
            Assert.AreEqual(0, service.Occupancy.OccupiedCellCount);
            Assert.IsFalse(service.CanUndo);

            Assert.IsTrue(service.Redo());
            Assert.AreEqual(2, map.Count);
            Assert.AreEqual(firstId, cmd.Placed[0].InstanceId);
            Assert.IsTrue(map.TryGet(firstId, out _));
        }

        [Test]
        public void PlaceMany_CandidatesOverlapEachOther_PlacesNothing()
        {
            var result = service.Execute(new PlaceManyCommand(new[]
            {
                new PlacedObject(0, "table", C(0, 0), GridRotation.R0), // (0,0)(1,0)
                new PlacedObject(0, "crate", C(1, 0), GridRotation.R0),
            }));

            Assert.AreEqual(PlacementCodes.Overlap, result.Code);
            Assert.AreEqual(0, map.Count);
        }

        [Test]
        public void PlaceMany_OverExistingObject_PlacesNothing()
        {
            Place("crate", 5, 5);

            var result = service.Execute(new PlaceManyCommand(new[]
            {
                new PlacedObject(0, "crate", C(4, 5), GridRotation.R0),
                new PlacedObject(0, "crate", C(5, 5), GridRotation.R0),
            }));

            Assert.AreEqual(PlacementCodes.Overlap, result.Code);
            Assert.AreEqual(1, map.Count);
        }

        [Test]
        public void PlaceMany_IgnoresCandidateIds_SoSourceObjectsStillBlock()
        {
            // 복제할 때 원본을 그대로 넘겨도 원본 자리를 빈 칸으로 보지 않는다.
            var a = Place("crate", 0, 0);

            var result = service.Execute(new PlaceManyCommand(new[] { a }));

            Assert.AreEqual(PlacementCodes.Overlap, result.Code);
            Assert.AreEqual(1, map.Count);
        }

        [Test]
        public void RemoveMany_RemovesAll_UndoRestores()
        {
            var a = Place("crate", 0, 0);
            var b = Place("table", 3, 3);

            Assert.IsTrue(service.Execute(new RemoveManyCommand(new[] { a.InstanceId, b.InstanceId })).IsValid);
            Assert.AreEqual(0, map.Count);
            Assert.AreEqual(0, service.Occupancy.OccupiedCellCount);

            Assert.IsTrue(service.Undo());
            Assert.AreEqual(a, Get(a.InstanceId));
            Assert.AreEqual(b, Get(b.InstanceId));
            Assert.AreEqual(3, service.Occupancy.OccupiedCellCount);
        }

        [Test]
        public void RemoveMany_MissingId_RemovesNothing()
        {
            var a = Place("crate", 0, 0);

            var result = service.Execute(new RemoveManyCommand(new[] { a.InstanceId, 999 }));

            Assert.AreEqual(PlacementCodes.NotFound, result.Code);
            Assert.AreEqual(1, map.Count);
        }

        [Test]
        public void SingleMove_StillCannotEnterAnotherObjectsCell()
        {
            var a = Place("crate", 0, 0);
            Place("crate", 1, 0);

            var result = service.Execute(new MoveCommand(a.InstanceId, C(1, 0), GridRotation.R0));

            Assert.AreEqual(PlacementCodes.Overlap, result.Code);
        }
    }
}
