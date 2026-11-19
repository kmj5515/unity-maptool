using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MapTool.Core.Tests
{
    public sealed class MapJsonTests
    {
        private static MapData Sample()
        {
            var map = new MapData(new Vector3Int(12, 3, 8), new GridSpace(new Vector3(5f, -1f, 2.5f), new Vector3(2f, 1.5f, 2f)));
            map.Add(new PlacedObject(3, "house", new Vector3Int(1, 0, 2), GridRotation.R90));
            map.Add(new PlacedObject(7, "crate", new Vector3Int(4, 1, 0), GridRotation.R270));
            map.AddZone(new PlacedZone(2, "광장", "rain", 5, new[]
            {
                new ZoneBox(new Vector3Int(0, 0, 0), new Vector3Int(3, 1, 2)),
                new ZoneBox(new Vector3Int(3, 0, 0), new Vector3Int(1, 2, 1)),
            }));
            map.AddScatter(new PlacedScatter(4, "grass", new Vector3(1.25f, 0f, 3.1f), Quaternion.Euler(0f, 37f, 0f), 0.8f));
            map.AddScatter(new PlacedScatter(9, "rock", new Vector3(-0.3f, 0.2f, 7.77f), Quaternion.Euler(10f, 200f, -5f), 1.3f));
            map.AddSpawn(new PlacedSpawn(1, "입구", new Vector3Int(0, 0, 0), GridRotation.R0));
            map.AddSpawn(new PlacedSpawn(6, "", new Vector3Int(5, 0, 5), GridRotation.R180));
            map.SetDefaultSpawnId(6);
            return map;
        }

        private static string Valid() => MapJson.ToJson(Sample());

        private static void AssertRejected(string json, string messagePart)
        {
            var e = Assert.Throws<MapJsonException>(() => MapJson.FromJson(json));
            StringAssert.Contains(messagePart, e.Message);
        }

        [Test]
        public void RoundTrip_KeepsEverything()
        {
            var original = Sample();
            var json = MapJson.ToJson(original);
            var loaded = MapJson.FromJson(json);

            Assert.AreEqual(original.Size, loaded.Size);
            Assert.AreEqual(original.Grid.Origin, loaded.Grid.Origin);
            Assert.AreEqual(original.Grid.CellSize, loaded.Grid.CellSize);
            CollectionAssert.AreEqual(original.Objects, loaded.Objects);
            CollectionAssert.AreEqual(original.Zones, loaded.Zones);
            CollectionAssert.AreEqual(original.Spawns, loaded.Spawns);
            Assert.AreEqual(6, loaded.DefaultSpawnId);

            Assert.AreEqual(original.Scatters.Count, loaded.Scatters.Count);
            for (int i = 0; i < original.Scatters.Count; i++)
            {
                var a = original.Scatters[i];
                var b = loaded.Scatters[i];
                Assert.AreEqual(a.Id, b.Id);
                Assert.AreEqual(a.DefinitionId, b.DefinitionId);
                Assert.That(Vector3.Distance(a.Position, b.Position), Is.LessThan(1e-5f));
                Assert.That(Quaternion.Angle(a.Rotation, b.Rotation), Is.LessThan(0.01f));
                Assert.That(b.Scale, Is.EqualTo(a.Scale).Within(1e-6f));
            }

            Assert.AreEqual(json, MapJson.ToJson(loaded), "다시 내보내면 같은 글자여야 diff가 생기지 않는다");
        }

        [Test]
        public void Json_IsReadable()
        {
            var json = Valid();
            StringAssert.Contains("\"format\": \"MapTool.Map\"", json);
            StringAssert.Contains("\"version\": 1", json);
            StringAssert.Contains("\"size\": [12, 3, 8]", json, "숫자 배열은 한 줄");
            StringAssert.Contains("\"cell\": [1, 0, 2]", json);
            StringAssert.Contains("\"rotation\": 90", json, "회전은 각도");
            StringAssert.Contains("\"rotation\": 270", json);
            StringAssert.Contains("\"definition\": \"house\"", json);
            StringAssert.Contains("광장", json);
        }

        [Test]
        public void Import_NewIdsContinueAfterLoadedOnes()
        {
            var loaded = MapJson.FromJson(Valid());
            Assert.AreEqual(8, loaded.AllocateId());
            Assert.AreEqual(3, loaded.AllocateZoneId());
            Assert.AreEqual(10, loaded.AllocateScatterId());
            Assert.AreEqual(7, loaded.AllocateSpawnId());
        }

        [Test]
        public void Import_KeepsContentProblems_ForValidatorToReport()
        {
            // 없는 정의·범위 밖 칸은 파일 형식 문제가 아니므로 읽어 들이고, 맵 검사가 알린다.
            var json = Valid().Replace("\"house\"", "\"no_such_thing\"").Replace("[4, 1, 0]", "[400, 1, 0]");
            var loaded = MapJson.FromJson(json);
            Assert.IsTrue(loaded.Objects.Any(o => o.DefinitionId == "no_such_thing"));
            Assert.IsTrue(loaded.Objects.Any(o => o.Cell.x == 400));
        }

        [Test]
        public void Import_DefaultSpawnPointingNowhere_IsDropped()
        {
            var loaded = MapJson.FromJson(Valid().Replace("\"defaultSpawn\": 6", "\"defaultSpawn\": 42"));
            Assert.AreEqual(0, loaded.DefaultSpawnId);
            Assert.IsTrue(loaded.TryGetDefaultSpawn(out var spawn));
            Assert.AreEqual(1, spawn.Id, "지정이 없으면 첫 스폰");
        }

        [Test]
        public void Import_EmptyMap_Works()
        {
            var empty = new MapData(new Vector3Int(1, 1, 1), GridSpace.Unit);
            var loaded = MapJson.FromJson(MapJson.ToJson(empty, pretty: false));
            Assert.AreEqual(Vector3Int.one, loaded.Size);
            Assert.AreEqual(0, loaded.Count + loaded.Zones.Count + loaded.ScatterCount + loaded.Spawns.Count);
        }

        [Test]
        public void Import_RotationAcceptsAnyMultipleOf90()
        {
            var loaded = MapJson.FromJson(Valid().Replace("\"rotation\": 90", "\"rotation\": -270"));
            Assert.AreEqual(GridRotation.R90, loaded.Objects.First(o => o.InstanceId == 3).Rotation);
        }

        [Test]
        public void ReplaceMap_SwapsEverything_UndoRestores_RedoReapplies()
        {
            var catalog = new ObjectCatalog(new[]
            {
                new ObjectDefinition("crate", new Vector3Int(1, 1, 1)),
                new ObjectDefinition("table", new Vector3Int(2, 1, 1)),
            });
            var map = new MapData(new Vector3Int(10, 1, 10), GridSpace.Unit);
            var service = new PlacementService(map, catalog);
            Assert.IsTrue(service.Execute(new PlaceCommand("table", new Vector3Int(0, 0, 0))).IsValid);
            Assert.IsTrue(service.Execute(new AddSpawnCommand(new PlacedSpawn(0, "옛 입구", new Vector3Int(5, 0, 5), GridRotation.R0))).IsValid);
            string before = MapJson.ToJson(map);

            int removed = 0, placed = 0;
            service.OnRemoved += _ => removed++;
            service.OnPlaced += _ => placed++;

            var incoming = Sample();
            Assert.IsTrue(service.Execute(new ReplaceMapCommand(incoming)).IsValid);
            Assert.AreSame(map, service.Map, "같은 MapData를 고쳐야 에셋·뷰가 그대로 따라온다");
            Assert.AreEqual(MapJson.ToJson(incoming), MapJson.ToJson(map));
            Assert.AreEqual(1, removed);
            Assert.AreEqual(2, placed);
            Assert.AreEqual(1, service.Occupancy.OccupiedCellCount, "crate만 카탈로그에 있다(house는 없는 정의)");

            Assert.IsTrue(service.Undo(), "덮어쓰기는 Undo 한 번");
            Assert.AreEqual(before, MapJson.ToJson(map));
            Assert.AreEqual(2, service.Occupancy.OccupiedCellCount);
            Assert.IsTrue(service.CanUndo, "앞선 편집 기록은 남아 있다");

            Assert.IsTrue(service.Redo());
            Assert.AreEqual(MapJson.ToJson(incoming), MapJson.ToJson(map));
        }

        [Test]
        public void ReplaceMap_CopiesSource_SoLaterChangesDontLeak()
        {
            var service = new PlacementService(new MapData(new Vector3Int(4, 1, 4), GridSpace.Unit), new ObjectCatalog());
            var incoming = Sample();
            var command = new ReplaceMapCommand(incoming);
            incoming.Add(new PlacedObject(50, "late", Vector3Int.zero, GridRotation.R0));

            Assert.IsTrue(service.Execute(command).IsValid);
            Assert.IsFalse(service.Map.TryGet(50, out _));
            Assert.AreEqual(new Vector3Int(12, 3, 8), service.Map.Size);
        }

        [Test]
        public void Import_RejectsBrokenFiles()
        {
            var json = Valid();
            AssertRejected("", "비어");
            AssertRejected("{ \"format\": ", "문법");
            AssertRejected("{ \"hello\": 1 }", "맵툴 맵 JSON이 아닙니다");
            AssertRejected(json.Replace("\"version\": 1", "\"version\": 99"), "더 새로운");
            AssertRejected(json.Replace("\"size\": [12, 3, 8]", "\"size\": [12, 0, 8]"), "맵 크기");
            AssertRejected(json.Replace("\"size\": [12, 3, 8]", "\"size\": [12, 3]"), "size");
            AssertRejected(json.Replace("\"cellSize\": [2.0, 1.5, 2.0]", "\"cellSize\": [2.0, 0.0, 2.0]"), "칸 크기");
            AssertRejected(json.Replace("\"rotation\": 90", "\"rotation\": 45"), "90도 단위");
            AssertRejected(json.Replace("\"id\": 7", "\"id\": 3"), "오브젝트 Id 3가 두 번");
            AssertRejected(json.Replace("\"id\": 7", "\"id\": 0"), "1 이상");
            AssertRejected(json.Replace("\"cell\": [1, 0, 2]", "\"cell\": [1, 0]"), "cell");
        }
    }
}
