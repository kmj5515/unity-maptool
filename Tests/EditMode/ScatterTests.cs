using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MapTool.Core.Tests
{
    public sealed class ScatterTests
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
                new ObjectDefinition("crate", new Vector3Int(1, 1, 1)),
                ObjectDefinition.Scatter("grass"),
                ObjectDefinition.Scatter("flower"),
            });
            map = new MapData(new Vector3Int(20, 2, 20), GridSpace.Unit);
            service = new PlacementService(map, catalog);

            events = new List<string>();
            service.OnScattersAdded += list => events.Add($"added {list.Count}");
            service.OnScattersRemoved += list => events.Add($"removed {list.Count}");
        }

        private static PlacedScatter At(float x, float z, string id = "grass", float scale = 1f) =>
            new(0, id, new Vector3(x, 0f, z), Quaternion.identity, scale);

        private IReadOnlyList<PlacedScatter> Add(params PlacedScatter[] candidates)
        {
            var cmd = new AddScattersCommand(candidates);
            var result = service.Execute(cmd);
            Assert.IsTrue(result.IsValid, result.ToString());
            return cmd.Added;
        }

        // 평지(높이 0). 맵 전체에서 지면이 있다.
        private static bool Flat(Vector2 xz, out Vector3 point, out Vector3 normal)
        {
            point = new Vector3(xz.x, 0f, xz.y);
            normal = Vector3.up;
            return true;
        }

        private static readonly ScatterEntry[] GrassOnly = { new("grass", 1f) };

        // ---- 데이터·커맨드 ----

        [Test]
        public void AddScatters_AssignsIds_DoesNotOccupyCells()
        {
            var added = Add(At(1.5f, 1.5f), At(2.2f, 1.5f));

            CollectionAssert.AreEqual(new[] { 1, 2 }, added.Select(s => s.Id));
            Assert.AreEqual(2, map.ScatterCount);
            Assert.AreEqual(0, service.Occupancy.OccupiedCellCount);
            Assert.IsTrue(service.Execute(new PlaceCommand("crate", new Vector3Int(1, 0, 1))).IsValid, "장식 위에도 칸 오브젝트를 놓을 수 있다");
            CollectionAssert.AreEqual(new[] { "added 2" }, events);
        }

        [Test]
        public void AddScatters_SkipsInvalidCandidates_KeepsValidOnes()
        {
            var cmd = new AddScattersCommand(new[]
            {
                At(1f, 1f),
                At(-1f, 1f),            // 맵 밖
                At(2f, 2f, "crate"),    // 칸 오브젝트
                At(3f, 3f, scale: 0f),  // 스케일 0
                At(4f, 4f, "nothing"),  // 모르는 정의
            });

            Assert.IsTrue(service.Execute(cmd).IsValid);
            Assert.AreEqual(1, cmd.Added.Count);
            Assert.AreEqual(4, cmd.Skipped);
        }

        [Test]
        public void AddScatters_AllInvalid_FailsWithFirstReason_AndLeavesNoHistory()
        {
            var result = service.Execute(new AddScattersCommand(new[] { At(-1f, 0f), At(1f, 1f, "crate") }));

            Assert.IsFalse(result.IsValid);
            Assert.AreEqual(PlacementCodes.OutOfBounds, result.Code);
            Assert.IsFalse(service.CanUndo);
            Assert.AreEqual(0, map.ScatterCount);
        }

        [Test]
        public void PlaceCommand_RejectsScatterDefinition()
        {
            var result = service.Execute(new PlaceCommand("grass", new Vector3Int(0, 0, 0)));

            Assert.AreEqual(PlacementCodes.WrongKind, result.Code);
        }

        [Test]
        public void RemoveScatters_SkipsMissing_UndoRestoresSameIds()
        {
            var added = Add(At(1f, 1f), At(2f, 2f), At(3f, 3f));

            var cmd = new RemoveScattersCommand(new[] { added[0].Id, added[2].Id, 999 });
            Assert.IsTrue(service.Execute(cmd).IsValid);
            Assert.AreEqual(1, map.ScatterCount);
            Assert.AreEqual(2, cmd.Removed.Count);

            Assert.IsTrue(service.Undo());
            Assert.AreEqual(3, map.ScatterCount);
            Assert.IsTrue(map.TryGetScatter(added[2].Id, out var back));
            Assert.AreEqual(added[2], back);
        }

        [Test]
        public void RemoveScatters_NoneFound_Fails()
        {
            Assert.AreEqual(PlacementCodes.NotFound, service.Execute(new RemoveScattersCommand(new[] { 5 })).Code);
        }

        [Test]
        public void UndoRedo_AddScatters_ReusesIds_AndUpdatesIndex()
        {
            var added = Add(At(5f, 5f), At(6f, 5f));

            service.Undo();
            Assert.AreEqual(0, map.ScatterCount);
            Assert.AreEqual(0, service.ScatterIndex.Count);

            service.Redo();
            CollectionAssert.AreEqual(added.Select(s => s.Id), map.Scatters.Select(s => s.Id));
            Assert.AreEqual(2, service.ScatterIndex.Count);
            CollectionAssert.AreEqual(new[] { "added 2", "removed 2", "added 2" }, events);
        }

        [Test]
        public void NewService_RebuildsIndex_FromLoadedMap_AndIdsContinue()
        {
            Add(At(1f, 1f), At(1.2f, 1f));

            var reloaded = new PlacementService(map, catalog);
            var found = new List<PlacedScatter>();
            reloaded.ScatterIndex.Query(new Vector3(1f, 0f, 1f), 0.5f, found);
            Assert.AreEqual(2, found.Count);

            var next = new AddScattersCommand(new[] { At(8f, 8f) });
            reloaded.Execute(next);
            Assert.AreEqual(3, next.Added[0].Id);
        }

        // ---- Undo 묶음 ----

        [Test]
        public void Group_ManyCommands_UndoOnce_RedoOnce()
        {
            service.BeginGroup();
            Add(At(1f, 1f));
            Add(At(2f, 2f));
            service.Execute(new RemoveScattersCommand(new[] { 1 }));
            service.EndGroup();

            Assert.AreEqual(1, map.ScatterCount);
            Assert.IsTrue(service.Undo());
            Assert.AreEqual(0, map.ScatterCount);
            Assert.IsFalse(service.CanUndo, "묶음은 Undo 한 칸");

            Assert.IsTrue(service.Redo());
            Assert.AreEqual(1, map.ScatterCount);
            Assert.IsTrue(map.TryGetScatter(2, out _));
        }

        [Test]
        public void Group_Empty_PushesNothing()
        {
            service.BeginGroup();
            service.Execute(new RemoveScattersCommand(new[] { 1 })); // 실패
            service.EndGroup();

            Assert.IsFalse(service.CanUndo);
        }

        [Test]
        public void Undo_WhileGrouping_ClosesGroupThenUndoesIt()
        {
            Add(At(9f, 9f)); // 묶음 밖

            service.BeginGroup();
            Add(At(1f, 1f));
            Add(At(2f, 2f));
            Assert.IsTrue(service.CanUndo);

            Assert.IsTrue(service.Undo());
            Assert.IsFalse(service.IsGrouping);
            Assert.AreEqual(1, map.ScatterCount, "묶음 두 개만 되돌리고 앞의 것은 남는다");
        }

        [Test]
        public void Execute_InsideGroup_ClearsRedo()
        {
            Add(At(1f, 1f));
            service.Undo();
            Assert.IsTrue(service.CanRedo);

            service.BeginGroup();
            Add(At(2f, 2f));
            Assert.IsFalse(service.CanRedo);
            service.EndGroup();
        }

        // ---- 공간 색인 ----

        [Test]
        public void SpatialIndex_QueryUsesXZDistance_AcrossBuckets()
        {
            var index = new ScatterSpatialIndex(1f);
            index.Add(new PlacedScatter(1, "grass", new Vector3(0.9f, 0f, 0.9f), Quaternion.identity, 1f));
            index.Add(new PlacedScatter(2, "grass", new Vector3(1.1f, 5f, 1.1f), Quaternion.identity, 1f)); // 높이는 무시
            index.Add(new PlacedScatter(3, "grass", new Vector3(4f, 0f, 4f), Quaternion.identity, 1f));

            var found = new List<PlacedScatter>();
            index.Query(new Vector3(1f, 0f, 1f), 0.5f, found);
            CollectionAssert.AreEquivalent(new[] { 1, 2 }, found.Select(s => s.Id));

            Assert.IsTrue(index.AnyWithin(new Vector3(4.2f, 0f, 4f), 0.3f));
            Assert.IsFalse(index.AnyWithin(new Vector3(4.3f, 0f, 4f), 0.3f), "경계는 간격 위반이 아니다");

            Assert.IsTrue(index.Remove(new PlacedScatter(2, "grass", new Vector3(1.1f, 5f, 1.1f), Quaternion.identity, 1f)));
            Assert.AreEqual(2, index.Count);
        }

        // ---- 브러시 계산 ----

        [Test]
        public void Generate_FillsToDensity_AndRespectsSpacing()
        {
            var settings = new ScatterBrushSettings { Radius = 3f, Density = 0.5f, MinSpacing = 0.6f };
            var center = new Vector3(10f, 0f, 10f);

            var made = ScatterBrush.Generate(center, settings, GrassOnly, service, Flat, new System.Random(1));

            // 기대값 π·9·0.5 ≈ 14.1 → 14 또는 15
            Assert.That(made.Count, Is.InRange(12, 15));
            foreach (var s in made)
            {
                Assert.LessOrEqual(new Vector2(s.Position.x - 10f, s.Position.z - 10f).magnitude, 3f + 1e-4f);
                Assert.AreEqual("grass", s.DefinitionId);
            }
            for (int i = 0; i < made.Count; i++)
                for (int j = i + 1; j < made.Count; j++)
                    Assert.GreaterOrEqual(Vector3.Distance(made[i].Position, made[j].Position), 0.6f - 1e-4f);
        }

        [Test]
        public void Generate_SecondDabOnSameSpot_AddsLittleOrNothing()
        {
            var settings = new ScatterBrushSettings { Radius = 2f, Density = 1f, MinSpacing = 0.3f };
            var center = new Vector3(10f, 0f, 10f);
            var rng = new System.Random(7);

            var first = ScatterBrush.Generate(center, settings, GrassOnly, service, Flat, rng);
            service.Execute(new AddScattersCommand(first));
            var second = ScatterBrush.Generate(center, settings, GrassOnly, service, Flat, rng);

            Assert.Greater(first.Count, 8);
            Assert.LessOrEqual(second.Count, 2, "이미 밀도만큼 차 있으면 문질러도 거의 늘지 않는다");
        }

        [Test]
        public void Generate_SlopeAndHeightFilters_RejectSurface()
        {
            var settings = new ScatterBrushSettings { Radius = 2f, Density = 2f, MinSpacing = 0f, MaxSlope = 30f };
            var center = new Vector3(10f, 0f, 10f);

            SurfaceSampler steep = (Vector2 xz, out Vector3 p, out Vector3 n) =>
            {
                p = new Vector3(xz.x, 0f, xz.y);
                n = Quaternion.Euler(45f, 0f, 0f) * Vector3.up; // 45도 비탈
                return true;
            };
            Assert.AreEqual(0, ScatterBrush.Generate(center, settings, GrassOnly, service, steep, new System.Random(1)).Count);

            settings.MaxSlope = 60f;
            Assert.Greater(ScatterBrush.Generate(center, settings, GrassOnly, service, steep, new System.Random(1)).Count, 0);

            settings.UseHeightFilter = true;
            settings.MinHeight = 1f;
            settings.MaxHeight = 5f;
            Assert.AreEqual(0, ScatterBrush.Generate(center, settings, GrassOnly, service, Flat, new System.Random(1)).Count, "높이 0은 범위 밖");
        }

        [Test]
        public void Generate_NoSurface_OrNoValidEntries_ReturnsEmpty()
        {
            var settings = new ScatterBrushSettings();
            var center = new Vector3(10f, 0f, 10f);
            SurfaceSampler none = (Vector2 xz, out Vector3 p, out Vector3 n) => { p = default; n = default; return false; };

            Assert.AreEqual(0, ScatterBrush.Generate(center, settings, GrassOnly, service, none, new System.Random(1)).Count);
            Assert.AreEqual(0, ScatterBrush.Generate(center, settings, new[] { new ScatterEntry("crate", 1f) }, service, Flat, new System.Random(1)).Count,
                "칸 오브젝트는 브러시에 담겨도 무시");
        }

        [Test]
        public void Generate_StaysInsideMap_AtEdge()
        {
            var settings = new ScatterBrushSettings { Radius = 3f, Density = 2f, MinSpacing = 0f };

            var made = ScatterBrush.Generate(new Vector3(0f, 0f, 0f), settings, GrassOnly, service, Flat, new System.Random(3));

            Assert.Greater(made.Count, 0);
            Assert.IsTrue(made.All(s => s.Position.x >= 0f && s.Position.z >= 0f));
            Assert.IsTrue(service.Execute(new AddScattersCommand(made)).IsValid);
            Assert.AreEqual(made.Count, map.ScatterCount);
        }

        [Test]
        public void Generate_WeightsAndRandomRanges()
        {
            var settings = new ScatterBrushSettings
            {
                Radius = 8f, Density = 3f, MinSpacing = 0f, ScaleMin = 0.5f, ScaleMax = 1.5f, RandomYaw = true,
            };
            var entries = new[] { new ScatterEntry("grass", 3f), new ScatterEntry("flower", 1f) };

            var made = ScatterBrush.Generate(new Vector3(10f, 0f, 10f), settings, entries, service, Flat, new System.Random(11));

            float grassRatio = made.Count(s => s.DefinitionId == "grass") / (float)made.Count;
            Assert.That(grassRatio, Is.InRange(0.65f, 0.85f));
            Assert.IsTrue(made.All(s => s.Scale >= 0.5f && s.Scale <= 1.5f));
            Assert.Greater(made.Select(s => Mathf.Round(s.Rotation.eulerAngles.y)).Distinct().Count(), 10, "회전이 제각각");
        }

        [Test]
        public void Generate_AlignToNormal_TiltsUp()
        {
            var settings = new ScatterBrushSettings { Radius = 1f, Density = 3f, MinSpacing = 0f, MaxSlope = 90f, AlignToNormal = 1f, RandomYaw = false };
            var slopeNormal = Quaternion.Euler(20f, 0f, 0f) * Vector3.up;
            SurfaceSampler tilted = (Vector2 xz, out Vector3 p, out Vector3 n) => { p = new Vector3(xz.x, 0f, xz.y); n = slopeNormal; return true; };

            var made = ScatterBrush.Generate(new Vector3(10f, 0f, 10f), settings, GrassOnly, service, tilted, new System.Random(2));

            Assert.Greater(made.Count, 0);
            Assert.Less(Vector3.Angle(made[0].Rotation * Vector3.up, slopeNormal), 0.1f);
        }

        [Test]
        public void CollectErase_FiltersByDefinition()
        {
            Add(At(10f, 10f), At(10.5f, 10f, "flower"), At(15f, 15f));

            var all = ScatterBrush.CollectErase(new Vector3(10f, 0f, 10f), 1f, service);
            var onlyFlower = ScatterBrush.CollectErase(new Vector3(10f, 0f, 10f), 1f, service, new HashSet<string> { "flower" });

            CollectionAssert.AreEquivalent(new[] { 1, 2 }, all);
            CollectionAssert.AreEqual(new[] { 2 }, onlyFlower);
        }

        [Test]
        public void Settings_Sanitize_SwapsAndClamps()
        {
            var s = new ScatterBrushSettings { Radius = -1f, ScaleMin = 2f, ScaleMax = 1f, MinHeight = 5f, MaxHeight = 1f, AlignToNormal = 3f };
            s.Sanitize();

            Assert.AreEqual(0.1f, s.Radius);
            Assert.AreEqual(1f, s.ScaleMin);
            Assert.AreEqual(2f, s.ScaleMax);
            Assert.AreEqual(1f, s.MinHeight);
            Assert.AreEqual(1f, s.AlignToNormal);
        }
    }
}
