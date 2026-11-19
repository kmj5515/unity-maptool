using System;
using System.Collections.Generic;
using MapTool.Core;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MapTool.Editor
{
    /// <summary>
    /// MapData의 스폰 지점 → 씬의 SpawnPoint 단방향 실시간 동기화. 존 동기화와 같은 방식이다.
    ///  - "[MapTool] 맵/Spawns" 아래에 스폰마다 "이름#ID" 오브젝트 하나와 SpawnPoint 컴포넌트를 둔다.
    ///  - 위치는 칸 바닥면 중심, 방향은 스폰 방향. 기본 스폰 표시는 맵의 기본 스폰(없으면 첫 스폰)을 따른다.
    ///  - 추가·삭제·변경·기본 변경 이벤트(Undo/Redo 포함)를 받아 바로 반영한다. Sync()는 이미 있는 자식을 데이터와 맞춘다.
    ///  - 결과물이므로 직접 고쳐도 데이터에 반영하지 않는다. 피킹을 꺼서 씬뷰에서 실수로 잡지 않게 한다.
    /// </summary>
    public sealed class SpawnSceneSync : IDisposable
    {
        private readonly PlacementService service;
        private readonly Transform mapRoot;
        private readonly Dictionary<int, SpawnPoint> spawns = new();
        private Transform container;

        public SpawnSceneSync(PlacementService service, Transform mapRoot)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
            this.mapRoot = mapRoot != null ? mapRoot : throw new ArgumentNullException(nameof(mapRoot));

            service.OnSpawnAdded += Create;
            service.OnSpawnRemoved += Destroy;
            service.OnSpawnChanged += Change;
            service.OnDefaultSpawnChanged += DefaultChanged;
        }

        public int Count => spawns.Count;

        public bool TryGetSpawn(int spawnId, out SpawnPoint spawn) =>
            spawns.TryGetValue(spawnId, out spawn) && spawn != null;

        public static string NameOf(PlacedSpawn spawn) =>
            $"{(string.IsNullOrEmpty(spawn.Name) ? "Spawn" : spawn.Name)}#{spawn.Id}";

        private int DefaultId => service.Map.TryGetDefaultSpawn(out var spawn) ? spawn.Id : 0;

        /// <summary>씬의 자식을 데이터와 맞춘다. 무엇이든 바뀌었으면 true.</summary>
        public bool Sync()
        {
            if (mapRoot == null) return false;

            bool changed = false;
            spawns.Clear();
            container = mapRoot.Find(MapSceneView.SpawnsChildName);

            var existing = new Dictionary<int, SpawnPoint>();
            if (container != null)
            {
                for (int i = container.childCount - 1; i >= 0; i--)
                {
                    var child = container.GetChild(i).gameObject;
                    var component = child.GetComponent<SpawnPoint>();
                    if (component != null && TryParseId(child.name, out int id) && !existing.ContainsKey(id))
                    {
                        existing.Add(id, component);
                    }
                    else
                    {
                        Object.DestroyImmediate(child);
                        changed = true;
                    }
                }
            }

            int defaultId = DefaultId;
            foreach (var spawn in service.Map.Spawns)
            {
                if (existing.Remove(spawn.Id, out var component))
                {
                    spawns.Add(spawn.Id, component);
                    changed |= Apply(component, spawn, defaultId);
                }
                else
                {
                    Create(spawn);
                    changed = true;
                }
            }

            foreach (var leftover in existing.Values)
            {
                Object.DestroyImmediate(leftover.gameObject);
                changed = true;
            }
            return changed;
        }

        public void Dispose()
        {
            service.OnSpawnAdded -= Create;
            service.OnSpawnRemoved -= Destroy;
            service.OnSpawnChanged -= Change;
            service.OnDefaultSpawnChanged -= DefaultChanged;
        }

        private static bool TryParseId(string name, out int id)
        {
            id = 0;
            int hash = name.LastIndexOf('#');
            return hash >= 0 && int.TryParse(name.Substring(hash + 1), out id) && id > 0;
        }

        private Transform EnsureContainer()
        {
            if (container != null) return container;
            container = mapRoot.Find(MapSceneView.SpawnsChildName);
            if (container != null) return container;

            var go = new GameObject(MapSceneView.SpawnsChildName);
            go.transform.SetParent(mapRoot, false);
            container = go.transform;
            return container;
        }

        private void Create(PlacedSpawn spawn)
        {
            if (mapRoot == null) return;

            var go = new GameObject();
            go.transform.SetParent(EnsureContainer(), false);
            var component = go.AddComponent<SpawnPoint>();
            SceneVisibilityManager.instance.DisablePicking(go, true);
            spawns[spawn.Id] = component;
            Apply(component, spawn, DefaultId);
            RefreshDefaults(); // 첫 스폰은 바로 기본이 된다
        }

        private void Destroy(PlacedSpawn spawn)
        {
            if (spawns.Remove(spawn.Id, out var component) && component != null)
                Object.DestroyImmediate(component.gameObject);
            RefreshDefaults(); // 기본이 지워지면 다음 스폰이 기본이 된다
        }

        private void Change(PlacedSpawn before, PlacedSpawn after)
        {
            if (TryGetSpawn(after.Id, out var component)) Apply(component, after, DefaultId);
            else Create(after);
        }

        private void DefaultChanged(int before, int after) => RefreshDefaults();

        private void RefreshDefaults()
        {
            int defaultId = DefaultId;
            foreach (var spawn in service.Map.Spawns)
            {
                if (TryGetSpawn(spawn.Id, out var component)) Apply(component, spawn, defaultId);
            }
        }

        /// <summary>컴포넌트를 스폰 값에 맞춘다. 바뀐 것이 있으면 true.</summary>
        private bool Apply(SpawnPoint component, PlacedSpawn spawn, int defaultId)
        {
            var go = component.gameObject;
            var transform = component.transform;
            bool changed = false;

            string name = NameOf(spawn);
            if (go.name != name)
            {
                go.name = name;
                changed = true;
            }

            var position = spawn.WorldPosition(service.Map.Grid);
            var rotation = spawn.WorldRotation;
            if ((transform.position - position).sqrMagnitude > 1e-8f || Quaternion.Angle(transform.rotation, rotation) > 0.01f)
            {
                transform.SetPositionAndRotation(position, rotation);
                changed = true;
            }

            bool isDefault = spawn.Id == defaultId;
            if (component.Id != spawn.Id || component.SpawnName != spawn.Name || component.IsDefault != isDefault)
            {
                component.Configure(spawn.Id, spawn.Name, isDefault);
                EditorUtility.SetDirty(component);
                changed = true;
            }
            return changed;
        }
    }
}
