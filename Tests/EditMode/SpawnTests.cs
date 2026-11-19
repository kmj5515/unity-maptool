using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MapTool.Core.Tests
{
    public sealed class SpawnTests
    {
        private ObjectCatalog catalog;
        private MapData map;
        private PlacementService service;
        private List<string> events;

        [SetUp]
        public void SetUp()
        {
            catalog = new ObjectCatalog(new[]
            {
                new ObjectDefinition("crate", new Vector3Int(2, 1, 1)),
                new ObjectDefinition("road", new Vector3Int(3, 1, 3), PlacementKind.Grid, isFloor: true),
            });
            map = new MapData(new Vector3Int(10, 2, 10), new GridSpace(Vector3.zero, new Vector3(2f, 3f, 2f)));
            service = new PlacementService(map, catalog);

            events = new List<string>();
            service.OnSpawnAdded += s => events.Add($"added @{s.Id}");
            service.OnSpawnRemoved += s => events.Add($"removed @{s.Id}");
            service.OnSpawnChanged += (before, after) => events.Add($"changed @{after.Id} {before.Cell}->{after.Cell}");
            service.OnDefaultSpawnChanged += (before, after) => events.Add($"default {before}->{after}");
        }

        private static PlacedSpawn Candidate(int x, int z, GridRotation rotation = GridRotation.R0, string name = "입구") =>
            new(0, name, new Vector3Int(x, 0, z), rotation);

        private PlacedSpawn Add(PlacedSpawn candidate)
        {
            var cmd = new AddSpawnCommand(candidate);
            var result = service.Execute(cmd);
            Assert.IsTrue(result.IsValid, result.ToString());
            return cmd.Added;
        }

        private int DefaultId => map.TryGetDefaultSpawn(out var s) ? s.Id : 0;

        // ── 데이터 ─────────────────────────────────────────────

        [Test]
        public void World_IsCellBaseCenter_AndRotationFacesClockwise()
        {
            var spawn = new PlacedSpawn(1, "", new Vector3Int(3, 1, 4), GridRotation.R90);

            Assert.AreEqual(new Vector3(7f, 3f, 9f), spawn.WorldPosition(map.Grid));
            var forward = spawn.WorldRotation * Vector3.forward;
            Assert.That(Vector3.Distance(forward, Vector3.right), Is.LessThan(1e-5f), "R90은 +X를 본다");
        }

        [Test]
        public void Serialization_RoundTrip_KeepsSpawnsAndDefault()
        {
            Add(Candidate(1, 1));
            var second = Add(Candidate(2, 2, GridRotation.R180, "광장"));
            service.Execute(new SetDefaultSpawnCommand(second.Id));

            var copy = new MapData(Vector3Int.one, GridSpace.Unit);
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(map), copy);

            CollectionAssert.AreEqual(map.Spawns, copy.Spawns);
            Assert.AreEqual(second.Id, copy.DefaultSpawnId);
        }

        [Test]
        public void OldData_WithoutSpawns_HasNoDefault_AndAllocatesFromOne()
        {
            var old = new MapData(Vector3Int.one, GridSpace.Unit);
            JsonUtility.FromJsonOverwrite("{\"size\":{\"x\":4,\"y\":1,\"z\":4}}", old);

            Assert.AreEqual(0, old.Spawns.Count);
            Assert.IsFalse(old.TryGetDefaultSpawn(out _));
            Assert.AreEqual(1, old.AllocateSpawnId());
        }

        // ── 추가 ─────────────────────────────────────────────

        [Test]
        public void Add_First_BecomesDefault_SecondDoesNot()
        {
            var first = Add(Candidate(1, 1));
            var second = Add(Candidate(2, 2));

            Assert.AreEqual((1, 2), (first.Id, second.Id));
            Assert.AreEqual(first.Id, map.DefaultSpawnId);
            CollectionAssert.AreEqual(new[] { "added @1", "default 0->1", "added @2" }, events);
        }

        [Test]
        public void Add_OutOfBounds_Fails()
        {
            var result = service.Execute(new AddSpawnCommand(Candidate(10, 0)));

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(PlacementCodes.OutOfBounds, result.Code);
            Assert.AreEqual(0, map.Spawns.Count);
        }

        [Test]
        public void Add_OnOccupiedCell_IsAllowed()
        {
            Assert.IsTrue(service.Execute(new PlaceCommand("crate", new Vector3Int(1, 0, 1))).IsValid);
            Add(Candidate(1, 1));
            Assert.AreEqual(1, map.Spawns.Count);
        }

        [Test]
        public void Add_UndoRedo_RestoresDefaultAndKeepsId()
        {
            var first = Add(Candidate(1, 1));

            service.Undo();
            Assert.AreEqual(0, map.Spawns.Count);
            Assert.AreEqual(0, map.DefaultSpawnId);

            service.Redo();
            Assert.IsTrue(map.TryGetSpawn(first.Id, out _));
            Assert.AreEqual(first.Id, map.DefaultSpawnId);
        }

        // ── 수정 ─────────────────────────────────────────────

        [Test]
        public void Modify_MoveAndRotate_UndoRestores()
        {
            var spawn = Add(Candidate(1, 1));
            events.Clear();

            var moved = spawn.WithCell(new Vector3Int(5, 1, 6)).WithRotation(GridRotation.R270);
            Assert.IsTrue(service.Execute(new ModifySpawnCommand(moved)).IsValid);
            Assert.IsTrue(map.TryGetSpawn(spawn.Id, out var stored));
            Assert.AreEqual(moved, stored);

            service.Undo();
            Assert.IsTrue(map.TryGetSpawn(spawn.Id, out stored));
            Assert.AreEqual(spawn, stored);
            CollectionAssert.AreEqual(new[]
            {
                "changed @1 (1, 0, 1)->(5, 1, 6)",
                "changed @1 (5, 1, 6)->(1, 0, 1)",
            }, events);
        }

        [Test]
        public void Modify_OutOfBoundsOrMissing_Fails()
        {
            var spawn = Add(Candidate(1, 1));

            Assert.AreEqual(PlacementCodes.OutOfBounds,
                service.Execute(new ModifySpawnCommand(spawn.WithCell(new Vector3Int(0, 2, 0)))).Code);
            Assert.AreEqual(PlacementCodes.NotFound,
                service.Execute(new ModifySpawnCommand(new PlacedSpawn(99, "", Vector3Int.zero, GridRotation.R0))).Code);
        }

        // ── 삭제 ─────────────────────────────────────────────

        [Test]
        public void Remove_Default_PassesToFirstRemaining_UndoRestoresOrderAndDefault()
        {
            var a = Add(Candidate(1, 1));
            var b = Add(Candidate(2, 2));
            var c = Add(Candidate(3, 3));
            service.Execute(new SetDefaultSpawnCommand(b.Id));

            Assert.IsTrue(service.Execute(new RemoveSpawnCommand(b.Id)).IsValid);
            Assert.AreEqual(a.Id, map.DefaultSpawnId);

            service.Undo();
            CollectionAssert.AreEqual(new[] { a.Id, b.Id, c.Id }, map.Spawns.Select(s => s.Id));
            Assert.AreEqual(b.Id, map.DefaultSpawnId);
        }

        [Test]
        public void Remove_Last_LeavesNoDefault()
        {
            var a = Add(Candidate(1, 1));
            service.Execute(new RemoveSpawnCommand(a.Id));

            Assert.AreEqual(0, map.DefaultSpawnId);
            Assert.IsFalse(map.TryGetDefaultSpawn(out _));
        }

        [Test]
        public void Remove_NonDefault_KeepsDefault()
        {
            var a = Add(Candidate(1, 1));
            var b = Add(Candidate(2, 2));
            service.Execute(new RemoveSpawnCommand(b.Id));
            Assert.AreEqual(a.Id, map.DefaultSpawnId);
        }

        // ── 기본 지정 ─────────────────────────────────────────

        [Test]
        public void SetDefault_UndoRestores_SameOrMissingFails()
        {
            var a = Add(Candidate(1, 1));
            var b = Add(Candidate(2, 2));

            Assert.IsTrue(service.Execute(new SetDefaultSpawnCommand(b.Id)).IsValid);
            Assert.AreEqual(b.Id, DefaultId);
            Assert.AreEqual(PlacementCodes.NothingToDo, service.Execute(new SetDefaultSpawnCommand(b.Id)).Code);
            Assert.AreEqual(PlacementCodes.NotFound, service.Execute(new SetDefaultSpawnCommand(99)).Code);

            service.Undo();
            Assert.AreEqual(a.Id, DefaultId);
        }

        [Test]
        public void DefaultSpawn_FallsBackToFirst_WhenStoredIdIsDangling()
        {
            map.AddSpawn(new PlacedSpawn(4, "", Vector3Int.zero, GridRotation.R0));
            map.AddSpawn(new PlacedSpawn(5, "", Vector3Int.one, GridRotation.R0));
            map.SetDefaultSpawnId(42); // 에셋을 직접 고친 경우

            Assert.IsTrue(map.TryGetDefaultSpawn(out var spawn));
            Assert.AreEqual(4, spawn.Id);
        }

        // ── 검사 ─────────────────────────────────────────────

        [Test]
        public void Validate_NoSpawn_Warns()
        {
            var issues = MapValidator.Validate(map, catalog);

            Assert.AreEqual(1, issues.Count, string.Join("\n", issues));
            Assert.AreEqual((IssueSeverity.Warning, ValidationCodes.NoSpawn, IssueTarget.Spawn),
                (issues[0].Severity, issues[0].Code, issues[0].Target));
            Assert.AreEqual(0, issues[0].TargetIds.Count);
        }

        [Test]
        public void Validate_SpawnOnObject_WarnsWithObjectAsOther()
        {
            service.Execute(new PlaceCommand("crate", new Vector3Int(1, 0, 1))); // 2칸짜리: (1,0,1)~(2,0,1)
            var spawn = Add(Candidate(2, 1));

            var issues = MapValidator.Validate(map, catalog);

            Assert.AreEqual(1, issues.Count, string.Join("\n", issues));
            Assert.AreEqual((ValidationCodes.SpawnBlocked, spawn.Id, map.Objects[0].InstanceId),
                (issues[0].Code, issues[0].TargetId, issues[0].OtherId));
            Assert.AreEqual(IssueSeverity.Warning, issues[0].Severity);
        }

        [Test]
        public void Validate_SpawnOnFloor_NoIssue_ButObjectOnFloorStillBlocks()
        {
            service.Execute(new PlaceCommand("road", new Vector3Int(4, 0, 4))); // (4,0,4)~(6,0,6)
            var onRoad = Add(Candidate(5, 5));

            Assert.AreEqual(0, MapValidator.Validate(map, catalog).Count, "길 위 스폰은 막힘이 아닙니다.");
            Assert.IsFalse(MapValidator.TryFindSpawnBlocker(map, catalog, onRoad.Cell, out _));

            // 길과 상자는 같은 칸을 겹쳐 쓸 수 없으므로, 길 옆 칸에 상자를 두고 그 칸의 스폰만 막히는지 본다.
            Assert.IsTrue(service.Execute(new PlaceCommand("crate", new Vector3Int(7, 0, 5))).IsValid);
            var crate = map.Objects.First(o => o.DefinitionId == "crate");
            Assert.IsTrue(MapValidator.TryFindSpawnBlocker(map, catalog, new Vector3Int(8, 0, 5), out var blocker));
            Assert.AreEqual(crate.InstanceId, blocker.InstanceId);
            Assert.IsFalse(MapValidator.TryFindSpawnBlocker(map, catalog, new Vector3Int(6, 0, 5), out _));
        }

        [Test]
        public void Validate_SpawnOutOfBounds_AndDuplicateId_AreErrors()
        {
            map.AddSpawn(new PlacedSpawn(1, "", new Vector3Int(1, 0, 1), GridRotation.R0));
            map.AddSpawn(new PlacedSpawn(2, "", new Vector3Int(3, 0, 3), GridRotation.R0));
            map.AddSpawn(new PlacedSpawn(3, "", new Vector3Int(-1, 0, 3), GridRotation.R0));
            var broken = new MapData(Vector3Int.one, GridSpace.Unit);
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(map).Replace("\"id\":2", "\"id\":1"), broken);

            var issues = MapValidator.Validate(broken, catalog);

            CollectionAssert.AreEquivalent(
                new[] { (ValidationCodes.DuplicateId, 1), (ValidationCodes.OutOfBounds, 3) },
                issues.Select(i => (i.Code, i.TargetId)));
            Assert.IsTrue(issues.All(i => i.IsError && i.Target == IssueTarget.Spawn));
        }

        [Test]
        public void Validate_ValidSpawn_NoIssues()
        {
            Add(Candidate(4, 4));
            CollectionAssert.IsEmpty(MapValidator.Validate(map, catalog));
        }
    }
}
