using System.Collections.Generic;
using MapTool.Core;
using NUnit.Framework;
using UnityEngine;

namespace MapTool.Editor.Tests
{
    /// <summary>브러시 4단계: 장식 데이터 → 씬 오브젝트(Scatters/정의ID/정의ID~ID) 동기화.</summary>
    public sealed class ScatterSceneViewTests
    {
        private static readonly GridSpace Grid = new(new Vector3(100f, 0f, 0f), Vector3.one);

        private readonly List<Object> created = new();
        private EditorObjectCatalog catalog;
        private PlacementService service;
        private Transform root;
        private ScatterSceneView view;

        private T Track<T>(T obj) where T : Object
        {
            created.Add(obj);
            return obj;
        }

        private MapObjectDefinition Definition(string id, GameObject prefab)
        {
            var definition = Track(ScriptableObject.CreateInstance<MapObjectDefinition>());
            definition.name = id;
            definition.Configure(id, Vector3Int.one, prefab, "Test", PlacementKind.Scatter);
            return definition;
        }

        [SetUp]
        public void SetUp()
        {
            var grassPrefab = Track(new GameObject("GrassPrefab"));
            grassPrefab.transform.localScale = new Vector3(2f, 2f, 2f);
            catalog = new EditorObjectCatalog(new[]
            {
                Definition("grass", grassPrefab),
                Definition("rock", Track(new GameObject("RockPrefab"))),
                Definition("ghost", null), // 프리팹 없음 → 대체 상자
            });
            service = new PlacementService(new MapData(new Vector3Int(20, 2, 20), Grid), catalog);
            root = Track(new GameObject("Root")).transform;
            view = new ScatterSceneView(service, catalog, root);
        }

        [TearDown]
        public void TearDown()
        {
            view.Dispose();
            for (int i = created.Count - 1; i >= 0; i--)
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            created.Clear();
        }

        private static PlacedScatter Candidate(string id, float x, float z, float yaw = 0f, float scale = 1f) =>
            new(0, id, new Vector3(x, 0.5f, z), Quaternion.Euler(0f, yaw, 0f), scale);

        private IReadOnlyList<PlacedScatter> Add(params PlacedScatter[] candidates)
        {
            var cmd = new AddScattersCommand(candidates);
            Assert.IsTrue(service.Execute(cmd).IsValid);
            return cmd.Added;
        }

        private GameObject InstanceOf(PlacedScatter scatter)
        {
            Assert.IsTrue(view.TryGetInstance(scatter.Id, out var go), $"{scatter} 인스턴스가 없음");
            return go;
        }

        private Transform Container => root.Find(MapSceneView.ScattersChildName);

        [Test]
        public void Add_CreatesInstanceUnderDefinitionGroup_WithPose()
        {
            var added = Add(Candidate("grass", 3f, 4f, 90f, 1.5f));
            var go = InstanceOf(added[0]);

            Assert.AreEqual("grass~" + added[0].Id, go.name);
            Assert.AreEqual("grass", go.transform.parent.name);
            Assert.AreEqual(Container, go.transform.parent.parent);
            Assert.Less((go.transform.position - new Vector3(103f, 0.5f, 4f)).magnitude, 1e-4f, "맵 원점 + 로컬 위치");
            Assert.Less(Quaternion.Angle(go.transform.rotation, Quaternion.Euler(0f, 90f, 0f)), 0.01f);
            Assert.Less((go.transform.localScale - Vector3.one * 3f).magnitude, 1e-4f, "프리팹 크기 2 × 장식 스케일 1.5");
        }

        [Test]
        public void Add_GroupsByDefinition()
        {
            Add(Candidate("grass", 1f, 1f), Candidate("grass", 2f, 2f), Candidate("rock", 3f, 3f));

            Assert.AreEqual(2, Container.childCount);
            Assert.AreEqual(2, Container.Find("grass").childCount);
            Assert.AreEqual(1, Container.Find("rock").childCount);
            Assert.AreEqual(3, view.Count);
        }

