using System.Collections.Generic;
using System.Linq;
using MapTool.Core;
using NUnit.Framework;
using UnityEngine;

namespace MapTool.Editor.Tests
{
    public sealed class MapBuildCheckTests
    {
        private readonly List<Object> created = new();
        private EditorObjectCatalog catalog;

        [SetUp]
        public void SetUp()
        {
            var prefab = new GameObject("prefab");
            created.Add(prefab);
            var definition = ScriptableObject.CreateInstance<MapObjectDefinition>();
            created.Add(definition);
            definition.name = "crate";
            definition.Configure("crate", Vector3Int.one, prefab, "Test", PlacementKind.Grid);
            catalog = new EditorObjectCatalog(new[] { definition });
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in created)
                if (obj != null) Object.DestroyImmediate(obj);
            created.Clear();
        }

        private static MapToolWindow.ProfileState AllOk(string id) => MapToolWindow.ProfileState.Ok;

        private static MapData CleanMap()
        {
            var map = new MapData(new Vector3Int(10, 2, 10), GridSpace.Unit);
            map.AddSpawn(new PlacedSpawn(1, "시작", new Vector3Int(9, 1, 9), GridRotation.R0));
            return map;
        }

        [Test]
        public void CleanMap_NoErrors()
        {
            var results = MapBuildCheck.Check(new[] { ("Assets/Clean.asset", CleanMap()) }, catalog, AllOk);

            Assert.That(results.Single().ErrorCount, Is.Zero);
        }

        [Test]
        public void DeletedDefinition_IsErrorForThatMapOnly()
        {
            var broken = CleanMap();
            broken.Add(new PlacedObject(1, "deleted-crate", Vector3Int.zero, GridRotation.R0));

            var results = MapBuildCheck.Check(new[]
            {
                ("Assets/Clean.asset", CleanMap()),
                ("Assets/Broken.asset", broken),
            }, catalog, AllOk);

            Assert.That(results.Select(r => r.ErrorCount), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(results[1].Issues[0].Code, Is.EqualTo(ValidationCodes.UnknownDefinition));
        }

        [Test]
        public void MissingSpawn_IsWarningAndDoesNotBlock()
        {
            // 연습용 맵처럼 스폰이 없는 맵은 경고만 낸다.
            var map = new MapData(new Vector3Int(10, 2, 10), GridSpace.Unit);
            var result = MapBuildCheck.Check(new[] { ("Assets/Practice.asset", map) }, catalog, AllOk).Single();

            Assert.That(result.ErrorCount, Is.Zero);
            Assert.That(result.Issues.Single().Code, Is.EqualTo(ValidationCodes.NoSpawn));
        }

        [Test]
        public void Format_ListsOnlyMapsWithIssues_AndSkipCount()
        {
            var broken = CleanMap();
            broken.Add(new PlacedObject(1, "deleted-crate", Vector3Int.zero, GridRotation.R0));
            var results = MapBuildCheck.Check(new[]
            {
                ("Assets/Clean.asset", CleanMap()),
                ("Assets/Broken.asset", broken),
            }, catalog, AllOk);

            string text = MapBuildCheck.Format(results, new[] { "Assets/WIP.asset" });

            StringAssert.Contains("맵 2개 검사: 오류 1, 경고 0", text);
            StringAssert.Contains("제외 1개", text);
            StringAssert.Contains("Assets/Broken.asset", text);
            StringAssert.DoesNotContain("Assets/Clean.asset", text);
        }
    }
}
