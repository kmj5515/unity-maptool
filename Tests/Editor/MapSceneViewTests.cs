using System.Collections.Generic;
using System.Text.RegularExpressions;
using MapTool.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MapTool.Editor.Tests
{
    public sealed class MapSceneViewTests
    {
        // 원점 (100, 0, 0), 셀 크기 (2, 1, 2): 월드 위치 계산이 셀 크기와 원점을 반영하는지 함께 본다.
        private static readonly GridSpace Grid = new(new Vector3(100f, 0f, 0f), new Vector3(2f, 1f, 2f));

        private readonly List<Object> created = new();
        private EditorObjectCatalog catalog;
        private PlacementService service;
        private Transform root;
        private MapSceneView view;

        private T Track<T>(T obj) where T : Object
        {
            created.Add(obj);
            return obj;
        }

        private MapObjectDefinition Definition(string id, Vector3Int size, GameObject prefab)
        {
            var definition = Track(ScriptableObject.CreateInstance<MapObjectDefinition>());
            definition.name = id;
            definition.Configure(id, size, prefab, "Test");
            return definition;
        }

        [SetUp]
        public void SetUp()
        {
            catalog = new EditorObjectCatalog(new[]
            {
                Definition("crate", new Vector3Int(1, 1, 1), Track(new GameObject("CratePrefab"))),
                Definition("table", new Vector3Int(2, 1, 1), Track(new GameObject("TablePrefab"))),
                Definition("ghost", new Vector3Int(1, 2, 1), null), // 프리팹 없음 → 대체 상자
            });
            service = new PlacementService(new MapData(new Vector3Int(10, 2, 10), Grid), catalog);
            root = Track(new GameObject("Root")).transform;
            view = new MapSceneView(service, catalog, root);
        }

        [TearDown]
        public void TearDown()
        {
            view.Dispose();
            for (int i = created.Count - 1; i >= 0; i--)
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            created.Clear();
        }

        private PlacedObject Place(string id, int x, int z, GridRotation r = GridRotation.R0)
        {
            var cmd = new PlaceCommand(id, new Vector3Int(x, 0, z), r);
            Assert.IsTrue(service.Execute(cmd).IsValid);
            return cmd.Placed;
        }

        private GameObject InstanceOf(PlacedObject placed)
        {
            Assert.IsTrue(view.TryGetInstance(placed.InstanceId, out var go), $"{placed} 인스턴스가 없음");
            return go;
        }

        private static void AssertNear(Vector3 expected, Vector3 actual) =>
            Assert.Less((expected - actual).magnitude, 1e-4f, $"expected {expected}, actual {actual}");

        [Test]
        public void Place_CreatesInstanceAtFootprintBaseCenter()
        {
            var table = Place("table", 1, 2);

            var go = InstanceOf(table);
            Assert.AreEqual(root, go.transform.parent);
            Assert.AreEqual($"table#{table.InstanceId}", go.name);
            // 영역 x 1..3, z 2..3 → 바닥 중심 셀 (2, 0, 2.5) → 월드 (100 + 4, 0, 5)
            AssertNear(new Vector3(104f, 0f, 5f), go.transform.position);
            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.identity, go.transform.rotation), 0.01f);
        }

        [Test]
        public void Rotate_MovesToNewFootprintCenterAndTurns90()
        {
            var table = Place("table", 1, 2);

            service.Execute(new MoveCommand(table.InstanceId, table.Cell, GridRotation.R90));

            var go = InstanceOf(table);
            // R90 영역 x 1..2, z 2..4 → 중심 셀 (1.5, 0, 3) → 월드 (103, 0, 6)
            AssertNear(new Vector3(103f, 0f, 6f), go.transform.position);
            Assert.AreEqual(90f, go.transform.rotation.eulerAngles.y, 0.01f);
        }

        [Test]
        public void Remove_DestroysInstance_UndoRecreates()
        {
            var crate = Place("crate", 0, 0);
            service.Execute(new RemoveCommand(crate.InstanceId));

            Assert.AreEqual(0, root.childCount);
            Assert.IsFalse(view.TryGetInstance(crate.InstanceId, out _));

            service.Undo();

            Assert.AreEqual(1, root.childCount);
            Assert.AreEqual($"crate#{crate.InstanceId}", InstanceOf(crate).name);
        }

        [Test]
        public void UndoMove_RestoresTransform()
        {
            var crate = Place("crate", 0, 0);
            var before = InstanceOf(crate).transform.position;
            service.Execute(new MoveCommand(crate.InstanceId, new Vector3Int(4, 0, 4), GridRotation.R180));

            service.Undo();

            AssertNear(before, InstanceOf(crate).transform.position);
            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.identity, InstanceOf(crate).transform.rotation), 0.01f);
        }

        [Test]
        public void MissingPrefab_CreatesPlaceholderSizedToFootprint()
        {
            var ghost = Place("ghost", 3, 3);

            var go = InstanceOf(ghost);
            var box = go.transform.GetChild(0);
            // 크기 (1, 2, 1) 셀 × 셀 크기 (2, 1, 2) = (2, 2, 2) 월드, 살짝 줄여서 그린다
            AssertNear(new Vector3(2f, 2f, 2f) * 0.95f, box.localScale);
            AssertNear(new Vector3(0f, 1f, 0f), box.localPosition);
        }

        [Test]
        public void Sync_RepairsDriftedScene_AndIsIdempotent()
        {
            var a = Place("crate", 0, 0);
            var b = Place("table", 4, 4);
            var expectedA = InstanceOf(a).transform.position;

            InstanceOf(a).transform.position += Vector3.up * 3f;   // 누가 씬에서 직접 옮김
            Object.DestroyImmediate(InstanceOf(b));                // 누가 지움
            new GameObject("junk").transform.SetParent(root);     // 데이터에 없는 오브젝트
            new GameObject("crate#99").transform.SetParent(root); // 데이터에 없는 ID

            Assert.IsTrue(view.Sync());

            Assert.AreEqual(2, root.childCount);
            AssertNear(expectedA, InstanceOf(a).transform.position);
            Assert.AreEqual($"table#{b.InstanceId}", InstanceOf(b).name);
            Assert.IsFalse(view.Sync(), "맞춰진 뒤에는 아무것도 바꾸지 않는다");
        }

        [Test]
        public void Sync_ReusesMatchingChildren()
        {
            var crate = Place("crate", 2, 2);
            var original = InstanceOf(crate);

            view.Dispose();
            view = new MapSceneView(service, catalog, root); // 도메인 리로드 뒤 다시 붙는 상황

            Assert.IsFalse(view.Sync());
            Assert.AreSame(original, InstanceOf(crate));
        }

        [Test]
        public void Catalog_DuplicateOrEmptyId_IsSkippedWithWarning()
        {
            LogAssert.Expect(LogType.Warning, new Regex("중복"));
            LogAssert.Expect(LogType.Warning, new Regex("비어"));

            var duplicate = new EditorObjectCatalog(new[]
            {
                Definition("crate", Vector3Int.one, null),
                Definition("crate", new Vector3Int(2, 1, 1), null),
                Definition("", Vector3Int.one, null),
            });

            Assert.AreEqual(1, duplicate.Definitions.Count);
            Assert.IsTrue(duplicate.TryGet("crate", out var definition));
            Assert.AreEqual(Vector3Int.one, definition.Size);
        }
    }
}
