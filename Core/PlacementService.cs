using System;
using System.Collections.Generic;
using UnityEngine;

namespace MapTool.Core
{
    /// <summary>
    /// 맵 편집의 단일 진입점.
    ///   입력 → 커맨드 → Execute → MapData·점유 맵 변경 → 이벤트 → 뷰 갱신
    /// Undo/Redo도 같은 경로로 이벤트를 발행하므로 뷰는 이벤트만 보고 씬을 맞추면 된다.
    /// 에디터의 Ctrl+Z는 유니티 Undo가 아니라 여기의 Undo/Redo에 연결한다.
    /// </summary>
    public sealed class PlacementService
    {
        private readonly IObjectCatalog catalog;
        private readonly List<IPlacementRule> rules;
        private readonly OccupancyMap occupancy = new();
        private readonly List<MapCommand> undoStack = new();
        private readonly List<MapCommand> redoStack = new();
        private readonly ScatterSpatialIndex scatterIndex;

        // BeginGroup ~ EndGroup 사이에 실행된 커맨드. null이면 묶는 중이 아니다.
        private List<MapCommand> openGroup;

        /// <param name="rules">null이면 기본 규칙(맵 범위, 겹침)을 쓴다.</param>
        public PlacementService(MapData map, IObjectCatalog catalog, IEnumerable<IPlacementRule> rules = null)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.rules = rules != null ? new List<IPlacementRule>(rules) : CreateDefaultRules();
            var cell = map.Grid.CellSize;
            scatterIndex = new ScatterSpatialIndex(Mathf.Max(Mathf.Max(cell.x, cell.z), 0.5f));
            RebuildOccupancy();
            RebuildScatterIndex();
        }

        public MapData Map { get; }
        public OccupancyMap Occupancy => occupancy;

        /// <summary>장식 위치 검색(브러시 최소 간격, 지우개). 좌표는 맵 로컬.</summary>
        public ScatterSpatialIndex ScatterIndex => scatterIndex;

        public bool CanUndo => undoStack.Count > 0 || openGroup is { Count: > 0 };
        public bool CanRedo => redoStack.Count > 0;

        /// <summary>BeginGroup 후 EndGroup 전이면 true.</summary>
        public bool IsGrouping => openGroup != null;

        public event Action<PlacedObject> OnPlaced;
        public event Action<PlacedObject> OnRemoved;

        /// <summary>(before, after). 회전만 바뀐 경우도 포함한다.</summary>
        public event Action<PlacedObject, PlacedObject> OnMoved;

        public event Action<PlacedZone> OnZoneAdded;
        public event Action<PlacedZone> OnZoneRemoved;

        /// <summary>(before, after). 상자·프로필·우선순위·이름 변경 모두 포함한다.</summary>
        public event Action<PlacedZone, PlacedZone> OnZoneChanged;

        public event Action<PlacedSpawn> OnSpawnAdded;
        public event Action<PlacedSpawn> OnSpawnRemoved;

        /// <summary>(before, after). 이동·회전·이름 변경 모두 포함한다.</summary>
        public event Action<PlacedSpawn, PlacedSpawn> OnSpawnChanged;

        /// <summary>(이전 기본 Id, 새 기본 Id). 0은 지정 없음.</summary>
        public event Action<int, int> OnDefaultSpawnChanged;

        /// <summary>장식이 한 묶음 들어왔다(브러시 한 번, 또는 지우개의 Undo). 뷰는 묶음 단위로 만들면 된다.</summary>
        public event Action<IReadOnlyList<PlacedScatter>> OnScattersAdded;

        /// <summary>장식이 한 묶음 빠졌다(지우개 한 번, 또는 브러시의 Undo).</summary>
        public event Action<IReadOnlyList<PlacedScatter>> OnScattersRemoved;

        public static List<IPlacementRule> CreateDefaultRules() => new() { new MapBoundsRule(), new OverlapRule() };

        /// <summary>정의를 모르는 오브젝트는 영역을 계산할 수 없으므로 false.</summary>
        public bool TryGetBounds(PlacedObject placed, out BoundsInt bounds)
        {
            if (catalog.TryGet(placed.DefinitionId, out var definition))
            {
                bounds = placed.GetBounds(definition);
                return true;
            }
            bounds = default;
            return false;
        }

