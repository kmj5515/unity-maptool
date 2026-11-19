using NUnit.Framework;
using UnityEngine;

namespace MapTool.Core.Tests
{
    public sealed class PlacementTests
    {
        // crate 1x1x1, table 2x1x1 (X로 긴 것), bed 2x1x3
        private ObjectCatalog catalog;
        private MapData map;
        private PlacementService service;

        [SetUp]
        public void SetUp()
        {
            catalog = new ObjectCatalog(new[]
            {
                new ObjectDefinition("crate", new Vector3Int(1, 1, 1)),
                new ObjectDefinition("table", new Vector3Int(2, 1, 1)),
                new ObjectDefinition("bed", new Vector3Int(2, 1, 3)),
            });
            map = new MapData(new Vector3Int(10, 2, 10), GridSpace.Unit);
            service = new PlacementService(map, catalog);
        }

        private PlacedObject Place(string id, int x, int z, GridRotation r = GridRotation.R0)
        {
            var cmd = new PlaceCommand(id, new Vector3Int(x, 0, z), r);
            var result = service.Execute(cmd);
            Assert.IsTrue(result.IsValid, result.ToString());
            return cmd.Placed;
        }

        [Test]
        public void Place_Valid_AddsObjectAndOccupiesCells()
        {
            var table = Place("table", 3, 4);

            Assert.AreEqual(1, map.Count);
            Assert.AreNotEqual(0, table.InstanceId);
            Assert.IsTrue(service.Occupancy.TryGetOccupant(new Vector3Int(4, 0, 4), out var occupant));
            Assert.AreEqual(table.InstanceId, occupant);
            Assert.AreEqual(2, service.Occupancy.OccupiedCellCount);
        }

        [Test]
        public void Place_OnOccupiedCell_FailsWithOverlap_AndMapUnchanged()
        {
            Place("table", 0, 0);

            var result = service.Execute(new PlaceCommand("crate", new Vector3Int(1, 0, 0)));

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(PlacementCodes.Overlap, result.Code);
            Assert.AreEqual(1, map.Count);
            Assert.IsFalse(service.CanRedo);
        }

        [Test]
        public void FailureMessages_NameDefinition_NotInstanceNumber()
        {
            var table = Place("table", 0, 0);

            var overlap = service.Execute(new PlaceCommand("crate", new Vector3Int(1, 0, 0)));
            StringAssert.Contains("'table'", overlap.Message);
            StringAssert.DoesNotContain($"#{table.InstanceId}", overlap.Message);

            var outside = service.Execute(new PlaceCommand("table", new Vector3Int(9, 0, 0)));
            Assert.AreEqual(PlacementCodes.OutOfBounds, outside.Code);
            StringAssert.Contains("'table'", outside.Message);
        }

        [Test]
        public void Place_SameCellDifferentLayer_IsAllowed()
        {
            Place("crate", 0, 0);

            var result = service.Execute(new PlaceCommand("crate", new Vector3Int(0, 1, 0)));

            Assert.IsTrue(result.IsValid, result.ToString());
        }

        [Test]
        public void Rotation_ChangesFootprint_ForOverlap()
        {
            // R90 테이블은 X가 아니라 Z로 두 칸을 차지한다
            Place("table", 0, 0, GridRotation.R90);

            Assert.IsTrue(service.CanPlace(new PlacedObject(0, "crate", new Vector3Int(1, 0, 0), GridRotation.R0)).IsValid);
            Assert.AreEqual(PlacementCodes.Overlap,
                service.CanPlace(new PlacedObject(0, "crate", new Vector3Int(0, 0, 1), GridRotation.R0)).Code);
        }

        [Test]
        public void Rotation_ChangesFootprint_ForBounds()
        {
            // 맵 X 끝(9)에서 R0 테이블은 10까지 나가지만 R90이면 X로 한 칸이다
            Assert.AreEqual(PlacementCodes.OutOfBounds,
                service.CanPlace(new PlacedObject(0, "table", new Vector3Int(9, 0, 0), GridRotation.R0)).Code);
            Assert.IsTrue(service.CanPlace(new PlacedObject(0, "table", new Vector3Int(9, 0, 0), GridRotation.R90)).IsValid);
        }

        [Test]
        public void Place_OutsideMap_FailsWithOutOfBounds()
        {
            Assert.AreEqual(PlacementCodes.OutOfBounds,
                service.Execute(new PlaceCommand("crate", new Vector3Int(-1, 0, 0))).Code);
            Assert.AreEqual(PlacementCodes.OutOfBounds,
                service.Execute(new PlaceCommand("crate", new Vector3Int(0, 2, 0))).Code);
            Assert.AreEqual(0, map.Count);
        }

