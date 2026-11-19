using System;
using System.Collections.Generic;
using UnityEngine;

namespace MapTool.Core
{
    /// <summary>
    /// 맵 데이터 전체를 검사한다. 배치 규칙은 놓는 순간만 막으므로, 저장된 뒤에 생긴 문제
    /// (정의 크기·종류 변경, 정의 삭제, 에셋 직접 편집, 병합 충돌)를 여기서 찾는다.
    /// 데이터와 카탈로그만 읽고 아무것도 바꾸지 않는다. 프리팹·사운드 프로필처럼 코어가 모르는 검사는 에디터가 덧붙인다.
    /// </summary>
    public static class MapValidator
    {
        /// <summary>검사해서 정렬한 목록을 돌려준다.</summary>
        public static List<ValidationIssue> Validate(MapData map, IObjectCatalog catalog)
        {
            var results = new List<ValidationIssue>();
            Validate(map, catalog, results);
            Sort(results);
            return results;
        }

        /// <summary>results 뒤에 덧붙인다(정렬하지 않음).</summary>
        public static void Validate(MapData map, IObjectCatalog catalog, List<ValidationIssue> results)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (results == null) throw new ArgumentNullException(nameof(results));

            CheckObjects(map, catalog, results);
            CheckZones(map, results);
            CheckScatters(map, catalog, results);
            CheckSpawns(map, catalog, results);
        }

        /// <summary>오류 먼저, 그다음 오브젝트·존·장식·스폰 순, 같은 종류는 ID 순.</summary>
        public static void Sort(List<ValidationIssue> issues)
        {
            issues.Sort((a, b) =>
            {
                int c = b.Severity.CompareTo(a.Severity);
                if (c != 0) return c;
                c = a.Target.CompareTo(b.Target);
                if (c != 0) return c;
                c = a.TargetId.CompareTo(b.TargetId);
                return c != 0 ? c : string.CompareOrdinal(a.Code, b.Code);
            });
        }

        /// <summary>셀 영역을 맵 원점 기준 미터 영역으로.</summary>
        public static Bounds CellArea(GridSpace grid, BoundsInt cells)
        {
            var min = Vector3.Scale(cells.position, grid.CellSize);
            var size = Vector3.Scale(cells.size, grid.CellSize);
            return new Bounds(min + size * 0.5f, size);
        }

        public static string Label(PlacedObject placed) => $"'{placed.DefinitionId}' #{placed.InstanceId}";

        public static string Label(PlacedZone zone) =>
            string.IsNullOrEmpty(zone.Name) ? $"존 #{zone.Id}" : $"존 '{zone.Name}' #{zone.Id}";

        public static string Label(PlacedSpawn spawn) =>
            string.IsNullOrEmpty(spawn.Name) ? $"스폰 지점 @{spawn.Id}" : $"스폰 지점 '{spawn.Name}' @{spawn.Id}";

        // ── 칸 오브젝트 ────────────────────────────────────────────

        private static void CheckObjects(MapData map, IObjectCatalog catalog, List<ValidationIssue> results)
        {
            var objects = map.Objects;
            var seen = new HashSet<int>();
            var owner = new Dictionary<Vector3Int, int>(); // 셀 → 먼저 차지한 objects 인덱스
            var overlaps = new Dictionary<(int first, int second), (int cells, Vector3Int min, Vector3Int max)>();
            var order = new List<(int first, int second)>();

            for (int i = 0; i < objects.Count; i++)
            {
                var placed = objects[i];
                var unit = CellArea(map.Grid, new BoundsInt(placed.Cell, Vector3Int.one));

                if (!seen.Add(placed.InstanceId))
                {
                    results.Add(new ValidationIssue(IssueSeverity.Error, ValidationCodes.DuplicateId, IssueTarget.Object,
                        placed.InstanceId, unit,
                        $"{Label(placed)}: 같은 ID가 맵에 또 있습니다. 맵 에셋이 직접 편집되었거나 병합이 꼬였을 수 있습니다."));
                    continue; // 같은 ID끼리는 구별할 수 없으니 나머지 검사는 첫 번째 것만 한다
                }

                if (!catalog.TryGet(placed.DefinitionId, out var definition))
                {
                    results.Add(new ValidationIssue(IssueSeverity.Error, ValidationCodes.UnknownDefinition, IssueTarget.Object,
                        placed.InstanceId, unit,
                        $"{Label(placed)}: 정의를 찾을 수 없습니다. 정의 에셋이 지워졌거나 ID가 바뀌었습니다."));
                    continue;
                }

                if (definition.Kind != PlacementKind.Grid)
                {
                    results.Add(new ValidationIssue(IssueSeverity.Error, ValidationCodes.WrongKind, IssueTarget.Object,
                        placed.InstanceId, unit,
                        $"{Label(placed)}: 정의가 장식으로 바뀌어 칸에 둘 수 없습니다. 지우고 브러시로 다시 뿌려 주세요."));
                    continue;
                }

                var bounds = placed.GetBounds(definition);
                if (!MapBoundsRule.IsInside(bounds, map.Bounds))
                {
                    results.Add(new ValidationIssue(IssueSeverity.Error, ValidationCodes.OutOfBounds, IssueTarget.Object,
                        placed.InstanceId, CellArea(map.Grid, bounds),
                        $"{Label(placed)}: 영역 {bounds.position}~{bounds.max}가 맵 범위 {map.Size}를 벗어납니다."));
                }

                foreach (var cell in bounds.allPositionsWithin)
                {
                    if (!owner.TryGetValue(cell, out int first))
                    {
                        owner.Add(cell, i);
                        continue;
                    }

                    var key = (first, i);
                    if (overlaps.TryGetValue(key, out var o))
                        overlaps[key] = (o.cells + 1, Vector3Int.Min(o.min, cell), Vector3Int.Max(o.max, cell));
                    else
                    {
                        overlaps.Add(key, (1, cell, cell));
                        order.Add(key);
                    }
                }
            }

            // 나중에 놓인 쪽을 대상으로 한다(지우기 쉬운 쪽). 상대는 OtherId.
            foreach (var key in order)
            {
                var o = overlaps[key];
                var a = objects[key.first];
                var b = objects[key.second];
                var cells = new BoundsInt(o.min, o.max - o.min + Vector3Int.one);
                results.Add(new ValidationIssue(IssueSeverity.Error, ValidationCodes.Overlap, IssueTarget.Object,
                    b.InstanceId, CellArea(map.Grid, cells),
                    $"{Label(b)}이(가) {Label(a)}와 {o.cells}칸 겹칩니다.", a.InstanceId));
            }
        }

