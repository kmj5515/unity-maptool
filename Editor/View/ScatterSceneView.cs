using System;
using System.Collections.Generic;
using MapTool.Core;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MapTool.Editor
{
    /// <summary>
    /// 장식(PlacedScatter) → 씬 오브젝트 단방향 동기화. 맵 루트의 "Scatters" 자식 아래 정의ID별 그룹에 둔다.
    ///   [MapTool] 맵 / Scatters / grass / grass~12
    ///  - PlacementService의 OnScattersAdded/Removed를 받아 만들고 지운다(Undo/Redo도 같은 이벤트).
    ///  - Sync()는 씬에 이미 있는 장식을 데이터와 맞춘다. 이름·그룹·프리팹·자세가 맞으면 재사용, 아니면 다시 만든다.
    ///  - 크기는 프리팹 원래 크기 × 장식 스케일. 프리팹이 없으면 작은 대체 상자(콜라이더 없음).
    ///  - 피킹을 끄고, 유니티 Undo에 기록하지 않는다(MapSceneView와 같은 규칙).
    ///  - Hidden은 씬 가시성(에디터 전용)으로 숨긴다. 씬 파일·빌드에는 영향이 없다.
    /// </summary>
    public sealed class ScatterSceneView : IDisposable
    {
        public const char IdSeparator = '~';
        /// <summary>프리팹이 없는 장식의 대체 상자 한 변(미터).</summary>
        public const float PlaceholderSize = 0.25f;

        private readonly PlacementService service;
        private readonly EditorObjectCatalog catalog;
        private readonly Transform mapRoot;
        private readonly Dictionary<int, GameObject> instances = new();
        private readonly Dictionary<string, Transform> groups = new(StringComparer.Ordinal);
        private readonly List<GameObject> createdBuffer = new();
        private bool hidden;

        public ScatterSceneView(PlacementService service, EditorObjectCatalog catalog, Transform mapRoot)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.mapRoot = mapRoot != null ? mapRoot : throw new ArgumentNullException(nameof(mapRoot));

            service.OnScattersAdded += OnAdded;
            service.OnScattersRemoved += OnRemoved;
        }

        public int Count => instances.Count;

        /// <summary>장식을 담는 "Scatters" 자식. 아직 장식이 없으면 null일 수 있다.</summary>
        public Transform Container => mapRoot != null ? mapRoot.Find(MapSceneView.ScattersChildName) : null;

        public bool TryGetInstance(int scatterId, out GameObject instance) =>
            instances.TryGetValue(scatterId, out instance) && instance != null;

        public static string NameOf(PlacedScatter scatter) => $"{scatter.DefinitionId}{IdSeparator}{scatter.Id}";

        /// <summary>씬뷰에서 장식을 숨긴다(에디터 전용). 새로 생기는 장식도 따른다.</summary>
        public bool Hidden
        {
            get => hidden;
            set
            {
                hidden = value;
                var container = Container;
                if (container == null) return;
                if (hidden) SceneVisibilityManager.instance.Hide(container.gameObject, true);
                else SceneVisibilityManager.instance.Show(container.gameObject, true);
            }
        }

        /// <summary>씬의 장식을 데이터와 맞춘다. 무엇이든 바뀌었으면 true.</summary>
        public bool Sync()
        {
            bool changed = false;
            instances.Clear();
            groups.Clear();

            var container = Container;
            var existing = new Dictionary<int, GameObject>();
            if (container != null)
            {
                for (int g = container.childCount - 1; g >= 0; g--)
                {
                    var group = container.GetChild(g);
                    if (groups.ContainsKey(group.name))
                    {
                        Object.DestroyImmediate(group.gameObject); // 같은 이름 그룹이 둘이면 뒤엣것을 버리고 다시 만든다
                        changed = true;
                        continue;
                    }
                    groups.Add(group.name, group);

                    for (int i = group.childCount - 1; i >= 0; i--)
                    {
                        var child = group.GetChild(i).gameObject;
                        if (TryParseId(child.name, out int id) && !existing.ContainsKey(id)) existing.Add(id, child);
                        else
                        {
                            Object.DestroyImmediate(child);
                            changed = true;
                        }
                    }
                }
            }

            createdBuffer.Clear();
            foreach (var scatter in service.Map.Scatters)
            {
                if (existing.Remove(scatter.Id, out var go) && Matches(go, scatter))
                {
                    instances.Add(scatter.Id, go);
                    continue;
                }

                if (go != null) Object.DestroyImmediate(go);
                Create(scatter);
                changed = true;
            }
            FinishCreated();

            foreach (var leftover in existing.Values)
            {
                Object.DestroyImmediate(leftover);
                changed = true;
            }

            changed |= RemoveEmptyGroups();
            return changed;
        }

        public void Dispose()
        {
            service.OnScattersAdded -= OnAdded;
            service.OnScattersRemoved -= OnRemoved;
        }

        // ───────────────────────── 이벤트 ─────────────────────────

        private void OnAdded(IReadOnlyList<PlacedScatter> added)
        {
            createdBuffer.Clear();
            foreach (var scatter in added)
            {
                if (instances.Remove(scatter.Id, out var old) && old != null) Object.DestroyImmediate(old);
                Create(scatter);
            }
            FinishCreated();
        }

        private void OnRemoved(IReadOnlyList<PlacedScatter> removed)
        {
            foreach (var scatter in removed)
            {
                if (instances.Remove(scatter.Id, out var go) && go != null) Object.DestroyImmediate(go);
            }
            RemoveEmptyGroups();
        }

        // ───────────────────────── 만들기 ─────────────────────────

        private static bool TryParseId(string name, out int id)
        {
            id = 0;
            int at = name.LastIndexOf(IdSeparator);
            return at >= 0 && int.TryParse(name.Substring(at + 1), out id) && id > 0;
        }

        private GameObject GetPrefab(string definitionId) =>
            catalog.TryGetAsset(definitionId, out var asset) ? asset.Prefab : null;

        private Vector3 ScaleOf(PlacedScatter scatter)
        {
            var prefab = GetPrefab(scatter.DefinitionId);
            var baseScale = prefab != null ? prefab.transform.localScale : Vector3.one;
            return baseScale * scatter.Scale;
        }

        private bool Matches(GameObject go, PlacedScatter scatter)
        {
            if (go.name != NameOf(scatter)) return false;
            if (go.transform.parent == null || go.transform.parent.name != scatter.DefinitionId) return false;

            var prefab = GetPrefab(scatter.DefinitionId);
            if (prefab != null && PrefabUtility.IsPartOfPrefabAsset(prefab) &&
                PrefabUtility.GetCorrespondingObjectFromSource(go) != prefab)
                return false;

            var t = go.transform;
            return (t.position - scatter.WorldPosition(service.Map.Grid)).sqrMagnitude < 1e-6f &&
                   Quaternion.Angle(t.rotation, scatter.Rotation) < 0.01f &&
                   (t.localScale - ScaleOf(scatter)).sqrMagnitude < 1e-8f;
        }

        private Transform GroupFor(string definitionId)
        {
            if (groups.TryGetValue(definitionId, out var group) && group != null) return group;

            var container = Container;
            if (container == null)
            {
                container = new GameObject(MapSceneView.ScattersChildName).transform;
                container.SetParent(mapRoot, false);
            }

            group = new GameObject(definitionId).transform;
            group.SetParent(container, false);
            groups[definitionId] = group;
            return group;
        }

        private void Create(PlacedScatter scatter)
        {
            var parent = GroupFor(scatter.DefinitionId);
            var prefab = GetPrefab(scatter.DefinitionId);
            GameObject go;
            if (prefab == null) go = CreatePlaceholder(parent);
            else if (PrefabUtility.IsPartOfPrefabAsset(prefab)) go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            else go = Object.Instantiate(prefab, parent);

            go.name = NameOf(scatter);
            go.transform.SetPositionAndRotation(scatter.WorldPosition(service.Map.Grid), scatter.Rotation);
            go.transform.localScale = ScaleOf(scatter);
            instances[scatter.Id] = go;
            createdBuffer.Add(go);
        }

        /// <summary>새로 만든 장식의 피킹을 끄고, 숨김 중이면 함께 숨긴다. 한 번에 모아 처리한다.</summary>
        private void FinishCreated()
        {
            if (createdBuffer.Count == 0) return;
            var array = createdBuffer.ToArray();
            createdBuffer.Clear();
            SceneVisibilityManager.instance.DisablePicking(array, true);
            if (hidden) SceneVisibilityManager.instance.Hide(array, true);
        }

        private bool RemoveEmptyGroups()
        {
            bool changed = false;
            var container = Container;
            if (container == null) return false;

            for (int g = container.childCount - 1; g >= 0; g--)
            {
                var group = container.GetChild(g);
                if (group.childCount > 0) continue;
                groups.Remove(group.name);
                Object.DestroyImmediate(group.gameObject);
                changed = true;
            }
            if (container.childCount == 0)
            {
                Object.DestroyImmediate(container.gameObject);
                changed = true;
            }
            return changed;
        }

        private static GameObject CreatePlaceholder(Transform parent)
        {
            var go = new GameObject();
            go.transform.SetParent(parent, false);
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(box.GetComponent<Collider>());
            box.name = "Placeholder";
            box.transform.SetParent(go.transform, false);
            box.transform.localPosition = new Vector3(0f, PlaceholderSize * 0.5f, 0f);
            box.transform.localScale = Vector3.one * PlaceholderSize;
            return go;
        }
    }
}
