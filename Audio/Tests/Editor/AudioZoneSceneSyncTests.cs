using System.Collections.Generic;
using Audio;
using MapTool.Core;
using MapTool.Editor;
using NUnit.Framework;
using UnityEngine;

namespace MapTool.Audio.Editor.Tests
{
    public sealed class AudioZoneSceneSyncTests
    {
        // 원점 (100, 0, 0), 셀 크기 (2, 1, 2): 월드 변환이 원점과 셀 크기를 반영하는지 함께 본다.
        private static readonly GridSpace Grid = new(new Vector3(100f, 0f, 0f), new Vector3(2f, 1f, 2f));

        private readonly List<Object> created = new();
        private readonly Dictionary<string, AudioZoneProfile> profiles = new();
        private PlacementService service;
        private Transform root;
        private AudioZoneSceneSync sync;

        private T Track<T>(T obj) where T : Object
        {
            created.Add(obj);
            return obj;
        }

        [SetUp]
        public void SetUp()
        {
            profiles["forest"] = Track(ScriptableObject.CreateInstance<AudioZoneProfile>());
            profiles["cave"] = Track(ScriptableObject.CreateInstance<AudioZoneProfile>());

            var catalog = new ObjectCatalog(new ObjectDefinition[0]);
            service = new PlacementService(new MapData(new Vector3Int(10, 4, 10), Grid), catalog);
            root = Track(new GameObject("Root")).transform;
            sync = NewSync();
        }

        [TearDown]
        public void TearDown()
        {
            sync.Dispose();
            for (int i = created.Count - 1; i >= 0; i--)
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            created.Clear();
        }

        private AudioZoneSceneSync NewSync() =>
            new(service, root, id => profiles.TryGetValue(id, out var p) ? p : null);

        private static ZoneBox Box(int x, int z, int sx, int sz, int sy = 1) =>
            new(new Vector3Int(x, 0, z), new Vector3Int(sx, sy, sz));

        private PlacedZone Add(string name, string profile, int priority, params ZoneBox[] boxes)
        {
            var cmd = new AddZoneCommand(new PlacedZone(0, name, profile, priority, boxes));
            Assert.IsTrue(service.Execute(cmd).IsValid);
            return cmd.Added;
        }

        private Transform Container => root.Find(MapSceneView.ZonesChildName);

        private AudioZone Get(int id)
        {
            Assert.IsTrue(sync.TryGetZone(id, out var zone), $"존 #{id}의 AudioZone이 없음");
            return zone;
        }

        [Test]
        public void NoZones_DoesNotCreateContainer()
        {
            Assert.IsFalse(sync.Sync());
            Assert.IsNull(Container);
        }

        [Test]
        public void AddZone_CreatesAudioZone_WithProfilePriorityAndWorldBoxes()
        {
            var zone = Add("숲", "forest", 3, Box(0, 0, 2, 3), Box(4, 0, 1, 1, 2));
            var audio = Get(zone.Id);

            Assert.AreEqual(Container, audio.transform.parent);
            Assert.AreEqual("숲#1", audio.name);
            Assert.AreSame(profiles["forest"], audio.Profile);
            Assert.AreEqual(3, audio.Priority);
            Assert.AreEqual(2, audio.Boxes.Count);

            // 셀 (0,0,0)~(2,1,3) → 월드 (100,0,0)~(104,1,6), 셀 (4,0,0)~(5,2,1) → 월드 (108,0,0)~(110,2,2)
            Assert.IsTrue(audio.Contains(new Vector3(101f, 0.5f, 5f)));
            Assert.IsTrue(audio.Contains(new Vector3(109f, 1.5f, 1f)));
            Assert.IsFalse(audio.Contains(new Vector3(106f, 0.5f, 1f)), "상자 사이 빈 곳");
            Assert.IsFalse(audio.Contains(new Vector3(101f, 1.5f, 1f)), "첫 상자 위");
            Assert.AreEqual(4f * 1f * 6f + 2f * 2f * 2f, audio.Volume, 1e-4f);
        }

