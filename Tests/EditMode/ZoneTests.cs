using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MapTool.Core.Tests
{
    public sealed class ZoneTests
    {
        private ObjectCatalog catalog;
        private MapData map;
        private PlacementService service;
        private List<string> events;

        [SetUp]
        public void SetUp()
        {
            catalog = new ObjectCatalog(new[] { new ObjectDefinition("crate", new Vector3Int(1, 1, 1)) });
            map = new MapData(new Vector3Int(10, 2, 10), GridSpace.Unit);
            service = new PlacementService(map, catalog);

            events = new List<string>();
            service.OnZoneAdded += z => events.Add($"added #{z.Id}");
            service.OnZoneRemoved += z => events.Add($"removed #{z.Id}");
            service.OnZoneChanged += (before, after) => events.Add($"changed #{after.Id} p{before.Priority}->p{after.Priority}");
        }

        private static ZoneBox Box(int x, int z, int sx, int sz) => new(new Vector3Int(x, 0, z), new Vector3Int(sx, 1, sz));

        private static PlacedZone Candidate(string profile = "forest", int priority = 0, params ZoneBox[] boxes) =>
            new(0, "숲", profile, priority, boxes.Length > 0 ? boxes : new[] { Box(0, 0, 4, 4) });

        private PlacedZone Add(PlacedZone candidate)
        {
            var cmd = new AddZoneCommand(candidate);
            var result = service.Execute(cmd);
            Assert.IsTrue(result.IsValid, result.ToString());
            return cmd.Added;
        }

        [Test]
        public void AddZone_AssignsId_StoresAndRaisesEvent()
        {
            var zone = Add(Candidate());

            Assert.AreEqual(1, zone.Id);
            Assert.IsTrue(map.TryGetZone(1, out var stored));
            Assert.AreEqual(zone, stored);
            Assert.AreEqual("forest", stored.ProfileId);
            CollectionAssert.AreEqual(new[] { "added #1" }, events);
        }

        [Test]
        public void Zone_DoesNotOccupyCells_AndMayOverlapObjectsAndZones()
        {
            Add(Candidate("forest"));
            Add(Candidate("cave", 1, Box(2, 2, 4, 4)));

            Assert.AreEqual(2, map.Zones.Count);
            Assert.AreEqual(0, service.Occupancy.OccupiedCellCount);
            Assert.IsTrue(service.Execute(new PlaceCommand("crate", new Vector3Int(1, 0, 1))).IsValid);
        }

        [Test]
        public void ZoneIds_AreIndependentFromObjectIds()
        {
            service.Execute(new PlaceCommand("crate", Vector3Int.zero));
            service.Execute(new PlaceCommand("crate", new Vector3Int(1, 0, 0)));

            Assert.AreEqual(1, Add(Candidate()).Id);
        }

        [Test]
        public void InvalidZones_AreRejected_AndMapUnchanged()
        {
            Assert.AreEqual(PlacementCodes.InvalidZone,
                service.Execute(new AddZoneCommand(new PlacedZone(0, "", "forest", 0, new ZoneBox[0]))).Code);
            Assert.AreEqual(PlacementCodes.InvalidZone,
                service.Execute(new AddZoneCommand(Candidate(boxes: new[] { Box(0, 0, 0, 3) }))).Code);
            Assert.AreEqual(PlacementCodes.OutOfBounds,
                service.Execute(new AddZoneCommand(Candidate(boxes: new[] { Box(8, 8, 4, 1) }))).Code);
            Assert.AreEqual(PlacementCodes.OutOfBounds,
                service.Execute(new AddZoneCommand(Candidate(boxes: new[] { new ZoneBox(new Vector3Int(0, 0, 0), new Vector3Int(1, 3, 1)) }))).Code);

            Assert.AreEqual(0, map.Zones.Count);
            Assert.IsFalse(service.CanUndo);
            CollectionAssert.IsEmpty(events);
        }

        [Test]
        public void EmptyProfile_IsAllowed()
        {
            var zone = Add(Candidate(profile: ""));
            Assert.AreEqual(string.Empty, zone.ProfileId);
        }

        [Test]
        public void ModifyZone_ReplacesValue_UndoRestores_RedoReapplies()
        {
            var zone = Add(Candidate());
            var edited = zone.WithPriority(5).WithBoxes(new[] { Box(0, 0, 2, 2), Box(5, 5, 2, 2) });

            Assert.IsTrue(service.Execute(new ModifyZoneCommand(edited)).IsValid);
            map.TryGetZone(zone.Id, out var current);
            Assert.AreEqual(edited, current);

            service.Undo();
            map.TryGetZone(zone.Id, out current);
            Assert.AreEqual(zone, current);

            service.Redo();
            map.TryGetZone(zone.Id, out current);
            Assert.AreEqual(edited, current);

            CollectionAssert.AreEqual(new[] { "added #1", "changed #1 p0->p5", "changed #1 p5->p0", "changed #1 p0->p5" }, events);
        }

        [Test]
        public void ModifyZone_InvalidOrMissing_Fails()
        {
            var zone = Add(Candidate());

            Assert.AreEqual(PlacementCodes.OutOfBounds,
                service.Execute(new ModifyZoneCommand(zone.WithBoxes(new[] { Box(9, 9, 2, 2) }))).Code);
            Assert.AreEqual(PlacementCodes.NotFound,
                service.Execute(new ModifyZoneCommand(new PlacedZone(99, "", "x", 0, new[] { Box(0, 0, 1, 1) }))).Code);

            map.TryGetZone(zone.Id, out var current);
            Assert.AreEqual(zone, current);
        }

        [Test]
        public void RemoveZone_UndoRestoresSameId()
        {
            var zone = Add(Candidate());

            Assert.IsTrue(service.Execute(new RemoveZoneCommand(zone.Id)).IsValid);
            Assert.AreEqual(0, map.Zones.Count);
            Assert.AreEqual(PlacementCodes.NotFound, service.Execute(new RemoveZoneCommand(zone.Id)).Code);

            service.Undo();
            Assert.IsTrue(map.TryGetZone(zone.Id, out var restored));
            Assert.AreEqual(zone, restored);
        }

        [Test]
        public void UndoRedoAdd_KeepsSameId()
        {
            var zone = Add(Candidate());
            service.Undo();
            Assert.AreEqual(0, map.Zones.Count);

            service.Redo();
            Assert.IsTrue(map.TryGetZone(zone.Id, out _));
            Assert.AreEqual(2, Add(Candidate()).Id);
        }

        [Test]
        public void ObjectsAndZones_ShareOneUndoStack()
        {
            service.Execute(new PlaceCommand("crate", Vector3Int.zero));
            Add(Candidate());

            service.Undo(); // 마지막 작업인 존부터 되돌린다
            Assert.AreEqual(0, map.Zones.Count);
            Assert.AreEqual(1, map.Count);

            service.Undo();
            Assert.AreEqual(0, map.Count);
        }

        [Test]
        public void PlacedZone_CopiesBoxes_AndComputesShape()
        {
            var boxes = new[] { Box(0, 0, 2, 2), Box(4, 1, 3, 1) };
            var zone = new PlacedZone(0, "n", "p", 0, boxes);
            boxes[0] = Box(9, 9, 1, 1);

            Assert.AreEqual(Box(0, 0, 2, 2), zone.Boxes[0]);
            Assert.AreEqual(7, zone.CellCount);
            Assert.AreEqual(new BoundsInt(new Vector3Int(0, 0, 0), new Vector3Int(7, 1, 2)), zone.Bounds);
            Assert.IsTrue(zone.Contains(new Vector3Int(1, 0, 1)));
            Assert.IsTrue(zone.Contains(new Vector3Int(6, 0, 1)));
            Assert.IsFalse(zone.Contains(new Vector3Int(3, 0, 0)));
            Assert.IsFalse(zone.Contains(new Vector3Int(1, 1, 1)));
        }

        [Test]
        public void JsonRoundTrip_RestoresZonesAndNextZoneId()
        {
            Add(Candidate("forest", 2, Box(0, 0, 2, 2), Box(3, 3, 2, 2)));
            var second = Add(Candidate("cave"));
            service.Execute(new RemoveZoneCommand(second.Id));

            var loaded = JsonUtility.FromJson<MapData>(JsonUtility.ToJson(map));
            var reloaded = new PlacementService(loaded, catalog);

            Assert.AreEqual(1, loaded.Zones.Count);
            Assert.AreEqual(map.Zones[0], loaded.Zones[0]);

            var next = new AddZoneCommand(Candidate());
            reloaded.Execute(next);
            Assert.AreEqual(3, next.Added.Id);
        }

        [Test]
        public void FaceMoved_GrowsAndShrinks_KeepingOppositeFace()
        {
            var box = new ZoneBox(new Vector3Int(2, 0, 2), new Vector3Int(3, 1, 3));
            var mapSize = new Vector3Int(10, 2, 10);

            Assert.AreEqual(new ZoneBox(new Vector3Int(2, 0, 2), new Vector3Int(5, 1, 3)), box.WithFaceMoved(0, true, 2, mapSize), "+X 면을 밖으로");
            Assert.AreEqual(new ZoneBox(new Vector3Int(1, 0, 2), new Vector3Int(4, 1, 3)), box.WithFaceMoved(0, false, 1, mapSize), "-X 면을 밖으로");
            Assert.AreEqual(new ZoneBox(new Vector3Int(2, 0, 3), new Vector3Int(3, 1, 2)), box.WithFaceMoved(2, false, -1, mapSize), "-Z 면을 안으로");
            Assert.AreEqual(new ZoneBox(new Vector3Int(2, 0, 2), new Vector3Int(3, 2, 3)), box.WithFaceMoved(1, true, 1, mapSize), "+Y 면을 위로");
        }

        [Test]
        public void FaceMoved_ClampsToMinimumSizeAndMapBounds()
        {
            var box = new ZoneBox(new Vector3Int(2, 0, 2), new Vector3Int(3, 1, 3));
            var mapSize = new Vector3Int(10, 2, 10);

            Assert.AreEqual(1, box.WithFaceMoved(0, true, -10, mapSize).Size.x, "최소 1칸");
            Assert.AreEqual(4, box.WithFaceMoved(0, false, -10, mapSize).Min.x, "반대 면 바로 앞까지만");
            Assert.AreEqual(10, box.WithFaceMoved(0, true, 100, mapSize).Bounds.xMax, "맵 끝까지만");
            Assert.AreEqual(0, box.WithFaceMoved(2, false, 100, mapSize).Min.z, "맵 시작까지만");
            Assert.AreEqual(2, box.WithFaceMoved(1, true, 5, mapSize).Size.y, "맵 높이까지만");
        }

        [Test]
        public void LegacyJsonWithoutZones_LoadsEmpty_AndAllocatesFromOne()
        {
            var legacy = JsonUtility.FromJson<MapData>(
                "{\"size\":{\"x\":10,\"y\":1,\"z\":10},\"objects\":[],\"nextInstanceId\":1}");
            var legacyService = new PlacementService(legacy, catalog);

            Assert.AreEqual(0, legacy.Zones.Count);
            var cmd = new AddZoneCommand(Candidate());
            Assert.IsTrue(legacyService.Execute(cmd).IsValid);
            Assert.AreEqual(1, cmd.Added.Id);
        }
    }
}
