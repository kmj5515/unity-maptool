using System;
using System.Collections.Generic;
using MapTool.Core;
using UnityEngine;

namespace MapTool.Editor
{
    /// <summary>에디터만 아는 검사(프리팹, 사운드 프로필)의 문제 코드.</summary>
    public static class EditorValidationCodes
    {
        public const string MissingPrefab = "missing-prefab";
        public const string ProfileEmpty = "profile-empty";
        public const string ProfileMissing = "profile-missing";
    }

    /// <summary>
    /// 코어 검사(MapValidator)에 에디터 검사를 더한다. 에디터 검사는 모두 경고다(맵은 동작하지만 의도와 다를 수 있다).
    ///   프리팹 없음: 정의는 있는데 프리팹이 비어 씬에 회색 상자로 보인다. 같은 정의끼리 한 줄로 묶는다.
    ///   존 프로필: 비어 있거나(소리가 안 바뀜) 프로필 에셋을 찾을 수 없다.
    /// </summary>
    internal static class EditorMapValidator
    {
        public static List<ValidationIssue> Validate(MapData map, EditorObjectCatalog catalog,
            Func<string, MapToolWindow.ProfileState> profileState = null)
        {
            var results = new List<ValidationIssue>();
            MapValidator.Validate(map, catalog, results);
            CheckPrefabs(map, catalog, results);
            CheckProfiles(map, profileState ?? (id => MapToolWindow.GetProfileState(id, out _)), results);
            MapValidator.Sort(results);
            return results;
        }

        private sealed class Group
        {
            public readonly List<int> Ids = new();
            public Bounds Area;
        }

        private static void CheckPrefabs(MapData map, EditorObjectCatalog catalog, List<ValidationIssue> results)
        {
            var objectGroups = new Dictionary<string, Group>();
            var objectOrder = new List<string>();
            var seen = new HashSet<int>();
            foreach (var placed in map.Objects)
            {
                if (!seen.Add(placed.InstanceId)) continue; // 중복 ID는 코어가 따로 알린다
                if (!IsMissingPrefab(catalog, placed.DefinitionId, false, out var definition)) continue;
                var area = MapValidator.CellArea(map.Grid, placed.GetBounds(definition));
                AddTo(objectGroups, objectOrder, placed.DefinitionId, placed.InstanceId, area);
            }

            var scatterGroups = new Dictionary<string, Group>();
            var scatterOrder = new List<string>();
            seen.Clear();
            foreach (var s in map.Scatters)
            {
                if (!seen.Add(s.Id)) continue;
                if (!IsMissingPrefab(catalog, s.DefinitionId, true, out _)) continue;
                if (!PlacementService.IsValidScatterValue(s)) continue; // 값이 깨진 것은 코어 오류로 충분하다
                AddTo(scatterGroups, scatterOrder, s.DefinitionId, s.Id, new Bounds(s.Position, map.Grid.CellSize));
            }

            foreach (var id in objectOrder)
            {
                var g = objectGroups[id];
                results.Add(new ValidationIssue(IssueSeverity.Warning, EditorValidationCodes.MissingPrefab, IssueTarget.Object,
                    g.Ids, g.Area, $"'{id}' {g.Ids.Count}개: 정의에 프리팹이 없어 회색 상자로 보입니다. 정의 에셋에 프리팹을 지정하세요."));
            }
            foreach (var id in scatterOrder)
            {
                var g = scatterGroups[id];
                results.Add(new ValidationIssue(IssueSeverity.Warning, EditorValidationCodes.MissingPrefab, IssueTarget.Scatter,
                    g.Ids, g.Area, $"장식 '{id}' {g.Ids.Count}개: 정의에 프리팹이 없어 회색 상자로 보입니다. 정의 에셋에 프리팹을 지정하세요."));
            }
        }

        /// <summary>종류가 맞는 정의인데 프리팹만 비었으면 true. 정의 없음·종류 바뀜은 코어 오류가 알린다.</summary>
        private static bool IsMissingPrefab(EditorObjectCatalog catalog, string definitionId, bool scatter, out ObjectDefinition definition)
        {
            definition = null;
            if (!catalog.TryGetAsset(definitionId, out var asset) || asset.Prefab != null) return false;
            if (asset.IsScatter != scatter) return false;
            return catalog.TryGet(definitionId, out definition);
        }

        private static void AddTo(Dictionary<string, Group> groups, List<string> order, string key, int id, Bounds area)
        {
            if (!groups.TryGetValue(key, out var group))
            {
                group = new Group { Area = area };
                groups.Add(key, group);
                order.Add(key);
            }
            else
            {
                group.Area.Encapsulate(area);
            }
            group.Ids.Add(id);
        }

        private static void CheckProfiles(MapData map, Func<string, MapToolWindow.ProfileState> profileState,
            List<ValidationIssue> results)
        {
            var seen = new HashSet<int>();
            foreach (var zone in map.Zones)
            {
                if (!seen.Add(zone.Id) || zone.Boxes.Count == 0) continue; // 상자 없음·중복은 코어 오류로 충분하다
                var area = MapValidator.CellArea(map.Grid, zone.Bounds);
                switch (profileState(zone.ProfileId))
                {
                    case MapToolWindow.ProfileState.Empty:
                        results.Add(new ValidationIssue(IssueSeverity.Warning, EditorValidationCodes.ProfileEmpty, IssueTarget.Zone,
                            zone.Id, area, $"{MapValidator.Label(zone)}: 프로필이 지정되지 않아 이 존에서 소리가 바뀌지 않습니다."));
                        break;
                    case MapToolWindow.ProfileState.Missing:
                        results.Add(new ValidationIssue(IssueSeverity.Warning, EditorValidationCodes.ProfileMissing, IssueTarget.Zone,
                            zone.Id, area, $"{MapValidator.Label(zone)}: 프로필 '{zone.ProfileId}'을(를) 찾을 수 없습니다. 지워졌거나 ID가 바뀌었습니다."));
                        break;
                }
            }
        }
    }
}