        [Test]
        public void ModifyZone_UpdatesInPlace_AndUndoRestores()
        {
            var zone = Add("숲", "forest", 0, Box(0, 0, 2, 2));
            var audio = Get(zone.Id);

            service.Execute(new ModifyZoneCommand(zone.WithName("동굴").WithProfile("cave").WithPriority(5).WithBoxes(new[] { Box(5, 5, 1, 1) })));
            Assert.AreSame(audio, Get(zone.Id), "같은 오브젝트를 고쳐 쓴다");
            Assert.AreEqual("동굴#1", audio.name);
            Assert.AreSame(profiles["cave"], audio.Profile);
            Assert.AreEqual(5, audio.Priority);
            Assert.IsTrue(audio.Contains(new Vector3(111f, 0.5f, 11f)));
            Assert.IsFalse(audio.Contains(new Vector3(101f, 0.5f, 1f)));

            service.Undo();
            Assert.AreEqual("숲#1", audio.name);
            Assert.AreSame(profiles["forest"], audio.Profile);
            Assert.IsTrue(audio.Contains(new Vector3(101f, 0.5f, 1f)));
        }

        [Test]
        public void EmptyOrUnknownProfile_GivesNullProfile()
        {
            Assert.IsNull(Get(Add("a", "", 0, Box(0, 0, 1, 1)).Id).Profile);
            Assert.IsNull(Get(Add("b", "deleted-guid", 0, Box(0, 0, 1, 1)).Id).Profile);
        }

        [Test]
        public void RemoveZone_DestroysObject_UndoBringsItBack()
        {
            var zone = Add("숲", "forest", 0, Box(0, 0, 2, 2));
            service.Execute(new RemoveZoneCommand(zone.Id));
            Assert.AreEqual(0, Container.childCount);
            Assert.IsFalse(sync.TryGetZone(zone.Id, out _));

            service.Undo();
            Assert.AreEqual(1, Container.childCount);
            Assert.AreSame(profiles["forest"], Get(zone.Id).Profile);
        }

        [Test]
        public void Sync_ReusesMatchingChildren_AndRemovesStrays()
        {
            var forest = Add("숲", "forest", 0, Box(0, 0, 2, 2));
            Add("동굴", "cave", 1, Box(3, 3, 2, 2));
            var kept = Get(forest.Id);
            sync.Dispose();

            new GameObject("손으로 넣은 것").transform.SetParent(Container, false);
            var orphan = new GameObject("옛 존#99");
            orphan.AddComponent<AudioZone>();
            orphan.transform.SetParent(Container, false);

            sync = NewSync();
            Assert.IsTrue(sync.Sync(), "떠돌이 정리");
            Assert.AreEqual(2, Container.childCount);
            Assert.AreSame(kept, Get(forest.Id), "맞는 오브젝트는 재사용");

            Assert.IsFalse(sync.Sync(), "이미 맞으면 아무것도 안 바뀜");
        }

        [Test]
        public void Sync_FixesChildrenEditedByHand()
        {
            var zone = Add("숲", "forest", 2, Box(0, 0, 2, 2));
            var audio = Get(zone.Id);
            audio.transform.position += Vector3.up * 5f;
            audio.Configure(null, 0, new[] { new Bounds(Vector3.zero, Vector3.one) });

            Assert.IsTrue(sync.Sync());
            Assert.AreSame(profiles["forest"], audio.Profile);
            Assert.AreEqual(2, audio.Priority);
            Assert.IsTrue(audio.Contains(new Vector3(103f, 0.5f, 3f)));
        }

        [Test]
        public void MapSceneViewSync_LeavesZonesContainerAlone()
        {
            Add("숲", "forest", 0, Box(0, 0, 2, 2));
            var catalog = new EditorObjectCatalog(new MapObjectDefinition[0]);
            using var view = new MapSceneView(service, catalog, root);

            view.Sync();
            Assert.IsNotNull(Container);
            Assert.AreEqual(1, Container.childCount);
        }
    }
}
