using System;
using System.Collections.Generic;
using Audio;
using MapTool.Core;
using MapTool.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MapTool.Audio.Editor
{
    /// <summary>
    /// MapData의 존 → 씬의 AudioZone 단방향 실시간 동기화. MapSceneView와 같은 방식이다.
    ///  - "[MapTool] 맵/Zones" 아래에 존마다 "이름#ID" 오브젝트 하나와 AudioZone 컴포넌트를 둔다.
    ///  - 오브젝트는 존 전체 영역의 중심에 놓고, 상자는 그 기준 로컬 Bounds로 넣는다.
    ///  - 추가·삭제·변경 이벤트(Undo/Redo 포함)를 받아 바로 반영한다. Sync()는 이미 있는 자식을 데이터와 맞춘다.
    ///  - 결과물이므로 직접 고쳐도 데이터에 반영하지 않는다. 피킹을 꺼서 씬뷰에서 실수로 잡지 않게 한다.
    /// </summary>
    public sealed class AudioZoneSceneSync : IZoneSceneSync
    {
        private readonly PlacementService service;
        private readonly Transform mapRoot;
        private readonly Func<string, AudioZoneProfile> resolveProfile;
        private readonly Dictionary<int, AudioZone> zones = new();
        private Transform container;

        public AudioZoneSceneSync(PlacementService service, Transform mapRoot, Func<string, AudioZoneProfile> resolveProfile)
        {
            this.service = service ?? throw new ArgumentNullException(nameof(service));
            this.mapRoot = mapRoot != null ? mapRoot : throw new ArgumentNullException(nameof(mapRoot));
            this.resolveProfile = resolveProfile ?? throw new ArgumentNullException(nameof(resolveProfile));

            service.OnZoneAdded += Create;
            service.OnZoneRemoved += Destroy;
            service.OnZoneChanged += Change;
        }

        public int Count => zones.Count;

        public bool TryGetZone(int zoneId, out AudioZone zone) =>
            zones.TryGetValue(zoneId, out zone) && zone != null;

        public static string NameOf(PlacedZone zone) => $"{zone.Name}#{zone.Id}";

        public bool Sync()
        {
            if (mapRoot == null) return false;

            bool changed = false;
            zones.Clear();
            container = mapRoot.Find(MapSceneView.ZonesChildName);

            var existing = new Dictionary<int, AudioZone>();
            if (container != null)
            {
                for (int i = container.childCount - 1; i >= 0; i--)
                {
                    var child = container.GetChild(i).gameObject;
                    var component = child.GetComponent<AudioZone>();
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

            foreach (var zone in service.Map.Zones)
            {
                if (existing.Remove(zone.Id, out var component))
                {
                    zones.Add(zone.Id, component);
                    changed |= Apply(component, zone);
                }
                else
                {
                    Create(zone);
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
            service.OnZoneAdded -= Create;
            service.OnZoneRemoved -= Destroy;
            service.OnZoneChanged -= Change;
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
            container = mapRoot.Find(MapSceneView.ZonesChildName);
            if (container != null) return container;

            var go = new GameObject(MapSceneView.ZonesChildName);
            go.transform.SetParent(mapRoot, false);
            container = go.transform;
            return container;
        }

        private void Create(PlacedZone zone)
        {
            if (mapRoot == null) return;

            var go = new GameObject();
            go.transform.SetParent(EnsureContainer(), false);
            var component = go.AddComponent<AudioZone>();
            SceneVisibilityManager.instance.DisablePicking(go, true);
            zones[zone.Id] = component;
            Apply(component, zone);
        }

        private void Destroy(PlacedZone zone)
        {
            if (zones.Remove(zone.Id, out var component) && component != null)
                Object.DestroyImmediate(component.gameObject);
        }

        private void Change(PlacedZone before, PlacedZone after)
        {
            if (TryGetZone(after.Id, out var component)) Apply(component, after);
            else Create(after);
        }

        /// <summary>컴포넌트를 존 값에 맞춘다. 바뀐 것이 있으면 true.</summary>
        private bool Apply(AudioZone component, PlacedZone zone)
        {
            var go = component.gameObject;
            var transform = component.transform;
            var grid = service.Map.Grid;
            bool changed = false;

            string name = NameOf(zone);
            if (go.name != name)
            {
                go.name = name;
                changed = true;
            }

            var area = zone.Bounds;
            var center = (grid.CellToWorld(area.min) + grid.CellToWorld(area.max)) * 0.5f;
            if ((transform.position - center).sqrMagnitude > 1e-8f || transform.rotation != Quaternion.identity)
            {
                transform.SetPositionAndRotation(center, Quaternion.identity);
                changed = true;
            }

            var boxes = new Bounds[zone.Boxes.Count];
            for (int i = 0; i < boxes.Length; i++)
            {
                var box = zone.Boxes[i].Bounds;
                var min = grid.CellToWorld(box.min);
                var max = grid.CellToWorld(box.max);
                var localCenter = transform.InverseTransformPoint((min + max) * 0.5f);
                var localSize = transform.InverseTransformVector(max - min);
                boxes[i] = new Bounds(localCenter, new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z)));
            }

            var profile = resolveProfile(zone.ProfileId);
            if (component.Profile != profile || component.Priority != zone.Priority || !SameBoxes(component.Boxes, boxes))
            {
                component.Configure(profile, zone.Priority, boxes);
                EditorUtility.SetDirty(component);
                changed = true;
            }
            return changed;
        }

        private static bool SameBoxes(IReadOnlyList<Bounds> a, IReadOnlyList<Bounds> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }
    }
}
