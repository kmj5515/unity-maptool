using System.Collections.Generic;
using System.Linq;
using MapTool.Core;
using NUnit.Framework;
using UnityEngine;

namespace MapTool.Editor.Tests
{
    public sealed class EditorMapValidatorTests
    {
        private readonly List<Object> created = new();
        private MapData map;

        private T Track<T>(T obj) where T : Object
        {
            created.Add(obj);
            return obj;
        }

        private MapObjectDefinition Definition(string id, PlacementKind kind, GameObject prefab)
        {
            var definition = Track(ScriptableObject.CreateInstance<MapObjectDefinition>());
            definition.name = id;
            definition.Configure(id, Vector3Int.one, prefab, "Test", kind);
            return definition;
        }

        private EditorObjectCatalog catalog;

        [SetUp]
        public void SetUp()
        {
            var prefab = Track(new GameObject("prefab"));
            catalog = new EditorObjectCatalog(new[]
            {
                Definition("crate", PlacementKind.Grid, prefab),
                Definition("bare", PlacementKind.Grid, null),
                Definition("grass", PlacementKind.Scatter, prefab),
                Definition("bareGrass", PlacementKind.Scatter, null),
            });
            map = new MapData(new Vector3Int(10, 2, 10), GridSpace.Unit);
            // 스폰이 없으면 경고가 나오므로 비어 있는 위층 구석에 하나 둔다.
            map.AddSpawn(new PlacedSpawn(1, "시작", new Vector3Int(9, 1, 9), GridRotation.R0));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in created)
                if (obj != null) Object.DestroyImmediate(obj);
            created.Clear();
        }

        private static MapToolWindow.ProfileState AllOk(string id) => MapToolWindow.ProfileState.Ok;

        private void Place(string def, int x, int z)
        {
            var service = new PlacementService(map, catalog);
            Assert.IsTrue(service.Execute(new PlaceCommand(def, new Vector3Int(x, 0, z), GridRotation.R0)).IsValid);
        }

        private void Scatter(params (string def, Vector3 position)[] items)
        {
            var service = new PlacementService(map, catalog);
            var list = items.Select(i => new PlacedScatter(0, i.def, i.position, Quaternion.identity, 1f)).ToList();
            Assert.IsTrue(service.Execute(new AddScattersCommand(list)).IsValid);
        }

        [Test]
        public void 프리팹있으면_경고없음()
        {
            Place("crate", 0, 0);
            Scatter(("grass", new Vector3(5f, 0f, 5f)));

            CollectionAssert.IsEmpty(EditorMapValidator.Validate(map, catalog, AllOk));
        }

        [Test]
        public void 프리팹없음_정의별로묶은경고()
        {
            Place("bare", 0, 0);
            Place("bare", 3, 3);
            Scatter(("bareGrass", new Vector3(5f, 0f, 5f)), ("bareGrass", new Vector3(6f, 0f, 6f)), ("grass", new Vector3(7f, 0f, 7f)));

            var issues = EditorMapValidator.Validate(map, catalog, AllOk);

            Assert.AreEqual(2, issues.Count, string.Join("\n", issues));
            Assert.IsTrue(issues.All(i => i.Severity == IssueSeverity.Warning && i.Code == EditorValidationCodes.MissingPrefab));
            Assert.AreEqual(IssueTarget.Object, issues[0].Target);
            Assert.AreEqual(2, issues[0].TargetIds.Count);
            Assert.IsTrue(issues[0].Area.Contains(new Vector3(0.5f, 0.5f, 0.5f)) && issues[0].Area.Contains(new Vector3(3.5f, 0.5f, 3.5f)));
            Assert.AreEqual(IssueTarget.Scatter, issues[1].Target);
            Assert.AreEqual(2, issues[1].TargetIds.Count);
            StringAssert.Contains("2개", issues[1].Message);
        }

        [Test]
        public void 존프로필_비어있음과없음_경고()
        {
            var service = new PlacementService(map, catalog);
            var box = new ZoneBox(Vector3Int.zero, new Vector3Int(2, 1, 2));
            Assert.IsTrue(service.Execute(new AddZoneCommand(new PlacedZone(0, "빈", "", 0, new[] { box }))).IsValid);
            Assert.IsTrue(service.Execute(new AddZoneCommand(new PlacedZone(0, "없음", "gone", 0, new[] { box }))).IsValid);
            Assert.IsTrue(service.Execute(new AddZoneCommand(new PlacedZone(0, "정상", "forest", 0, new[] { box }))).IsValid);

            var issues = EditorMapValidator.Validate(map, catalog, id =>
                string.IsNullOrEmpty(id) ? MapToolWindow.ProfileState.Empty
                : id == "gone" ? MapToolWindow.ProfileState.Missing
                : MapToolWindow.ProfileState.Ok);

            Assert.AreEqual(2, issues.Count, string.Join("\n", issues));
            Assert.AreEqual((EditorValidationCodes.ProfileEmpty, IssueTarget.Zone), (issues[0].Code, issues[0].Target));
            Assert.AreEqual((EditorValidationCodes.ProfileMissing, IssueTarget.Zone), (issues[1].Code, issues[1].Target));
            StringAssert.Contains("gone", issues[1].Message);
        }

        [Test]
        public void 코어오류가_경고보다앞()
        {
            Place("bare", 0, 0);
            Scatter(("grass", new Vector3(5f, 0f, 5f)));
            // 에셋을 직접 고쳐 장식을 맵 밖으로 옮긴 상황 → 코어 오류
            string json = JsonUtility.ToJson(map);
            Assert.IsTrue(json.Contains("\"x\":5.0"), json);
            var broken = new MapData(Vector3Int.one, GridSpace.Unit);
            JsonUtility.FromJsonOverwrite(json.Replace("\"x\":5.0", "\"x\":-3.0"), broken);

            var issues = EditorMapValidator.Validate(broken, catalog, AllOk);

            Assert.AreEqual(2, issues.Count, string.Join("\n", issues));
            Assert.AreEqual(IssueSeverity.Error, issues[0].Severity);
            Assert.AreEqual(IssueSeverity.Warning, issues[1].Severity);
        }
    }
}
