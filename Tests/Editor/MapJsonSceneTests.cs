using System.Collections.Generic;
using MapTool.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MapTool.Editor.Tests
{
    /// <summary>JSON 덮어쓰기(ReplaceMapCommand) 뒤 씬이 데이터와 맞는지, 가져오기 요약이 맞는지.</summary>
    public sealed class MapJsonSceneTests
    {
        private readonly List<Object> created = new();
        private EditorObjectCatalog catalog;
        private PlacementService service;
        private Transform root;
        private MapSceneView view;
        private SpawnSceneSync spawns;

        private T Track<T>(T obj) where T : Object
        {
            created.Add(obj);
            return obj;
        }

        [SetUp]
        public void SetUp()
        {
            var crate = Track(ScriptableObject.CreateInstance<MapObjectDefinition>());
            crate.name = "crate";
            crate.Configure("crate", Vector3Int.one, Track(new GameObject("CratePrefab")), "Test");
            catalog = new EditorObjectCatalog(new[] { crate });

            service = new PlacementService(new MapData(new Vector3Int(10, 2, 10), GridSpace.Unit), catalog);
            root = Track(new GameObject("Root")).transform;
            view = new MapSceneView(service, catalog, root);
            spawns = new SpawnSceneSync(service, root);
        }

        [TearDown]
        public void TearDown()
        {
            view.Dispose();
            spawns.Dispose();
            for (int i = created.Count - 1; i >= 0; i--)
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            created.Clear();
        }

        private static MapData Incoming()
        {
            // 칸 크기가 다른 맵: 다시 놓인 오브젝트는 새 그리드 위치에 있어야 한다.
            var map = new MapData(new Vector3Int(6, 1, 6), new GridSpace(Vector3.zero, new Vector3(2f, 1f, 2f)));
            map.Add(new PlacedObject(4, "crate", new Vector3Int(1, 0, 1), GridRotation.R0));
            map.Add(new PlacedObject(5, "missing_def", new Vector3Int(3, 0, 3), GridRotation.R0));
            map.AddScatter(new PlacedScatter(2, "missing_grass", new Vector3(1f, 0f, 1f), Quaternion.identity, 1f));
            map.AddSpawn(new PlacedSpawn(3, "새 입구", new Vector3Int(2, 0, 2), GridRotation.R0));
            return map;
        }

        [Test]
        public void Replace_ResyncsScene_AndUndoBringsOldBack()
        {
            var place = new PlaceCommand("crate", new Vector3Int(7, 0, 7));
            Assert.IsTrue(service.Execute(place).IsValid);
            Assert.IsTrue(service.Execute(new AddSpawnCommand(new PlacedSpawn(0, "옛 입구", new Vector3Int(0, 0, 0), GridRotation.R0))).IsValid);
            int oldSpawnId = service.Map.Spawns[0].Id;

            // 없는 정의는 경고를 남기고 대체 상자로 그린다.
            LogAssert.ignoreFailingMessages = true;
            Assert.IsTrue(service.Execute(new ReplaceMapCommand(Incoming())).IsValid);
            LogAssert.ignoreFailingMessages = false;

            Assert.IsFalse(view.TryGetInstance(place.Placed.InstanceId, out _), "옛 오브젝트(#1)는 사라진다");
            Assert.IsTrue(view.TryGetInstance(4, out var crate));
            Assert.That(Vector3.Distance(new Vector3(3f, 0f, 3f), crate.transform.position), Is.LessThan(1e-4f), "새 칸 크기(2)로 놓인다");
            Assert.IsTrue(view.TryGetInstance(5, out _), "없는 정의도 자리를 차지한다");
            Assert.IsTrue(spawns.TryGetSpawn(3, out var spawn));
            Assert.IsTrue(spawn.IsDefault);
            Assert.IsFalse(spawns.TryGetSpawn(oldSpawnId, out _), "옛 스폰(@1)은 사라진다");
            Assert.IsFalse(view.Sync(), "이벤트만으로 씬이 이미 맞아야 한다");

            Assert.IsTrue(service.Undo());
            Assert.AreEqual(new Vector3Int(10, 2, 10), service.Map.Size);
            Assert.IsTrue(view.TryGetInstance(place.Placed.InstanceId, out var back));
            Assert.That(Vector3.Distance(new Vector3(7.5f, 0f, 7.5f), back.transform.position), Is.LessThan(1e-4f));
            Assert.IsTrue(spawns.TryGetSpawn(oldSpawnId, out _));
            Assert.AreEqual(1, spawns.Count);
            Assert.IsFalse(view.Sync());
        }

        [Test]
        public void Summary_CountsUnknownDefinitions()
        {
            var map = Incoming();
            Assert.AreEqual(2, MapJsonSummary.CountUnknownDefinitions(map, catalog), "missing_def 오브젝트 + missing_grass 장식");
            StringAssert.Contains("오브젝트 2", MapJsonSummary.Counts(map));
            StringAssert.Contains("크기 6×1×6", MapJsonSummary.Counts(map));
        }
    }
}