        /// <summary>후보를 놓을 수 있는지 검사한다. 맵은 바꾸지 않는다. 기존 오브젝트의 이동 후보면 자기 자신과의 겹침은 무시한다.</summary>
        public PlacementResult CanPlace(PlacedObject candidate)
        {
            if (!catalog.TryGet(candidate.DefinitionId, out var definition))
                return PlacementResult.Fail(PlacementCodes.UnknownDefinition, $"정의 '{candidate.DefinitionId}'를 찾을 수 없습니다.");
            if (definition.Kind != PlacementKind.Grid)
                return PlacementResult.Fail(PlacementCodes.WrongKind, $"'{definition.Id}'는 장식이라 칸에 놓을 수 없습니다. 브러시로 뿌려 주세요.");

            var context = new PlacementContext(Map, occupancy, candidate, definition);
            foreach (var rule in rules)
            {
                var result = rule.Check(context);
                if (!result.IsValid) return result;
            }
            return PlacementResult.Ok;
        }

        /// <summary>
        /// 존 후보를 검사한다. 맵은 바꾸지 않는다.
        /// 상자가 하나 이상이고, 모든 상자의 크기가 1 이상이며 맵 범위 안이어야 한다.
        /// 오브젝트·다른 존과의 겹침은 허용한다. 프로필이 비어 있어도 놓을 수 있다(검증 단계에서 경고).
        /// </summary>
        public PlacementResult CanPlaceZone(PlacedZone candidate)
        {
            var boxes = candidate.Boxes;
            if (boxes.Count == 0)
                return PlacementResult.Fail(PlacementCodes.InvalidZone, "존에 상자가 하나도 없습니다.");

            var map = Map.Bounds;
            for (int i = 0; i < boxes.Count; i++)
            {
                var box = boxes[i];
                if (box.IsEmpty)
                    return PlacementResult.Fail(PlacementCodes.InvalidZone, $"상자 {i}의 크기 {box.Size}가 0 이하입니다.");

                var b = box.Bounds;
                if (!MapBoundsRule.IsInside(b, map))
                    return PlacementResult.Fail(PlacementCodes.OutOfBounds,
                        $"존 상자 {i}의 영역 {b.position}~{b.max}가 맵 범위 {map.size}를 벗어납니다.");
            }
            return PlacementResult.Ok;
        }

        /// <summary>
        /// 스폰 지점 후보를 검사한다. 맵은 바꾸지 않는다. 칸이 맵 범위 안이어야 한다.
        /// 오브젝트가 있는 칸도 놓을 수는 있다(나중에 오브젝트를 치울 수 있으니). 검사에서 경고한다.
        /// </summary>
        public PlacementResult CanPlaceSpawn(PlacedSpawn candidate)
        {
            if (!Map.Bounds.Contains(candidate.Cell))
                return PlacementResult.Fail(PlacementCodes.OutOfBounds, $"스폰 칸 {candidate.Cell}이 맵 범위 {Map.Size}를 벗어납니다.");
            return PlacementResult.Ok;
        }

        /// <summary>
        /// 장식 후보를 검사한다. 맵은 바꾸지 않는다.
        /// 장식 정의여야 하고, 스케일이 0보다 크며, XZ 위치가 맵 범위 안이어야 한다(높이는 보지 않는다).
        /// 다른 장식·칸 오브젝트와의 겹침은 허용한다(간격은 브러시가 조절).
        /// </summary>
        public PlacementResult CanPlaceScatter(PlacedScatter candidate)
        {
            if (!catalog.TryGet(candidate.DefinitionId, out var definition))
                return PlacementResult.Fail(PlacementCodes.UnknownDefinition, $"정의 '{candidate.DefinitionId}'를 찾을 수 없습니다.");
            if (definition.Kind != PlacementKind.Scatter)
                return PlacementResult.Fail(PlacementCodes.WrongKind, $"'{definition.Id}'는 칸 오브젝트라 브러시로 뿌릴 수 없습니다.");

            if (!IsValidScatterValue(candidate))
                return PlacementResult.Fail(PlacementCodes.InvalidScatter, $"장식 값이 올바르지 않습니다: {candidate}");

            if (!IsScatterInside(Map, candidate.Position))
                return PlacementResult.Fail(PlacementCodes.OutOfBounds, $"장식 위치 {candidate.Position}가 맵 범위 밖입니다.");

            return PlacementResult.Ok;
        }