        // ── 존 ───────────────────────────────────────────────────

        private static void CheckZones(MapData map, List<ValidationIssue> results)
        {
            var seen = new HashSet<int>();
            foreach (var zone in map.Zones)
            {
                var area = CellArea(map.Grid, zone.Bounds);

                if (!seen.Add(zone.Id))
                {
                    results.Add(new ValidationIssue(IssueSeverity.Error, ValidationCodes.DuplicateId, IssueTarget.Zone,
                        zone.Id, area, $"{Label(zone)}: 같은 ID의 존이 맵에 또 있습니다."));
                    continue;
                }

                var boxes = zone.Boxes;
                if (boxes.Count == 0)
                {
                    results.Add(new ValidationIssue(IssueSeverity.Error, ValidationCodes.InvalidZone, IssueTarget.Zone,
                        zone.Id, area, $"{Label(zone)}: 상자가 하나도 없어 아무 곳에도 적용되지 않습니다."));
                    continue;
                }

                var empty = new List<int>();
                var outside = new List<int>();
                for (int i = 0; i < boxes.Count; i++)
                {
                    if (boxes[i].IsEmpty) empty.Add(i + 1);
                    else if (!MapBoundsRule.IsInside(boxes[i].Bounds, map.Bounds)) outside.Add(i + 1);
                }

                if (empty.Count > 0)
                {
                    results.Add(new ValidationIssue(IssueSeverity.Error, ValidationCodes.InvalidZone, IssueTarget.Zone,
                        zone.Id, area, $"{Label(zone)}: 상자 {string.Join(", ", empty)}번의 크기가 0입니다."));
                }

                if (outside.Count > 0)
                {
                    results.Add(new ValidationIssue(IssueSeverity.Error, ValidationCodes.OutOfBounds, IssueTarget.Zone,
                        zone.Id, area, $"{Label(zone)}: 상자 {string.Join(", ", outside)}번이 맵 범위 {map.Size}를 벗어납니다."));
                }
            }
        }

        // ── 장식 ─────────────────────────────────────────────────

        // 장식은 수천 개라 같은 원인·같은 정의를 한 줄로 묶는다(정의 하나가 지워지면 수백 줄이 되지 않게).
        private sealed class ScatterGroup
        {
            public readonly List<int> Ids = new();
            public Bounds Area;
        }

