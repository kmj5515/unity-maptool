using System.Collections.Generic;
using MapTool.Core;
using NUnit.Framework;
using UnityEngine;

namespace MapTool.Editor.Tests
{
    public sealed class SpawnSceneSyncTests
    {
        // 원점 (100, 0, 0), 셀 크기 (2, 1, 2): 월드 위치가 셀 크기와 원점을 반영하는지 함께 본다.
        private static readonly GridSpace Grid = new(new Vector3(100f, 0f, 0f), new Vector3(2f, 1f, 2f));

        private readonly List<Object> created = new();
        private PlacementService service;
        private Transform root;
        private SpawnSceneSync sync;

        private T Track<T>(T obj) where T : Object
        {
            created.Add(obj);
            return obj;
        }

        [SetUp]
        public void SetUp()
        {
            var catalog = new EditorObjectCatalog(new MapObjectDefinition[0]);
            service = new PlacementService(new MapData(new Vector3Int(10, 2, 10), Grid), catalog);
            root = Track(new GameObject("Root")).transform;
            sync = new SpawnSceneSync(service, root);
        }

        [TearDown]
        public void TearDown()
        {
            sync.Dispose();
            for (int i = created.Count - 1; i >= 0; i--)
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            created.Clear();
        }

        private PlacedSpawn Add(int x, int z, GridRotation r = GridRotation.R0, string name = "입구")
        {
            var cmd = new AddSpawnCommand(new PlacedSpawn(0, name, new Vector3Int(x, 0, z), r));
            Assert.IsTrue(service.Execute(cmd).IsValid);
            return cmd.Added;
        }

        private Transform Container => root.Find(MapSceneView.SpawnsChildName);

        private SpawnPoint Get(int id)
        {
            Assert.IsTrue(sync.TryGetSpawn(id, out var point), $"스폰 #{id} 오브젝트가 없습니다.");
            return point;
        }

        [Test]
        public void NoSpawns_NoContainer()
        {
            Assert.IsFalse(sync.Sync());
            Assert.IsNull(Container);
            Assert.AreEqual(0, sync.Count);
        }

        [Test]
        public void Add_CreatesSpawnPoint_WithPoseAndDefault()
        {
            var spawn = Add(3, 4, GridRotation.R90);
            var point = Get(spawn.Id);

            Assert.AreEqual(Container, point.transform.parent);
            Assert.AreEqual($"입구#{spawn.Id}", point.name);
            Assert.AreEqual(spawn.Id, point.Id);
            Assert.AreEqual("입구", point.SpawnName);
            Assert.IsTrue(point.IsDefault, "첫 스폰은 기본이어야 합니다.");
            // 칸 (3,0,4), 셀 (2,1,2), 원점 (100,0,0) → 바닥면 중심 (107, 0, 9)
            Assert.That(Vector3.Distance(new Vector3(107f, 0f, 9f), point.transform.position), Is.LessThan(1e-4f));
            Assert.That(Quaternion.Angle(Quaternion.Euler(0f, 90f, 0f), point.transform.rotation), Is.LessThan(0.01f));

            var second = Add(5, 5, name: "");
            Assert.AreEqual($"Spawn#{second.Id}", Get(second.Id).name);
            Assert.IsFalse(Get(second.Id).IsDefault);
        }

        [Test]
        public void Modify_UpdatesObject_AndUndoRestores()
        {
            var spawn = Add(1, 1);
            var point = Get(spawn.Id);
            var before = point.transform.position;

            Assert.IsTrue(service.Execute(new ModifySpawnCommand(spawn.WithCell(new Vector3Int(2, 0, 1)).WithName("광장"))).IsValid);
            Assert.AreEqual($"광장#{spawn.Id}", point.name);
            Assert.AreEqual("광장", point.SpawnName);
            Assert.That(Vector3.Distance(before + new Vector3(2f, 0f, 0f), point.transform.position), Is.LessThan(1e-4f));

            Assert.IsTrue(service.Undo());
            Assert.AreEqual($"입구#{spawn.Id}", point.name);
            Assert.That(Vector3.Distance(before, point.transform.position), Is.LessThan(1e-4f));
        }

        [Test]
        public void SetDefault_MovesFlag()
        {
            var first = Add(1, 1);
            var second = Add(2, 2);

            Assert.IsTrue(service.Execute(new SetDefaultSpawnCommand(second.Id)).IsValid);
            Assert.IsFalse(Get(first.Id).IsDefault);
            Assert.IsTrue(Get(second.Id).IsDefault);

            Assert.IsTrue(service.Undo());
            Assert.IsTrue(Get(first.Id).IsDefault);
            Assert.IsFalse(Get(second.Id).IsDefault);
        }