        [Test]
        public void MissingPrefab_CreatesSmallPlaceholderWithoutCollider()
        {
            var added = Add(Candidate("ghost", 1f, 1f));
            var go = InstanceOf(added[0]);

            var box = go.transform.Find("Placeholder");
            Assert.IsNotNull(box);
            Assert.IsNull(box.GetComponent<Collider>(), "브러시 레이캐스트·게임에 걸리지 않게");
            Assert.AreEqual(ScatterSceneView.PlaceholderSize, box.localScale.x, 1e-5f);
        }

        [Test]
        public void Remove_DestroysInstance_AndEmptyGroups()
        {
            var added = Add(Candidate("grass", 1f, 1f), Candidate("rock", 2f, 2f));
            var grass = InstanceOf(added[0]);

            service.Execute(new RemoveScattersCommand(new[] { added[0].Id }));
            Assert.IsTrue(grass == null);
            Assert.IsNull(Container.Find("grass"), "빈 그룹은 지운다");

            service.Execute(new RemoveScattersCommand(new[] { added[1].Id }));
            Assert.IsNull(Container, "장식이 하나도 없으면 Scatters도 지운다");
        }

        [Test]
        public void UndoRedo_FollowData()
        {
            service.BeginGroup();
            Add(Candidate("grass", 1f, 1f));
            Add(Candidate("grass", 4f, 4f));
            service.EndGroup();
            Assert.AreEqual(2, view.Count);

            Assert.IsTrue(service.Undo());
            Assert.AreEqual(0, view.Count);
            Assert.IsNull(Container);

            Assert.IsTrue(service.Redo());
            Assert.AreEqual(2, view.Count);
            Assert.AreEqual(2, Container.Find("grass").childCount);
        }

        [Test]
        public void Sync_ReusesMatching_RecreatesMoved_RemovesStrays()
        {
            var added = Add(Candidate("grass", 1f, 1f), Candidate("rock", 2f, 2f));
            var keep = InstanceOf(added[0]);
            var moved = InstanceOf(added[1]);
            moved.transform.position += Vector3.right; // 손으로 옮긴 것 → 데이터 위치로 다시 만든다

            var stray = new GameObject("grass~999"); // 데이터에 없는 장식
            stray.transform.SetParent(Container.Find("grass"), false);
            var junk = new GameObject("not a scatter");
            junk.transform.SetParent(Container.Find("rock"), false);

            view.Dispose();
            view = new ScatterSceneView(service, catalog, root); // 도메인 리로드 뒤 다시 붙는 상황
            Assert.IsTrue(view.Sync());

            Assert.AreSame(keep, InstanceOf(added[0]), "맞는 오브젝트는 재사용");
            Assert.IsTrue(moved == null);
            Assert.Less((InstanceOf(added[1]).transform.position - added[1].WorldPosition(Grid)).magnitude, 1e-4f);
            Assert.IsTrue(stray == null);
            Assert.IsTrue(junk == null);
            Assert.AreEqual(2, view.Count);

            Assert.IsFalse(view.Sync(), "이미 맞으면 아무것도 바꾸지 않는다");
        }

        [Test]
        public void Sync_CreatesMissing_AndFixesWrongGroup()
        {
            var added = Add(Candidate("grass", 1f, 1f), Candidate("rock", 2f, 2f));
            Object.DestroyImmediate(InstanceOf(added[0]));
            InstanceOf(added[1]).transform.SetParent(Container, true); // 그룹 밖으로 빠진 장식

            view.Dispose();
            view = new ScatterSceneView(service, catalog, root);
            Assert.IsTrue(view.Sync());

            Assert.AreEqual("grass", InstanceOf(added[0]).transform.parent.name);
            Assert.AreEqual("rock", InstanceOf(added[1]).transform.parent.name);
            Assert.AreEqual(2, Container.childCount, "그룹만 남는다");
        }

        [Test]
        public void GridSync_LeavesScattersAlone()
        {
            Add(Candidate("grass", 1f, 1f));
            using var gridView = new MapSceneView(service, catalog, root);

            gridView.Sync();
            Assert.IsNotNull(Container);
            Assert.AreEqual(1, Container.Find("grass").childCount);
        }
    }
}