        private static void CheckScatters(MapData map, IObjectCatalog catalog, List<ValidationIssue> results)
        {
            var groups = new Dictionary<(string code, string definitionId), ScatterGroup>();
            var order = new List<(string code, string definitionId)>();
            var seen = new HashSet<int>();
            var unitSize = map.Grid.CellSize;

            void Add(string code, PlacedScatter s)
            {
                var key = (code, s.DefinitionId ?? string.Empty);
                // 값이 깨진 장식은 위치를 믿을 수 없으니 원점으로 둔다.
                var center = PlacementService.IsValidScatterValue(s) ? s.Position : Vector3.zero;
                var unit = new Bounds(center, unitSize);
                if (!groups.TryGetValue(key, out var group))
                {
                    group = new ScatterGroup { Area = unit };
                    groups.Add(key, group);
                    order.Add(key);
                }
                else
                {
                    group.Area.Encapsulate(unit);
                }
                group.Ids.Add(s.Id);
            }

            foreach (var s in map.Scatters)
            {
                if (!seen.Add(s.Id)) { Add(ValidationCodes.DuplicateId, s); continue; }
                if (!catalog.TryGet(s.DefinitionId, out var definition)) { Add(ValidationCodes.UnknownDefinition, s); continue; }
                if (definition.Kind != PlacementKind.Scatter) { Add(ValidationCodes.WrongKind, s); continue; }
                if (!PlacementService.IsValidScatterValue(s)) { Add(ValidationCodes.InvalidScatter, s); continue; }
                if (!PlacementService.IsScatterInside(map, s.Position)) Add(ValidationCodes.OutOfBounds, s);
            }

            foreach (var key in order)
            {
                var group = groups[key];
                results.Add(new ValidationIssue(IssueSeverity.Error, key.code, IssueTarget.Scatter, group.Ids, group.Area,
                    ScatterMessage(key.code, key.definitionId, group.Ids.Count)));
            }
        }

        // ── 스폰 지점 ─────────────────────────────────────────────

        private static void CheckSpawns(MapData map, IObjectCatalog catalog, List<ValidationIssue> results)
        {
            var spawns = map.Spawns;
            if (spawns.Count == 0)
            {
                results.Add(new ValidationIssue(IssueSeverity.Warning, ValidationCodes.NoSpawn, IssueTarget.Spawn,
                    Array.Empty<int>(), CellArea(map.Grid, map.Bounds),
                    "스폰 지점이 없습니다. 플레이하면 플레이어가 씬에 놓인 자리에서 시작합니다."));
                return;
            }

            var seen = new HashSet<int>();
            foreach (var spawn in spawns)
            {
                var cell = new BoundsInt(spawn.Cell, Vector3Int.one);
                var area = CellArea(map.Grid, cell);

                if (!seen.Add(spawn.Id))
                {
                    results.Add(new ValidationIssue(IssueSeverity.Error, ValidationCodes.DuplicateId, IssueTarget.Spawn,
                        spawn.Id, area, $"{Label(spawn)}: 같은 ID의 스폰 지점이 맵에 또 있습니다."));
                    continue;
                }

                if (!map.Bounds.Contains(spawn.Cell))
                {
                    results.Add(new ValidationIssue(IssueSeverity.Error, ValidationCodes.OutOfBounds, IssueTarget.Spawn,
                        spawn.Id, area, $"{Label(spawn)}: 칸 {spawn.Cell}이 맵 범위 {map.Size}를 벗어납니다."));
                    continue;
                }

                if (TryFindSpawnBlocker(map, catalog, spawn.Cell, out var blocker))
                    results.Add(new ValidationIssue(IssueSeverity.Warning, ValidationCodes.SpawnBlocked, IssueTarget.Spawn,
                        spawn.Id, area, $"{Label(spawn)}: {Label(blocker)}이(가) 있는 칸이라 플레이어가 끼일 수 있습니다.",
                        blocker.InstanceId));
            }
        }

        /// <summary>
        /// 이 칸에 스폰하면 플레이어가 끼일 칸 오브젝트를 찾는다.
        /// 장식과 바닥 오브젝트(<see cref="ObjectDefinition.IsFloor"/>, 예: 길)는 위에 설 수 있으므로 막지 않는다.
        /// </summary>
        public static bool TryFindSpawnBlocker(MapData map, IObjectCatalog catalog, Vector3Int cell, out PlacedObject blocker)
        {
            foreach (var placed in map.Objects)
            {
                if (!catalog.TryGet(placed.DefinitionId, out var definition)) continue;
                if (definition.Kind != PlacementKind.Grid || definition.IsFloor) continue;
                if (!placed.GetBounds(definition).Contains(cell)) continue;
                blocker = placed;
                return true;
            }
            blocker = default;
            return false;
        }

        private static string ScatterMessage(string code, string definitionId, int count)
        {
            string who = $"장식 '{definitionId}' {count}개";
            return code switch
            {
                ValidationCodes.DuplicateId => $"{who}: 같은 ID가 맵에 또 있습니다. 맵 에셋이 직접 편집되었거나 병합이 꼬였을 수 있습니다.",
                ValidationCodes.UnknownDefinition => $"{who}: 정의를 찾을 수 없습니다. 정의 에셋이 지워졌거나 ID가 바뀌었습니다.",
                ValidationCodes.WrongKind => $"{who}: 정의가 칸 오브젝트로 바뀌어 장식으로 둘 수 없습니다.",
                ValidationCodes.InvalidScatter => $"{who}: 크기·위치·회전 값이 깨져 있습니다.",
                ValidationCodes.OutOfBounds => $"{who}: 맵 범위 밖에 있습니다.",
                _ => who,
            };
        }
    }
}