        [Test]
        public void Remove_DestroysObject_AndUndoBringsBack()
        {
            var first = Add(1, 1);
            var second = Add(2, 2);

            Assert.IsTrue(service.Execute(new RemoveSpawnCommand(first.Id)).IsValid);
            Assert.IsFalse(sync.TryGetSpawn(first.Id, out _));
            Assert.AreEqual(1, Container.childCount);
            Assert.IsTrue(Get(second.Id).IsDefault, "기본이 지워지면 남은 스폰이 기본이 됩니다.");

            Assert.IsTrue(service.Undo());
            Assert.IsTrue(Get(first.Id).IsDefault);
            Assert.IsFalse(Get(second.Id).IsDefault);
            Assert.AreEqual(2, Container.childCount);
        }

        [Test]
        public void Sync_ReusesMatching_AndRemovesStrays()
        {
            var spawn = Add(1, 1);
            var point = Get(spawn.Id);
            sync.Dispose();

            // 새 세션: 기존 자식은 재사용, 엉뚱한 자식과 지워진 스폰의 자식은 정리
            new GameObject("쓰레기").transform.SetParent(Container, false);
            var orphan = new GameObject("옛 스폰#99");
            orphan.AddComponent<SpawnPoint>();
            orphan.transform.SetParent(Container, false);

            sync = new SpawnSceneSync(service, root);
            Assert.IsTrue(sync.Sync());
            Assert.AreSame(point, Get(spawn.Id));
            Assert.AreEqual(1, Container.childCount);

            Assert.IsFalse(sync.Sync(), "이미 맞으면 바뀐 것이 없어야 합니다.");
        }

        [Test]
        public void MapSceneViewSync_LeavesSpawnsContainerAlone()
        {
            var spawn = Add(1, 1);
            var catalog = new EditorObjectCatalog(new MapObjectDefinition[0]);
            using var view = new MapSceneView(service, catalog, root);

            view.Sync();
            Assert.IsNotNull(Container);
            Assert.IsTrue(sync.TryGetSpawn(spawn.Id, out _));
        }

        [Test]
        public void PlayerSpawner_MoveTo_MovesCharacterController_AndKeepsItEnabled()
        {
            var player = Track(new GameObject("Player"));
            var controller = player.AddComponent<CharacterController>();
            var spawn = Track(new GameObject("Spawn")).transform;
            spawn.SetPositionAndRotation(new Vector3(5f, 1f, -3f), Quaternion.Euler(0f, 180f, 0f));

            PlayerSpawner.MoveTo(player.transform, spawn);

            Assert.That(Vector3.Distance(spawn.position, player.transform.position), Is.LessThan(1e-4f));
            Assert.That(Quaternion.Angle(spawn.rotation, player.transform.rotation), Is.LessThan(0.01f));
            Assert.IsTrue(controller.enabled);
        }

        [Test]
        public void PickDefault_PrefersFlag_ThenLowestId_ElseNull()
        {
            SpawnPoint Make(int id, bool isDefault)
            {
                var point = Track(new GameObject($"S{id}")).AddComponent<SpawnPoint>();
                point.Configure(id, "", isDefault);
                return point;
            }

            var a = Make(5, false);
            var b = Make(2, false);
            Assert.AreSame(b, SpawnPoint.PickDefault(new[] { a, b }));

            var c = Make(9, true);
            Assert.AreSame(c, SpawnPoint.PickDefault(new[] { a, b, c }));

            Assert.IsNull(SpawnPoint.PickDefault(new SpawnPoint[0]));
            Assert.IsNull(SpawnPoint.PickDefault(null));
        }

        [Test]
        public void ChooseStart_UsesRequested_ElseFallsBackToDefault()
        {
            SpawnPoint Make(int id, bool isDefault)
            {
                var point = Track(new GameObject($"S{id}")).AddComponent<SpawnPoint>();
                point.Configure(id, "", isDefault);
                return point;
            }

            var main = Make(1, true);
            var side = Make(4, false);
            var all = new[] { main, side };

            Assert.AreSame(side, PlayerSpawner.ChooseStart(all, 4, out bool missing), "고른 스폰이 기본보다 우선");
            Assert.IsFalse(missing);

            Assert.AreSame(main, PlayerSpawner.ChooseStart(all, 0, out missing), "지정이 없으면 기본");
            Assert.IsFalse(missing);

            Assert.AreSame(main, PlayerSpawner.ChooseStart(all, 99, out missing), "고른 스폰이 지워졌으면 기본");
            Assert.IsTrue(missing);

            Assert.IsNull(PlayerSpawner.ChooseStart(new SpawnPoint[0], 4, out missing));
            Assert.IsNull(SpawnPoint.PickById(null, 4));
        }

        [Test]
        public void PlayFromSpawn_PendingIdClears()
        {
            try
            {
                UnityEditor.SessionState.SetInt(PlayerSpawner.PlayFromSpawnKey, 7);
                Assert.AreEqual(7, PlayFromSpawn.PendingSpawnId);
                PlayFromSpawn.Clear();
                Assert.AreEqual(0, PlayFromSpawn.PendingSpawnId);
            }
            finally
            {
                UnityEditor.SessionState.EraseInt(PlayerSpawner.PlayFromSpawnKey);
            }
        }
    }
}