        [Test]
        public void Place_UnknownDefinition_Fails()
        {
            var result = service.Execute(new PlaceCommand("piano", Vector3Int.zero));

            Assert.AreEqual(PlacementCodes.UnknownDefinition, result.Code);
        }

        [Test]
        public void Move_OverlappingOnlyItself_IsAllowed()
        {
            var bed = Place("bed", 0, 0); // (0..1, 0..2)

            var result = service.Execute(new MoveCommand(bed.InstanceId, new Vector3Int(1, 0, 0), GridRotation.R0));

            Assert.IsTrue(result.IsValid, result.ToString());
            Assert.IsFalse(service.Occupancy.IsOccupied(new Vector3Int(0, 0, 0)), "옛 자리는 비워진다");
            Assert.IsTrue(service.Occupancy.IsOccupied(new Vector3Int(2, 0, 2)));
            Assert.AreEqual(6, service.Occupancy.OccupiedCellCount);
        }

        [Test]
        public void Move_IntoOther_Fails_AndKeepsOriginalPose()
        {
            var crate = Place("crate", 0, 0);
            Place("crate", 5, 5);

            var result = service.Execute(new MoveCommand(crate.InstanceId, new Vector3Int(5, 0, 5), GridRotation.R0));

            Assert.AreEqual(PlacementCodes.Overlap, result.Code);
            Assert.IsTrue(map.TryGet(crate.InstanceId, out var current));
            Assert.AreEqual(crate, current);
        }

        [Test]
        public void Rotate_InPlace_UpdatesFootprint()
        {
            var table = Place("table", 0, 0);

            var result = service.Execute(new MoveCommand(table.InstanceId, table.Cell, GridRotation.R90));

            Assert.IsTrue(result.IsValid, result.ToString());
            Assert.IsFalse(service.Occupancy.IsOccupied(new Vector3Int(1, 0, 0)));
            Assert.IsTrue(service.Occupancy.IsOccupied(new Vector3Int(0, 0, 1)));
        }

        [Test]
        public void Remove_FreesCells()
        {
            var table = Place("table", 0, 0);

            service.Execute(new RemoveCommand(table.InstanceId));

            Assert.AreEqual(0, map.Count);
            Assert.AreEqual(0, service.Occupancy.OccupiedCellCount);
            Assert.IsTrue(service.Execute(new PlaceCommand("crate", new Vector3Int(1, 0, 0))).IsValid);
        }

        [Test]
        public void MoveOrRemove_Missing_FailsWithNotFound()
        {
            Assert.AreEqual(PlacementCodes.NotFound, service.Execute(new RemoveCommand(42)).Code);
            Assert.AreEqual(PlacementCodes.NotFound,
                service.Execute(new MoveCommand(42, Vector3Int.zero, GridRotation.R0)).Code);
        }

        [Test]
        public void CustomRule_IsChecked_AfterDefaults()
        {
            var rules = PlacementService.CreateDefaultRules();
            rules.Add(new GroundFloorOnlyRule());
            service = new PlacementService(new MapData(new Vector3Int(10, 2, 10), GridSpace.Unit), catalog, rules);

            var result = service.Execute(new PlaceCommand("crate", new Vector3Int(0, 1, 0)));

            Assert.AreEqual("ground-only", result.Code);
        }

        [Test]
        public void MapData_JsonRoundTrip_RestoresObjectsOccupancyAndNextId()
        {
            Place("table", 0, 0);
            var crate = Place("crate", 5, 5);
            service.Execute(new RemoveCommand(crate.InstanceId)); // ID 2는 비고 다음 ID는 3이어야 한다

            var json = JsonUtility.ToJson(map);
            var loaded = JsonUtility.FromJson<MapData>(json);
            var reloaded = new PlacementService(loaded, catalog);

            Assert.AreEqual(map.Size, loaded.Size);
            Assert.AreEqual(1, loaded.Count);
            Assert.AreEqual(PlacementCodes.Overlap,
                reloaded.Execute(new PlaceCommand("crate", new Vector3Int(1, 0, 0))).Code);

            var next = new PlaceCommand("crate", new Vector3Int(9, 0, 9));
            reloaded.Execute(next);
            Assert.AreEqual(3, next.Placed.InstanceId);
        }

        private sealed class GroundFloorOnlyRule : IPlacementRule
        {
            public PlacementResult Check(in PlacementContext context) =>
                context.Bounds.yMin == 0 ? PlacementResult.Ok : PlacementResult.Fail("ground-only", "1층에만 놓을 수 있습니다.");
        }
    }
}
