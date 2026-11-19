using System;
using System.Collections.Generic;
using MapTool.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MapTool.Editor
{
    /// <summary>
    /// MapData → 씬 오브젝트 단방향 동기화.
    ///  - PlacementService 이벤트를 받아 프리팹 인스턴스를 만들고, 옮기고, 지운다(Undo/Redo도 같은 이벤트로 들어온다).
    ///  - Sync()는 씬에 이미 있는 자식을 데이터와 맞춘다(존용 "Zones"·장식용 "Scatters" 자식은 제외). 이름(정의ID#인스턴스ID)·프리팹·자세가 맞으면 재사용하고 아니면 다시 만든다.
    ///  - 씬 오브젝트는 결과물이므로 Transform을 직접 고쳐도 데이터에 반영하지 않는다. 피킹을 꺼서 실수로 잡지 않게 한다.
    ///  - 유니티 Undo에 기록하지 않는다(코어 커맨드 스택과 이중화 방지).
    /// </summary>
    public sealed class MapSceneView : IDisposable
    {
        public const string RootPrefix = "[MapTool] ";

        /// <summary>루트 아래 존 오브젝트를 모아 두는 자식. 오브젝트 동기화는 이 자식을 건드리지 않는다.</summary>
        public const string ZonesChildName = "Zones";
        /// <summary>장식(브러시) 인스턴스를 담는 자식. Sync는 건드리지 않고, 브러시 지면 레이캐스트는 이 아래를 무시한다.</summary>
        public const string ScattersChildName = "Scatters";
        /// <summary>스폰 지점(SpawnPoint)을 담는 자식. 오브젝트 동기화는 이 자식을 건드리지 않는다.</summary>
        public const string SpawnsChildName = "Spawns";

        private readonly PlacementService service;
        private readonly EditorObjectCatalog catalog;
        private readonly Dictionary<int, GameObject> instances = new();

        public MapSceneView(PlacementService service, EditorObjectCatalog catalog, Transform root)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            Root = root != null ? root : throw new ArgumentNullException(nameof(root));

            service.OnPlaced += Create;
            service.OnRemoved += Destroy;
            service.OnMoved += Move;
        }

        public Transform Root { get; }
        public int Count => instances.Count;

        public bool TryGetInstance(int instanceId, out GameObject instance) =>
            instances.TryGetValue(instanceId, out instance) && instance != null;

        public static string RootName(string mapName) => RootPrefix + mapName;

        public static Transform FindRoot(Scene scene, string mapName)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
            string rootName = RootName(mapName);
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.name == rootName) return go.transform;
            }
            return null;
        }

        public static Transform CreateRoot(Scene scene, string mapName)
        {
            var go = new GameObject(RootName(mapName));
            if (scene.IsValid()) SceneManager.MoveGameObjectToScene(go, scene);
            return go.transform;
        }

        /// <summary>오브젝트가 놓일 월드 자세. 점유 영역 바닥 중심에 두고 Y축으로 회전한다.</summary>
        public static Pose ComputePose(PlacementService service, PlacedObject placed)
        {
            var grid = service.Map.Grid;
            var position = service.TryGetBounds(placed, out var bounds)
                ? grid.FootprintBaseCenter(bounds)
                : grid.CellToWorld(placed.Cell);
            return new Pose(position, Quaternion.Euler(0f, placed.Rotation.ToDegrees(), 0f));
        }

        /// <summary>씬의 자식을 데이터와 맞춘다. 무엇이든 바뀌었으면 true.</summary>
        public bool Sync()
        {
            bool changed = false;
            instances.Clear();

            var existing = new Dictionary<int, GameObject>();
            for (int i = Root.childCount - 1; i >= 0; i--)
            {
                var child = Root.GetChild(i).gameObject;
                if (child.name == ZonesChildName || child.name == ScattersChildName || child.name == SpawnsChildName) continue;
                if (TryParseInstanceId(child.name, out int id) && !existing.ContainsKey(id))
                {
                    existing.Add(id, child);
                }
                else
                {
                    Object.DestroyImmediate(child);
                    changed = true;
                }
            }

            foreach (var placed in service.Map.Objects)
            {
                if (existing.Remove(placed.InstanceId, out var go) && Matches(go, placed))
                {
                    instances.Add(placed.InstanceId, go);
                    continue;
                }

                if (go != null) Object.DestroyImmediate(go);
                Create(placed);
                changed = true;
            }

            foreach (var leftover in existing.Values)
            {
                Object.DestroyImmediate(leftover);
                changed = true;
            }

            return changed;
        }

        public void Dispose()
        {
            service.OnPlaced -= Create;
            service.OnRemoved -= Destroy;
            service.OnMoved -= Move;
        }

        private static string NameOf(PlacedObject placed) => $"{placed.DefinitionId}#{placed.InstanceId}";

        private static bool TryParseInstanceId(string name, out int id)
        {
            id = 0;
            int hash = name.LastIndexOf('#');
            return hash >= 0 && int.TryParse(name.Substring(hash + 1), out id) && id > 0;
        }

        private bool Matches(GameObject go, PlacedObject placed)
        {
            if (go.name != NameOf(placed)) return false;

            var prefab = GetPrefab(placed);
            if (prefab != null && PrefabUtility.IsPartOfPrefabAsset(prefab) &&
                PrefabUtility.GetCorrespondingObjectFromSource(go) != prefab)
                return false;

            var pose = ComputePose(service, placed);
            return (go.transform.position - pose.position).sqrMagnitude < 1e-6f &&
                   Quaternion.Angle(go.transform.rotation, pose.rotation) < 0.01f;
        }

        private GameObject GetPrefab(PlacedObject placed) =>
            catalog.TryGetAsset(placed.DefinitionId, out var asset) ? asset.Prefab : null;

        private void Create(PlacedObject placed)
        {
            var prefab = GetPrefab(placed);
            GameObject go;
            if (prefab == null) go = CreatePlaceholder(placed);
            else if (PrefabUtility.IsPartOfPrefabAsset(prefab)) go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, Root);
            else go = Object.Instantiate(prefab, Root);

            go.name = NameOf(placed);
            var pose = ComputePose(service, placed);
            go.transform.SetPositionAndRotation(pose.position, pose.rotation);
            SceneVisibilityManager.instance.DisablePicking(go, true);
            instances[placed.InstanceId] = go;
        }

        private void Destroy(PlacedObject placed)
        {
            if (instances.Remove(placed.InstanceId, out var go) && go != null) Object.DestroyImmediate(go);
        }

        private void Move(PlacedObject before, PlacedObject after)
        {
            if (!TryGetInstance(after.InstanceId, out var go))
            {
                Create(after);
                return;
            }
            var pose = ComputePose(service, after);
            go.transform.SetPositionAndRotation(pose.position, pose.rotation);
        }

        /// <summary>프리팹이 없거나 정의를 모를 때 점유 크기만큼의 상자로 대신 보여 준다.</summary>
        private GameObject CreatePlaceholder(PlacedObject placed)
        {
            var size = catalog.TryGet(placed.DefinitionId, out var definition) ? definition.Size : Vector3Int.one;
            var world = Vector3.Scale(size, service.Map.Grid.CellSize);

            var go = new GameObject();
            go.transform.SetParent(Root, false);
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(box.GetComponent<Collider>());
            box.name = "Placeholder";
            box.transform.SetParent(go.transform, false);
            box.transform.localPosition = new Vector3(0f, world.y * 0.5f, 0f);
            box.transform.localScale = world * 0.95f;
            return go;
        }
    }
}
