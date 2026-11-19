using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MapTool.Core.Tests
{
    public sealed class UndoTests
    {
        private MapData map;
        private PlacementService service;
        private List<string> events;

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

            events = new List<string>();
            service.OnPlaced += p => events.Add($"placed #{p.InstanceId} {p.Cell}");
            service.OnRemoved += p => events.Add($"removed #{p.InstanceId}");
            service.OnMoved += (before, after) => events.Add($"moved #{after.InstanceId} {before.Cell}->{after.Cell} {after.Rotation}");
        }

        private PlacedObject Place(string id, int x, int z)
        {
            var cmd = new PlaceCommand(id, new Vector3Int(x, 0, z));
            Assert.IsTrue(service.Execute(cmd).IsValid);
            return cmd.Placed;
        }

        [Test]
        public void UndoPlace_RemovesAndFreesCells_RedoRestoresSameId()
        {
            var table = Place("table", 0, 0);

            Assert.IsTrue(service.Undo());
            Assert.AreEqual(0, map.Count);
            Assert.AreEqual(0, service.Occupancy.OccupiedCellCount);

            Assert.IsTrue(service.Redo());
            Assert.IsTrue(map.TryGet(table.InstanceId, out var restored));
            Assert.AreEqual(table, restored);
            Assert.AreEqual(2, service.Occupancy.OccupiedCellCount);
        }

        [Test]
        public void UndoMove_RestoresPoseAndOccupancy()
        {
            var table = Place("table", 0, 0);
            service.Execute(new MoveCommand(table.InstanceId, new Vector3Int(5, 0, 5), GridRotation.R90));

            service.Undo();

            Assert.IsTrue(map.TryGet(table.InstanceId, out var current));
            Assert.AreEqual(table, current);
            Assert.IsTrue(service.Occupancy.IsOccupied(new Vector3Int(1, 0, 0)));
            Assert.IsFalse(service.Occupancy.IsOccupied(new Vector3Int(5, 0, 5)));
        }

        [Test]
        public void UndoRemove_RestoresObject()
        {
            var crate = Place("crate", 3, 3);
            service.Execute(new RemoveCommand(crate.InstanceId));

            service.Undo();

            Assert.IsTrue(map.TryGet(crate.InstanceId, out var restored));
            Assert.AreEqual(crate, restored);
            Assert.IsTrue(service.Occupancy.IsOccupied(new Vector3Int(3, 0, 3)));
        }

        [Test]
        public void NewCommand_AfterUndo_ClearsRedo()
        {
            Place("crate", 0, 0);
            service.Undo();
            Assert.IsTrue(service.CanRedo);

            Place("crate", 1, 1);

            Assert.IsFalse(service.CanRedo);
            Assert.IsFalse(service.Redo());
        }

        [Test]
        public void FailedCommand_IsNotRecorded()
        {
            Place("crate", 0, 0);
            service.Execute(new PlaceCommand("crate", Vector3Int.zero)); // 겹침

            Assert.IsTrue(service.Undo());
            Assert.IsFalse(service.CanUndo, "실패한 커맨드는 스택에 없다");
        }

        [Test]
        public void UndoRedo_OnEmptyStacks_ReturnFalse()
        {
            Assert.IsFalse(service.Undo());
            Assert.IsFalse(service.Redo());
        }

        [Test]
        public void MultipleUndo_RevertsInReverseOrder()
        {
            var a = Place("crate", 0, 0);
            service.Execute(new MoveCommand(a.InstanceId, new Vector3Int(1, 0, 0), GridRotation.R0));
            service.Execute(new MoveCommand(a.InstanceId, new Vector3Int(2, 0, 0), GridRotation.R0));

            service.Undo();
            service.Undo();

            Assert.IsTrue(map.TryGet(a.InstanceId, out var current));
            Assert.AreEqual(new Vector3Int(0, 0, 0), current.Cell);
            service.Undo();
            Assert.AreEqual(0, map.Count);
        }

        [Test]
        public void Events_AreRaisedForExecuteUndoAndRedo()
        {
            var crate = Place("crate", 0, 0);
            service.Execute(new MoveCommand(crate.InstanceId, new Vector3Int(2, 0, 0), GridRotation.R90));
            service.Undo();
            service.Undo();
            service.Redo();

            int id = crate.InstanceId;
            CollectionAssert.AreEqual(new[]
            {
                $"placed #{id} {new Vector3Int(0, 0, 0)}",
                $"moved #{id} {new Vector3Int(0, 0, 0)}->{new Vector3Int(2, 0, 0)} R90",
                $"moved #{id} {new Vector3Int(2, 0, 0)}->{new Vector3Int(0, 0, 0)} R0",
                $"removed #{id}",
                $"placed #{id} {new Vector3Int(0, 0, 0)}",
            }, events);
        }

        [Test]
        public void FailedCommand_RaisesNoEvents()
        {
            Place("crate", 0, 0);
            events.Clear();

            service.Execute(new PlaceCommand("crate", Vector3Int.zero));
            service.Execute(new RemoveCommand(999));

            CollectionAssert.IsEmpty(events);
        }

        [Test]
        public void ClearHistory_DropsUndoAndRedo()
        {
            Place("crate", 0, 0);
            Place("crate", 1, 0);
            service.Undo();

            service.ClearHistory();

            Assert.IsFalse(service.CanUndo);
            Assert.IsFalse(service.CanRedo);
            Assert.AreEqual(1, map.Count);
        }
    }
}