        /// <summary>장식 정의인지. 브러시 팔레트가 뿌릴 수 있는 항목을 거를 때 쓴다.</summary>
        public bool IsScatterDefinition(string definitionId) =>
            catalog.TryGet(definitionId, out var definition) && definition.Kind == PlacementKind.Scatter;

        /// <summary>커맨드를 실행한다. 성공하면 Undo 스택에 쌓고 Redo 스택을 비운다. 실패하면 맵은 그대로다.</summary>
        public PlacementResult Execute(MapCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));

            var result = command.Do(this);
            if (result.IsValid)
            {
                if (openGroup != null) openGroup.Add(command);
                else undoStack.Add(command);
                redoStack.Clear();
            }
            return result;
        }

        /// <summary>
        /// 이후 EndGroup까지 실행한 커맨드를 Undo 한 번으로 묶는다(브러시 한 번 드래그).
        /// 이미 묶는 중이면 앞 묶음을 먼저 닫는다.
        /// </summary>
        public void BeginGroup()
        {
            if (openGroup != null) EndGroup();
            openGroup = new List<MapCommand>();
        }

        /// <summary>묶음을 닫아 Undo 스택에 한 칸으로 올린다. 실행된 커맨드가 없으면 아무것도 쌓지 않는다.</summary>
        public void EndGroup()
        {
            if (openGroup == null) return;
            var commands = openGroup;
            openGroup = null;
            if (commands.Count == 0) return;
            undoStack.Add(commands.Count == 1 ? commands[0] : new CommandGroup(commands));
        }

        public bool Undo()
        {
            EndGroup(); // 드래그 도중 Ctrl+Z면 지금까지 그린 것을 한 묶음으로 닫고 그걸 되돌린다
            if (!CanUndo) return false;

            var command = Pop(undoStack);
            command.Undo(this);
            redoStack.Add(command);
            return true;
        }

        public bool Redo()
        {
            EndGroup();
            if (!CanRedo) return false;

            var command = Pop(redoStack);
            if (!command.Do(this).IsValid)
            {
                // Undo로 상태를 되돌렸으니 정상이면 실패하지 않는다. 실패하면 이후 Redo도 믿을 수 없으므로 버린다.
                redoStack.Clear();
                return false;
            }
            undoStack.Add(command);
            return true;
        }

        /// <summary>맵을 새로 불러왔을 때 등 이전 기록이 의미 없어졌을 때 호출한다.</summary>
        public void ClearHistory()
        {
            openGroup = null;
            undoStack.Clear();
            redoStack.Clear();
        }

        internal void AddObject(PlacedObject placed)
        {
            Map.Add(placed);
            if (TryGetBounds(placed, out var bounds)) occupancy.Fill(bounds, placed.InstanceId);
            OnPlaced?.Invoke(placed);
        }

        internal void RemoveObject(PlacedObject placed)
        {
            Map.Remove(placed.InstanceId);
            if (TryGetBounds(placed, out var bounds)) occupancy.Clear(bounds, placed.InstanceId);
            OnRemoved?.Invoke(placed);
        }

        internal void ReplaceObject(PlacedObject before, PlacedObject after)
        {
            if (TryGetBounds(before, out var oldBounds)) occupancy.Clear(oldBounds, before.InstanceId);
            Map.Replace(after);
            if (TryGetBounds(after, out var newBounds)) occupancy.Fill(newBounds, after.InstanceId);
            OnMoved?.Invoke(before, after);
        }

        internal void AddZone(PlacedZone zone)
        {
            Map.AddZone(zone);
            OnZoneAdded?.Invoke(zone);
        }

        internal void RemoveZone(PlacedZone zone)
        {
            Map.RemoveZone(zone.Id);
            OnZoneRemoved?.Invoke(zone);
        }

        internal void ReplaceZone(PlacedZone before, PlacedZone after)
        {
            Map.ReplaceZone(after);
            OnZoneChanged?.Invoke(before, after);
        }

        internal void AddSpawn(PlacedSpawn spawn, int index = -1)
        {
            Map.AddSpawn(spawn, index);
            OnSpawnAdded?.Invoke(spawn);
        }

        internal void RemoveSpawn(PlacedSpawn spawn)
        {
            Map.RemoveSpawn(spawn.Id);
            OnSpawnRemoved?.Invoke(spawn);
        }

        internal void ReplaceSpawn(PlacedSpawn before, PlacedSpawn after)
        {
            Map.ReplaceSpawn(after);
            OnSpawnChanged?.Invoke(before, after);
        }

        internal void SetDefaultSpawn(int spawnId)
        {
            int before = Map.DefaultSpawnId;
            if (before == spawnId) return;
            Map.SetDefaultSpawnId(spawnId);
            OnDefaultSpawnChanged?.Invoke(before, spawnId);
        }

        internal void AddScatters(IReadOnlyList<PlacedScatter> added)
        {
            foreach (var s in added)
            {
                Map.AddScatter(s);
                scatterIndex.Add(s);
            }
            OnScattersAdded?.Invoke(added);
        }

        internal void RemoveScatters(IReadOnlyList<PlacedScatter> removed)
        {
            var ids = new HashSet<int>();
            foreach (var s in removed) ids.Add(s.Id);
            Map.RemoveScatters(ids);
            foreach (var s in removed) scatterIndex.Remove(s);
            OnScattersRemoved?.Invoke(removed);
        }

        /// <summary>
        /// 맵 내용 전체(크기·그리드 포함)를 source로 바꾼다. ReplaceMapCommand가 쓴다.
        /// 모두 지운 뒤 다시 넣으며 평소의 추가·제거 이벤트를 그대로 보내므로 뷰는 따로 할 일이 없다.
        /// </summary>
        internal void ReplaceAll(MapSnapshot source)
        {
            SetDefaultSpawn(0);
            for (int i = Map.Spawns.Count - 1; i >= 0; i--) RemoveSpawn(Map.Spawns[i]);
            for (int i = Map.Zones.Count - 1; i >= 0; i--) RemoveZone(Map.Zones[i]);
            if (Map.ScatterCount > 0) RemoveScatters(new List<PlacedScatter>(Map.Scatters));
            for (int i = Map.Objects.Count - 1; i >= 0; i--) RemoveObject(Map.Objects[i]);

            Map.Reshape(source.Size, source.Grid);

            foreach (var o in source.Objects) AddObject(o);
            foreach (var z in source.Zones) AddZone(z);
            if (source.Scatters.Length > 0) AddScatters(source.Scatters);
            foreach (var s in source.Spawns) AddSpawn(s);
            SetDefaultSpawn(source.DefaultSpawnId);
        }

        private void RebuildScatterIndex()
        {
            scatterIndex.Clear();
            foreach (var s in Map.Scatters) scatterIndex.Add(s);
        }

        /// <summary>스케일이 0보다 크고 위치·회전이 유한한 숫자이며 회전이 0에 가깝지 않은지. 배치와 검증이 같이 쓴다.</summary>
        public static bool IsValidScatterValue(PlacedScatter scatter)
        {
            var p = scatter.Position;
            var q = scatter.Rotation;
            return scatter.Scale > 0f && !float.IsInfinity(scatter.Scale) &&
                   IsFinite(p.x) && IsFinite(p.y) && IsFinite(p.z) &&
                   IsFinite(q.x) && IsFinite(q.y) && IsFinite(q.z) && IsFinite(q.w) &&
                   q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w >= 0.5f;
        }

        /// <summary>장식 위치(맵 원점 기준 미터)의 XZ가 맵 범위 안인지. 높이는 보지 않는다.</summary>
        public static bool IsScatterInside(MapData map, Vector3 position)
        {
            var extent = Vector3.Scale(map.Size, map.Grid.CellSize);
            return position.x >= 0f && position.z >= 0f && position.x < extent.x && position.z < extent.z;
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private void RebuildOccupancy()
        {
            occupancy.ClearAll();
            foreach (var placed in Map.Objects)
            {
                if (TryGetBounds(placed, out var bounds)) occupancy.Fill(bounds, placed.InstanceId);
            }
        }

        private static MapCommand Pop(List<MapCommand> stack)
        {
            var command = stack[^1];
            stack.RemoveAt(stack.Count - 1);
            return command;
        }
    }
}
